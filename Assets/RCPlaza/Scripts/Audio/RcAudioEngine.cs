using RCPlaza.Sim;
using UnityEngine;

namespace RCPlaza.Audio
{
    /// <summary>
    /// 三层程序化音频引擎(设计文档 §4),5 个 2D 音源(spatialBlend=0)全部启动时
    /// 以 volume=0 播放,运行时只调音量/音高/滤波 → 无启动/停止爆音:
    ///   ① 电机 PWM 啸叫:谐波音调 loop,pitch ∝ 电机转速(0.35–2.2),音量随油门;
    ///   ② 齿轮噪声:白噪 + 低通(截止 600–3200Hz 随转速);
    ///   ③④ 胎噪双源:棕噪,硬路面低通 500–900Hz / 草地碎石 200–350Hz,
    ///       地表切换 0.2s 恒定功率交叉淡化(h²+s²=1,cos²/sin²);
    ///   ⑤ 风噪:白噪 + 低通 + 音量 ∝ v²(高速呼啸)。
    /// 音频始终绑定当前被控车辆(换车即无缝切换声源参数)。
    /// </summary>
    public class RcAudioEngine : MonoBehaviour
    {
        AudioSource motorSrc, gearSrc, tyreHard, tyreSoft, windSrc;
        AudioLowPassFilter gearLp, hardLp, softLp, windLp;
        float fade; // 地面混合系数:0=硬路面,1=软路面

        void Awake()
        {
            motorSrc = AddSource("Audio_Motor", NoiseFactory.Tone("Tone_Motor", 2f, 220f, 14, 1.4f));
            gearSrc = AddSource("Audio_Gear", NoiseFactory.White("Noise_Gear", 2f));
            gearLp = gearSrc.gameObject.AddComponent<AudioLowPassFilter>();
            gearLp.cutoffFrequency = 1500f;

            tyreHard = AddSource("Audio_TyreHard", NoiseFactory.Brown("Noise_TyreHard", 3f));
            hardLp = tyreHard.gameObject.AddComponent<AudioLowPassFilter>();
            hardLp.cutoffFrequency = 650f;
            tyreSoft = AddSource("Audio_TyreSoft", NoiseFactory.Brown("Noise_TyreSoft", 3f));
            softLp = tyreSoft.gameObject.AddComponent<AudioLowPassFilter>();
            softLp.cutoffFrequency = 260f;

            windSrc = AddSource("Audio_Wind", NoiseFactory.White("Noise_Wind", 2f));
            windLp = windSrc.gameObject.AddComponent<AudioLowPassFilter>();
            windLp.cutoffFrequency = 500f;
        }

        AudioSource AddSource(string name, AudioClip clip)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = 0f;   // 2D:声像不随车辆移动(立体声环境音)
            s.volume = 0f;
            s.Play();
            return s;
        }

        /// <summary>每帧由 GameHost 调用,参数取自当前被控车辆。</summary>
        public void UpdateFor(RcCarController car)
        {
            if (car == null) return;
            var b = car.Battery;
            float v = car.SpeedMps;
            float x = Mathf.Clamp01(car.MotorRpm / Mathf.Max(b.noLoadRpm, 1f)); // 转速占比
            float thr = Mathf.Abs(car.ThrottleCmd);

            // ① 电机:PWM 啸叫(怠速微鸣 → 全油门尖锐)
            motorSrc.pitch = Mathf.Clamp(Mathf.Lerp(0.35f, 2.2f, x), 0.25f, 2.5f);
            motorSrc.volume = Mathf.Clamp01(0.02f + 0.30f * thr + 0.18f * x);

            // ② 齿轮:白噪低通,截止频率与转速成正比
            gearLp.cutoffFrequency = Mathf.Lerp(600f, 3200f, x);
            gearSrc.volume = Mathf.Clamp01(0.02f + 0.12f * thr + 0.10f * x);

            // ③④ 胎噪:0.2s 恒定功率交叉淡化(铺装 ⇄ 草地/碎石)
            float target = car.OnSmoothSurface ? 0f : 1f;
            float k = 1f - Mathf.Exp(-Time.deltaTime / 0.10f);
            fade += (target - fade) * k;
            float a = fade * Mathf.PI * 0.5f;
            float hGain = Mathf.Cos(a); hGain *= hGain;
            float sGain = Mathf.Sin(a); sGain *= sGain;
            float roll = Mathf.Clamp01(v / 18f);
            tyreHard.volume = hGain * (0.05f + 0.55f * roll);
            tyreSoft.volume = sGain * (0.05f + 0.55f * roll);
            hardLp.cutoffFrequency = Mathf.Lerp(500f, 900f, roll);
            softLp.cutoffFrequency = Mathf.Lerp(200f, 350f, roll);

            // ⑤ 风噪:v² 增长
            float v2 = Mathf.Clamp01(v / 25f); v2 *= v2;
            windSrc.volume = 0.42f * v2;
            windLp.cutoffFrequency = Mathf.Lerp(300f, 1100f, v2);
            windSrc.pitch = 0.80f + 0.45f * v2;
        }

        // ---------- 测试只读诊断(自动化验收:音频状态追踪车况,文档 §4) ----------
        public float MotorPitch      => motorSrc  != null ? motorSrc.pitch  : 0f;
        public float MotorVolume     => motorSrc  != null ? motorSrc.volume : 0f;
        public float TyreHardVolume  => tyreHard   != null ? tyreHard.volume  : 0f;
        public float TyreSoftVolume  => tyreSoft   != null ? tyreSoft.volume  : 0f;
        public float WindVolume      => windSrc    != null ? windSrc.volume   : 0f;
    }
}