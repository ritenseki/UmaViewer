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
    /// 按「整体朝向摄像机」处理**看起来**合理；**2 代表什么没有依据，故不处理**，只打一次日志。
    ///
    /// ── 2026-08-07：朝向行为默认关闭（<see cref="EnableRotation"/> = false）──
    ///
    /// 实机观察到「灯一直转来转去，而那东西看起来不该转」。查下来有两层问题：
    ///
    /// 1. **确定的 bug（已修）**：`_cachedTarget` 只解析一次就再不更新，而
    ///    `Director.MainCameraTransform` 是**每帧重新赋值**的（`UpdateMainCamera()` 里
    ///    `_cameraTransforms[_activeCameraIndex]`），并且会随 CameraSwitcher 轨道在 3 个
    ///    摄像机之间切换。于是切换之后，面片朝的是一个**观众看不到的摄像机**——而那个
    ///    摄像机仍在按自己的时间轴运动，看上去就是「无缘无故一直转」。改为每帧解析。
    ///
    /// 2. **没有依据的部分（因此关掉）**：`_rotationType == 0` 到底是不是「整轴朝向摄像机」，
    ///    以及朝向该用摄像机的 up（跟着镜头 roll，屏幕对齐）还是世界 up（只绕 Y），
    ///    两者画面完全不同，而**没有任何 ground truth 能判**。按项目规矩「缺依据即不实现」，
    ///    默认关闭；关掉时面片保持 prefab 作者摆好的朝向，那至少是真实数据而不是我的猜测。
    ///
    /// 目标物件本身是查过的，不是猜的：106 个 type-0 实例名字全部是
    /// `*_billbord` / `*_billboard` / `glow_plane` / `spotlight`，且 106/106 都带 MeshRenderer，
    /// 灯体在父链上（`swing_ < arm_ < stand_`）。所以「这些该 billboard 化」大概率成立，
    /// 不成立的是「怎么转」。
    /// </summary>
    public class BillboardController : MonoBehaviour
    {
        /// <summary>
        /// 朝向行为总开关。缺 ground truth，默认 false —— 见类注释。
        /// 要对着参考视频标定时把它打开（和 PostFilmRendererFeature._enableRendering 同一个道理）。
        /// </summary>
        public static bool EnableRotation = false;

        // ↓ 这三个名字来自 bundle TypeTree，改名就收不到数据
        [SerializeField] private Transform _targetCameraTransform;
        [SerializeField] private int _rotationType;
        [SerializeField] private bool _isInversedForward;

        private static bool _loggedUnknownType;

        /// <summary>
        /// **每帧都要重新解析，不能缓存** —— 生效的直播摄像机会随 CameraSwitcher 换人。
        /// </summary>
        private Transform ResolveTarget()
        {
            if (_targetCameraTransform != null) return _targetCameraTransform;

            // 舞台数据里这个字段 164/164 全是空的，回落到当前生效的直播摄像机。
            if (Director.instance != null && Director.instance.MainCameraTransform != null)
                return Director.instance.MainCameraTransform;

            var cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        private void LateUpdate()
        {
            if (!EnableRotation) return;

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

            Transform target = ResolveTarget();
            if (target == null) return;

            Vector3 forward = transform.position - target.position;
            if (_isInversedForward) forward = -forward;
            if (forward.sqrMagnitude < 1e-8f) return;

            // TODO: up 用 target.up（屏幕对齐、跟着镜头 roll）还是 Vector3.up（只绕 Y）无依据。
            transform.rotation = Quaternion.LookRotation(forward.normalized, target.up);
        }
    }
}
