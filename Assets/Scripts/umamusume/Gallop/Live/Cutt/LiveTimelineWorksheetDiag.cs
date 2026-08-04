using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    /// <summary>
    /// 临时诊断。当前用途：核对 WorkSheet 字段名是否命中 bundle 的 TypeTree。
    ///
    /// Unity 从 AssetBundle 反序列化 ScriptableObject 时按字段名严格匹配（区分大小写）。
    /// 名字没命中的字段不会被赋值，保持 C# 默认值 null；名字命中但本曲没数据的，
    /// 会被填成空列表。所以 null 与「空」是两件事：
    ///   NULL -> 字段名很可能拼错了，该轨道在所有歌里都收不到数据
    ///   空    -> 名字是对的，只是这首歌没有这条轨道
    ///
    /// 结论出来后整个文件可删，调用点只有 Director.InitializeTimeline 一处。
    /// </summary>
    public static class LiveTimelineWorksheetDiag
    {
        private const BindingFlags kPublicInstance = BindingFlags.Public | BindingFlags.Instance;

        public static void Dump(LiveTimelineData data)
        {
            if (data == null)
            {
                Debug.LogWarning("[WorksheetDiag] data == null");
                return;
            }
            if (data.worksheetList == null)
            {
                Debug.LogWarning("[WorksheetDiag] data.worksheetList == null");
                return;
            }

            int sheetCount = data.worksheetList.Count;
            Debug.Log($"[WorksheetDiag] ===== worksheetList.Count = {sheetCount} =====");

            FieldInfo[] sheetFields = typeof(LiveTimelineWorkSheet).GetFields(kPublicInstance);
            var nullFieldsAllSheets = new List<string>();

            for (int i = 0; i < sheetCount; i++)
            {
                LiveTimelineWorkSheet sheet = data.worksheetList[i];
                if (sheet == null)
                {
                    Debug.Log($"[WorksheetDiag] [{i}] <null>");
                    continue;
                }

                var populated = new List<string>();
                var empty = new List<string>();
                var nulls = new List<string>();

                foreach (FieldInfo f in sheetFields)
                {
                    if (!IsTrackField(f.FieldType)) continue;

                    object value;
                    try { value = f.GetValue(sheet); }
                    catch { continue; }

                    if (value == null)
                    {
                        nulls.Add(f.Name);
                        nullFieldsAllSheets.Add($"[{i}].{f.Name}");
                        continue;
                    }

                    string detail = Describe(value);
                    if (detail == null) empty.Add(f.Name);
                    else populated.Add($"{f.Name}: {detail}");
                }

                var sb = new StringBuilder();
                sb.AppendLine($"[WorksheetDiag] [{i}] SheetType={sheet.SheetType} version='{sheet.version}' " +
                              $"targetCameraIndex={sheet.targetCameraIndex} TotalTimeLength={sheet.TotalTimeLength}");

                sb.AppendLine($"  --- 有数据 ({populated.Count}) ---");
                foreach (string s in populated) sb.AppendLine($"      {s}");

                sb.AppendLine($"  --- 空，名字对但本曲无此轨道 ({empty.Count}) ---");
                sb.AppendLine($"      {string.Join(", ", empty)}");

                sb.AppendLine($"  --- NULL，字段名未命中 TypeTree ({nulls.Count}) ---");
                sb.AppendLine($"      {string.Join(", ", nulls)}");

                Debug.Log(sb.ToString());
            }

            if (nullFieldsAllSheets.Count > 0)
            {
                Debug.LogWarning("[WorksheetDiag] 以下字段为 NULL，字段名很可能与 bundle 不匹配 -> " +
                                 string.Join(", ", nullFieldsAllSheets));
            }
            else
            {
                // 注意：Unity 加载 bundle 时会把所有 List 字段初始化为空列表，
                // 所以「没有 NULL」并不能证明字段名都正确，只能靠 A/B 段判断。
                Debug.Log("[WorksheetDiag] 无 NULL 字段（但这不能证明名字都对，见 WashLight A/B 段）。");
            }
        }

        /// <summary>
        /// 字段名探针。新增一个轨道字段后，用它确认名字是否命中 bundle 的 TypeTree：
        /// 名字对且本曲有数据 -> 「★ 有数据」；名字错 -> 恒为「空」。
        /// 拿不准大小写时，并列声明两个拼写各探一次即可（WashLightList 就是这么定下来的）。
        /// </summary>
        public static void Probe(LiveTimelineWorkSheet sheet, string fieldName)
        {
            if (sheet == null) { Debug.LogWarning($"[WorksheetDiag] Probe({fieldName}): sheet == null"); return; }

            FieldInfo f = typeof(LiveTimelineWorkSheet).GetField(fieldName, kPublicInstance);
            if (f == null) { Debug.LogWarning($"[WorksheetDiag] Probe: C# 里没有字段 '{fieldName}'"); return; }

            Debug.Log($"[WorksheetDiag] Probe {fieldName}: {DescribeOrEmpty(f.GetValue(sheet))}");
        }

        /// <summary>区分 null、空列表、有数据三种状态。</summary>
        private static string DescribeOrEmpty(object value)
        {
            if (value == null) return "null";
            string detail = Describe(value);
            if (detail != null) return "★ 有数据 -> " + detail;
            return (value is IList list) ? $"空（{list.Count} 组）" : "空";
        }

        /// <summary>只关心时间轴轨道字段，跳过 version / targetCameraIndex 之类的普通字段。</summary>
        private static bool IsTrackField(System.Type type)
        {
            if (typeof(ILiveTimelineKeyDataList).IsAssignableFrom(type)) return true;
            if (type.IsArray) return type.GetElementType().Name.StartsWith("LiveTimeline");
            if (type.IsGenericType)
            {
                System.Type[] args = type.GetGenericArguments();
                return args.Length == 1 && args[0].Name.StartsWith("LiveTimeline");
            }
            return type.Name.StartsWith("LiveTimeline");
        }

        /// <summary>返回该字段的关键帧摘要；没有任何关键帧时返回 null。</summary>
        private static string Describe(object value)
        {
            if (value == null) return null;

            if (value is ILiveTimelineKeyDataList keyList)
                return keyList.Count > 0 ? $"{keyList.Count} keys" : null;

            if (value is IList list && !(value is string))
            {
                int totalKeys = 0;
                var entries = new List<string>();

                foreach (object element in list)
                {
                    if (element == null) continue;

                    int keys = CountKeys(element, 0);
                    if (keys <= 0) continue;

                    totalKeys += keys;
                    string name = (element as ILiveTimelineGroupDataWithName)?.name;
                    entries.Add(string.IsNullOrEmpty(name) ? $"?({keys})" : $"{name}({keys})");
                }

                if (totalKeys == 0) return null;
                return $"{entries.Count} 组 / {totalKeys} keys -> {string.Join(", ", entries)}";
            }

            int nested = CountKeys(value, 0);
            return nested > 0 ? $"{nested} keys" : null;
        }

        /// <summary>递归统计对象里所有 ILiveTimelineKeyDataList 的关键帧总数。</summary>
        private static int CountKeys(object obj, int depth)
        {
            if (obj == null || depth > 2) return 0;

            if (obj is ILiveTimelineKeyDataList keyList)
                return keyList.Count;

            System.Type type = obj.GetType();
            if (type.IsPrimitive || type.IsEnum || obj is string) return 0;

            int total = 0;
            foreach (FieldInfo f in type.GetFields(kPublicInstance))
            {
                object value;
                try { value = f.GetValue(obj); }
                catch { continue; }
                if (value == null) continue;

                if (value is ILiveTimelineKeyDataList nestedList)
                {
                    total += nestedList.Count;
                }
                else if (value is IList inner && !(value is string))
                {
                    foreach (object element in inner)
                        total += CountKeys(element, depth + 1);
                }
                else if (!f.FieldType.IsPrimitive && !f.FieldType.IsEnum && f.FieldType != typeof(string))
                {
                    total += CountKeys(value, depth + 1);
                }
            }
            return total;
        }
    }
}
