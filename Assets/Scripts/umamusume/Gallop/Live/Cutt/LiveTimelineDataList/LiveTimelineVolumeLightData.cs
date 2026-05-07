using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyVolumeLightData : LiveTimelineKeyWithInterpolate
    {
        public Vector3 sunPosition;
        public Color color1;
        public float power;
        public float komorebi;
        public float blurRadius;
        public float ColorRate;
        public float ScreenColorPower;
        public float EffectColorPower;
        public bool enable;
        public bool isEnabledBorderClear;
        public string BlinkLightName;
        public int BlinkLightNameHash;
        public int BlinkLightContainerIndex;
        public float BlinkLightBrightnessPower;
        public bool IsAdjustedBlinkLightColor;
    }

    [Serializable]
    public class LiveTimelineKeyVolumeLightDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyVolumeLightData>
    {
    }

    [Serializable]
    public class LiveTimelineVolumeLightData : ILiveTimelineGroupDataWithName
    {
        private const string default_name = "VolumeLight";
        public LiveTimelineKeyVolumeLightDataList keys;
    }
}
