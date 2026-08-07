using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
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
}
