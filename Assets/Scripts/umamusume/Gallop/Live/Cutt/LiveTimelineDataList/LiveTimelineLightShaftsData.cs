using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyLightShaftsData : LiveTimelineKeyWithInterpolate
    {
        public bool enabled;
        public Vector4 speed;
        public Vector4 angle;
        public Vector4 offset;
        public Vector4 alpha;
        public Vector4 alpha2;
        public Vector4 maskAlpha;
        public float maskAnimeTime;
        public Vector2 maskAlphaRange;
        public float scale;
        public bool _isAdjustScale;
    }

    [Serializable]
    public class LiveTimelineKeyLightShaftsDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyLightShaftsData>
    {
    }

    [Serializable]
    public class LiveTimelineLightShaftsData : ILiveTimelineGroupDataWithName
    {
        private const string default_name = "LightShafts";
        public LiveTimelineKeyLightShaftsDataList keys;
    }
}
