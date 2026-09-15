using RCPlaza.Core;
using RCPlaza.Root;
using RCPlaza.World;
using UnityEngine;

namespace RCPlaza.Sim
{
    /// <summary>
    /// 整车控制器(设计文档 §3「物理仿真模型」集成):
    /// FixedUpdate 数据流 = 输入(油门/转向/伺服滞后)→ 四轮 raycast 悬挂
    /// → Pacejka 轮胎力(作用于接触点 → 自然俯仰/侧倾/重量转移)→ 轮 ω 积分
    /// → 防倾杆 + 空气阻力。品牌无关的原语拼装车身(文档 §8 诚实边界)。
    /// 物理稳定性措施:隐式阻尼、ω/力 clamp、CCD、Interpolate、静置休眠。
    /// </summary>
    public class RcCarController : MonoBehaviour
    {
        [SerializeField] CarSpec spec;
        [SerializeField] MotorAndBattery battery;
        [SerializeField] WheelUnit[] wheels;

        Rigidbody rb;
        int rayMask;

        // ---------- 控制状态 ----------
        /// <summary>测试油门覆盖:−1 = 跟随玩家输入;0..1 = 强制油门(F3 滑行 / F4 加速测试)。</summary>
        [System.NonSerialized] public float TestThrottleOverride = -1f;
        /// <summary>是否当前被控车辆(Tab 换车后旧车输入归零、自然静置休眠)。</summary>
        [System.NonSerialized] public bool IsActive = true;
        float escThrottle;   // 电调功率爬升后的实际开度
        float idleTime;
        /// <summary>F3/F4 测试结果文本(HUD 显示,由 GameHost 写入)。</summary>
        [System.NonSerialized] public string testResult;

        // ---------- 只读输出(相机 / 音频 / HUD 消费) ----------
        public float SpeedMps { get; private set; }
        public float MotorRpm { get; private set; }
        public float ThrottleCmd { get; private set; }
        public float SteerCmd { get; private set; }
        public float BrakeInput { get; private set; }
        public float BodyRollDeg { get; private set; }
        public float BodyPitchDeg { get; private set; }
        public bool OnSmoothSurface { get; private set; }
        public CarSpec Spec => spec;
        public MotorAndBattery Battery => battery;
        public WheelUnit[] Wheels => wheels;
        public Rigidbody Body => rb;

        // ============================================================
        // 创建
        // ============================================================
        public static RcCarController Create(CarSpec spec, Vector3 pos, Quaternion rot, string goName)
        {
            var go = new GameObject(goName);
            go.transform.SetPositionAndRotation(pos, rot);
            var car = go.AddComponent<RcCarController>();
            car.Build(spec);
            return car;
        }

        void Build(CarSpec spec_)
        {
            spec = spec_;
            SetLayerRecursive(gameObject, PlazaConfig.IgnoreRaycastLayer);
            rayMask = Physics.DefaultRaycastLayers; // 排除 Ignore Raycast(车身),其它全部参与

            rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = spec.mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;      // 100Hz 物理 → 60fps 渲染去微抖
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // CCD:极速不穿墙(文档 §6.1)
            rb.maxAngularVelocity = 20f;                                // 限制极端旋转防物理爆炸
            rb.solverIterations = 8;
            rb.solverVelocityIterations = 2;
            rb.sleepThreshold = 0.005f;
            rb.angularDrag = 0.15f;
            rb.centerOfMass = Vector3.zero;                             // 车根原点即重心(cgHeight 处于静态高度)

            // 底盘唯一碰撞体:底部高度 = chassisClearance,
            // 决定能否骑上路沿(Slash 3.2cm < 路沿 4.5cm 需角度/速度;Kraton 5.5cm 直接上)
            var bc = gameObject.AddComponent<BoxCollider>();
            float collH = 0.10f;
            float bottom = spec.chassisClearance - spec.cgHeight;
            bc.center = new Vector3(0f, bottom + collH * 0.5f, 0f);
            bc.size = new Vector3(spec.length * 0.80f, collH, spec.width * 0.80f);

            // 四轮(顺序:FL, FR, RL, RR)
            wheels = new WheelUnit[4];
            var anchors = new[]
            {
                new Vector3(-spec.trackFront / 2f, spec.wheelRadius - spec.cgHeight,  spec.wheelbase / 2f),
                new Vector3( spec.trackFront / 2f, spec.wheelRadius - spec.cgHeight,  spec.wheelbase / 2f),
                new Vector3(-spec.trackRear  / 2f, spec.wheelRadius - spec.cgHeight, -spec.wheelbase / 2f),
                new Vector3( spec.trackRear  / 2f, spec.wheelRadius - spec.cgHeight, -spec.wheelbase / 2f),
            };
            for (int i = 0; i < 4; i++)
            {
                wheels[i] = new WheelUnit();
                wheels[i].Init(spec, anchors[i], i < 2);
            }

            battery = new MotorAndBattery(spec);
            BuildVisualsAndWheels();

            SpawnPos = transform.position;
            SpawnRot = transform.rotation;
        }

        // ---------- 视觉拼装(全部 GameObject 原语,共享材质,品牌无关) ----------
        void BuildVisualsAndWheels()
        {
            bool mt = spec.wheelRadius >= 0.07f; // 大脚胎 → MT 造型;否则短卡造型

            Material bodyM   = MaterialFactory.Colored(spec.bodyColor, 0.32f);
            Material accentM = MaterialFactory.Colored(spec.accentColor, 0.30f);
            Material chassisM = MaterialFactory.Colored(spec.chassisColor, 0.25f);
            Material rimM    = MaterialFactory.Colored(spec.rimColor, 0.45f);
            Material tireM   = MaterialFactory.Colored(spec.tireColor, 0.06f);

            Transform root = transform;

            // 底盘板 + 前后护杠
            Box("Chassis", root, new Vector3(spec.length * 0.75f, 0.012f, spec.width * 0.70f),
                new Vector3(0f, spec.chassisClearance - spec.cgHeight + 0.006f, 0f), chassisM);
            Box("FrontBumper", root, new Vector3(0.05f, 0.045f, spec.width * 0.80f),
                new Vector3(0f, 0.012f - spec.cgHeight, spec.length * 0.30f), chassisM);
            Box("RearBumper", root, new Vector3(0.05f, 0.045f, spec.width * 0.80f),
                new Vector3(0f, 0.012f - spec.cgHeight, -spec.length * 0.30f), chassisM);

            if (!mt)
            {
                // ---- 短卡 SCT:皮卡壳 + 车顶 + 后斗架 + 尾翼 ----
                Box("BodyShell", root, new Vector3(spec.length * 0.72f, 0.085f, spec.width * 0.78f),
                    new Vector3(0f, 0.035f, -0.005f), bodyM);
                Box("Cab", root, new Vector3(spec.length * 0.20f, 0.060f, spec.width * 0.70f),
                    new Vector3(0f, 0.062f, spec.length * 0.115f), accentM);
                Box("BedBarL", root, new Vector3(0.010f, 0.010f, spec.length * 0.30f),
                    new Vector3(-spec.width * 0.30f, 0.042f, -spec.length * 0.14f), chassisM);
                Box("BedBarR", root, new Vector3(0.010f, 0.010f, spec.length * 0.30f),
                    new Vector3(spec.width * 0.30f, 0.042f, -spec.length * 0.14f), chassisM);
                Box("Wing", root, new Vector3(spec.width * 0.62f, 0.006f, 0.045f),
                    new Vector3(0f, 0.108f, -spec.length * 0.28f), accentM);
                Box("WingPostL", root, new Vector3(0.008f, 0.030f, 0.008f),
                    new Vector3(-spec.width * 0.20f, 0.090f, -spec.length * 0.28f), chassisM);
                Box("WingPostR", root, new Vector3(0.008f, 0.030f, 0.008f),
                    new Vector3(spec.width * 0.20f, 0.090f, -spec.length * 0.28f), chassisM);
            }
            else
            {
                // ---- 大脚 MT:高耸车体 + 防滚笼 + 侧踏杆 + 大尾翼 ----
                Box("BodyTub", root, new Vector3(spec.length * 0.68f, 0.100f, spec.width * 0.55f),
                    new Vector3(0f, 0.055f, 0f), bodyM);
                Box("Cab", root, new Vector3(spec.length * 0.22f, 0.070f, spec.width * 0.50f),
                    new Vector3(0f, 0.090f, spec.length * 0.10f), accentM);
                Box("RoofBarF", root, new Vector3(0.012f, 0.012f, spec.width * 0.52f),
                    new Vector3(0f, 0.152f, spec.length * 0.05f), chassisM);
                Box("RoofBarR", root, new Vector3(0.012f, 0.012f, spec.width * 0.52f),
                    new Vector3(0f, 0.130f, -spec.length * 0.15f), chassisM);
                Box("NerfL", root, new Vector3(spec.length * 0.62f, 0.025f, 0.014f),
                    new Vector3(-spec.width * 0.40f, 0.030f, 0f), accentM);
                Box("NerfR", root, new Vector3(spec.length * 0.62f, 0.025f, 0.014f),
                    new Vector3(spec.width * 0.40f, 0.030f, 0f), accentM);
                Box("Wing", root, new Vector3(spec.width * 0.80f, 0.006f, 0.050f),
                    new Vector3(0f, 0.135f, -spec.length * 0.27f), bodyM);
            }

            // 轮视觉:轮根节点位于挂点,内挂胎 + 轮毂圆柱(无碰撞体:地面交互全部由悬挂射线完成)
            string[] wn = { "WheelFL", "WheelFR", "WheelRL", "WheelRR" };
            for (int i = 0; i < 4; i++)
            {
                var wheelGO = new GameObject(wn[i]);
                wheelGO.transform.SetParent(root, false);
                wheelGO.transform.localPosition = wheels[i].anchorLocal;
                wheels[i].visualRoot = wheelGO.transform;

                float r = spec.wheelRadius, w = spec.tireWidth;
                var tire = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(tire.GetComponent<Collider>());
                tire.name = "Tire";
                tire.transform.SetParent(wheelGO.transform, false);
                tire.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                tire.transform.localScale = new Vector3(r * 2f, w * 0.5f, r * 2f);
                tire.GetComponent<Renderer>().sharedMaterial = tireM;

                var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(rim.GetComponent<Collider>());
                rim.name = "Rim";
                rim.transform.SetParent(wheelGO.transform, false);
                rim.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                rim.transform.localScale = new Vector3(r * 1.24f, w * 0.54f, r * 1.24f);
                rim.GetComponent<Renderer>().sharedMaterial = rimM;
            }
        }

        GameObject Box(string name, Transform parent, Vector3 scale, Vector3 localPos, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.Destroy(go.GetComponent<BoxCollider>()); // 装饰件不与世界碰撞:整车仅一个底盘碰撞体
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
        }

        // ============================================================
        // 物理步(100Hz)
        // ============================================================
        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            GameHost host = GameHost.I;

            float throttleCmd = 0f, steerCmd = 0f;
            bool handbrake = false;
            if (host != null && IsActive)
            {
                throttleCmd = TestThrottleOverride >= 0f ? TestThrottleOverride : host.PlayerInput.ThrottleRaw;
                steerCmd = host.PlayerInput.SteerCurrent; // 已含舵机滞后(文档 §8)
                handbrake = host.PlayerInput.Handbrake;
            }
            throttleCmd = Mathf.Clamp(throttleCmd, -1f, 1f);
            ThrottleCmd = throttleCmd;
            SteerCmd = steerCmd;

            // 电调功率爬升(软启动滤波)
            float kEsc = 1f - Mathf.Exp(-dt / Mathf.Max(spec.throttleTau, 1e-3f));
            escThrottle += (throttleCmd - escThrottle) * kEsc;

            // 静置休眠判定(文档 §6.1):静止 + 无油门 ≥1s → 暂停施力并令刚体入睡(冻结姿态)
            bool wantsIdle = rb.velocity.sqrMagnitude < 1e-4f
                          && rb.angularVelocity.sqrMagnitude < 1e-4f
                          && Mathf.Abs(throttleCmd) < 0.001f;
            idleTime = wantsIdle ? idleTime + dt : 0f;

            if (idleTime > 1.0f)
            {
                // 休眠路径:不施加任何力(持续施力会永久唤醒刚体);
                // 入睡后物理停止积分,车身姿态冻结在悬挂静平衡高度
                rb.Sleep();
                MotorRpm = 0f;
                for (int i = 0; i < 4; i++) wheels[i].omega = 0f;
                UpdateReadouts();
                return;
            }
            rb.WakeUp();

            // 电机转速 = 四轮平均角速度 × 传动比
            float avgOmega = 0.25f * (wheels[0].omega + wheels[1].omega
                                     + wheels[2].omega + wheels[3].omega);
            MotorRpm = Mathf.Abs(avgOmega) * spec.gearRatio * 60f / (Mathf.PI * 2f);

            // 电池 + 电机扭矩(内含电压 sag、LVC 限功/切断)
            float tMotor = battery.Update(Mathf.Abs(escThrottle), MotorRpm, dt);

            // 前进 → 驱动;前进中 S → 制动;低速持续 S → 倒车(30% 功率,真实 ESC 行为)
            float vFwd = Vector3.Dot(rb.velocity, transform.forward);
            float drive = 0f, brake = 0f;
            if (escThrottle > 0.0005f) drive = escThrottle;
            else if (escThrottle < -0.0005f)
            {
                if (vFwd > 0.8f) brake = -escThrottle;
                else drive = escThrottle * 0.3f;
            }
            if (handbrake) brake = Mathf.Max(brake, 1f);
            BrakeInput = brake;

            // 轮上扭矩:电机扭矩 × 总传动比 × 效率 ÷ 4(开放差速 = 各驱动轮等扭矩)
            float wheelTorque = tMotor * spec.drivetrainEff * spec.gearRatio / 4f;
            float torqueSign = drive < 0f ? -1f : 1f;
            float maxOmega = 1.3f * spec.WheelNoLoadOmega(battery.packVoltage);

            bool allGrounded = true;
            int smoothCount = 0;

            for (int i = 0; i < 4; i++)
            {
                WheelUnit w = wheels[i];
                if (w.isFront) w.steerDeg = FrontSteer(i, steerCmd);
                w.UpdateRay(rb, rayMask, dt);

                if (w.grounded)
                {
                    SurfaceParams sp = SurfaceCatalog.Get(w.surface);
                    bool smooth = sp.type == SurfaceType.Brick
                               || sp.type == SurfaceType.Asphalt
                               || sp.type == SurfaceType.Concrete;
                    if (smooth) smoothCount++;

                    float latScale = smooth ? spec.latGripOnRoad : spec.latGripOffRoad;
                    Vector3 force = w.ComputeTyreForces(rb, sp, latScale, w.steerDeg);
                    w.IntegrateSpin(wheelTorque * torqueSign, brake, sp, maxOmega, dt);
                    rb.AddForceAtPosition(force, w.hitPoint, ForceMode.Force);
                }
                else
                {
                    allGrounded = false;
                    w.fz = w.fx = w.fy = 0f;
                    // 离地空转:无地面约束,落地瞬间大滑移 → 真实"轰油落地打滑"
                    w.IntegrateSpin(wheelTorque * torqueSign, brake, default(SurfaceParams), maxOmega, dt);
                }
            }
            OnSmoothSurface = smoothCount >= 2;

            // 防倾杆(每轴):两侧压缩差 → 等大反向竖直力(限制侧倾、力偶不改变重心高度)
            for (int axle = 0; axle < 2; axle++)
            {
                int il = axle * 2, ir = axle * 2 + 1;
                float diff = wheels[il].compression - wheels[ir].compression;
                if (Mathf.Abs(diff) < 1e-5f) continue;
                float f = spec.antiRollBar * diff;
                Vector3 up = rb.transform.up;
                rb.AddForceAtPosition(-f * up, rb.transform.TransformPoint(wheels[il].anchorLocal), ForceMode.Force);
                rb.AddForceAtPosition( f * up, rb.transform.TransformPoint(wheels[ir].anchorLocal), ForceMode.Force);
            }

            // 空气阻力(作用于质心;与电机扭矩衰减共同决定真实极速)
            float v2 = rb.velocity.sqrMagnitude;
            if (v2 > 0.01f)
            {
                rb.AddForce(-rb.velocity * (0.5f * 1.225f * spec.dragCd * spec.dragAreaM2 * Mathf.Sqrt(v2)),
                            ForceMode.Force);
            }

            UpdateReadouts();
        }

        void UpdateReadouts()
        {
            SpeedMps = rb.velocity.magnitude;
            Vector3 right = transform.right;
            Vector3 fwd = transform.forward;
            BodyRollDeg = -Mathf.Asin(Mathf.Clamp(right.y, -1f, 1f)) * Mathf.Rad2Deg;
            BodyPitchDeg = -Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            for (int i = 0; i < 4; i++) wheels[i].UpdateVisual(dt);
        }

        // ---------- Ackermann 转向(内轮转角大于外轮,最小转弯半径 ≈ 轴距/tanδmax ≈ 0.66m) ----------
        float FrontSteer(int i, float steerInput)
        {
            if (Mathf.Abs(steerInput) < 0.001f) return 0f;
            float dMax = spec.maxSteerDeg * steerInput;      // 内侧轮目标角
            float tanD = Mathf.Tan(dMax * Mathf.Deg2Rad);
            float L = spec.wheelbase, T = spec.trackFront;
            float R = L / Mathf.Abs(tanD);
            if (R < T * 0.5f + 0.01f) R = T * 0.5f + 0.01f;
            bool inner = (dMax > 0f) == (i == 1);            // 右转 → 右轮(FR, i=1)为内侧
            float r = inner ? R - T * 0.5f : R + T * 0.5f;
            float d = Mathf.Atan(L / Mathf.Max(r, 0.02f)) * Mathf.Rad2Deg;
            return dMax > 0f ? d : -d;
        }

        // ============================================================
        // 重置 / 姿态
        // ============================================================
        public Vector3 SpawnPos { get; private set; }
        public Quaternion SpawnRot { get; private set; }

        public void ResetPose()
        {
            transform.SetPositionAndRotation(SpawnPos, SpawnRot);
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.WakeUp();
            escThrottle = 0f;
            idleTime = 0f;
            for (int i = 0; i < 4; i++) wheels[i].ResetState();
        }

        /// <summary>车型切换:接管另一台车的位置与朝向。</summary>
        public void TakePoseFrom(RcCarController other)
        {
            transform.SetPositionAndRotation(other.transform.position, other.transform.rotation);
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.WakeUp();
            escThrottle = 0f;
            idleTime = 0f;
            for (int i = 0; i < 4; i++) wheels[i].ResetState();
        }
    }
}