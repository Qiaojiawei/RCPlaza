using System.Collections.Generic;
using RCPlaza.Core;
using UnityEngine;

namespace RCPlaza.World
{
    /// <summary>
    /// 程序化地表纹理工厂(零二进制资产,启动时各生成并缓存一次):
    /// 砖(顺砖错缝+灰缝)、沥青(双频灰噪)、草地(低频色块+高频噪)、
    /// 碎石(高频杂色)、混凝土(细噪)。全部 Repeat + Bilinear + 各向异性。
    /// 纹理尺寸:砖 512²(大图案),其余 256²;生成总量约 0.5M 像素,启动开销 <10ms。
    /// </summary>
    public static class TextureFactory
    {
        static readonly Dictionary<SurfaceType, Texture2D> _cache = new Dictionary<SurfaceType, Texture2D>();

        // 基色(设计文档 §5 住宅小区色调:暖红砖 / 冷灰路 / 自然绿)
        static readonly Color32 BrickBase   = new Color32(0xB3, 0x69, 0x47, 0xFF);
        static readonly Color32 BrickDark   = new Color32(0x9C, 0x52, 0x36, 0xFF);
        static readonly Color32 Mortar      = new Color32(0xD8, 0xCC, 0xBB, 0xFF);
        static readonly Color32 AsphaltBase = new Color32(0x4B, 0x4F, 0x53, 0xFF);
        static readonly Color32 GrassBase   = new Color32(0x4E, 0x79, 0x2D, 0xFF);
        static readonly Color32 GravelBase  = new Color32(0x8C, 0x82, 0x72, 0xFF);
        static readonly Color32 ConcreteBase = new Color32(0x9C, 0x9C, 0x97, 0xFF);

        public static Texture2D Get(SurfaceType type)
        {
            Texture2D tex;
            if (!_cache.TryGetValue(type, out tex))
            {
                tex = Build(type);
                _cache[type] = tex;
            }
            return tex;
        }

        static Texture2D NewTex(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 4;
            return tex;
        }

        /// <summary>确定性伪随机(纹理仅生成一次,无跨平台差异要求)。</summary>
        static float Hash(int x, int y)
        {
            float v = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        static Texture2D Build(SurfaceType type)
        {
            int size = type == SurfaceType.Brick ? 512 : 256;
            var tex = NewTex(size);
            var px = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    px[y * size + x] = Pixel(type, x, y, size);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }

        static Color32 Pixel(SurfaceType type, int x, int y, int size)
        {
            switch (type)
            {
                case SurfaceType.Brick: return BrickPixel(x, y);
                case SurfaceType.Asphalt: return AsphaltPixel(x, y);
                case SurfaceType.Grass: return GrassPixel(x, y);
                case SurfaceType.Gravel: return GravelPixel(x, y);
                default: return ConcretePixel(x, y);
            }
        }

        // 砖:96×48 像素/砖、7px 灰缝、奇数行错半砖(running bond)、逐砖色差 + 边缘压暗
        static Color32 BrickPixel(int x, int y)
        {
            int bw = 128, bh = 64, m = 7;
            int row = y / bh;
            int bx = (x + (row % 2) * (bw / 2)) % bw;
            int by = y % bh;
            if (bx < m || by < m)
            {
                return Scale(Mortar, 0.92f + 0.08f * Hash(x, y));
            }
            Color32 c = Color32.Lerp(BrickBase, BrickDark, Hash(row, x / bw));
            float br = 0.88f + 0.10f * Hash(x / 3, y / 3);   // 砖面细微纹理
            br *= 0.96f + 0.08f * Hash(x, y);                  // 细噪
            if (bx < m + 3 || bx > bw - m - 4 || by > bh - m - 4) br *= 0.90f; // 边缘压暗
            return Scale(c, br);
        }

        static Color32 AsphaltPixel(int x, int y)
        {
            float n = 0.90f + 0.15f * Hash(x / 4, y / 4) + 0.05f * Hash(x, y);
            if (Hash(x * 7 + 3, y * 7 + 11) > 0.985f) n *= 1.30f; // 偶发浅色颗粒
            return Scale(AsphaltBase, n);
        }

        static Color32 GrassPixel(int x, int y)
        {
            float patch = 0.85f + 0.20f * Hash(x / 16, y / 16);  // 色块
            float fine = 0.94f + 0.10f * Hash(x, y);              // 草叶噪
            return Scale(GrassBase, patch * fine);
        }

        static Color32 GravelPixel(int x, int y)
        {
            float n = 0.74f + 0.42f * Hash(x, y) + 0.14f * Hash(x / 12, y / 12);
            return Scale(GravelBase, n);
        }

        static Color32 ConcretePixel(int x, int y)
        {
            return Scale(ConcreteBase, 0.94f + 0.09f * Hash(x, y));
        }

        static Color32 Scale(Color32 c, float k)
        {
            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * k), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * k), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * k), 0, 255),
                255);
        }
    }
}