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
        // 阻尼取 ζ≈0.9: c = 2ζ√(k·m/4)。(曾 0.7/25:起步俯仰振荡 + 前轮 droop 边界
        // 半波整流踢实测把 SCT 顶成 3° 持续翘头、极速测试在 42km/h 端头腾空;实测定圆
        // 侧倾均值只取决于 k 稳态,c 只伤瞬态,故提高到 0.9 拟临界。)
        public float suspensionTravel = 0.035f; // 行程 35mm(文档 §2.1:避震长 48mm,行程约 35mm)
        public float springStiffness  = 370f;   // N/m per wheel(修正值,推导见上)
        public float damperRate       = 32f;    // N·s/m per wheel(ζ≈0.9)
        public float antiRollBar      = 30f;    // N/m 每轴(压缩差→两侧等大反向竖直力)

        // ---------- 动力传动(文档 §2.1 / §3.4) ----------
        public float motorKv        = 3500f;    // Velineon 3500:3500 RPM/V
        public float gearRatio      = 7.32f;    // 总传动比 7.32:1(内齿比 5.04:1)
        public float drivetrainEff  = 0.88f;    // 齿轮传动效率 85–92% 取 0.88
        /// <summary>电机峰值扭矩(标称电压下)。推导:轮转耦合(−Fx·r)补全后,起步轮上力走
        /// T·η·g/r 的诚实扭矩通道——0.100 N·m ⇒ ≈11.3 N(0→40km/h ≈2.7–2.9s,在 μFz
        /// 抓地 ≈16.8N 之下不烧胎,文档 §7②);极速由 风阻 × 扭矩衰减 平衡落 ≈58.5 km/h,
        /// 与文档"约 50 km/h(2S)"一致(3S 外推 ≈80 km/h 亦对得上,README 偏差 4 复核;
        /// 实测跑满 74m 直道后 ≈59.0 km/h,落在收敛区间 57.5–58.5 附近)。
        /// 曾按 0.085 推导,但那是 λ 储能通道放大的结果,耦合补全后实测 0→40≈4.9s 超标。</summary>
        public float torquePeakNm   = 0.100f;   // N·m
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

        // ---------- 轮胎特性(文档 §3.1:越野胎横向抓地下降 20–25%;越野胎滚阻 1.8–2.2× 公路胎) ----------
        public float latGripOnRoad   = 1.00f;   // 铺装路面横向抓地倍率(Slash 公路胎)
        public float latGripOffRoad  = 0.85f;   // 草地/碎石横向抓地倍率
        public float crrMult         = 1.0f;    // 滚阻倍率(胎质差异:WheelUnit 里 × 地表 Crr;Kraton 越野胎取 2.0)

        // ---------- 稳定性工程项(文档未列,烧胎失稳必须) ----------
        public float yawDamp         = 0.8f;    // N·m·s/rad 偏航阻尼(等价轮胎自回正+轮距几何)

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
            s.damperRate  = 55f;                   // ζ≈0.9(同 Slash 成效:压起步俯仰振荡)
            s.antiRollBar = 60f;
            s.motorKv     = 2050f;                 // Spektrum 2050Kv,6S
            s.gearRatio   = 10.0f;                 // 出厂偏扭力齿比(峰值扭矩 3.5× Slash,见下偏差 6)
            s.drivetrainEff = 0.88f;
            s.torquePeakNm = 0.35f;                // 3.5× Slash,略超文档 §2.2"扭矩 2.5–3 倍"指引(偏差 6):
                                                   // 反推自 §7② 0–70km/h≈3s 与 §5 半油门爬 4.5cm 路沿;起步轮上力
                                                   // ≈37N > μFz 抓地 ≈31N,由轮转闭合的峰值饱和兜底(轻微烧胎起步),
                                                   // 0–70km/h 实测 ≈3.2s,极速 ≈ 92km/h(文档:96,高速齿比)
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
            s.crrMult        = 2.0f;               // 文档 §3.1:越野胎滚阻 = 公路胎 1.8–2.2 倍,取 2.0
            s.bodyColor    = new Color(0.16f, 0.37f, 0.79f); // 车壳蓝
            s.accentColor  = new Color(0.94f, 0.64f, 0.10f); // 橙色点缀
            s.chassisColor = new Color(0.15f, 0.15f, 0.17f);
            s.rimColor     = new Color(0.18f, 0.18f, 0.20f);
            s.tireColor    = new Color(0.11f, 0.11f, 0.11f);
            return s;
        }
    }
}