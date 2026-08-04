# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

UmaViewer is a Unity application (2022.3.62f1) that loads and displays assets from Uma Musume: Pretty Derby. It reads encrypted asset bundles directly from the game installation without modifying game files.

## Development

**Open the project:** Unity Hub → Add → `C:\Users\riten\Desktop\UmaBuild`
**Run:** Open `Assets/Scenes/Version2` scene → Play in Unity Editor
**Build:** File → Build Settings → PC Standalone → Build

No CLI build commands — Unity Editor is required.

## Game Data & Asset Pipeline

Game assets are encrypted. Key files:
- `meta` (SQLite-MC ChaCha20 encrypted) — maps bundle names → hash filenames in `dat/`
- `dat/{2-char-prefix}/{hash}` — encrypted asset bundles, each with a per-bundle XOR key from meta

**Bundle decryption** (`UmaAssetBundleStream.cs`): header (first 256 bytes) is plain; bytes ≥256 are XOR'd with `FKey = ABKey[i] XOR bundleKey_bytes[j]`.

**meta DB decryption**: `UmaDatabaseController.ReadMetaFromEncryptedDb()` uses `sqlite3mc_x64.dll` with cipher=3 (ChaCha20). Final key = `DBKey[i] XOR DBBaseKey[i % 13]`.

**Asset bundle paths** (key formats):
- Live cutt timelines: `cutt/cutt_son{music_id}/cutt_son{music_id}`
- Live effect prefabs: `3d/effect/live/pfb_{effectName}`
- Stage prefabs: `3d/env/live/live{bgId}/pfb_env_live{bgId}_controller000`

**Python tools** (`tools/`) — rebuilt 2026-08-04 after the old `UmaCrack\` toolchain was deleted. Run with `~/.venvs/umatools/bin/python3` (needs `UnityPy` + `apsw-sqlite3mc`, **not** plain `apsw`; see `tools/requirements.txt`).

| Script | Use |
|---|---|
| `uma_common.py` | Library: key constants, `decrypt_bundle_bytes()`, `MetaDb` (path→hash, UnityPy load) |
| `dump_meta.py` | `plain` → `out/meta_plain.db` · `find <LIKE>` · `info <path>` (hash, dat path, deps, `[NOT DOWNLOADED]`) · `songs` |
| `dump_bundle.py` | `list` / `tree` / `json` / `save` / `gameobjects`, `--deps` |
| `dump_cutt_typetree.py` | **Authoritative track schema.** `--fields <song>` · `--tree <field> <song>` · `--sample <field> <song>` · `--groups` |
| `scan_live_tracks.py` | All 59 songs → `out/scan_keys.csv`, `scan_groups.csv`, `scan_summary.txt`, `scan.json` (~21 s) |

Notes: `meta` decrypts via sqlite3mc `PRAGMA cipher='chacha20'` + `PRAGMA hexkey='<64 hex>'` (the `key=x'...'` and `raw:` forms both fail); `VACUUM INTO` re-encrypts the target, so `dump_meta.py plain` copies schema+rows explicitly.

**Cutt bundles ship full TypeTrees, so field names and keyframe counts from these tools are ground truth.** All 70 worksheets across all 59 songs share one 128-field schema — no version drift. Only `son<id>_camera` and `type_01..type_10` carry the trees; the `cutt_son<id>` prefab and `data` bundles are stripped.

**Getting a track's real field name without the dump tools** — Unity matches bundle TypeTree fields by name, case-sensitively. A field whose name doesn't match is never populated (it does *not* become null — Unity initialises every serialised `List<T>` to an empty list, so null-vs-empty tells you nothing). To test a candidate name, declare it on `LiveTimelineWorkSheet` and check whether it receives keys:

```csharp
LiveTimelineWorksheetDiag.Probe(sheet, "candidateFieldName");
```

Unsure about casing? Declare both spellings side by side — only the correct one fills. This is how `WashLightList` (capital W) was confirmed; note upstream katboi01 uses `washLightList`, which never populates.

## Live Timeline Architecture

The Live playback system is in `Assets/Scripts/umamusume/Gallop/Live/`:

```
Director.cs              ← singleton, wires everything together
Cutt/
  LiveTimelineControl.cs ← dispatches timeline events each frame (~1900 lines)
  LiveTimelineWorkSheet.cs ← all track data fields (ScriptableObject)
  LiveTimelineDataList/   ← one .cs per track type
  UpdateInfo/             ← structs passed to event handlers
StageController.cs        ← manages stage GameObjects, StageObjectMap
```

**Track implementation pattern** (repeat for each new track):
1. Add data class in `LiveTimelineDataList/` (extend `LiveTimelineKeyWithInterpolate` for key, add group wrapper implementing `ILiveTimelineGroupDataWithName`)
2. Add `[SerializeField] public List<...> trackList;` to `LiveTimelineWorkSheet`
3. Add `public event Action<TData, TKey> OnUpdateXxx;` in `LiveTimelineControl`
4. Call `AlterUpdate_SimpleListControl(workSheet.trackList, d => d.keys, OnUpdateXxx, _currentFrame)` in `AlterLateUpdate()`
5. Subscribe `_liveTimelineControl.OnUpdateXxx += handler` in `Director.InitializeTimeline()`

**Important:** `LiveTimelineKeyWithInterpolate` already declares `frame`, `attribute`, `interpolateType`, `curve`, `easingType` — do NOT redeclare these in subclasses.

**Stage shader colour channels** (enumerated at runtime via `Shader.GetPropertyType`, confirmed on stage live10149 / song 1177):

| shader | colour | brightness | other |
|---|---|---|---|
| `Gallop/3D/Live/Stage/DefaultNoAmbient` | `_MulColor0` | `_ColorPower` | |
| `Gallop/3D/Live/Stage/DefaultEnvMapNoAmbient` | `_MulColor0` | `_ColorPower` | `_AddColor`, `_EnvMap` |
| `Gallop/3D/Live/Stage/DefaultTransparentNoAmbient` | `_MulColor0` | `_ColorPower` | `_AmbientColor` |
| `Gallop/3D/Live/Stage/LightBlinkBlend` | `_BlinkLightColor` | `_ColorPower` | `_SrcBlend`/`_DstBlend` |
| `Gallop/3D/Live/Stage/LightBlend` / `LightAdd1` | `_MulColor0`, `_MulColor1` | `_ColorPower`, `_ColorPowerMultiply` | |
| `Gallop/3D/Live/Stage/Laser` | `_MulColor0`, `_MulColor1` | `_ColorPower` | `_RandomValue` |
| `Gallop/3D/Bg/BgMirrorBall` | `_MulColor0` | **(none)** | |
| `Gallop/3D/Live/Stage/StageMirrorBallShine` | (none) | | |
| `Gallop/3D/Live/Stage/StageTransmittedLightMask` | (none) | | `_TransmittedLightMaskScale` |

**Brightness is a separate channel — do not fold it into the colour.** Write the key's colour to the colour property and its `power` to `_ColorPower`; let the shader combine them. Only fall back to `colour * power` where `_ColorPower` is absent (`BgMirrorBall`).

Dump any shader's real property table with:

```python
# tools/, via ~/.venvs/umatools/bin/python3
env = MetaDb().load("shader")          # 499 shaders
pf = obj.read_typetree()["m_ParsedForm"]   # m_Name, m_PropInfo.m_Props
```

**`_MulColor0` is on every stage shader and is BgColor1's channel; `_AmbientColor` belongs to BgColor2.** They are separate properties on the same shader, so the two tracks must not write the same one. Same naming family as WashLight's `MulColor0` and UVScrollLight's `mulColor1`.

⚠️ A material's `m_SavedProperties` in the bundle lists properties it no longer has — it retains entries from whatever shader it was authored against. `Material.HasProperty()` asks the *current shader*. Always enumerate the shader, never trust the bundle's saved-property table.

**Track group names are not always GameObject names.** Four separate silent failures were traced to this in one session, so check the mapping before assuming a track is unimplemented:
- a whitelist filter in the dispatcher dropped 92% of BgColor1's keys (`validBgColorNames`)
- `StageObjectMap` dedupes by name, so a crowd of 66 same-named objects only ever got its first member
- the Object track's `neonsign` is a `_stageObjectUnits` **unit name**, not a GameObject — no object of that name exists
- `ObjectUpdateInfo.OffsetType` was populated by the dispatcher but never read by `StageController`, so `Add`-relative positions were written as absolute

All four failed silently, with no error. Treat a track that "works" but looks wrong as a routing problem first.

**StageObjectMap:** Stage child GameObjects are indexed by name. Objects whose names match `IsTimelineControlledLight()` (`blinklight`, `spotlight3d`, `_wash_`, `laser`) start `SetActive(false)` — their handlers call `SetActive(true)` when the track fires. All other objects (neonsigns, glow meshes, UV scroll objects, etc.) keep their prefab default state.

## Working rule: no ground truth, no implementation

Field names, keyframe values and shader property tables are all recoverable from the bundles
(`tools/`), so **verify before you write code** — and when something genuinely cannot be
recovered, leave it unimplemented with a TODO rather than guessing a value.

Guessing has cost real time in this project: the PostFilm vignette geometry was guessed twice
(full-screen wash, then a huge border) before it turned out the defining shader isn't shipped in
any bundle. `PostFilmRendererFeature._enableRendering` is now `false` for exactly this reason.

Note the distinction that has caught me out: *"the property exists"* is not *"this field maps to
that property"*. `_ColorPower` is present on the stage shaders, but there is no evidence that
BgColor1's `power` field drives it — so that write was reverted while BlinkLight's was kept
(there the shader `LightBlinkBlend` is named for the track and pairs `_BlinkLightColor` with
`_ColorPower`, which is a much tighter fit).

Open items and why each is blocked are tracked in `LIVE_TRACKS.md` → 「已知未解问题」.

## Currently Implemented Tracks

| Track | Data class | Notes |
|-------|-----------|-------|
| Camera (pos/lookat/fov/roll/switcher) | existing | fully working |
| CharaMotionSequence | existing | |
| Facial / LipSync / FormationOffset | existing | |
| GlobalLight (48) | existing | sets rim light shader props on characters |
| BgColor1 | existing | **two branches.** Groups named `CharaCenter/CharaLeft/CharaRight/CharaColor` (`LiveTimelineControl.CharaBgColorNames`) → character toon props (`_CharaColor`, `_ToonDarkColor`, …). Every other group name → stage renderers, resolved by walking the whole stage hierarchy for same-named Transforms (`Director.ResolveStageTargets`), writing **`_MulColor0`** via `MaterialPropertyBlock`. |
| BgColor2 | existing | sets `_AmbientColor` on stage renderers via `Lerp(color1, color2, value)` |
| Transform / Object | existing | handled by `StageController.cs` |
| Effect (60) | `LiveTimelineEffectData` | loads prefab from `3d/effect/live/pfb_{name}` |
| GlobalFog (49) | `LiveTimelineGlobalFogData` | sets `RenderSettings.fog*` |
| Spotlight3d (68) | `LiveTimelineSpotlight3dData` | looks up `keyData.assetName` in StageObjectMap |
| UVScrollLight (46) | `LiveTimelineUVScrollLightData` | sets texture UV on stage materials |
| VolumeLight (37) | `LiveTimelineVolumeLightData` | data deserialized, no visual (component absent) |
| LightShafts (50) | `LiveTimelineLightShaftsData` | data deserialized, no visual (component absent) |
| Particle (41) | `LiveTimelineParticleData` | sets ParticleSystem.emission.rateOverTime |
| ParticleGroup (42) | `LiveTimelineParticleGroupData` | sets FlickerLightRate |
| ChromaticAberration (73) | `LiveTimelineChromaticAberrationData` | URP `ChromaticAberration.intensity` ← `power`；per-channel offset 无法映射 |
| HdrBloom (38) | `LiveTimelineHdrBloomData` | **dead code** — 0/58 songs carry data (`hdrBloomKeys` empty in every scan and in the song-1177 dump). Safe to delete. |
| ColorCorrection (61) | `LiveTimelineColorCorrectionData` | URP `ColorAdjustments.saturation` + `ColorCurves` RGB；depth/blend curve 无 URP 对应 |
| BlinkLight | `LiveTimelineBlinkLightData` | SetActive + child Light color/intensity (56/58 songs). **Biggest lighting payload by far** — 全语料 19420 keys / 57 songs, and over half are named `*_wash_*` (`wash_truss_a` 147, `wash_truss_b` 147, `wash_ground_a` 51, …). `color1Array` / `LightBlendMode` / `isReverseHueArray` still unused. |
| WashLight | `LiveTimelineWashLightData` | SetActive only (5/58 songs). Low value: real "wash light" output comes from the BlinkLight `*_wash_*` groups above, not this track — song 1177 has just 1 group / 11 keys here. |
| Laser | `LiveTimelineLaserData` | SetActive + position/rotation/scale — blink/raycast not implemented (6/58 songs) |

## Track Coverage (from 58-song scan)

**The pre-2026-08-04 scan data in `LIVE_TRACKS.md` was wrong** and has been re-measured with `tools/`. Two systematic errors: a batch of tracks marked "WorkSheet 无 X 字段" do have the field (`MobControlKeys`, `CyalumeControlKeys`, `nodeScaleList`, `sweatLocatorList`, `monitorCameraPos/LookAtKeys`, `facialNoiseKeys`, `charaMotionNoiseKeys`, …), and a batch marked "全 0 keyframe 空占位" carry thousands of keys (`postFilmKeys`/`postFilm2Keys`/`postFilm3Keys` ≈20 200 keys over 59/59 songs, `radialBlurKeys`, `tiltShiftKeys`, `charaFootLightKeys`, …). Rows tagged ✅实测 are trustworthy; untagged rows are still old data. **Re-verify with `tools/` before acting on any untagged row.**

Coverage numbers measure **what exists in the bundles**, not what the C# consumes — the two diverge. When a track looks under-powered, check the dispatcher in `AlterUpdate_*` for filters before assuming it's unimplemented (BgColor1 was dropping ~92% of its keys that way).

**Measuring what a song actually carries**: `LiveTimelineWorksheetDiag.Dump()` is called from `Director.InitializeTimeline()` and logs, per worksheet, every track field with its group names and key counts. Confirmed on song 1177: `worksheetList.Count == 1`, `SheetType == MainLive`, so reading `worksheetList[0]` loses nothing.
