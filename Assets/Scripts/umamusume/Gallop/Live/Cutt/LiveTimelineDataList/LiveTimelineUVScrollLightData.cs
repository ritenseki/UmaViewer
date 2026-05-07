using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyUVScrollLightData : LiveTimelineKeyWithInterpolate
    {
        public Color mulColor0;
        public Color mulColor1;
        public float colorPower;
        public float scrollOffsetX;
        public float scrollOffsetY;
        public float scrollSpeedX;
        public float scrollSpeedY;
        public int ColorType0;
        public int ColorType1;
        public int CharacterIndex0;
        public int CharacterIndex1;
        public bool IsColorBlend0;
        public bool IsColorBlend1;
        public float ColorBlendRate0;
        public float ColorBlendRate1;
        public Color AltCharaColor0;
        public Color AltCharaColor1;
        public int loopType;
        public int loopCount;
        public int loopExecutedCount;
        public int loopIntervalFrame;
        public bool isPasteLoopUnit;
        public bool isChangeLoopInterpolate;
    }

    [Serializable]
    public class LiveTimelineKeyUVScrollLightDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyUVScrollLightData>
    {
    }

    [Serializable]
    public class LiveTimelineUVScrollLightData : ILiveTimelineGroupDataWithName
    {
        private const string default_name = "UVScrollLight";
        public LiveTimelineKeyUVScrollLightDataList keys;
    }
}
