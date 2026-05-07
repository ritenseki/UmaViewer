# Live Timeline 开发地图

## 系统架构

```
Asset Bundles（游戏加密包）
  └── LiveTimelineWorkSheet（ScriptableObject，103 条轨道的关键帧数据）
        └── LiveTimelineControl（每帧按帧号插值，触发事件）
              └── Director（订阅事件，调用 Unity API）
                    ├── GameObject / Transform  → SetActive, position/rotation/scale
                    ├── Camera                 → fieldOfView, transform
                    ├── Light                  → color, intensity
                    ├── MaterialPropertyBlock  → toon shader 属性（已移植到 URP）
                    ├── ParticleSystem         → emission rate
                    ├── RenderSettings         → fog, ambient
                    └── URP Volume / Feature   → 后处理效果
```

游戏原版 Built-in 管线；UmaViewer 用 **URP 14（Unity 2022.3.62f1）**。
Toon shader 已移植，卡住的主要是全屏后处理和几个逆向未完成的轨道。

---

## 轨道 → Unity API 对照

### GameObject / Transform
| 轨道 | 状态 | 操作 |
|------|------|------|
| Object (33) | ✅ | `SetActive` |
| Transform (31) | ✅ | position / rotation / scale |
| WashLight (43) | ⚠️ | `SetActive`（颜色/投影未做） |
| Laser (44) | ⚠️ | `SetActive` + transform（blink 未做） |
| BlinkLight (45) | ⚠️ | `SetActive` + 子 `Light`（color1Array/BlendMode 未做） |
| Spotlight3d (68) | ✅ | `SetActive` + `Light.color/intensity` |
| Effect (60) | ✅ | `Instantiate` prefab |

### Camera
| 轨道 | 状态 |
|------|------|
| CameraPos (1) / LookAt (2) / Fov (3) / Roll (4) | ✅ |
| CameraSwitcher (11) | ✅ |
| MultiCameraPos (53) / LookAt (54) | ✅ |

### MaterialPropertyBlock → Shader
| 轨道 | 状态 | 属性 |
|------|------|------|
| BgColor1 (8) | ⚠️ | `_CharaColor/_ToonDarkColor/_ToonBrightColor/_OutlineColor/_Saturation`（vertexColorToonPower 等未传） |
| GlobalLight (48) | ✅ | 角色 rim light 属性 |
| UVScrollLight (46) | ⚠️ | `_MainTex` offset 累积（已修 bug，mulColor 等未用） |

### ParticleSystem
| 轨道 | 状态 |
|------|------|
| Particle (41) | ✅ `emission.rateOverTime` |
| ParticleGroup (42) | ✅ `FlickerLightRate` MinMaxCurve |

### RenderSettings
| 轨道 | 状态 |
|------|------|
| GlobalFog (49) | ✅ fogColor / fogMode / fogDensity / fogStart / fogEnd |
| BgColor2 (9) | ✅ 写含 `_AmbientColor` 属性的舞台材质，`Lerp(color1, white, value)` |

### URP Volume / RendererFeature
| 轨道 | 状态 |
|------|------|
| ChromaticAberration (73) | ⚠️ intensity 完成，通道偏移无 URP 对应 |
| ColorCorrection (61) | ⚠️ saturation 完成，depth/blend curve 无 URP 对应 |
| HdrBloom (38) | ❌ 0/58 首无数据，无需实现 |
| PostFilm (39) / RadialBlur (15) / TiltShift (63) / SunShafts (37) | ❌ 见后处理 TODO |

### 内部系统
| 轨道 | 状态 |
|------|------|
| CharaMotionSequence (7) / LipSync (12) / FormationOffset (28) | ✅ |
| FacialFace/Eye/Mouth 等 (18–25) | ✅ |

---

## 卡点分类

### 缺零件（知道要做什么，Unity/URP 没有现成组件）

| 轨道 | 缺什么 |
|------|-------|
| PostFilm (39) / RadialBlur (15) / TiltShift (63) / SunShafts (37) | URP 无对应，需自写 ScriptableRendererFeature + shader → 见 `live-shader-todo.md` |
| LightProjection (74) | `Projector` 组件在 URP 不工作，需 DecalProjector 或自定义（36/58 首）|
| Environment (58) | Planar Reflection 系统完全没有（第二摄像机→RenderTexture→地板 shader）（48/58 首）|
| LensFlare (57) | 舞台用 `CustomLensFlare` 脚本，UmaBuild 无源码；可先做 SetActive（45/58 首）|

### 没 dump 找明白（不知道打哪里）

| 轨道 | 不明白什么 |
|------|-----------|
| MonitorControl (10) | `dispID` 0–15 不对应 monitor 材质索引，内容资源路径未知；颜色/UV 控制可先做（★★/58 首）|
| AdditionalLight (82) | 字段未 dump，结构未知（22/58 首）|

### 可以直接做

| 轨道 | 工作 | 难度 |
|------|------|------|
| BgColor1 (8) 缺字段 | 补 `vertexColorToonPower`/`outlineWidthPower`/`IsSilhouette`/`LightBlendMode` | ★☆☆ |
| BlinkLight (45) 缺字段 | `color1Array` 多色循环、`LightBlendMode` | ★★☆ |
| Billboard (75) | 始终朝摄像机面片，Unity 有内置 LookAt 逻辑（12/58 首）| ★☆☆ |
| AdditionalLight (82) | 先 dump 字段再实现（22/58 首）| 待评估 |

---

## 未实现轨道优先级（综合覆盖率 + 难度）

1. **AdditionalLight (82)** — 22/58 首，先 dump 评估
2. **Billboard (75)** — 12/58 首，简单
3. **BgColor1 缺字段** — 58/58 首都有，补全影响所有歌
4. **LensFlare SetActive (57)** — 45/58 首，先做 enableFlare 这一层
5. **后处理系列** — 见 `live-shader-todo.md`
6. **MonitorControl 框架 (10)** — 颜色/UV 先做，dispID 留 TODO
7. **LightProjection (74)** — 工程量大
8. **Environment (58)** — 最复杂，Planar Reflection 从零搭

---

## 关键文件

| 文件 | 说明 |
|------|------|
| `Assets/Scripts/umamusume/Gallop/Live/Director.cs` | 事件订阅 + 所有 handler |
| `Assets/Scripts/umamusume/Gallop/Live/Cutt/LiveTimelineControl.cs` | 每帧插值 + 事件触发 |
| `Assets/Scripts/umamusume/Gallop/Live/Cutt/LiveTimelineWorkSheet.cs` | 所有轨道字段声明 |
| `Assets/Scripts/umamusume/Gallop/Live/Cutt/LiveTimelineDataList/` | 各轨道数据类 |
| `Assets/Scripts/umamusume/Gallop/Live/StageController.cs` | StageObjectMap（按名字查舞台对象）；`IsTimelineControlledLight()` 决定哪些对象默认 inactive（`blinklight`/`spotlight3d`/`_wash_`/`laser`）|
| `Assets/Resources/RenderPipeline/UMAUniversalRenderPipelineAsset_Renderer.asset` | RendererFeature 注册位置 |
| `LIVE_TRACKS.md` | 103 条轨道全览（ID/覆盖率/状态/Bug）|
| `live-shader-todo.md` | 后处理轨道实现细节 |
