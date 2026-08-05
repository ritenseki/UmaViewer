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
                    // 唯一解：把它设成默认 clip 并在每次启用时播放。
                    anim.clip = clips[0];
                    var auto = anim.gameObject.GetComponent<StageAnimationAutoPlay>();
                    if (auto == null) auto = anim.gameObject.AddComponent<StageAnimationAutoPlay>();
                    auto.Bind(anim, clips[0].name);
                    single++;
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
    /// 在 OnEnable 时播放绑定的 clip。受时间轴控制的舞台灯初始 inactive，
    /// 等 BlinkLight/WashLight/Laser 的 handler 把它们 SetActive(true) 之后才需要动起来。
    /// </summary>
    public class StageAnimationAutoPlay : MonoBehaviour
    {
        private Animation _anim;
        private string _clipName;

        public void Bind(Animation anim, string clipName)
        {
            _anim = anim;
            _clipName = clipName;
            if (isActiveAndEnabled) PlayClip();
        }

        private void OnEnable() => PlayClip();

        private void PlayClip()
        {
            if (_anim == null || string.IsNullOrEmpty(_clipName)) return;
            // 已经在播就不要重来，否则每次 SetActive 都会把循环打回起点。
            if (_anim.IsPlaying(_clipName)) return;
            _anim.Play(_clipName);
        }
    }
}
