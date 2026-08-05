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

## 状态记号

2026-08-05 起区分「实现存在」和「实现被核查过」：

| 记号 | 含义 |
|------|------|
| ✅ | 已实现，**且本次审计核查过**（读代码 + 用 `tools/` 对过 bundle 数据）|
| ✅? | 标着已实现，但**没人核查过**。本项目已经出现过 8 次「看着能用其实整条静默失效」，未核查 ≠ 正常 |
| ⚠️ | 已实现但不完整，缺的部分写在备注里 |
| ❌ | 未实现 |

审计报告见 `LIVE_AUDIT_2026-08-05.md`。

---

## 轨道 → Unity API 对照

### GameObject / Transform
| 轨道 | 状态 | 操作 |
|------|------|------|
| Object (33) | ✅? | `SetActive` |
| Transform (31) | ✅ | position / rotation / scale。2026-08-05 修：原先只查 `StageObjectUnitMap`，组名是 GameObject 名（son1028 的 `light001_glow_001`）时整条轨道静默失效，12 首 / 681 keys 从未生效 |
| WashLight (43) | ⚠️ | `SetActive`（颜色/投影未做） |
| Laser (44) | ⚠️ | `SetActive` + transform（blink 未做） |
| BlinkLight (45) | ⚠️ | `SetActive` + 写 Renderer 的 `_BlinkLightColor`/`_ColorPower`（color1Array/BlendMode 未做） |
| Spotlight3d (68) | ✅ | `SetActive` + 写 Renderer 颜色属性（**不是** Unity `Light`）。2026-08-05 修：原先写 `_Color`，而 `Gallop/3D/Live/Stage/*` 下**没有任何 shader** 声明该属性（133 个全枚举过），颜色写入一直是空操作；灯柱是 `StageBeamLight` 系列 → `_MulColor0` + `_ColorPower`，改用 BlinkLight 的 shader 探测 |
| Effect (60) | ✅? | `Instantiate` prefab |

> ⚠️ **舞台上没有 Unity `Light` 组件。** live10149 实测：10102 个 GameObject、
> 898 MeshRenderer、602 SkinnedMeshRenderer、**Light 0 个**；整个 Live 代码也没有任何
> 地方读写 `Light.color/intensity`。所有「灯」都是自发光几何体，靠时间轴写 shader 颜色属性。
> 本表此前把 BlinkLight / Spotlight3d 写成「子 Light color/intensity」，是错的，已更正。

### Camera
| 轨道 | 状态 |
|------|------|
| CameraPos (1) / LookAt (2) / Fov (3) / Roll (4) | ✅? |
| CameraSwitcher (11) | ✅? |
| MultiCameraPos (53) / LookAt (54) | ✅? |

### MaterialPropertyBlock → Shader
| 轨道 | 状态 | 属性 |
|------|------|------|
| BgColor1 (8) 角色分支 | ⚠️ | `_CharaColor/_ToonDarkColor/_ToonBrightColor/_OutlineColor/_Saturation`；`vertexColorToonPower`/`outlineWidthPower`/`LightBlendMode`/`IsSilhouette` 已填入 UpdateInfo 但 handler 还没用 |
| BgColor1 (8) 舞台分支 | ⚠️ | 2026-08-04 新增。遍历舞台层级按 Transform 名解析 Renderer，写 **`_MulColor0`**（已确认）。`_AmbientColor` 归 BgColor2，别抢 |
| GlobalLight (48) | ✅ | 角色 rim light 属性。2026-08-05 修：`SetPropertyBlock` 是整块替换，BgColor1 角色分支在它之后跑，每帧把 13 个 rim 属性全抹掉 —— 全语料 59/59 首同时有这两条轨道，等于从来没生效过。两处都改成 `GetPropertyBlock` 读-改-写 |
| UVScrollLight (46) | ⚠️ | `_MainTex` offset 累积（已修 bug，mulColor 等未用） |

### ParticleSystem
| 轨道 | 状态 |
|------|------|
| Particle (41) | ✅? `emission.rateOverTime`；每帧全场景 GetComponentsInChildren 且漏 inactive |
| ParticleGroup (42) | ✅? `FlickerLightRate` MinMaxCurve；与 Particle 同名时会覆盖它 |

### RenderSettings
| 轨道 | 状态 |
|------|------|
| GlobalFog (49) | ⚠️ 已核查：数据是**高度雾**(`isDistance=0,isHeight=1`)，却当成距离雾在开；Unity RenderSettings 没有高度雾。见审计报告 C1 |
| BgColor2 (9) | ⚠️ 写 `_AmbientColor`，`Lerp(color1, color2, value)`。2026-08-05：组名已接通（原先整条被丢弃，最多 15 个组每帧各刷一遍全舞台、只有最后一个留得下来），性能已修。**但 `BgWashA..O`/`LaserA..C` 既不是 GameObject 名也不是 unit 名，映射未知** —— 解析不到时退回全舞台写入并打警告 |

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
| CharaMotionSequence (7) / LipSync (12) / FormationOffset (28) | ✅? |
| FacialFace/Eye/Mouth 等 (18–25) | ✅? |

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

> dump 工具链已于 2026-08-04 重建（`tools/`），这一类现在可以自己解决：
> `~/.venvs/umatools/bin/python3 tools/dump_cutt_typetree.py --tree <字段名> <songid>` 出结构，
> `--sample <字段名> <songid>` 出第一个真实关键帧的值。

| 轨道 | 不明白什么 |
|------|-----------|
| MonitorControl (10) | `dispID` 0–15 不对应 monitor 材质索引，内容资源路径未知；颜色/UV 控制可先做（3911 keys / 55 首）|
| AdditionalLight (82) | 字段结构未知，但 `AdditionalLightList` 确认存在，657 keys / 23 首 → 用 `--tree AdditionalLightList` 即可 dump |

### 可以直接做

| 轨道 | 工作 | 难度 |
|------|------|------|
| BgColor1 (8) 缺字段 | 补 `vertexColorToonPower`/`outlineWidthPower`/`IsSilhouette`/`LightBlendMode` | ★☆☆ |
| BlinkLight (45) 缺字段 | `color1Array` 多色循环、`LightBlendMode` | ★★☆ |
| Billboard (75) | 始终朝摄像机面片，Unity 有内置 LookAt 逻辑（12/58 首）| ★☆☆ |
| AdditionalLight (82) | 先 dump 字段再实现（22/58 首）| 待评估 |

---

## 未实现轨道优先级

完整分级见 `LIVE_TRACKS.md` 末尾「优先级排序」。**2026-08-04 用 `tools/` 全量重测后重排**（59 首全语料，数字是总 keyframe 数）：

1. **BgColor1 (8) 舞台分支** — **43617 keys / 59首**，全语料最大轨道。分发过滤已拆，通道已确认为 `_MulColor0`；剩 `BgBL`/`FollowSpotColor`/`Shadow`/`mob_00` 4 个非物件名的组待定
2. **BlinkLight (45) 补字段** — **19420 keys / 57首**，灯光线最大权重（含全部 `*_wash_*` 效果）
3. **PostFilm (39)** — **~20200 keys / 59首**（三字段合计），未实现里最大的一块。旧记录说它「全 0 keyframe」是错的
4. **FacialToon (47)** — 6341 keys / 59首，只差 C# 字段 + handler
5. **PostEffectDOF (13)** — 3879 keys / 59首，URP 有内置 DepthOfField，属路径 A
6. **VolumeLight / SunShafts (37)** — 2048 keys / 58首，字段表齐全，做通后 URP Feature 就有模板
7. **LightProjection (74)** — 3793 keys / 37首，URP 下 Projector 不工作
8. **Environment (58)** — 1375 keys / 49首，Planar Reflection 从零搭

> Environment 之前排第一，现已后移：它属于「从零搭系统」，而 1、2 属于「数据早就在手但没用上」，性价比高一个量级。
>
> **旧的 ❌「WorkSheet 无字段」判断多数是错的** —— MobControl / CyalumeControl / NodeScale / SweatLocator / MonitorCameraPos / MonitorCameraLookAt / FacialNoise / CharaMotionNoise 的字段都存在且有数据，它们属于「只差 C# 声明」的第三档，不是被卡住。

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
| `LIVE_AUDIT_2026-08-05.md` | ✅ 轨道审计报告：A1–A4 已修的证据与改法，C1–C3 未修项及其阻塞原因 |
