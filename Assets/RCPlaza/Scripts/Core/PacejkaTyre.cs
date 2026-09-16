using UnityEngine;

namespace RCPlaza.Core
{
    /// <summary>
    /// Pacejka 魔术公式轮胎模型(设计文档 §3.1)。
    /// 纯静态、零分配。形状参数:峰值滑移率 ≈0.2、峰值侧偏角 ≈9–10°、
    /// 原点归一化刚度 B·C ≈ 18–20,滑移/侧偏角超过峰值后渐进衰减 → "渐进滑移"手感。
    /// </summary>
    public static class PacejkaTyre
    {
        /// <summary>归一化魔术公式(峰值为 1):F = sin(C·atan(Bx − E(Bx − atan(Bx))))。</summary>
        public static float Magic(float x, float B, float C, float E)
        {
            float bx = B * x;
            return Mathf.Sin(C * Mathf.Atan(bx - E * (bx - Mathf.Atan(bx))));
        }

        /// <summary>纵向力:slip 为滑移率 λ(ωr−v 方向),正滑移 → 正驱动力。</summary>
        public static float Longitudinal(float slip, float fz, float mu,
            float b, float c, float e)
        {
            if (fz <= 0.0005f) return 0f;
            return fz * mu * Magic(Mathf.Abs(slip), b, c, e) * Mathf.Sign(slip);
        }

        /// <summary>
        /// 横向力:alpha 为侧偏角(rad)。摩擦力的方向必须**对抗**侧滑:
        /// 接地印迹相对地面的横向滑速为 vLat,地面施加的横向力 Fy = −sign(vLat)·|Fy|。
        /// (纵向情形相反:λ = ωr−v 度量的就是轮面滑速方向,驱动力恰为 +sign(λ),
        ///  故纵向横向两式结构不同,不可照抄。)
        /// </summary>
        public static float Lateral(float alpha, float fz, float muEff,
            float b, float c, float e)
        {
            if (fz <= 0.0005f) return 0f;
            return fz * muEff * Magic(Mathf.Abs(alpha), b, c, e) * (-Mathf.Sign(alpha));
        }

        /// <summary>
        /// 摩擦椭圆合成:纵向受力上限 μlon·Fz,横向 μlat·Fz;
        /// 合成超过椭圆时等比例收缩(在滑移大时保持方向稳定)。
        /// </summary>
        public static void Combine(float fx, float fy, float muLonFz, float muLatFz,
            out float fxc, out float fyc)
        {
            if (muLonFz <= 0.0005f || muLatFz <= 0.0005f) { fxc = 0f; fyc = 0f; return; }
            float sx = Mathf.Clamp(fx / muLonFz, -1f, 1f);
            float sy = Mathf.Clamp(fy / muLatFz, -1f, 1f);
            float r = Mathf.Sqrt(sx * sx + sy * sy);
            if (r > 1f) { sx /= r; sy /= r; }
            fxc = sx * muLonFz;
            fyc = sy * muLatFz;
        }
    }
}