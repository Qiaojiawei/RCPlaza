using RCPlaza.Core;
using UnityEngine;

namespace RCPlaza.Sim
{
    /// <summary>
    /// 无刷电机三段扭矩曲线 + 锂电池电压/容量模拟(设计文档 §3.4 / §3.5)。
    ///
    /// 扭矩曲线(以转速/空载转速 x 为横轴):
    ///   [0, 0.25): 0.30 → 0.92 线性上升(文档曲线为从 0 上升;纯 0 起点会令车辆
    ///              永无法起步(起步扭矩=0 是不动点),故保留 0.30 堵转扭矩兜底——对文档的唯一自认偏差)
    ///   [0.25,0.70]: 0.92 峰值平台(文档:峰值的 85–95%)
    ///   [0.70,0.95]: 0.92 → 0.375 线性衰减(文档:衰减至峰值的 35–40%)
    ///   [0.95,1.00]: 0.375 → 0 收尾(文档未描述,但扭矩若不归零车辆将无真实极速;
    ///               此尾段使极速由 风阻 × 扭矩衰减 自然平衡产生)
    ///
    /// 电池:电压随 SOC 与放电电流变化 V = V_ocv(SOC) − I·R_pack(电压 sag);
    /// 电机可用扭矩 ∝ V/V_nom,空载转速 = Kv·V;低压保护 LVC:<3.4V/芯 限功 50%,
    /// <3.2V/芯 切断动力(文档 §3.5:电调低压保护)。
    /// </summary>
    [System.Serializable]
    public class MotorAndBattery
    {
        public float soc { get; private set; }           // 剩余电量 0..1
        public float packVoltage { get; private set; }   // 当前端电压(含 sag)
        public float ocv { get; private set; }           // 开路电压
        public float currentA { get; private set; }      // 放电电流
        public float powerLimit { get; private set; }    // LVC 一级:限功系数(1 或 0.5)
        public bool  lvcCut { get; private set; }        // LVC 二级:动力切断
        public float lastTorque { get; private set; }    // 最后一次计算的电机扭矩
        public float noLoadRpm { get; private set; }     // 当前电压下的空载转速

        readonly CarSpec spec;

        public MotorAndBattery(CarSpec spec)
        {
            this.spec = spec;
            soc = 1f;
            ocv = spec.FullVoltage;
            packVoltage = ocv;
            powerLimit = 1f;
        }

        /// <summary>开路电压(每芯分段线性:满电 4.2V → 50% 约 3.75V → 接近耗尽 3.3V → 3.1V)。</summary>
        public float OcvFromSoc(float s)
        {
            float v;
            if (s >= 0.5f)      v = Mathf.Lerp(3.75f, 4.2f, (s - 0.5f) / 0.5f);
            else if (s >= 0.05f) v = Mathf.Lerp(3.30f, 3.75f, (s - 0.05f) / 0.45f);
            else                v = Mathf.Lerp(3.10f, 3.30f, s / 0.05f);
            return v * spec.cells;
        }

        /// <summary>三段式 + 尾段扭矩因子曲线,输入转速占比 x∈[0,1]。</summary>
        public float TorqueFactor(float x)
        {
            x = Mathf.Clamp01(x);
            if (x < 0.25f) return Mathf.Lerp(0.30f, 0.92f, x / 0.25f);
            if (x < 0.70f) return 0.92f;
            if (x < 0.95f) return Mathf.Lerp(0.92f, 0.375f, (x - 0.70f) / 0.25f);
            return Mathf.Lerp(0.375f, 0f, (x - 0.95f) / 0.05f);
        }

        /// <summary>
        /// 每物理步更新。throttle ∈[0,1] 为驱动开度(不含转向/制动逻辑);
        /// motorRpm 为电机转速(驱动轮平均 ω × 传动比,取绝对值)。
        /// 返回本步可用电机扭矩(N·m,恒 ≥0,方向由调用方决定)。
        /// </summary>
        public float Update(float throttle, float motorRpm, float dt)
        {
            float t = Mathf.Clamp01(throttle);
            ocv = OcvFromSoc(soc);

            // 电压 sag:V = V_ocv − I·R(I 用上一步电流,100Hz 下滞后一步可忽略)
            packVoltage = Mathf.Max(ocv - currentA * spec.packResistanceOhm,
                                    spec.NominalVoltage * 0.25f);

            // LVC 保护(文档 §3.5:3.4V/芯 限功 50%;3.2V/芯 切断)
            float cellV = packVoltage / spec.cells;
            powerLimit = cellV < 3.4f ? 0.5f : 1f;
            lvcCut     = cellV < 3.2f;
            t *= powerLimit;

            // 当前电压下的空载转速与转速占比
            noLoadRpm = Mathf.Max(spec.NoLoadRpm(packVoltage), 1f);
            float x = Mathf.Abs(motorRpm) / noLoadRpm;

            // 扭矩:曲线 × 电压比例(电压下降 → 整体动力衰减,文档 §3.5)
            float torque = spec.torquePeakNm * TorqueFactor(x)
                         * (packVoltage / spec.NominalVoltage) * t;

            // 电流限制(电调上限 & 电池 C 倍率上限)
            float maxI = Mathf.Min(spec.escCurrentLimitA, spec.cRating * spec.capacityAh);
            float torqueLimitI = maxI * spec.MotorKT;
            if (torque > torqueLimitI) torque = torqueLimitI;

            currentA = t > 0.0001f ? torque / spec.MotorKT : 0f;

            // 库仑计:SOC 耗减(DischargeTimeScale 加速游戏内的电量衰减过程,文档 §7④ 验证)
            soc -= currentA * dt * PlazaConfig.DischargeTimeScale / (spec.capacityAh * 3600f);
            if (soc < 0f) soc = 0f;

            if (lvcCut) { torque = 0f; }

            lastTorque = torque;
            return torque;
        }

        /// <summary>整组电压,供 HUD 显示。</summary>
        public float CellVoltage => packVoltage / spec.cells;
    }
}