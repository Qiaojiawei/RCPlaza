using RCPlaza.Core;
using UnityEngine;

namespace RCPlaza.World
{
    /// <summary>
    /// 住宅小区广场场景构建(设计文档 §5 / §6.3),全部运行时程序生成:
    ///   80×80 沥青底(顶面 y=0)
    ///   → 40×40 砖地广场(顶面 y=+0.02)
    ///   → 广场四周 4.5cm 混凝土路沿(顶面 y=+0.065:Slash 3.2cm 离地间隙
    ///     需速度/角度才能上,MT 5.5cm 可直接骑上路沿,文档 §5 玩法差异)
    ///   → 南北两条 6m 草地带、东侧 3m 碎石带
    /// 装饰:4 灯柱(四角沥青上)、6 长椅(路沿内缘)、4 花坛(广场内四角)、
    /// 4 树(草地带)、5 石块(碎石带)。地面全部 BoxCollider + SurfaceRegion;
    /// 装饰物保留碰撞体(撞上即有物理反馈)。整树 static + 静态合批。
    /// </summary>
    public static class PlazaBuilder
    {
        public static GameObject Build(Transform parent)
        {
            var root = new GameObject("RCPlaza_World");
            if (parent != null) root.transform.SetParent(parent, false);

            var ground = new GameObject("Ground");
            ground.transform.SetParent(root.transform, false);
            var props = new GameObject("Props");
            props.transform.SetParent(root.transform, false);

            BuildGround(ground.transform);
            BuildProps(props.transform);

            StaticBatchingUtility.Combine(ground);
            StaticBatchingUtility.Combine(props);
            return root;
        }

        // ============================================================
        // 地面层
        // ============================================================
        static void BuildGround(Transform parent)
        {
            float A = PlazaConfig.AsphaltSize;
            float P = PlazaConfig.PlazaSize;
            float cw = PlazaConfig.CurbWidth;
            float ch = PlazaConfig.CurbHeight;
            float gm = P / 2f + cw + PlazaConfig.GrassWidth / 2f; // 草带中心距 23.2
            float gr = P / 2f + cw + PlazaConfig.GravelWidth / 2f; // 碎石带中心距 23.2

            // 沥青底 + 砖台
            Box(SurfaceType.Asphalt, parent, new Vector3(0f, -0.04f, 0f), new Vector3(A, 0.08f, A));
            Box(SurfaceType.Brick, parent, new Vector3(0f, -0.02f, 0f), new Vector3(P, 0.04f, P));

            // 路沿四边(顶面 y = 0.02 + 0.045)
            float e = P / 2f - cw / 2f;
            float top = PlazaConfig.PlazaTopY + ch / 2f;
            Box(SurfaceType.Concrete, parent, new Vector3(0f, top, e), new Vector3(P + cw, ch, cw));
            Box(SurfaceType.Concrete, parent, new Vector3(0f, top, -e), new Vector3(P + cw, ch, cw));
            Box(SurfaceType.Concrete, parent, new Vector3(e, top, 0f), new Vector3(cw, ch, P + cw));
            Box(SurfaceType.Concrete, parent, new Vector3(-e, top, 0f), new Vector3(cw, ch, P + cw));

            // 南北草地带(6 × 40)
            Box(SurfaceType.Grass, parent, new Vector3(0f, -0.02f, gm), new Vector3(P, 0.04f, PlazaConfig.GrassWidth));
            Box(SurfaceType.Grass, parent, new Vector3(0f, -0.02f, -gm), new Vector3(P, 0.04f, PlazaConfig.GrassWidth));

            // 东侧碎石带(40 × 3)
            Box(SurfaceType.Gravel, parent, new Vector3(gr, -0.02f, 0f), new Vector3(PlazaConfig.GravelWidth, 0.04f, P));
        }

        // ============================================================
        // 装饰层
        // ============================================================
        static void BuildProps(Transform parent)
        {
            Material metal = MaterialFactory.Colored(new Color32(0x55, 0x5A, 0x60, 0xFF), 0.50f);
            Material lampM = MaterialFactory.Colored(new Color32(0xFF, 0xEA, 0xA0, 0xFF), 0.05f);
            Material wood = MaterialFactory.Colored(new Color32(0x8A, 0x5A, 0x33, 0xFF), 0.15f);
            Material foliage = MaterialFactory.Colored(new Color32(0x2F, 0x63, 0x20, 0xFF), 0.02f);
            Material stone = MaterialFactory.Colored(new Color32(0x8F, 0x8B, 0x84, 0xFF), 0.04f);
            Material planterM = MaterialFactory.Colored(new Color32(0x6E, 0x75, 0x7E, 0xFF), 0.20f);

            // 灯柱 ×4:广场四角外(沥青上),杆朝广场中心倾臂
            Vector3[] lampPos = { new Vector3(21, 0, 21), new Vector3(-21, 0, 21), new Vector3(21, 0, -21), new Vector3(-21, 0, -21) };
            foreach (var p in lampPos)
            {
                var root = new GameObject("Lamp");
                root.transform.SetParent(parent, false);
                root.transform.localPosition = p;
                Vector3 toCenter = -p; toCenter.y = 0f;
                root.transform.localRotation = Quaternion.LookRotation(toCenter); // +z 朝向广场
                AddSurface(root, SurfaceType.Concrete);

                Prim(Cylinder, root, "Base", new Vector3(0f, 0.07f, 0f), new Vector3(0.26f, 0.07f, 0.26f), metal, true);
                Prim(Cylinder, root, "Post", new Vector3(0f, 1.35f, 0f), new Vector3(0.10f, 1.35f, 0.10f), metal, true);
                Prim(Cube, root, "Arm", new Vector3(0f, 2.62f, 0.35f), new Vector3(0.07f, 0.07f, 0.55f), metal, false);
                Prim(Cube, root, "Head", new Vector3(0f, 2.54f, 0.62f), new Vector3(0.36f, 0.10f, 0.22f), lampM, false);
            }

            // 长椅 ×6:南北各 3(路沿内侧,面向广场中心)
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = -1; i <= 1; i++)
                {
                    float x = i * 10f, z = side * 17.2f;
                    var root = new GameObject("Bench_" + (side > 0 ? "N" : "S") + (i + 2));
                    root.transform.SetParent(parent, false);
                    root.transform.localPosition = new Vector3(x, PlazaConfig.PlazaTopY, z);
                    Vector3 toCenter = new Vector3(-x, 0f, -z);
                    root.transform.localRotation = Quaternion.LookRotation(toCenter);
                    AddSurface(root, SurfaceType.Concrete);
                    var bc = root.AddComponent<BoxCollider>();
                    bc.center = new Vector3(0f, 0.28f, 0f);
                    bc.size = new Vector3(1.9f, 0.58f, 0.52f);

                    Prim(Cube, root, "LegL", new Vector3(-0.78f, 0.21f, 0f), new Vector3(0.08f, 0.44f, 0.44f), wood, false);
                    Prim(Cube, root, "LegR", new Vector3(0.78f, 0.21f, 0f), new Vector3(0.08f, 0.44f, 0.44f), wood, false);
                    Prim(Cube, root, "Seat", new Vector3(0f, 0.46f, 0f), new Vector3(1.9f, 0.07f, 0.52f), wood, false);
                    Prim(Cube, root, "Back", new Vector3(0f, 0.72f, -0.24f), new Vector3(1.9f, 0.45f, 0.07f), wood, false);
                }
            }

            // 花坛 ×4:广场内四角(基座 + 三丛灌木)
            Vector3[] planterPos = { new Vector3(16.8f, 0, 16.8f), new Vector3(-16.8f, 0, 16.8f),
                                     new Vector3(16.8f, 0, -16.8f), new Vector3(-16.8f, 0, -16.8f) };
            int pIdx = 0;
            foreach (var p in planterPos)
            {
                var root = new GameObject("Planter_" + (pIdx++));
                root.transform.SetParent(parent, false);
                root.transform.localPosition = new Vector3(p.x, PlazaConfig.PlazaTopY, p.z);
                AddSurface(root, SurfaceType.Concrete);
                var bc = root.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, 0.28f, 0f);
                bc.size = new Vector3(1.75f, 0.58f, 1.75f);

                Prim(Cube, root, "Tub", new Vector3(0f, 0.21f, 0f), new Vector3(1.7f, 0.42f, 1.7f), planterM, false);
                Prim(Cube, root, "Rim", new Vector3(0f, 0.44f, 0f), new Vector3(1.82f, 0.07f, 1.82f), stone, false);
                Prim(Sphere, root, "ShrubA", new Vector3(-0.36f, 0.62f, 0.18f), new Vector3(0.72f, 0.55f, 0.72f), foliage, false);
                Prim(Sphere, root, "ShrubB", new Vector3(0.28f, 0.56f, -0.14f), new Vector3(0.62f, 0.48f, 0.62f), foliage, false);
                Prim(Sphere, root, "ShrubC", new Vector3(0.05f, 0.70f, 0.36f), new Vector3(0.55f, 0.50f, 0.55f), foliage, false);
            }

            // 树 ×4:南北草带各 2(树干保留胶囊碰撞体)
            Vector3[] treePos = { new Vector3(-11f, 0, 23.2f), new Vector3(11f, 0, 23.2f),
                                  new Vector3(-10f, 0, -23.2f), new Vector3(10f, 0, -23.2f) };
            float[] treeScale = { 1.15f, 0.95f, 1.05f, 0.90f };
            for (int i = 0; i < 4; i++)
            {
                var root = new GameObject("Tree_" + i);
                root.transform.SetParent(parent, false);
                root.transform.localPosition = treePos[i];
                AddSurface(root, SurfaceType.Grass);

                float s = treeScale[i];
                Prim(Cylinder, root, "Trunk", new Vector3(0f, 0.85f * s, 0f), s * new Vector3(0.30f, 0.85f, 0.30f), wood, true);
                Prim(Sphere, root, "CrownA", new Vector3(0f, 2.05f * s, 0f), s * new Vector3(1.55f, 1.15f, 1.55f), foliage, false);
                Prim(Sphere, root, "CrownB", new Vector3(0.50f * s, 1.55f * s, 0.30f * s), s * new Vector3(0.85f, 0.65f, 0.85f), foliage, false);
                Prim(Sphere, root, "CrownC", new Vector3(-0.45f * s, 1.45f * s, -0.25f * s), s * new Vector3(0.75f, 0.55f, 0.75f), foliage, false);
            }

            // 石块 ×5:碎石带内(压扁球体,保留球碰撞体)
            float[] rockZ = { -16f, -8f, 0f, 8f, 16f };
            float[] rockS = { 0.90f, 0.65f, 1.10f, 0.75f, 0.85f };
            Vector3[] rockRot = { new Vector3(0, 20, 0), new Vector3(0, 120, 0), new Vector3(15, 60, 0),
                                  new Vector3(0, 200, 0), new Vector3(10, 310, 0) };
            for (int i = 0; i < 5; i++)
            {
                var root = new GameObject("Rock_" + i);
                root.transform.SetParent(parent, false);
                root.transform.localPosition = new Vector3(23.2f, 0f, rockZ[i]);
                AddSurface(root, SurfaceType.Gravel);

                float s = rockS[i];
                Prim(Sphere, root, "Rock", new Vector3(0f, 0.22f * s, 0f), new Vector3(0.85f * s, 0.55f * s, 0.95f * s), stone, true, rockRot[i]);
            }
        }

        const PrimitiveType Cylinder = PrimitiveType.Cylinder;
        const PrimitiveType Cube = PrimitiveType.Cube;
        const PrimitiveType Sphere = PrimitiveType.Sphere;

        static void AddSurface(GameObject go, SurfaceType type)
        {
            go.AddComponent<SurfaceRegion>().type = type;
        }

        /// <summary>地面块:含 BoxCollider + 地表标记。</summary>
        static GameObject Box(SurfaceType type, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(Cube);
            go.name = "Srf_" + type;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = MaterialFactory.Ground(type);
            go.isStatic = true;
            go.AddComponent<SurfaceRegion>().type = type;
            return go;
        }

        static GameObject Prim(PrimitiveType p, GameObject parent, string name, Vector3 pos, Vector3 scale,
                               Material mat, bool keepCollider, Vector3 euler = default(Vector3))
        {
            var go = GameObject.CreatePrimitive(p);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!keepCollider) Object.Destroy(go.GetComponent<Collider>());
            go.isStatic = true;
            return go;
        }
    }
}