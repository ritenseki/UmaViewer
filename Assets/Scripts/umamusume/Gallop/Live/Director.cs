using Gallop.Live.Cutt;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.Live
{
    public class Director : MonoBehaviour
    {
        private static Director _instance = null;
        public LiveTimelineControl _liveTimelineControl; //Edited to public
        [SerializeField]
        public float _liveCurrentTime;  //Edited to public
        public bool _isLiveSetup; //Edit to pulic
        public StageController _stageController; //Edited to public
        [SerializeField]
        private GameObject[] _cameraNodes;
        private Camera[] _cameraObjects;
        private Transform[] _cameraTransforms;
        [SerializeField]
        private CameraLookAt _cameraLookAt;
        private int _activeCameraIndex  = 1;
        private readonly int[] kTimelineCameraIndices = new int[3] { 1, 2, 3 };

        public static Director instance => _instance;

        //real work start
        public LiveEntry live;
        private const string CUTT_PATH = "cutt/cutt_son{0}/cutt_son{0}";
        private const string STAGE_PATH = "3d/env/live/live{0}/pfb_env_live{0}_controller000";
        private const string SONG_PATH = "sound/l/{0}/snd_bgm_live_{0}_oke_01";
        private const string VOCAL_PATH = "sound/l/{0}/snd_bgm_live_{0}_chara_{1}_01";
        private const string RANDOM_VOCAL_PATH = "sound/l/{0}/snd_bgm_live_{0}_chara";
        private const string LIVE_PART_PATH = "live/musicscores/m{0}/m{0}_part";
        private const string EFFECT_PATH = "3d/effect/live/pfb_{0}";

        private UmaViewerBuilder Builder => UmaViewerBuilder.Instance;

        public List<Transform> charaObjs;

        public List<UmaContainerCharacter> CharaContainerScript = new List<UmaContainerCharacter>();

        public List<Animation> charaAnims;
        public List<UmaViewerAudio.CuteAudioSource> liveVocal = new List<UmaViewerAudio.CuteAudioSource>();

        // effect track: maps effectList entry -> (last key frame, active instance)
        private Dictionary<LiveTimelineEffectData, (int frame, GameObject instance)> _activeEffects
            = new Dictionary<LiveTimelineEffectData, (int, GameObject)>();

        private Dictionary<string, Vector2> _uvScrollAccum = new Dictionary<string, Vector2>();

        private Volume _postProcessVolume;

        public UmaViewerAudio.CuteAudioSource liveMusic = new UmaViewerAudio.CuteAudioSource();

        public PartEntry partInfo;

        public bool _syncTime = false;
        public bool _soloMode = false;

        public int characterCount = 0;
        public int allowCount = 0;

        public int liveMode = 1;

        public LiveViewerUI UI;

        public float totalTime;

        public SliderControl sliderControl;

        public bool IsRecordVMD;

        public bool RequireStage = true;

        public Transform MainCameraTransform => _mainCameraTransform;

        private Transform _mainCameraTransform;

        public bool isTimelineControlled
        {
            get
            {
                if (_liveTimelineControl != null)
                {
                    return _liveTimelineControl.data != null;
                }
                return false;
            }
        }

        public float CalcFrameJustifiedMusicTime()
        {
            if (isTimelineControlled)
            {
                return Mathf.RoundToInt(musicScoreTime * 60f) / 60f;
            }
            return musicScoreTime;
        }

        public float musicScoreTime => Mathf.Clamp(smoothMusicScoreTime, 0f, 99999f);

        private float smoothMusicScoreTime => _liveCurrentTime;//temp to liveCurrentTime

        public void Initialize()
        {
            if (live != null)
            {
                _instance = this;
                Debug.Log(string.Format(CUTT_PATH, live.MusicId));
                Builder.LoadAssetPath(string.Format(CUTT_PATH, live.MusicId), transform);
                if (RequireStage)
                {
                    Debug.Log(live.BackGroundId);
                    Builder.LoadAssetPath(string.Format(STAGE_PATH, live.BackGroundId), transform);
                    _liveTimelineControl.StageObjectMap = _stageController.StageObjectMap;
                }

                //Make CharacterObject

                var characterStandPos = _liveTimelineControl.transform.Find("CharacterStandPos");
                int counter = 0;
                var standPos = characterStandPos.GetComponentsInChildren<Transform>();
                var count = _liveTimelineControl.data.characterSettings.useHighPolygonModel.Length;
                for (int i = 0; i < count; i++)
                {
                    if (i < characterStandPos.childCount)
                    {
                        var newObj = Instantiate(standPos[i + 1], transform);
                        newObj.gameObject.name = string.Format("CharacterObject{0}", counter);
                        charaObjs.Add(newObj.transform);
                        counter++;
                    }
                    else
                    {
                        var newObj = Instantiate(standPos[i % characterStandPos.childCount + 1], transform);
                        newObj.gameObject.name = string.Format("CharacterObject{0}", counter);
                        charaObjs.Add(newObj.transform);
                        counter++;
                    }
                };


                //Get live parts info
                UmaDatabaseEntry partAsset = UmaViewerMain.Instance.AbList[string.Format(LIVE_PART_PATH, live.MusicId)];
                UmaViewerAudio.LastAudioPartIndex = -1;

                Debug.Log(partAsset.Name);

                AssetBundle bundle = UmaAssetManager.LoadAssetBundle(partAsset);
                TextAsset partData = bundle.LoadAsset<TextAsset>($"m{live.MusicId}_part");
                partInfo = new PartEntry(partData.text);

            }
        }

        public void InitializeUI()
        {
            UI = GameObject.Find("LiveUI").GetComponent<LiveViewerUI>();

            sliderControl = UI.ProgressBar.GetComponent<SliderControl>();
            LiveViewerUI.Instance.RecordingUI.SetActive(IsRecordVMD);
            LiveViewerUI.Instance.RecordingText.text = $"�� Recording...\r\n VMD will be saved in {Path.GetFullPath(Application.dataPath + UnityHumanoidVMDRecorder.FileSavePath)}";
        }

        public void InitializeTimeline(List<LiveCharacterSelect> characters, int mode)
        {
            _uvScrollAccum.Clear();
            totalTime = _liveTimelineControl.data.timeLength;

            liveMode = mode;

            allowCount = characters.Count;

            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].CharaEntry.Name != "")
                {
                    characterCount += 1;
                }
            }
            if (characterCount == 1)
            {
                _soloMode = true;
            }

            _liveTimelineControl.InitCharaMotionSequence(_liveTimelineControl.data.characterSettings.motionSequenceIndices);

            _liveTimelineControl.OnUpdateLipSync += OnLipSyncUpdate;
            _liveTimelineControl.OnUpdateFacial += OnFacialUpdate;
            _liveTimelineControl.OnUpdateGlobalLight += OnGlobalLightUpdate;
            _liveTimelineControl.OnUpdateBgColor1 += OnBgColor1Update;

            SetupCharacterLocator();
            InitializeCamera();
            UpdateMainCamera();
            InitializeMultiCamera(_liveTimelineControl);
            for (int i = 0; i < kTimelineCameraIndices.Length; i++)
            {
                int num = kTimelineCameraIndices[i];
                if (num < _cameraObjects.Length)
                {
                    _liveTimelineControl.SetTimelineCamera(_cameraObjects[num], i);
                }
            }

            _liveTimelineControl.OnUpdateCameraSwitcher += OnCameraSwitcherUpdate;

            _liveTimelineControl.OnUpdateBgColor2 += OnBgColor2Update;
            _liveTimelineControl.OnUpdateEffect += OnEffectUpdate;
            _liveTimelineControl.OnUpdateGlobalFog += OnGlobalFogUpdate;
            _liveTimelineControl.OnUpdateSpotlight3d += OnSpotlight3dUpdate;
            _liveTimelineControl.OnUpdateUVScrollLight += OnUVScrollLightUpdate;
            _liveTimelineControl.OnUpdateVolumeLight += OnVolumeLightUpdate;
            _liveTimelineControl.OnUpdateLightShafts += OnLightShaftsUpdate;
            _liveTimelineControl.OnUpdateParticle += OnParticleUpdate;
            _liveTimelineControl.OnUpdateParticleGroup += OnParticleGroupUpdate;
            _liveTimelineControl.OnUpdateWashLight += OnWashLightUpdate;
            _liveTimelineControl.OnUpdateLaser += OnLaserUpdate;
            _liveTimelineControl.OnUpdateBlinkLight += OnBlinkLightUpdate;
            _liveTimelineControl.OnUpdateChromaticAberration += OnChromaticAberrationUpdate;
            _liveTimelineControl.OnUpdateHdrBloom += OnHdrBloomUpdate;
            _liveTimelineControl.OnUpdateColorCorrection += OnColorCorrectionUpdate;

            // 获取或创建摄像机上的 Volume 组件，供后处理 handler 使用
            var mainCam = Camera.main;
            if (mainCam != null)
            {
                _postProcessVolume = mainCam.GetComponent<Volume>();
                if (_postProcessVolume == null)
                    _postProcessVolume = mainCam.gameObject.AddComponent<Volume>();
                if (_postProcessVolume.profile == null)
                    _postProcessVolume.profile = ScriptableObject.CreateInstance<VolumeProfile>();

                var profile = _postProcessVolume.profile;
                if (!profile.Has<ChromaticAberration>()) profile.Add<ChromaticAberration>(true);
                if (!profile.Has<Bloom>())              profile.Add<Bloom>(true);
                if (!profile.Has<ColorAdjustments>())   profile.Add<ColorAdjustments>(true);
                if (!profile.Has<ColorCurves>())        profile.Add<ColorCurves>(true);
            }
        }

        private void OnEffectUpdate(LiveTimelineEffectData effectData, LiveTimelineKeyEffectData keyData)
        {
            if (keyData == null) return;

            // Only (re)instantiate when the key frame changes
            if (_activeEffects.TryGetValue(effectData, out var current) && current.frame == keyData.frame)
            {
                // Same key — update position if following owner
                if (current.instance != null)
                    ApplyEffectTransform(current.instance.transform, keyData);
                return;
            }

            // Destroy previous instance
            if (_activeEffects.TryGetValue(effectData, out var old) && old.instance != null)
                Destroy(old.instance);

            // Load prefab
            string path = string.Format(EFFECT_PATH, effectData.name);
            if (!UmaViewerMain.Instance.AbList.ContainsKey(path))
            {
                _activeEffects[effectData] = (keyData.frame, null);
                return;
            }

            AssetBundle bundle = UmaAssetManager.LoadAssetBundle(UmaViewerMain.Instance.AbList[path]);
            if (bundle == null)
            {
                _activeEffects[effectData] = (keyData.frame, null);
                return;
            }

            GameObject prefab = bundle.LoadAsset<GameObject>(System.IO.Path.GetFileName(path));
            if (prefab == null)
            {
                _activeEffects[effectData] = (keyData.frame, null);
                return;
            }

            GameObject instance = Instantiate(prefab, transform);
            ApplyEffectTransform(instance.transform, keyData);
            _activeEffects[effectData] = (keyData.frame, instance);
        }

        private void ApplyEffectTransform(Transform t, LiveTimelineKeyEffectData keyData)
        {
            Vector3 basePos = Vector3.zero;

            // owner == World (18) or out of range: world origin
            int ownerIndex = keyData.owner;
            if (ownerIndex >= 0 && ownerIndex < CharaContainerScript.Count)
            {
                var container = CharaContainerScript[ownerIndex];
                if (container != null)
                {
                    basePos = new Vector3(
                        keyData.IsLinkOwnerPositionX ? container.transform.position.x : 0f,
                        keyData.IsLinkOwnerPositionY ? container.transform.position.y : 0f,
                        keyData.IsLinkOwnerPositionZ ? container.transform.position.z : 0f
                    );
                }
            }

            t.position = basePos + keyData.offset;
            t.eulerAngles = keyData.offsetAngle;
            t.localScale = keyData.offsetScale;
        }

        private void OnLipSyncUpdate(LiveTimelineKeyIndex keyData_, float liveTime_)
        {
            var prevKey = keyData_.prevKey as LiveTimelineKeyLipSyncData;
            var curKey  = keyData_.key     as LiveTimelineKeyLipSyncData;
            var nextKey = keyData_.nextKey as LiveTimelineKeyLipSyncData;
            for (int k = 0; k < charaObjs.Count; k++)
            {
                if (k < CharaContainerScript.Count)
                    CharaContainerScript[k].FaceDrivenKeyTarget.AlterUpdateAutoLip(prevKey, curKey, liveTime_, ((int)curKey.character >> k) % 2);
            }
        }

        private void OnFacialUpdate(FacialDataUpdateInfo updateInfo_, float liveTime_, int position)
        {
            if (position < charaObjs.Count)
                CharaContainerScript[position].FaceDrivenKeyTarget.AlterUpdateFacialNew(ref updateInfo_, liveTime_);
        }

        private void OnGlobalLightUpdate(ref GlobalLightUpdateInfo updateInfo)
        {
            var tmpPos = -(updateInfo.lightRotation * Vector3.forward).normalized;
            foreach (var locator in _liveTimelineControl.liveCharactorLocators)
            {
                if (locator == null || !updateInfo.flags.hasFlag(locator.liveCharaStandingPosition) || locator is not LiveTimelineCharaLocator charaLocator) continue;
                var container = charaLocator.UmaContainer;
                if (!container) continue;
                MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                propertyBlock.SetFloat("_RimShadowRate",    updateInfo.globalRimShadowRate);
                propertyBlock.SetColor("_RimColor",          updateInfo.rimColor);
                propertyBlock.SetFloat("_RimStep",           updateInfo.rimStep);
                propertyBlock.SetFloat("_RimFeather",        updateInfo.rimFeather);
                propertyBlock.SetFloat("_RimSpecRate",       updateInfo.rimSpecRate);
                propertyBlock.SetFloat("_RimHorizonOffset",  updateInfo.RimHorizonOffset);
                propertyBlock.SetFloat("_RimVerticalOffset", updateInfo.RimVerticalOffset);
                propertyBlock.SetFloat("_RimHorizonOffset2",  updateInfo.RimHorizonOffset2);
                propertyBlock.SetFloat("_RimVerticalOffset2", updateInfo.RimVerticalOffset2);
                propertyBlock.SetColor("_RimColor2",         updateInfo.rimColor2);
                propertyBlock.SetFloat("_RimStep2",          updateInfo.rimStep2);
                propertyBlock.SetFloat("_RimFeather2",       updateInfo.rimFeather2);
                propertyBlock.SetFloat("_RimSpecRate2",      updateInfo.rimSpecRate2);
                propertyBlock.SetFloat("_RimShadowRate2",    updateInfo.globalRimShadowRate2);
                foreach (var renderer in container.Renderers)
                {
                    renderer.SetPropertyBlock(propertyBlock);
                    foreach (var mat in renderer.materials)
                    {
                        mat.SetFloat("_UseOriginalDirectionalLight", 1);
                        mat.SetVector("_OriginalDirectionalLightDir", tmpPos);
                    }
                }
            }
        }

        private void OnBgColor1Update(ref BgColor1UpdateInfo updateInfo)
        {
            foreach (var locator in _liveTimelineControl.liveCharactorLocators)
            {
                var EFlags = (LiveCharaPositionFlag)updateInfo.flags;
                if (locator == null || (updateInfo.flags != 0 && !EFlags.hasFlag(locator.liveCharaStandingPosition)) || locator is not LiveTimelineCharaLocator charaLocator) continue;
                var container = charaLocator.UmaContainer;
                if (!container) continue;
                MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                propertyBlock.SetColor("_CharaColor",    updateInfo.color);
                propertyBlock.SetColor("_ToonDarkColor", updateInfo.toonDarkColor);
                propertyBlock.SetColor("_ToonBrightColor", updateInfo.toonBrightColor);
                propertyBlock.SetColor("_OutlineColor",  updateInfo.outlineColor);
                propertyBlock.SetFloat("_Saturation",    updateInfo.Saturation);
                foreach (var renderer in container.Renderers)
                    renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private void OnCameraSwitcherUpdate(int cameraIndex_)
        {
            if (cameraIndex_ < 0)
                _activeCameraIndex = 0;
            else if (cameraIndex_ < kTimelineCameraIndices.Length)
                _activeCameraIndex = kTimelineCameraIndices[cameraIndex_];
        }

        private void OnBgColor2Update(ref BgColor2UpdateInfo updateInfo)
        {
            // color1/color2 are a two-tone gradient; value is blend power.
            // Exact target shader properties are unconfirmed — driving ambient gradient for now.
            // TODO: verify against original game; may target stage background mesh materials instead.
            RenderSettings.ambientSkyColor     = Color.Lerp(updateInfo.color1, updateInfo.color2, updateInfo.value);
            RenderSettings.ambientEquatorColor = updateInfo.color1;
            RenderSettings.ambientGroundColor  = updateInfo.color2;
        }

        private void OnGlobalFogUpdate(LiveTimelineGlobalFogData fogData, LiveTimelineKeyGlobalFogData keyData)
        {
            if (keyData == null) return;
            RenderSettings.fog = keyData.isDistance || keyData.isHeight || keyData.fogMode != 0;
            RenderSettings.fogColor = keyData.color;
            RenderSettings.fogMode = (FogMode)keyData.fogMode;
            RenderSettings.fogDensity = keyData.expDensity;
            RenderSettings.fogStartDistance = keyData.start;
            RenderSettings.fogEndDistance = keyData.end;
        }

        private void OnSpotlight3dUpdate(LiveTimelineSpotlight3dData spotData, LiveTimelineKeySpotlight3dData keyData)
        {
            if (keyData == null || _stageController == null) return;

            if (!_stageController.StageObjectMap.TryGetValue(keyData.assetName, out var go)) return;

            go.SetActive(keyData.isActive);
            if (!keyData.isActive) return;

            Vector3 basePos = Vector3.zero;
            if (keyData.characterIndex >= 0 && keyData.characterIndex < CharaContainerScript.Count)
                basePos = CharaContainerScript[keyData.characterIndex].transform.position;

            go.transform.position = basePos + keyData.position;
            go.transform.eulerAngles = keyData.rotation;
            go.transform.localScale = keyData.scale;

            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var mat in r.materials)
                {
                    mat.SetColor("_Color", keyData.color);
                    mat.SetFloat("_ColorPower", keyData.colorPower);
                }
        }

        private void OnUVScrollLightUpdate(LiveTimelineUVScrollLightData data, LiveTimelineKeyUVScrollLightData keyData)
        {
            if (keyData == null || _stageController == null) return;
            if (!_uvScrollAccum.ContainsKey(data.name))
                _uvScrollAccum[data.name] = Vector2.zero;
            _uvScrollAccum[data.name] += new Vector2(keyData.scrollSpeedX, keyData.scrollSpeedY) * Time.deltaTime;
            Vector2 totalOffset = new Vector2(keyData.scrollOffsetX, keyData.scrollOffsetY) + _uvScrollAccum[data.name];
            foreach (var r in _stageController.GetComponentsInChildren<Renderer>())
            {
                foreach (var mat in r.materials)
                {
                    if (mat.name.Replace(" (Instance)", "") != data.name) continue;
                    mat.SetTextureOffset("_MainTex", totalOffset);
                    mat.SetColor("_Color", keyData.mulColor0 * keyData.colorPower);
                    // TODO: mulColor1, ColorType0/1, CharacterIndex0/1, IsColorBlend0/1, loopType/loopCount
                }
            }
        }

        private void OnChromaticAberrationUpdate(LiveTimelineChromaticAberrationData data, LiveTimelineKeyChromaticAberrationData keyData)
        {
            if (keyData == null || _postProcessVolume == null) return;
            if (!_postProcessVolume.profile.TryGet<ChromaticAberration>(out var fx)) return;
            bool on = keyData.isEnable != 0;
            fx.active = on;
            if (on)
                fx.intensity.Override(keyData.power);
            // TODO: keyData.redOffset/greenOffset/blueOffset — per-channel displacement,
            // not expressible in URP built-in ChromaticAberration. clip, effectType unused.
        }

        private void OnHdrBloomUpdate(LiveTimelineHdrBloomData data, LiveTimelineKeyHdrBloomData keyData)
        {
            if (keyData == null || _postProcessVolume == null) return;
            if (!_postProcessVolume.profile.TryGet<Bloom>(out var fx)) return;
            fx.intensity.Override(keyData.bloomIntensity);
            fx.threshold.Override(keyData.threshold);
            // TODO: field mapping unconfirmed — no bundle data found. Verify when data becomes available.
        }

        private void OnColorCorrectionUpdate(LiveTimelineColorCorrectionData data, LiveTimelineKeyColorCorrectionData keyData)
        {
            if (keyData == null || _postProcessVolume == null) return;

            bool on = keyData.enable != 0;

            if (_postProcessVolume.profile.TryGet<ColorAdjustments>(out var ca))
            {
                ca.active = on;
                if (on)
                    // game: 1.0 = neutral; URP: 0 = neutral, range -100..100
                    ca.saturation.Override((keyData.saturation - 1f) * 100f);
            }

            if (_postProcessVolume.profile.TryGet<ColorCurves>(out var cc))
            {
                cc.active = on;
                if (on && keyData.redCurve != null)
                {
                    cc.red.Override(new TextureCurve(keyData.redCurve.keys, 0f, false, new Vector2(0f, 1f)));
                    cc.green.Override(new TextureCurve(keyData.greenCurve.keys, 0f, false, new Vector2(0f, 1f)));
                    cc.blue.Override(new TextureCurve(keyData.blueCurve.keys, 0f, false, new Vector2(0f, 1f)));
                }
            }
            // TODO: depthRedCurve/depthGreenCurve/depthBlueCurve — depth-based curves, no URP equivalent.
            // blendCurve, mode, selective, keyColor, targetColor unused.
        }

        private void OnBlinkLightUpdate(LiveTimelineBlinkLightData data, LiveTimelineKeyBlinkLightData keyData)
        {
            if (keyData == null || _stageController == null) return;
            if (!_stageController.StageObjectMap.TryGetValue(data.name, out var go)) return;
            go.SetActive(true);

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            if (keyData.pattern == 0)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    int idx = i < keyData.powerArray?.Length ? i : 0;
                    float power = keyData.powerArray != null && keyData.powerArray.Length > idx ? keyData.powerArray[idx] : 1f;
                    Color col = keyData.color0Array != null && keyData.color0Array.Length > idx ? keyData.color0Array[idx] : Color.white;
                    foreach (var mat in renderers[i].materials)
                    {
                        mat.SetColor("_Color", col);
                        mat.SetFloat("_ColorPower", power);
                    }
                }
                return;
            }

            // Blink: 按时间周期计算强度
            float elapsed = _liveTimelineControl.currentLiveTime - keyData.frame / 60f - keyData.waitTime;
            float intensity = ComputeBlinkIntensity(keyData, elapsed);
            for (int i = 0; i < renderers.Length; i++)
            {
                Color col = keyData.color0Array != null && i < keyData.color0Array.Length ? keyData.color0Array[i] : Color.white;
                foreach (var mat in renderers[i].materials)
                {
                    mat.SetColor("_Color", col * intensity);
                    mat.SetFloat("_ColorPower", intensity);
                }
            }
            // TODO: color1Array, LightBlendMode, isReverseHueArray, color blend fields unused
        }

        private static float ComputeBlinkIntensity(LiveTimelineKeyBlinkLightData keyData, float elapsed)
        {
            if (elapsed < 0f) return keyData.powerMin;

            float cycleDuration = keyData.turnOnTime + keyData.keepTime + keyData.turnOffTime + keyData.intervalTime;
            if (cycleDuration <= 0f) return keyData.powerMax;

            if (keyData.loopCount > 0 && elapsed >= cycleDuration * keyData.loopCount)
                return keyData.powerMin;

            float t = elapsed % cycleDuration;

            if (t < keyData.turnOnTime)
                return Mathf.Lerp(keyData.powerMin, keyData.powerMax, keyData.turnOnTime > 0f ? t / keyData.turnOnTime : 1f);
            t -= keyData.turnOnTime;

            if (t < keyData.keepTime)
                return keyData.powerMax;
            t -= keyData.keepTime;

            if (t < keyData.turnOffTime)
                return Mathf.Lerp(keyData.powerMax, keyData.powerMin, keyData.turnOffTime > 0f ? t / keyData.turnOffTime : 1f);

            return keyData.powerMin; // intervalTime: off
        }

        private void OnWashLightUpdate(LiveTimelineWashLightData data, LiveTimelineKeyWashLightData keyData)
        {
            if (keyData == null || _stageController == null) return;
            if (!_stageController.StageObjectMap.TryGetValue(data.name, out var go)) return;
            go.SetActive(true);

            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var mat in r.materials)
                {
                    mat.SetFloat("_ProjectorColorPower", keyData.CameraProjectionColorPower);
                    // TODO: RaycastDistance, CameraProjectionSide unused.
                    // _ProjectorMulColor0 (wash color) has no corresponding field in keyData.
                }
        }

        private void OnLaserUpdate(LiveTimelineLaserData data, LiveTimelineKeyLaserData keyData)
        {
            if (keyData == null || _stageController == null) return;
            if (!_stageController.StageObjectMap.TryGetValue(data.name, out var go)) return;
            go.SetActive(true);
            go.transform.localPosition = keyData.objectPosition;
            go.transform.localEulerAngles = keyData.objectRotate;
            go.transform.localScale = keyData.objectScale;
            // TODO: incomplete — keyData.blink/blinkPeriod (SetActive flicker), degLaserPitch (beam angle),
            // RaycastDistance (beam length via scale), formation/posInterval (multi-laser layout) unused.
        }

        private void OnVolumeLightUpdate(LiveTimelineVolumeLightData data, LiveTimelineKeyVolumeLightData keyData)
        {
            // SunShafts component not present in this build — data deserialized only
        }

        private void OnLightShaftsUpdate(LiveTimelineLightShaftsData data, LiveTimelineKeyLightShaftsData keyData)
        {
            // LightShaftsController component not present in this build — data deserialized only
        }

        private void OnParticleUpdate(LiveTimelineParticleData data, LiveTimelineKeyParticleData keyData)
        {
            if (keyData == null || _stageController == null) return;
            foreach (var ps in _stageController.GetComponentsInChildren<ParticleSystem>())
            {
                if (ps.gameObject.name != data.name) continue;
                var emission = ps.emission;
                emission.rateOverTime = keyData.emissionRate;
            }
        }

        private void OnParticleGroupUpdate(LiveTimelineParticleGroupData data, LiveTimelineKeyParticleGroupData keyData)
        {
            if (keyData == null || _stageController == null) return;
            foreach (var ps in _stageController.GetComponentsInChildren<ParticleSystem>())
            {
                if (ps.gameObject.name != data.name) continue;
                var emission = ps.emission;
                emission.rateOverTime = new ParticleSystem.MinMaxCurve(keyData.FlickerDarkRate, keyData.FlickerLightRate);
            }
        }

        public void InitializeCamera()
        {
            if (_cameraObjects == null)
            {
                _cameraObjects = new Camera[_cameraNodes.Length + 1];
                _cameraTransforms = new Transform[_cameraNodes.Length + 1];
                for (int i = 0; i < _cameraNodes.Length; i++)
                {
                    GameObject gameObject = _cameraNodes[i];
                    Camera camera = gameObject.GetComponent<Camera>();
                    if (camera == null)
                    {
                        camera = gameObject.GetComponentInChildren<Camera>();
                    }
                    //camera.cullingMask = num;
                    _cameraObjects[i] = camera;
                    _cameraTransforms[i] = camera.transform;
                }
            }
        }

        public void InitializeMultiCamera(LiveTimelineControl control)
        {
            var cameraCount = control.data.multiCameraSettings.cameraNum;
            MultiCamera[] cameras = new MultiCamera[cameraCount];
            var root = new GameObject("MultiCameras");
            root.transform.SetParent(control.transform);
            for (int i = 0; i < cameraCount; i++)
            {
                var camObj = new GameObject($"MultiCamera_{i}");
                camObj.transform.SetParent(root.transform);

                var cam = camObj.AddComponent<MultiCamera>();
                cam.Initialize();
                cameras[i] = cam;
                control.MultiRecordFrames.Add(new List<LiveCameraFrame>());
            }
            control.SetMultiCamera(cameras);
        }

        private void UpdateMainCamera()
        {
            if (_cameraObjects == null) return;
            for (int i = 0; i < _cameraNodes.Length; i++)
            {
                bool activeSelf = _cameraNodes[i].activeSelf;
                bool flag = i == _activeCameraIndex;
                _cameraNodes[i].SetActive(flag);
                if (i == 0 && activeSelf != flag && flag && _cameraLookAt != null)
                {
                    _cameraLookAt.ActivationUpdate();
                }
            }
            _mainCameraTransform = _cameraTransforms[_activeCameraIndex];
        }

        private void SetupCharacterLocator()
        {
            if (!_liveTimelineControl) return;
            for (int i = 0; i < CharaContainerScript.Count; i++)
            {
                var container = CharaContainerScript[i];
                container.LiveLocator = new LiveTimelineCharaLocator(container);
                container.LiveLocator.liveCharaStandingPosition = (LiveCharaPosition)i;
                _liveTimelineControl.liveCharactorLocators[i] = container.LiveLocator;
                container.LiveLocator.liveCharaInitialPosition = container.transform.position;
            }
        }

        public void InitializeMusic(int songid, List<LiveCharacterSelect> characters)
        {

            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].CharaEntry.Name != "" && i < partInfo.SingerCount)
                {
                    var charaid = characters[i].CharaEntry.Id;

                    var entry = UmaViewerMain.Instance.AbSounds.FirstOrDefault(a => a.Name.Contains(string.Format(VOCAL_PATH, songid, charaid)) && a.Name.EndsWith("awb"));
                    if (entry == null)
                    {
                        List<UmaDatabaseEntry> entries = new List<UmaDatabaseEntry>();
                        foreach (var random in UmaViewerMain.Instance.AbSounds.Where(a => (a.Name.Contains(string.Format(RANDOM_VOCAL_PATH, songid)) && a.Name.EndsWith("awb"))))
                        {
                            entries.Add(random);
                        }
                        if (entries.Count > 0)
                        {
                            entry = entries[UnityEngine.Random.Range(0, entries.Count - 1)];
                        }
                    }

                    if (entry != null)
                    {
                        Debug.Log(entry.Name);
                        liveVocal.Add(UmaViewerAudio.ApplySound(entry.Name.Split('.')[0], i));
                    }
                }
            }


            liveMusic = UmaViewerAudio.ApplySound(string.Format(SONG_PATH, songid), -1);
        }

        public void Play()
        {

            foreach (var vocal in liveVocal)
            {
                UmaViewerAudio.Play(vocal);
            }
            UmaViewerAudio.Play(liveMusic);

            _isLiveSetup = true;
            _liveCurrentTime = 0;

            if (IsRecordVMD)
            {
                foreach (var container in CharaContainerScript)
                {
                    var rootbone = container.transform.Find("Position");
                    var newRecorder = rootbone.gameObject.AddComponent<UnityHumanoidVMDRecorder>();
                    newRecorder.UseParentOfAll = true;
                    newRecorder.UseAbsoluteCoordinateSystem = true;
                    newRecorder.Initialize();
                    if (!newRecorder.IsRecording)
                    {
                        newRecorder.StartRecording(true);
                    }
                }
            }
        }

        private void OnTimelineUpdate(float _liveCurrentTime)
        {
            _liveTimelineControl.AlterUpdate(_liveCurrentTime);
            if (!_soloMode)
            {
                UmaViewerAudio.AlterUpdate(_liveCurrentTime, partInfo, liveVocal, sliderControl.is_Outed);
            }
        }

        bool isExit;
        void Update()
        {
            if (isExit) return;

            if (_isLiveSetup)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || _liveCurrentTime >= totalTime)
                {
                    ExitLive();
                }

                if (_syncTime == false)
                {
                    if(liveMusic.sourceList.Count == 0)
                    {
                        _syncTime = true;
                    }
                    else if (liveMusic.sourceList[0].time > 0.01)
                    {
                        _liveCurrentTime = liveMusic.sourceList[0].time;
                        _syncTime = true;
                    }
                }
                else
                {
                    if (IsRecordVMD)
                    {
                        _liveCurrentTime += (1 / 60f);
                        if (liveMusic != null)
                        {
                            UmaViewerAudio.Stop(liveMusic);
                            foreach (var vocal in liveVocal)
                            {
                                UmaViewerAudio.Stop(vocal);
                            }
                        }

                        UI.ProgressBar.SetValueWithoutNotify(_liveCurrentTime / totalTime);
                        OnTimelineUpdate(_liveCurrentTime);
                        _liveTimelineControl.AlterLateUpdate();
                    }
                    else if (sliderControl.is_Outed)
                    {
                        _liveCurrentTime = UI.ProgressBar.value * totalTime;

                        if (liveMusic != null)
                        {
                            UmaViewerAudio.SetTime(liveMusic, _liveCurrentTime);

                            foreach (var vocal in liveVocal)
                            {
                                UmaViewerAudio.SetTime(vocal, _liveCurrentTime);
                            }

                            UmaViewerAudio.Play(liveMusic);

                            foreach (var vocal in liveVocal)
                            {
                                UmaViewerAudio.Play(vocal);
                            }
                        }

                        OnTimelineUpdate(_liveCurrentTime);

                        sliderControl.is_Outed = false;
                        sliderControl.is_Touched = false;
                        _syncTime = false;
                    }
                    else if (sliderControl.is_Touched)
                    {
                        _liveCurrentTime = UI.ProgressBar.value * totalTime;

                        if (liveMusic != null)
                        {
                            UmaViewerAudio.Stop(liveMusic);
                            foreach (var vocal in liveVocal)
                            {
                                UmaViewerAudio.Stop(vocal);
                            }
                        }

                        OnTimelineUpdate(_liveCurrentTime);
                    }
                    else
                    {
                        _liveCurrentTime += Time.deltaTime;
                        UI.ProgressBar.SetValueWithoutNotify(_liveCurrentTime / totalTime);
                        OnTimelineUpdate(_liveCurrentTime);
                    }
                }

                UpdateMainCamera();
            }
        }

        private void LateUpdate()
        {
            if (_isLiveSetup && _syncTime && !IsRecordVMD)
            {
                _liveTimelineControl.AlterLateUpdate();
            }
        }

        private void FixedUpdate()
        {
            LiveViewerUI.Instance.UpdateLyrics(_liveCurrentTime);
        }

        DateTime ExitTime;
        private void ExitLive()
        {
            isExit = true;
            if (_liveTimelineControl.IsRecordVMD)
            {
                ExitTime = DateTime.Now;
                SaveCameraVMD();
                SaveMultiCameraVMD();
                SaveCharacterVMD();
            }
            UmaSceneController.LoadScene("Version2");
            UmaAssetManager.UnloadAllBundle(true);
        }

        private void SaveCharacterVMD()
        {
            foreach (var container in CharaContainerScript)
            {
                var rootbone = container.transform.Find("Position");
                if (rootbone.gameObject.TryGetComponent(out UnityHumanoidVMDRecorder recorder))
                {
                    if (recorder.IsRecording)
                    {
                        recorder.StopRecording();
                        recorder.SaveLiveVMD(live, ExitTime, $"Live{live.MusicId}_Pos{CharaContainerScript.IndexOf(container)}", Config.Instance.VmdKeyReductionLevel);
                    }
                }
            }
        }

        private void SaveMultiCameraVMD()
        {
            for (int i = 0; i < _liveTimelineControl.data.worksheetList[0].multiCameraPosKeys.Count; i++)
            {
                var frames = _liveTimelineControl.MultiRecordFrames[i];
                frames[0].FovVaild = true;
                var fov = _liveTimelineControl.data.worksheetList[0].multiCameraPosKeys[i].keys.thisList;
                fov.ForEach(k =>
                {
                    var keyframe = frames.Find(f => f.frameIndex == k.frame);
                    if (keyframe != null)
                    {
                        var index = frames.IndexOf(keyframe);
                        keyframe.FovVaild = true;
                        if (index + 1 < frames.Count) frames[index + 1].FovVaild = true;
                        if (index - 1 > 0) frames[index - 1].FovVaild = true;
                        if (index - 2 > 0) frames[index - 2].FovVaild = true;
                        if (index - 3 > 0) frames[index - 3].FovVaild = true;
                    }
                });

                UnityCameraVMDRecorder.SaveLiveCameraVMD(live, ExitTime, frames, i);
            }
        }

        private void SaveCameraVMD()
        {
            var frames = _liveTimelineControl.RecordFrames;
            frames[0].FovVaild = true;
            var fov = _liveTimelineControl.data.worksheetList[0].cameraFovKeys.thisList;
            fov.ForEach(k =>
            {

                var keyframe = frames.Find(f => f.frameIndex == k.frame);
                if (keyframe != null)
                {
                    var index = frames.IndexOf(keyframe);
                    keyframe.FovVaild = true;
                    if (index + 1 < frames.Count) frames[index + 1].FovVaild = true;
                    if (index - 1 > 0) frames[index - 1].FovVaild = true;
                    if (index - 2 > 0) frames[index - 2].FovVaild = true;
                    if (index - 3 > 0) frames[index - 3].FovVaild = true;
                }
            });

            UnityCameraVMDRecorder.SaveLiveCameraVMD(live, ExitTime, frames);
        }

        public static List<UmaDatabaseEntry> GetLiveAllVoiceEntry(int songid, List<LiveCharacterSelect> characters)
        {
            List<UmaDatabaseEntry> entryList = new List <UmaDatabaseEntry>();
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].CharaEntry.Name != "")
                {
                    var charaid = characters[i].CharaEntry.Id;

                    var entry = UmaViewerMain.Instance.AbSounds.FirstOrDefault(a => a.Name.Contains(string.Format(VOCAL_PATH, songid, charaid)) && a.Name.EndsWith("awb"));
                    if (entry == null)
                    {
                        List<UmaDatabaseEntry> entries = new List<UmaDatabaseEntry>();
                        foreach (var random in UmaViewerMain.Instance.AbSounds.Where(a => (a.Name.Contains(string.Format(RANDOM_VOCAL_PATH, songid)) && a.Name.EndsWith("awb"))))
                        {
                            entries.Add(random);
                        }
                        if (entries.Count > 0)
                        {
                            entry = entries[UnityEngine.Random.Range(0, entries.Count - 1)];
                        }
                    }

                    if (entry != null)
                    {
                        entryList.Add(entry);
                    }
                }
            }

            var bgEntry = UmaViewerMain.Instance.AbSounds.FirstOrDefault(a => a.Name.Contains(string.Format(SONG_PATH, songid)) && a.Name.EndsWith("awb"));
            if (bgEntry != null)
            {
                entryList.Add(bgEntry);
            }
            return entryList;
        }
    }

}