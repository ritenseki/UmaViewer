using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyChromaticAberrationData : LiveTimelineKeyWithInterpolate
    {
        public int isEnable;
        public Vector2 redOffset;
        public Vector2 greenOffset;
        public Vector2 blueOffset;
        public float power;
        public float clip;
        public int effectType;
    }

    [Serializable]
    public class LiveTimelineKeyChromaticAberrationDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyChromaticAberrationData>
    {
    }

    [Serializable]
    public class LiveTimelineChromaticAberrationData : ILiveTimelineGroupDataWithName
    {
        private const string default_name = "ChromaticAberration";
        public LiveTimelineKeyChromaticAberrationDataList keys;
    }
}
