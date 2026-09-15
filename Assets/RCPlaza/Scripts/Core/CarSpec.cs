using UnityEngine;

namespace RCPlaza.Core
{
    /// <summary>
    /// 车型全套参数(设计文档 §2「车型选择与参数体系」/ §3「物理仿真模型」)。
    /// 全部为真实比例的公制单位:米、千克、N·m、伏特、安时。
    /// 采用"风格化但可辨识"的命名(文档 §8 诚实边界:避免直接使用品牌外观)。
    /// </summary>
    [System.Serializable]
    public class CarSpec
    {
        /// <summary>显示名(HUD 用)。</summary>
        public string displayName = "短卡 SCT(Slash 型参数)";

        // ---------- 尺寸(文档 §2.1 / §2.2 参数表) ----------
        public float length        = 0.568f;   // 全长(Slash 568mm)
        public float width         = 0.296f;   // 全宽 = 轮距
        public float height        = 0.193f;   // 全高
        public float wheelbase     = 0.324f;   // 轴距
        public float trackFront    = 0.296f;   // 前轮距
        public float trackRear     = 0.296f;   // 后轮距
        public float wheelRadius   = 0.0525f;  // 轮胎半径(Slash 2.2 英寸轮,直径约 105mm)
        public float tireWidth     = 0.048f;   // 胎面宽(视觉)
        /// <summary>底盘最低点离地(静止、悬挂中性时)——决定能否爬上 4.5cm 路沿的关键。</summary>
        public float chassisClearance = 0.032f;
        /// <summary>重心高度(文档 §3.2:Slash 55–65mm 取 0.06)。车根原点即重心位置。</summary>
        public float cgHeight      = 0.06f;

        // ---------- 质量(文档 §3.2) ----------
        public float mass          = 2.64f;    // 整车质量 RTR(含电池)
        public float unsprungMassPerWheel = 0.12f; // 簧下质量/轮(用于轮惯量估算)

        // ---------- 悬架(文档 §3.3) ----------
        // 注意:文档给出 k=28000–32000 N/m、c=2200–2600 N·s/m。该值下 2.64kg 车身静止压缩仅 0.21mm,
        // 悬挂视觉冻结、25km/h 定圆侧倾仅 0.04°,与文档自身的"TargetPosition 0.5""侧倾 3–5°"验收目标
        // (文档 §7③)矛盾。此处按"静载压缩 ≈ 50% 行程"反推修正:k = mg/4 ÷ (0.5×行程)。
        // Slash: 6.48N ÷ 0.0175m ≈ 370 N/m;Kraton: 11.9N ÷ 0.0225m ≈ 530 N/m。
        // 阻尼取 ζ≈0.7: c = 2ζ√(k·m/4)。
        public float suspensionTravel = 0.035f; // 行程 35mm(文档 §2.1:避震长 48mm,行程约 35mm)
        public float springStiffness  = 370f;   // N/m per wheel(修正值,推导见上)
        public float damperRate       = 25f;    // N·s/m per wheel(ζ≈0.72)
        public float antiRollBar      = 30f;    // N/m 每轴(压缩差→两侧等大反向竖直力)

        // ---------- 动力传动(文档 §2.1 / §3.4) ----------
        public float motorKv        = 3500f;    // Velineon 3500:3500 RPM/V
        public float gearRatio      = 7.32f;    // 总传动比 7.32:1(内齿比 5.04:1)
        public float drivetrainEff  = 0.88f;    // 齿轮传动效率 85–92% 取 0.88
        /// <summary>电机峰值扭矩(标称电压下)。推导:使起步轮上力 ≈ 10N(0–40km/h ≈ 2.9s,文档 §7②),
        /// 且 2S 极速落 50–55km/h(文档 §2.1)。</summary>
        public float torquePeakNm   = 0.085f;   // N·m
        public float escCurrentLimitA = 200f;   // VXL-3s 电调电流上限
        /// <summary>F4 加速测试目标速度(m/s,文档 §7②:Slash 0–40km/h / Kraton 0–70km/h)。</summary>
        public float accelTargetMps = 11.11f;   // 40 km/h

        // ---------- 电池(文档 §3.5) ----------
        public int cells            = 2;        // 2S LiPo
        public float capacityAh     = 4.2f;     // 4200mAh
        public float packResistanceOhm = 0.008f; // 电池组内阻(电压 sag 来源)
        public float cRating        = 25f;      // 放电倍率 → 最大放电电流 105A

        // ---------- 操控 ----------
        public float maxSteerDeg    = 26f;      // 满舵角(MC 车典型 26°,最小转向半径 ≈0.66m)
        /// <summary>舵机响应:文档 §8「真实 RC 舵机 0.06–0.12 秒/60°」取 0.08s。</summary>
        public float servoTau        = 0.08f;
        /// <summary>电调功率爬升时间常数(无刷电机启动柔化)。</summary>
        public float throttleTau     = 0.06f;
        /// <summary>满制动输入时每轮制动力矩(0.35N·m > μFz·r≈0.20,可锁死车轮)。</summary>
        public float brakeTorqueNm   = 0.35f;

        // ---------- 空气阻力 ----------
        public float dragCd          = 1.05f;   // 短卡车身风阻大
        public float dragAreaM2      = 0.050f;  // 迎风面积

        // ---------- 轮胎特性(文档 §3.1:越野胎横向抓地下降 20–25%) ----------
        public float latGripOnRoad   = 1.00f;   // 铺装路面横向抓地倍率(Slash 公路胎)
        public float latGripOffRoad  = 0.85f;   // 草地/碎石横向抓地倍率

        // ---------- 视觉 ----------
        public Color bodyColor    = new Color(0.79f, 0.18f, 0.16f); // 车壳红
        public Color accentColor  = new Color(0.92f, 0.93f, 0.90f); // 亮色点缀(车顶/翼)
        public Color chassisColor = new Color(0.16f, 0.17f, 0.18f); // 底盘深灰
        public Color rimColor     = new Color(0.10f, 0.10f, 0.11f);
        public Color tireColor    = new Color(0.13f, 0.13f, 0.13f);

        // 便捷量
        public float NominalVoltage => cells * 3.7f;   // 标称电压(Slash 2S = 7.4V)
        public float FullVoltage    => cells * 4.2f;   // 满电电压(2S = 8.4V)
        public float MotorKT        => 9.5493f / motorKv; // 扭矩常数 N·m/A(60/2π/Kv)
        /// <summary>该电压下的电机空载转速 RPM。</summary>
        public float NoLoadRpm(float packVoltage) => motorKv * packVoltage;
        /// <summary>理论轮上极速(m/s,空载转速换算)。</summary>
        public float WheelNoLoadOmega(float packVoltage) =>
            NoLoadRpm(packVoltage) * Mathf.PI * 2f / 60f / gearRatio;

        /// <summary>A 型:Traxxas Slash 4X4 VXL 同级短卡(文档 §2.1)。</summary>
        public static CarSpec CreateSlash() => new CarSpec();

        /// <summary>B 型:ARRMA Kraton 6S 同级大脚(文档 §2.2)。</summary>
        public static CarSpec CreateKraton()
        {
            var s = new CarSpec();
            s.displayName = "大脚 MT(Kraton 型参数)";
            s.length     = 0.594f;
            s.width      = 0.461f;
            s.height     = 0.195f;
            s.wheelbase  = 0.378f;
            s.trackFront = 0.461f;
            s.trackRear  = 0.461f;
            s.wheelRadius = 0.0825f;               // dBoots Copperhead 2 ≈ 165mm
            s.tireWidth  = 0.065f;
            s.chassisClearance = 0.055f;           // 离地 33mm 以上,路沿(4.5cm)可爬
            s.cgHeight   = 0.07f;                  // 文档 §3.2:Kraton 65–75mm
            s.mass       = 4.85f;
            s.unsprungMassPerWheel = 0.18f;
            s.suspensionTravel = 0.045f;
            s.springStiffness = 530f;              // 修正推导:11.9N ÷ 0.0225m
            s.damperRate  = 40f;
            s.antiRollBar = 60f;
            s.motorKv     = 2050f;                 // Spektrum 2050Kv,6S
            s.gearRatio   = 10.0f;                 // 出厂偏扭力齿比(扭矩峰值约为 Slash 2.5–3 倍)
            s.drivetrainEff = 0.88f;
            s.torquePeakNm = 0.35f;                // 推导:起步轮上力 ≈ 34N(≈1.2×抓地,轻微烧胎),
                                                   // 0–70km/h ≈ 2.7s,极速 ≈ 92km/h(文档:96,高速齿比)
            s.escCurrentLimitA = 150f;             // Firma 150A 电调
            s.accelTargetMps = 19.44f;              // 70 km/h
            s.cells      = 6;
            s.capacityAh = 5.0f;                   // 5000mAh
            s.packResistanceOhm = 0.018f;
            s.cRating    = 25f;
            s.maxSteerDeg = 26f;
            s.servoTau   = 0.09f;                  // 更重的转向负载,舵机略慢
            s.throttleTau = 0.06f;
            s.brakeTorqueNm = 1.0f;                // μFz·r ≈ 0.61N·m,可锁死
            s.dragCd     = 0.90f;
            s.dragAreaM2 = 0.095f;
            s.latGripOnRoad  = 0.78f;              // 文档 §3.1:越野胎铺装路面横向抓地 −22%
            s.latGripOffRoad = 1.00f;              // 越野胎在草地/碎石上有相对优势
            s.bodyColor    = new Color(0.16f, 0.37f, 0.79f); // 车壳蓝
            s.accentColor  = new Color(0.94f, 0.64f, 0.10f); // 橙色点缀
            s.chassisColor = new Color(0.15f, 0.15f, 0.17f);
            s.rimColor     = new Color(0.18f, 0.18f, 0.20f);
            s.tireColor    = new Color(0.11f, 0.11f, 0.11f);
            return s;
        }
    }
}