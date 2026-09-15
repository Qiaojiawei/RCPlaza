using RCPlaza.Core;
using RCPlaza.World;
using UnityEngine;

namespace RCPlaza.Sim
{
    /// <summary>
    /// 单轮子系统(设计文档 §3.1–§3.3):
    /// raycast 悬挂(弹簧+阻尼)→ 接触点速度分解 → Pacejka 轮胎力 → 车轮角速度积分。
    /// 非 MonoBehaviour 纯类,由 RcCarController 在 FixedUpdate 中驱动;零 GC 分配。
    ///
    /// 压缩量约定:compression 以"静态悬挂高度"为 0(正值 = 压缩,负值 = 下垂至全伸)。
    /// 静态压缩量 cRest = mg/4k 恰好等于行程的 50%(springStiffness 的推导正是基于此,
    /// 对应文档 §3.3 "Target Position 0.5")。
    /// </summary>
    public class WheelUnit
    {
        // ---------- 构造期配置 ----------
        public Vector3 anchorLocal;      // 轮挂点(车身局部坐标,静态悬挂高度下的轮毂位置)
        public bool isFront;
        public Transform visualRoot;     // 轮视觉根节点(位于挂点,随压缩量上下)

        // ---------- 每步可变状态(读) ----------
        public float steerDeg;           // 当前转向角(Ackermann 分配后)
        public float omega;              // 车轮角速度 rad/s
        public float compression;        // 相对静态高度的压缩量(米)
        public bool grounded;
        public Vector3 hitPoint, hitNormal;
        public SurfaceType surface;

        // ---------- 调试读量 ----------
        public float fz, fx, fy;         // 本轮垂直/纵向/横向力(N)
        public float slipRatio;          // 滑移率 λ
        public float slipAngleRad;       // 侧偏角 α
        public float vLong;              // 接触点纵向速度分量

        // ---------- 缓存(Init 填充) ----------
        float wheelRadius, travel, springK, damperC, brakeMax;
        float cRest;                     // 静态压缩量 = mg / (4k)
        float inertia;                   // 轮转动惯量(含下限防爆炸)
        float massEff;                   // 隐式阻尼用等效质量
        float compVisual;                // 渲染平滑用
        float rollVisual;                // 渲染滚动角累计

        public void Init(CarSpec spec, Vector3 anchor, bool front)
        {
            anchorLocal = anchor;
            isFront = front;
            wheelRadius = spec.wheelRadius;
            travel = spec.suspensionTravel;
            springK = spec.springStiffness;
            damperC = spec.damperRate;
            brakeMax = spec.brakeTorqueNm;
            float g = 9.81f;
            cRest = spec.mass * g / (4f * springK);
            inertia = Mathf.Max(0.5f * spec.unsprungMassPerWheel * wheelRadius * wheelRadius, 1e-3f);
            massEff = spec.mass * 0.5f;
            omega = 0f;
        }

        /// <summary>
        /// 第一步:悬挂射线 + 弹簧力。沿 -车身up 方向(侧倾时自然得到正确压缩);
        /// 射线长度 = 轮半径 + 行程 + 裕量。阻尼使用隐式增益 F_d = c·v/(1 + c·dt/m_eff),
        /// 离散积分无条件稳定(即使调参改回很大的 c 也不会振荡)。
        /// </summary>
        public void UpdateRay(Rigidbody rb, int rayMask, float dt)
        {
            Vector3 anchor = rb.transform.TransformPoint(anchorLocal);
            Vector3 down = -rb.transform.up;
            float maxDist = wheelRadius + travel + 0.02f;

            RaycastHit hit;
            grounded = false;
            fz = 0f;
            if (Physics.Raycast(anchor, down, out hit, maxDist, rayMask))
            {
                grounded = true;
                hitPoint = hit.point;
                hitNormal = hit.normal;
                surface = SurfaceRegion.Resolve(hit.collider);

                compression = Mathf.Clamp(wheelRadius - hit.distance, -cRest, travel * 1.2f);

                // 压缩速度:挂点沿 -up 的速度分量(>0 = 正在压缩)
                float vComp = Vector3.Dot(rb.GetPointVelocity(anchor), down);
                float springForce = springK * (compression + cRest);
                float dampForce = damperC * vComp / (1f + damperC * dt / massEff);
                // 弹簧只推不拉;上限防冲撞瞬间的力尖峰
                fz = Mathf.Clamp(springForce + dampForce, 0f, springK * travel * 1.5f);
            }
            else
            {
                compression = -cRest;
                hitNormal = Vector3.up;
            }
        }

        /// <summary>
        /// 第二步:轮胎力。由接触点速度分解出滑移率 λ 与侧偏角 α,
        /// 经 Pacejka 魔术公式与摩擦椭圆合成后,返回作用于接触点的总力向量
        /// (垂直分量为悬挂力 Fz,纵向/横向为轮胎力,力作用于接触点 → 自然产生俯仰与侧倾)。
        /// </summary>
        public Vector3 ComputeTyreForces(Rigidbody rb, in SurfaceParams sp, float latScale, float steerAbsDeg)
        {
            Vector3 bodyUp = rb.transform.up;
            Vector3 fwd = (isFront && Mathf.Abs(steerAbsDeg) > 0.01f)
                ? Quaternion.AngleAxis(steerAbsDeg, bodyUp) * rb.transform.forward
                : rb.transform.forward;
            Vector3 right = Vector3.Cross(hitNormal, fwd);
            if (right.sqrMagnitude < 1e-6f) right = rb.transform.right;
            right.Normalize();
            fwd = Vector3.Cross(right, hitNormal);
            if (fwd.sqrMagnitude < 1e-6f) fwd = rb.transform.forward;
            fwd.Normalize();

            Vector3 v = rb.GetPointVelocity(hitPoint);
            vLong = Vector3.Dot(v, fwd);
            float vLat = Vector3.Dot(v, right);
            float absV = Mathf.Abs(vLong);

            // λ 分母 ε=1.0 防低速除零;α 分母 0.4 截止(原地打转时侧偏角有界)
            slipRatio = (omega * wheelRadius - vLong) / Mathf.Max(absV, 1.0f);
            slipAngleRad = Mathf.Atan2(vLat, Mathf.Max(absV, 0.4f));

            float muLat = sp.mu * latScale;
            float fx0 = PacejkaTyre.Longitudinal(slipRatio, fz, sp.mu, sp.bx, sp.cx, sp.ex);
            float fy0 = PacejkaTyre.Lateral(slipAngleRad, fz, muLat, sp.by, sp.cy, sp.ey);
            PacejkaTyre.Combine(fx0, fy0, sp.mu * fz, muLat * fz, out fx, out fy);

            return hitNormal * fz + fwd * fx + right * fy;
        }

        /// <summary>
        /// 第三步:车轮角速度积分(驱动扭矩来自电机+传动比分摊;
        /// 刹车为制动力矩;滚动阻力为扭矩形式,tanh 保证静止可停稳)。
        /// </summary>
        public void IntegrateSpin(float driveTorque, float brakeIn, in SurfaceParams sp, float maxOmega, float dt)
        {
            float t = driveTorque;
            if (brakeIn > 0f)
            {
                float tb = brakeIn * brakeMax * Mathf.Clamp01(Mathf.Abs(omega) / 1.5f);
                t -= Mathf.Sign(omega) * tb;
            }
            if (grounded)
                t -= sp.crr * fz * wheelRadius * (float)System.Math.Tanh(omega / 2f);

            omega += t * dt / inertia;
            omega = Mathf.Clamp(omega, -maxOmega, maxOmega);
        }

        /// <summary>
        /// 渲染帧调用:旋转轮视觉(转向角 + 滚动角),位置随压缩量平滑下沉。
        /// </summary>
        public void UpdateVisual(float renderDt)
        {
            if (visualRoot == null) return;
            rollVisual += omega * renderDt * Mathf.Rad2Deg;
            if (rollVisual > 360f) rollVisual -= 360f;
            else if (rollVisual < -360f) rollVisual += 360f;

            visualRoot.localRotation =
                Quaternion.Euler(0f, steerDeg, 0f) * Quaternion.Euler(rollVisual, 0f, 0f);

            float k = 1f - Mathf.Exp(-renderDt * 35f);
            compVisual += (compression - compVisual) * k;
            Vector3 lp = visualRoot.localPosition;
            lp.y = anchorLocal.y + compVisual;
            visualRoot.localPosition = lp;
        }

        /// <summary>重置(车型切换 / R 键)。</summary>
        public void ResetState()
        {
            omega = 0f;
            compression = 0f;
            compVisual = 0f;
            rollVisual = 0f;
            fz = fx = fy = 0f;
        }
    }
}