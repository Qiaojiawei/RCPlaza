namespace RCPlaza.Core
{
    /// <summary>地面材质类型(设计文档 §3.1 / §5)。</summary>
    public enum SurfaceType
    {
        Brick,     // 干燥广场砖(主铺装区)
        Asphalt,   // 沥青(广场外围道路)
        Concrete,  // 水泥 / 混凝土(路沿)
        Grass,     // 草地 / 绿化带
        Gravel,    // 碎石 / 泥土小径
    }

    /// <summary>
    /// 单一地表的路面参数:Pacejka 魔术公式系数 + 滚动阻力。
    /// 数值来源:设计文档 §3.1 表格(μ 取表中区间中值,滚动阻力系数取区间中值)。
    /// </summary>
    [System.Serializable]
    public struct SurfaceParams
    {
        public SurfaceType type;
        /// <summary>峰值摩擦系数(静摩擦 COF 基准)。</summary>
        public float mu;
        /// <summary>滚动阻力系数。</summary>
        public float crr;
        /// <summary>Pacejka 纵向刚度因子 Bx。</summary>
        public float bx;
        /// <summary>Pacejka 横向刚度因子 By。</summary>
        public float by;
        /// <summary>Pacejka 纵向形状因子 Cx。</summary>
        public float cx;
        /// <summary>Pacejka 横向形状因子 Cy。</summary>
        public float cy;
        /// <summary>Pacejka 纵向曲率因子 Ex。</summary>
        public float ex;
        /// <summary>Pacejka 横向曲率因子 Ey。</summary>
        public float ey;

        public SurfaceParams(SurfaceType t, float mu, float crr)
        {
            type = t;
            this.mu = mu;
            this.crr = crr;
            // 形状参数:B·C ≈ 18–20(原点刚度),峰值滑移率 ≈ 0.2 / 峰值侧偏角 ≈ 9–10°
            bx = 10f;  cx = 1.8f;  ex = 0.95f;
            by = 12f;  cy = 1.7f;  ey = 0.90f;
        }
    }

    /// <summary>地表参数目录——设计文档 §3.1 摩擦模型参数表。</summary>
    public static class SurfaceCatalog
    {
        // μ 取值:干燥广场砖 0.55–0.65 → 0.60;水泥/混凝土 0.50–0.60 → 0.55;
        // 沥青 0.60–0.70 → 0.65;草地 0.35–0.45 → 0.40;碎石近似草地稍高 → 0.45。
        // Crr 取值:砖 0.010–0.015 → 0.012;沥青/水泥 0.010–0.018 → 0.014;
        // 草地 0.025–0.035 → 0.030;碎石 → 0.028。
        private static readonly SurfaceParams[] s_table =
        {
            new SurfaceParams(SurfaceType.Brick,    0.60f, 0.012f),
            new SurfaceParams(SurfaceType.Asphalt,  0.65f, 0.014f),
            new SurfaceParams(SurfaceType.Concrete, 0.55f, 0.014f),
            new SurfaceParams(SurfaceType.Grass,    0.40f, 0.030f),
            new SurfaceParams(SurfaceType.Gravel,   0.45f, 0.028f),
        };

        /// <summary>按类型取参数(线性查找,仅 5 项,零分配)。</summary>
        public static SurfaceParams Get(SurfaceType type)
        {
            for (int i = 0; i < s_table.Length; i++)
                if (s_table[i].type == type)
                    return s_table[i];
            return s_table[0]; // 防御性回退:广场砖
        }
    }
}