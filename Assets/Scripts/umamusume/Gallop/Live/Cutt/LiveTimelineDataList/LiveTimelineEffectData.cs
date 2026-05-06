using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyEffectData : LiveTimelineKeyWithInterpolate
    {
        public Color color;
        public float colorPower;
        public int ColorProperty;
        public int owner;
        public int occurrenceSpot;
        public string ParentStageObjectName;
        public bool IsAttachProps;
        public int PropsIndex;
        public Vector3 offset;
        public Vector3 offsetAngle;
        public Vector3 offsetScale;
        public string BlinkLightName;
        public int BlinkLightNameHash;
        public int BlinkLightContainerIndex;
        public float BlinkLightBrightnessPower;
        public bool IsAdjustedBlinkLightColor;
        public bool IsSyncBlinkLight;
        public bool IsLinkOwnerPositionX;
        public bool IsLinkOwnerPositionY;
        public bool IsLinkOwnerPositionZ;
        public bool IsLinkOwnerRotate;
    }

    [Serializable]
    public class LiveTimelineKeyEffectDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyEffectData>
    {
    }

    [Serializable]
    public class LiveTimelineEffectData : ILiveTimelineGroupDataWithName
    {
        private const string default_name = "Effect";
        public LiveTimelineKeyEffectDataList keys;
    }
}
