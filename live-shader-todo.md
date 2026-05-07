# Live Post-Processing Shader TODO

UmaViewer uses **URP (Unity 2022.3.62f1)**。游戏原版是 Built-in 管线，以下 shader 需要移植或重写为 URP 实现。

---

## 接线方式（通用）

每个效果的 Director handler 在 `Director.cs` 的 `InitializeTimeline()` 里订阅：

```csharp
_liveTimelineControl.OnUpdateXxx += (data, keyData) => {
    var vol = Camera.main.GetComponent<Volume>();
    if (vol.profile.TryGet<URP后处理类型>(out var fx)) {
        fx.参数.value = keyData.对应字段;
    }
};
```

---

## 1. ChromaticAberration ✅ URP 内置
**Track**: `chromaticAberrationList`（22/58 首）
**原版 shader**: `ImageEffect/ChromaticAberra`（Unity 官方开源）
**URP 方案**: `UnityEngine.Rendering.Universal.ChromaticAberration`（Volume Override 内置）

字段对应（从 bundle TypeTree 获取，待 dump）：
- `effectType` (int) → 目前值为 0，可能控制是否启用

**实现步骤**：
1. 在摄像机 GameObject 上加 `Volume` 组件
2. Profile 添加 `ChromaticAberration` override
3. `Director.cs` 订阅 `OnUpdateChromaticAberration`，设 `intensity.value`

---

## 2. Bloom / HdrBloom ✅ URP 内置
**Track**: `hdrBloomKeys`（58/58 首，但疑似骨架数据）
**原版 shader**: `ARBloom` / `RBloom`（Cygames 自定义，基于标准 Bloom）
**URP 方案**: `UnityEngine.Rendering.Universal.Bloom`

字段：待 dump hdrBloomKeys bundle 数据。

---

## 3. ColorCorrection ✅ URP 内置（近似）
**Track**: `colorCorrectionDataLists`
**URP 方案**: `ColorAdjustments` + `ColorCurves`

---

## 4. SunShafts (VolumeLight) ⚠️ 需移植
**Track**: `volumeLightKeys`（58/58 首）
**原版 shader**: `ImageEffects/SunShafts`（**Unity 官方开源 MIT**）
**源码**: https://github.com/Unity-Technologies/ImageEffects

字段对应（已从 bundle dump）：
```
sunPosition: Vector3     → 光源世界坐标
color1: Color            → 光线颜色
power: float             → 光线强度（当前 0.0，enable=0 时不渲染）
komorebi: float          → 丁达尔分量强度
blurRadius: float        → 模糊半径
ScreenColorPower: float  → 屏幕混合强度
EffectColorPower: float  → 效果强度
enable: bool             → 是否启用
```

**实现步骤**：
1. 从 Unity ImageEffects 仓库取 `SunShafts.cs` + `SunShaftsComposite.shader` + `SunShaftsComposite.cginc`
2. 移植为 URP `ScriptableRendererFeature`（参考：https://github.com/search?q=sun+shafts+urp）
3. `Director.cs` 订阅 `OnUpdateVolumeLight`，设置 Feature 参数

---

## 5. RadialBlur ⚠️ 需自写
**Track**: `radialBlurKeys`（58/58 首，骨架数据居多）
**原版 shader**: `Cygames/ImageEffects/Radial`（私有）
**URP 方案**: 自写 ScriptableRendererFeature + Blit shader

算法：屏幕空间径向模糊，从中心向外采样 N 次，offset 随距离增大。
参数字段待 dump。

---

## 6. TiltShift ⚠️ 需自写
**Track**: `tiltShiftKeys`（58/58 首，骨架数据居多）
**原版 shader**: `Cygames/ImageEffects/TiltShiftHdrLens`（私有）
**URP 方案**: 高斯模糊 pass + 渐变 mask

---

## 7. PostFilm ⚠️ 需自写（复杂）
**Track**: `postFilmKeys` / `postFilm2Keys` / `postFilm3Keys`（58/58 首）
**原版 shader**: `PostFilm` / `POSTFILM2`（Cygames 私有）

已知字段（从 bundle 字节扫描）：
- `PostFilmColor0`（颜色）
- `PostFilmPower`（强度）
- `movieScale`
- `IsAlphaMask`
- `_SCREEN_OVERLAY1`（混合模式关键字）
- `_POSTFILM_DIMM`（亮度调暗）

效果：叠加色调层 + 胶片质感，类似 Color Grading + Overlay。

---

## 8. Komorebi / 丁达尔 ⚠️ 需自写（高难度）
**字段位置**: `volumeLightKeys[0].keys.thisList[0].komorebi`
**原版 shader**: `Komorebi`（Cygames 私有，屏幕空间光斑散射）

目前由 VolumeLight handler 读取但未使用。工作量最大，可最后做。

---

## 9. DepthOfField ✅ URP 内置
**Track**: `postEffectDOFKeys`
**URP 方案**: `UnityEngine.Rendering.Universal.DepthOfField`

---

## 优先顺序

| 优先级 | Track | 工作量 | 收益 |
|--------|-------|--------|------|
| 1 | ChromaticAberration | 小 | 22首有效果 |
| 2 | Bloom | 小 | 全局画质提升 |
| 3 | ColorCorrection | 小 | 色彩正确性 |
| 4 | SunShafts | 中 | 58首，效果明显 |
| 5 | RadialBlur | 中 | 转场效果 |
| 6 | TiltShift | 中 | 焦点效果 |
| 7 | PostFilm | 大 | 整体色调 |
| 8 | Komorebi | 大 | 体积光细节 |

---

## 项目信息
- Unity: 2022.3.62f1c1（URP）
- 渲染资产: `Assets/Resources/RenderPipeline/UMAUniversalRenderPipelineAsset.asset`
- 渲染器: `UMAUniversalRenderPipelineAsset_Renderer.asset`
- Director 入口: `Assets/Scripts/umamusume/Gallop/Live/Director.cs` → `InitializeTimeline()`
- Control 事件模板: `LiveTimelineControl.cs` → `AlterUpdate_SimpleListControl`
