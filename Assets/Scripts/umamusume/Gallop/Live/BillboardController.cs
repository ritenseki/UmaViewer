using UnityEngine;

namespace Gallop.Live
{
    /// <summary>
    /// 舞台面片朝向摄像机。对应轨道 Billboard (75)，live10149 上 164 个实例。
    ///
    /// 这是第一个「按原签名重建」的舞台脚本。bundle 里的 MonoScript 记着
    /// ClassName=BillboardController、Namespace=Gallop.Live、AssemblyName=umamusume，
    /// 而 <c>Assets/Scripts/umamusume.asmdef</c> 的程序集名正是 umamusume，
    /// 所以只要类名 + namespace 对上，Unity 就会把下面三个字段从 bundle 反序列化进来 ——
    /// 不需要按名字猜，也不需要把 dump 的参数表打包进项目。
    ///
    /// 字段名必须和 bundle 的 TypeTree 逐字一致（大小写敏感），实测为：
    ///   _targetCameraTransform / _rotationType / _isInversedForward
    ///
    /// live10149 的实测取值：`_targetCameraTransform` 164/164 全为空 → 回落主摄像机；
    /// `_isInversedForward` 全 0；`_rotationType` 只有 0（106 个，wash/glow 类）
    /// 和 2（58 个，全是 glow_ramp）两种。
    ///
    /// ⚠ `_rotationType` 的枚举语义仍未知。0 是枚举零值、类名又是 BillboardController，
    /// 按「整体朝向摄像机」处理是合理的；**2 代表什么没有依据，故不处理**，
    /// 只打一次日志。等拿到证据再补，不猜。
    /// </summary>
    public class BillboardController : MonoBehaviour
    {
        // ↓ 这三个名字来自 bundle TypeTree，改名就收不到数据
        [SerializeField] private Transform _targetCameraTransform;
        [SerializeField] private int _rotationType;
        [SerializeField] private bool _isInversedForward;

        private Transform _cachedTarget;
        private static bool _loggedUnknownType;

        private Transform ResolveTarget()
        {
            if (_targetCameraTransform != null) return _targetCameraTransform;

            // 舞台数据里这个字段全是空的，回落到当前生效的直播摄像机。
            if (Director.instance != null && Director.instance.MainCameraTransform != null)
                return Director.instance.MainCameraTransform;

            var cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        private void LateUpdate()
        {
            // 放 LateUpdate：必须在 Director 当帧摆好摄像机之后再朝向它，否则永远差一帧。
            if (_rotationType != 0)
            {
                if (!_loggedUnknownType)
                {
                    _loggedUnknownType = true;
                    Debug.Log($"[Billboard] _rotationType={_rotationType}（如 '{name}'）语义未知，未处理。" +
                              "live10149 上这一类共 58 个，全是 glow_ramp。");
                }
                return;
            }

            if (_cachedTarget == null) _cachedTarget = ResolveTarget();
            if (_cachedTarget == null) return;

            Vector3 forward = transform.position - _cachedTarget.position;
            if (_isInversedForward) forward = -forward;
            if (forward.sqrMagnitude < 1e-8f) return;

            transform.rotation = Quaternion.LookRotation(forward.normalized, _cachedTarget.up);
        }
    }
}
