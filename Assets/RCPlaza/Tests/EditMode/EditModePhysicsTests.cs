using NUnit.Framework;
using RCPlaza.Core;
using RCPlaza.Sim;
using RCPlaza.World;
using UnityEngine;

namespace RCPlaza.Tests
{
    /// <summary>
    /// 编辑模式快速单元测试(纯 CPU 计算,毫秒级,不进入 Play 循环):
    /// 电池电压 sag/LVC/库仑计、电机扭矩曲线、Pacejka 边界与摩擦椭圆、
    /// 路面系数表(设计文档 §3.1)、程序纹理生成。
    /// </summary>
    public class EditModePhysicsTests
    {
        [Test]
        public void Battery_Sags_Under_Load_Then_LVC_Cuts()
        {
            var spec = CarSpec.CreateSlash();
            var b = new MotorAndBattery(spec);
            Assert.That(b.packVoltage, Is.EqualTo(8.4f).Within(1e-3f), "满电应 4.2V/芯×2");
            Assert.That(b.soc, Is.EqualTo(1f), "初始 SOC=100%");

            // 满油门 15000rpm 连续放电(CPU 模拟 400s,无需 Play)
            // 注:电流→端电压滞后一步,首帧 V 仍为 OCV,故统计全程最小值
            float dt = 0.01f;
            float vMin = 99f;
            bool sawSag = false, sawLimit = false, sawCut = false;
            for (float t = 0f; t < 400f; t += dt)
            {
                float torque = b.Update(1f, 15000f, dt);
                vMin = Mathf.Min(vMin, b.packVoltage);
                if (b.packVoltage < b.ocv - 0.01f) sawSag = true;
                if (b.powerLimit < 1f) sawLimit = true;
                if (b.lvcCut)
                {
                    sawCut = true;
                    Assert.That(torque, Is.EqualTo(0f), "LVC 切断后扭矩应为 0");
                    break;
                }
            }
            Assert.That(vMin, Is.LessThan(8.35f), $"满油门下端电压应明显低于 OCV(电压 sag,实测最低 {vMin:F3}V)");
            Assert.That(sawSag, "放电全程应出现电压 sag");
            Assert.That(b.soc, Is.LessThan(0.2f), "持续放电应消耗大量电量");
            Assert.That(sawLimit, "低 SOC 时应先触发 50% 限功(3.4V/芯)");
            Assert.That(sawCut, "SOC 耗尽后应触发 LVC 动力切断(3.2V/芯)");
        }

        [Test]
        public void Torque_Curve_Matches_Doc_Profile()
        {
            var b = new MotorAndBattery(CarSpec.CreateSlash());
            Assert.That(b.TorqueFactor(0f), Is.InRange(0.28f, 0.32f), "堵转兜底 0.30(README 记录偏差)");
            Assert.That(b.TorqueFactor(0.10f), Is.InRange(0.50f, 0.62f), "0–25% 上升段");
            Assert.That(b.TorqueFactor(0.50f), Is.InRange(0.90f, 0.93f), "25–70% 峰值平台 0.92");
            Assert.That(b.TorqueFactor(0.80f), Is.InRange(0.62f, 0.75f), "70–95% 衰减段");
            Assert.That(b.TorqueFactor(0.95f), Is.EqualTo(0.375f).Within(0.01f), "95% 处应衰减至 0.375");
            Assert.That(b.TorqueFactor(1.0f), Is.LessThan(1e-4f), "空载转速处扭矩归零(极速修正尾段)");
        }

        [Test]
        public void Pacejka_Bounded_By_Friction_Circle()
        {
            float mu = 0.65f, fz = 6.48f;
            for (float s = -3f; s <= 3.001f; s += 0.02f)
            {
                float fx = PacejkaTyre.Longitudinal(s, fz, mu, 10f, 1.8f, 0.95f);
                Assert.That(Mathf.Abs(fx), Is.LessThanOrEqualTo(mu * fz + 1e-3f), "|Fx| ≤ μFz 恒成立");
                if (Mathf.Abs(s) > 1e-4f)
                    Assert.That(Mathf.Sign(fx), Is.EqualTo(Mathf.Sign(s)), $"Fx 方向应跟随滑移率(s={s})");
            }
            // 峰值滑移率 ≈0.2,之后渐进衰减(渐进滑移模型特征)
            float peak = Mathf.Abs(PacejkaTyre.Longitudinal(0.2f, fz, mu, 10f, 1.8f, 0.95f));
            float tail = Mathf.Abs(PacejkaTyre.Longitudinal(1.2f, fz, mu, 10f, 1.8f, 0.95f));
            Assert.That(peak, Is.GreaterThan(tail), "纵滑峰值后应衰减");
            Assert.That(PacejkaTyre.Longitudinal(0f, fz, mu, 10f, 1.8f, 0.95f),
                Is.EqualTo(0f).Within(1e-5f), "零滑移 → 零力");

            // 摩擦椭圆合成:等比例收缩,矢量和不超过椭圆边界
            PacejkaTyre.Combine(0.8f * mu * fz, 0.8f * mu * fz, mu * fz, mu * fz, out float fxc, out float fyc);
            Assert.That(Mathf.Sqrt(fxc * fxc + fyc * fyc), Is.LessThanOrEqualTo(mu * fz * 1.001f),
                "摩擦椭圆合成不得越界");
        }

        [Test]
        public void Surface_Catalog_Matches_Doc_Table()
        {
            // 设计文档 §3.1:砖 μ 0.55–0.65→0.60 Crr 0.012;沥青 0.60–0.70→0.65 Crr 0.014;
            // 水泥 0.50–0.60→0.55 Crr 0.014;草地 0.35–0.45→0.40 Crr 0.030;碎石→0.45/0.028
            Assert.That(SurfaceCatalog.Get(SurfaceType.Brick).mu, Is.EqualTo(0.60f));
            Assert.That(SurfaceCatalog.Get(SurfaceType.Asphalt).mu, Is.EqualTo(0.65f));
            Assert.That(SurfaceCatalog.Get(SurfaceType.Concrete).mu, Is.EqualTo(0.55f));
            Assert.That(SurfaceCatalog.Get(SurfaceType.Grass).mu, Is.EqualTo(0.40f));
            Assert.That(SurfaceCatalog.Get(SurfaceType.Gravel).mu, Is.EqualTo(0.45f));
            Assert.That(SurfaceCatalog.Get(SurfaceType.Brick).crr, Is.EqualTo(0.012f));
            Assert.That(SurfaceCatalog.Get(SurfaceType.Asphalt).crr, Is.EqualTo(0.014f));
            Assert.That(SurfaceCatalog.Get(SurfaceType.Grass).crr, Is.EqualTo(0.030f));
            Assert.That(SurfaceCatalog.Get(SurfaceType.Gravel).crr, Is.EqualTo(0.028f));
        }

        [Test]
        public void Textures_Generate_For_All_Surfaces()
        {
            foreach (SurfaceType t in System.Enum.GetValues(typeof(SurfaceType)))
            {
                var tex = TextureFactory.Get(t);
                Assert.NotNull(tex, $"{t} 纹理生成失败");
                Assert.That(tex.width, Is.GreaterThanOrEqualTo(256));
                Assert.That(tex.height, Is.EqualTo(tex.width));
                Assert.That(tex.wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
            }
            // 缓存机制:同类型两次取得同一实例
            Assert.That(TextureFactory.Get(SurfaceType.Brick), Is.SameAs(TextureFactory.Get(SurfaceType.Brick)));
        }
    }
}