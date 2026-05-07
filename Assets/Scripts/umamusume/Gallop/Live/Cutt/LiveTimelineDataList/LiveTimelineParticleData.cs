using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyParticleData : LiveTimelineKeyWithInterpolate
    {
        public float emissionRate;
    }

    [Serializable]
    public class LiveTimelineKeyParticleDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyParticleData>
    {
    }

    [Serializable]
    public class LiveTimelineParticleData : ILiveTimelineGroupDataWithName
    {
        private const string default_name = "Particle";
        public LiveTimelineKeyParticleDataList keys;
    }

    [Serializable]
    public class LiveTimelineKeyParticleGroupData : LiveTimelineKeyWithInterpolate
    {
        public float FlickerLightRate;
        public float FlickerDarkRate;
    }

    [Serializable]
    public class LiveTimelineKeyParticleGroupDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyParticleGroupData>
    {
    }

    [Serializable]
    public class LiveTimelineParticleGroupData : ILiveTimelineGroupDataWithName
    {
        private const string default_name = "ParticleGroup";
        public LiveTimelineKeyParticleGroupDataList keys;
    }
}
