using System.Collections.Generic;
using RCPlaza.Core;
using UnityEngine;

namespace RCPlaza.World
{
    /// <summary>
    /// 材质工厂(设计文档 §6.3 性能 → 全局共享材质 + 静态合批):
    /// Shader.Find 三级降级 Standard → Legacy Shaders/Diffuse → Sprites/Default
    /// (→ Unlit/Color 兜底),纯色/地表材质全程缓存,整场材质总数 ~15 份。
    /// </summary>
    public static class MaterialFactory
    {
        static Shader _shader;
        static readonly Dictionary<SurfaceType, Material> _ground = new Dictionary<SurfaceType, Material>();
        static readonly Dictionary<string, Material> _colored = new Dictionary<string, Material>();

        public static Shader Shader
        {
            get
            {
                if (_shader == null)
                {
                    _shader = Shader.Find("Standard");
                    if (_shader == null) _shader = Shader.Find("Legacy Shaders/Diffuse");
                    if (_shader == null) _shader = Shader.Find("Sprites/Default");
                    if (_shader == null) _shader = Shader.Find("Unlit/Color");
                    if (_shader == null)
                        throw new System.Exception("RCPlaza: 找不到任何可用 Shader(Standard/Legacy/Sprites/Unlit)!");
                }
                return _shader;
            }
        }

        /// <summary>地表材质(程序纹理 + 按地表定制的 tiling)。</summary>
        public static Material Ground(SurfaceType type)
        {
            Material m;
            if (!_ground.TryGetValue(type, out m))
            {
                m = new Material(Shader);
                m.mainTexture = TextureFactory.Get(type);
                ApplyGloss(m, 0.01f);
                switch (type)
                {
                    case SurfaceType.Brick:    m.mainTextureScale = new Vector2(24f, 24f); break; // 砖格 ≈0.42m
                    case SurfaceType.Asphalt:  m.mainTextureScale = new Vector2(6f, 6f); break;   // 80m 底
                    case SurfaceType.Concrete: m.mainTextureScale = new Vector2(1f, 33f); break;  // 路沿带 40m
                    case SurfaceType.Grass:    m.mainTextureScale = new Vector2(2f, 13f); break;  // 6×40 草带
                    case SurfaceType.Gravel:   m.mainTextureScale = new Vector2(13f, 1f); break;  // 40×3 碎石带
                }
                _ground[type] = m;
            }
            return m;
        }

        /// <summary>纯色材质(车身 / 装饰件),按颜色+光泽缓存。</summary>
        public static Material Colored(Color c, float gloss = 0.3f)
        {
            string key = string.Format("{0:X2}{1:X2}{2:X2}|{3:F2}",
                (int)(c.r * 255f), (int)(c.g * 255f), (int)(c.b * 255f), gloss);
            Material m;
            if (!_colored.TryGetValue(key, out m))
            {
                m = new Material(Shader);
                m.color = c;
                ApplyGloss(m, gloss);
                _colored[key] = m;
            }
            return m;
        }

        static void ApplyGloss(Material m, float gloss)
        {
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        }
    }
}