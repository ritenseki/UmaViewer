using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeySpotlight3dData : LiveTimelineKeyWithInterpolate
    {
        public bool isActive;
        public Color color;
        public float colorPower;
        public float localHeight;
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale;
        public Vector3 characterPosition;
        public int targetCameraType;
        public int targetCameraIndex;
        public string assetName;
        public int characterIndex;
    }

    [Serializable]
    public class LiveTimelineKeySpotlight3dDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeySpotlight3dData>
    {
    }

    [Serializable]
    public class LiveTimelineSpotlight3dData : ILiveTimelineGroupDataWithName
    {
        private const string default_name = "Spotlight3d";
        public LiveTimelineKeySpotlight3dDataList keys;
    }
}
