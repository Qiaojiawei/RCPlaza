using UnityEngine;

namespace RCPlaza.Audio
{
    /// <summary>
    /// 程序化噪声/音调合成(设计文档 §4 三层音频,零音频资产):
    /// White 白噪(颗粒/风)、Brown 棕噪(泄漏积分,低频为主的胎噪)、
    /// Tone 谐波音调(整周期整数采样合成 → 无缝循环,电机 PWM 啸叫)。
    /// 全部 44.1kHz / 2–3 秒,生成开销 <20ms,仅产生一次。
    /// </summary>
    public static class NoiseFactory
    {
        const int Rate = 44100;

        /// <summary>白噪(确定性种子,每次一致)。</summary>
        public static AudioClip White(string name, float seconds = 2f)
        {
            int n = (int)(Rate * seconds);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            var data = new float[n];
            var rng = new System.Random(12345);
            for (int i = 0; i < n; i++)
                data[i] = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.5f;
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>棕噪:一阶泄漏积分(高频衰减 −6dB/oct),再归一化到峰值 0.8。</summary>
        public static AudioClip Brown(string name, float seconds = 3f)
        {
            int n = (int)(Rate * seconds);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            var data = new float[n];
            var rng = new System.Random(54321);
            float acc = 0f, peak = 0f;
            for (int i = 0; i < n; i++)
            {
                acc = 0.996f * acc + 0.020f * (float)(rng.NextDouble() * 2.0 - 1.0);
                data[i] = acc;
                float a = Mathf.Abs(acc);
                if (a > peak) peak = a;
            }
            float g = peak > 1e-4f ? 0.8f / peak : 1f;
            for (int i = 0; i < n; i++) data[i] *= g;
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// 谐波音调:取"秒数内整数个周期"的实际基频 → 循环首尾无缝;
        /// 谐波幅度按 1/k^falloff 衰减(近似方波的 PWM 啸叫质感)。
        /// </summary>
        public static AudioClip Tone(string name, float seconds, float f0, int harmonics, float falloff)
        {
            int n = (int)(Rate * seconds);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            var data = new float[n];

            int cycles = Mathf.Max(1, Mathf.RoundToInt(f0 * seconds));
            float f = cycles / seconds;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float v = 0f;
                for (int k = 1; k <= harmonics; k++)
                {
                    if (k * f > Rate * 0.45f) break;      // 防混叠
                    v += Mathf.Sin(2f * Mathf.PI * k * f * t) / Mathf.Pow(k, falloff);
                }
                data[i] = v;
            }

            float peak = 0.001f;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Abs(data[i]);
                if (a > peak) peak = a;
            }
            float g = 0.85f / peak;
            for (int i = 0; i < n; i++) data[i] *= g;
            clip.SetData(data, 0);
            return clip;
        }
    }
}