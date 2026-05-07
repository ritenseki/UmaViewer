using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyWashLightData : LiveTimelineKeyWithInterpolate
    {
        public float RaycastDistance;
        public float CameraProjectionSide;
        public float CameraProjectionColorPower;
    }

    [Serializable]
    public class LiveTimelineKeyWashLightDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyWashLightData>
    {
    }

    [Serializable]
    public class LiveTimelineWashLightData : ILiveTimelineGroupDataWithName
    {
        private const string default_name = "WashLight";
        public LiveTimelineKeyWashLightDataList keys;
    }
}
