using Gallop.Live.Cutt;
using System;
using System.Collections.Generic;
using UnityEngine;


namespace Gallop.Live
{
    [Serializable]
    public class StageObjectUnit
    {
        public string UnitName;
        public GameObject[] ChildObjects;
        public string[] _childObjectNames;
    }

    public class StageController : MonoBehaviour
    {
        public List<GameObject> _stageObjects;
        public StageObjectUnit[] _stageObjectUnits;
        public Dictionary<string, StageObjectUnit> StageObjectUnitMap = new Dictionary<string, StageObjectUnit>();
        public Dictionary<string, GameObject> StageObjectMap = new Dictionary<string, GameObject>();
        public Dictionary<string, Transform> StageParentMap = new Dictionary<string, Transform>();

        private void Awake()
        {
            InitializeStage();
            if (Director.instance)
            {
                Director.instance._stageController = this;
                Director.instance._liveTimelineControl.OnUpdateTransform += UpdateTransform;
                Director.instance._liveTimelineControl.OnUpdateObject += UpdateObject;
            }
        }

        private void OnDestroy()
        {
            if (Director.instance)
            {
                Director.instance._liveTimelineControl.OnUpdateTransform -= UpdateTransform;
                Director.instance._liveTimelineControl.OnUpdateObject -= UpdateObject;
            }
        }

        // Objects controlled by timeline handlers (BlinkLight / WashLight / Spotlight3d / Laser)
        // start inactive; their handlers call SetActive(true) when the track fires.
        private static bool IsTimelineControlledLight(string name) =>
            name.Contains("blinklight") ||
            name.Contains("spotlight3d") ||
            name.Contains("_wash_") ||
            name.Contains("laser");

        /// <summary>
        /// 祖先里是否已经有一盏受时间轴控制的灯。有的话本对象跟着祖先一起显隐，
        /// 不该在初始化时被单独关掉（关了就再也没人打开）。
        /// </summary>
        private bool HasTimelineControlledAncestor(Transform t)
        {
            for (Transform p = t.parent; p != null && p != transform; p = p.parent)
            {
                if (IsTimelineControlledLight(p.name.Replace("(Clone)", "")))
                    return true;
            }
            return false;
        }

        public void InitializeStage()
        {
            foreach (GameObject stage_part in _stageObjects)
            {
                var instance = Instantiate(stage_part, transform);
                foreach (var child in instance.GetComponentsInChildren<Transform>(true))
                {
                    // 判重和插入统一用剥掉 "(Clone)" 的名字。
                    // 原先判重用 child.name（未剥）、插入用剥掉的，两个键不一致。
                    var tmp_name = child.name.Replace("(Clone)", "");

                    // 只关「本分支里最上层」的那盏受控灯。
                    //
                    // 轨道寻址的是**容器**：live10149 实测 28 个 BlinkLight 组名全部一一对应
                    // 一个 GameObject（27 个命中、1 个 wash_discoball 没有对应物件），真正的灯是
                    // 它的子物件，由 handler 的 GetComponentsInChildren 覆盖。
                    //
                    // 原来 SetActive(false) 写在判重 if 里面，于是**同名子物件只有第一个被关**，
                    // 而 handler 只会 SetActive(true) 容器 —— 那第一个子物件再没有人打开，全程是黑的。
                    // live10149 上的受害者：light001_wash_ground_a(×4)、swing_wash_ground_a(×2)、
                    // base_rotate_wash_ground_a(×2)、light000_wash_ground_a(×2)，各有 1 盏常黑。
                    //
                    // 祖先已经是受控灯的，跟着祖先显隐即可，不该再单独关。
                    if (IsTimelineControlledLight(tmp_name) && !HasTimelineControlledAncestor(child))
                        child.gameObject.SetActive(false);

                    if (!StageObjectMap.ContainsKey(tmp_name))
                    {
                        StageObjectMap.Add(tmp_name, child.gameObject);
                        StageParentMap.TryAdd(tmp_name, child.gameObject.transform.parent);
                    }
                }
            }

            foreach (var unit in _stageObjectUnits)
            {
                if (!StageObjectUnitMap.ContainsKey(unit.UnitName))
                {
                    StageObjectUnitMap.Add(unit.UnitName, unit);
                }
            }

            // 舞台自带 Animation 的播放（原版由缺失的 AnimationObjectController 负责）。
            StageAnimationPlayer.Setup(this);

            // 缺失脚本普查：每按签名实现一个原版脚本，「脚本丢失」计数就该下降对应实例数。
            StageMissingScriptCensus.Dump(this);
        }

        public void UpdateObject(ref ObjectUpdateInfo updateInfo) {

            if (updateInfo.data == null)
            {
                return;
            }
            // 组名可能是 _stageObjectUnits 的单元名而不是 GameObject 名
            // （live10149 的 'neonsign' 就是 unit，底下挂 glow/wash_a/wash_b 三个子对象；
            //  没有任何 GameObject 叫 neonsign，所以只查 StageObjectMap 会整条轨道失效，
            //  灯牌就不会从吊顶降下来）。
            if (!StageObjectMap.ContainsKey(updateInfo.data.name) &&
                StageObjectUnitMap.TryGetValue(updateInfo.data.name, out StageObjectUnit unit))
            {
                foreach (var child in unit.ChildObjects)
                {
                    if (child == null) continue;
                    if (!StageObjectMap.TryGetValue(child.name.Replace("(Clone)", ""), out var childGo))
                        childGo = child;

                    childGo.SetActive(updateInfo.renderEnable);
                    ApplyObjectTrs(childGo.transform, ref updateInfo);
                }
                return;
            }

            if (StageObjectMap.TryGetValue(updateInfo.data.name, out GameObject gameObject))
            {
                gameObject.SetActive(updateInfo.renderEnable);

                Transform attach_transform = null;
                switch (updateInfo.AttachTarget)
                {
                    case AttachType.None:
                        if(StageParentMap.TryGetValue(updateInfo.data.name, out Transform parentTransform))
                        {
                            attach_transform = parentTransform;
                        }
                        break;
                    case AttachType.Character:
                        var chara = Director.instance.CharaContainerScript[updateInfo.CharacterPosition];
                        if (chara)
                        {
                            attach_transform = chara.transform;
                        }
                        break;
                    case AttachType.Camera:
                        attach_transform = Director.instance.MainCameraTransform;
                        break;
                }
                if (gameObject.transform.parent != attach_transform)
                {
                    gameObject.transform.SetParent(attach_transform);
                }

                ApplyObjectTrs(gameObject.transform, ref updateInfo);
            }
        }

        /// <summary>物件的原始 local TRS，Add 模式要在它之上叠加。首次触碰时懒记录。</summary>
        private readonly Dictionary<Transform, TransformBaseData> _objectBaseTrs =
            new Dictionary<Transform, TransformBaseData>();

        /// <summary>
        /// 按 OffsetType 应用 TRS。
        /// Direct(0) = 绝对值；Add(1) = 相对物件原始 local TRS 的偏移。
        /// 之前一直无视这个字段，一律当绝对值写，于是 Add 语义的物件全被挪到父物体原点
        /// ——live10149 的 neonsign 灯牌就是这样掉到地上的（它的 y=12.58→0 是「抬高 12.58 开场，再降回原位」）。
        /// </summary>
        private void ApplyObjectTrs(Transform tr, ref ObjectUpdateInfo updateInfo)
        {
            if (!_objectBaseTrs.TryGetValue(tr, out TransformBaseData baseTrs))
            {
                baseTrs = new TransformBaseData
                {
                    position = tr.localPosition,
                    rotation = tr.localRotation,
                    scale = tr.localScale,
                };
                _objectBaseTrs[tr] = baseTrs;
            }

            bool add = updateInfo.OffsetType == OffsetType.Add;

            if (updateInfo.data.enablePosition)
                tr.localPosition = add
                    ? baseTrs.position + updateInfo.updateData.position
                    : updateInfo.updateData.position;

            if (updateInfo.data.enableRotate)
                tr.localRotation = add
                    ? baseTrs.rotation * updateInfo.updateData.rotation
                    : updateInfo.updateData.rotation;

            // scale 用乘法而不是加法：关键帧里 scale 恒为 1，加法会把物件放大一倍。
            if (updateInfo.data.enableScale)
                tr.localScale = add
                    ? Vector3.Scale(baseTrs.scale, updateInfo.updateData.scale)
                    : updateInfo.updateData.scale;
        }

        /// <summary>
        /// Transform 轨道。
        ///
        /// 原实现只查 StageObjectUnitMap，组名不是 unit 名就直接什么都不做、也不报错 ——
        /// 这是 UpdateObject 那个 bug 的**镜像**（那次是只查 StageObjectMap、漏了 unit 名）。
        /// 实测组名两种都有：son1028 的第一个组叫 light001_glow_001，是 GameObject 名，
        /// 于是这 12 首歌 / 681 个关键帧整条轨道从来没生效过。两种都查。
        ///
        /// 注：TypeTree 实测 LiveTimelineKeyTransformData 只有 position/rotate/scale，
        /// **没有 OffsetType 字段**（和 Object 轨道不同），所以这里一律绝对写入是对的，
        /// 不需要 ApplyObjectTrs 那套 Add 相对语义。
        /// </summary>
        public void UpdateTransform(ref TransformUpdateInfo updateInfo)
        {
            if (updateInfo.data == null)
            {
                return;
            }

            if (StageObjectUnitMap.TryGetValue(updateInfo.data.name, out StageObjectUnit objectUnit))
            {
                foreach (var child in objectUnit.ChildObjects)
                {
                    if (child == null) continue;
                    // StageObjectMap 存的是剥掉 "(Clone)" 的名字，查的时候也得剥
                    // —— UpdateObject 有这一步，这里原先漏了。
                    if (!StageObjectMap.TryGetValue(child.name.Replace("(Clone)", ""), out var childGo))
                        childGo = child;
                    ApplyTransformTrs(childGo.transform, ref updateInfo);
                }
                return;
            }

            if (StageObjectMap.TryGetValue(updateInfo.data.name, out GameObject gameObject))
            {
                ApplyTransformTrs(gameObject.transform, ref updateInfo);
            }
        }

        private static void ApplyTransformTrs(Transform tr, ref TransformUpdateInfo updateInfo)
        {
            if (updateInfo.data.enablePosition)
            {
                tr.localPosition = updateInfo.updateData.position;
            }
            if (updateInfo.data.enableRotate)
            {
                tr.localRotation = updateInfo.updateData.rotation;
            }
            if (updateInfo.data.enableScale)
            {
                tr.localScale = updateInfo.updateData.scale;
            }
        }
    }
}