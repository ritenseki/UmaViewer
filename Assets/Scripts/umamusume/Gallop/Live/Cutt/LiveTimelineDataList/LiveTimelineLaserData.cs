using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyLaserData : LiveTimelineKeyWithInterpolate
    {
        public Vector3 objectPosition;
        public Vector3 objectRotate;
        public Vector3 objectScale;
        public int formation;
        public Vector3 rotate;
        public float degRootYaw;
        public float degLaserPitch;
        public float posInterval;
        public bool blink;
        public float blinkPeriod;
        public float RaycastDistance;
    }

    [Serializable]
    public class LiveTimelineKeyLaserDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyLaserData>
    {
    }

    [Serializable]
    public class LiveTimelineLaserData : ILiveTimelineGroupDataWithName
    {
        private const string default_name = "Laser";
        public LiveTimelineKeyLaserDataList keys;
    }
}
