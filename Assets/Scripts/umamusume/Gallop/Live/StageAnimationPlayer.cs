using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    /// <summary>
    /// 舞台自带 <see cref="Animation"/> 的播放。
    ///
    /// 为什么需要：live10149 的舞台 prefab 引用了 11 个脚本类，UmaViewer 里**只有
    /// StageController 存在**，另外 10 个（AnimationObjectController、MirrorBallProjector、
    /// MirrorReflection、CustomLensFlare、UnityLensFlareController、CustomProjector、
    /// LightProjection、WashLightController、BillboardController、ShaderParamController）
    /// 全部缺失，那些 MonoBehaviour 反序列化后脚本引用为空，永远不执行。
    ///
    /// 其中 AnimationObjectController 的缺席后果最直接：舞台上 283 个 Animation 组件
    /// **全部** `playAutomatically = true`，但**默认 clip（m_Animation）全部为空**。
    /// Unity 的 playAutomatically 播的是默认 clip，默认 clip 为空就什么都不播 ——
    /// 原版是靠那个脚本显式 Play("clip名") 的。于是整个舞台的动画一个都没动：
    /// 迪斯科灯球不转、wash 灯不扫、霓虹灯牌不动。
    ///
    /// **只处理唯一解的情形。** clip 数量分布：
    ///   1 个 clip → 4 个对象（mirrorball_flarelight、wash_ground_b/c/d），全是 `*_loop_000`，
    ///               没有歧义，直接播。
    ///   3 个 clip → `loop_000` / `up_000` / `loop_001`，是「过渡 + 两个循环态」的状态机，
    ///               选哪个、何时切换都没有依据。
    ///   5 个 clip → 272 个对象（占 96%，多半是观众席），同样未知。
    /// 后两类只打一次日志把 clip 名列出来，**不猜**。选择规则逆出来之后再补。
    ///
    /// 播放时机走 OnEnable 而不是初始化时一次性 Play：受时间轴控制的灯（名字含
    /// blinklight/_wash_/laser/spotlight3d）初始是 inactive 的，要等轨道把它们打开，
    /// 在 inactive 对象上调 Play() 不会生效。镜面球容器
    /// pfb_env_live10149_blinklight_mirrorball_flarelight 正好就是这种情况。
    /// </summary>
    public static class StageAnimationPlayer
    {
        public static void Setup(StageController stage)
        {
            if (stage == null) return;

            int single = 0, ambiguous = 0, empty = 0;
            var loggedClipSets = new HashSet<string>();

            foreach (var anim in stage.GetComponentsInChildren<Animation>(true))
            {
                if (anim == null || !anim.playAutomatically) continue;

                var clips = new List<AnimationClip>();
                foreach (AnimationState st in anim)
                {
                    if (st != null && st.clip != null) clips.Add(st.clip);
                }

                if (clips.Count == 0) { empty++; continue; }

                if (clips.Count == 1)
                {
                    // 唯一解。注意**不要**设 anim.clip、也不要 Play()：
                    // playAutomatically 是 true，一旦有了默认 clip，Unity 会用自己的时钟
                    // 自动播放，于是 live 暂停/拖动进度条时舞台照转。必须由时间轴驱动。
                    anim.Stop();
                    var driver = anim.gameObject.GetComponent<StageAnimationAutoPlay>();
                    if (driver == null) driver = anim.gameObject.AddComponent<StageAnimationAutoPlay>();
                    driver.Bind(anim, clips[0]);
                    single++;
                    // 打出时长：镜面球那条 clip 离线量到 2.0s 转满 360°（30 RPM），
                    // 如果运行时 length 不是 2.0，说明转速问题出在 clip 解析而不是编排。
                    Debug.Log($"[StageAnim] 接管 '{anim.gameObject.name}' ← {clips[0].name} " +
                              $"length={clips[0].length:F3}s wrapMode={clips[0].wrapMode}");
                    continue;
                }

                ambiguous++;
                var names = new List<string>();
                foreach (var c in clips) names.Add(c.name);
                string key = string.Join(",", names);
                if (loggedClipSets.Add(key))
                {
                    Debug.Log($"[StageAnim] '{anim.gameObject.name}' 有 {clips.Count} 个 clip，" +
                              $"选择规则未知，**未播放**：{key}");
                }
            }

            Debug.Log($"[StageAnim] 单 clip 已接管 {single} 个；多 clip 未处理 {ambiguous} 个；无 clip {empty} 个");
        }
    }

    /// <summary>
    /// 缺失脚本的运行时普查，当进度表用：每按原签名实现一个原版脚本，
    /// 「脚本丢失」就该下降对应的实例数（已验证 BillboardController 令 892→728，正好 −164）。
    ///
    /// 签名清单、各类实例数、以及「为什么建同名类就能收到字段」见 CLAUDE.md
    /// 「用原签名重建脚本」。一句话：Unity 按**类名 + namespace + 程序集名**解析 MonoScript，
    /// 而 `umamusume.asmdef` 的程序集名与 bundle 里写的完全一致 —— 那 892 个空槽位
    /// 只是没人写，不是读不到。（这条最初记反了，是本普查的结果自己推翻的：
    /// 存活的 `AssetHolder` 和 `StageController` 正是靠这个机制绑上的。）
    /// </summary>
    public static class StageMissingScriptCensus
    {
        public static void Dump(StageController stage)
        {
            if (stage == null) return;

            int slots = 0, missing = 0, alive = 0;
            var aliveTypes = new Dictionary<string, int>();

            foreach (var tr in stage.GetComponentsInChildren<Transform>(true))
            {
                var comps = tr.GetComponents<Component>();
                foreach (var c in comps)
                {
                    // MonoBehaviour 槽位里脚本丢失时，元素为 null。
                    if (c == null) { slots++; missing++; continue; }
                    if (c is MonoBehaviour)
                    {
                        slots++; alive++;
                        string n = c.GetType().Name;
                        aliveTypes.TryGetValue(n, out int k);
                        aliveTypes[n] = k + 1;
                    }
                }
            }

            var parts = new List<string>();
            foreach (var kv in aliveTypes) parts.Add($"{kv.Key}x{kv.Value}");
            Debug.Log($"[StageScripts] MonoBehaviour 槽位 {slots}：脚本丢失 {missing}，存活 {alive}" +
                      (parts.Count > 0 ? $"（{string.Join(", ", parts)}）" : "") +
                      "。基线 892（live10149）；每按签名实现一个类，丢失数应下降对应实例数。" +
                      "已验证：BillboardController 令 892→728（-164）。");
        }
    }

    /// <summary>
    /// 用 live 时间轴的时间手动采样 clip。
    ///
    /// **不能用 Animation.Play()** —— 那跑的是 Unity 自己的时钟，结果是：live 暂停了舞台
    /// 照转、拖动进度条不跟随、播放速度和歌无关。舞台动画属于演出编排的一部分，
    /// 必须和 <see cref="LiveTimelineControl.currentLiveTime"/> 同源，
    /// 所以这里每帧设 AnimationState.time 再 Sample()。
    ///
    /// 时间取 currentLiveTime，暂停时它不前进，拖动时它跳变，两种情况都自动正确。
    /// </summary>
    public class StageAnimationAutoPlay : MonoBehaviour
    {
        private Animation _anim;
        private AnimationClip _clip;
        private string _clipName;
        private float _length;
        private bool _loop;

        public void Bind(Animation anim, AnimationClip clip)
        {
            _anim = anim;
            _clip = clip;
            _clipName = clip != null ? clip.name : null;
            _length = clip != null ? clip.length : 0f;
            // clip 自身的 wrapMode 决定循环与否；live10149 的 22 个 clip 里 16 个是 Loop。
            _loop = clip != null && (clip.wrapMode == WrapMode.Loop || clip.wrapMode == WrapMode.PingPong);
        }

        private void LateUpdate()
        {
            if (_anim == null || _clip == null || _length <= 0f) return;

            var ctrl = Director.instance != null ? Director.instance._liveTimelineControl : null;
            if (ctrl == null) return;

            float t = ctrl.currentLiveTime;
            // TODO: PingPong 按 Loop 处理，来回摆的那一半没实现（live10149 上没有 PingPong clip）。
            float sampleTime = _loop ? Mathf.Repeat(t, _length) : Mathf.Clamp(t, 0f, _length);

            var state = _anim[_clipName];
            if (state == null) return;
            state.enabled = true;
            state.weight = 1f;
            state.time = sampleTime;
            _anim.Sample();
            // 采样完就关掉，避免 Animation 自己再按 Unity 时钟推进一次。
            state.enabled = false;
        }
    }
}
