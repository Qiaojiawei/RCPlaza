using RCPlaza.Cam;
using RCPlaza.Core;
using RCPlaza.Sim;
using RCPlaza.UI;
using RCPlaza.Audio;
using RCPlaza.World;
using UnityEngine;

namespace RCPlaza.Root
{
    /// <summary>
    /// 入口 + 会话主机(交付"零场景依赖"的唯一保证,README 双保险的一半):
    /// [RuntimeInitializeOnLoadMethod(AfterSceneLoad)] 在任何场景加载后自举,
    /// 静态幂等标志防重复 → 无论打开哪个场景(含编辑器任意空场景)按 Play 都完整运行。
    /// GameHost 挂 DontDestroyOnLoad,世界/车辆/相机/UI/音频全部建为其子物体。
    /// </summary>
    public static class GameBootstrap
    {
        static bool _booted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (_booted) return;   // 场景重载防重复(编辑器域重载时静态自动清零,行为正确)
            _booted = true;

            // 性能预算(设计文档 §6):100Hz 物理 + 60fps
            Time.fixedDeltaTime = PlazaConfig.FixedDt;
            Time.maximumDeltaTime = PlazaConfig.MaxAllowedDt;
            Time.maximumParticleDeltaTime = 0.033f;
            Application.targetFrameRate = PlazaConfig.TargetFPS;
            QualitySettings.vSyncCount = 1;
            Physics.defaultContactOffset = 0.005f;

            var go = new GameObject("RCPlaza_GameHost");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<GameHost>();
        }
    }

    /// <summary>会话主机:场景/车辆/相机/音频/UI 的总装配与主循环。</summary>
    public class GameHost : MonoBehaviour
    {
        /// <summary>全局唯一实例(车辆 FixedUpdate 借此取输入)。</summary>
        public static GameHost I { get; private set; }
        public RcInput PlayerInput { get; } = new RcInput();

        /// <summary>全部车辆(自动化测试/外部工具用)。</summary>
        public RcCarController[] Cars => cars;
        /// <summary>当前被控车辆。</summary>
        public RcCarController ActiveCar => cars != null ? cars[carIdx] : null;
        /// <summary>测试用:强制切换被控车(等价 Tab,但跳过位置接管)。</summary>
        public void ForceActiveCar(int idx)
        {
            idx = Mathf.Clamp(idx, 0, cars.Length - 1);
            carIdx = idx;
            for (int i = 0; i < cars.Length; i++) cars[i].IsActive = (i == carIdx);
            camScript.SetTarget(cars[carIdx]);
            hud.SetCar(cars[carIdx]);
        }

        RcCarController[] cars;
        int carIdx;
        ChaseCamera camScript;
        HudController hud;
        RcAudioEngine audioEngine;

        // ---------- F3/F4 测试状态机 ----------
        enum TestPhase { None, Accel, Coast }
        TestPhase testPhase;
        bool testIsLaunch;          // true=F4 全油门加速;false=F3 滑行
        Vector3 coastStart;
        float testClock;
        float resultUntil;          // 结果横幅自动消失时刻

        void Awake()
        {
            I = this;
            BuildLights();
            var camGo = new GameObject("ChaseCamera");
            camGo.transform.SetParent(transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; // 无天空盒(设计文档 §5 街景纯色天光)
            cam.backgroundColor = new Color(0.55f, 0.63f, 0.74f);
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 400f;
            camGo.AddComponent<AudioListener>();
            camScript = camGo.AddComponent<ChaseCamera>();

            PlazaBuilder.Build(transform);

            var audioGo = new GameObject("RcAudio");
            audioGo.transform.SetParent(transform, false);
            audioEngine = audioGo.AddComponent<RcAudioEngine>();

            var hudGo = new GameObject("HUD");
            hudGo.transform.SetParent(transform, false);
            hud = hudGo.AddComponent<HudController>();

            SpawnCars();
        }

        void OnDestroy() { if (I == this) I = null; }

        void BuildLights()
        {
            // 鲜艳街景光:方向光 + 三天光,无天空盒,相机 SolidColor(视觉风格按文档 §5 氛围)
            var sunGo = new GameObject("Sun_Visual");
            sunGo.transform.SetParent(transform, false);
            sunGo.transform.rotation = Quaternion.Euler(44f, -28f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.965f, 0.90f);
            sun.intensity = 1.05f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sun.shadowBias = 0.02f;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.63f, 0.74f);
            RenderSettings.ambientEquatorColor = new Color(0.48f, 0.50f, 0.53f);
            RenderSettings.ambientGroundColor = new Color(0.27f, 0.27f, 0.29f);
            RenderSettings.fog = false;
        }

        void SpawnCars()
        {
            var slash = CarSpec.CreateSlash();
            var kraton = CarSpec.CreateKraton();

            cars = new RcCarController[2];
            cars[0] = RcCarController.Create(slash,
                new Vector3(-3f, slash.cgHeight + 0.06f, 5f), Quaternion.identity, "RC_短卡SCT");
            cars[1] = RcCarController.Create(kraton,
                new Vector3(3f, kraton.cgHeight + 0.06f, 5f), Quaternion.identity, "RC_大脚MT");
            foreach (var t in cars) t.transform.SetParent(transform, true);

            cars[0].IsActive = true;
            cars[1].IsActive = false;
            carIdx = 0;

            camScript.SetTarget(cars[0]);
            hud.SetCar(cars[0]);
        }

        void Update()
        {
            var car = cars[carIdx];
            float dt = Time.unscaledDeltaTime;

            PlayerInput.Poll(dt, car.Spec.servoTau);

            if (PlayerInput.PressedSwitchCar) SwitchCar();
            if (PlayerInput.PressedReset)     { AbortTest(); car.ResetPose(); }
            if (PlayerInput.PressedCamera)     camScript.ToggleMode();
            if (PlayerInput.PressedHelp)       hud.ToggleHelp();
            if (PlayerInput.PressedDebug)      hud.ToggleDebug();
            if (PlayerInput.PressedSlideTest)  StartSlideTest(car);
            if (PlayerInput.PressedAccelTest)  StartAccelTest(car);

            UpdateTest(car, dt);
            audioEngine.UpdateFor(car);

            // 测试结果横幅自动消失
            if (car.testResult != null && Time.unscaledTime > resultUntil)
                car.testResult = null;
        }

        void SwitchCar()
        {
            AbortTest();
            int next = 1 - carIdx;
            cars[next].TakePoseFrom(cars[carIdx]); // 换车 = 控制权交接:新车接管旧车位置
            carIdx = next;
            for (int i = 0; i < 2; i++) cars[i].IsActive = (i == carIdx);
            camScript.SetTarget(cars[carIdx]);
            hud.SetCar(cars[carIdx]);
        }

        // ============================================================
        // F3 滑行测试(设计文档 §7①:1 m/s 收油滑行 3–6 m——解析值 v²/2Crr·g ≈ 4.1m)
        // ============================================================
        void StartSlideTest(RcCarController car)
        {
            if (testPhase != TestPhase.None) return;
            testIsLaunch = false;
            if (car.SpeedMps >= 0.9f) BeginCoast(car);
            else
            {
                testPhase = TestPhase.Accel;
                car.TestThrottleOverride = 0.35f;      // 温和起步到 1 m/s
            }
            testClock = 0f;
        }

        // F4 加速测试(§7②:静止全油门 0→40/70 km/h 计时)
        void StartAccelTest(RcCarController car)
        {
            if (testPhase != TestPhase.None) return;
            if (car.SpeedMps > 0.2f)
            {
                SetResult(car, "加速测试:需先静止(松油门停稳或按 R 复位后重试)");
                return;
            }
            testIsLaunch = true;
            testPhase = TestPhase.Accel;
            car.TestThrottleOverride = 1.0f;
            testClock = 0f;
        }

        void BeginCoast(RcCarController car)
        {
            testPhase = TestPhase.Coast;
            car.TestThrottleOverride = -1f;
            coastStart = car.transform.position;
            testClock = 0f;
        }

        void UpdateTest(RcCarController car, float dt)
        {
            if (testPhase == TestPhase.None) return;
            testClock += dt;

            if (testPhase == TestPhase.Accel)
            {
                if (testIsLaunch)
                {
                    if (car.SpeedMps >= car.Spec.accelTargetMps)
                    {
                        float kmh = car.Spec.accelTargetMps * 3.6f;
                        SetResult(car, string.Format("加速测试 · 0→{0:0} km/h 用时 {1:F2} s(文档预期 2.5–3.5 s)",
                            kmh, testClock));
                        EndTest(car);
                    }
                    else if (testClock > 25f)
                    {
                        SetResult(car, "加速测试:F4 超时(25s 未达目标速度)——LVC 限功或抓地不足?");
                        EndTest(car);
                    }
                }
                else
                {
                    if (car.SpeedMps >= 1.0f) BeginCoast(car);
                    else if (testClock > 20f) { SetResult(car, "滑行测试:未能在 20s 内达到 1 m/s,已中止"); EndTest(car); }
                }
            }
            else // Coast
            {
                if (car.SpeedMps < 0.02f)
                {
                    Vector3 d = car.transform.position - coastStart;
                    float dist = Mathf.Sqrt(d.x * d.x + d.z * d.z);
                    SetResult(car, string.Format("滑行测试 · 1 m/s 收油滑行 {0:F1} m(文档预期 3–6 m)", dist));
                    EndTest(car);
                }
                else if (testClock > 30f) { SetResult(car, "滑行测试:30s 未停稳,已中止"); EndTest(car); }
            }
        }

        void EndTest(RcCarController car)
        {
            testPhase = TestPhase.None;
            car.TestThrottleOverride = -1f;
        }

        void AbortTest()
        {
            if (testPhase == TestPhase.None && cars != null && cars[0].TestThrottleOverride < 0f
                && cars[1].TestThrottleOverride < 0f) return;
            testPhase = TestPhase.None;
            if (cars != null)
                foreach (var c in cars) c.TestThrottleOverride = -1f;
        }

        void SetResult(RcCarController car, string s)
        {
            car.testResult = s;
            resultUntil = Time.unscaledTime + 10f;
        }
    }
}