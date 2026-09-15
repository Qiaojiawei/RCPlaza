using System.Text;
using RCPlaza.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace RCPlaza.UI
{
    /// <summary>
    /// HUD(设计文档 §6.4 UI):UGUI 纯代码构建,无 EventSystem / GraphicRaycaster(纯显示,零输入依赖)。
    /// 中文动态字体:Font.CreateDynamicFontFromOSFont(微软雅黑 → 黑体 → LegacyRuntime 兜底)。
    /// 布局:左上 车型+速度 / 右上 电池电压·电量·低压警示 / 左下 油门·转向·电机
    /// / 底中 键位提示 / 中心 帮助面板(F1)/ 左侧 物理调试面板(F2)/ 测试结果横幅(F3/F4)。
    /// 分辨率适配:CanvasScaler 1280×720 等比缩放。
    /// </summary>
    public class HudController : MonoBehaviour
    {
        RcCarController car;
        Font font;

        Text carNameText, speedText, battText, socText, warnText, ctrlText;
        Text helpText, debugText, testText;
        GameObject helpPanel, debugPanel;

        bool helpVisible, debugVisible;
        float fpsRate; int fpsFrames; float fpsTimer;
        readonly StringBuilder sb = new StringBuilder(512);

        public void SetCar(RcCarController c)
        {
            car = c;
            if (carNameText != null && c != null) carNameText.text = c.Spec.displayName;
        }

        public void ToggleHelp()  { helpVisible = !helpVisible;   helpPanel.SetActive(helpVisible); }
        public void ToggleDebug() { debugVisible = !debugVisible; debugPanel.SetActive(debugVisible); }

        void Awake()
        {
            font = GetFont();
            BuildUi();
            helpPanel.SetActive(false);
            debugPanel.SetActive(false);
        }

        static Font GetFont()
        {
            string[] names = { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "SimSun" };
            foreach (var n in names)
            {
                Font f = Font.CreateDynamicFontFromOSFont(n, 16);
                if (f != null) return f;
            }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // ---------- 构建 ----------
        void BuildUi()
        {
            var canvasGo = new GameObject("HUD_Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            RectTransform cv = (RectTransform)canvasGo.transform;

            // 左上:车型 + 速度
            AddPanel(cv, new Vector2(0f, 1f), new Vector2(14f, -10f), new Vector2(340f, 82f), new Color(0f, 0f, 0f, 0.45f));
            carNameText = AddText(cv, new Vector2(0f, 1f), new Vector2(30f, -24f), 20, "", Color.white, true);
            speedText = AddText(cv, new Vector2(0f, 1f), new Vector2(30f, -54f), 34, "0.0 km/h", new Color(1f, 0.86f, 0.45f), true);

            // 右上:电池
            AddPanel(cv, new Vector2(1f, 1f), new Vector2(-14f, -10f), new Vector2(300f, 92f), new Color(0f, 0f, 0f, 0.45f));
            battText = AddText(cv, new Vector2(1f, 1f), new Vector2(-30f, -24f), 16, "", Color.white, false);
            socText = AddText(cv, new Vector2(1f, 1f), new Vector2(-30f, -50f), 24, "", new Color(0.55f, 0.95f, 0.55f), true);
            warnText = AddText(cv, new Vector2(1f, 1f), new Vector2(-30f, -78f), 15, "", new Color(1f, 0.30f, 0.22f), true);

            // 左下:控制状态
            AddPanel(cv, new Vector2(0f, 0f), new Vector2(14f, 10f), new Vector2(460f, 42f), new Color(0f, 0f, 0f, 0.45f));
            ctrlText = AddText(cv, new Vector2(0f, 0f), new Vector2(30f, 31f), 17, "", new Color(0.85f, 0.90f, 0.95f), false);

            // 底中:键位提示
            AddPanel(cv, new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(870f, 24f), new Color(0f, 0f, 0f, 0.30f));
            AddText(cv, new Vector2(0.5f, 0f), new Vector2(0f, 24f), 14,
                "WASD / 方向键 行驶 · SPACE 手刹 · TAB 换车 · R 复位 · C 视角 · F1 帮助 · F2 调试 · F3 滑行测试 · F4 加速测试",
                new Color(0.78f, 0.81f, 0.84f), false, TextAnchor.LowerCenter);

            // 帮助面板(F1)
            helpPanel = AddPanel(cv, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(700f, 460f), new Color(0f, 0f, 0f, 0.84f));
            helpText = AddText((RectTransform)helpPanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 6f), 19,
                "================ 操 作 指 南 ================\n\n"
                + "W / ↑  前进          S / ↓  刹车(前进时)→ 倒车\n"
                + "A D / ← →  转向        SPACE  手刹\n"
                + "TAB  切换车型(短卡 SCT ⇄ 大脚 MT)\n"
                + "R    复位到出生点      C  视角(追尾 ⇄ 车顶 FPV)\n\n"
                + "------------ 文 档 验 收 测 试 ------------\n"
                + "F3  滑行测试:自动加速到 1 m/s 后收油,\n"
                + "    量取滑行距离(文档预期 3–6 m)\n"
                + "F4  加速测试:静止全油门 0→40/70 km/h 计时\n"
                + "    (SCT 预期 2.5–3.5 s,MT 预期约 2.7 s)\n\n"
                + "---------------- 提 示 ----------------\n"
                + "电池放电为演示加速(DoubleTime 2x),电压低于\n"
                + "3.4V/芯 自动限功 50%,低于 3.2V/芯 切断动力,\n"
                + "右上角会出现低压警示。路沿 4.5cm:SCT 需助跑,\n"
                + "MT 可直接骑上。F2 面板可核对每轮力/滑移/电压。",
                Color.white, false, TextAnchor.MiddleCenter);

            // 调试面板(F2)
            debugPanel = AddPanel(cv, new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(480f, 600f), new Color(0f, 0f, 0f, 0.66f));
            debugText = AddText((RectTransform)debugPanel.transform, new Vector2(0f, 1f), new Vector2(18f, -14f), 15, "", Color.white, false);

            // 测试结果横幅
            testText = AddText(cv, new Vector2(0.5f, 0f), new Vector2(0f, 110f), 24, "",
                new Color(1f, 0.90f, 0.30f), true, TextAnchor.LowerCenter);
            testText.enabled = false;
        }

        GameObject AddPanel(RectTransform parent, Vector2 anchor, Vector2 offset, Vector2 size, Color color)
        {
            var go = new GameObject("Panel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = offset;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return go;
        }

        Text AddText(RectTransform parent, Vector2 anchor, Vector2 offset, int size, string text,
                     Color color, bool bold, TextAnchor align = TextAnchor.UpperLeft)
        {
            var go = new GameObject("Txt", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = offset;
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.text = text;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            if (bold) t.fontStyle = FontStyle.Bold;
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.7f);
            sh.effectDistance = new Vector2(1f, -1f);
            return t;
        }

        // ---------- 每帧刷新 ----------
        void Update()
        {
            FpsCount();
            if (car == null) return;

            var b = car.Battery;
            speedText.text = (car.SpeedMps * 3.6f).ToString("F1") + " km/h";
            battText.text = "电池 " + b.packVoltage.ToString("F2") + " V(ocv " + b.ocv.ToString("F2") + " V)";
            socText.text = "电量 " + (b.soc * 100f).ToString("F0") + "%";

            if (b.lvcCut)              warnText.text = "低压保护 · 动力已切断";
            else if (b.powerLimit < 1f) warnText.text = "低压保护 · 功率降至 50%";
            else                        warnText.text = "";
            warnText.enabled = b.powerLimit < 1f || b.lvcCut;

            float steerDeg = car.Spec.maxSteerDeg * car.SteerCmd;
            ctrlText.text = string.Format("油门 {0}%    转向 {1:0}°    电机 {2:0} rpm   {3}",
                (car.ThrottleCmd * 100f).ToString("F0"),
                steerDeg, car.MotorRpm,
                car.BrakeInput > 0.001f ? "[刹车]" : "");

            if (debugVisible) RefreshDebug();

            testText.enabled = !string.IsNullOrEmpty(car.testResult);
            if (testText.enabled) testText.text = car.testResult;
        }

        void RefreshDebug()
        {
            var b = car.Battery;
            sb.Length = 0;
            sb.Append("FPS ").Append(fpsRate.ToString("F0"))
              .Append(" · 速度 ").Append(car.SpeedMps.ToString("F1")).Append(" m/s\n");
            sb.Append("车身 侧倾 ").Append(car.BodyRollDeg.ToString("+0.0;-0.0;0")).Append("°")
              .Append("  俯仰 ").Append(car.BodyPitchDeg.ToString("+0.0;-0.0;0")).Append("°")
              .Append("  平滑路面 ").Append(car.OnSmoothSurface ? "是" : "否").Append('\n');
            sb.Append("电机 ").Append(car.MotorRpm.ToString("F0")).Append(" rpm")
              .Append(" · 扭矩 ").Append(b.lastTorque.ToString("F3")).Append(" N·m")
              .Append(" · 限功 ").Append(b.powerLimit.ToString("F2")).Append('\n');
            sb.Append("电池 ").Append(b.packVoltage.ToString("F2")).Append(" V")
              .Append(" · 电流 ").Append(b.currentA.ToString("F1")).Append(" A")
              .Append(" · SOC ").Append((b.soc * 100f).ToString("F0")).Append("%\n\n");

            string[] wn = { "左前", "右前", "左后", "右后" };
            string[] wn2 = { "●", "○" };
            for (int i = 0; i < 4; i++)
            {
                var w = car.Wheels[i];
                sb.Append(wn[i]).Append(w.grounded ? wn2[0] : wn2[1])
                  .Append(" Fz ").Append(w.fz.ToString("F1"))
                  .Append("  Fx ").Append(w.fx.ToString("F1"))
                  .Append("  Fy ").Append(w.fy.ToString("F1"))
                  .Append("  λ ").Append(w.slipRatio.ToString("+0.00;-0.00;0"))
                  .Append("  α ").Append((w.slipAngleRad * Mathf.Rad2Deg).ToString("+0.0;-0.0;0")).Append("°")
                  .Append("  ω ").Append(w.omega.ToString("F0")).Append('\n');
            }
            debugText.text = sb.ToString();
        }

        void FpsCount()
        {
            fpsFrames++;
            fpsTimer += Time.unscaledDeltaTime;
            if (fpsTimer >= 0.5f)
            {
                fpsRate = fpsFrames / fpsTimer;
                fpsFrames = 0;
                fpsTimer = 0f;
            }
        }
    }
}