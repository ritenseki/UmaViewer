---
name: Live 轨道全览
description: UmaViewer Live 时间轴全部103条轨道的ID、名称、作用、实现状态及已知Bug（2026-05-07核查）
type: reference
originSessionId: 319ae839-0292-4b1e-80de-2f3c91b1f40e
---
# Live 轨道全览（LiveTimelineKeyDataType 枚举，ID 0–102）

数据来源：LiveTimelineDefine.cs 枚举 + WorkSheet CS字段核查 + scan_full.py全量扫描（58首歌，2026-05-07）+ Director/Control代码审查

> **术语**：下文「WorkSheet 有/无 XXX 字段」指的是**游戏 bundle 的 TypeTree**，不是本仓库的 C# `LiveTimelineWorkSheet` 类。两者是分开的：`postFilmKeys`、`radialBlurKeys`、`tiltShiftKeys`、`flashPlayerKeys`、`charaFootLightKeys`、`MultiLightShadowKeys`、`lightProjectionList`、`postFilm1MultiCameraKeys`、`postEffectDOFKeys` 在 bundle 里有，在 C# 里**都还没声明**。
>
> **覆盖率数字同理**——「N/58 首」量的是 bundle 里有没有数据，不代表 C# 收得到。分发层的过滤可能让有数据的轨道照样落空（BgColor1 就是如此，见 ID 8）。
>
> ## ⚠️ 2026-08-04：旧扫描数据不可信，已用新工具全量重测
>
> `tools/` 下的 dump 工具链已重建（见 `CLAUDE.md`）。cutt bundle 自带完整 TypeTree，**字段名和 keyframe 数是权威的**。全 59 首、70 张 worksheet 共享同一套 schema（128 字段），无版本漂移。
>
> 重测结果与本文件原有记录冲突严重，**下表中带「✅实测」标记的才是可信数据**，其余未标记的行仍是旧扫描结论，谨慎对待。两类系统性错误：
>
> 1. **「WorkSheet无XXX字段」全部是错的** —— 那些字段都存在，而且多数有大量数据（见 ID 26/27/51/52/59/70/77/84/85/98/99）
> 2. **「全0 keyframe空占位」多数是错的** —— RadialBlur/PostFilm/TiltShift/FlashPlayer/CharaFootLight/MultiLightShadow 实测都有真实数据（见 ID 15/39/63/66/72/83）
>
> 真正确认为空的只有：`hdrBloomKeys`、`other4EyeTrackKeys`、`ScreenCaptureDataList`、`tailMotionDataList`（0/59 首）。
>
> 复查命令：`~/.venvs/umatools/bin/python3 tools/dump_cutt_typetree.py --fields <songid>`；全量表在 `tools/out/scan_summary.txt`。

**实现状态图例**
- ✅ 完整
- ⚠️ 部分实现（有TODO）
- ⚠️🔇 仅数据（无视觉输出）
- 🔴 Bug（声明但从不触发）
- ❌ 未实现

**★** = song 1001 有真实数据（括号内条数）

---

## 摄像机 / 基础轨道

| ID | 名称 | 作用 | 状态 | Bug |
|----|------|------|------|-----|
| 0 | Timescale | 播放倍速 | ✅ Control内部 | — |
| 1 | CameraPos | 主摄像机位置 | ✅ | — |
| 2 | CameraLookAt | 主摄像机朝向 | ✅ | — |
| 3 | CameraFov | 视野角 | ✅ | — |
| 4 | CameraRoll | 摄像机滚转 | ✅ | — |
| 5 | HandShakeCamera | 手持抖动效果 | ❌ | ✅实测 `handShakeCameraKeys` 56/59首 / 594 keys |
| 6 | Event | 时间轴触发事件 | ❌ | — |
| 7 | CharaMotionSequence ★(5) | 角色动作序列切换 | ✅ | — |
| 8 | BgColor1 ★(13) | 角色**和舞台物件**的色调/轮廓/阴影颜色 | ⚠️ | 曾误标 ✅。`AlterUpdate_BgColor1` 原本用 `validBgColorNames` 只放行 4 个角色组，其余全丢——实测约 92% 的 key 被丢弃（全语料 43617 keys / 59首，是最大的轨道）。2026-08-04 已拆掉过滤并加舞台分支，**舞台侧写 `_MulColor0`**（运行时枚举 shader 确认，每个舞台 shader 都有；`_AmbientColor` 归 BgColor2，两者是同一 shader 上的独立通道）。song 1177 的 20 组里 16 组已正常写入，仍未解析：`BgBL`(256 keys)、`FollowSpotColor`(16)、`Shadow`(2)、`mob_00`(2) —— 这 4 个不是物件名，疑为全局通道 |
| 9 | BgColor2 ★(1) | 背景渐变/环境色（两色混合）【15/58首】 | ✅ | 写含 `_AmbientColor` 属性的舞台材质，颜色 = `Lerp(color1, color2, value)`；color2 恒为白色 |
| 10 | MonitorControl ★(2) | LED舞台屏幕内容控制 | ❌ | ✅实测 `monitorControlList` **55/59首 / 3911 keys**。**舞台上的黑色大屏就是它没实现导致的**：monitor 材质的 `_MainTex`/`_FadeTex`/`_FilterTex` 三个贴图槽在 bundle 里全未绑定，内容靠运行时喂。字段：`dispID` / `speed` / `outputTextureLabel` / `blendFactor` / `Src·DstBlendMode` / `RenderQueueNo` / `LightImageNo` / `CrossFadeRate` / `FilterTexScale`。**内容资源已定位**：`live/uvmovie/gal_uvmovie_<songid>_<001..017>`（图片序列，带 `_tex_NN`），另有 `gal_uvmovie_<songid>_light` 对应 `LightImageNo`。全库 219 个 uvmovie 目录。**但 dispID→资源的映射未解**：song 1177 的 key 是 `dispID=1`，却既没有 `gal_uvmovie_1177_001`，也没有 monitorCamera 数据（`monitorCameraPosKeys` 0 keys），两条路都不通 |
| 11 | CameraSwitcher | 切换活动摄像机 | ✅ | — |
| 12 | LipSync | 口型同步 | ✅ | — |
| 13 | PostEffectDOF | 景深模糊（单机） | ❌ | ✅实测 `postEffectDOFKeys` **59/59首 / 3879 keys**。URP 有内置 DepthOfField，属路径 A |
| 14 | PostEffectBloomDiffusion | 泛光扩散（单机） | ❌ | ✅实测 `postEffectBloomDiffusionKeys` **59/59首 / 1969 keys** |
| 15 | RadialBlur | 径向运动模糊（单机） | ❌ | ✅实测 `radialBlurKeys` **59/59首 / 1667 keys**（单曲最多95）。旧记录「全部0 keyframe」是错的 |
| 16 | CameraLayer | 摄像机渲染层遮罩 | ❌ | ✅实测 `cameraLayerKeys` **58/59首 / 3306 keys** |
| 17 | Projector ★(4) | 舞台投影仪（gobo图案） | ❌ | — |

---

## 角色表情 / 动作

| ID | 名称 | 作用 | 状态 | Bug |
|----|------|------|------|-----|
| 18 | FacialFace | 面部整体表情 | ✅ AlterUpdate_FacialData | — |
| 19 | FacialMouth | 嘴部表情 | ✅ | — |
| 20 | FacialCheek | 脸颊表情 | ✅ | — |
| 21 | FacialEye | 眼部表情 | ✅ | — |
| 22 | FacialEyebrow | 眉毛 | ✅ | — |
| 23 | FacialEyeTrack | 眼球追踪目标 | ✅ | — |
| 24 | FacialEar | 耳部动画 | ✅ | — |
| 25 | FacialEffect | 表情叠加特效 | ✅ | — |
| 26 | FacialNoise | 表情随机扰动 | ❌ | ✅实测 `facialNoiseKeys` **存在**，50/59首 / 127 keys。旧记录「无字段」是错的 |
| 27 | CharaMotionNoise | 动作随机扰动 | ❌ | ✅实测 `charaMotionNoiseKeys` **存在**，**59/59首** / 135 keys。旧记录「无字段」是错的 |
| 28 | FormationOffset | 角色队形位置偏移 | ✅ | — |
| 29 | Animation ★(1) | GameObject Animation组件序列 | ❌ | ✅实测 `animationList` 39/59首 / 1575 keys（单曲最多218），比 ★(1) 高得多 |
| 30 | TextureAnimation | 材质纹理帧动画 | ❌ | — |

---

## 舞台物件

| ID | 名称 | 作用 | 状态 | Bug |
|----|------|------|------|-----|
| 31 | Transform | 舞台物件位移/旋转/缩放 | ✅ StageController | — |
| 32 | Renderer | 舞台Renderer启用/禁用 | ❌ | — |
| 33 | Object ★(10) | 舞台GameObject激活/隐藏**+ 位移** | ✅ StageController | 2026-08-04 修了两个静默失效：① 组名可能是 `_stageObjectUnits` 的**单元名**而非 GameObject 名（live10149 的 `neonsign` 就是 unit，底下挂 3 个子对象；没有任何 GameObject 叫 neonsign，整条轨道此前从不触发，灯牌不会从吊顶降下）；② `OffsetType` 字段分发层一直在填但 `UpdateObject` 从不读，`Add` 语义被当绝对坐标写，物件被挪到父物体原点。现按 `Direct`/`Add` 分别处理，`Add` 在物件原始 local TRS 上叠加 |
| 34 | Audience ★(12) | 观众人群动画参数 | ❌ | — |
| 35 | Props | 舞台道具激活控制 | ❌ | ✅实测 `propsList` 28/59首 / 1940 keys（单曲最多567） |
| 36 | PropsAttach | 角色附着道具 | ❌ | ✅实测 `propsAttachList` 28/59首 / 298 keys |

---

## 灯光 / 特效

| ID | 名称 | 作用 | 状态 | Bug |
|----|------|------|------|-----|
| 37 | VolumeLight ★(1) | 体积光/SunShafts | ⚠️🔇 | 无SunShafts组件，仅数据。✅实测 `volumeLightKeys` **58/59首 / 2048 keys** |
| 38 | HdrBloom | HDR泛光强度/阈值 | ⚠️🔇 | 0/58首有数据，**但已经实现了**（Director 有 handler，走 URP Bloom）→ 是本仓库里的死代码，建议删。song 1177 实测 `hdrBloomKeys` 为空 |
| 39 | PostFilm | 全屏后期叠加（晕影/颜色/UV电影） | ⚠️🔇 | ✅实测三字段全有大量数据：`postFilmKeys` 59/59首6689 keys、`postFilm2Keys` 59/59首**7290 keys**、`postFilm3Keys` 59/59首6240 keys（song 1177 为 100/161/87）。旧记录「全部0 keyframe」是错的。**2026-08-05 已完成数据层**：数据类字段按 TypeTree 顺序重排并补齐 5 个 `BlinkLight*`、新增 `LiveTimelineKeyPostFilmDataList`、WorkSheet 三字段、`AlterUpdate_PostFilm` 分发与插值、`PostFilmUpdateInfo`、`PostFilmRendererFeature` + `Resources/Shaders/PostFilm.shader`。**渲染层默认关闭**（`PostFilmRendererFeature._enableRendering = false`）：song 1177 的 100 个 key 全是 Vignette 变体，而 `filmOptionParam`（取值如 (0.2,0.05)/(0,0.05)/(0.2,0.25)）的几何语义**无法从资源中还原** —— 游戏自己的 PostFilm shader 不在 `shader` bundle 里（499 个 shader 按名字和属性签名都搜过），应在 app 本体的 resources.assets。要继续做只能对着参考视频标定 Width/Strength 两个系数 |
| 40 | Fade | 场景淡入淡出 | ❌ | ✅实测 `fadeKeys` **59/59首** / 243 keys。量小但覆盖全 |
| 41 | Particle ★(3) | 粒子系统发射速率 | ✅ | — |
| 42 | ParticleGroup ★(3) | 粒子组闪烁速率（双速率曲线） | ✅ | — |
| 43 | WashLight | 洗光灯 SetActive+颜色亮度 | ⚠️ | RaycastDistance/CameraProjectionSide/MulColor0未使用。**低优先级**：song 1177 仅 1 组 11 keys，真正的"洗光"效果由 ID 45 的 `*_wash_*` 组承担。字段名 `WashLightList`（大写 W）已用 A/B 探针确认；上游 katboi01 写作 `washLightList`，永远填不上，其 WashLightController 是死代码 |
| 44 | Laser | 激光灯变换+SetActive | ⚠️ | blinkRate/blinkOffset/rotateFollowCamera/RaycastDistance未实现。song 1177 该轨道为空 |
| 45 | BlinkLight ★(9) | 频闪灯亮灭周期/颜色/亮度 | ⚠️ | **灯光线最大权重**：全语料 19420 keys / 57首；song 1177 实测 27 组，一半以上组名是 `*_wash_*`。★(9) 是 song 1001 的数字，严重低估。2026-08-04 修：写入属性从 `_Color`（这些 shader 上不存在，写入是空操作，灯全渲染成黑）改为按存在性探测 `_BlinkLightColor`/`_MulColor0`/`_Color`。**仍未解**：`color0Array`/`powerArray` 恒为 10 项（调色板），与 renderer 数（可达 570）无关，灯→槽位的映射规则未逆出，暂统一取第 0 槽；`pattern` 语义未逆出，逐灯相位（U闪→M闪→A闪）未实现；`color1Array`/`LightBlendMode`/`isReverseHueArray` 未用。另注意部分 renderer 用 `StageTransmittedLightMask`/`StageMirrorBallShine`，**shader 里一个 Color 属性都没有**，BlinkLight 点不亮（对应未实现的 95/96 TransmittedLight 轨道） |
| 46 | UVScrollLight ★(1) | 材质UV滚动灯光效果 | ⚠️ | mulColor1/ColorType/CharacterIndex等未使用 |
| 47 | FacialToon | 角色卡通着色参数 | ❌ | ✅实测 `facialToonSet` **59/59首 / 6341 keys**（单曲最多600）。未实现轨道里数据量第二大 |
| 48 | GlobalLight ★(2) | 全局光方向/RimLight参数 | ✅ | — |
| 49 | GlobalFog ★(1) | 全局雾效（颜色/密度/范围） | ✅ | — |
| 50 | LightShafts ★(1) | 光轴/丁达尔效果 | ⚠️🔇 | 无LightShaftsController组件，仅数据 |
| 51 | MonitorCameraPos ★(1) | 舞台监控摄像机位置 | ❌ | ✅实测 `monitorCameraPosKeys` **存在**，34/59首 / 964 keys。旧记录「无字段」是错的。用途推测是 LED 大屏的副摄像机实时画面（IMAG），与 ID 10 配套 |
| 52 | MonitorCameraLookAt ★(1) | 舞台监控摄像机朝向 | ❌ | ✅实测 `monitorCameraLookAtKeys` **存在**，34/59首 / 971 keys。旧记录「无字段」是错的 |
| 53 | MultiCameraPos ★(2) | 多机位摄像机位置 | ✅ | — |
| 54 | MultiCameraLookAt ★(2) | 多机位摄像机朝向 | ✅ | — |
| 55 | EyeCameraPos | 眼部特写摄像机位置 | ❌ | — |
| 56 | EyeCameraLookAt | 眼部特写摄像机朝向 | ❌ | — |
| 57 | LensFlare | 镜头光晕 | ❌ | ✅实测 `lensFlareList` 45/59首 / 993 keys（单曲最多181）。舞台用 `CustomLensFlare` 脚本无源码；可先做 SetActive 层 |
| 58 | Environment ★(1) | 地板镜面反射/水面波纹/阴影/FovShift | ❌ | ✅实测 `environmentDataLists` 49/59首 / 1375 keys。**注意 schema 里另有独立字段 `MirrorReflectionDataList`**，两者不是一回事（上游 katboi01 把它俩混为一谈，按 name 分流）。核心是 Planar Reflection，工程量大 |
| 59 | SweatLocator ★(3) | 角色汗珠特效挂点 | ❌ | ✅实测 `sweatLocatorList` **存在**，32/59首 / 608 keys。旧记录「无字段」是错的 |
| 60 | Effect ★(8) | 特效Prefab加载/播放（3d/effect/live/） | ✅ | — |
| 61 | ColorCorrection | 色彩校正（饱和度/RGB曲线） | ⚠️ | depthRedCurve/Green/Blue/blendCurve/mode/selective/keyColor/targetColor无URP对应 |
| 62 | PreColorCorrection | 前置色彩校正 | ❌ | — |
| 63 | TiltShift | 移轴模糊 | ❌ | ✅实测 `tiltShiftKeys` **58/59首 / 1202 keys**（单曲最多127）。旧记录「全部0 keyframe」是错的 |
| 64 | A2U | 内部动作系统参数 | ❌ | 非视觉轨道 |
| 65 | A2UConfig | A2U配置 | ❌ | 非视觉轨道 |
| 66 | FlashPlayer | 全屏闪光播放 | ❌ | ✅实测 `flashPlayerKeys` 20/59首 / 50 keys。旧记录「全部0 keyframe」是错的，但确实量小 |
| 67 | Title | 歌曲名字幕显示 | ❌ | ✅实测 `titleKeys` 55/59首 / 137 keys |
| 68 | Spotlight3d ★(3) | 3D聚光灯 SetActive+颜色/强度 | ✅ | — |
| 69 | CharaNode | 角色骨骼节点位置控制 | ❌ | — |
| 70 | NodeScale ★(2) | 舞台节点缩放 | ❌ | ✅实测 `nodeScaleList` **存在**，**57/59首** / 541 keys。旧记录「无字段」是错的，且覆盖率远高于原估计 |
| 71 | Fluctuation | 画面波动/震颤效果 | ❌ | ✅实测 `FluctuationKeys` 51/59首 / 330 keys |
| 72 | CharaFootLight | 角色脚部补光灯 | ❌ | ✅实测 `charaFootLightKeys` **49/59首 / 340 keys**。旧记录「全部0 keyframe」是错的 |
| 73 | ChromaticAberration ★(1) | 色差后处理 | ⚠️ | redOffset/greenOffset/blueOffset（通道偏移）无URP内置对应；effectType未使用 |
| 74 | LightProjection ★(18!) | 投影灯（Gobo图案灯） | ❌ | ✅实测 `lightProjectionList` **37/59首 / 3793 keys**（单曲最多566）。URP 下 `Projector` 组件不工作，需 DecalProjector 或自写 |
| 75 | Billboard | 广告牌（始终面向摄像机的面片）【12/58首】 | ❌ | — |

---

## 多机位后处理

| ID | 名称 | 作用 | 状态 | Bug |
|----|------|------|------|-----|
| 76 | MultiCameraPostFilm ★(2) | 多机位全屏后期叠加 | ❌ | ✅实测 `postFilm1MultiCameraKeys` 51/59首 / 708 keys |
| 77 | MultiCameraPostEffectBloomDiffusion ★(2) | 多机位泛光 | ❌ | ✅实测 `postEffectBloomDiffusionMultiCameraKeys` **存在**，49/59首 / 381 keys。旧记录「无字段」是错的 |
| 78 | MultiCameraColorCorrection | 多机位色彩校正 | ❌ | — |
| 79 | MultiCameraTiltShift | 多机位移轴模糊 | ❌ | — |
| 80 | MultiCameraRadialBlur | 多机位径向模糊 | ❌ | — |
| 81 | MultiCameraPostEffectDOF | 多机位景深 | ❌ | — |

---

## 附加灯光 / 系统

| ID | 名称 | 作用 | 状态 | Bug |
|----|------|------|------|-----|
| 82 | AdditionalLight | 附加动态光源【22/58首】 | ❌ | — |
| 83 | MultiLightShadow | 多光源阴影 | ❌ | ✅实测 `MultiLightShadowKeys` 37/59首 / 183 keys。旧记录「全部0 keyframe」是错的 |
| 84 | MobControl ★(4) | 观众人群行为/动画控制 | ❌ | ✅实测 `MobControlKeys` **存在**，32/59首 / **2392 keys**（单曲最多495）。旧记录「无字段」是错的 |
| 85 | CyalumeControl ★(4) | 荧光棒颜色/同步控制 | ❌ | ✅实测 `CyalumeControlKeys` **存在**，33/59首 / **2372 keys**（单曲最多496）。旧记录「无字段」是错的。`CyalumeController3D.cs` 已存在，只差接时间轴 |
| 86 | CameraMotion | 摄像机动作曲线轨道 | ❌ | — |
| 87 | WaveObject | 波浪变形物体 | ❌ | — |
| 88 | CharaWind | 角色风效（头发/裙摆） | ❌ | ✅实测 `charaWind` 24/59首 / 744 keys |
| 89 | CharaParts | 角色部件显隐控制 | ❌ | — |
| 90 | CameraCutNo | 镜头编号标记（元数据） | ❌ | — |
| 91 | ToneCurve | 色调曲线（ACES/自定义） | ❌ | — |
| 92 | Exposure | 曝光值控制 | ❌ | — |
| 93 | Vortex | 旋涡/扭曲屏幕效果 | ❌ | — |
| 94 | CharaCollision | 角色碰撞辅助（非视觉） | ❌ | — |
| 95 | TransmittedLight | 透射光（次表面散射） | ❌ | — |
| 96 | TransmittedLightMask | 透射光遮罩 | ❌ | — |
| 97 | Voice | 语音切换控制 | ❌ | — |
| 98 | LipSyncPatternRange | 口型模式范围定义 | ❌ | ✅实测 `LipSyncPatternRangeKeys` **存在** |
| 99 | LipSyncPattern | 口型模式数据 | ❌ | ✅实测 `LipSyncPatternDataList` **存在** |
| 100 | LensDistortion | 镜头桶形/枕形畸变 | ❌ | — |
| 101 | CharaNodeOffset | 角色骨骼节点偏移 | ❌ | — |
| 102 | TransparentCamera | 透明度摄像机控制 | ❌ | — |

---

## 统计摘要

| 状态 | 数量 |
|------|------|
| ✅ 完整 | 21 |
| ⚠️ 部分实现 | 7 |
| ⚠️🔇 仅数据 | 2 |
| 🔴 Bug（已修）| 1 |
| ❌ 未实现 | 72 |

## 已知未解问题（2026-08-05）

按「是否有 ground truth」分类。**缺依据的一律不实现**，代码里保留 TODO 注释而不是猜一个值上去。

### 有依据、可以直接做

| 问题 | 依据 |
|---|---|
| ~~BlinkLight 调色板槽位映射~~ ✅ 2026-08-05 解决 | 槽位号 = 渲染器自身或最近祖先的 `lightNNN_`/`alphaNNN_` 名字前缀。live10149 全组实测「前缀种数 N」与「槽 0..N-1 的去重颜色数」精确相等，槽 N..9 一律白色填充，7 组无一例外。此前统一取第 0 槽，于是镜面球整个发粉红、truss wash 丢掉黄绿蓝紫四色 |
| BlinkLight `pattern` 语义 | 实测有 0 和 2 两种；相位公式是推测，`pattern != 0` 时按 `cycle * i / N` 错开 |
| BgColor1 剩余 4 组 | `BgBL`(256 keys) / `FollowSpotColor`(16) / `Shadow`(2) / `mob_00`(2) 不是物件名也不是材质名 |
| BgColor1 的 `power` → `_ColorPower`? | `_ColorPower` 属性确实存在，但映射关系无证据，已撤回 |

### 缺 ground truth，已禁用

| 问题 | 为什么查不到 |
|---|---|
| PostFilm 的 `filmOptionParam` 几何语义 | 游戏自己的 PostFilm shader 不在 `shader` bundle（499 个按名字和属性签名都搜过），应在 app 本体 `resources.assets` |
| MonitorControl 的 `dispID` → 资源 | 内容资源族已定位为 `live/uvmovie/gal_uvmovie_<songid>_<NNN>`（全库 219 个目录），但 song 1177 只有 `_light` 没有 `_001`，且 `monitorCameraPosKeys` 为 0，两条路都不通 |

### 与时间轴无关的渲染问题

| 现象 | 状态 |
|---|---|
| 舞台地板长期偏亮发白 | ✅ 2026-08-06 解决。**白的不是文档里记的那三个物件** —— 是与 `plane_000` 同位同尺度、叠在一起的 `mirror_a`（同在 `pfb_env_live10149_main000` 下，局部变换全为单位）。它用 `Cygames/MirrorAndShadow/ReceiveMirror`，`_ReflectionRate` 默认 1.0 而 `_ReflectionTex` 从没赋值，Unity 对未绑定采样器代入白贴图 = 满强度白反射。实现 `Gallop.MirrorReflection` 接上反射 RT 后恢复正常。**教训见 `CLAUDE.md`「现象描述不是 ground truth」**：整轮排查都围着文档里那三个名字转，那个「`_EnvRate` 归零无变化」的实验切的是另一批物件 |
| 小灯泡渲染成黑块 | 用 `StageTransmittedLightMask` / `StageMirrorBallShine`，这两个 shader **没有任何颜色属性**，BlinkLight 点不亮。疑为 URP 移植问题，对应未实现的 95/96 TransmittedLight 轨道 |
| LED 大屏黑屏 | MonitorControl (10) 未实现，见上 |

---

## 优先级排序

**2026-08-04 用 `tools/` 全量重测后重排。** 依据 = 实测数据量 × 覆盖率 × 工作性质。
全量表：`tools/out/scan_summary.txt`。

### 第一优先：数据早就在手，只是没用上（性价比最高）

| 轨道 | 实测数据量 | 难度 | 说明 |
|---|---|---|---|
| **BgColor1 (8) 舞台分支** | **43617 keys / 59首** | ★☆☆~★★☆ | 全语料最大的轨道。分发过滤已拆，剩下确认舞台侧 shader 属性 |
| **BlinkLight (45) 补字段** | **19420 keys / 57首** | ★★☆ | 灯光线最大权重，含全部 `*_wash_*` 效果 |
| Laser (44) / UVScrollLight (46) 补字段 | 2661 keys / 33首 | ★☆☆ | 纯管道 |

### 第二优先：URP 后处理 Feature（路径见 `live-shader-todo.md`）

| 轨道 | 实测数据量 | 路径 |
|---|---|---|
| **PostFilm (39)** | **~20200 keys / 59首**（三个字段合计） | B，最大的一块 |
| PostEffectDOF (13) | 3879 keys / 59首 | A（URP 内置 DepthOfField） |
| VolumeLight/SunShafts (37) | 2048 keys / 58首 | B，数据类已有，字段表齐全 |
| PostEffectBloomDiffusion (14) | 1969 keys / 59首 | B |
| RadialBlur (15) | 1667 keys / 59首 | B |
| TiltShift (63) | 1202 keys / 58首 | B |

### 第三优先：只差 C# 字段声明 + handler（bundle 里数据齐备）

FacialToon(47, **6341 keys/59首**) > MonitorControl(10, 3911/55首，dispID 语义仍未知) > CameraLayer(16, 3306/58首) > MobControl(84, 2392/32首) > CyalumeControl(85, 2372/33首，`CyalumeController3D.cs` 已存在) > Props(35, 1940/28首) > Animation(29, 1575/39首) > MonitorCameraPos/LookAt(51/52, ~1900/34首) > CharaWind(88, 744/24首) > AdditionalLight(82, 657/23首) > SweatLocator(59, 608/32首) > NodeScale(70, 541/57首) > Fluctuation(71, 330/51首) > CharaFootLight(72, 340/49首) > Fade(40, 243/59首) > Title(67, 137/55首) > FacialNoise(26)/CharaMotionNoise(27)

### 第四优先：要从零搭渲染系统 ★★★

LightProjection(74, 3793/37首，URP 下 Projector 不工作) > Environment(58, 1375/49首，注意还有独立的 `MirrorReflectionDataList`；**Planar Reflection 本身已不用从零搭** —— `Gallop.MirrorReflection` 已实现，缺的是把轨道关键帧接上去) > LensFlare(57, 993/45首，可先做 SetActive 层；`CustomLensFlare` 的字段可按签名重建拿到)

### 不该做

**HdrBloom (38)** —— 0/59 首，却已经实现了，是死代码，建议删。
其余确认为空：`other4EyeTrackKeys`、`ScreenCaptureDataList`、`tailMotionDataList`。
A2U / A2UConfig / CameraCutNo —— 非视觉轨道。

> 之前「PostFilm 到底有没有数据」的矛盾已解决：**`live-shader-todo.md` 是对的**，本文件旧版记的「全部 0 keyframe」是错的。
