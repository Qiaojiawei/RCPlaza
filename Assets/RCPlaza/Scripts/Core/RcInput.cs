using UnityEngine;

namespace RCPlaza.Core
{
    /// <summary>
    /// 键盘输入封装。仅使用 Input.GetKey / GetKeyDown(KeyCode)——
    /// 不依赖 InputManager 轴定义,不依赖 Input System 包(设计文档 §6.4 建议
    /// 的 Input System 可在后续升级,本类保持单一入口以便替换)。
    /// 转向含"舵机滞后"一阶滤波(文档 §8:真实舵机 0.06–0.12 秒/60°)。
    /// </summary>
    public class RcInput
    {
        /// <summary>油门原始指令 ∈[−1,1]:W/↑ = 1,S/↓ = −1。</summary>
        public float ThrottleRaw { get; private set; }
        /// <summary>转向目标 ∈[−1,1]:D/→ = 1,A/← = −1。</summary>
        public float SteerTarget { get; private set; }
        /// <summary>转向实际值(经舵机滞后的转向指令)。</summary>
        public float SteerCurrent { get; private set; }
        /// <summary>手刹(空格)——与前轮制动叠加。</summary>
        public bool Handbrake { get; private set; }
        /// <summary>自动化测试用转向指令覆盖 ∈[−1,1];−2 = 不覆盖(跟随键盘)。</summary>
        public float SteerOverrideX = -2f;

        // 边沿事件(由 GameHost 轮询消费后自动清零)
        public bool PressedSwitchCar { get; private set; } // Tab
        public bool PressedReset    { get; private set; } // R
        public bool PressedCamera   { get; private set; } // C
        public bool PressedHelp     { get; private set; } // F1
        public bool PressedDebug    { get; private set; } // F2
        public bool PressedSlideTest { get; private set; } // F3
        public bool PressedAccelTest { get; private set; } // F4

        /// <summary>每帧轮询(由 GameHost 调用)。servoTau 来自当前车型(文档 §8 舵机响应)。</summary>
        public void Poll(float dt, float servoTau)
        {
            int fwd = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) ? 1 : 0;
            int bwd = (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) ? 1 : 0;
            int rgt = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) ? 1 : 0;
            int lft = (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) ? 1 : 0;

            ThrottleRaw = fwd - bwd;
            SteerTarget = SteerOverrideX >= -1f ? SteerOverrideX : (rgt - lft);
            Handbrake   = Input.GetKey(KeyCode.Space);

            // 舵机迟滞:一阶低通逼近"0.06–0.12s/60°"(τ=0.08s → 60° 约需 3τ≈0.24s 的 75%,
            // 手感接近真实舵机行程;τ 直接等于文档的 s/60° 时间尺度)
            float k = 1f - Mathf.Exp(-dt / Mathf.Max(servoTau, 0.001f));
            SteerCurrent += (SteerTarget - SteerCurrent) * k;

            PressedSwitchCar = Input.GetKeyDown(KeyCode.Tab);
            PressedReset     = Input.GetKeyDown(KeyCode.R);
            PressedCamera    = Input.GetKeyDown(KeyCode.C);
            PressedHelp      = Input.GetKeyDown(KeyCode.F1);
            PressedDebug     = Input.GetKeyDown(KeyCode.F2);
            PressedSlideTest = Input.GetKeyDown(KeyCode.F3);
            PressedAccelTest = Input.GetKeyDown(KeyCode.F4);
        }
    }
}