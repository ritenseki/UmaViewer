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
                    _liveTimelineControl.StageObjectUnitMap = _stageController.StageObjectUnitMap;
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

            // 临时诊断：确认是否有 worksheet[1..] 携带轨道数据。结论出来后连同 LiveTimelineWorksheetDiag.cs 一起删。
            LiveTimelineWorksheetDiag.Dump(_liveTimelineControl.data);

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
            _liveTimelineControl.OnUpdatePostFilm += OnPostFilmUpdate;
            PostFilmRendererFeature.ResetLayers();

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

        // BgColor1 在舞台物件上对应哪个 shader 属性尚未确认，按存在性依次尝试。
        // 首次解析每个轨道组时会打一条日志，说明命中了什么以及材质上有哪些候选属性。
        // 舞台 shader 的染色通道。运行时枚举 shader 属性表实测：
        //   Gallop/3D/Live/Stage/DefaultNoAmbient        -> _MulColor0
        //   Gallop/3D/Live/Stage/DefaultEnvMapNoAmbient  -> _MulColor0, _AddColor
        //   Gallop/3D/Live/Stage/LightBlinkBlend         -> _BlinkLightColor（BlinkLight 轨道的，不归 BgColor1）
        //   Gallop/3D/Live/Stage/StageTransmittedLightMask -> 无
        // 和 WashLight 的 MulColor0 / UVScrollLight 的 mulColor1 是同一套命名体系。
        // 刻意不含 _AmbientColor：那个通道归 BgColor2（OnBgColor2Update），两条轨道不该抢同一个属性。
        private static readonly string[] kStageBgColor1Props = { "_MulColor0" };

        /// <summary>解析结果：目标 Renderer + 它实际拥有的那个颜色属性。</summary>
        private struct StageBgColorTarget
        {
            public Renderer renderer;
            public string prop;
            public bool hasColorPower;
        }

        private readonly Dictionary<string, List<StageBgColorTarget>> _bgColor1StageCache =
            new Dictionary<string, List<StageBgColorTarget>>();

        private MaterialPropertyBlock _bgColor1Block;

        private void OnBgColor1Update(ref BgColor1UpdateInfo updateInfo)
        {
            if (!string.IsNullOrEmpty(updateInfo.TimelineName) &&
                !LiveTimelineControl.CharaBgColorNames.Contains(updateInfo.TimelineName))
            {
                ApplyBgColor1ToStage(ref updateInfo);
                return;
            }

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

        /// <summary>
        /// BgColor1 的舞台物件分支。轨道组名可能指向 GameObject，也可能指向材质名
        /// （uvScrollLightList 用的就是材质名），所以两种都试。
        /// </summary>
        private void ApplyBgColor1ToStage(ref BgColor1UpdateInfo updateInfo)
        {
            if (_stageController == null) return;

            string key = updateInfo.TimelineName;
            if (!_bgColor1StageCache.TryGetValue(key, out var targets))
            {
                targets = ResolveStageTargets(key);
                _bgColor1StageCache[key] = targets;
                LogBgColor1Resolution(key, targets);
            }
            if (targets.Count == 0) return;

            _bgColor1Block ??= new MaterialPropertyBlock();

            foreach (var t in targets)
            {
                if (t.renderer == null) continue;
                t.renderer.GetPropertyBlock(_bgColor1Block);
                _bgColor1Block.SetColor(t.prop, updateInfo.color);

                // ⚠ 不要在这里写 _ColorPower —— 缺 ground truth。
                // 已核实的只有「这些 shader 上 _ColorPower 这个 Float 属性存在」；
                // **没有任何证据表明 BgColor1 的 power 字段映射到它**，它也可能是乘在
                // 材质原值上、或者对应完全不同的东西。2026-08-05 试写过一版，无法判断对错，
                // 遂按「缺依据即不做」撤回。t.hasColorPower 保留，供将来确认后启用。
                //
                // （注：舞台地板 plane_000 / stage_object_001 / specular_002 长期偏亮发白
                //   是**早于本改动就存在**的问题，与 _ColorPower 无关，另行排查。）

                t.renderer.SetPropertyBlock(_bgColor1Block);
            }
        }

        /// <summary>
        /// 枚举 shader 真正声明的 Color 属性。
        /// 注意：材质在 bundle 里的 m_SavedProperties 会保留历史属性，和当前 shader 声明的不是一回事，
        /// 判断能写什么必须问 shader，不能看材质存档表。
        /// </summary>
        private static string DescribeShaderColors(Shader sh)
        {
            if (sh == null) return "<null shader>";

            var colors = new List<string>();
            int count = sh.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (sh.GetPropertyType(i) == ShaderPropertyType.Color)
                    colors.Add(sh.GetPropertyName(i));
            }
            return $"{sh.name} 颜色属性: {(colors.Count > 0 ? string.Join(",", colors) : "<无>")}";
        }

        /// <summary>用 sharedMaterials 探测（不会实例化材质），只保留确实有候选属性的 Renderer。</summary>
        private List<StageBgColorTarget> ResolveStageTargets(string timelineName)
        {
            var result = new List<StageBgColorTarget>();

            _bgColor1FoundObject = false;
            _bgColor1ObjCount = 0;
            _bgColor1RendererCount = 0;
            _bgColor1MatInfo.Clear();

            // 按 Transform 名遍历整个舞台层级。
            // 不能只查 StageObjectMap：它按名字去重，而观众群里同名对象有几十上百个
            // （mob_a000 实测 66 个），只取第一个会漏掉绝大多数。
            // StageObjectMap 里的对象本身也在这个层级里，所以这一趟已经覆盖它。
            var seen = new HashSet<Renderer>();
            var byName = new List<Renderer>();
            foreach (var tr in _stageController.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(tr.name.Replace("(Clone)", ""), timelineName, StringComparison.OrdinalIgnoreCase))
                    continue;
                _bgColor1FoundObject = true;
                _bgColor1ObjCount++;
                foreach (var r in tr.GetComponentsInChildren<Renderer>(true))
                    if (r != null && seen.Add(r)) byName.Add(r);
            }

            // StageObjectMap 兜底：万一有对象不在 _stageController 层级下。
            if (_stageController.StageObjectMap.TryGetValue(timelineName, out var go) && go != null)
            {
                _bgColor1FoundObject = true;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    if (r != null && seen.Add(r)) byName.Add(r);
            }

            _bgColor1RendererCount = byName.Count;
            foreach (var r in byName)
            {
                if (r == null) continue;
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null) { _bgColor1MatInfo.Add("<null mat>"); continue; }
                    string info = $"{mat.name}[{DescribeShaderColors(mat.shader)}]";
                    if (!_bgColor1MatInfo.Contains(info)) _bgColor1MatInfo.Add(info);
                }
            }
            if (byName.Count > 0)
            {
                CollectTargets(byName, result);
                if (result.Count > 0) return result;
            }

            // 退回按材质名匹配（uvScrollLightList 用的就是材质名）
            var matched = new List<Renderer>();
            foreach (var r in _stageController.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null) continue;
                    if (mat.name.IndexOf(timelineName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _bgColor1FoundObject = true;
                        matched.Add(r);
                        break;
                    }
                }
            }
            CollectTargets(matched, result);
            return result;
        }

        // ResolveStageTargets 的副产物，用于把失败原因拆干净：
        // 找到几个同名对象 / 它们下面有几个 Renderer / 这些 Renderer 用的材质和 shader。
        private bool _bgColor1FoundObject;
        private int _bgColor1ObjCount;
        private int _bgColor1RendererCount;
        private readonly List<string> _bgColor1MatInfo = new List<string>();

        private static void CollectTargets(IEnumerable<Renderer> renderers, List<StageBgColorTarget> result)
        {
            foreach (var r in renderers)
            {
                if (r == null) continue;
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null) continue;
                    string hit = null;
                    foreach (string p in kStageBgColor1Props)
                    {
                        if (mat.HasProperty(p)) { hit = p; break; }
                    }
                    if (hit != null)
                    {
                        result.Add(new StageBgColorTarget
                        {
                            renderer = r,
                            prop = hit,
                            hasColorPower = mat.HasProperty(kColorPowerProp),
                        });
                        break;
                    }
                }
            }
        }

        private void LogBgColor1Resolution(string timelineName, List<StageBgColorTarget> targets)
        {
            if (targets.Count == 0)
            {
                if (!_bgColor1FoundObject)
                    Debug.LogWarning($"[BgColor1] '{timelineName}'：整个舞台层级里找不到同名对象，材质名也匹配不上");
                else if (_bgColor1RendererCount == 0)
                    Debug.LogWarning($"[BgColor1] '{timelineName}'：找到 {_bgColor1ObjCount} 个同名对象，但它们下面没有任何 Renderer");
                else
                    Debug.LogWarning($"[BgColor1] '{timelineName}'：找到 {_bgColor1ObjCount} 个对象 / {_bgColor1RendererCount} 个 Renderer，" +
                                     $"但材质无候选属性。材质[shader]: {string.Join(" | ", _bgColor1MatInfo)}");
                return;
            }

            var props = new HashSet<string>();
            foreach (var t in targets) props.Add(t.prop);

            // 成功分支也打印 shader 真实声明的颜色属性，便于核对写对了通道。
            var shaders = new List<string>();
            foreach (var t in targets)
                foreach (var mat in t.renderer.sharedMaterials)
                {
                    if (mat == null) continue;
                    string info = DescribeShaderColors(mat.shader);
                    if (!shaders.Contains(info)) shaders.Add(info);
                }

            Debug.Log($"[BgColor1] '{timelineName}' -> {targets.Count} 个 Renderer，写入 {string.Join(",", props)}；" +
                      $"{string.Join(" | ", shaders)}");
        }

        /// <summary>
        /// PostFilm (39)。三条轨道各占一层，参数写进 PostFilmRendererFeature.Layers，
        /// 由 RendererFeature 在 AfterRenderingPostProcessing 做全屏叠加。
        /// 该 Feature 必须先加进 UMAUniversalRenderPipelineAsset_Renderer.asset 才会生效。
        /// </summary>
        private void OnPostFilmUpdate(ref PostFilmUpdateInfo info)
        {
            int i = info.layerIndex;
            if (i < 0 || i >= PostFilmRendererFeature.kLayerCount) return;

            PostFilmRendererFeature.Layers[i] = new PostFilmRendererFeature.LayerState
            {
                enable = info.enable,
                filmMode = info.filmMode,
                colorType = info.colorType,
                filmPower = info.filmPower,
                color0 = info.color0,
                color1 = info.color1,
                color2 = info.color2,
                color3 = info.color3,
                filmOffset = info.filmOffsetParam,
                filmScale = info.filmScale,
                rollAngle = info.rollAngle,
                filmOption = info.filmOptionParam,
            };

            LogPostFilmOnce(i, ref info);
        }

        private readonly HashSet<int> _postFilmLogged = new HashSet<int>();

        private void LogPostFilmOnce(int layer, ref PostFilmUpdateInfo info)
        {
            if (!_postFilmLogged.Add(layer)) return;
            Debug.Log($"[PostFilm] layer{layer} filmMode={info.filmMode} colorType={info.colorType} " +
                      $"power={info.filmPower:F3} layerMode={info.layerMode} colorBlend={info.colorBlend} " +
                      $"movieResId={info.movieResId} color0={info.color0} scale={info.filmScale} " +
                      $"offset={info.filmOffsetParam} roll={info.rollAngle:F3} option={info.filmOptionParam}");
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
            if (_stageController == null) return;
            Color c = Color.Lerp(updateInfo.color1, updateInfo.color2, updateInfo.value);
            foreach (var r in _stageController.GetComponentsInChildren<Renderer>())
                foreach (var mat in r.materials)
                    if (mat.HasProperty("_AmbientColor"))
                        mat.SetColor("_AmbientColor", c);
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
            foreach (var r in _stageController.GetComponentsInChildren<Renderer>(true))
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

            _blinkLightBlock ??= new MaterialPropertyBlock();
            int noProp = 0;

            // color0Array/powerArray 恒为 10 项，是「调色板」而不是每盏灯一项
            // （实测：renderer 数可达 570，数组仍是 10；wash_truss_a 的前 4 项正好等于本曲成员数）。
            // 每盏灯到调色板槽位的映射规则尚未逆出来，暂统一取第 0 槽 —— 对灯牌是正确的
            // （它 6 个有效槽同色），对多色组（wash/mob）是简化。
            Color baseCol = (keyData.color0Array != null && keyData.color0Array.Length > 0)
                ? keyData.color0Array[0] : Color.white;
            float basePower = (keyData.powerArray != null && keyData.powerArray.Length > 0)
                ? keyData.powerArray[0] : 1f;

            float baseElapsed = _liveTimelineControl.currentLiveTime - keyData.frame / 60f - keyData.waitTime;
            float cycle = keyData.turnOnTime + keyData.keepTime + keyData.turnOffTime + keyData.intervalTime;

            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;

                Color col = baseCol;
                float power = basePower;

                if (keyData.pattern != 0)
                {
                    // pattern != 0 时逐灯错开相位，做出滚动闪烁（U 闪 → M 闪 → A 闪）。
                    // pattern 的确切语义还没逆出来，这里只区分「同步」与「滚动」两种；
                    // 若方向或速度不对，调这里的相位公式即可。
                    float phase = (cycle > 0f && renderers.Length > 1)
                        ? cycle * i / renderers.Length
                        : 0f;
                    power *= ComputeBlinkIntensity(keyData, baseElapsed - phase);
                }

                // 这些灯的 shader 是 Gallop/3D/Live/Stage/LightBlinkBlend，唯一的 Color 属性是
                // _BlinkLightColor。之前写的是 _Color —— 该 shader 上没有这个属性，写入是空操作，
                // 于是 _BlinkLightColor 一直是默认值，灯全渲染成黑块。
                BlinkTarget t = ResolveBlinkTarget(r);
                if (t.colorProp == null) { noProp++; continue; }

                r.GetPropertyBlock(_blinkLightBlock);
                if (t.hasColorPower)
                {
                    // shader 自带独立的亮度通道，颜色和强度分开写，合成交给 shader。
                    _blinkLightBlock.SetColor(t.colorProp, col);
                    _blinkLightBlock.SetFloat(kColorPowerProp, power);
                }
                else
                {
                    // BgMirrorBall 这类只有 _MulColor0、没有 _ColorPower，只能把强度乘进颜色。
                    _blinkLightBlock.SetColor(t.colorProp, col * power);
                }
                r.SetPropertyBlock(_blinkLightBlock);
            }
            LogBlinkLightOnce(data.name, keyData, renderers, noProp);
            // TODO: 调色板槽位映射、color1Array、LightBlendMode、isReverseHueArray 尚未实现。
        }

        private MaterialPropertyBlock _blinkLightBlock;

        // 舞台灯光 shader 的通道（枚举 shader 属性表实测，见 CLAUDE.md）：
        //   LightBlinkBlend               _BlinkLightColor + _ColorPower
        //   DefaultNoAmbient              _MulColor0       + _ColorPower
        //   DefaultEnvMapNoAmbient        _MulColor0       + _ColorPower (+_AddColor)
        //   DefaultTransparentNoAmbient   _MulColor0       + _ColorPower (+_AmbientColor)
        //   BgMirrorBall                  _MulColor0       （无 _ColorPower）
        //   StageMirrorBallShine / StageTransmittedLightMask  完全没有颜色属性
        private static readonly string[] kBlinkColorProps = { "_BlinkLightColor", "_MulColor0", "_Color" };
        private const string kColorPowerProp = "_ColorPower";

        private struct BlinkTarget
        {
            public string colorProp;
            public bool hasColorPower;
        }

        private readonly Dictionary<Renderer, BlinkTarget> _blinkPropCache = new Dictionary<Renderer, BlinkTarget>();
        private readonly HashSet<string> _blinkLoggedGroups = new HashSet<string>();

        private BlinkTarget ResolveBlinkTarget(Renderer r)
        {
            if (_blinkPropCache.TryGetValue(r, out BlinkTarget cached)) return cached;

            BlinkTarget t = default;
            foreach (var mat in r.sharedMaterials)
            {
                if (mat == null) continue;
                foreach (string p in kBlinkColorProps)
                {
                    if (mat.HasProperty(p)) { t.colorProp = p; break; }
                }
                if (t.colorProp != null)
                {
                    t.hasColorPower = mat.HasProperty(kColorPowerProp);
                    break;
                }
            }
            _blinkPropCache[r] = t;
            return t;
        }

        /// <summary>每个 BlinkLight 组只打一次：pattern / 数组长度 / renderer 数 / shader 属性，用于设计逐灯相位。</summary>
        private void LogBlinkLightOnce(string groupName, LiveTimelineKeyBlinkLightData k, Renderer[] renderers, int noProp)
        {
            if (!_blinkLoggedGroups.Add(groupName)) return;

            var shaders = new List<string>();
            foreach (var r in renderers)
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null) continue;
                    string info = DescribeShaderColors(mat.shader);
                    if (!shaders.Contains(info)) shaders.Add(info);
                }

            Debug.Log($"[BlinkLight] '{groupName}' renderers={renderers.Length} 无可写颜色属性={noProp} " +
                      $"pattern={k.pattern} colorType={k.colorType} blendMode={k.LightBlendMode} " +
                      $"color0Array={k.color0Array?.Length ?? -1} color1Array={k.color1Array?.Length ?? -1} " +
                      $"powerArray={k.powerArray?.Length ?? -1} reverseHue={k.isReverseHueArray?.Length ?? -1} " +
                      $"power={k.powerMin}~{k.powerMax} loop={k.loopCount} " +
                      $"wait={k.waitTime} on={k.turnOnTime} keep={k.keepTime} off={k.turnOffTime} interval={k.intervalTime}; " +
                      $"{string.Join(" | ", shaders)}");
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