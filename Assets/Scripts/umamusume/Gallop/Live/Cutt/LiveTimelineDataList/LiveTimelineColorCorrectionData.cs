using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyColorCorrectionData : LiveTimelineKeyWithInterpolate
    {
        public int enable;
        public float saturation;  // 1.0 = neutral
        public int mode;
        public AnimationCurve redCurve;
        public AnimationCurve greenCurve;
        public AnimationCurve blueCurve;
        public AnimationCurve depthRedCurve;
        public AnimationCurve depthGreenCurve;
        public AnimationCurve depthBlueCurve;
        public AnimationCurve blendCurve;
        public int selective;
        public Color keyColor;
        public Color targetColor;
    }

    [Serializable]
    public class LiveTimelineKeyColorCorrectionDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyColorCorrectionData>
    {
    }

    [Serializable]
    public class LiveTimelineColorCorrectionData : ILiveTimelineGroupDataWithName
    {
        private const string default_name = "ColorCorrection";
        public LiveTimelineKeyColorCorrectionDataList keys;
    }
}
