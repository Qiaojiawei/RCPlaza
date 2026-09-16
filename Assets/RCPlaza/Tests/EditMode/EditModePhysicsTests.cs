using NUnit.Framework;
using RCPlaza.Audio;
using RCPlaza.Core;
using RCPlaza.Sim;
using RCPlaza.World;
using UnityEngine;

namespace RCPlaza.Tests
{
    /// <summary>
    /// 编辑模式快速单元测试(纯 CPU 计算,毫秒级,不进入 Play 循环):
    /// 电池电压 sag/LVC/库仑计、电机扭矩曲线、Pacejka 边界与摩擦椭圆、
    /// 路面系数表(设计文档 §3.1)、程序纹理生成、车型参数表(§2/§3.2/§3.5)、
    /// 悬挂静压目标位置(§3.3)、程序音频波形(§4)、SOC 测试钩子(§7④)。
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
            Assert.That(b.TorqueFactor(0f), Is.EqualTo(0.92f).Within(0.01f), "堵转即满扭矩 0.92(平台起步,README 偏差 2;曾用 0.30 兜底——轮转耦合补全后实测 0→40≈4.9s 超标)");
            Assert.That(b.TorqueFactor(0.10f), Is.EqualTo(0.92f).Within(0.01f), "0–70% 峰值平台");
            Assert.That(b.TorqueFactor(0.50f), Is.EqualTo(0.92f).Within(0.01f), "0–70% 峰值平台");
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

            // 横向力必须**对抗**侧滑(Fy = −sign(α)·|Fy|),否则侧滑正反馈自加速
            // (曾实测:收油后车辆无动力横向螺旋提速 4.3 m/s²——符号错误的直接后果)
            for (float a = -1.5f; a <= 1.501f; a += 0.02f)
            {
                float fy = PacejkaTyre.Lateral(a, fz, mu, 12f, 1.7f, 0.9f);
                Assert.That(Mathf.Abs(fy), Is.LessThanOrEqualTo(mu * fz + 1e-3f), "|Fy| ≤ μFz 恒成立");
                if (Mathf.Abs(a) > 1e-4f)
                    Assert.That(Mathf.Sign(fy), Is.EqualTo(-Mathf.Sign(a)), $"Fy 应对抗侧偏角(α={a:F2})");
            }
            float peakY = Mathf.Abs(PacejkaTyre.Lateral(0.16f, fz, mu, 12f, 1.7f, 0.9f));
            float tailY = Mathf.Abs(PacejkaTyre.Lateral(1.2f, fz, mu, 12f, 1.7f, 0.9f));
            Assert.That(peakY, Is.GreaterThan(tailY), "侧偏峰值后应衰减");

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

        [Test]
        public void CarSpec_Parameters_Match_Doc_Tables()
        {
            var sct = CarSpec.CreateSlash();
            // 文档 §2.1 参数表:568×296×193mm,轴距 324mm,轮径 ≈105mm(半径 52.5mm)
            Assert.That(sct.length, Is.EqualTo(0.568f), "Slash 全长 568mm");
            Assert.That(sct.width, Is.EqualTo(0.296f), "Slash 全宽 296mm");
            Assert.That(sct.height, Is.EqualTo(0.193f), "Slash 全高 193mm");
            Assert.That(sct.wheelbase, Is.EqualTo(0.324f), "Slash 轴距 324mm");
            Assert.That(sct.wheelRadius, Is.InRange(0.050f, 0.055f), "轮径 100–110mm");
            // §2.1 传动:Kv 3500、齿比 7.32、效率 85–92%
            Assert.That(sct.motorKv, Is.EqualTo(3500f));
            Assert.That(sct.gearRatio, Is.InRange(7.0f, 7.5f));
            Assert.That(sct.drivetrainEff, Is.InRange(0.85f, 0.92f));
            // §3.2:质量 RTR 2.64kg、CG 高 55–65mm
            Assert.That(sct.mass, Is.InRange(2.5f, 2.8f));
            Assert.That(sct.cgHeight, Is.InRange(0.055f, 0.065f));
            // §3.5 电池:2S 4200mAh
            Assert.That(sct.cells, Is.EqualTo(2));
            Assert.That(sct.capacityAh, Is.EqualTo(4.2f));
            // §8 舵机:60° 行程 0.06–0.12s
            Assert.That(sct.servoTau, Is.InRange(0.06f, 0.12f));

            var mt = CarSpec.CreateKraton();
            // 文档 §2.2:594×461×195mm,轴距 378mm,轮径 165mm
            Assert.That(mt.length, Is.EqualTo(0.594f));
            Assert.That(mt.width, Is.EqualTo(0.461f));
            Assert.That(mt.wheelbase, Is.EqualTo(0.378f));
            Assert.That(mt.wheelRadius, Is.InRange(0.078f, 0.085f));
            Assert.That(mt.mass, Is.InRange(4.5f, 5.2f), "Kraton RTR ≈4.85kg");
            Assert.That(mt.motorKv, Is.EqualTo(2050f));
            Assert.That(mt.cells, Is.EqualTo(6), "6S");
            Assert.That(mt.capacityAh, Is.EqualTo(5.0f));
            // §5 玩法:离地 ≥33mm 才能骑上 4.5cm 路沿
            Assert.That(mt.chassisClearance, Is.GreaterThanOrEqualTo(0.033f), "离地不足骑不上路沿(§5)");
            // §3.1 胎质差异:Kraton 越野胎滚阻 = 公路胎 1.8–2.2 倍;铺装横向抓地 −20~25%
            Assert.That(sct.crrMult, Is.EqualTo(1.0f), "Slash 公路胎滚阻基线 1.0");
            Assert.That(mt.crrMult, Is.InRange(1.8f, 2.2f), "越野胎滚阻应为公路胎 1.8–2.2 倍(§3.1)");
            Assert.That(mt.latGripOnRoad, Is.InRange(0.73f, 0.80f), "越野胎铺装抓地 −20~25%");
            Assert.That(mt.latGripOffRoad, Is.GreaterThan(mt.latGripOnRoad), "越野胎软路面应有相对优势");
            // §2.2:MT 峰值扭矩指引 ≥2.5× SCT(工程取 3.5×,README 六·偏差 6)
            Assert.That(mt.torquePeakNm, Is.GreaterThanOrEqualTo(2.5f * sct.torquePeakNm), "MT 峰值扭矩应 ≥2.5× SCT");
        }

        [Test]
        public void Suspension_Static_Compression_Half_Travel()
        {
            // 文档 §3.3:悬挂目标位置 0.5(静载压缩 = 50% 行程)。文档同时给出
            // k=28000–32000 N/m:2.64kg 车身下静压仅 0.21mm,与目标位置 0.5 及
            // §7③ 侧倾 3–5° 互斥(README 六·偏差 1)——k 按 mg/4 ÷ (0.5×行程) 反推。
            var sct = CarSpec.CreateSlash();
            Assert.That(sct.mass * 9.81f / (4f * sct.springStiffness) / sct.suspensionTravel,
                Is.InRange(0.45f, 0.55f), "Slash 静载压缩应为行程的 ≈50%(§3.3 TargetPosition 0.5)");
            var mt = CarSpec.CreateKraton();
            Assert.That(mt.mass * 9.81f / (4f * mt.springStiffness) / mt.suspensionTravel,
                Is.InRange(0.45f, 0.55f), "Kraton 静载压缩应为行程的 ≈50%");
            // 阻尼比 ζ≈0.7:既不欠阻尼振荡,也不过阻尼僵死
            foreach (var s in new[] { sct, mt })
            {
                float zeta = s.damperRate / (2f * Mathf.Sqrt(s.springStiffness * s.mass / 4f));
                Assert.That(zeta, Is.InRange(0.3f, 1.1f), $"阻尼比 ζ={zeta:F2} 应落在 0.3–1.1");
            }
        }

        [Test]
        public void Battery_Soc_Hook_Enters_Low_Voltage_State()
        {
            var b = new MotorAndBattery(CarSpec.CreateSlash());
            b.SetSocForTest(0.12f);   // 测试钩子:跳过模拟放电,直达低电量(§7④ 验收)
            Assert.That(b.soc, Is.EqualTo(0.12f));
            Assert.That(b.packVoltage, Is.EqualTo(b.OcvFromSoc(0.12f)).Within(1e-3f), "钩子应同步开路电压");
            b.Update(1f, 15000f, 0.01f);   // 下一步 LVC 逻辑按新电压计算
            Assert.That(b.powerLimit, Is.EqualTo(0.5f), "SOC 12%(≈3.37V/芯)应立即限功 50%");
            Assert.That(b.lvcCut, Is.False, "未到 3.2V/芯不应切断");
            b.SetSocForTest(0.02f);
            float t = b.Update(1f, 15000f, 0.01f);
            Assert.That(b.lvcCut, "SOC 2%(≈3.18V/芯,带载 sag 后 <3.2)应触发 LVC 切断");
            Assert.That(t, Is.EqualTo(0f), "LVC 切断后扭矩应为 0");
        }

        [Test]
        public void NoiseFactory_Generates_Deterministic_Clips()
        {
            // 文档 §4:三层程序化音频(电机谐波音调/齿轮白噪/胎噪棕噪),44.1kHz 确定性合成
            var white = NoiseFactory.White("TestWhite", 0.5f);
            var brown = NoiseFactory.Brown("TestBrown", 0.5f);
            var tone  = NoiseFactory.Tone("TestTone", 1f, 220f, 14, 1.4f);
            Assert.That(white.frequency, Is.EqualTo(44100), "白噪采样率 44.1kHz");
            Assert.That(brown.frequency, Is.EqualTo(44100));
            Assert.That(tone.frequency, Is.EqualTo(44100));
            Assert.That(white.samples, Is.EqualTo(22050), "0.5s 白噪长度");

            // 白噪:均匀(−0.5,0.5) → RMS ≈ 0.289;固定种子 → 两次生成逐样本一致
            var d = new float[white.samples];
            white.GetData(d, 0);
            float sum = 0f;
            foreach (var v in d) sum += v * v;
            float rms = Mathf.Sqrt(sum / d.Length);
            Assert.That(rms, Is.InRange(0.24f, 0.33f), $"白噪 RMS {rms:F3} ≈ 0.289(均匀分布)");
            var w2 = NoiseFactory.White("TestWhite2", 0.5f);
            var d2 = new float[w2.samples];
            w2.GetData(d2, 0);
            CollectionAssert.AreEqual(d2, d, "固定种子 → 每次生成逐样本一致");

            // 棕噪:泄漏积分后归一化峰值 0.8
            var bd = new float[brown.samples];
            brown.GetData(bd, 0);
            float bPeak = 0f;
            foreach (var v in bd) bPeak = Mathf.Max(bPeak, Mathf.Abs(v));
            Assert.That(bPeak, Is.InRange(0.79f, 0.81f), $"棕噪峰值 {bPeak:F3} 应归一化到 0.8");

            // 音调:1s 220Hz → 整周期 220 个 → 过零 ≈440 次;峰值归一化 0.85;首尾近似连续(无缝循环)
            var td = new float[tone.samples];
            tone.GetData(td, 0);
            int crossings = 0;
            float tPeak = 0f;
            for (int i = 1; i < td.Length; i++)
            {
                if ((td[i - 1] > 0f && td[i] <= 0f) || (td[i - 1] < 0f && td[i] >= 0f)) crossings++;
                tPeak = Mathf.Max(tPeak, Mathf.Abs(td[i]));
            }
            Assert.That(crossings, Is.InRange(390, 490), $"220Hz 音调过零 {crossings} 次(理论 440)");
            Assert.That(tPeak, Is.InRange(0.84f, 0.86f), $"音调峰值 {tPeak:F3} 应归一化到 0.85");
            Assert.That(Mathf.Abs(td[0] - td[td.Length - 1]), Is.LessThan(0.25f), "循环首尾应近似连续(无缝循环)");
        }
    }
}