using RCPlaza.Core;
using UnityEngine;

namespace RCPlaza.World
{
    /// <summary>
    /// 地表类型标记:纯标记组件,不自动补碰撞体(自动补会导致灯柱/树干根部出现
    /// 1m 隐形碰撞箱)。挂在每块地面及装饰物"根物体"上,悬挂射线命中其任意
    /// 子碰撞体后,经 GetComponentInParent 解析地表参数(设计文档 §3.1 路面表)。
    /// </summary>
    public class SurfaceRegion : MonoBehaviour
    {
        public SurfaceType type = SurfaceType.Brick;

        /// <summary>碰撞体 → 地表类型;无标记的面(编辑器临时地面等)按沥青处理。</summary>
        public static SurfaceType Resolve(Collider col)
        {
            var sr = col != null ? col.GetComponentInParent<SurfaceRegion>() : null;
            return sr != null ? sr.type : SurfaceType.Asphalt;
        }

        void Reset() { type = SurfaceType.Brick; }
    }
}