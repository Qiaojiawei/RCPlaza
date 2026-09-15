using RCPlaza.Sim;
using UnityEngine;

namespace RCPlaza.Cam
{
    /// <summary>
    /// 追尾相机(设计文档 §6.2「视角与操作」):LateUpdate 平滑尾随车辆,
    /// 滞后 ≈0.1s(指数趋近),高度下限防穿地;FOV 随速度扩张(60→74°,速度感);
    /// C 键在 追尾 ⇄ 车顶 FPV 间切换。仅一个 Camera + 纯代码,无 Cinemachine。
    /// </summary>
    public class ChaseCamera : MonoBehaviour
    {
        RcCarController target;
        Camera cam;
        bool fpv;

        Vector3 chaseOffset = new Vector3(0f, 2.05f, -3.6f);  // 车后上方
        Vector3 fpvLocal    = new Vector3(0f, 0.50f, 0.28f);  // 车顶略前
        float baseFov = 60f, speedFov = 74f, heightFloor = 0.35f;

        public bool IsFpv => fpv;

        void Awake()
        {
            cam = GetComponent<Camera>();
            if (cam == null) cam = gameObject.AddComponent<Camera>();
        }

        public void SetTarget(RcCarController car)
        {
            target = car;
            if (target != null)
                transform.SetPositionAndRotation(ChasePos(target), ChaseRot(target));
        }

        public void ToggleMode()
        {
            fpv = !fpv;
            cam.nearClipPlane = fpv ? 0.05f : 0.1f; // FPV 贴近车壳不裁切
        }

        void LateUpdate()
        {
            if (target == null) return;

            if (fpv)
            {
                transform.SetPositionAndRotation(
                    target.transform.TransformPoint(fpvLocal),
                    target.transform.rotation);
            }
            else
            {
                Vector3 desired = ChasePos(target);
                float k = 1f - Mathf.Exp(-Time.deltaTime * 9f);   // τ≈0.11s 尾随滞后
                Vector3 p = Vector3.Lerp(transform.position, desired, k);
                if (p.y < heightFloor) p.y = heightFloor;
                transform.SetPositionAndRotation(p, ChaseRot(target));
            }

            // FOV ∝ v²(高速越拉越明显)
            float t = Mathf.Clamp01(target.SpeedMps / 25f);
            t *= t;
            cam.fieldOfView = Mathf.Lerp(baseFov, speedFov, t);
        }

        /// <summary>追尾位:车后上方世界坐标(高度取世界 up,车身侧倾不晃相机)。</summary>
        Vector3 ChasePos(RcCarController car)
        {
            return car.transform.position
                 + car.transform.forward * chaseOffset.z
                 + Vector3.up * chaseOffset.y;
        }

        Quaternion ChaseRot(RcCarController car)
        {
            Vector3 look = car.transform.position + car.transform.up * 0.45f;
            return Quaternion.LookRotation(look - transform.position, Vector3.up);
        }
    }
}