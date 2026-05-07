using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyHdrBloomData : LiveTimelineKeyWithInterpolate
    {
        // TODO: fields unconfirmed — no bundle data found in 30-song scan.
        // Likely candidates based on URP Bloom: intensity, threshold.
        public float bloomIntensity;
        public float threshold;
    }

    [Serializable]
    public class LiveTimelineKeyHdrBloomDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyHdrBloomData>
    {
    }

    [Serializable]
    public class LiveTimelineHdrBloomData : ILiveTimelineGroupDataWithName
    {
        private const string default_name = "HdrBloom";
        public LiveTimelineKeyHdrBloomDataList keys;
    }
}
