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

        public void InitializeStage()
        {
            foreach (GameObject stage_part in _stageObjects)
            {
                var instance = Instantiate(stage_part, transform);
                foreach (var child in instance.GetComponentsInChildren<Transform>(true))
                {
                    if (!StageObjectMap.ContainsKey(child.name))
                    {
                        if (IsTimelineControlledLight(child.name))
                            child.gameObject.SetActive(false);
                        var tmp_name = child.name.Replace("(Clone)", "");
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

        public void UpdateTransform(ref TransformUpdateInfo updateInfo)
        {
            if (updateInfo.data == null)
            {
                return;
            }
            if (StageObjectUnitMap.TryGetValue(updateInfo.data.name, out StageObjectUnit objectUnit))
            {
                foreach(var child in objectUnit.ChildObjects)
                {
                    if (StageObjectMap.TryGetValue(child.name, out GameObject gameObject))
                    {
                        if (updateInfo.data.enablePosition)
                        {
                            gameObject.transform.localPosition = updateInfo.updateData.position;
                        }
                        if (updateInfo.data.enableRotate)
                        {
                            gameObject.transform.localRotation = updateInfo.updateData.rotation;
                        }
                        if (updateInfo.data.enableScale)
                        {
                            gameObject.transform.localScale = updateInfo.updateData.scale;
                        }
                    }
                }
            }
        }
    }
}