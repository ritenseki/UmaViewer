// PostFilm (轨道 39) 的全屏叠加 —— MVP 版本。
//
// 已实现：filmMode 的 None/Lerp/Add/Mul/Monochrome，colorType 全部 4 种布局，
//         Vignette* 三种按「基础混合 × 径向衰减」近似。
// 未实现：depthPower/DepthClip（深度混合）、layerMode=UVMovie（图层贴图）。
//
// 参数由 Director.OnPostFilmUpdate 每帧写入，见 PostFilmRendererFeature.cs。
Shader "Hidden/Gallop/PostFilm"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "PostFilm"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // 两个 include 缺一不可，且顺序不能反：
            //   universal/ShaderLibrary/Core.hlsl —— 定义 TEXTURE2D_X / SAMPLE_TEXTURE2D_X 等 XR 宏
            //   core/Runtime/Utilities/Blit.hlsl  —— 声明 TEXTURE2D_X(_BlitTexture)、全屏三角形 Vert()、
            //                                        并带进 GlobalSamplers.hlsl（sampler_LinearClamp）
            // 注意 Blit.hlsl 在 **core** 包的 Runtime/Utilities/ 下，不在 universal 包的 ShaderLibrary/。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // filmMode: None=0 Lerp=1 Add=2 Mul=3 VignetteLerp=4 VignetteAdd=5 VignetteMul=6 Monochrome=7
            // colorType: ColorAll=0 Color2TopBottom=1 Color2LeftRight=2 Color4=3
            int    _FilmMode;
            int    _ColorType;
            float  _FilmPower;
            float4 _Color0;
            float4 _Color1;
            float4 _Color2;
            float4 _Color3;
            float2 _FilmOffset;
            float2 _FilmScale;
            float  _RollAngle;
            float4 _FilmOption;
            // Vignette 几何参数的标定系数，由 RendererFeature 暴露到 Inspector 供实时调。
            // 原始 filmOptionParam 的单位未知，只能靠观感标定。
            float  _VignetteWidth;     // 缩放作用宽度
            float  _VignetteStrength;  // 缩放整体强度

            // 按 colorType 在屏幕上铺开颜色
            float4 SampleFilmColor(float2 uv)
            {
                if (_ColorType == 1)          // Color2TopBottom
                    return lerp(_Color1, _Color0, saturate(uv.y));
                if (_ColorType == 2)          // Color2LeftRight
                    return lerp(_Color0, _Color1, saturate(uv.x));
                if (_ColorType == 3)          // Color4：双线性四角插值
                {
                    float4 bottom = lerp(_Color2, _Color3, saturate(uv.x));
                    float4 top    = lerp(_Color0, _Color1, saturate(uv.x));
                    return lerp(bottom, top, saturate(uv.y));
                }
                return _Color0;               // ColorAll
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                // _BlitTexture 是 TEXTURE2D_X，必须用 _X 版本采样
                float4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (_FilmMode == 0 || _FilmPower <= 0.0)
                    return src;

                // 颜色采样用的 UV 支持缩放/偏移/旋转（roll）
                float2 cuv = uv - 0.5;
                float s, c;
                sincos(_RollAngle, s, c);
                cuv = float2(cuv.x * c - cuv.y * s, cuv.x * s + cuv.y * c);
                float2 scale = (abs(_FilmScale.x) < 1e-5 || abs(_FilmScale.y) < 1e-5)
                             ? float2(1.0, 1.0) : _FilmScale;
                cuv = cuv / scale + 0.5 + _FilmOffset;

                float4 film = SampleFilmColor(cuv);
                float power = _FilmPower;

                // Vignette 变体：从画面边缘往内衰减。
                // _FilmOption.x = 作用宽度（离边缘多远内为满强度），.y = 过渡柔和度。
                // 依据：song 1177 的实际取值为 (0,0.05) (0.05,0.05) (0.08,0.05) (0.2,0.05)
                // (0.04,0.17) (0.1,0.17) (0.2,0.25)。y<x 的组合排除了「内径/外径」的读法；
                // 而 x=0 在边缘解释下是「只有最外圈 5%」，在径向解释下会变成全屏，后者显然不对。
                // 注意这仍是推断，若观感不符先调这里。
                if (_FilmMode >= 4 && _FilmMode <= 6)
                {
                    // m 归一化到 0(边缘)~1(中心)，这样 _FilmOption.x 才是「占半屏的比例」。
                    // 之前漏了这一步：m 的原始范围是 0~0.5，等于把作用宽度放大了一倍。
                    float2 e = min(uv, 1.0 - uv);
                    float m = min(e.x, e.y) * 2.0;
                    float start = max(_FilmOption.x, 0.0) * _VignetteWidth;
                    float soft  = max(_FilmOption.y * _VignetteWidth, 1e-4);
                    power *= (1.0 - smoothstep(start, start + soft, m)) * _VignetteStrength;
                }

                float3 rgb;
                int mode = _FilmMode;
                if (mode == 7)                                  // Monochrome
                {
                    float lum = dot(src.rgb, float3(0.299, 0.587, 0.114));
                    rgb = lerp(src.rgb, lum * film.rgb, power);
                }
                else if (mode == 2 || mode == 5)                // Add / VignetteAdd
                {
                    rgb = src.rgb + film.rgb * film.a * power;
                }
                else if (mode == 3 || mode == 6)                // Mul / VignetteMul
                {
                    rgb = lerp(src.rgb, src.rgb * film.rgb, film.a * power);
                }
                else                                            // Lerp / VignetteLerp
                {
                    rgb = lerp(src.rgb, film.rgb, film.a * power);
                }

                return float4(rgb, src.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
