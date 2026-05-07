# Live Timeline 轨道实现 TODO

UmaViewer 逆向实现了游戏的 Live 演出系统：
- 解密加载 asset bundle → 反序列化 `LiveTimelineWorkSheet` → `LiveTimelineControl` 每帧插值触发事件 → `Director` 订阅事件调用 Unity API
- 游戏原版 Built-in 管线；UmaViewer 用 URP 14（Unity 2022.3.62f1），toon shader 已移植
- 103 条轨道中约 21 条完整，其余卡点分两类（见下）

---

## 卡点分类

### 缺零件（知道要做什么，Unity/URP 没有现成组件）

| 轨道 | 缺什么 |
|------|-------|
| PostFilm (39) / RadialBlur (15) / TiltShift (63) / SunShafts (37) | URP 无对应效果，需自写 ScriptableRendererFeature + shader |
| LightProjection (74) | Unity `Projector` 组件在 URP 不工作，需 DecalProjector 或自定义实现 |
| Environment (58) | Planar Reflection 系统（第二摄像机→RenderTexture→地板 shader）项目里完全没有 |
| LensFlare (57) | 舞台用 `CustomLensFlare` 脚本，UmaBuild 里无源码 |

### 没 dump 找明白（不知道打哪里）

| 轨道 | 不明白什么 |
|------|-----------|
| BgColor2 (9) | entry 名 `BgWashA`～`J` 在所有 bundle 里找不到，目标对象未知 |
| MonitorControl (10) | `dispID` 值 0–15 不对应 monitor 材质索引，内容资源路径未知 |
| AdditionalLight (82) | 字段未 dump，结构未知 |

### 两个都缺一点

| 轨道 | 情况 |
|------|------|
| WashLight (43) | 对象能找到，`RaycastDistance`/`CameraProjection` 语义不明，可能需要 Projector |
| UVScrollLight (46) | 知道怎么修（offset 累积），还没动手 |

---

## 已实现但不完整的 Handler（`Director.cs`）

| Handler | 问题 | 难度 |
|---------|------|------|
| ~~`OnBlinkLightUpdate`~~ | ✅ 已修复：`pattern==0` 走静态路径，`pattern!=0` 用 `turnOnTime`/`keepTime`/`turnOffTime`/`intervalTime`/`waitTime`/`loopCount`/`powerMin`/`powerMax` 计算每帧强度（含淡入淡出）。剩余未做：`color1Array`、`LightBlendMode`、颜色混合字段 | — |
| `OnUVScrollLightUpdate` | `scrollSpeedX/Y` 映射为 `SetTextureScale` 可能错误——若原版 shader 以速度做每帧增量位移，应改为 `offset += speed * deltaTime`。另：`mulColor1`、`ColorType`、`CharacterIndex`、颜色混合字段未使用 | ★☆☆（待确认语义） |
| ~~`OnParticleGroupUpdate`~~ | ✅ 已修复：改用 `MinMaxCurve(FlickerDarkRate, FlickerLightRate)`，Unity 粒子系统在两个发射率之间随机取值，自然模拟闪烁 | — |
| `OnWashLightUpdate` | 仅 `SetActive(true)`，`RaycastDistance`/`CameraProjectionSide`/`CameraProjectionColorPower` 未使用，需 Projector 或自定义投影组件 | ★★☆ |
| `OnLaserUpdate` | `blink`/`blinkPeriod`/`degLaserPitch`/`RaycastDistance`/`formation`/`posInterval` 未使用 | ★★☆ |

### 剩余问题说明

**UVScrollLight scrollSpeed**：需先用 `read_cutt_effect.py` dump 一首有 UV 滚动的曲目，对比 `scrollSpeedX/Y` 的实际值和视觉效果来确认语义。如果值是每秒 offset 增量，改为在 handler 里维护累计 offset（需持久化，不能每帧从 keyData 重置）。

**WashLight / Laser**：依赖舞台 prefab 的实际组件结构，需先加载一个含 WashLight/Laser 的 Live 看 prefab 里有什么，才能决定如何驱动。在此之前无法实现。

---

## 架构说明（必读）

### 为什么不能直接用旧方法

Built-in 管线支持 `OnRenderImage()`，脚本可直接拦截帧缓冲做全屏处理。**URP 删掉了这个接口**。

项目同时装了 `com.unity.postprocessing 3.4.0`（旧 Built-in 包）和 URP 14。`Screenshot.cs` 在用旧包的 `PostProcessLayer`，但该组件在 URP 管线下**不渲染任何效果**，只是没报错。后续实现不要依赖旧包。

### 两种实现路径

**路径 A — URP Volume Override（适用于内置效果）**

摄像机上挂 `Volume` 组件，`VolumeProfile` 里添加对应 Override，每帧在 Director handler 里设值：

```csharp
_liveTimelineControl.OnUpdateXxx += (data, keyData) => {
    var vol = Camera.main.GetComponent<Volume>();
    if (vol.profile.TryGet<SomeUrpEffect>(out var fx))
        fx.someParam.Override(keyData.value);
};
```

适用：ChromaticAberration、Bloom、ColorAdjustments、DepthOfField。

**路径 B — ScriptableRendererFeature（适用于自定义效果）**

URP 在管线中插入自定义 Render Pass，用 Material 做全屏 Blit：

```
ScriptableRendererFeature         ← 注册到 Renderer Asset
  └── ScriptableRenderPass
        └── Blitter.BlitCameraTexture(cmd, src, dst, material, pass)
```

Feature 暴露公开字段，Director 持有 Feature 引用，每帧写入参数：

```csharp
// Director 字段
private PostFilmRendererFeature _postFilmFeature;

// InitializeTimeline 里获取
var data = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
_postFilmFeature = data.GetRenderer(0) ... // 或序列化直接引用

// handler 里设值
_postFilmFeature.settings.color0 = keyData.color0;
_postFilmFeature.settings.filmMode = (int)keyData.filmMode;
```

Feature 必须在 `UMAUniversalRenderPipelineAsset_Renderer.asset` 的 Renderer Features 列表里手动添加。

适用：PostFilm、RadialBlur、TiltShift、SunShafts。

---

## 分组与优先顺序

### 第一组：URP 内置，工作量小

| 优先级 | Track ID | 字段名 | URP 类型 | 覆盖率 |
|--------|----------|--------|---------|--------|
| 1 | 73 | `chromaticAberrationList` | `ChromaticAberration` | 22/58 |
| 2 | 38 | `hdrBloomKeys` | `Bloom` | 58/58（疑似骨架） |
| 3 | 61 | `colorCorrectionDataLists` | `ColorAdjustments` + `ColorCurves` | — |
| 4 | 13 | `postEffectDOFKeys` | `DepthOfField` | — |

这四个照标准 5 步走，在 Director handler 里操作 Volume 即可。

### 第二组：自定义 shader，工作量大

| 优先级 | Track ID | 字段名 | 覆盖率 | 备注 |
|--------|----------|--------|--------|------|
| 5 | 39 | `postFilmKeys` / `postFilm2Keys` / `postFilm3Keys` | 58/58 | 最复杂，优先做 |
| 6 | 37 | `volumeLightKeys` | 58/58 | 数据类已有，shader 需移植 |
| 7 | 15 | `radialBlurKeys` | 58/58（骨架居多） | 大多数曲目值为0 |
| 8 | 63 | `tiltShiftKeys` | 58/58（骨架居多） | 大多数曲目值为0 |

---

## 各效果详情

### 1. ChromaticAberration（路径 A）

**Track ID**: 73  
**URP 类型**: `UnityEngine.Rendering.Universal.ChromaticAberration`

字段（待从 bundle dump）：
- `effectType` (int) — 目前值为 0，可能控制启用/强度

实现：摄像机 Volume → Profile 加 ChromaticAberration override → handler 设 `intensity.Override(value)`。

---

### 2. HdrBloom（路径 A）

**Track ID**: 38  
**URP 类型**: `UnityEngine.Rendering.Universal.Bloom`

字段（待 dump）。注意 `hdrBloomSettings` 在 `LiveTimelineData.cs` 里只有 `bloomBlurIterations` 一个字段，
实际 key 数据需要从 bundle 确认。

---

### 3. ColorCorrection（路径 A）

**Track ID**: 61  
**URP 类型**: `ColorAdjustments`（曝光、饱和度、色相）+ `ColorCurves`

字段（待 dump）。

---

### 4. DepthOfField（路径 A）

**Track ID**: 13  
**URP 类型**: `UnityEngine.Rendering.Universal.DepthOfField`

字段（待 dump）。注意 URP DOF 分 Gaussian 和 Bokeh 两种模式，选哪种取决于原版字段。

---

### 5. PostFilm（路径 B，优先）

**Track ID**: 39（还有 76=MultiCameraPostFilm）  
**数据类**: `LiveTimelineKeyPostFilmData`（`LiveTimelineKeyPostFilmDataList.cs` 已存在）

**为什么复杂**：`filmMode` 决定混合逻辑（8种），`PostColorType` 决定颜色布局（4种），
shader 需要处理 4×8 种组合，用 keyword 或 uniform 分支实现：

```
filmMode:   None=0  Lerp=1  Add=2  Mul=3
            VignetteLerp=4  VignetteAdd=5  VignetteMul=6  Monochrome=7

colorType:  ColorAll=0  Color2TopBottom=1  Color2LeftRight=2  Color4=3
```

已知字段（`LiveTimelineKeyPostFilmData`）：
```
filmMode, colorType
filmPower          → 全局强度
filmOffsetParam    → 位移参数
filmOptionParam    → 附加参数（Vector4）
color0/1/2/3       → 最多4个颜色分区
depthPower         → 深度混合强度
DepthClip          → 深度剪裁
RollAngle          → 旋转角度
FilmScale          → 缩放
layerMode          → Color=0 / UVMovie=1
colorBlend         → None/Lerp/Additive/Multiply
colorBlendFactor   → 混合系数
```

**实现步骤**：
1. 创建 `PostFilmRendererFeature.cs` + `PostFilmRenderPass.cs`
2. 写 `PostFilm.shader`（HLSL，`AfterRenderingPostProcessing` 插入点）
3. 在 `UMAUniversalRenderPipelineAsset_Renderer.asset` 添加该 Feature
4. Director 持有 Feature 引用，`OnPostFilmUpdate` handler 写入参数
5. WorkSheet 加 `postFilmKeys`、`postFilm2Keys`、`postFilm3Keys` 字段，Control 加事件

---

### 6. SunShafts / VolumeLight（路径 B）

**Track ID**: 37  
**数据类**: `LiveTimelineVolumeLightData`（已有，handler 目前是存根）  
**原版 shader**: Unity 官方开源 `ImageEffects/SunShafts`（MIT）

已知字段：
```
sunPosition: Vector3  → 光源世界坐标（需转屏幕空间）
color1: Color         → 光线颜色
power: float          → 光线强度
komorebi: float       → 丁达尔分量（单独 pass，可最后做）
blurRadius: float     → 径向模糊半径
ScreenColorPower      → 屏幕混合强度
EffectColorPower      → 效果强度
enable: bool          → 启用开关
```

**实现步骤**：
1. 从 Unity ImageEffects 仓库取 `SunShaftsComposite.shader` 作为参考
2. 改写为 URP HLSL（`HLSLPROGRAM`，去掉 `GrabPass`，用 `_CameraOpaqueTexture`）
3. Feature 在 `BeforeRenderingPostProcessing` 插入，两个 pass：遮罩 + 径向模糊
4. `OnVolumeLightUpdate` handler 填充 Feature 参数（enable 为 false 时关闭 Feature）

---

### 7. RadialBlur（路径 B）

**Track ID**: 15  
**字段**: 待从 bundle dump。  
**算法**: 屏幕空间径向模糊，从中心向外方向采样 N 次，每次 offset = direction × step × i。

注意：58/58 首都有此 track，但大多数 key 值为 0（骨架数据），只有转场时有效。可先把 Feature 架子搭好，
shader 逻辑简单实现，等有真实数据时再调参。

---

### 8. TiltShift（路径 B）

**Track ID**: 63  
**字段**: 待从 bundle dump。  
**算法**: 屏幕中间区域清晰，上下（或可配置方向）做高斯模糊，用渐变 mask 混合。

同 RadialBlur，骨架数据居多，优先级最低。

---

### 9. Komorebi / 丁达尔（SunShafts 子项）

**字段位置**: `volumeLightKeys[i].keys.thisList[j].komorebi`  
**原版 shader**: Cygames 私有，屏幕空间光斑散射  

目前 VolumeLight handler 读取该字段但未使用。归并到 SunShafts Feature 里作为额外 pass，
工作量最大，在 SunShafts 基础实现完成后再加。

---

## 舞台 / 灯光轨道

### 可以直接做

| 轨道 | 问题 | 难度 |
|------|------|------|
| UVScrollLight (46) bug | `scrollSpeedX/Y` 现在用 `SetTextureScale` 是错的，应每帧累积 `offset += speed * deltaTime`，需持久化累计值 | ★☆☆ |
| BgColor1 (8) 缺字段 | `vertexColorToonPower`、`outlineWidthPower`、`IsSilhouette`、`LightBlendMode` 未传给 shader | ★☆☆ |
| BlinkLight (45) 缺字段 | `color1Array`（多色循环）、`LightBlendMode`、`isReverseHueArray` 未使用 | ★★☆ |
| AdditionalLight (82) | 22/58 首有数据，字段未 dump，可能是普通 Light 参数，需先 dump 再评估 | 待评估 |
| Billboard (75) | 12/58 首有数据，始终朝摄像机的面片，Unity 有内置 `BillboardRenderer` 或 LookAt 逻辑 | ★☆☆ |

### 暂时卡住

| 轨道 | 卡点 | 解法方向 |
|------|------|---------|
| BgColor2 (9) | entry 名 `BgWashA`～`BgWashJ` 在所有 stage bundle 和 meta DB 里均不存在；舞台 shader 不响应 Unity 环境光，所以当前 `RenderSettings.ambient*` 实现无效 | 需 Frame Debugger 截帧确认实际 shader 属性名，须找到镜头给到舞台背景全景的歌曲 |
| LensFlare (57) | Stage bundle 用 `CustomLensFlare` / `UnityLensFlareController` 脚本，UmaBuild 里无源码 | 可先做 `SetActive`（`enableFlare`）；完整颜色/亮度控制需找到脚本接口或用 Renderer 直接设材质颜色 |
| MonitorControl (10) | `dispID` 值 0–15 不对应 monitor 材质索引（每个舞台只有 2–5 张）；内容资源路径格式未知 | 可先做框架 + `colorFade`/UV 控制，`dispID` 内容切换留 TODO |
| LightProjection (74) | Unity 的 `Projector` 组件在 URP 不工作；URP 有 `DecalProjector` 但 API 差异较大 | 用 URP `DecalProjector` 替代；或用自定义 `ScriptableRendererFeature` 实现投影 |
| Environment (58) | 核心是 Planar Reflection（第二摄像机 → RenderTexture → 地板 shader），项目里完全没有这套系统 | 水面 UV 参数 + 阴影开关可部分实现；镜面反射需从零搭 Planar Reflection Camera |

---

## 项目信息

- Unity: 2022.3.62f1（URP 14.0.12）
- 渲染资产: `Assets/Resources/RenderPipeline/UMAUniversalRenderPipelineAsset.asset`
- 渲染器（Feature 注册位置）: `UMAUniversalRenderPipelineAsset_Renderer.asset`
- Director 入口: `Assets/Scripts/umamusume/Gallop/Live/Director.cs` → `InitializeTimeline()`
- Control 事件模板: `LiveTimelineControl.cs` → `AlterUpdate_SimpleListControl`
- PostFilm 数据类: `Assets/Scripts/umamusume/Gallop/Live/Cutt/LiveTimelineDataList/LiveTimelineKeyPostFilmDataList.cs`
