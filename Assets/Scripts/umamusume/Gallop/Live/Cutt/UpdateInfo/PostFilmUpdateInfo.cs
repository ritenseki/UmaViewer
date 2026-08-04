using UnityEngine;

namespace Gallop.Live.Cutt
{
    /// <summary>
    /// PostFilm (39) 的每帧结果。三条轨道 postFilmKeys / postFilm2Keys / postFilm3Keys
    /// 各自独立叠加，用 layerIndex 区分（0/1/2）。
    /// </summary>
    public struct PostFilmUpdateInfo
    {
        public int layerIndex;
        public bool enable;

        public PostFilmMode filmMode;
        public PostColorType colorType;
        public float filmPower;

        public Color color0;
        public Color color1;
        public Color color2;
        public Color color3;

        public Vector2 filmOffsetParam;
        public Vector4 filmOptionParam;
        public Vector2 filmScale;
        public float rollAngle;

        public float depthPower;
        public float depthClip;

        public LiveTimelineKeyPostFilmData.LayerMode layerMode;
        public LiveTimelineKeyPostFilmData.ColorBlend colorBlend;
        public float colorBlendFactor;

        // layerMode == UVMovie 时用；内容资源是 live/uvmovie/gal_uvmovie_<songid>_<NNN>。
        // 寻址规则尚未确定，先透传。
        public int movieResId;
        public int movieFrameOffset;
        public float movieSpeed;
    }

    public delegate void PostFilmUpdateInfoDelegate(ref PostFilmUpdateInfo updateInfo);
}
