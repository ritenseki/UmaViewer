using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    /// <summary>
    /// 舞台地板/高光面「过亮发白」的诊断。
    ///
    /// 已从 bundle 查明的部分（ground truth，见 LIVE_AUDIT_2026-08-05.md）：
    ///
    ///   * live10149 上发白的 plane_000 / stage_object_001 / specular_002 三个物件，
    ///     用的是**同一个** shader：Gallop/3D/Live/Stage/DefaultEnvMapNoAmbient。
    ///     它是舞台 shader 里唯一带环境反射通道的一族。
    ///   * 该 shader 真实声明的属性只有 8 个：
    ///         _MainTex  _MulColor0  _ColorPower  _AddColor
    ///         _TripleMaskMap  _EnvMap  _EnvRate(Range 0..1, 默认 0.5)  _EnvBias(Range 0..8, 默认 1)
    ///   * 三个材质的 _EnvRate 分别是 0.75 / 1.0 / 1.0，_EnvBias 是 2 / 2 / 5。
    ///   * 材质里 _EnvMap 是**绑定了**的（指向别的 bundle，不是 null）。
    ///
    /// 剩下的唯一未知项是运行时：那些跨 bundle 的贴图引用 UmaViewer 到底解析出来没有。
    /// 如果 _EnvMap 运行时是 null，Unity 会代入默认贴图（白），再乘上 _EnvRate=1.0
    /// 就是满强度的白色反射 —— 正好是「地板发白」的样子。
    ///
    /// 这个类只负责把那个未知项打出来，不做任何修改。
    ///
    /// 可证伪的预测：plane_000 的 _EnvRate 是 0.75，另外两个是 1.0，
    /// 所以**如果**这个假设成立，plane_000 应该比另外两个略轻一些。
    /// 若三者一样白，说明白化另有来源，这条假设就该丢掉。
    /// </summary>
    public static class StageEnvMapDiag
    {
        private const string kEnvShader = "Gallop/3D/Live/Stage/DefaultEnvMapNoAmbient";

        public static void Dump(StageController stage)
        {
            if (stage == null) return;

            var lines = new List<string>();
            int total = 0, nullEnv = 0;

            foreach (var r in stage.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null || mat.shader == null) continue;
                    if (mat.shader.name != kEnvShader) continue;

                    total++;
                    // 必须问当前 shader；材质的 m_SavedProperties 里还留着一堆 URP Lit 的
                    // 历史属性（_WorkflowMode/_Smoothness/_ClearCoatMask…），那个表不可信。
                    Texture env = mat.HasProperty("_EnvMap") ? mat.GetTexture("_EnvMap") : null;
                    if (env == null) nullEnv++;

                    lines.Add(string.Format(
                        "  {0,-22} mat={1,-34} _EnvMap={2,-26} _EnvRate={3:F2} _EnvBias={4:F2}",
                        r.gameObject.name,
                        mat.name.Replace(" (Instance)", ""),
                        env == null ? "*** null（会代入白贴图）***" : env.name,
                        mat.HasProperty("_EnvRate") ? mat.GetFloat("_EnvRate") : -1f,
                        mat.HasProperty("_EnvBias") ? mat.GetFloat("_EnvBias") : -1f));
                    break;
                }
            }

            if (total == 0)
            {
                Debug.Log($"[EnvMapDiag] 本舞台没有使用 {kEnvShader} 的渲染器");
                return;
            }

            Debug.Log($"[EnvMapDiag] {kEnvShader}：{total} 个渲染器，其中 _EnvMap 为 null 的有 {nullEnv} 个\n"
                      + string.Join("\n", lines));

            if (nullEnv > 0)
                Debug.LogWarning($"[EnvMapDiag] {nullEnv}/{total} 个材质的 _EnvMap 运行时没解析出来 —— " +
                                 "Unity 会代入白贴图，_EnvRate 越接近 1 就越白。地板发白的成因基本可以坐实。");
            else
                Debug.Log("[EnvMapDiag] _EnvMap 全部解析成功 —— 发白不是贴图缺失造成的。" +
                          "按 F9 可在「原始 _EnvRate ↔ 0」之间来回切换，判断反射项是不是发白的来源。");

            if (stage.GetComponent<StageEnvRateToggle>() == null)
                stage.gameObject.AddComponent<StageEnvRateToggle>();
        }
    }

    /// <summary>
    /// 判决实验：按 F9 把 <c>DefaultEnvMapNoAmbient</c> 的 `_EnvRate` 在「原始值 ↔ 0」之间切换。
    ///
    /// 为什么需要它：`_EnvRate` 0.75 与 1.00 只差 25%，如果反射项本来就超过 1.0 被钳到白，
    /// 两者会一样白 —— 所以「plane_000 看起来没比别的轻」**不能**否定反射项假设，
    /// 那个对比检验设计得太弱。把 `_EnvRate` 直接归零才是二元的：
    ///
    ///   发白消失  → 就是 env 反射项，问题转化为「反射强度该由谁压下来」
    ///   发白照旧  → env 路径无关，改查 Built-in shader 在 URP 下的翻译
    ///
    /// 走 MaterialPropertyBlock，不动材质本身；再按一次即可还原。纯诊断，默认不改变任何东西。
    /// </summary>
    public class StageEnvRateToggle : MonoBehaviour
    {
        public KeyCode key = KeyCode.F9;

        private const string kEnvShader = "Gallop/3D/Live/Stage/DefaultEnvMapNoAmbient";
        private bool _zeroed;
        private MaterialPropertyBlock _block;
        private readonly List<Renderer> _targets = new List<Renderer>();
        private bool _collected;

        private void Collect()
        {
            _collected = true;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat != null && mat.shader != null && mat.shader.name == kEnvShader)
                    {
                        _targets.Add(r);
                        break;
                    }
                }
            }
        }

        private void Update()
        {
            if (!Input.GetKeyDown(key)) return;
            if (!_collected) Collect();
            if (_targets.Count == 0)
            {
                Debug.Log("[EnvMapDiag] 本舞台没有带 env 反射的渲染器，F9 无事可做");
                return;
            }

            _zeroed = !_zeroed;
            _block ??= new MaterialPropertyBlock();

            foreach (var r in _targets)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                if (_zeroed)
                {
                    _block.SetFloat("_EnvRate", 0f);
                }
                else
                {
                    // 还原成材质自己的授权值（plane_000=0.75，另两个=1.0）
                    float authored = 0.5f;
                    foreach (var mat in r.sharedMaterials)
                    {
                        if (mat != null && mat.HasProperty("_EnvRate")) { authored = mat.GetFloat("_EnvRate"); break; }
                    }
                    _block.SetFloat("_EnvRate", authored);
                }
                r.SetPropertyBlock(_block);
            }

            Debug.Log($"[EnvMapDiag] _EnvRate → {(_zeroed ? "0（反射关闭）" : "原始值（反射恢复）")}，" +
                      $"作用于 {_targets.Count} 个渲染器。地板此刻变了吗？");
        }
    }
}
