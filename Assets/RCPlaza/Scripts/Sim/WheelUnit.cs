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
        float vLongSmooth;               // 滑移参考速度(τ≈50ms 低通,更新位置见 UpdateRay)

        // ---------- 缓存(Init 填充) ----------
        float wheelRadius, travel, springK, damperC, brakeMax;
        float cRest;                     // 静态压缩量 = mg / (4k)
        float inertia;                   // 轮转动惯量(含下限防爆炸)
        float massEff;                   // 隐式阻尼用等效质量
        float compVisual;                // 渲染平滑用
        float rollVisual;                // 渲染滚动角累计
        float crrMult;                   // 滚阻倍率(车型胎质差异,文档 §3.1)
        float lambdaPeak;                // 峰值滑移率(Magic 单调上升支端点,Init 预计算)

        public void Init(CarSpec spec, Vector3 anchor, bool front)
        {
            anchorLocal = anchor;
            isFront = front;
            wheelRadius = spec.wheelRadius;
            travel = spec.suspensionTravel;
            springK = spec.springStiffness;
            damperC = spec.damperRate;
            brakeMax = spec.brakeTorqueNm;
            crrMult = spec.crrMult;
            float g = 9.81f;
            cRest = spec.mass * g / (4f * springK);
            // 轮惯量下限防数值爆炸:物理值 ≈4e-5(0.03kg×2.6cm 胎),下限取 1e-4——
            // 曾用 1e-3(24 倍),收油滑行时轮旋转动能把滑距从解析的 4.3m 顶到 ≈6.9m(超标)。
            inertia = Mathf.Max(0.5f * spec.unsprungMassPerWheel * wheelRadius * wheelRadius, 1e-4f);
            massEff = spec.mass * 0.5f;
            omega = 0f;
            // 峰值滑移率预计算(≈0.19):所有地表共用同一套 Magic 形状参数(SurfaceData §3.1),
            // 取任意一份参数即可。滚阻反力矩通道的平衡逆解只在单调上升支 [0,λp] 上取值,
            // 见 IntegrateSpin 注释。
            var sp0 = SurfaceCatalog.Get(SurfaceType.Brick);
            lambdaPeak = FindPeakLambda(sp0.bx, sp0.cx, sp0.ex);
        }

        /// <summary>黄金分割求 Magic 峰值位置(单调上升支端点;启动时一次,非每步)。</summary>
        static float FindPeakLambda(float bx, float cx, float ex)
        {
            float lo = 0f, hi = 1.6f;
            const float gr = 0.6180339887f;
            float a = lo + (1f - gr) * (hi - lo);
            float b = lo + gr * (hi - lo);
            float ma = MagicShape(a, bx, cx, ex), mb = MagicShape(b, bx, cx, ex);
            for (int it = 0; it < 20; it++)
            {
                if (ma >= mb)
                { hi = b; b = a; mb = ma; a = lo + (1f - gr) * (hi - lo); ma = MagicShape(a, bx, cx, ex); }
                else
                { lo = a; a = b; ma = mb; b = lo + gr * (hi - lo); mb = MagicShape(b, bx, cx, ex); }
            }
            return 0.5f * (lo + hi);
        }

        /// <summary>归一化魔术公式(峰值=1,与 PacejkaTyre.Magic 同形)。</summary>
        static float MagicShape(float x, float bx, float cx, float ex)
        {
            float u = bx * x;
            return Mathf.Sin(cx * Mathf.Atan(u - ex * (u - Mathf.Atan(u))));
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
                // 全伸(droop)边界附近阻尼渐退:不然悬空轮在下行程末端被阻尼"踢"起来——
                // 半波整流泵振,实测 SCT 极速测试被顶成持续翘头 + 42km/h 端头腾空。
                // 真实避震在全伸处也有缓冲垫,不产生无弹簧预紧的尖峰力。
                float droopEngage = Mathf.Clamp01((compression + cRest) / 0.004f);
                dampForce *= droopEngage;
                // 弹簧只推不拉;上限防冲撞瞬间的力尖峰
                fz = Mathf.Clamp(springForce + dampForce, 0f, springK * travel * 1.5f);
            }
            else
            {
                compression = -cRest;
                hitNormal = Vector3.up;
            }

            // 纵向参考速度(λ 的参照)每物理步恰好更新一次,并在**钉扎与滑移评估之前**:
            // 曾放在 ComputeTyreForces(该函数每步被调用时"顺手"更新),导致 IntegrateSpin
            // 的钉扎 ω=(vS+λ*·max)/r 用上一步的 vS、而滑移率用更新后的 vS——车辆加/减速时
            // 两者相差 a·dt,滑移率每帧朝零漂移,制动力/驱动力被系统性衰减(收油滑行制动力
            // ≈理论一半、滑距 9.4-9.7m 对理论 4.2-4.9m;高速巡航时该漂移还给俯仰振荡注入
            // 维持能量)。同一帧内统一参照后,滑移率=λ* 精确成立。
            // 转向角对纵向投影的影响纳入 fwd(与 ComputeTyreForces 同构,见该处注释)。
            {
                Vector3 fwd = (isFront && Mathf.Abs(steerDeg) > 0.01f)
                    ? Quaternion.AngleAxis(steerDeg, rb.transform.up) * rb.transform.forward
                    : rb.transform.forward;
                Vector3 vAnchor = rb.GetPointVelocity(anchor);
                vLong = Vector3.Dot(vAnchor, fwd);
                const float tauV = 0.05f; // 50ms 低通:压制步间左右轮中心速度噪声(定圆推力对消症的修复)
                vLongSmooth += (vLong - vLongSmooth) * (1f - Mathf.Exp(-0.01f / tauV));
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

            // 关键:滑移速度取**轮心(悬挂挂点)**速度,而非接触点速度——接触点速度
            // 混入车身俯仰/侧倾角速度×力臂的分量,低速时该分量与车速同量级,经
            // λ→Fx 通道把俯仰振荡自激(实测 1 m/s 收油滑行:接触点法产生 ±6 N 持续
            // 泵振、四轮 Fz 在 2↔11 N 间跳变、滑距失真为 2.6 m,理论应 4.1 m)。
            // 轮心对重心力臂 ≈0,切断这条正反馈;力仍施加在接触点(自然重量转移)。
            Vector3 anchorW = rb.transform.TransformPoint(anchorLocal);
            Vector3 v = rb.GetPointVelocity(anchorW);
            // vLong/vLongSmooth 在 UpdateRay 中更新(与钉扎共用同一参照,见该处注释);
            // 此处只取接触点横向分量:vLat 不进 λ 通量,α 仍用原始值(低速转向经 atan2 天然饱和)。
            float vLat = Vector3.Dot(v, right);

            // λ 分母 ε=1.0 防低速除零;α 分母 0.4 截止(原地打转时侧偏角有界)
            slipRatio = (omega * wheelRadius - vLongSmooth) / Mathf.Max(Mathf.Abs(vLongSmooth), 1.0f);
            slipAngleRad = Mathf.Atan2(vLat, Mathf.Max(Mathf.Abs(vLong), 0.4f));

            float muLat = sp.mu * latScale;
            float fx0 = PacejkaTyre.Longitudinal(slipRatio, fz, sp.mu, sp.bx, sp.cx, sp.ex);
            float fy0 = PacejkaTyre.Lateral(slipAngleRad, fz, muLat, sp.by, sp.cy, sp.ey);
            PacejkaTyre.Combine(fx0, fy0, sp.mu * fz, muLat * fz, out fx, out fy);
            // 滚动阻力不在此处按车身力施加:它走轮端反力矩通道(IntegrateSpin 里
            // −crrF·r 进入扭矩平衡)。若在车身力通道再加一份,滚阻会在车身力与
            // 轮力矩两条通道上重复计阻,且 tanh 低速尾巴会让收油滑行拖过验收上界
            // (实测纯车身 crr 滑行 >6m/40s 未停稳)。

            return hitNormal * fz + fwd * fx + right * fy;
        }

        /// <summary>
        /// 第三步:车轮角速度更新(驱动扭矩来自电机+传动比分摊;刹车为制动力矩;
        /// 轮胎纵向力以反作用力矩 −Fx·r 闭合轮转——轮转与车身运动的刚性耦合)。
        /// 滚动阻力仍按车身力施加(见 ComputeTyreForces 注释),这里不再重复计阻。
        /// </summary>
        public void IntegrateSpin(float driveTorque, float brakeIn, in SurfaceParams sp, float maxOmega, float dt)
        {
            // 牵引控制(ESC 防滑,全速段常开):Pacejka 峰值滑移率 ≈0.2,超过后纵向力
            // 斜率为负,无约束驱动扭矩会把车轮推到 λ≥1.2 的死亡区——轮速反超空载转速,
            // 电机扭矩曲线归零,车彻底失速(实测顶速从 65 掉到 43.2 km/h)。
            // λ>λ_tc 后按比例缩减驱动扭矩,λ≥1.2 处压到 ≈0(近断油;只按比例降到 35%
            // 会把车停在峰值外的斥力区——实测离地烧胎 25s 纹丝不动)。
            // 巡航/极速段 λ 天然 <0.1(轮转闭合把 λ 钉在轮胎力与驱动扭矩的平衡点),
            // 阶梯阈值 0.30 不触顶,故全速段常开无损极速、只在烧胎工况介入。
            const float lambdaTc = 0.30f;    // 开始限滑的滑移率
            const float lambdaOff = 1.20f;   // 此滑移率处驱动扭矩 →5%(近断油)
            float tc = 1f;
            float la = Mathf.Abs(slipRatio);
            if (la > lambdaTc)
                tc = Mathf.Lerp(1f, 0.05f, Mathf.Clamp01((la - lambdaTc) / (lambdaOff - lambdaTc)));

            float tNet = driveTorque * tc;
            if (brakeIn > 0f)
            {
                float tb = brakeIn * brakeMax * Mathf.Clamp01(Mathf.Abs(omega) / 1.5f);
                tNet -= Mathf.Sign(omega) * tb;
            }

            if (fz > 0.01f)
            {
                // 轮转闭合(准静态平衡):接地轮惯量 ≈1e-4,该模式的响应(≈0.001s)远快于
                // 物理步;若显式积分 −Fx·r,离散环路增益 r²·dt·(∂Fx/∂λ)/(I·max(|v|,1))
                // 在 v<≈0.6m/s 超过 1——实测 ±2000rad/s² 的点头颤振、Fz 在 3↔11N 摆动、
                // 滑行测试尾段多爬 2m 超程。解法:把 ω 直接钉在"轮胎力=轮上净扭矩"的
                // 平衡点 λ* = Magic⁻¹(T_net/(r·μFz)),轮速即时跟随车身(快模式不储能,
                // 能量账等价;缺失闭合时(曾犯)λ 自由跑飞:滑行测试 14.6m 匀速漂移、极速 43.2)。
                //
                // 平衡目标含**滚阻反力矩**:−crrF·r 并入 tNet。收油滑行时平衡点自然落在
                // λ* = −inv(crr/μ) 的小负滑移,轮胎稳定产出 −crrF 的减速力——滚阻由轮胎
                // 力链条自洽实现(真实滚阻即接触斑损耗,本来就是轮上现象)。tanh 阈值取
                // 0.30(低速不拖尾:纯车身 crr 在 tanh(v/0.5) 下收油滑行实测 >6m 超标)。
                float crrF = sp.crr * crrMult * fz * (float)System.Math.Tanh(vLong / 0.30f);

                float fMax = sp.mu * fz + 1e-6f;
                float fTarget = Mathf.Clamp(tNet / wheelRadius - crrF, -fMax, fMax);
                float ratio = Mathf.Abs(fTarget) / fMax;

                // 逆解只在**峰值前的单调上升支** [0, λp] 上二分;扭矩需求触及/超出
                // μFz 时饱和在 λp(≈0.19)——轮胎输出抓地力上限,残余扭矩不再注入轮速,
                // 等价理想牵引控制。旧实现对 [0,1.6] 全域二分:M(λ) 峰值后非单调,
                // 需求比 >峰值时根落在 λ≈1.2 的死亡区,与 TC 阶梯(λ>0.30 限扭、
                // >1.2 近断油)形成 100Hz 极限环——实测净推力只剩 40–60%(0→40 拖到
                // 4.3–4.5s、极速卡在 47 km/h、滑行 40s 未停稳)。
                float lambdaStar;
                if (ratio >= 1f) lambdaStar = lambdaPeak;
                else
                {
                    float lo = 0f, hi = lambdaPeak;
                    for (int it = 0; it < 14; it++)
                    {
                        float mid = 0.5f * (lo + hi);
                        if (MagicShape(mid, sp.bx, sp.cx, sp.ex) >= ratio) hi = mid; else lo = mid;
                    }
                    lambdaStar = 0.5f * (lo + hi);
                }
                if (fTarget < 0f) lambdaStar = -lambdaStar;
                // 平衡点钉在**平滑后的**参考速度上(与 λ 计算共用同一参考,一致性闭环;
                // vS 的更新位置在 UpdateRay——钉扎与滑移评估必须同帧同参照,见该处注释)
                omega = (vLongSmooth + lambdaStar * Mathf.Max(Mathf.Abs(vLongSmooth), 1f)) / wheelRadius;
                omega = Mathf.Clamp(omega, -maxOmega, maxOmega);
            }
            else
            {
                // 离地空转:无地面约束,显式积分保留"轰油落地打滑"的真实感
                omega += tNet * dt / inertia;
                omega = Mathf.Clamp(omega, -maxOmega, maxOmega);
            }
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
            vLongSmooth = 0f;
        }
    }
}