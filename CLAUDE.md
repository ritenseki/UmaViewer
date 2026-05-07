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

**meta DB decryption**: `UmaDatabaseController.ReadMetaFromEncryptedDb()` uses `sqlite3mc_x64.dll` with cipher=3 (ChaCha20). Final key = `DBKey[i] XOR DBBaseKey[i % 13]`. A decrypted copy lives at `C:\Users\riten\Desktop\UmaCrack\meta_plain.db`.

**Asset bundle paths** (key formats):
- Live cutt timelines: `cutt/cutt_son{music_id}/cutt_son{music_id}`
- Live effect prefabs: `3d/effect/live/pfb_{effectName}`
- Stage prefabs: `3d/env/live/live{bgId}/pfb_env_live{bgId}_controller000`

**Python tools** (`C:\Users\riten\Desktop\UmaCrack\`):
- `dump_meta_cutt.py` — decrypts meta DB → `meta_plain.db`, run with Windows Python
- `read_cutt_effect.py` — loads cutt bundles via UnityPy, dumps WorkSheet field TypeTrees

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

**StageObjectMap:** Stage child GameObjects are indexed by name. Objects with "light" in their name start `SetActive(false)` but ARE registered — handlers call `SetActive(true)` as needed.

## Currently Implemented Tracks

| Track | Data class | Notes |
|-------|-----------|-------|
| Camera (pos/lookat/fov/roll/switcher) | existing | fully working |
| CharaMotionSequence | existing | |
| Facial / LipSync / FormationOffset | existing | |
| GlobalLight (48) | existing | sets rim light shader props on characters |
| BgColor1/2 | existing | sets toon shader color props |
| Transform / Object | existing | handled by `StageController.cs` |
| Effect (60) | `LiveTimelineEffectData` | loads prefab from `3d/effect/live/pfb_{name}` |
| GlobalFog (49) | `LiveTimelineGlobalFogData` | sets `RenderSettings.fog*` |
| Spotlight3d (68) | `LiveTimelineSpotlight3dData` | looks up `keyData.assetName` in StageObjectMap |
| UVScrollLight (46) | `LiveTimelineUVScrollLightData` | sets texture UV on stage materials |
| VolumeLight (37) | `LiveTimelineVolumeLightData` | data deserialized, no visual (component absent) |
| LightShafts (50) | `LiveTimelineLightShaftsData` | data deserialized, no visual (component absent) |
| Particle (41) | `LiveTimelineParticleData` | sets ParticleSystem.emission.rateOverTime |
| ParticleGroup (42) | `LiveTimelineParticleGroupData` | sets FlickerLightRate |
| BlinkLight | `LiveTimelineBlinkLightData` | SetActive + child Light color/intensity from timeline data (56/58 songs) |
| WashLight | `LiveTimelineWashLightData` | SetActive only — RaycastDistance/CameraProjection fields unused (5/58 songs) |
| Laser | `LiveTimelineLaserData` | SetActive + position/rotation/scale — blink/raycast not implemented (6/58 songs) |

## Track Coverage (from 58-song scan)

All high-priority tracks from the 58-song scan are now implemented. Field definitions are obtained by running `read_cutt_effect.py` to dump the WorkSheet TypeTree from a cutt bundle.
