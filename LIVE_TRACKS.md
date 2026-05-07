---
name: Live 轨道全览
description: UmaViewer Live 时间轴全部103条轨道的ID、名称、作用、实现状态及已知Bug（2026-05-07核查）
type: reference
originSessionId: 319ae839-0292-4b1e-80de-2f3c91b1f40e
---
# Live 轨道全览（LiveTimelineKeyDataType 枚举，ID 0–102）

数据来源：LiveTimelineDefine.cs 枚举 + WorkSheet CS字段核查 + scan_full.py全量扫描（58首歌，2026-05-07）+ Director/Control代码审查

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
| 5 | HandShakeCamera | 手持抖动效果 | ❌ | — |
| 6 | Event | 时间轴触发事件 | ❌ | — |
| 7 | CharaMotionSequence ★(5) | 角色动作序列切换 | ✅ | — |
| 8 | BgColor1 ★(13) | 角色色调/轮廓/阴影颜色 | ✅ | — |
| 9 | BgColor2 ★(1) | 背景渐变/环境色（两色混合）【15/58首】 | ⚠️ | 当前用RenderSettings.ambient*近似，但舞台shader不响应Unity实时光照，实际shader属性名待确认 |
| 10 | MonitorControl ★(2) | LED舞台屏幕内容控制 | ❌ | — |
| 11 | CameraSwitcher | 切换活动摄像机 | ✅ | — |
| 12 | LipSync | 口型同步 | ✅ | — |
| 13 | PostEffectDOF | 景深模糊（单机） | ❌ | — |
| 14 | PostEffectBloomDiffusion | 泛光扩散（单机） | ❌ | — |
| 15 | RadialBlur | 径向运动模糊（单机） | ❌ | WorkSheet有radialBlurKeys字段，58/58首存在但全部0 keyframe（空占位） |
| 16 | CameraLayer | 摄像机渲染层遮罩 | ❌ | — |
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
| 26 | FacialNoise | 表情随机扰动 | ❌ | WorkSheet无facialNoiseKeys字段 |
| 27 | CharaMotionNoise | 动作随机扰动 | ❌ | WorkSheet无charaMotionNoiseKeys字段 |
| 28 | FormationOffset | 角色队形位置偏移 | ✅ | — |
| 29 | Animation ★(1) | GameObject Animation组件序列 | ❌ | — |
| 30 | TextureAnimation | 材质纹理帧动画 | ❌ | — |

---

## 舞台物件

| ID | 名称 | 作用 | 状态 | Bug |
|----|------|------|------|-----|
| 31 | Transform | 舞台物件位移/旋转/缩放 | ✅ StageController | — |
| 32 | Renderer | 舞台Renderer启用/禁用 | ❌ | — |
| 33 | Object ★(10) | 舞台GameObject激活/隐藏 | ✅ StageController | — |
| 34 | Audience ★(12) | 观众人群动画参数 | ❌ | — |
| 35 | Props | 舞台道具激活控制 | ❌ | — |
| 36 | PropsAttach | 角色附着道具 | ❌ | — |

---

## 灯光 / 特效

| ID | 名称 | 作用 | 状态 | Bug |
|----|------|------|------|-----|
| 37 | VolumeLight ★(1) | 体积光/SunShafts | ⚠️🔇 | 无SunShafts组件，仅数据 |
| 38 | HdrBloom | HDR泛光强度/阈值 | ❌ | 全量扫描确认0/58首有数据，不需要实现 |
| 39 | PostFilm | 全屏后期叠加（晕影/颜色/UV电影） | ❌ | WorkSheet有postFilmKeys字段，58/58首存在但全部0 keyframe（空占位） |
| 40 | Fade | 场景淡入淡出 | ❌ | — |
| 41 | Particle ★(3) | 粒子系统发射速率 | ✅ | — |
| 42 | ParticleGroup ★(3) | 粒子组闪烁速率（双速率曲线） | ✅ | — |
| 43 | WashLight | 洗光灯 SetActive+颜色亮度 | ⚠️ | RaycastDistance/CameraProjectionSide/MulColor0未使用 |
| 44 | Laser | 激光灯变换+SetActive | ⚠️ | blinkRate/blinkOffset/rotateFollowCamera/RaycastDistance未实现 |
| 45 | BlinkLight ★(9) | 频闪灯亮灭周期/颜色/亮度 | ⚠️ | color1Array/LightBlendMode/isReverseHueArray未使用 |
| 46 | UVScrollLight ★(1) | 材质UV滚动灯光效果 | ⚠️ | scrollSpeedX/Y误用SetTextureScale（应每帧累积offset）；mulColor1/ColorType/CharacterIndex等未使用 |
| 47 | FacialToon | 角色卡通着色参数 | ❌ | — |
| 48 | GlobalLight ★(2) | 全局光方向/RimLight参数 | ✅ | — |
| 49 | GlobalFog ★(1) | 全局雾效（颜色/密度/范围） | ✅ | — |
| 50 | LightShafts ★(1) | 光轴/丁达尔效果 | ⚠️🔇 | 无LightShaftsController组件，仅数据 |
| 51 | MonitorCameraPos ★(1) | 舞台监控摄像机位置 | ❌ | WorkSheet无monitorCameraPosKeys字段 |
| 52 | MonitorCameraLookAt ★(1) | 舞台监控摄像机朝向 | ❌ | WorkSheet无monitorCameraLookAtKeys字段 |
| 53 | MultiCameraPos ★(2) | 多机位摄像机位置 | ✅ | — |
| 54 | MultiCameraLookAt ★(2) | 多机位摄像机朝向 | ✅ | — |
| 55 | EyeCameraPos | 眼部特写摄像机位置 | ❌ | — |
| 56 | EyeCameraLookAt | 眼部特写摄像机朝向 | ❌ | — |
| 57 | LensFlare | 镜头光晕【45/58首】 | ❌ | — |
| 58 | Environment ★(1) | 地板镜面反射/水面波纹/阴影/FovShift【48/58首】 | ❌ | 核心是Planar Reflection（第二摄像机→RenderTexture→地板shader），无现成系统则工程量大；水面UV参数和阴影开关可部分实现 |
| 59 | SweatLocator ★(3) | 角色汗珠特效挂点 | ❌ | WorkSheet无sweatLocatorList字段 |
| 60 | Effect ★(8) | 特效Prefab加载/播放（3d/effect/live/） | ✅ | — |
| 61 | ColorCorrection | 色彩校正（饱和度/RGB曲线） | ⚠️ | depthRedCurve/Green/Blue/blendCurve/mode/selective/keyColor/targetColor无URP对应 |
| 62 | PreColorCorrection | 前置色彩校正 | ❌ | — |
| 63 | TiltShift | 移轴模糊 | ❌ | WorkSheet有tiltShiftKeys字段，58/58首存在但全部0 keyframe（空占位） |
| 64 | A2U | 内部动作系统参数 | ❌ | 非视觉轨道 |
| 65 | A2UConfig | A2U配置 | ❌ | 非视觉轨道 |
| 66 | FlashPlayer | 全屏闪光播放 | ❌ | WorkSheet有flashPlayerKeys字段，58/58首存在但全部0 keyframe（空占位） |
| 67 | Title | 歌曲名字幕显示 | ❌ | — |
| 68 | Spotlight3d ★(3) | 3D聚光灯 SetActive+颜色/强度 | ✅ | — |
| 69 | CharaNode | 角色骨骼节点位置控制 | ❌ | — |
| 70 | NodeScale ★(2) | 舞台节点缩放 | ❌ | WorkSheet无nodeScaleList字段 |
| 71 | Fluctuation | 画面波动/震颤效果 | ❌ | — |
| 72 | CharaFootLight | 角色脚部补光灯 | ❌ | WorkSheet有charaFootLightKeys字段，58/58首存在但全部0 keyframe（空占位） |
| 73 | ChromaticAberration ★(1) | 色差后处理 | ⚠️ | redOffset/greenOffset/blueOffset（通道偏移）无URP内置对应；effectType未使用 |
| 74 | LightProjection ★(18!) | 投影灯（Gobo图案灯）【36/58首】 | ❌ | WorkSheet有lightProjectionList字段；song 1001有18条，高优先级 |
| 75 | Billboard | 广告牌（始终面向摄像机的面片）【12/58首】 | ❌ | — |

---

## 多机位后处理

| ID | 名称 | 作用 | 状态 | Bug |
|----|------|------|------|-----|
| 76 | MultiCameraPostFilm ★(2) | 多机位全屏后期叠加【50/58首】 | ❌ | WorkSheet有postFilm1MultiCameraKeys字段 |
| 77 | MultiCameraPostEffectBloomDiffusion ★(2) | 多机位泛光 | ❌ | WorkSheet无对应字段 |
| 78 | MultiCameraColorCorrection | 多机位色彩校正 | ❌ | — |
| 79 | MultiCameraTiltShift | 多机位移轴模糊 | ❌ | — |
| 80 | MultiCameraRadialBlur | 多机位径向模糊 | ❌ | — |
| 81 | MultiCameraPostEffectDOF | 多机位景深 | ❌ | — |

---

## 附加灯光 / 系统

| ID | 名称 | 作用 | 状态 | Bug |
|----|------|------|------|-----|
| 82 | AdditionalLight | 附加动态光源【22/58首】 | ❌ | — |
| 83 | MultiLightShadow | 多光源阴影 | ❌ | WorkSheet有MultiLightShadowKeys字段，58/58首存在但全部0 keyframe（空占位） |
| 84 | MobControl ★(4) | 观众人群行为/动画控制 | ❌ | WorkSheet无MobControlKeys字段 |
| 85 | CyalumeControl ★(4) | 荧光棒颜色/同步控制 | ❌ | WorkSheet无CyalumeControlKeys；CyalumeController3D.cs存在但未接入时间轴 |
| 86 | CameraMotion | 摄像机动作曲线轨道 | ❌ | — |
| 87 | WaveObject | 波浪变形物体 | ❌ | — |
| 88 | CharaWind | 角色风效（头发/裙摆） | ❌ | — |
| 89 | CharaParts | 角色部件显隐控制 | ❌ | — |
| 90 | CameraCutNo | 镜头编号标记（元数据） | ❌ | — |
| 91 | ToneCurve | 色调曲线（ACES/自定义） | ❌ | — |
| 92 | Exposure | 曝光值控制 | ❌ | — |
| 93 | Vortex | 旋涡/扭曲屏幕效果 | ❌ | — |
| 94 | CharaCollision | 角色碰撞辅助（非视觉） | ❌ | — |
| 95 | TransmittedLight | 透射光（次表面散射） | ❌ | — |
| 96 | TransmittedLightMask | 透射光遮罩 | ❌ | — |
| 97 | Voice | 语音切换控制 | ❌ | — |
| 98 | LipSyncPatternRange | 口型模式范围定义 | ❌ | WorkSheet无字段 |
| 99 | LipSyncPattern | 口型模式数据 | ❌ | WorkSheet无字段 |
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

## 未实现轨道优先级排序

LensFlare(57, 45/58首) > LightProjection(74, 36/58首) > AdditionalLight(82, 22/58首) > Billboard(75, 12/58首) > Audience(34, ★12条) > MobControl(84, 4条) > CyalumeControl(85, 4条) > Projector(17, 4条) > MultiCameraPostFilm(76, 50/58首) > NodeScale(70, ★2条) > MonitorControl(10, ★2条) > Animation(29, ★1条) > Environment(58, 48/58首, 需Planar Reflection系统)
