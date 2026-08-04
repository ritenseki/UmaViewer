using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    /// <summary>
    /// PostFilm (39) 的 URP 实现。全语料 ~20200 keys / 59 首，是数据量最大的未实现轨道。
    ///
    /// 用法：把本 Feature 加到 Assets/Resources/RenderPipeline/UMAUniversalRenderPipelineAsset_Renderer.asset
    /// 的 Renderer Features 列表里（一次性操作）。shader 从 Resources 载入，不需要在 Inspector 指派。
    ///
    /// Director 每帧通过静态的 Layers 写入参数；三条轨道
    /// postFilmKeys / postFilm2Keys / postFilm3Keys 各占一层，按 0→1→2 顺序叠加。
    /// </summary>
    public class PostFilmRendererFeature : ScriptableRendererFeature
    {
        public const int kLayerCount = 3;

        [Serializable]
        public struct LayerState
        {
            public bool enable;
            public PostFilmMode filmMode;
            public PostColorType colorType;
            public float filmPower;
            public Color color0, color1, color2, color3;
            public Vector2 filmOffset;
            public Vector2 filmScale;
            public float rollAngle;
            public Vector4 filmOption;
        }

        /// <summary>Director 每帧写这里；Feature 只读。索引 = PostFilmUpdateInfo.layerIndex。</summary>
        public static readonly LayerState[] Layers = new LayerState[kLayerCount];

        /// <summary>Live 结束时调用，避免参数残留到下一首歌。</summary>
        public static void ResetLayers()
        {
            for (int i = 0; i < kLayerCount; i++) Layers[i] = default;
        }

        [SerializeField] private RenderPassEvent _passEvent = RenderPassEvent.AfterRenderingPostProcessing;

        // ───────────────────────────────────────────────────────────────────────
        // 默认关闭：渲染部分缺少 ground truth，开着会画错。
        //
        // 已验证的部分（保留并启用）：字段名/类型、三条轨道的关键帧解析、分发与插值、
        //   filmMode / colorType 的枚举取值（song 1177 实测 100/161/87 keys）。
        //
        // 未验证的部分（就是它导致必须关闭）：filmOptionParam 的单位与几何含义。
        //   song 1177 的 100 个 key 全是 Vignette 变体，取值形如 (0.2,0.05) (0,0.05) (0.2,0.25)，
        //   但「0.2」对应屏幕上多大范围无从得知——定义在游戏自己的 PostFilm shader 里，
        //   而全库 499 个 shader（`shader` bundle）按名字和属性签名都搜过，**没有这个 shader**，
        //   它应该编在 app 本体的 resources.assets 里，我们拿不到。
        //   没有它，vignette 衰减只能靠观感标定，猜了两版都不对（一版全屏泛色、一版大边框）。
        //
        // 要继续做：对着参考视频找一帧 vignette 明显的画面，勾上下面的开关，
        //   调 Width/Strength 直到吻合，然后把数值写死进代码并注明标定依据。
        // ───────────────────────────────────────────────────────────────────────
        [Header("⚠ 渲染部分未标定，默认关闭")]
        [Tooltip("勾上才会实际绘制。filmOptionParam 的几何语义未知，开启后画面大概率不准。")]
        [SerializeField] private bool _enableRendering = false;

        [Header("Vignette 标定（仅在上面勾选后有意义）")]
        [Tooltip("作用宽度系数。filmOptionParam.x 乘以它后，1.0 = 从边缘一直吃到画面中心。调小 = 边框更窄。")]
        [Range(0.02f, 1f)]
        [SerializeField] private float _vignetteWidth = 0.25f;

        [Tooltip("整体强度系数。调小 = 边缘颜色更淡。")]
        [Range(0f, 2f)]
        [SerializeField] private float _vignetteStrength = 1f;

        private PostFilmPass _pass;
        private Material _material;

        public override void Create()
        {
            if (_material == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/PostFilm");
                if (shader == null)
                {
                    Debug.LogError("[PostFilm] 找不到 Resources/Shaders/PostFilm，Feature 不会生效");
                    return;
                }
                _material = CoreUtils.CreateEngineMaterial(shader);
            }
            _pass = new PostFilmPass(_material) { renderPassEvent = _passEvent };
            _pass.SetCalibration(_vignetteWidth, _vignetteStrength);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // 渲染默认关闭，理由见字段声明处。数据层与分发层仍然照常跑，
            // Director 的 [PostFilm] 日志不受影响，方便继续查参数。
            if (!_enableRendering) return;
            if (_pass == null || _material == null) return;
            // 每帧同步，Inspector 里拖动能立刻在 Play 模式下看到变化
            _pass.SetCalibration(_vignetteWidth, _vignetteStrength);
            if (renderingData.cameraData.cameraType != CameraType.Game &&
                renderingData.cameraData.cameraType != CameraType.SceneView) return;

            bool anyActive = false;
            for (int i = 0; i < kLayerCount; i++)
            {
                if (Layers[i].enable && Layers[i].filmMode != PostFilmMode.None && Layers[i].filmPower > 0f)
                {
                    anyActive = true;
                    break;
                }
            }
            if (!anyActive) return;

            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            _pass?.Dispose();
            CoreUtils.Destroy(_material);
            _material = null;
        }

        private class PostFilmPass : ScriptableRenderPass
        {
            private static readonly int kFilmMode   = Shader.PropertyToID("_FilmMode");
            private static readonly int kColorType  = Shader.PropertyToID("_ColorType");
            private static readonly int kFilmPower  = Shader.PropertyToID("_FilmPower");
            private static readonly int kColor0     = Shader.PropertyToID("_Color0");
            private static readonly int kColor1     = Shader.PropertyToID("_Color1");
            private static readonly int kColor2     = Shader.PropertyToID("_Color2");
            private static readonly int kColor3     = Shader.PropertyToID("_Color3");
            private static readonly int kFilmOffset = Shader.PropertyToID("_FilmOffset");
            private static readonly int kFilmScale  = Shader.PropertyToID("_FilmScale");
            private static readonly int kRollAngle  = Shader.PropertyToID("_RollAngle");
            private static readonly int kFilmOption = Shader.PropertyToID("_FilmOption");

            private static readonly int kVigWidth    = Shader.PropertyToID("_VignetteWidth");
            private static readonly int kVigStrength = Shader.PropertyToID("_VignetteStrength");

            private readonly Material _material;
            private RTHandle _temp;
            private float _vigWidth = 0.25f;
            private float _vigStrength = 1f;

            public PostFilmPass(Material material)
            {
                _material = material;
                profilingSampler = new ProfilingSampler("PostFilm");
            }

            public void SetCalibration(float width, float strength)
            {
                _vigWidth = width;
                _vigStrength = strength;
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;
                RenderingUtils.ReAllocateIfNeeded(ref _temp, desc, FilterMode.Bilinear,
                    TextureWrapMode.Clamp, name: "_PostFilmTemp");
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_material == null || _temp == null) return;

                RTHandle src = renderingData.cameraData.renderer.cameraColorTargetHandle;
                if (src == null) return;

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, profilingSampler))
                {
                    for (int i = 0; i < kLayerCount; i++)
                    {
                        LayerState s = Layers[i];
                        if (!s.enable || s.filmMode == PostFilmMode.None || s.filmPower <= 0f) continue;

                        _material.SetInt(kFilmMode, (int)s.filmMode);
                        _material.SetInt(kColorType, (int)s.colorType);
                        _material.SetFloat(kFilmPower, s.filmPower);
                        _material.SetColor(kColor0, s.color0);
                        _material.SetColor(kColor1, s.color1);
                        _material.SetColor(kColor2, s.color2);
                        _material.SetColor(kColor3, s.color3);
                        _material.SetVector(kFilmOffset, s.filmOffset);
                        _material.SetVector(kFilmScale, s.filmScale);
                        _material.SetFloat(kRollAngle, s.rollAngle);
                        _material.SetVector(kFilmOption, s.filmOption);
                        _material.SetFloat(kVigWidth, _vigWidth);
                        _material.SetFloat(kVigStrength, _vigStrength);

                        // 不能把 RT 直接 blit 回自身，走一次临时 RT。
                        Blitter.BlitCameraTexture(cmd, src, _temp, _material, 0);
                        Blitter.BlitCameraTexture(cmd, _temp, src);
                    }
                }
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }

            public void Dispose()
            {
                _temp?.Release();
                _temp = null;
            }
        }
    }
}
