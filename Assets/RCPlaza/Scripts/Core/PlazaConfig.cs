namespace RCPlaza.Core
{
    /// <summary>
    /// 全局配置(对应设计文档 §6「性能优先」与 §5「场景与环境」)。
    /// 所有可调常数集中在顶层,方便对照文档调校。
    /// </summary>
    public static class PlazaConfig
    {
        // ---------- 物理时序(文档 §6.1) ----------
        /// <summary>固定物理步长:0.01s = 100Hz。RC 车加速度极高,需要高于默认 50Hz 的物理频率。</summary>
        public const float FixedDt = 0.01f;
        /// <summary>Maximum Allowed Timestep:最坏情况下每帧物理切片上限,防止帧率骤降时物理爆炸。</summary>
        public const float MaxAllowedDt = 0.033f;
        /// <summary>目标渲染帧率(配合关闭垂直同步)。</summary>
        public const int TargetFPS = 60;

        // ---------- 场景尺寸(文档 §5,单位:米,真实比例) ----------
        /// <summary>广场主铺装区边长(正方形)。</summary>
        public const float PlazaSize = 40f;
        /// <summary>广场砖台顶面高度(略高于周边沥青以形成地坪差)。</summary>
        public const float PlazaTopY = 0.02f;
        /// <summary>路沿高度 3–5cm,取 4.5cm(文档 §5 B 区)。</summary>
        public const float CurbHeight = 0.045f;
        /// <summary>路沿宽度。</summary>
        public const float CurbWidth = 0.15f;
        /// <summary>周边沥青铺装区尺寸。</summary>
        public const float AsphaltSize = 80f;
        /// <summary>绿化带宽度。</summary>
        public const float GrassWidth = 6f;
        /// <summary>碎石带宽度。</summary>
        public const float GravelWidth = 3f;

        // ---------- 玩法 ----------
        /// <summary>
        /// 电池放电时间倍率:真实 Slash 2S 满油续航约 10–15 分钟(文档 §3.5)。
        /// 为使电池衰减(文档 §7 验收④)在游戏时间内可观察,默认按 2 倍速放电。
        /// 改回 1.0 即完全真实时间。
        /// </summary>
        public const float DischargeTimeScale = 2.0f;
        /// <summary>R 键重置后车辆位置。</summary>
        public const bool ShowDebugByDefault = false;

        // ---------- 物体层(避开 Unity 内置 "Ignore Raycast" 层,防止悬挂射线打到车身) ----------
        public const int IgnoreRaycastLayer = 2;
    }
}