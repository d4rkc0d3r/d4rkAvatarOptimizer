using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using System.Text.RegularExpressions;

#if UNITY_EDITOR
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using UnityEngine.Rendering;
using UnityEngine.Animations;
using UnityEditor;
using UnityEditor.Animations;
using d4rkpl4y3r.AvatarOptimizer;
using d4rkpl4y3r.AvatarOptimizer.Util;
using d4rkpl4y3r.AvatarOptimizer.Extensions;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;

using Math = System.Math;
using Type = System.Type;
using Path = System.IO.Path;
using AnimationPath = System.ValueTuple<string, string, System.Type>;
using BlendableLayer = VRC.SDKBase.VRC_AnimatorLayerControl.BlendableLayer;
#endif

[HelpURL("https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/blob/main/README.md")]
public class d4rkAvatarOptimizer : MonoBehaviour
#if HAS_IEDITOR_ONLY
, VRC.SDKBase.IEditorOnly
#endif
{
    #region Settings
    [System.Serializable]
    public class Settings
    {
        public bool ApplyOnUpload = true;
        public bool WritePropertiesAsStaticValues = false;
        public bool MergeSkinnedMeshes = true;
        public int MergeSkinnedMeshesWithShaderToggle = 0;
        public int MergeSkinnedMeshesWithNaNimation = 0;
        public bool NaNimationAllow3BoneSkinning = false;
        public bool MergeSkinnedMeshesSeparatedByDefaultEnabledState = true;
        public bool MergeStaticMeshesAsSkinned = false;
        public bool MergeDifferentPropertyMaterials = false;
        public bool MergeSameDimensionTextures = false;
        public bool MergeMainTex = false;
        public bool OptimizeFXLayer = true;
        public bool CombineApproximateMotionTimeAnimations = false;
        public bool DisablePhysBonesWhenUnused = true;
        public bool MergeSameRatioBlendShapes = true;
        public bool MMDCompatibility = true;
        public bool DeleteUnusedComponents = true;
        public int DeleteUnusedGameObjects = 0;
        public bool UseRingFingerAsFootCollider = false;
        public bool ProfileTimeUsed = false;
    }

    public Settings settings = new Settings();
    public bool DoAutoSettings = true;
    public bool ShowExcludedTransforms = false;
    public List<Transform> ExcludeTransforms = new List<Transform>();
    public bool ShowMeshAndMaterialMergePreview = true;
    public bool ShowFXLayerMergeResults = true;
    private bool _ShowFXLayerMergeErrors = false;
    public bool ShowFXLayerMergeErrors { get { return _ShowFXLayerMergeErrors; } set { _ShowFXLayerMergeErrors = value; } }
    public bool ShowDebugInfo = false;
    public bool DebugShowUnparsableMaterials = true;
    public bool DebugShowUnmergableMaterials = true;
    public bool DebugShowUnmergableTextureMaterials = true;
    public bool DebugShowCrunchedTextures = true;
    public bool DebugShowNonBC5NormalMaps = true;
    public bool DebugShowMeshesThatCantMergeNaNimationCausedByAnimations = true;
    public bool DebugShowLockedInMaterials = true;
    public bool DebugShowUnlockedMaterials = true;
    public bool DebugShowPenetrators = true;
    public bool DebugShowMergeableBlendShapes = true;
    public bool DebugShowBoneWeightStats = true;
    public bool DebugShowPhysBoneDependencies = true;
    public bool DebugShowUnusedComponents = true;
    public bool DebugShowAlwaysDisabledGameObjects = true;
    public bool DebugShowMaterialSwaps = true;
    public bool DebugShowAnimatedMaterialPropertyPaths = true;
    public bool DebugShowGameObjectsWithToggle = true;
    public bool DebugShowUnmovingBones = false;
    #endregion

    public struct MaterialSlot
    {
        public Renderer renderer;
        public int index;
        public Material material
        {
            get { return renderer.sharedMaterials[index]; }
        }
        public MaterialSlot(Renderer renderer, int index)
        {
            this.renderer = renderer;
            this.index = index;
        }
        public static MaterialSlot[] GetAllSlotsFrom(Renderer renderer)
        {
            var result = new MaterialSlot[renderer.sharedMaterials.Length];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new MaterialSlot(renderer, i);
            }
            return result;
        }
#if UNITY_EDITOR
        public MeshTopology GetTopology()
        {
            return renderer.GetSharedMesh()?.GetTopology(Math.Min(index, renderer.GetSharedMesh().subMeshCount - 1)) ?? MeshTopology.Triangles;
        }
#endif
    }

#if UNITY_EDITOR

    public void Optimize()
    {
        var oldCulture = Thread.CurrentThread.CurrentCulture;
        var oldUICulture = Thread.CurrentThread.CurrentUICulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            DisplayProgressBar("Clear TrashBin Folder", 0.01f);
            ClearTrashBin();
            Profiler.StartSection("ClearCaches()");
            optimizedMaterials.Clear();
            optimizedMaterialImportPaths.Clear();
            optimizedSlotSwapMaterials.Clear();
            newAnimationPaths.Clear();
            texArrayPropertiesToSet.Clear();
            keepTransforms.Clear();
            convertedMeshRendererPaths.Clear();
            constantAnimatedValuesToAdd.Clear();
            ClearCaches();
            DisplayProgressBar("Destroying unused components", 0.2f);
            Profiler.StartNextSection("DestroyEditorOnlyGameObjects()");
            DestroyEditorOnlyGameObjects();
            Profiler.StartNextSection("DestroyUnusedComponents()");
            DestroyUnusedComponents();
            if (WritePropertiesAsStaticValues)
            {
                DisplayProgressBar("Parsing Shaders", 0.05f);
                Profiler.StartNextSection("ParseAndCacheAllShaders()");
                ShaderAnalyzer.ParseAndCacheAllShaders(FindAllUsedMaterials().Select(m => m.shader), true,
                    (done, total) => DisplayProgressBar($"Parsing Shaders ({done}/{total})", 0.05f + 0.15f * done / total));
            }
            physBonesToDisable = FindAllPhysBonesToDisable();
            Profiler.StartNextSection("ConvertStaticMeshesToSkinnedMeshes()");
            ConvertStaticMeshesToSkinnedMeshes();
            Profiler.StartNextSection("CalculateUsedBlendShapePaths()");
            CalculateUsedBlendShapePaths();
            Profiler.StartNextSection("DeleteAllUnusedSkinnedMeshRenderers()");
            DeleteAllUnusedSkinnedMeshRenderers();
            Profiler.StartNextSection("CombineSkinnedMeshes()");
            DisplayProgressBar("Combining meshes", 0.2f);
            CombineSkinnedMeshes();
            Profiler.StartNextSection("CreateTextureArrays()");
            CreateTextureArrays();
            Profiler.StartNextSection("CombineAndOptimizeMaterials()");
            DisplayProgressBar("Optimizing materials", 0.3f);
            CombineAndOptimizeMaterials();
            Profiler.StartNextSection("OptimizeMaterialSwapMaterials()");
            OptimizeMaterialSwapMaterials();
            Profiler.StartNextSection("OptimizeMaterialsOnNonSkinnedMeshes()");
            OptimizeMaterialsOnNonSkinnedMeshes();
            Profiler.StartNextSection("SaveOptimizedMaterials()");
            DisplayProgressBar("Reload optimized materials", 0.60f);
            SaveOptimizedMaterials();
            Profiler.StartNextSection("DestroyUnusedGameObjects()");
            DisplayProgressBar("Destroying unused GameObjects", 0.90f);
            DestroyUnusedGameObjects();
            Profiler.StartNextSection("FixAllAnimationPaths()");
            DisplayProgressBar("Fixing animation paths", 0.95f);
            FixAllAnimationPaths();
            Profiler.StartNextSection("MoveRingFingerColliderToFeet()");
            DisplayProgressBar("Done", 1.0f);
            MoveRingFingerColliderToFeet();
            Profiler.StartNextSection("DestroyImmediate(this)");
            DestroyImmediate(this);
            Profiler.EndSection();
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = oldCulture;
            Thread.CurrentThread.CurrentUICulture = oldUICulture;
            EditorUtility.ClearProgressBar();
        }
    }

    public static bool HasCustomShaderSupport { get => EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64; }

    #if HAS_IEDITOR_ONLY
    public bool ApplyOnUpload { get { return settings.ApplyOnUpload; } set { settings.ApplyOnUpload = value; } }
    #else
    public bool ApplyOnUpload { get { return false; } set { settings.ApplyOnUpload = false; } }
    #endif
    public bool WritePropertiesAsStaticValues {
        get { return HasCustomShaderSupport && (settings.WritePropertiesAsStaticValues || MergeSkinnedMeshesWithShaderToggle || settings.MergeDifferentPropertyMaterials); }
        set { settings.WritePropertiesAsStaticValues = value; } }
    public bool MergeSkinnedMeshes { get { return settings.MergeSkinnedMeshes; } set { settings.MergeSkinnedMeshes = value; } }
    public bool MergeSkinnedMeshesWithShaderToggle {
        get { return HasCustomShaderSupport && settings.MergeSkinnedMeshes && settings.MergeSkinnedMeshesWithShaderToggle != 0; }
        set { settings.MergeSkinnedMeshesWithShaderToggle = value ? 1 : 0; } }
    public bool MergeSkinnedMeshesWithNaNimation {
        get { return settings.MergeSkinnedMeshes && settings.MergeSkinnedMeshesWithNaNimation != 0; }
        set { settings.MergeSkinnedMeshesWithNaNimation = value ? 1 : 0; } }
    public bool NaNimationAllow3BoneSkinning {
        get { return MergeSkinnedMeshesWithNaNimation && settings.NaNimationAllow3BoneSkinning; }
        set { settings.NaNimationAllow3BoneSkinning = value; } }
    public bool MergeSkinnedMeshesSeparatedByDefaultEnabledState {
        get { return MergeSkinnedMeshesWithNaNimation && settings.MergeSkinnedMeshesSeparatedByDefaultEnabledState; }
        set { settings.MergeSkinnedMeshesSeparatedByDefaultEnabledState = value; } }
    public bool MergeStaticMeshesAsSkinned {
        get { return settings.MergeSkinnedMeshes && settings.MergeStaticMeshesAsSkinned; }
        set { settings.MergeStaticMeshesAsSkinned = value; } }
    public bool MergeDifferentPropertyMaterials {
        get { return HasCustomShaderSupport && settings.MergeDifferentPropertyMaterials; }
        set { settings.MergeDifferentPropertyMaterials = value; } }
    public bool MergeSameDimensionTextures {
        get { return settings.MergeDifferentPropertyMaterials && settings.MergeSameDimensionTextures; }
        set { settings.MergeSameDimensionTextures = value; } }
    public bool MergeMainTex {
        get { return MergeSameDimensionTextures && settings.MergeMainTex; }
        set { settings.MergeMainTex = value; } }
    public bool MMDCompatibility { get { return settings.MMDCompatibility; } set { settings.MMDCompatibility = value; } }
    public bool DeleteUnusedComponents { get { return settings.DeleteUnusedComponents; } set { settings.DeleteUnusedComponents = value; } }
    public bool DeleteUnusedGameObjects { get { return settings.DeleteUnusedGameObjects != 0; } set { settings.DeleteUnusedGameObjects = value ? 1 : 0; } }
    public bool OptimizeFXLayer { get { return settings.OptimizeFXLayer; } set { settings.OptimizeFXLayer = value; } }
    public bool CombineApproximateMotionTimeAnimations {
        get { return settings.OptimizeFXLayer && settings.CombineApproximateMotionTimeAnimations; }
        set { settings.CombineApproximateMotionTimeAnimations = value; } }
    public bool DisablePhysBonesWhenUnused { get { return settings.DisablePhysBonesWhenUnused; } set { settings.DisablePhysBonesWhenUnused = value; } }
    public bool MergeSameRatioBlendShapes { get { return settings.MergeSameRatioBlendShapes; } set { settings.MergeSameRatioBlendShapes = value; } }
    public bool UseRingFingerAsFootCollider { get { return settings.UseRingFingerAsFootCollider; } set { settings.UseRingFingerAsFootCollider = value; } }
    public bool ProfileTimeUsed { get { return settings.ProfileTimeUsed; } set { settings.ProfileTimeUsed = value; } } 

    public bool CanChangeSetting(string fieldName)
    {
        switch (fieldName)
        {
            case nameof(WritePropertiesAsStaticValues):
                return !(MergeSkinnedMeshesWithShaderToggle || settings.MergeDifferentPropertyMaterials);
            case nameof(MergeSkinnedMeshesWithShaderToggle):
            case nameof(MergeSkinnedMeshesWithNaNimation):
            case nameof(MergeStaticMeshesAsSkinned):
                return settings.MergeSkinnedMeshes;
            case nameof(NaNimationAllow3BoneSkinning):
            case nameof(MergeSkinnedMeshesSeparatedByDefaultEnabledState):
                return MergeSkinnedMeshesWithNaNimation;
            case nameof(MergeSameDimensionTextures):
                return settings.MergeDifferentPropertyMaterials;
            case nameof(MergeMainTex):
                return MergeSameDimensionTextures;
            case nameof(CombineApproximateMotionTimeAnimations):
                return settings.OptimizeFXLayer;
            #if !HAS_IEDITOR_ONLY
            case nameof(ApplyOnUpload):
                return false;
            #endif
            default:
                return true;
        }
    }

    private static Dictionary<string, string> FieldDisplayName = new Dictionary<string, string>() {
        {nameof(ApplyOnUpload), "Apply on Upload"},
        {nameof(WritePropertiesAsStaticValues), "Write Properties as Static Values"},
        {nameof(MergeSkinnedMeshes), "Merge Skinned Meshes"},
        {nameof(MergeSkinnedMeshesWithShaderToggle), "Use Shader Toggles"},
        {nameof(MergeSkinnedMeshesWithNaNimation), "NaNimation Toggles"},
        {nameof(NaNimationAllow3BoneSkinning), "Allow 3 Bone Skinning"},
        {nameof(MergeSkinnedMeshesSeparatedByDefaultEnabledState), "Keep Default Enabled State"},
        {nameof(MergeStaticMeshesAsSkinned), "Merge Static Meshes as Skinned"},
        {nameof(MergeDifferentPropertyMaterials), "Merge Different Property Materials"},
        {nameof(MergeSameDimensionTextures), "Merge Same Dimension Textures"},
        {nameof(MergeMainTex), "Merge MainTex"},
        {nameof(MMDCompatibility), "MMD Compatibility"},
        {nameof(DeleteUnusedComponents), "Delete Unused Components"},
        {nameof(DeleteUnusedGameObjects), "Delete Unused GameObjects"},
        {nameof(OptimizeFXLayer), "Optimize FX Layer"},
        {nameof(CombineApproximateMotionTimeAnimations), "Combine Motion Time Approximation"},
        {nameof(DisablePhysBonesWhenUnused), "Disable Phys Bones When Unused"},
        {nameof(MergeSameRatioBlendShapes), "Merge Same Ratio Blend Shapes"},
        {nameof(UseRingFingerAsFootCollider), "Use Ring Finger as Foot Collider"},
        {nameof(ProfileTimeUsed), "Profile Time Used"},
        {nameof(ShowFXLayerMergeErrors), "Show FX Layer Merge Errors"},
    };

    public static string GetDisplayName(string fieldName)
    {
        if (FieldDisplayName.ContainsKey(fieldName))
        {
            return FieldDisplayName[fieldName];
        }
        return fieldName;
    }

    private static List<(string name, Dictionary<string, object>)> SettingsPresets = new List<(string name, Dictionary<string, object>)>()
    {
        ("Basic", new Dictionary<string, object>() {
            {nameof(Settings.ApplyOnUpload), true},
            {nameof(Settings.WritePropertiesAsStaticValues), false},
            {nameof(Settings.MergeSkinnedMeshes), true},
            {nameof(Settings.MergeSkinnedMeshesWithShaderToggle), 0},
            {nameof(Settings.MergeSkinnedMeshesWithNaNimation), 0},
            {nameof(Settings.NaNimationAllow3BoneSkinning), false},
            {nameof(Settings.MergeSkinnedMeshesSeparatedByDefaultEnabledState), true},
            {nameof(Settings.MergeStaticMeshesAsSkinned), false},
            {nameof(Settings.MergeDifferentPropertyMaterials), false},
            {nameof(Settings.MergeSameDimensionTextures), false},
            {nameof(Settings.MergeMainTex), false},
            {nameof(Settings.OptimizeFXLayer), true},
            {nameof(Settings.CombineApproximateMotionTimeAnimations), false},
            {nameof(Settings.DisablePhysBonesWhenUnused), true},
            {nameof(Settings.MergeSameRatioBlendShapes), true},
            {nameof(Settings.MMDCompatibility), true},
            {nameof(Settings.DeleteUnusedComponents), true},
            {nameof(Settings.DeleteUnusedGameObjects), 0},
        }),
        ("Shader Toggles", new Dictionary<string, object>() {
            {nameof(Settings.ApplyOnUpload), true},
            {nameof(Settings.WritePropertiesAsStaticValues), true},
            {nameof(Settings.MergeSkinnedMeshes), true},
            {nameof(Settings.MergeSkinnedMeshesWithShaderToggle), 1},
            {nameof(Settings.MergeSkinnedMeshesWithNaNimation), 1},
            {nameof(Settings.NaNimationAllow3BoneSkinning), false},
            {nameof(Settings.MergeSkinnedMeshesSeparatedByDefaultEnabledState), true},
            {nameof(Settings.MergeStaticMeshesAsSkinned), true},
            {nameof(Settings.MergeDifferentPropertyMaterials), true},
            {nameof(Settings.MergeSameDimensionTextures), true},
            {nameof(Settings.MergeMainTex), false},
            {nameof(Settings.OptimizeFXLayer), true},
            {nameof(Settings.CombineApproximateMotionTimeAnimations), false},
            {nameof(Settings.DisablePhysBonesWhenUnused), true},
            {nameof(Settings.MergeSameRatioBlendShapes), true},
            {nameof(Settings.MMDCompatibility), true},
            {nameof(Settings.DeleteUnusedComponents), true},
            {nameof(Settings.DeleteUnusedGameObjects), 0},
        }),
        ("Full", new Dictionary<string, object>() {
            {nameof(Settings.ApplyOnUpload), true},
            {nameof(Settings.WritePropertiesAsStaticValues), true},
            {nameof(Settings.MergeSkinnedMeshes), true},
            {nameof(Settings.MergeSkinnedMeshesWithShaderToggle), 1},
            {nameof(Settings.MergeSkinnedMeshesWithNaNimation), 1},
            {nameof(Settings.NaNimationAllow3BoneSkinning), true},
            {nameof(Settings.MergeSkinnedMeshesSeparatedByDefaultEnabledState), false},
            {nameof(Settings.MergeStaticMeshesAsSkinned), true},
            {nameof(Settings.MergeDifferentPropertyMaterials), true},
            {nameof(Settings.MergeSameDimensionTextures), true},
            {nameof(Settings.MergeMainTex), true},
            {nameof(Settings.OptimizeFXLayer), true},
            {nameof(Settings.CombineApproximateMotionTimeAnimations), true},
            {nameof(Settings.DisablePhysBonesWhenUnused), true},
            {nameof(Settings.MergeSameRatioBlendShapes), true},
            {nameof(Settings.MMDCompatibility), false},
            {nameof(Settings.DeleteUnusedComponents), true},
            {nameof(Settings.DeleteUnusedGameObjects), 1},
        }),
    };

    public List<string> GetPresetNames()
    {
        return SettingsPresets.Select(x => x.name).Where(x => HasCustomShaderSupport || x != "Shader Toggles").ToList();
    }

    public bool IsPresetActive(string presetName)
    {
        var preset = SettingsPresets.Find(x => x.name == presetName).Item2;
        foreach (var entry in preset)
        {
            var field = typeof(Settings).GetField(entry.Key);
            if (typeof(bool) == field.FieldType && !field.GetValue(settings).Equals(entry.Value))
                return false;
            if (typeof(int) == field.FieldType && (int)entry.Value == 1 && (int)field.GetValue(settings) == 0)
                return false;
            if (typeof(int) == field.FieldType && (int)entry.Value == 0 && (int)field.GetValue(settings) == 1)
                return false;
        }
        return true;
    }
    public void SetPreset(string presetName)
    {
        var preset = SettingsPresets.Find(x => x.name == presetName).Item2;
        foreach (var field in preset)
        {
            typeof(Settings).GetField(field.Key).SetValue(settings, field.Value);
        }
        ApplyAutoSettings();
    }

    public static long MaxPolyCountForAutoShaderToggle = 150000;

    public void ApplyAutoSettings()
    {
        DoAutoSettings = false;
        if (settings.DeleteUnusedGameObjects == 2)
        {
            DeleteUnusedGameObjects = !UsesAnyLayerMasks();
        }
        if (settings.MergeSkinnedMeshesWithShaderToggle == 2)
        {
            MergeSkinnedMeshesWithShaderToggle = GetPolyCount() < MaxPolyCountForAutoShaderToggle;
        }
        if (settings.MergeSkinnedMeshesWithNaNimation == 2)
        {
            MergeSkinnedMeshesWithNaNimation = GetPolyCount() < MaxPolyCountForAutoShaderToggle;
        }
    }

    private static string packageRootPath = "Assets/d4rkAvatarOptimizer";
    private static string trashBinPath = "Assets/d4rkAvatarOptimizer/TrashBin/";
    private HashSet<string> usedBlendShapes = new HashSet<string>();
    private Dictionary<SkinnedMeshRenderer, List<int>> blendShapesToBake = new Dictionary<SkinnedMeshRenderer, List<int>>();
    private Dictionary<AnimationPath, AnimationPath> newAnimationPaths = new Dictionary<AnimationPath, AnimationPath>();
    private List<(Material target, List<Material> sources, ShaderOptimizer.OptimizedShader optimizerResult)> optimizedMaterials = new List<(Material, List<Material>, ShaderOptimizer.OptimizedShader)>();
    private List<string> optimizedMaterialImportPaths = new List<string>();
    private Dictionary<string, List<List<string>>> oldPathToMergedPaths = new Dictionary<string, List<List<string>>>();
    private Dictionary<string, string> oldPathToMergedPath = new Dictionary<string, string>();
    private Dictionary<string, List<string>> physBonesToDisable = new Dictionary<string, List<string>>();
    private Dictionary<(string path, int slot), HashSet<Material>> slotSwapMaterials = new Dictionary<(string, int), HashSet<Material>>();
    private Dictionary<(string path, int slot), Dictionary<Material, Material>> optimizedSlotSwapMaterials = new Dictionary<(string, int), Dictionary<Material, Material>>();
    private Dictionary<(string path, int index), (string path, int index)> materialSlotRemap = new Dictionary<(string, int), (string, int)>();
    private Dictionary<string, HashSet<string>> animatedMaterialProperties = new Dictionary<string, HashSet<string>>();
    private Dictionary<string, HashSet<string>> fusedAnimatedMaterialProperties = new Dictionary<string, HashSet<string>>();
    private List<List<Texture2D>> textureArrayLists = new List<List<Texture2D>>();
    private List<Texture2DArray> textureArrays = new List<Texture2DArray>();
    private Dictionary<Material, List<(string name, Texture2DArray array)>> texArrayPropertiesToSet = new Dictionary<Material, List<(string name, Texture2DArray array)>>();
    private HashSet<Transform> keepTransforms = new HashSet<Transform>();
    private HashSet<string> convertedMeshRendererPaths = new HashSet<string>();
    private Dictionary<Transform, Transform> movingParentMap = new Dictionary<Transform, Transform>();
    private Dictionary<string, Transform> transformFromOldPath = new Dictionary<string, Transform>();
    private Dictionary<EditorCurveBinding, float> constantAnimatedValuesToAdd = new Dictionary<EditorCurveBinding, float>();
    // blendshape names come from https://www.deviantart.com/xoriu/art/MMD-Facial-Expressions-Chart-341504917
    private static HashSet<string> MMDBlendShapes = new HashSet<string>()
    {
        "まばたき", "Blink",
        "笑い", "Smile",
        "ウィンク", "Wink",
        "ウィンク右", "Wink-a",
        "ウィンク２", "Wink-b",
        "ｳｨﾝｸ２右", "Wink-c",
        "なごみ", "Howawa",
        "はぅ", "> <",
        "びっくり", "Ha!!!",
        "じと目", "Jito-eye",
        "ｷﾘｯ", "Kiri-eye",
        "はちゅ目", "O O",
        "星目", "EyeStar",
        "はぁと", "EyeHeart",
        "瞳小", "EyeSmall",
        "瞳縦潰れ", "EyeSmall-v",
        "光下", "EyeUnderli",
        "恐ろしい子！", "EyeFunky",
        "ハイライト消", "EyeHi-off",
        "映り込み消", "EyeRef-off",
        "喜び", "Joy",
        "わぉ?!", "Wao?!",
        "なごみω", "Howawa ω",
        "悲しむ", "Wail",
        "敵意", "Hostility",
        "あ", "a",
        "い", "i",
        "う", "u",
        "え", "e",
        "お", "o",
        "あ２", "a 2",
        "ん", "n",
        "▲", "Mouse_1",
        "∧", "Mouse_2",
        "□", "□",
        "ワ", "Wa",
        "ω", "Omega",
        "ω□", "ω□",
        "にやり", "Niyari",
        "にやり２", "Niyari2",
        "にっこり", "Smile",
        "ぺろっ", "Pero",
        "てへぺろ", "Bero-tehe",
        "てへぺろ２", "Bero-tehe2",
        "口角上げ", "MouseUP",
        "口角下げ", "MouseDW",
        "口横広げ", "MouseWD",
        "歯無し上", "ToothAnon",
        "歯無し下", "ToothBnon",
        "真面目", "Serious",
        "困る", "Trouble",
        "にこり", "Smily",
        "怒り", "Get angry",
        "上", "UP",
        "下", "Down",
    };

    private static float progressBar = 0;

    private void DisplayProgressBar(string text)
    {
        var titleName = name.EndsWith("(BrokenCopy)") ? name.Substring(0, name.Length - "(BrokenCopy)".Length) : name;
        EditorUtility.DisplayProgressBar("Optimizing " + titleName, text, progressBar);
    }

    private void DisplayProgressBar(string text, float progress)
    {
        progressBar = progress;
        DisplayProgressBar(text);
    }

    public static string GetTrashBinPath()
    {
        var path = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("d4rkAvatarOptimizer")[0]);
        packageRootPath = path.Substring(0, path.LastIndexOf('/'));
        packageRootPath = packageRootPath.Substring(0, packageRootPath.LastIndexOf('/'));
        var trashBinRoot = packageRootPath;
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
        if (packageInfo?.source != UnityEditor.PackageManager.PackageSource.Embedded)
        {
            trashBinRoot = "Assets/d4rkAvatarOptimizer";
            if (!AssetDatabase.IsValidFolder("Assets/d4rkAvatarOptimizer"))
            {
                AssetDatabase.CreateFolder("Assets", "d4rkAvatarOptimizer");
            }
        }
        trashBinPath = trashBinRoot + "/TrashBin/";
        if (!AssetDatabase.IsValidFolder(trashBinRoot + "/TrashBin"))
        {
            AssetDatabase.CreateFolder(trashBinRoot, "TrashBin");
        }
        return trashBinPath;
    }

    private void ClearTrashBin()
    {
        Profiler.StartSection("ClearTrashBin()");
        var path = AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(this));
        packageRootPath = path.Substring(0, path.LastIndexOf('/'));
        packageRootPath = packageRootPath.Substring(0, packageRootPath.LastIndexOf('/'));
        var trashBinRoot = packageRootPath;
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
        if (packageInfo?.source != UnityEditor.PackageManager.PackageSource.Embedded)
        {
            trashBinRoot = "Assets/d4rkAvatarOptimizer";
            if (!AssetDatabase.IsValidFolder("Assets/d4rkAvatarOptimizer"))
            {
                AssetDatabase.CreateFolder("Assets", "d4rkAvatarOptimizer");
            }
        }
        trashBinPath = trashBinRoot + "/TrashBin/";
        AssetDatabase.DeleteAsset(trashBinRoot + "/TrashBin");
        AssetDatabase.CreateFolder(trashBinRoot, "TrashBin");
        binaryAssetBundlePath = null;
        materialAssetBundlePath = null;
        Profiler.EndSection();
    }

    private string binaryAssetBundlePath = null;
    private string materialAssetBundlePath = null;
    private void CreateUniqueAsset(Object asset, string name)
    {
        Profiler.StartSection("AssetDatabase.CreateAsset()");
        var invalids = Path.GetInvalidFileNameChars();
        var sanitizedName = string.Join("_", name.Split(invalids, System.StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        if (asset is Material)
        {
            if (materialAssetBundlePath == null)
            {
                materialAssetBundlePath = AssetDatabase.GenerateUniqueAssetPath(trashBinPath + sanitizedName);
                AssetDatabase.CreateAsset(asset, materialAssetBundlePath);
            }
            else
            {
                AssetDatabase.AddObjectToAsset(asset, materialAssetBundlePath);
            }
        }
        else
        {
            if (binaryAssetBundlePath == null)
            {
                binaryAssetBundlePath = AssetDatabase.GenerateUniqueAssetPath(trashBinPath + "BinaryAssetBundle.asset");
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<BinarySerializationSO>(), binaryAssetBundlePath);
            }
            AssetDatabase.AddObjectToAsset(asset, binaryAssetBundlePath);
        }
        Profiler.EndSection();
    }

    private static string GetTransformPathTo(Transform t, Transform root)
    {
        if (t == root)
            return "";
        string path = t.name;
        while ((t = t.parent) != root)
        {
            if (t == null)
                return null;
            path = $"{t.name}/{path}";
        }
        return path;
    }

    public string GetTransformPathToRoot(Transform t)
    {
        return GetTransformPathTo(t, transform);
    }

    public Transform GetTransformFromPath(string path)
    {
        if (path == "")
            return transform;
        string[] pathParts = path.Split('/');
        Transform t = transform;
        for (int i = 0; i < pathParts.Length; i++)
        {
            t = t.Find(pathParts[i]);
            if (t == null)
                return null;
        }
        return t;
    }

    public string GetPathToRoot(GameObject obj)
    {
        return GetTransformPathToRoot(obj.transform);
    }

    public string GetPathToRoot(Component component)
    {
        return GetTransformPathToRoot(component.transform);
    }

    public void ClearCaches()
    {
        var fields = GetType().GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        foreach (var field in fields)
        {
            if (field.Name.StartsWithSimple("cache_"))
            {
                field.SetValue(this, null);
            }
        }
    }

    public long GetPolyCount()
    {
        long polyCount = 0;
        foreach (var renderer in GetUsedComponentsInChildren<Renderer>())
        {
            if (!(renderer is SkinnedMeshRenderer || renderer is MeshRenderer))
                continue;
            var mesh = renderer.GetSharedMesh();
            if (mesh == null)
                continue;
            polyCount += Enumerable.Range(0, mesh.subMeshCount).Sum(i => mesh.GetIndexCount(i) / 3);
        }
        return polyCount;
    }

    private static bool IsMaterialReadyToCombineWithOtherMeshes(Material material)
    {
        return material == null ? false : ShaderAnalyzer.Parse(material.shader).CanMerge();
    }

    private bool IsBasicCombinableRenderer(Renderer candidate)
    {
        if (candidate.TryGetComponent(out Cloth cloth))
            return false;
        if (candidate is MeshRenderer && (candidate.gameObject.layer == 12 || !MergeStaticMeshesAsSkinned))
            return false;
        return true;
    }

    private bool IsShaderToggleCombinableRenderer(Renderer candidate)
    {
        if (!IsBasicCombinableRenderer(candidate))
            return false;
        foreach (var slot in MaterialSlot.GetAllSlotsFrom(candidate))
        {
            if (!IsMaterialReadyToCombineWithOtherMeshes(slot.material))
                return false;
            if (slotSwapMaterials.TryGetValue((GetPathToRoot(slot.renderer), slot.index), out var materials))
            {
                if (!materials.Any(material => IsMaterialReadyToCombineWithOtherMeshes(material)))
                    return false;
            }
        }
        return true;
    }

    public bool GetRendererDefaultEnabledState(Renderer r) => r.enabled && r.gameObject.activeSelf;

    private bool CanCombineRendererWithBasicMerge(List<Renderer> list, Renderer candidate, bool withNaNimation)
    {
        if (!IsBasicCombinableRenderer(candidate))
            return false;
        if (list.Any(r => !IsBasicCombinableRenderer(r)))
            return false;
        if (list.Count == 1 || list.Skip(1).All(r => RenderersHaveSameAnimationCurves(list[0], r, withNaNimation)))
            return RenderersHaveSameAnimationCurves(list[0], candidate, withNaNimation);
        return false;
    }

    private bool CanCombineRendererWith(List<Renderer> list, Renderer candidate)
    {
        if (!MergeSkinnedMeshes)
            return false;
        if (list[0].gameObject.layer != candidate.gameObject.layer)
            return false;
        bool OneOfParentsHasGameObjectToggleThatTheOthersArentChildrenOf(Transform t, string[] otherPaths)
        {
            while ((t = t.parent) != transform)
            {
                var path = GetPathToRoot(t);
                if (FindAllGameObjectTogglePaths().Contains(path) && otherPaths.All(p => !p.StartsWith(path)))
                    return true;
            }
            return false;
        }
        if (OneOfParentsHasGameObjectToggleThatTheOthersArentChildrenOf(list[0].transform, new string[] { GetPathToRoot(candidate.transform.parent) }))
            return false;
        if (OneOfParentsHasGameObjectToggleThatTheOthersArentChildrenOf(candidate.transform, list.Select(r => GetPathToRoot(r.transform.parent)).ToArray()))
            return false;
        if (MergeSkinnedMeshesSeparatedByDefaultEnabledState)
        {
            bool candidateDefaultEnabledState = GetRendererDefaultEnabledState(candidate);
            if (list.Any(r => GetRendererDefaultEnabledState(r) != candidateDefaultEnabledState))
                return false;
        }
        if (CanCombineRendererWithBasicMerge(list, candidate, true))
            return true;
        if (!MergeSkinnedMeshesWithShaderToggle)
            return false;
        if (!IsShaderToggleCombinableRenderer(candidate))
            return false;
        if (list.Any(r => !IsShaderToggleCombinableRenderer(r)))
            return false;
        return true;
    }

    private Dictionary<string, bool> cache_MeshUses4BoneSkinning = null;
    private bool MeshUses4BoneSkinning(string path)
    {
        if (cache_MeshUses4BoneSkinning == null)
            cache_MeshUses4BoneSkinning = new Dictionary<string, bool>();
        if (cache_MeshUses4BoneSkinning.TryGetValue(path, out var cachedResult))
            return cachedResult;
        var renderer = GetTransformFromPath(path)?.GetComponent<Renderer>();
        if (renderer == null)
            return cache_MeshUses4BoneSkinning[path] = false;
        var mesh = renderer.GetSharedMesh();
        if (mesh == null)
            return cache_MeshUses4BoneSkinning[path] = false;
        var boneWeights = mesh.boneWeights;
        for (int i = 0; i < boneWeights.Length; i++)
        {
            if (boneWeights[i].weight3 > 0)
                return cache_MeshUses4BoneSkinning[path] = true;
        }
        return cache_MeshUses4BoneSkinning[path] = false;
    }

    private Dictionary<string, bool> cache_CanUseNaNimationOnMesh = null;
    public bool CanUseNaNimationOnMesh(string path)
    {
        if (!MergeSkinnedMeshesWithNaNimation)
            return false;
        if (cache_CanUseNaNimationOnMesh == null)
            cache_CanUseNaNimationOnMesh = new Dictionary<string, bool>();
        if (cache_CanUseNaNimationOnMesh.TryGetValue(path, out var cachedResult))
            return cachedResult;
        if (!NaNimationAllow3BoneSkinning && MeshUses4BoneSkinning(path))
            return cache_CanUseNaNimationOnMesh[path] = false;
        var renderer = GetTransformFromPath(path)?.GetComponent<Renderer>();
        if (renderer == null)
            return cache_CanUseNaNimationOnMesh[path] = false;
        return cache_CanUseNaNimationOnMesh[path] = GetRendererDefaultEnabledState(renderer) || !FindAllPathsWhereMeshOrGameObjectHasOnlyOnOrOffAnimation().Contains(path);
    }

    private HashSet<string> cache_FindAllPathsWhereMeshOrGameObjectHasOnlyOnOrOffAnimation = null;
    public HashSet<string> FindAllPathsWhereMeshOrGameObjectHasOnlyOnOrOffAnimation()
    {
        if (cache_FindAllPathsWhereMeshOrGameObjectHasOnlyOnOrOffAnimation == null) {
            cache_FindAllPathsWhereMeshOrGameObjectHasOnlyOnOrOffAnimation = new HashSet<string>();
            var goOffPaths = new HashSet<string>();
            var goOnPaths = new HashSet<string>();
            var meshOffPaths = new HashSet<string>();
            var meshOnPaths = new HashSet<string>();
            var fxLayer = GetFXLayer();
            var uselessLayers = FindUselessFXLayers();
            var fxLayerLayers = GetFXLayerLayers();
            for (int i = 0; fxLayer != null && i < fxLayerLayers.Length; i++) {
                if (fxLayerLayers[i] == null || fxLayerLayers[i].stateMachine == null)
                    continue;
                if (OptimizeFXLayer && (uselessLayers.Contains(i) || IsMergeableFXLayer(i)))
                    continue;
                goOffPaths.Clear();
                goOnPaths.Clear();
                meshOffPaths.Clear();
                meshOnPaths.Clear();
                foreach (var state in fxLayerLayers[i].stateMachine.EnumerateAllStates()) {
                    if (state.motion == null)
                        continue;
                    foreach (var clip in state.motion.EnumerateAllClips()) {
                        foreach (var binding in AnimationUtility.GetCurveBindings(clip)) {
                            if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive") {
                                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                                foreach (var key in curve.keys) {
                                    if (key.value == 0)
                                        goOffPaths.Add(binding.path);
                                    else if (key.value == 1)
                                        goOnPaths.Add(binding.path);
                                }
                            } else if (typeof(Renderer).IsAssignableFrom(binding.type) && binding.propertyName == "m_Enabled") {
                                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                                foreach (var key in curve.keys) {
                                    if (key.value == 0)
                                        meshOffPaths.Add(binding.path);
                                    else if (key.value == 1)
                                        meshOnPaths.Add(binding.path);
                                }
                            }
                        }
                    }
                }
                cache_FindAllPathsWhereMeshOrGameObjectHasOnlyOnOrOffAnimation.UnionWith(goOffPaths.Except(goOnPaths));
                cache_FindAllPathsWhereMeshOrGameObjectHasOnlyOnOrOffAnimation.UnionWith(goOnPaths.Except(goOffPaths));
                cache_FindAllPathsWhereMeshOrGameObjectHasOnlyOnOrOffAnimation.UnionWith(meshOffPaths.Except(meshOnPaths));
                cache_FindAllPathsWhereMeshOrGameObjectHasOnlyOnOrOffAnimation.UnionWith(meshOnPaths.Except(meshOffPaths));
            }
            foreach (var path in cache_FindAllPathsWhereMeshOrGameObjectHasOnlyOnOrOffAnimation.ToList()) {
                var t = GetTransformFromPath(path);
                if (t == null || (t.GetComponent<MeshRenderer>() == null && t.GetComponent<SkinnedMeshRenderer>() == null))
                    cache_FindAllPathsWhereMeshOrGameObjectHasOnlyOnOrOffAnimation.Remove(path);
            }
        }
        return cache_FindAllPathsWhereMeshOrGameObjectHasOnlyOnOrOffAnimation;
    }

    private Dictionary<string, HashSet<AnimationClip>> cache_FindAllAnimationClipsAffectingRenderer = null;
    private bool? cache_withNaNimation = null;
    private Dictionary<(Renderer, Renderer), bool> cache_RendererHaveSameAnimationCurves = null;
    private bool RenderersHaveSameAnimationCurves(Renderer a, Renderer b, bool withNaNimation)
    {
        if (cache_withNaNimation == null || cache_withNaNimation != withNaNimation)
        {
            cache_withNaNimation = withNaNimation;
            cache_FindAllAnimationClipsAffectingRenderer = null;
            cache_RendererHaveSameAnimationCurves = null;
        }
        if (cache_RendererHaveSameAnimationCurves == null)
            cache_RendererHaveSameAnimationCurves = new Dictionary<(Renderer, Renderer), bool>();
        var aPath = GetPathToRoot(a);
        var bPath = GetPathToRoot(b);
        if (aPath.CompareTo(bPath) > 0) {
            var temp = aPath;
            aPath = bPath;
            bPath = temp;
            var temp2 = a;
            a = b;
            b = temp2;
        }
        if (cache_RendererHaveSameAnimationCurves.TryGetValue((a, b), out var result))
            return result;
        bool IsRelevantBindingForSkinnedMeshMerge(EditorCurveBinding binding) {
            if (withNaNimation && CanUseNaNimationOnMesh(binding.path)) {
                if (typeof(Renderer).IsAssignableFrom(binding.type))
                    return !binding.propertyName.StartsWithSimple("blendShape.") && binding.propertyName != "m_Enabled";
            } else {
                if (typeof(Renderer).IsAssignableFrom(binding.type))
                    return !binding.propertyName.StartsWithSimple("blendShape.");
                if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive")
                    return true;
            }
            return false;
        }
        Dictionary<string, HashSet<AnimationClip>> FindAllAnimationClipsAffectingRenderer()
        {
            if (cache_FindAllAnimationClipsAffectingRenderer != null)
                return cache_FindAllAnimationClipsAffectingRenderer;
            cache_FindAllAnimationClipsAffectingRenderer = new Dictionary<string, HashSet<AnimationClip>>();
            foreach (var clip in GetAllUsedFXLayerAnimationClips())
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (IsRelevantBindingForSkinnedMeshMerge(binding))
                    {
                        if (!cache_FindAllAnimationClipsAffectingRenderer.TryGetValue(binding.path, out var clips))
                        {
                            cache_FindAllAnimationClipsAffectingRenderer[binding.path] = clips = new HashSet<AnimationClip>();
                        }
                        clips.Add(clip);
                    }
                }
            }
            return cache_FindAllAnimationClipsAffectingRenderer;
        }
        var aHasClips = FindAllAnimationClipsAffectingRenderer().TryGetValue(aPath, out var aClips);
        var bHasClips = FindAllAnimationClipsAffectingRenderer().TryGetValue(bPath, out var bClips);
        if (aHasClips != bHasClips)
            return cache_RendererHaveSameAnimationCurves[(a, b)] = false;
        if (!aHasClips)
            return cache_RendererHaveSameAnimationCurves[(a, b)] = true;
        if (!aClips.SetEquals(bClips))
            return cache_RendererHaveSameAnimationCurves[(a, b)] = false;
        foreach (var clip in aClips)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var binding in bindings)
            {
                if (!IsRelevantBindingForSkinnedMeshMerge(binding))
                    continue;
                var otherBinding = binding;
                if (binding.path == aPath)
                {
                    otherBinding.path = bPath;
                    if (!bindings.Contains(otherBinding))
                        return cache_RendererHaveSameAnimationCurves[(a, b)] = false;
                }
                else if (binding.path == bPath)
                {
                    otherBinding.path = aPath;
                    if (!bindings.Contains(otherBinding))
                        return cache_RendererHaveSameAnimationCurves[(a, b)] = false;
                }
                else
                {
                    continue;
                }
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                var otherCurve = AnimationUtility.GetEditorCurve(clip, otherBinding);
                if (curve.keys.Length != otherCurve.keys.Length)
                    return cache_RendererHaveSameAnimationCurves[(a, b)] = false;
                for (int i = 0; i < curve.keys.Length; ++i)
                {
                    if (curve.keys[i].value != otherCurve.keys[i].value || curve.keys[i].time != otherCurve.keys[i].time)
                        return cache_RendererHaveSameAnimationCurves[(a, b)] = false;
                }
            }
        }
        return cache_RendererHaveSameAnimationCurves[(a, b)] = true;
    }

    private static List<string> ColorPropertyComponents = new List<string> { ".r", ".g", ".b", ".a" };
    private static List<string> VectorPropertyComponents = new List<string> { ".x", ".y", ".z", ".w" };
    private Dictionary<string, HashSet<AnimationClip>> cache_AllAnimationClipsAffectingRendererMaterialProperties = null;
    private Dictionary<(string a, string b), HashSet<string>> cache_FindSameAnimatedMaterialProperties = null;
    private HashSet<string> FindSameAnimatedMaterialProperties(string aPath, string bPath)
    {
        if (cache_FindSameAnimatedMaterialProperties == null)
            cache_FindSameAnimatedMaterialProperties = new Dictionary<(string a, string b), HashSet<string>>();
        if (aPath.CompareTo(bPath) > 0) {
            var temp = aPath;
            aPath = bPath;
            bPath = temp;
        }
        if (cache_FindSameAnimatedMaterialProperties.TryGetValue((aPath, bPath), out var cachedResult))
            return cachedResult;
        bool IsRelevantBinding(EditorCurveBinding binding) {
            return typeof(Renderer).IsAssignableFrom(binding.type) && binding.propertyName.StartsWithSimple("material.");
        }
        if (cache_AllAnimationClipsAffectingRendererMaterialProperties == null) {
            cache_AllAnimationClipsAffectingRendererMaterialProperties = new Dictionary<string, HashSet<AnimationClip>>();
            foreach (var clip in GetAllUsedFXLayerAnimationClips()) {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip)) {
                    if (IsRelevantBinding(binding)) {
                        if (!cache_AllAnimationClipsAffectingRendererMaterialProperties.TryGetValue(binding.path, out var clips)) {
                            cache_AllAnimationClipsAffectingRendererMaterialProperties[binding.path] = clips = new HashSet<AnimationClip>();
                        }
                        clips.Add(clip);
                    }
                }
            }
        }
        var allClips = new HashSet<AnimationClip>();
        if (cache_AllAnimationClipsAffectingRendererMaterialProperties.TryGetValue(aPath, out var aClips))
            allClips.UnionWith(aClips);
        if (cache_AllAnimationClipsAffectingRendererMaterialProperties.TryGetValue(bPath, out var bClips))
            allClips.UnionWith(bClips);
        var result = new HashSet<string>();
        foreach (var clip in allClips) {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var binding in bindings) {
                if (IsRelevantBinding(binding) && (binding.path == aPath || binding.path == bPath)) {
                    result.Add(binding.propertyName);
                }
            }
        }
        foreach (var clip in allClips) {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var binding in bindings) {
                if (IsRelevantBinding(binding) && (binding.path == aPath || binding.path == bPath) && result.Contains(binding.propertyName)) {
                    var otherBinding = binding;
                    otherBinding.path = binding.path == aPath ? bPath : aPath;
                    if (!bindings.Contains(otherBinding)) {
                        result.Remove(binding.propertyName);
                        continue;
                    }
                    var aKeys = AnimationUtility.GetEditorCurve(clip, binding).keys;
                    var bKeys = AnimationUtility.GetEditorCurve(clip, otherBinding).keys;
                    if (aKeys.Length != bKeys.Length) {
                        result.Remove(binding.propertyName);
                        continue;
                    }
                    for (int i = 0; i < aKeys.Length; ++i) {
                        if (aKeys[i].value != bKeys[i].value || aKeys[i].time != bKeys[i].time) {
                            result.Remove(binding.propertyName);
                            break;
                        }
                    }
                }
            }
        }
        var colorProperties = new HashSet<string>();
        var vectorProperties = new HashSet<string>();
        foreach (var property in result) {
            if (ColorPropertyComponents.Any(c => property.EndsWith(c)))
                colorProperties.Add(property.Substring(0, property.Length - 2));
            else if (VectorPropertyComponents.Any(c => property.EndsWith(c)))
                vectorProperties.Add(property.Substring(0, property.Length - 2));
        }
        foreach (var colorProperty in colorProperties) {
            if (ColorPropertyComponents.All(c => result.Contains(colorProperty + c))) {
                result.Add(colorProperty);
            }
            ColorPropertyComponents.ForEach(c => result.Remove(colorProperty + c));
        }
        foreach (var vectorProperty in vectorProperties) {
            if (VectorPropertyComponents.All(c => result.Contains(vectorProperty + c))) {
                result.Add(vectorProperty);
            }
            VectorPropertyComponents.ForEach(c => result.Remove(vectorProperty + c));
        }
        return cache_FindSameAnimatedMaterialProperties[(aPath, bPath)] = new HashSet<string>(result.Select(p => p.Substring("material.".Length)));
    }

    private HashSet<Transform> cache_FindAllTransformsWithScaleAnimation = null;
    private HashSet<Transform> FindAllTransformsWithScaleAnimation()
    {
        if (cache_FindAllTransformsWithScaleAnimation == null)
        {
            cache_FindAllTransformsWithScaleAnimation = new HashSet<Transform>();
            foreach (var clip in GetAllUsedAnimationClips())
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type == typeof(Transform) && binding.propertyName == "m_LocalScale.x" && GetTransformFromPath(binding.path) != null)
                    {
                        cache_FindAllTransformsWithScaleAnimation.Add(GetTransformFromPath(binding.path));
                    }
                }
            }
        }
        return cache_FindAllTransformsWithScaleAnimation;
    }

    private void AddAnimationPathChange((string path, string name, Type type) source, (string path, string name, Type type) target)
    {
        if (source == target)
            return;
        newAnimationPaths[source] = target;
        if (MergeStaticMeshesAsSkinned && source.type == typeof(SkinnedMeshRenderer))
        {
            source.type = typeof(MeshRenderer);
            newAnimationPaths[source] = target;
        }
    }

    public bool UsesAnyLayerMasks()
    {
        var avDescriptor = GetComponent<VRCAvatarDescriptor>();
        if (avDescriptor == null)
            return false;
        var playableLayers = avDescriptor.baseAnimationLayers.Union(avDescriptor.specialAnimationLayers).ToArray();
        foreach (var playableLayer in playableLayers)
        {
            var controller = playableLayer.animatorController as AnimatorController;
            if (controller == null)
                continue;
            if (controller.layers.Any(layer => layer.avatarMask != null))
                return true;
        }
        return false;
    }

    private HashSet<SkinnedMeshRenderer> FindAllUnusedSkinnedMeshRenderers()
    {
        var togglePaths = FindAllGameObjectTogglePaths();
        var unused = new HashSet<SkinnedMeshRenderer>();
        var skinnedMeshRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var exclusions = GetAllExcludedTransforms();
        foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
        {
            if (skinnedMeshRenderer.gameObject.activeSelf)
                continue;
            if (togglePaths.Contains(GetPathToRoot(skinnedMeshRenderer)))
                continue;
            if (exclusions.Contains(skinnedMeshRenderer.transform))
                continue;
            unused.Add(skinnedMeshRenderer);
        }
        return unused;
    }

    private void DeleteAllUnusedSkinnedMeshRenderers()
    {
        foreach (var skinnedMeshRenderer in FindAllUnusedSkinnedMeshRenderers())
        {
            var obj = skinnedMeshRenderer.gameObject;
            DestroyImmediate(skinnedMeshRenderer);
            if (!keepTransforms.Contains(obj.transform) && (obj.transform.childCount == 0 && obj.GetNonNullComponents().Length == 1))
                DestroyImmediate(obj);
        }
    }

    public List<List<Renderer>> FindPossibleSkinnedMeshMerges()
    {
        slotSwapMaterials = FindAllMaterialSwapMaterials();
        var renderers = GetUsedComponentsInChildren<Renderer>();
        var matchedSkinnedMeshes = new List<List<Renderer>>();
        var unmergableRenderers = new List<Renderer>();
        var exclusions = GetAllExcludedTransforms();
        var penetrators = FindAllPenetrators();
        foreach (var renderer in renderers) {
            if (renderer.sharedMaterials.Length == 0)
                continue;

            if (exclusions.Contains(renderer.transform) ||
                    !(renderer is SkinnedMeshRenderer || renderer is MeshRenderer) ||
                    renderer.GetSharedMesh() == null ||
                    penetrators.Contains(renderer)) {
                unmergableRenderers.Add(renderer);
                continue;
            }

            bool foundMatch = false;
            foreach (var subList in matchedSkinnedMeshes) {
                if (CanCombineRendererWith(subList, renderer)) {
                    subList.Add(renderer);
                    foundMatch = true;
                    break;
                }
            }
            if (!foundMatch) {
                matchedSkinnedMeshes.Add(new List<Renderer> { renderer });
            }
        }
        matchedSkinnedMeshes.AddRange(unmergableRenderers.Select(r => new List<Renderer> { r }));
        for (int i = 0; i < matchedSkinnedMeshes.Count && MergeStaticMeshesAsSkinned; i++)
        {
            var mergedMeshes = matchedSkinnedMeshes[i];
            if (mergedMeshes.Count == 1)
                continue;
            var meshRenderers = mergedMeshes.Where(r => r is MeshRenderer).Cast<MeshRenderer>().ToList();
            if (meshRenderers.Count == 0)
                continue;
            var mergedMaterialSlots = FindAllMergeAbleMaterials(mergedMeshes);
            var meshRenderersWithMergedMaterials = new HashSet<MeshRenderer>(meshRenderers.Where(renderer =>
                mergedMaterialSlots.Any(mergedSlots => mergedSlots.Any(slot => slot.renderer == renderer) && mergedSlots.Count > 1)));
            var unhelpfullyMergedMeshRenderers = meshRenderers.Where(r => !meshRenderersWithMergedMaterials.Contains(r));
            foreach (var meshRenderer in unhelpfullyMergedMeshRenderers)
            {
                if (mergedMeshes.Count == 1)
                    break;
                var newSubList = new List<Renderer> { meshRenderer };
                matchedSkinnedMeshes.Add(newSubList);
                mergedMeshes.Remove(meshRenderer);
            }
        }
        var avDescriptor = GetComponent<VRCAvatarDescriptor>();
        foreach (var subList in matchedSkinnedMeshes)
        {
            if (subList.Count == 1)
                continue;
            var obj = subList.OrderByDescending(smr => GetPathToRoot(smr) == "Body" ? 1 : 0)
                .ThenByDescending(smr => smr == avDescriptor.VisemeSkinnedMesh)
                .ThenBy(smr => GetPathToRoot(smr).Count(c => c == '/')).First();
            int index = subList.IndexOf(obj);
            var oldFirst = subList[0];
            subList[0] = subList[index];
            subList[index] = oldFirst;
        }
        matchedSkinnedMeshes = matchedSkinnedMeshes
            .OrderBy(subList => subList[0] is SkinnedMeshRenderer || subList[0] is MeshRenderer ? 0 : 1)
            .ThenByDescending(subList => subList.Count).ToList();
        return matchedSkinnedMeshes;
    }

    private HashSet<string> cache_TargetPathHasAnyMaterialSwap = null;
    private bool TargetPathHasAnyMaterialSwap(string path)
    {
        if (cache_TargetPathHasAnyMaterialSwap == null) {
            cache_TargetPathHasAnyMaterialSwap = new HashSet<string>();
            foreach (var oldPath in FindAllMaterialSwapMaterials().Keys.Select(key => key.Item1).Distinct()) {
                var newPath = oldPath;
                if (oldPathToMergedPath.TryGetValue(oldPath, out var mergedPath)) {
                    newPath = mergedPath;
                }
                if (transformFromOldPath.TryGetValue(newPath, out var t) && t != null) {
                    newPath = GetPathToRoot(t);
                }
                cache_TargetPathHasAnyMaterialSwap.Add(newPath);
            }
        }
        return cache_TargetPathHasAnyMaterialSwap.Contains(path);
    }

    private EditorCurveBinding FixAnimationBindingPath(EditorCurveBinding binding, ref bool changed)
    {
        var newBinding = binding;
        if (transformFromOldPath.TryGetValue(newBinding.path, out var transform))
        {
            if (transform != null)
            {
                var path = GetPathToRoot(transform);
                changed = changed || path != newBinding.path;
                newBinding.path = path;
                if (binding.type == typeof(MeshRenderer) && !transform.TryGetComponent(out MeshRenderer renderer))
                {
                    newBinding.type = typeof(SkinnedMeshRenderer);
                    changed = true;
                }
            }
        }
        return newBinding;
    }

    private EditorCurveBinding FixAnimationBinding(EditorCurveBinding binding, ref bool changed)
    {
        var currentPath = (binding.path, binding.propertyName, binding.type);
        var newBinding = binding;
        if (newAnimationPaths.TryGetValue(currentPath, out var modifiedPath))
        {
            newBinding.path = modifiedPath.Item1;
            newBinding.propertyName = modifiedPath.Item2;
            newBinding.type = modifiedPath.Item3;
            changed = true;
        }
        else if (typeof(Renderer).IsAssignableFrom(binding.type) && newAnimationPaths.TryGetValue((binding.path, binding.propertyName, typeof(SkinnedMeshRenderer)), out modifiedPath))
        {
            newBinding.path = modifiedPath.Item1;
            newBinding.propertyName = modifiedPath.Item2;
            newBinding.type = modifiedPath.Item3;
            changed = true;
        }
        return FixAnimationBindingPath(newBinding, ref changed);
    }

    private AnimationCurve ReplaceZeroWithNaN(AnimationCurve curve)
    {
        var newCurve = new AnimationCurve();
        for (int i = 0; i < curve.keys.Length; i++)
        {
            var key = curve.keys[i];
            if (key.value == 0)
            {
                key.value = float.NaN;
            }
            newCurve.AddKey(key);
        }
        newCurve.preWrapMode = curve.preWrapMode;
        newCurve.postWrapMode = curve.postWrapMode;
        return newCurve;
    }

    static Dictionary<char, char> otherVectorOrColorComponent = new Dictionary<char, char> {
        { 'x', 'r' }, { 'y', 'g' }, { 'z', 'b' }, { 'w', 'a' },
        { 'r', 'x' }, { 'g', 'y' }, { 'b', 'z' }, { 'a', 'w' },
    };
    Dictionary<string, HashSet<(string property, Type type)>> cache_IsAnimatableBinding = null;
    public bool IsAnimatableBinding(EditorCurveBinding binding) {
        if (cache_IsAnimatableBinding == null)
            cache_IsAnimatableBinding = new Dictionary<string, HashSet<(string property, Type type)>>();
        if (!cache_IsAnimatableBinding.TryGetValue(binding.path, out var animatableBindings)) {
            animatableBindings = new HashSet<(string property, Type type)>();
            GameObject targetObject = GetTransformFromPath(binding.path)?.gameObject;
            if (targetObject != null) {
                foreach (var animatableBinding in AnimationUtility.GetAnimatableBindings(targetObject, gameObject)) {
                    var name = animatableBinding.propertyName;
                    animatableBindings.Add((name, animatableBinding.type));
                    if (name.Length > 2 && name[name.Length - 2] == '.' && otherVectorOrColorComponent.TryGetValue(name[name.Length - 1], out var otherComponent)) {
                        // Color & Vector properties can both be animated by .xyzw or .rgba but only one of them gets returned by GetAnimatableBindings
                        animatableBindings.Add((name.Substring(0, name.Length - 1) + otherComponent, animatableBinding.type));
                    }
                }
                animatableBindings.Add(("ComponentExists", typeof(GameObject)));
                foreach (var component in targetObject.GetNonNullComponents()) {
                    animatableBindings.Add(("ComponentExists", component.GetType()));
                }
                if (targetObject.TryGetComponent(out VRCStation station)) {
                    // even if box collider doesn't exist right now, the station script will create one at runtime
                    animatableBindings.Add(("ComponentExists", typeof(BoxCollider)));
                    animatableBindings.Add(("m_IsTrigger", typeof(BoxCollider)));
                    animatableBindings.Add(("m_Enabled", typeof(BoxCollider)));
                    animatableBindings.Add(("m_Center.x", typeof(BoxCollider)));
                    animatableBindings.Add(("m_Center.y", typeof(BoxCollider)));
                    animatableBindings.Add(("m_Center.z", typeof(BoxCollider)));
                    animatableBindings.Add(("m_Size.x", typeof(BoxCollider)));
                    animatableBindings.Add(("m_Size.y", typeof(BoxCollider)));
                    animatableBindings.Add(("m_Size.z", typeof(BoxCollider)));
                }
            }
            cache_IsAnimatableBinding[binding.path] = animatableBindings;
        }
        if (GetAllExcludedTransformPaths().Contains(binding.path)) {
            return true;
        }
        if (animatableBindings.Count == 0) {
            return false;
        }
        if (binding.propertyName.StartsWithSimple("material.") && TargetPathHasAnyMaterialSwap(binding.path)) {
            return true;
        }
        // only check for the property name when the type is a Renderer as GetAnimatableBindings seems to be very unreliable
        // otherwise only check if the component exists
        return animatableBindings.Contains((typeof(Renderer).IsAssignableFrom(binding.type) ? binding.propertyName : "ComponentExists", binding.type));
    }
    
    private Dictionary<float, AnimationClip> cache_DummyAnimationClipOfLength = null;
    private AnimationClip FixAnimationClipPaths(AnimationClip clip)
    {
        if (clip.name == "d4rkAvatarOptimizer_MergedLayers_Constants")
            return clip;
        var newClip = Instantiate(clip);
        newClip.ClearCurves();
        newClip.name = clip.name;
        bool changed = false;
        float lastUsedKeyframeTime = -1;
        float lastUnusedKeyframeTime = -1;
        void SetFloatCurve(AnimationClip clipToSet, EditorCurveBinding bindingToSet, AnimationCurve curveToSet) {
            if (IsAnimatableBinding(bindingToSet)) {
                lastUsedKeyframeTime = Mathf.Max(curveToSet.length > 0 ? curveToSet.keys[curveToSet.length - 1].time : 0, lastUsedKeyframeTime);
                AnimationUtility.SetEditorCurve(clipToSet, bindingToSet, curveToSet);
            } else {
                lastUnusedKeyframeTime = Mathf.Max(curveToSet.length > 0 ? curveToSet.keys[curveToSet.length - 1].time : 0, lastUnusedKeyframeTime);
                changed = true;
            }
        }
        void SetObjectReferenceCurve(AnimationClip clipToSet, EditorCurveBinding bindingToSet, ObjectReferenceKeyframe[] curveToSet) {
            if (IsAnimatableBinding(bindingToSet)) {
                lastUsedKeyframeTime = Mathf.Max(curveToSet.Length > 0 ? curveToSet[curveToSet.Length - 1].time : 0, lastUsedKeyframeTime);
                AnimationUtility.SetObjectReferenceCurve(clipToSet, bindingToSet, curveToSet);
            } else {
                lastUnusedKeyframeTime = Mathf.Max(curveToSet.Length > 0 ? curveToSet[curveToSet.Length - 1].time : 0, lastUnusedKeyframeTime);
                changed = true;
            }
        }
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            var fixedBinding = FixAnimationBinding(binding, ref changed);
            if (fixedBinding.propertyName.StartsWithSimple("NaNimation")) {
                var shaderToggleInfo = fixedBinding.propertyName.Substring("NaNimation".Length);
                var propertyNames = new string[] { "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" };
                var NaNCurve = ReplaceZeroWithNaN(curve);
                for (int i = 0; i < propertyNames.Length; i++) {
                    fixedBinding.propertyName = propertyNames[i];
                    SetFloatCurve(newClip, fixedBinding, NaNCurve);
                }
                if (shaderToggleInfo.Length > 0) {
                    shaderToggleInfo = shaderToggleInfo.Substring(1);
                    var semicolonIndex = shaderToggleInfo.IndexOf(';');
                    fixedBinding.path = shaderToggleInfo.Substring(semicolonIndex + 1);
                    fixedBinding.propertyName = $"material._IsActiveMesh{shaderToggleInfo.Substring(0, semicolonIndex)}";
                    fixedBinding.type = typeof(SkinnedMeshRenderer);
                    SetFloatCurve(newClip, FixAnimationBindingPath(fixedBinding, ref changed), curve);
                }
            } else {
                SetFloatCurve(newClip, fixedBinding, curve);
                if (fixedBinding.propertyName.StartsWithSimple($"material.d4rkAvatarOptimizer") && MergeSkinnedMeshesWithNaNimation) {
                    var otherBinding = fixedBinding;
                    var match = Regex.Match(fixedBinding.propertyName, @"material\.d4rkAvatarOptimizer(.+)_ArrayIndex\d+(\.[a-z])?");
                    otherBinding.propertyName = $"material.{match.Groups[1].Value}{match.Groups[2].Value}";
                    SetFloatCurve(newClip, otherBinding, curve);
                }
            }
            bool addPhysBoneCurves = (binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName == "m_Enabled")
                || (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive");
            if (addPhysBoneCurves && physBonesToDisable.TryGetValue(binding.path, out var physBonePaths))
            {
                var physBoneBinding = binding;
                physBoneBinding.propertyName = "m_Enabled";
                physBoneBinding.type = typeof(VRCPhysBone);
                foreach (var physBonePath in physBonePaths)
                {
                    physBoneBinding.path = physBonePath;
                    SetFloatCurve(newClip, FixAnimationBindingPath(physBoneBinding, ref changed), curve);
                    changed = true;
                }
            }
        }
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
        {
            var curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            for (int i = 0; i < curve.Length; i++)
            {
                var oldMat = curve[i].value as Material;
                if (oldMat == null)
                    continue;
                if (!int.TryParse(binding.propertyName.Substring(binding.propertyName.LastIndexOf('[') + 1).TrimEnd(']'), out int index))
                    continue;
                if (optimizedSlotSwapMaterials.TryGetValue((binding.path, index), out var newMats))
                {
                    if (newMats.TryGetValue(oldMat, out var newMat))
                    {
                        curve[i].value = newMat;
                        changed = true;
                    }
                }
            }
            var newBinding = FixAnimationBinding(binding, ref changed);
            SetObjectReferenceCurve(newClip, newBinding, curve);
        }
        if (lastUnusedKeyframeTime > lastUsedKeyframeTime && lastUnusedKeyframeTime > -1) {
            // add dummy curve referencing nothing to make sure the clip still has the same length as before
            var dummyBinding = EditorCurveBinding.FloatCurve("ThisHopefullyDoesntExist", typeof(GameObject), "m_IsActive");
            var dummyCurve = AnimationCurve.Constant(0, lastUnusedKeyframeTime, 1);
            AnimationUtility.SetEditorCurve(newClip, dummyBinding, dummyCurve);
            changed = true;
            if (lastUsedKeyframeTime == -1) {
                if (cache_DummyAnimationClipOfLength == null) {
                    cache_DummyAnimationClipOfLength = new Dictionary<float, AnimationClip>();
                }
                if (!cache_DummyAnimationClipOfLength.TryGetValue(lastUnusedKeyframeTime, out var dummyClip)) {
                    newClip.name = $"DummyClip_{lastUnusedKeyframeTime}";
                    CreateUniqueAsset(newClip, newClip.name + ".anim");
                    cache_DummyAnimationClipOfLength[lastUnusedKeyframeTime] = dummyClip = newClip;
                }
                return dummyClip;
            }
        }
        if (changed)
        {
            CreateUniqueAsset(newClip, newClip.name + ".anim");
            return newClip;
        }
        return clip;
    }

    private Motion FixMotion(Motion motion, Dictionary<Motion, Motion> fixedMotions, string assetPath)
    {
        if (motion == null)
            return null;
        if (fixedMotions.TryGetValue(motion, out var fixedMotionValue))
            return fixedMotionValue;
        if (motion is BlendTree oldTree)
        {
            var newTree = new BlendTree();
            newTree.name = oldTree.name;
            newTree.blendType = oldTree.blendType;
            newTree.blendParameter = oldTree.blendParameter;
            newTree.blendParameterY = oldTree.blendParameterY;
            newTree.minThreshold = oldTree.minThreshold;
            newTree.maxThreshold = oldTree.maxThreshold;
            newTree.useAutomaticThresholds = oldTree.useAutomaticThresholds;
            var childNodes = oldTree.children;
            for (int j = 0; j < childNodes.Length; j++)
            {
                childNodes[j].motion = FixMotion(childNodes[j].motion, fixedMotions, assetPath);
            }
            newTree.children = childNodes;
            fixedMotions[motion] = newTree;
            newTree.hideFlags = HideFlags.HideInHierarchy;
            AnimatorOptimizer.CopyNormalizedBlendValuesProperty(oldTree, newTree);
            Profiler.StartSection("AssetDatabase.AddObjectToAsset()");
            AssetDatabase.AddObjectToAsset(newTree, assetPath);
            Profiler.EndSection();
            return newTree;
        }
        return motion;
    }
    
    private void FixAllAnimationPaths()
    {
        var avDescriptor = GetComponent<VRCAvatarDescriptor>();
        if (avDescriptor == null)
            return;
        
        var layerCopyPaths = new string[avDescriptor.baseAnimationLayers.Length];
        var optimizedControllers = new AnimatorController[avDescriptor.baseAnimationLayers.Length];
        for (int i = 0; i < avDescriptor.baseAnimationLayers.Length; i++)
        {
            var layer = avDescriptor.baseAnimationLayers[i].animatorController as AnimatorController;
            if (layer == null)
                continue;
        }

        var fxLayersToMerge = new List<int>();
        var fxLayersToDestroy = new List<int>();
        var fxLayerMap = new Dictionary<int, int>();
        if (OptimizeFXLayer && GetFXLayer() != null)
        {
            var nonErrors = new HashSet<string>() {"toggle", "motion time", "blend tree", "multi toggle"};
            var errors = AnalyzeFXLayerMergeAbility();
            var uselessLayers = FindUselessFXLayers();
            int currentLayer = 0;
            for (int i = 0; i < GetFXLayerLayers().Length; i++)
            {
                fxLayerMap[i] = currentLayer;
                if (uselessLayers.Contains(i))
                {
                    fxLayersToDestroy.Add(i);
                    continue;
                }
                if (errors[i].All(e => nonErrors.Contains(e)))
                {
                    fxLayersToMerge.Add(i);
                    continue;
                }
                currentLayer++;
            }
            if (fxLayersToMerge.Count < 2 && fxLayersToDestroy.Count == 0)
            {
                fxLayersToMerge.Clear();
                fxLayerMap.Clear();
            }
        }

        Profiler.StartSection("AnimatorOptimizer.Run()");
        for (int i = 0; i < avDescriptor.baseAnimationLayers.Length; i++)
        {
            var controller = avDescriptor.baseAnimationLayers[i].animatorController as AnimatorController;
            if (controller == null)
                continue;
            layerCopyPaths[i] = $"{trashBinPath}BaseAnimationLayer{i}{controller.name}(OptimizedCopy).controller";
            optimizedControllers[i] = controller == GetFXLayer()
                ? AnimatorOptimizer.Run(controller, layerCopyPaths[i], fxLayerMap, fxLayersToMerge, fxLayersToDestroy, constantAnimatedValuesToAdd.Select(kvp => (kvp.Key, kvp.Value)).ToList())
                : AnimatorOptimizer.Copy(controller, layerCopyPaths[i], fxLayerMap);
            optimizedControllers[i].name = $"BaseAnimationLayer{i}{controller.name}(OptimizedCopy)";
        }
        Profiler.EndSection();

        var animations = new HashSet<AnimationClip>();
        for (int i = 0; i < optimizedControllers.Length; i++)
        {
            if (optimizedControllers[i] == null)
                continue;
            animations.UnionWith(optimizedControllers[i].animationClips);
        }

        var fixedMotions = new Dictionary<Motion, Motion>();
        foreach (var clip in animations)
        {
            fixedMotions[clip] = FixAnimationClipPaths(clip);
        }
        
        for (int i = 0; i < optimizedControllers.Length; i++)
        {
            var newLayer = optimizedControllers[i];
            if (newLayer == null)
                continue;

            foreach (var state in newLayer.EnumerateAllStates())
            {
                state.motion = FixMotion(state.motion, fixedMotions, layerCopyPaths[i]);
            }

            if (DeleteUnusedGameObjects) {
                var playAudioType = Type.GetType("VRC.SDKBase.VRC_AnimatorPlayAudio, VRCSDKBase");
                if (playAudioType != null) {
                    var pathField = playAudioType.GetField("SourcePath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                    foreach (var behaviour in newLayer.layers.SelectMany(layer => layer.stateMachine.EnumerateAllBehaviours())) {
                        if (playAudioType.IsAssignableFrom(behaviour.GetType())) {
                            var path = (string)pathField.GetValue(behaviour) ?? "";
                            if (transformFromOldPath.TryGetValue(path, out var transform) && transform != null) {
                                pathField.SetValue(behaviour, GetPathToRoot(transform));
                            }
                        }
                    }
                }
            }

            avDescriptor.baseAnimationLayers[i].animatorController = newLayer;
        }
        Profiler.StartSection("AssetDatabase.SaveAssets()");
        AssetDatabase.SaveAssets();
        Profiler.EndSection();
    }

    private HashSet<(string path, Type type)> GetAllCurveBindings(AnimatorStateMachine stateMachine)
    {
        var result = new HashSet<(string, Type)>();
        if (stateMachine == null)
            return result;
        foreach (var state in stateMachine.EnumerateAllStates())
        {
            if (state.motion == null)
                continue;
            foreach (var clip in state.motion.EnumerateAllClips())
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);
                foreach (var binding in bindings)
                {
                    result.Add(($"{binding.path}.{binding.propertyName}", binding.type));
                }
                bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                foreach (var binding in bindings)
                {
                    result.Add(($"{binding.path}.{binding.propertyName}", binding.type));
                }
            }
        }
        return result;
    }

    private List<List<string>> cache_AnalyzeFXLayerMergeAbility;
    public List<List<string>> AnalyzeFXLayerMergeAbility()
    {
        if (cache_AnalyzeFXLayerMergeAbility != null)
            return cache_AnalyzeFXLayerMergeAbility;
        var fxLayer = GetFXLayer();
        if (fxLayer == null)
            return new List<List<string>>();
        var avDescriptor = GetComponent<VRCAvatarDescriptor>();

        var fxLayerLayers = GetFXLayerLayers();
        var errorMessages = fxLayerLayers.Select(layer => new List<string>()).ToList();

        for (int i = 0; i < avDescriptor.baseAnimationLayers.Length; i++)
        {
            var controller = avDescriptor.baseAnimationLayers[i].animatorController as AnimatorController;
            if (controller == null)
                continue;
            var controllerLayers = controller.layers;
            for (int j = 0; j < controllerLayers.Length; j++)
            {
                var layer = controllerLayers[j];
                var stateMachine = layer.stateMachine;
                if (stateMachine == null)
                    continue;
                foreach (var behaviour in stateMachine.EnumerateAllBehaviours())
                {
                    if (behaviour is VRCAnimatorLayerControl layerControl)
                    {
                        if (layerControl.layer <= errorMessages.Count && layerControl.playable == BlendableLayer.FX)
                        {
                            var playableName = new string[] { "Base", "Additive", "Gesture", "Action", "FX" }[i];
                            errorMessages[layerControl.layer].Add($"layer control from {playableName} {j} {layer.name}");
                        }
                    }
                }
            }
        }

        var fxLayerBindings = fxLayerLayers.Select(layer => GetAllCurveBindings(layer.stateMachine)).ToList();
        var uselessLayers = FindUselessFXLayers();
        var fxLayerParameters = fxLayer.parameters;
        var intParams = new HashSet<string>(fxLayerParameters.Where(p => p.type == AnimatorControllerParameterType.Int).Select(p => p.name));
        var intParamsWithNotEqualConditions = new HashSet<string>();

        for (int i = 0; i < fxLayerLayers.Length; i++) {
            if (uselessLayers.Contains(i)) {
                continue;
            }
            foreach (var condition in fxLayerLayers[i].stateMachine.EnumerateAllTransitions().SelectMany(t => t.conditions)) {
                if (condition.mode == AnimatorConditionMode.NotEqual && intParams.Contains(condition.parameter)) {
                    intParamsWithNotEqualConditions.Add(condition.parameter);
                }
            }
        }

        for (int i = 0; i < fxLayerLayers.Length; i++)
        {
            if (uselessLayers.Contains(i))
            {
                errorMessages[i].Add("useless");
                continue;
            }
            if (i <= 2 && MMDCompatibility)
            {
                errorMessages[i].Add("MMD compatibility requires the first 3 layers to be kept as is");
                continue;
            }
            var layer = fxLayerLayers[i];
            var stateMachine = layer.stateMachine;
            if (stateMachine == null)
            {
                errorMessages[i].Add("no state machine");
                continue;
            }
            if (stateMachine.stateMachines.Length != 0)
            {
                errorMessages[i].Add($"{stateMachine.stateMachines.Length} sub state machines");
                continue;
            }
            if (stateMachine.EnumerateAllBehaviours().Any())
            {
                errorMessages[i].Add($"has state machine behaviours");
                continue;
            }
            if (layer.defaultWeight != 1)
            {
                errorMessages[i].Add($"default weight {layer.defaultWeight}");
            }
            var states = stateMachine.states;
            if (states.Length == 0)
            {
                errorMessages[i].Add($"{states.Length} states");
                continue;
            }

            bool IsStateConvertableToAnimationOrBlendTree(AnimatorState state) {
                if (state.motion == null) {
                    errorMessages[i].Add($"{state.name} has no motion");
                    return false;
                }
                if (state.motion is AnimationClip clip) {
                    if (state.timeParameterActive) {
                        if (!CombineApproximateMotionTimeAnimations) {
                            errorMessages[i].Add($"{state.name} has motion time and motion time combination is disabled");
                            return false;
                        }
                    } else {
                        // check if all curves are constant
                        var bindings = AnimationUtility.GetCurveBindings(clip);
                        foreach (var binding in bindings) {
                            var curve = AnimationUtility.GetEditorCurve(clip, binding);
                            if (curve.keys.Length <= 1)
                                continue;
                            var firstValue = curve.keys[0].value;
                            for (int j = 1; j < curve.keys.Length; j++) {
                                if (curve.keys[j].value != firstValue) {
                                    errorMessages[i].Add($"{state.name} has non-constant curve for {binding.propertyName}");
                                    return false;
                                }
                            }
                        }
                    }
                    if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Any()) {
                        errorMessages[i].Add($"{state.name} has object reference curves");
                        return false;
                    }
                    return true;
                }
                if (state.motion is BlendTree blendTree) {
                    if (state.timeParameterActive) {
                        errorMessages[i].Add($"{state.name} is blend tree and has motion time");
                        return false;
                    }
                    if (!state.writeDefaultValues && blendTree.blendType == BlendTreeType.Direct) {
                        errorMessages[i].Add($"{state.name} is direct blend tree and does not have write defaults");
                        return false;
                    }
                    return true;
                }
                errorMessages[i].Add($"{state.name} is not null, animation clip or blend tree ???");
                return false;
            }

            HashSet<EditorCurveBinding> GetAllMotionBindings(AnimatorState state) {
                return new HashSet<EditorCurveBinding>(state.motion.EnumerateAllClips().SelectMany(c => AnimationUtility.GetCurveBindings(c)));
            }

            if (states.Length == 1) {
                var state = states[0].state;
                if (!IsStateConvertableToAnimationOrBlendTree(state))
                    continue;
                errorMessages[i].Add(state.motion is AnimationClip ? $"motion time" : $"blend tree");
            }

            var param = states.SelectMany(s => s.state.transitions).Concat(stateMachine.anyStateTransitions).SelectMany(t => t.conditions).Select(c => c.parameter).Distinct().ToList();
            var paramLookup = param.ToDictionary(p => p, p => fxLayerParameters.FirstOrDefault(p2 => p2.name == p));
            if (paramLookup.Values.Any(p => p == null)) {
                errorMessages[i].Add($"didn't find parameter {paramLookup.First(p => p.Value == null).Key}");
                continue;
            }

            if (states.Length == 2) {
                int singleTransitionStateIndex = System.Array.FindIndex(states, s => s.state.transitions.Length == 1);
                if (singleTransitionStateIndex == -1) {
                    errorMessages[i].Add($"no single transition state");
                    continue;
                }
                var orState = states[singleTransitionStateIndex].state;
                var andState = states[1 - singleTransitionStateIndex].state;
                if (orState.transitions[0].conditions.Length != andState.transitions.Length) {
                    errorMessages[i].Add($"or state has {orState.transitions[0].conditions.Length} conditions but and state has {andState.transitions.Length} transitions");
                    continue;
                }
                if (andState.transitions.Length == 0) {
                    errorMessages[i].Add($"and state has no transitions");
                    continue;
                }
                if (andState.transitions.Any(t => t.conditions.Length != 1)) {
                    errorMessages[i].Add($"a and state transition has multiple conditions");
                    continue;
                }
                bool conditionError = false;
                foreach (var condition in orState.transitions[0].conditions) {
                    if (condition.mode == AnimatorConditionMode.Equals || condition.mode == AnimatorConditionMode.NotEqual) {
                        errorMessages[i].Add($"a transition condition mode is {condition.mode}");
                        conditionError = true;
                        break;
                    }
                    if (intParamsWithNotEqualConditions.Contains(condition.parameter)) {
                        errorMessages[i].Add($"parameter {condition.parameter} has a not equal condition somewhere");
                        conditionError = true;
                        break;
                    }
                    var inverseConditionMode = condition.mode == AnimatorConditionMode.If ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If;
                    if (condition.mode == AnimatorConditionMode.Greater)
                        inverseConditionMode = AnimatorConditionMode.Less;
                    if (condition.mode == AnimatorConditionMode.Less)
                        inverseConditionMode = AnimatorConditionMode.Greater;
                    if ((condition.mode == AnimatorConditionMode.If || condition.mode == AnimatorConditionMode.IfNot)
                        && !andState.transitions.Any(t => t.conditions.Any(c => c.parameter == condition.parameter && c.mode == inverseConditionMode))) {
                        errorMessages[i].Add($"condition with parameter {condition.parameter} has no inverse transition");
                        conditionError = true;
                        break;
                    }
                    if (paramLookup[condition.parameter].type == AnimatorControllerParameterType.Float) {
                        errorMessages[i].Add($"parameter {condition.parameter} is float");
                        conditionError = true;
                        break;
                    }
                    bool isInt = paramLookup[condition.parameter].type == AnimatorControllerParameterType.Int;
                    float inverseThreshold = condition.threshold + (isInt ? (condition.mode == AnimatorConditionMode.Greater ? 1 : -1) : 0);
                    if ((condition.mode == AnimatorConditionMode.Greater || condition.mode == AnimatorConditionMode.Less)
                        && !andState.transitions.Any(t => t.conditions.Any(c => c.parameter == condition.parameter && c.mode == inverseConditionMode && c.threshold == inverseThreshold))) {
                        errorMessages[i].Add($"condition with parameter {condition.parameter} has no inverse transition");
                        conditionError = true;
                        break;
                    }
                }
                if (conditionError) {
                    continue;
                }

                bool reliesOnWriteDefaults = false;
                for (int j = 0; j < 2; j++) {
                    var state = states[j].state;
                    var otherState = states[1 - j].state;
                    if (state.transitions.Any(t => t.destinationState != otherState)) {
                        errorMessages[i].Add($"{state} transition destination state is not the other state");
                        break;
                    }
                    if (state.transitions.Any(t => t.hasExitTime && t.exitTime != 0.0f)) {
                        errorMessages[i].Add($"{state} transition has exit time");
                        break;
                    }
                    if (AnimatorOptimizer.IsNullOrEmpty(state.motion)) {
                        if (AnimatorOptimizer.IsNullOrEmpty(otherState.motion)) {
                            errorMessages[i].Add($"both states have no motion or are empty clips");
                            break;
                        } else if (!(otherState.motion is AnimationClip)) {
                            errorMessages[i].Add($"state {j} has no motion or an empty clip but {1 - j} has non animation clip motion");
                            break;
                        } else if (!otherState.writeDefaultValues || !state.writeDefaultValues) {
                            errorMessages[i].Add($"state {j} has no motion or an empty clip but states do not have write defaults");
                            break;
                        } else {
                            reliesOnWriteDefaults = true;
                        }
                        continue;
                    } else if (!IsStateConvertableToAnimationOrBlendTree(state)) {
                        break;
                    }
                }
                bool onlyBoolBindings = true;
                foreach (var binding in fxLayerBindings[i]) {
                    if (!binding.path.EndsWith("m_Enabled") && !binding.path.EndsWith("m_IsActive")) {
                        onlyBoolBindings = false;
                        break;
                    }
                }
                if (!reliesOnWriteDefaults) {
                    var bindings0 = GetAllMotionBindings(states[0].state);
                    var bindings1 = GetAllMotionBindings(states[1].state);
                    if (!bindings1.SetEquals(bindings0)) {
                        bindings1.Except(bindings0).Concat(bindings0.Except(bindings1)).ToList()
                            .ForEach(b => errorMessages[i].Add($"{b.path}/{b.propertyName} is not animated in both states"));
                        continue;
                    }
                }
                if (reliesOnWriteDefaults && !onlyBoolBindings) {
                    errorMessages[i].Add($"relies on write defaults and animates something other than m_Enabled/m_IsActive");
                }
                if (states.Any(s => s.state.transitions.Any(t => t.duration != 0.0f)) && !onlyBoolBindings) {
                    errorMessages[i].Add($"transition has non 0 duration and animates something other than m_Enabled/m_IsActive");
                }
                errorMessages[i].Add($"toggle");
            }

            if (states.Length > 2) {
                if (paramLookup.Count != 1 || (paramLookup.Count == 1 && paramLookup.First().Value.type != AnimatorControllerParameterType.Int)) {
                    errorMessages[i].Add($"more than 2 states and not a single int parameter");
                    continue;
                }
                if (states.Any(s => s.state.transitions.Length != 0)) {
                    errorMessages[i].Add($"more than 2 states and a state has transitions");
                    continue;
                }
                if (intParamsWithNotEqualConditions.Contains(paramLookup.First().Key)) {
                    errorMessages[i].Add($"parameter {paramLookup.First().Key} has a not equal condition somewhere");
                    continue;
                }
                if (stateMachine.anyStateTransitions.Length != states.Length) {
                    errorMessages[i].Add($"any state transitions length is not the same as states length");
                    continue;
                }
                if (stateMachine.anyStateTransitions.Any(t => t.conditions.Length != 1)) {
                    errorMessages[i].Add($"a transition has multiple conditions");
                    continue;
                }
                if (stateMachine.anyStateTransitions.Any(t => (t.hasExitTime && t.exitTime != 0.0f) || t.duration != 0.0f)) {
                    errorMessages[i].Add($"a transition has non 0 exit time or duration");
                    continue;
                }
                var thresholdsNeeded = Enumerable.Range(0, states.Length).Select(number => (float)number).ToList();
                if (thresholdsNeeded.Any(t => !stateMachine.anyStateTransitions.Any(tr => tr.conditions[0].threshold == t && tr.conditions[0].mode == AnimatorConditionMode.Equals))) {
                    errorMessages[i].Add($"missing some transition with correct threshold and condition mode");
                    continue;
                }
                if (states.Any(s => s.state.motion == null)) {
                    errorMessages[i].Add($"a state has no motion");
                    continue;
                }
                if (states.Any(s => !IsStateConvertableToAnimationOrBlendTree(s.state))) {
                    continue;
                }
                var firstBindings = GetAllMotionBindings(states[0].state);
                bool hadError = false;
                for (int j = 1; j < states.Length; j++) {
                    var bindings = GetAllMotionBindings(states[j].state);
                    if (!bindings.SetEquals(firstBindings)) {
                        bindings.Except(firstBindings).Concat(firstBindings.Except(bindings)).ToList()
                            .ForEach(b => errorMessages[i].Add($"{b.path}/{b.propertyName} is not animated in all states"));
                        hadError = true;
                        break;
                    }
                }
                if (hadError) {
                    continue;
                }
                errorMessages[i].Add($"multi toggle");
            }

            for (int j = 0; j < fxLayerLayers.Length; j++) {
                if (i == j || uselessLayers.Contains(j))
                    continue;
                foreach (var binding in fxLayerBindings[i]) {
                    if (fxLayerBindings[j].Contains(binding)) {
                        errorMessages[i].Add($"{binding.path} is used in {j} {fxLayerLayers[j].name}");
                    }
                }
            }
        }
        for (int i = 0; i < errorMessages.Count; i++)
        {
            errorMessages[i] = errorMessages[i].Distinct().ToList();
        }
        return cache_AnalyzeFXLayerMergeAbility = errorMessages;
    }

    public bool IsMergeableFXLayer(int layerIndex)
    {
        var errors = AnalyzeFXLayerMergeAbility();
        var nonErrors = new HashSet<string>() {"toggle", "motion time", "useless", "blend tree", "multi toggle"};
        return layerIndex < errors.Count && errors[layerIndex].Count == 1 && nonErrors.Contains(errors[layerIndex][0]);
    }

    private HashSet<int> cache_FindUselessFXLayers = null;
    public HashSet<int> FindUselessFXLayers()
    {
        if (cache_FindUselessFXLayers != null)
            return cache_FindUselessFXLayers;
        var fxLayer = GetFXLayer();
        if (fxLayer == null || !OptimizeFXLayer)
            return new HashSet<int>();
        Profiler.StartSection("FindUselessFXLayers()");
        var avDescriptor = GetComponent<VRCAvatarDescriptor>();

        var isAffectedByLayerWeightControl = new HashSet<int>();

        for (int i = 0; i < avDescriptor.baseAnimationLayers.Length; i++)
        {
            var controller = avDescriptor.baseAnimationLayers[i].animatorController as AnimatorController;
            if (controller == null)
                continue;
            var controllerLayers = controller.layers;
            for (int j = 0; j < controllerLayers.Length; j++)
            {
                var stateMachine = controllerLayers[j].stateMachine;
                if (stateMachine == null)
                    continue;
                foreach (var behaviour in stateMachine.EnumerateAllBehaviours())
                {
                    if (behaviour is VRCAnimatorLayerControl layerControl && layerControl.playable == BlendableLayer.FX)
                    {
                        isAffectedByLayerWeightControl.Add(layerControl.layer);
                    }
                }
            }
        }

        var uselessLayers = new HashSet<int>();

        var possibleBindingTypes = new Dictionary<string, HashSet<string>>();
        bool IsPossibleBinding(EditorCurveBinding binding)
        {
            if (!possibleBindingTypes.TryGetValue(binding.path, out var possibleTypeNames))
            {
                possibleTypeNames = new HashSet<string>();
                var transform = GetTransformFromPath(binding.path);
                if (transform != null)
                {
                    // AnimationUtility.GetAnimatableBindings(transform.gameObject, gameObject)
                    // is too slow, so we just check if the components mentioned in the bindings exist at that path
                    possibleTypeNames.UnionWith(transform.GetNonNullComponents().Select(c => c.GetType().FullName));
                    possibleTypeNames.Add(typeof(GameObject).FullName);
                }
                possibleBindingTypes[binding.path] = possibleTypeNames;
            }
            return possibleTypeNames.Contains(binding.type.FullName);
        }

        var fxLayerLayers = GetFXLayerLayers();
        int lastNonUselessLayer = fxLayerLayers.Length;
        for (int i = fxLayerLayers.Length - 1; i >= 0; i--)
        {
            if (i <= 2 && MMDCompatibility)
                break;
            var layer = fxLayerLayers[i];
            bool isNotFirstLayerOrLastNonUselessLayerCanBeFirst = i != 0 ||
                (lastNonUselessLayer < fxLayerLayers.Length && fxLayerLayers[lastNonUselessLayer].avatarMask == layer.avatarMask
                    && fxLayerLayers[lastNonUselessLayer].defaultWeight == 1 && !isAffectedByLayerWeightControl.Contains(lastNonUselessLayer));
            var stateMachine = layer.stateMachine;
            if (stateMachine == null || (stateMachine.stateMachines.Length == 0 && stateMachine.states.Length == 0))
            {
                if (isNotFirstLayerOrLastNonUselessLayerCanBeFirst)
                {
                    uselessLayers.Add(i);
                }
                continue;
            }
            if (i == 0 || stateMachine.EnumerateAllBehaviours().Any())
            {
                lastNonUselessLayer = i;
                continue;
            }
            if (layer.defaultWeight == 0 && !isAffectedByLayerWeightControl.Contains(i))
            {
                uselessLayers.Add(i);
                continue;
            }
            var layerBindings = stateMachine.EnumerateAllStates()
                .SelectMany(s => s.motion == null ? new AnimationClip[0] : s.motion.EnumerateAllClips()).Distinct()
                .SelectMany(c => AnimationUtility.GetCurveBindings(c).Concat(AnimationUtility.GetObjectReferenceCurveBindings(c)));
            if (layerBindings.All(b => !IsPossibleBinding(b)))
            {
                uselessLayers.Add(i);
                continue;
            }
            lastNonUselessLayer = i;
        }
        Profiler.EndSection();
        return cache_FindUselessFXLayers = uselessLayers;
    }

    private HashSet<AnimationClip> cache_GetAllUsedFXLayerAnimationClips = null;
    private HashSet<AnimationClip> GetAllUsedFXLayerAnimationClips()
    {
        if (cache_GetAllUsedFXLayerAnimationClips != null)
            return cache_GetAllUsedFXLayerAnimationClips;
        var fxLayer = GetFXLayer();
        if (fxLayer == null)
            return new HashSet<AnimationClip>();
        var unusedLayers = FindUselessFXLayers();
        var usedClips = new HashSet<AnimationClip>();
        var fxLayerLayers = GetFXLayerLayers();
        for (int i = 0; i < fxLayerLayers.Length; i++)
        {
            var stateMachine = fxLayerLayers[i].stateMachine;
            if (stateMachine == null || unusedLayers.Contains(i))
                continue;
            foreach (var state in stateMachine.EnumerateAllStates())
            {
                if (state.motion == null)
                    continue;
                usedClips.UnionWith(state.motion.EnumerateAllClips());
            }
        }
        return cache_GetAllUsedFXLayerAnimationClips = usedClips;
    }

    private HashSet<AnimationClip> cache_GetAllUsedAnimationClips = null;
    private HashSet<AnimationClip> GetAllUsedAnimationClips()
    {
        if (cache_GetAllUsedAnimationClips != null)
            return cache_GetAllUsedAnimationClips;
        var usedClips = new HashSet<AnimationClip>();
        var avDescriptor = GetComponent<VRCAvatarDescriptor>();
        if (avDescriptor == null)
            return usedClips;
        var fxLayer = GetFXLayer();
        foreach (var layer in avDescriptor.baseAnimationLayers)
        {
            var controller = layer.animatorController as AnimatorController;
            if (controller == null || controller == fxLayer)
                continue;
            usedClips.UnionWith(controller.animationClips);
        }
        usedClips.UnionWith(GetAllUsedFXLayerAnimationClips());
        return cache_GetAllUsedAnimationClips = usedClips;
    }

    private HashSet<EditorCurveBinding> cache_GetAllUsedFXLayerCurveBindings = null;
    private HashSet<EditorCurveBinding> GetAllUsedFXLayerCurveBindings()
    {
        if (cache_GetAllUsedFXLayerCurveBindings != null)
            return cache_GetAllUsedFXLayerCurveBindings;
        var result = new HashSet<EditorCurveBinding>();
        foreach (var clip in GetAllUsedFXLayerAnimationClips())
        {
            result.UnionWith(AnimationUtility.GetCurveBindings(clip));
            result.UnionWith(AnimationUtility.GetObjectReferenceCurveBindings(clip));
        }
        return cache_GetAllUsedFXLayerCurveBindings = result;
    }

    private Dictionary<VRCPhysBoneBase, HashSet<Object>> cache_FindAllPhysBoneDependencies = null;
    public Dictionary<VRCPhysBoneBase, HashSet<Object>> FindAllPhysBoneDependencies()
    {
        if (cache_FindAllPhysBoneDependencies != null)
            return cache_FindAllPhysBoneDependencies;
        var result = new Dictionary<VRCPhysBoneBase, HashSet<Object>>();
        var physBonePath = new Dictionary<string, VRCPhysBoneBase>();
        var physBones = GetComponentsInChildren<VRCPhysBoneBase>(true);
        foreach (var physBone in physBones)
        {
            result.Add(physBone, new HashSet<Object>());
            physBonePath[GetPathToRoot(physBone)] = physBone;
        }
        var parameterSuffixes = new string[] { "_IsGrabbed", "_IsPosed", "_Angle", "_Stretch", "_Squish" };
        foreach (var controller in GetComponent<VRCAvatarDescriptor>().baseAnimationLayers.Select(l => l.animatorController as AnimatorController).Where(c => c != null))
        {
            var parameterNames = new HashSet<string>(controller.parameters.Select(p => p.name));
            foreach (var physBone in physBones)
            {
                if (parameterSuffixes.Any(s => parameterNames.Contains(physBone.parameter + s)))
                {
                    result[physBone].Add(controller);
                }
            }
        }
        foreach (var clip in GetAllUsedFXLayerAnimationClips())
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.propertyName == "m_Enabled" && typeof(VRCPhysBoneBase).IsAssignableFrom(binding.type) && physBonePath.TryGetValue(binding.path, out var physBone))
                {
                    result[physBone].Add(clip);
                }
            }
        }
        var transformToDependency = new Dictionary<Transform, HashSet<Object>>();
        void AddDependency(Transform t, Object obj)
        {
            if (t == null)
                return;
            if (!transformToDependency.TryGetValue(t, out var dependencies))
            {
                transformToDependency[t] = dependencies = new HashSet<Object>();
            }
            dependencies.Add(obj);
        }
        foreach (var skinnedMesh in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skinnedMesh.bones.Length == 0)
            {
                var root = skinnedMesh.rootBone != null ? skinnedMesh.rootBone : skinnedMesh.transform;
                AddDependency(skinnedMesh.rootBone, skinnedMesh);
                continue;
            }
            if (skinnedMesh.sharedMesh == null)
                continue;
            var meshBones = skinnedMesh.bones;
            var usedBoneIDs = new bool[meshBones.Length];
            var boneWeights = skinnedMesh.sharedMesh.boneWeights;
            for (int i = 0; i < boneWeights.Length; i++)
            {
                usedBoneIDs[boneWeights[i].boneIndex0] = true;
                if (boneWeights[i].weight1 > 0) {
                    usedBoneIDs[boneWeights[i].boneIndex1] = true;
                    if (boneWeights[i].weight2 > 0) {
                        usedBoneIDs[boneWeights[i].boneIndex2] = true;
                        if (boneWeights[i].weight3 > 0) {
                            usedBoneIDs[boneWeights[i].boneIndex3] = true;
                        }
                    }
                }
            }
            for (int i = 0; i < usedBoneIDs.Length; i++)
            {
                if (usedBoneIDs[i])
                {
                    AddDependency(meshBones[i], skinnedMesh);
                }
            }
        }
        foreach (var behavior in GetComponentsInChildren<Behaviour>(true)
            .Where(b => b != null && (b.GetType().Name.Contains("Constraint") || b.GetType().FullName.StartsWithSimple("RootMotion.FinalIK"))))
        {
            foreach (var t in FindReferencedTransforms(behavior))
            {
                AddDependency(t, behavior);
            }
        }
        foreach (var skinnedRenderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            AddDependency(skinnedRenderer.rootBone, skinnedRenderer);
        }
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            AddDependency(renderer.probeAnchor, renderer);
            AddDependency(renderer.transform, renderer);
        }
        foreach (var contact in GetComponentsInChildren<ContactBase>(true))
        {
            AddDependency(contact.GetRootTransform(), contact);
        }
        foreach (var physBone in physBones)
        {
            var root = physBone.GetRootTransform();
            foreach (Transform current in root.GetAllDescendants().Concat(new Transform[] { root }))
            {
                if (transformToDependency.TryGetValue(current, out var dependencies))
                {
                    result[physBone].UnionWith(dependencies);
                }
                result[physBone].UnionWith(current.GetNonNullComponents().Where(c => c != physBone && !(c is Transform)));
            }
        }

        return cache_FindAllPhysBoneDependencies = result;
    }

    public Dictionary<string, List<string>> FindAllPhysBonesToDisable()
    {
        var result = new Dictionary<string, List<string>>();
        if (!DisablePhysBonesWhenUnused)
            return result;
        var physBoneDependencies = FindAllPhysBoneDependencies();
        foreach(var dependencies in physBoneDependencies.Values)
        {
            dependencies.RemoveWhere(o => o == null);
        }
        foreach(var entry in physBoneDependencies)
        {
            if (entry.Key != null && entry.Value.Count(o => !(o is AnimatorController)) == 1 && entry.Value.First(o => !(o is AnimatorController)) is SkinnedMeshRenderer target)
            {
                var targetPath = GetPathToRoot(target);
                if (!result.TryGetValue(targetPath, out var physBones))
                {
                    result[targetPath] = physBones = new List<string>();
                }
                physBones.Add(GetPathToRoot(entry.Key));
            }
        }
        return result;
    }

    private Dictionary<(string path, int index), HashSet<Material>> cache_FindAllMaterialSwapMaterials;
    public Dictionary<(string path, int index), HashSet<Material>> FindAllMaterialSwapMaterials()
    {
        if (cache_FindAllMaterialSwapMaterials != null)
            return cache_FindAllMaterialSwapMaterials;
        var result = new Dictionary<(string path, int index), HashSet<Material>>();
        var fxLayer = GetFXLayer();
        if (fxLayer == null)
            return result;
        foreach (var clip in GetAllUsedFXLayerAnimationClips())
        {
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (!typeof(Renderer).IsAssignableFrom(binding.type)
                    || !binding.propertyName.StartsWithSimple("m_Materials.Array.data["))
                    continue;
                int start = binding.propertyName.IndexOf('[') + 1;
                int end = binding.propertyName.IndexOf(']') - start;
                int slot = int.Parse(binding.propertyName.Substring(start, end));
                var index = (binding.path, slot);
                var curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                var curveMaterials = curve.Select(c => c.value as Material).Where(m => m != null).Distinct().ToList();
                if (!result.TryGetValue(index, out var materials))
                {
                    result[index] = materials = new HashSet<Material>();
                }
                materials.UnionWith(curveMaterials);
            }
        }
        return cache_FindAllMaterialSwapMaterials = result;
    }

    private void OptimizeMaterialSwapMaterials()
    {
        var exclusions = GetAllExcludedTransforms();
        foreach (var entry in slotSwapMaterials)
        {
            var current = GetTransformFromPath(entry.Key.path);
            if (exclusions.Contains(current))
                continue;
            int mergedMeshCount = 1;
            int meshIndex = 0;
            string targetPath = entry.Key.path;
            if (oldPathToMergedPaths.TryGetValue(entry.Key.path, out var currentMergedMeshes))
            {
                mergedMeshCount = currentMergedMeshes.Count;
                meshIndex = currentMergedMeshes.FindIndex(list => list.Contains(entry.Key.path));
                targetPath = currentMergedMeshes[0][0];
            }
            if (!optimizedSlotSwapMaterials.TryGetValue(entry.Key, out var optimizedMaterials))
            {
                optimizedSlotSwapMaterials[entry.Key] = optimizedMaterials = new Dictionary<Material, Material>();
            }
            foreach (var material in entry.Value)
            {
                if (!optimizedMaterials.TryGetValue(material, out var optimizedMaterial))
                {
                    DisplayProgressBar("Optimizing swap material: " + material.name);
                    var matWrapper = new List<List<Material>>() { new List<Material>() { material } };
                    var sourcePathWrapper = new List<List<string>>() { Enumerable.Repeat(entry.Key.path, mergedMeshCount).ToList() };
                    var mergedMeshIndexWrapper = new List<List<int>>() { new List<int>() { meshIndex } };
                    optimizedMaterials[material] = CreateOptimizedMaterials(matWrapper, mergedMeshCount, targetPath, sourcePathWrapper, mergedMeshIndexWrapper)[0];
                }
            }
        }
    }

    public bool IsHumanoid()
    {
        var rootAnimator = GetComponent<Animator>();
        return rootAnimator != null && rootAnimator.avatar != null && rootAnimator.avatar.isHuman;
    }

    public AnimatorController GetFXLayer()
    {
        var avDescriptor = GetComponent<VRCAvatarDescriptor>();
        var baseLayerCount = IsHumanoid() ? 5 : 3;
        if (avDescriptor == null || avDescriptor.baseAnimationLayers.Length != baseLayerCount)
            return null;
        return avDescriptor.baseAnimationLayers[baseLayerCount - 1].animatorController as AnimatorController;
    }

    private AnimatorControllerLayer[] cache_GetFXLayerLayers = null;
    public AnimatorControllerLayer[] GetFXLayerLayers()
    {
        if (cache_GetFXLayerLayers != null)
            return cache_GetFXLayerLayers;
        var fxLayer = GetFXLayer();
        return cache_GetFXLayerLayers = fxLayer != null ? fxLayer.layers : new AnimatorControllerLayer[0];
    }

    public void CalculateUsedBlendShapePaths()
    {
        usedBlendShapes.Clear();
        blendShapesToBake.Clear();
        var avDescriptor = GetComponent<VRCAvatarDescriptor>();
        if (avDescriptor != null)
        {
            if (avDescriptor.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape
                && avDescriptor.VisemeSkinnedMesh != null)
            {
                var meshRenderer = avDescriptor.VisemeSkinnedMesh;
                string path = GetPathToRoot(meshRenderer) + "/blendShape.";
                foreach (var blendShapeName in avDescriptor.VisemeBlendShapes)
                {
                    usedBlendShapes.Add(path + blendShapeName);
                }
            }
            if (avDescriptor.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.JawFlapBlendShape
                && avDescriptor.VisemeSkinnedMesh != null)
            {
                var meshRenderer = avDescriptor.VisemeSkinnedMesh;
                string path = GetPathToRoot(meshRenderer) + "/blendShape.";
                usedBlendShapes.Add(path + avDescriptor.MouthOpenBlendShapeName);
            }
            if (avDescriptor.customEyeLookSettings.eyelidType == VRCAvatarDescriptor.EyelidType.Blendshapes
                && avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh != null
                && avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh.sharedMesh != null)
            {
                var meshRenderer = avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh;
                string path = GetPathToRoot(meshRenderer) + "/blendShape.";
                foreach (var blendShapeID in avDescriptor.customEyeLookSettings.eyelidsBlendshapes)
                {
                    if (blendShapeID >= 0 && blendShapeID < meshRenderer.sharedMesh.blendShapeCount)
                    {
                        usedBlendShapes.Add(path + meshRenderer.sharedMesh.GetBlendShapeName(blendShapeID));
                    }
                }
            }
            foreach (var clip in GetAllUsedFXLayerAnimationClips())
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(SkinnedMeshRenderer)
                        || !binding.propertyName.StartsWithSimple("blendShape."))
                        continue;
                    var t = GetTransformFromPath(binding.path);
                    if (t == null)
                        continue;
                    var smr = t.GetComponent<SkinnedMeshRenderer>();
                    if (smr == null)
                        continue;
                    var mesh = smr.sharedMesh;
                    if (mesh == null)
                        continue;
                    var blendShapeName = binding.propertyName.Substring("blendShape.".Length);
                    var blendShapeID = mesh.GetBlendShapeIndex(blendShapeName);
                    if (blendShapeID < 0)
                        continue;
                    var keyframes = AnimationUtility.GetEditorCurve(clip, binding).keys;
                    if (keyframes.All(k => k.value == 0) && smr.GetBlendShapeWeight(blendShapeID) == 0)
                        continue;
                    usedBlendShapes.Add(binding.path + "/" + binding.propertyName);
                }
            }
        }
        foreach (var skinnedMeshRenderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = skinnedMeshRenderer.sharedMesh;
            if (mesh == null)
                continue;
            var blendShapeIDsToBake = new List<int>();
            blendShapesToBake[skinnedMeshRenderer] = blendShapeIDsToBake;
            string path = GetPathToRoot(skinnedMeshRenderer) + "/blendShape.";
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                var name = mesh.GetBlendShapeName(i);
                if (MMDCompatibility && MMDBlendShapes.Contains(name))
                {
                    usedBlendShapes.Add(path + name);
                    continue;
                }
                if (skinnedMeshRenderer.GetBlendShapeWeight(i) != 0 && !usedBlendShapes.Contains(path + name))
                {
                    if (mesh.GetBlendShapeFrameCount(i) > 1)
                    {
                        usedBlendShapes.Add(path + name);
                    }
                    else
                    {
                        blendShapeIDsToBake.Add(i);
                    }
                }
            }
        }
    }

    public HashSet<string> GetUsedBlendShapePaths()
    {
        return new HashSet<string>(usedBlendShapes);
    }

    public List<List<(string blendshape, float value)>> FindMergeableBlendShapes(IEnumerable<Renderer> mergedMeshBlob)
    {
        var avDescriptor = GetComponent<VRCAvatarDescriptor>();
        var fxLayer = GetFXLayer();
        if (avDescriptor == null || fxLayer == null)
            return new List<List<(string blendshape, float value)>>();
        var exclusions = GetAllExcludedTransforms();
        var validPaths = new HashSet<string>();
        var blendShapeNameToID = new Dictionary<string, int>();
        var blendShapeIDToName = new List<string>();
        int GetBlendShapeID(string name)
        {
            if (blendShapeNameToID.TryGetValue(name, out var id))
                return id;
            id = blendShapeIDToName.Count;
            blendShapeNameToID[name] = id;
            blendShapeIDToName.Add(name);
            return id;
        }
        var ratiosDict = new List<Dictionary<int, float>>() { new Dictionary<int, float>() };
        foreach (var renderer in mergedMeshBlob)
        {
            var skinnedMeshRenderer = renderer as SkinnedMeshRenderer;
            var mesh = skinnedMeshRenderer?.sharedMesh;
            if (mesh == null || exclusions.Contains(skinnedMeshRenderer.transform))
                continue;
            string path = GetPathToRoot(skinnedMeshRenderer) + "/blendShape.";
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                var name = mesh.GetBlendShapeName(i);
                if (MMDCompatibility && MMDBlendShapes.Contains(name))
                    continue;
                if (mesh.GetBlendShapeFrameCount(i) == 1)
                {
                    validPaths.Add(path + name);
                    ratiosDict[0][GetBlendShapeID(path + name)] = skinnedMeshRenderer.GetBlendShapeWeight(i);
                }
            }
        }
        if (validPaths.Count == 0)
            return new List<List<(string blendshape, float value)>>();
        if (avDescriptor.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape
            && avDescriptor.VisemeSkinnedMesh != null)
        {
            var meshRenderer = avDescriptor.VisemeSkinnedMesh;
            string path = GetPathToRoot(meshRenderer) + "/blendShape.";
            foreach (var blendShapeName in avDescriptor.VisemeBlendShapes)
            {
                validPaths.Remove(path + blendShapeName);
            }
        }
        if (avDescriptor.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.JawFlapBlendShape
            && avDescriptor.VisemeSkinnedMesh != null)
        {
            var meshRenderer = avDescriptor.VisemeSkinnedMesh;
            string path = GetPathToRoot(meshRenderer) + "/blendShape.";
            validPaths.Remove(path + avDescriptor.MouthOpenBlendShapeName);
        }
        if (avDescriptor.customEyeLookSettings.eyelidType == VRCAvatarDescriptor.EyelidType.Blendshapes
            && avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh != null
            && avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh.sharedMesh != null)
        {
            var meshRenderer = avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh;
            string path = GetPathToRoot(meshRenderer) + "/blendShape.";
            foreach (var blendShapeID in avDescriptor.customEyeLookSettings.eyelidsBlendshapes)
            {
                if (blendShapeID >= 0 && blendShapeID < meshRenderer.sharedMesh.blendShapeCount)
                {
                    validPaths.Remove(path + meshRenderer.sharedMesh.GetBlendShapeName(blendShapeID));
                }
            }
        }
        var mergeableBlendShapes = new List<List<(int blendshapeID, float value)>>();
        var hasEntryInMergeableBlendShapes = new HashSet<int>();
        foreach (var clip in GetAllUsedFXLayerAnimationClips())
        {
            var blendShapes = new List<(int blendShapeID, EditorCurveBinding binding)>();
            var keyframes = new HashSet<float>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(SkinnedMeshRenderer)
                    || !binding.propertyName.StartsWithSimple("blendShape."))
                    continue;
                var path = $"{binding.path}/{binding.propertyName}";
                if (!validPaths.Contains(path))
                    continue;
                blendShapes.Add((GetBlendShapeID(path), binding));
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                keyframes.UnionWith(curve.keys.Select(x => x.time));
            }
            foreach (var key in keyframes)
            {
                var blendShapeValues = new Dictionary<int, float>();
                foreach (var blendShape in blendShapes)
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, blendShape.binding);
                    blendShapeValues[blendShape.blendShapeID] = curve.Evaluate(key);
                }
                NormalizeBlendShapeValues(blendShapeValues);
                if (!ratiosDict.Any(list => list.SequenceEqual(blendShapeValues)))
                    ratiosDict.Add(blendShapeValues);
            }
            foreach (var blendshape in blendShapes)
            {
                if (!hasEntryInMergeableBlendShapes.Contains(blendshape.blendShapeID))
                {
                    hasEntryInMergeableBlendShapes.Add(blendshape.blendShapeID);
                    mergeableBlendShapes.Add(new List<(int blendshapeID, float value)>() { (blendshape.blendShapeID, 1) });
                }
            }
        }
        var ratiosArray = ratiosDict.Select(x => {
            var array = Enumerable.Repeat(float.NegativeInfinity, blendShapeIDToName.Count).ToArray();
            foreach (var entry in x) {
                array[entry.Key] = entry.Value;
            }
            return array;
        }).ToArray();
        for (int i = 0; i < mergeableBlendShapes.Count - 1; i++)
        {
            for (int j = i + 1; j < mergeableBlendShapes.Count; j++)
            {
                var subList = mergeableBlendShapes[i];
                var candidate = mergeableBlendShapes[j][0].blendshapeID;
                float value = -1;
                bool canAddToRatio = true;
                for (int k = 0; k < ratiosArray.Length; k++) {
                    if (!TryAddBlendShapeToSubList(subList, candidate, ref value, ratiosArray[k])) {
                        canAddToRatio = false;
                        break;
                    }
                }
                if (canAddToRatio && value != -1) {
                    subList.Add((candidate, value));
                    NormalizeBlendShapeValues(subList);
                    mergeableBlendShapes.RemoveAt(j);
                    j--;
                }
            }
        }
        mergeableBlendShapes.RemoveAll(x => x.Count == 1);
        return mergeableBlendShapes.Select(x => x.OrderByDescending(y => y.value).Select(z => (blendShapeIDToName[z.blendshapeID], z.value)).ToList()).ToList();
    }

    private void NormalizeBlendShapeValues(List<(int blendshape, float value)> blendShapeValues)
    {
        var maxValue = blendShapeValues.Max(x => x.value);
        if (maxValue == 0 || maxValue == 1)
            return;
        for (int i = 0; i < blendShapeValues.Count; i++)
        {
            blendShapeValues[i] = (blendShapeValues[i].blendshape, blendShapeValues[i].value / maxValue);
        }
    }

    private void NormalizeBlendShapeValues(Dictionary<int, float> blendShapeValues)
    {
        var maxValue = blendShapeValues.Max(x => x.Value);
        if (maxValue == 0 || maxValue == 1)
            return;
        foreach (var key in blendShapeValues.Keys.ToList())
        {
            blendShapeValues[key] /= maxValue;
        }
    }

    private bool TryAddBlendShapeToSubList(List<(int blendshapeID, float value)> subList, int blendshapeID, ref float value, float[] ratioToCheckAgainst)
    {
        int intersectionCount = 0;
        float intersectionMax = 0;
        int subListCount = subList.Count;
        for (int i = 0; i < subListCount; i++)
        {
            float ratioValue = ratioToCheckAgainst[subList[i].blendshapeID];
            if (ratioValue != float.NegativeInfinity)
            {
                intersectionCount++;
                intersectionMax = Mathf.Max(intersectionMax, ratioValue);
            }
            else if (intersectionCount > 0)
            {
                return false;
            }
        }
        float candidateValue = ratioToCheckAgainst[blendshapeID];
        bool hasCandidate = candidateValue != float.NegativeInfinity;
        if (intersectionCount == 0 && !hasCandidate)
            return true;
        if (intersectionCount != subListCount || !hasCandidate)
            return false;
        if (intersectionMax == 0)
            return candidateValue == 0;
        if (candidateValue == 0)
            return false;
        for (int i = 0; i < subListCount; i++)
        {
            var match = ratioToCheckAgainst[subList[i].blendshapeID];
            if (Mathf.Abs(subList[i].value - match / intersectionMax) > 0.01f)
                return false;
        }
        if (value < 0)
            value = candidateValue / intersectionMax;
        else if (Mathf.Abs(value - candidateValue / intersectionMax) > 0.01f)
            return false;
        return true;
    }

    private Dictionary<string, HashSet<string>> cache_FindAllAnimatedMaterialProperties;
    public Dictionary<string, HashSet<string>> FindAllAnimatedMaterialProperties() {
        if (cache_FindAllAnimatedMaterialProperties != null)
            return cache_FindAllAnimatedMaterialProperties;
        var map = new Dictionary<string, HashSet<string>>();
        var fxLayer = GetFXLayer();
        if (fxLayer == null)
            return map;
        foreach (var binding in GetAllUsedFXLayerCurveBindings()) {
            if (!binding.propertyName.StartsWithSimple("material.") || !typeof(Renderer).IsAssignableFrom(binding.type))
                continue;
            if (!map.TryGetValue(binding.path, out var props)) {
                map[binding.path] = (props = new HashSet<string>());
            }
            var propName = binding.propertyName.Substring(9);
            if (!Regex.IsMatch(propName, @"^[#_a-zA-Z][#_a-zA-Z0-9]*(\.[rgbaxyzw])?$"))
                continue;
            if (propName.Length > 2 && propName[propName.Length - 2] == '.') {
                props.Add(propName.Substring(0, propName.Length - 2));
            }
            props.Add(propName);
        }
        return cache_FindAllAnimatedMaterialProperties = map;
    }

    private HashSet<string> cache_FindAllGameObjectTogglePaths = null;
    public HashSet<string> FindAllGameObjectTogglePaths()
    {
        if (cache_FindAllGameObjectTogglePaths != null)
            return cache_FindAllGameObjectTogglePaths;
        var togglePaths = new HashSet<string>();
        var fxLayer = GetFXLayer();
        if (fxLayer == null)
            return togglePaths;
        foreach (var binding in GetAllUsedFXLayerCurveBindings())
        {
            if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive")
                togglePaths.Add(binding.path);
        }
        return cache_FindAllGameObjectTogglePaths = togglePaths;
    }

    private HashSet<string> cache_FindAllRendererTogglePaths = null;
    public HashSet<string> FindAllRendererTogglePaths()
    {
        if (cache_FindAllRendererTogglePaths != null)
            return cache_FindAllRendererTogglePaths;
        var togglePaths = new HashSet<string>();
        foreach (var binding in GetAllUsedFXLayerCurveBindings())
        {
            if (typeof(Renderer).IsAssignableFrom(binding.type) && binding.propertyName == "m_Enabled")
                togglePaths.Add(binding.path);
        }
        togglePaths.UnionWith(FindAllGameObjectTogglePaths());
        return cache_FindAllRendererTogglePaths = togglePaths;
    }

    private HashSet<Transform> cache_FindAllAlwaysDisabledGameObjects = null;
    public HashSet<Transform> FindAllAlwaysDisabledGameObjects()
    {
        if (cache_FindAllAlwaysDisabledGameObjects != null)
            return cache_FindAllAlwaysDisabledGameObjects;
        var togglePaths = FindAllGameObjectTogglePaths();
        var disabledGameObjects = new HashSet<Transform>();
        var queue = new Queue<Transform>();
        var exclusions = GetAllExcludedTransforms();
        queue.Enqueue(transform);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (exclusions.Contains(current))
                continue;
            if (current != transform && !current.gameObject.activeSelf && !togglePaths.Contains(GetPathToRoot(current)))
            {
                disabledGameObjects.Add(current);
                foreach (var child in current.GetAllDescendants())
                {
                    disabledGameObjects.Add(child);
                }
            }
            else
            {
                foreach (Transform child in current)
                {
                    queue.Enqueue(child);
                }
            }
        }
        return cache_FindAllAlwaysDisabledGameObjects = disabledGameObjects;
    }

    private HashSet<Component> cache_FindAllUnusedComponents = null;
    public HashSet<Component> FindAllUnusedComponents()
    {
        if (cache_FindAllUnusedComponents != null)
            return cache_FindAllUnusedComponents;
        var fxLayer = GetFXLayer();
        if (fxLayer == null)
            return new HashSet<Component>();
        var behaviourToggles = new HashSet<string>();
        foreach (var binding in GetAllUsedFXLayerCurveBindings()) {
            if (typeof(Behaviour).IsAssignableFrom(binding.type) && binding.propertyName == "m_Enabled") {
                behaviourToggles.Add(binding.path);
            } else if (typeof(Renderer).IsAssignableFrom(binding.type) && binding.propertyName == "m_Enabled") {
                behaviourToggles.Add(binding.path);
            }
        }

        var alwaysDisabledBehaviours = new HashSet<Component>(GetComponentsInChildren<Behaviour>(true)
            .Where(b => b != null && !b.enabled)
            .Where(b => !(b is VRCPhysBoneColliderBase))
            .Where(b => !behaviourToggles.Contains(GetPathToRoot(b))));

        alwaysDisabledBehaviours.UnionWith(GetComponentsInChildren<Renderer>(true)
            .Where(r => r != null && !r.enabled && !(r is ParticleSystemRenderer))
            .Where(r => !behaviourToggles.Contains(GetPathToRoot(r))));

        alwaysDisabledBehaviours.UnionWith(FindAllAlwaysDisabledGameObjects()
            .SelectMany(t => t.GetNonNullComponents().Where(c => !(c is Transform))));
        
        var exclusions = GetAllExcludedTransforms();

        foreach(var entry in FindAllPhysBoneDependencies())
        {
            if (exclusions.Contains(entry.Key.transform))
                continue;
            var dependencies = entry.Value.Select(d => d as Component).Where(d => d != null);
            if (!entry.Value.Any(d => d is AnimatorController) && dependencies.All(d => alwaysDisabledBehaviours.Contains(d)))
            {
                alwaysDisabledBehaviours.Add(entry.Key);
            }
        }

        var usedPhysBoneColliders = GetComponentsInChildren<VRCPhysBoneBase>(true)
            .Where(pb => !alwaysDisabledBehaviours.Contains(pb) || exclusions.Contains(pb.transform))
            .SelectMany(pb => pb.colliders);

        alwaysDisabledBehaviours.UnionWith(GetComponentsInChildren<VRCPhysBoneColliderBase>(true)
            .Where(c => !usedPhysBoneColliders.Contains(c)));

        alwaysDisabledBehaviours.RemoveWhere(c => exclusions.Contains(c.transform) || c.transform == transform);

        return cache_FindAllUnusedComponents = alwaysDisabledBehaviours;
    }

    private HashSet<Transform> cache_FindAllMovingTransforms = null;
    private HashSet<Transform> FindAllMovingTransforms()
    {
        if (cache_FindAllMovingTransforms != null)
            return cache_FindAllMovingTransforms;
        var avDescriptor = GetComponent<VRCAvatarDescriptor>();
        if (avDescriptor == null)
            return new HashSet<Transform>();
        var transforms = new HashSet<Transform>();

        if (avDescriptor.enableEyeLook)
        {
            transforms.Add(avDescriptor.customEyeLookSettings.leftEye);
            transforms.Add(avDescriptor.customEyeLookSettings.rightEye);
        }
        if (avDescriptor.customEyeLookSettings.eyelidType == VRCAvatarDescriptor.EyelidType.Bones)
        {
            transforms.Add(avDescriptor.customEyeLookSettings.lowerLeftEyelid);
            transforms.Add(avDescriptor.customEyeLookSettings.lowerRightEyelid);
            transforms.Add(avDescriptor.customEyeLookSettings.upperLeftEyelid);
            transforms.Add(avDescriptor.customEyeLookSettings.upperRightEyelid);
        }

        if (avDescriptor.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.JawFlapBone)
        {
            transforms.Add(avDescriptor.lipSyncJawBone);
        }

        var layers = avDescriptor.baseAnimationLayers.Select(a => a.animatorController).ToList();
        layers.AddRange(avDescriptor.specialAnimationLayers.Select(a => a.animatorController));
        foreach (var layer in layers.Where(a => a != null))
        {
            foreach (var binding in layer.animationClips.SelectMany(clip => AnimationUtility.GetCurveBindings(clip)))
            {
                if (binding.type == typeof(Transform))
                {
                    transforms.Add(GetTransformFromPath(binding.path));
                }
            }
        }

        var animators = GetComponentsInChildren<Animator>(true);
        foreach (var animator in animators)
        {
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                continue;
            foreach (var boneId in System.Enum.GetValues(typeof(HumanBodyBones)).Cast<HumanBodyBones>())
            {
                if (boneId < 0 || boneId >= HumanBodyBones.LastBone)
                    continue;
                transforms.Add(animator.GetBoneTransform(boneId));
            }
        }

        var alwaysDisabledComponents = FindAllUnusedComponents();
        var physBones = GetComponentsInChildren<VRCPhysBoneBase>(true)
            .Where(pb => !alwaysDisabledComponents.Contains(pb)).ToList();
        foreach (var physBone in physBones)
        {
            var root = physBone.GetRootTransform();
            var exclusions = new HashSet<Transform>(physBone.ignoreTransforms);
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                transforms.Add(current);
                if (exclusions.Contains(current))
                    continue;
                foreach (Transform child in current)
                {
                    stack.Push(child);
                }
            }
        }

        var constraints = GetComponentsInChildren<Behaviour>(true)
            .Where(b => b != null && !alwaysDisabledComponents.Contains(b))
            .Where(b => b.GetType().Name.Contains("Constraint")).ToList();
        foreach (var constraint in constraints)
        {
            transforms.Add(constraint.transform);
        }

        var finalIKScripts = GetComponentsInChildren<Behaviour>(true)
            .Where(b => b != null && !alwaysDisabledComponents.Contains(b))
            .Where(b => b.GetType().FullName.StartsWithSimple("RootMotion.FinalIK")).ToList();
        foreach (var finalIKScript in finalIKScripts)
        {
            transforms.UnionWith(FindReferencedTransforms(finalIKScript));
        }

        var headChopType = Type.GetType("VRC.SDK3.Avatars.Components.VRCHeadChop, VRCSDK3A");
        if (headChopType != null) {
            foreach (var headChop in GetComponentsInChildren(headChopType, true)) {
                using (var so = new SerializedObject(headChop)) {
                    var targetBonesProperty = so.FindProperty("targetBones");
                    for (int i = 0; i < targetBonesProperty.arraySize; i++) {
                        var targetBone = targetBonesProperty.GetArrayElementAtIndex(i).FindPropertyRelative("transform").objectReferenceValue as Transform;
                        if (targetBone != null) {
                            transforms.Add(targetBone);
                        }
                    }
                }
            }
        }

        transforms.UnionWith(transform.GetAllDescendants().Where(t => t.localScale != Vector3.one));

        return cache_FindAllMovingTransforms = transforms;
    }

    private HashSet<Transform> cache_FindAllUnmovingTransforms = null;
    public  HashSet<Transform> FindAllUnmovingTransforms()
    {
        if (cache_FindAllUnmovingTransforms != null)
            return cache_FindAllUnmovingTransforms;
        var avDescriptor = GetComponent<VRCAvatarDescriptor>();
        if (avDescriptor == null)
            return new HashSet<Transform>();
        var moving = FindAllMovingTransforms();
        return cache_FindAllUnmovingTransforms = new HashSet<Transform>(transform.GetAllDescendants().Where(t => !moving.Contains(t)));
    }

    private bool IsDPSPenetratorTipLight(Light light)
    {
        return light.type == LightType.Point && light.renderMode == LightRenderMode.ForceVertex
            && light.color.r < 0.01 && light.color.g < 0.01 && light.color.b < 0.01
            && light.range % 0.1 - 0.09 < 0.001;
    }

    private bool IsDPSPenetratorRoot(Transform t)
    {
        if (t == null)
            return false;
        if (t.GetComponentsInChildren<Light>(true).Count(IsDPSPenetratorTipLight) != 1)
            return false;
        if (t.GetComponentsInChildren<MeshRenderer>(true).Count() != 1)
            return false;
        var meshRenderer = t.GetComponentsInChildren<MeshRenderer>(true).First();
        if (meshRenderer.sharedMaterials.Length == 0)
            return false;
        var material = meshRenderer.sharedMaterials[0];
        if (material == null)
            return false;
        return material.HasProperty("_Length");
    }

    private bool IsTPSPenetratorRoot(Transform t)
    {
        if (t == null)
            return false;
        if (t.GetComponentsInChildren<Renderer>(true).Count() != 1)
            return false;
        var meshRenderer = t.GetComponentsInChildren<Renderer>(true).First();
        if (meshRenderer.sharedMaterials.Length == 0)
            return false;
        var material = meshRenderer.sharedMaterials[0];
        if (material == null)
            return false;
        return material.HasProperty("_TPSPenetratorEnabled") && material.GetFloat("_TPSPenetratorEnabled") > 0.5f;
    }

    private bool IsSPSPenetratorRoot(Transform t) {
		if(t == null)
			return false;
		if(t.GetComponentsInChildren<Renderer>(true).Count() != 1)
			return false;
		var meshRenderer = t.GetComponentsInChildren<Renderer>(true).First();
		if(meshRenderer.sharedMaterials.Length == 0)
			return false;
		var material = meshRenderer.sharedMaterials[0];
		if(material == null)
			return false;
		return material.HasProperty("_SPS_Length");
	}

    private HashSet<Renderer> cache_FindAllPenetrators = null;
    public HashSet<Renderer> FindAllPenetrators()
    {
        if (cache_FindAllPenetrators != null)
            return cache_FindAllPenetrators;
        var penetratorTipLights = GetComponentsInChildren<Light>(true)
            .Where(l => IsDPSPenetratorTipLight(l)).ToList();
        var penetrators = new HashSet<Renderer>();
        foreach (var light in penetratorTipLights)
        {
            var candidate = light.transform;
            while (candidate != null && !IsDPSPenetratorRoot(candidate))
            {
                candidate = candidate.parent;
            }
            if (IsDPSPenetratorRoot(candidate))
            {
                penetrators.Add(candidate.GetComponentsInChildren<MeshRenderer>(true).First());
            }
        }
        penetrators.UnionWith(GetComponentsInChildren<Renderer>(true).Where(m => IsTPSPenetratorRoot(m.transform) || IsSPSPenetratorRoot(m.transform)));
        return cache_FindAllPenetrators = penetrators;
    }

    public List<T> GetNonEditorOnlyComponentsInChildren<T>() where T : Component
    {
        var components = new List<T>();
        var stack = new Stack<Transform>();
        stack.Push(transform);
        while (stack.Count > 0)
        {
            var currentTransform = stack.Pop();
            if (currentTransform.gameObject.CompareTag("EditorOnly"))
                continue;
            components.AddRange(currentTransform.GetComponents<T>());
            foreach (Transform child in currentTransform)
            {
                stack.Push(child);
            }
        }
        return components;
    }

    public List<T> GetUsedComponentsInChildren<T>() where T : Component
    {
        Profiler.StartSection("GetUsedComponentsInChildren()");
        var result = new List<T>();
        var stack = new Stack<Transform>();
        var alwaysDisabledGameObjects = FindAllAlwaysDisabledGameObjects();
        var unusedComponents = FindAllUnusedComponents();
        if (!DeleteUnusedComponents)
        {
            alwaysDisabledGameObjects = new HashSet<Transform>();
            unusedComponents = new HashSet<Component>();
        }
        stack.Push(transform);
        while (stack.Count > 0)
        {
            var currentTransform = stack.Pop();
            if (currentTransform.gameObject.CompareTag("EditorOnly") || alwaysDisabledGameObjects.Contains(currentTransform))
                continue;
            result.AddRange(currentTransform.GetComponents<T>().Where(c => c != null && !unusedComponents.Contains(c)));
            foreach (Transform child in currentTransform)
            {
                stack.Push(child);
            }
        }
        Profiler.EndSection();
        return result;
    }

    private HashSet<Material> cache_FindAllUsedMaterials = null;
    public HashSet<Material> FindAllUsedMaterials()
    {
        if (cache_FindAllUsedMaterials != null)
            return cache_FindAllUsedMaterials;
        var materials = new HashSet<Material>();
        foreach (var renderer in GetUsedComponentsInChildren<Renderer>())
        {
            materials.UnionWith(renderer.sharedMaterials.Where(m => m != null));
        }
        return cache_FindAllUsedMaterials = materials;
    }

    private Texture2DArray CombineTextures(List<Texture2D> textures)
    {
        Profiler.StartSection("CombineTextures()");
        bool isLinear = IsTextureLinear(textures[0]);
        var texArray = new Texture2DArray(textures[0].width, textures[0].height,
            textures.Count, textures[0].format, textures[0].mipmapCount > 1, isLinear);
        texArray.anisoLevel = textures[0].anisoLevel;
        texArray.wrapMode = textures[0].wrapMode;
        texArray.filterMode = textures[0].filterMode;
        for (int i = 0; i < textures.Count; i++)
        {
            Graphics.CopyTexture(textures[i], 0, texArray, i);
        }
        Profiler.EndSection();
        texArray.name = $"{texArray.width}x{texArray.height}_{texArray.format}_{(isLinear ? "linear" : "sRGB")}_{texArray.wrapMode}_{texArray.filterMode}_2DArray";
        CreateUniqueAsset(texArray, $"{texArray.name}.asset");
        return texArray;
    }

    private void AddWeighted(ref Matrix4x4 a, Matrix4x4 b, float weight)
    {
        if (weight == 0)
            return;
        a.m00 += b.m00 * weight; a.m01 += b.m01 * weight; a.m02 += b.m02 * weight; a.m03 += b.m03 * weight;
        a.m10 += b.m10 * weight; a.m11 += b.m11 * weight; a.m12 += b.m12 * weight; a.m13 += b.m13 * weight;
        a.m20 += b.m20 * weight; a.m21 += b.m21 * weight; a.m22 += b.m22 * weight; a.m23 += b.m23 * weight;
        a.m30 += b.m30 * weight; a.m31 += b.m31 * weight; a.m32 += b.m32 * weight; a.m33 += b.m33 * weight;
    }

    private void SearchForTextureArrayCreation(List<List<Material>> sources)
    {
        foreach (var source in sources)
        {
            var parsedShader = ShaderAnalyzer.Parse(source[0]?.shader);
            if (parsedShader == null || !parsedShader.parsedCorrectly)
                continue;
            var propertyTextureLists = new Dictionary<string, List<Texture2D>>();
            foreach (var mat in source)
            {
                foreach (var prop in parsedShader.properties)
                {
                    if (!mat.HasProperty(prop.name))
                        continue;
                    if (prop.type != ParsedShader.Property.Type.Texture2D)
                        continue;
                    if (!propertyTextureLists.TryGetValue(prop.name, out var textureArray))
                    {
                        textureArray = new List<Texture2D>();
                        propertyTextureLists[prop.name] = textureArray;
                    }
                    var tex = mat.GetTexture(prop.name);
                    var tex2D = tex as Texture2D;
                    int index = textureArray.IndexOf(tex2D);
                    if (index == -1 && tex2D != null)
                    {
                        textureArray.Add(tex2D);
                    }
                }
            }
            foreach (var texArray in propertyTextureLists.Values.Where(a => a.Count > 1))
            {
                List<Texture2D> list = null;
                foreach (var subList in textureArrayLists)
                {
                    if (CanCombineTextures(subList[0], texArray[0]))
                    {
                        list = subList;
                        break;
                    }
                }
                if (list == null)
                {
                    textureArrayLists.Add(list = new List<Texture2D>());
                }
                list.AddRange(texArray.Except(list));
            }
        }
    }

    private string GenerateUniqueName(string name, HashSet<string> usedNames)
    {
        if (usedNames.Add(name))
        {
            return name;
        }
        int count = 1;
        while (!usedNames.Add(name + " " + count))
        {
            count++;
        }
        return name + " " + count;
    }

    private Material[] CreateOptimizedMaterials(
        List<List<Material>> sources,
        int meshToggleCount,
        string path,
        List<List<string>> originalMeshPaths = null,
        List<List<int>> mergedMeshIndices = null)
    {
        if (!(WritePropertiesAsStaticValues || sources.Any(list => list.Count > 1) || (meshToggleCount > 1 && MergeSkinnedMeshesWithShaderToggle)))
        {
            return sources.Select(list => list[0]).ToArray();
        }
        if (!fusedAnimatedMaterialProperties.TryGetValue(path, out var usedMaterialProps))
            usedMaterialProps = new HashSet<string>();
        if (mergedMeshIndices == null)
            mergedMeshIndices = sources.Select(s => Enumerable.Range(0, meshToggleCount).ToList()).ToList();
        HashSet<(string name, bool isVector)> defaultAnimatedProperties = null;
        oldPathToMergedPaths.TryGetValue(path, out var allOriginalMeshPaths);
        var sameAnimatedProperties = GetSameAnimatedPropertiesOnMergedMesh(path);
        if (allOriginalMeshPaths != null && (sources.Count != 1 || sources[0].Count != 1)) {
            defaultAnimatedProperties = new HashSet<(string name, bool isVector)>();
            for (int i = 0; i < allOriginalMeshPaths.Count; i++) {
                if (animatedMaterialProperties.TryGetValue(allOriginalMeshPaths[i][0], out var animatedProps)) {
                    foreach (var prop in animatedProps) {
                        string name = prop;
                        bool isVector = name.EndsWith(".x") || name.EndsWith(".r");
                        if (isVector) {
                            name = name.Substring(0, name.Length - 2);
                        } else if ((name.Length > 2 && name[name.Length - 2] == '.')
                                || (!isVector && (animatedProps.Contains($"{name}.x") || animatedProps.Contains($"{name}.r")))) {
                            continue;
                        }
                        if (sameAnimatedProperties.Contains(name)) {
                            continue;
                        }
                        defaultAnimatedProperties.Add(($"d4rkAvatarOptimizer{name}_ArrayIndex{i}", isVector));
                        defaultAnimatedProperties.Add((name, isVector));
                    }
                }
                defaultAnimatedProperties.Add(($"_IsActiveMesh{i}", false));
            }
        }
        var materials = new Material[sources.Count];
        var parsedShader = new ParsedShader[sources.Count];
        var sanitizedMaterialNames = new string[sources.Count];
        var setShaderKeywords = new List<string>[sources.Count];
        var replace = new Dictionary<string, string>[sources.Count];
        var texturesToMerge = new HashSet<string>[sources.Count];
        var propertyTextureArrayIndex = new Dictionary<string, int>[sources.Count];
        var arrayPropertyValues = new Dictionary<string, (string type, List<string> values)>[sources.Count];
        var texturesToCheckNull = new Dictionary<string, string>[sources.Count];
        var animatedPropertyValues = new Dictionary<string, string>[sources.Count];
        var poiUsedPropertyDefines = new Dictionary<string, bool>[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            parsedShader[i] = ShaderAnalyzer.Parse(source[0]?.shader);
            if (parsedShader[i] == null || !parsedShader[i].parsedCorrectly)
            {
                materials[i] = source[0];
                continue;
            }
            sanitizedMaterialNames[i] = "s_" + Path.GetFileNameWithoutExtension(parsedShader[i].filePath)
                + " " + string.Join("_", source[0].name.Split(Path.GetInvalidFileNameChars(), System.StringSplitOptions.RemoveEmptyEntries));
            texturesToMerge[i] = new HashSet<string>();
            propertyTextureArrayIndex[i] = new Dictionary<string, int>();
            arrayPropertyValues[i] = new Dictionary<string, (string type, List<string> values)>();
            poiUsedPropertyDefines[i] = new Dictionary<string, bool>();
            foreach (var mat in source)
            {
                foreach (var prop in parsedShader[i].properties)
                {
                    if (!mat.HasProperty(prop.name))
                        continue;
                    switch (prop.type)
                    {
                        case ParsedShader.Property.Type.Float:
                            (string type, List<string> values) propertyArray;
                            if (!arrayPropertyValues[i].TryGetValue(prop.name, out propertyArray))
                            {
                                propertyArray.type = "float";
                                propertyArray.values = new List<string>();
                                arrayPropertyValues[i][prop.name] = propertyArray;
                            }
                            var value = mat.GetFloat(prop.name);
                            value = (prop.hasGammaTag) ? Mathf.GammaToLinearSpace(value) : value;
                            propertyArray.values.Add($"{value}");
                        break;
                        case ParsedShader.Property.Type.Int:
                            if (!arrayPropertyValues[i].TryGetValue(prop.name, out propertyArray))
                            {
                                propertyArray.type = "int";
                                propertyArray.values = new List<string>();
                                arrayPropertyValues[i][prop.name] = propertyArray;
                            }
                            propertyArray.values.Add("" + mat.GetInt(prop.name));
                        break;
                        case ParsedShader.Property.Type.Color:
                        case ParsedShader.Property.Type.ColorHDR:
                        case ParsedShader.Property.Type.Vector:
                            if (!arrayPropertyValues[i].TryGetValue(prop.name, out propertyArray))
                            {
                                propertyArray.type = "float4";
                                propertyArray.values = new List<string>();
                                arrayPropertyValues[i][prop.name] = propertyArray;
                            }
                            var col = mat.GetColor(prop.name);
                            col = (prop.type == ParsedShader.Property.Type.Color || prop.hasGammaTag) ? col.linear : col;
                            propertyArray.values.Add($"float4({col.r}, {col.g}, {col.b}, {col.a})");
                            break;
                        case ParsedShader.Property.Type.Texture2D:
                            if (!arrayPropertyValues[i].TryGetValue("arrayIndex" + prop.name, out var textureArray))
                            {
                                arrayPropertyValues[i]["arrayIndex" + prop.name] = ("float", new List<string>());
                                arrayPropertyValues[i]["shouldSample" + prop.name] = ("bool", new List<string>());
                            }
                            var tex = mat.GetTexture(prop.name);
                            var tex2D = tex as Texture2D;
                            int index = 0;
                            if (tex2D != null)
                            {
                                int texArrayIndex = textureArrayLists.FindIndex(l => l.Contains(tex2D));
                                if (texArrayIndex != -1)
                                {
                                    index = textureArrayLists[texArrayIndex].IndexOf(tex2D);
                                    texturesToMerge[i].Add(prop.name);
                                    propertyTextureArrayIndex[i][prop.name] = texArrayIndex;
                                }
                            }
                            arrayPropertyValues[i]["arrayIndex" + prop.name].values.Add("" + index);
                            arrayPropertyValues[i]["shouldSample" + prop.name].values.Add((tex != null).ToString().ToLowerInvariant());
                            if (!arrayPropertyValues[i].TryGetValue(prop.name + "_TexelSize", out propertyArray))
                            {
                                propertyArray.type = "float4";
                                propertyArray.values = new List<string>();
                                arrayPropertyValues[i][prop.name + "_TexelSize"] = propertyArray;
                            }
                            var texelSize = new Vector2(tex?.width ?? 4, tex?.height ?? 4);
                            propertyArray.values.Add($"float4(1.0 / {texelSize.x}, 1.0 / {texelSize.y}, {texelSize.x}, {texelSize.y})");
                            break;
                    }
                }
            }

            replace[i] = new Dictionary<string, string>();
            foreach (var tuple in arrayPropertyValues[i].ToList())
            {
                if (usedMaterialProps.Contains(tuple.Key) && !(meshToggleCount > 1))
                {
                    arrayPropertyValues[i].Remove(tuple.Key);
                }
                else if (tuple.Value.values.All(v => v == tuple.Value.values[0]))
                {
                    arrayPropertyValues[i].Remove(tuple.Key);
                    replace[i][tuple.Key] = tuple.Value.values[0];
                }
            }
            if (!WritePropertiesAsStaticValues)
            {
                foreach (string key in replace[i].Keys.Where(k => !k.StartsWithSimple("arrayIndex")).ToArray())
                {
                    replace[i].Remove(key);
                }
            }

            texturesToCheckNull[i] = new Dictionary<string, string>();
            foreach (var prop in parsedShader[i].properties)
            {
                if (prop.type == ParsedShader.Property.Type.Texture2D)
                {
                    if (arrayPropertyValues[i].ContainsKey("shouldSample" + prop.name))
                    {
                        texturesToCheckNull[i][prop.name] = prop.defaultValue;
                    }
                }
                switch (prop.type)
                {
                    case ParsedShader.Property.Type.Texture2D:
                    case ParsedShader.Property.Type.Texture2DArray:
                    case ParsedShader.Property.Type.Texture3D:
                    case ParsedShader.Property.Type.TextureCube:
                    case ParsedShader.Property.Type.TextureCubeArray:
                        bool isUsed = arrayPropertyValues[i].ContainsKey($"shouldSample{prop.name}")
                            || source[0].GetTexture(prop.name) != null;
                        poiUsedPropertyDefines[i][$"PROP{prop.name.ToUpper()}"] = isUsed;
                        break;
                }
            }

            animatedPropertyValues[i] = new Dictionary<string, string>();
            if (meshToggleCount > 1) {
                foreach (var propName in usedMaterialProps) {
                    if (sameAnimatedProperties.Contains(propName)) {
                        arrayPropertyValues[i].Remove(propName);
                        replace[i].Remove(propName);
                        continue;
                    }
                    if (originalMeshPaths != null) {
                        bool skipProp = true;
                        foreach (var originalPath in originalMeshPaths[i]) {
                            if (animatedMaterialProperties.TryGetValue(originalPath, out var props)) {
                                if (props.Contains(propName) || props.Contains(propName + ".x") || props.Contains(propName + ".r")) {
                                    skipProp = false;
                                    break;
                                }
                            }
                        }
                        if (skipProp)
                            continue;
                    }
                    if (parsedShader[i].propertyTable.TryGetValue(propName, out var prop)) {
                        string type = "float4";
                        if (prop.type == ParsedShader.Property.Type.Float)
                            type = "float";
                        if (prop.type == ParsedShader.Property.Type.Int)
                            type = "int";
                        animatedPropertyValues[i][propName] = type;
                    }
                }
            }

            setShaderKeywords[i] = parsedShader[i].shaderFeatureKeyWords.Where(k => source[0].IsKeywordEnabled(k)).ToList();
        }

        var optimizedShader = new ShaderOptimizer.OptimizedShader[sources.Count];
        var basicMergedMeshPaths = allOriginalMeshPaths?.Select(list => string.Join(", ", list)).ToList();
        Profiler.StartSection("ShaderOptimizer.Run()");
        Parallel.For(0, sources.Count, i =>
        {
            if (parsedShader[i] != null && parsedShader[i].parsedCorrectly)
            {
                optimizedShader[i] = ShaderOptimizer.Run(
                    parsedShader[i],
                    replace[i],
                    meshToggleCount,
                    basicMergedMeshPaths,
                    i == 0 ? defaultAnimatedProperties : null,
                    mergedMeshIndices[i],
                    arrayPropertyValues[i],
                    texturesToCheckNull[i],
                    texturesToMerge[i],
                    animatedPropertyValues[i],
                    setShaderKeywords[i],
                    poiUsedPropertyDefines[i],
                    sanitizedMaterialNames[i]);
            }
        });
        Profiler.EndSection();

        for (int i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            if (parsedShader[i] == null || !parsedShader[i].parsedCorrectly)
                continue;

            DisplayProgressBar($"Optimizing shader {source[0].shader.name} ({i + 1}/{sources.Count})");
            var shaderName = optimizedShader[i].name;
            var shaderFilePath = AssetDatabase.GenerateUniqueAssetPath(trashBinPath + shaderName + ".shader");
            var name = Path.GetFileNameWithoutExtension(shaderFilePath);
            optimizedShader[i].SetName(name);
            foreach (var opt in optimizedShader[i].files)
            {
                var filePath = shaderFilePath;
                if (opt.name != "Shader")
                {
                    filePath = trashBinPath + opt.name;
                }
                System.IO.File.WriteAllLines(filePath, opt.lines);
                optimizedMaterialImportPaths.Add(filePath);
            }
            var optimizedMaterial = new Material(Shader.Find("Unlit/Texture"));
            optimizedMaterial.shader = null;
            optimizedMaterial.name = "m_" + name.Substring(2);
            materials[i] = optimizedMaterial;
            optimizedMaterials.Add((optimizedMaterial, source, optimizedShader[i]));
            var arrayList = new List<(string name, Texture2DArray array)>();
            foreach (var texArray in propertyTextureArrayIndex[i])
            {
                arrayList.Add((texArray.Key, textureArrays[texArray.Value]));
            }
            if (arrayList.Count > 0)
            {
                texArrayPropertiesToSet[optimizedMaterial] = arrayList;
            }
        }
        return materials;
    }

    private void SaveOptimizedMaterials()
    {
        Profiler.StartSection("AssetDatabase.ImportAsset()");
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach(var importPath in optimizedMaterialImportPaths)
            {
                AssetDatabase.ImportAsset(importPath);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
        Profiler.EndSection();

        for (int i = 0; i < optimizedMaterials.Count; i++)
        {
            var mat = optimizedMaterials[i].target;
            var sources = optimizedMaterials[i].sources;
            var source = sources[0];
            var optimizedShader = optimizedMaterials[i].optimizerResult;
            DisplayProgressBar($"Loading optimized shader {mat.name}", 0.7f + 0.2f * (i / (float)optimizedMaterials.Count));
            Profiler.StartSection("AssetDatabase.LoadAssetAtPath<Shader>()");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>($"{trashBinPath}{optimizedShader.name}.shader");
            Profiler.StartNextSection("mat.shader = shader");
            mat.shader = shader;
            mat.renderQueue = source.renderQueue;
            Profiler.StartNextSection("CopyMaterialProperties");
            var texArrayProperties = new HashSet<string>();
            if (texArrayPropertiesToSet.TryGetValue(mat, out var texArrays))
            {
                foreach (var texArray in texArrays)
                {
                    string texArrayName = texArray.name;
                    if (texArrayName == "_MainTex")
                    {
                        texArrayName = "_MainTexButNotQuiteSoThatUnityDoesntCry";
                    }
                    mat.SetTexture(texArrayName, texArray.array);
                    mat.SetTextureOffset(texArrayName, source.GetTextureOffset(texArray.name));
                    mat.SetTextureScale(texArrayName, source.GetTextureScale(texArray.name));
                    texArrayProperties.Add(texArrayName);
                }
            }
            foreach (var prop in optimizedShader.tex2DProperties)
            {
                if (!source.HasProperty(prop) || texArrayProperties.Contains(prop))
                    continue;
                var tex = sources.Select(m => m.GetTexture(prop)).FirstOrDefault(t => t != null);
                mat.SetTexture(prop, tex);
                mat.SetTextureOffset(prop, source.GetTextureOffset(prop));
                mat.SetTextureScale(prop, source.GetTextureScale(prop));
            }
            foreach (var prop in optimizedShader.tex3DCubeProperties)
            {
                if (!source.HasProperty(prop))
                    continue;
                var tex = sources.Select(m => m.GetTexture(prop)).FirstOrDefault(t => t != null);
                mat.SetTexture(prop, tex);
            }
            foreach (var prop in optimizedShader.floatProperties)
            {
                if (!source.HasProperty(prop))
                    continue;
                mat.SetFloat(prop, source.GetFloat(prop));
            }
            foreach (var prop in optimizedShader.colorProperties)
            {
                if (!source.HasProperty(prop))
                    continue;
                mat.SetColor(prop, source.GetColor(prop));
            }
            foreach (var prop in optimizedShader.integerProperties)
            {
                if (!source.HasProperty(prop))
                    continue;
                mat.SetInteger(prop, source.GetInt(prop));
            }
            var vrcFallback = source.GetTag("VRCFallback", false, "not_set");
            if (vrcFallback != "not_set")
            {
                mat.SetOverrideTag("VRCFallback", vrcFallback);
            }
            Profiler.EndSection();
        }

        var skinnedMeshRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var meshRenderer in skinnedMeshRenderers)
        {
            var mesh = meshRenderer.sharedMesh;
            if (mesh == null)
                continue;

            var props = new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(props);
            int meshCount = props.GetInt("d4rkAvatarOptimizer_CombinedMeshCount");

            if (MergeSkinnedMeshesWithShaderToggle
                && fusedAnimatedMaterialProperties.TryGetValue(GetPathToRoot(meshRenderer), out var animatedProperties))
            {
                foreach (var mat in meshRenderer.sharedMaterials)
                {
                    if (mat == null)
                        continue;
                    foreach (var animPropName in animatedProperties)
                    {
                        var propName = animPropName;
                        bool isVector = propName.EndsWith(".x");
                        bool isColor = propName.EndsWith(".r");
                        if (isColor || isVector) {
                            propName = propName.Substring(0, propName.Length - 2);
                        } else if (propName[propName.Length - 2] == '.') {
                            continue;
                        } else if (animatedProperties.Contains($"{propName}.x")) {
                            isVector = true;
                        } else if (animatedProperties.Contains($"{propName}.r")) {
                            isColor = true;
                        }
                        for (int mID = 0; mID < meshCount; mID++)
                        {
                            var propArrayName = $"d4rkAvatarOptimizer{propName}_ArrayIndex{mID}";
                            if (!mat.HasProperty(propArrayName))
                                continue;
                            var signal = float.NaN;
                            if (isVector) {
                                mat.SetVector(propArrayName, new Vector4(signal, signal, signal, signal));
                            } else if(isColor) {
                                mat.SetColor(propArrayName, new Color(signal, signal, signal, signal));
                            } else {
                                mat.SetFloat(propArrayName, signal);
                            }
                        }
                    }
                }
            }

            if (meshCount > 1)
            {
                foreach (var mat in meshRenderer.sharedMaterials)
                {
                    if (mat == null)
                        continue;
                    for (int mID = 0; mID < meshCount; mID++)
                    {
                        var propName = $"_IsActiveMesh{mID}";
                        mat.SetFloat(propName, props.GetFloat(propName));
                    }
                }
            }
        }

        foreach (var mat in optimizedMaterials.Select(o => o.target))
        {
            CreateUniqueAsset(mat, mat.name + ".mat");
        }
    }

    private bool IsTextureLinear(Texture2D tex)
    {
        if (tex == null)
            return false;
        var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) as TextureImporter;
        if (importer == null)
            return false;
        return importer.sRGBTexture == false;
    }

    private bool CanCombineTextures(Texture a, Texture b)
    {
        if (a == b)
            return true;
        if (a == null && b is Texture2D)
            return true;
        if (a is Texture2D && b == null)
            return true;
        if (!(a is Texture2D) || !(b is Texture2D))
            return false;
        if (a.texelSize != b.texelSize)
            return false;
        var a2D = a as Texture2D;
        var b2D = b as Texture2D;
        if (a2D.format != b2D.format)
            return false;
        if (a2D.format == TextureFormat.DXT1Crunched || a2D.format == TextureFormat.DXT5Crunched)
            return false;
        if (a2D.mipmapCount != b2D.mipmapCount)
            return false;
        if (a2D.filterMode != b2D.filterMode)
            return false;
        if (a2D.wrapMode != b2D.wrapMode)
            return false;
        if (IsTextureLinear(a2D) != IsTextureLinear(b2D))
            return false;
        return true;
    }

    private bool CanCombineMaterialsWith(List<MaterialSlot> list, MaterialSlot candidate)
    {
        var candidateMat = candidate.material;
        var firstMat = list[0].material;
        if (candidateMat == null || firstMat == null)
            return false;
        if (firstMat.shader != candidateMat.shader)
            return false;
        if (list.Any(slot => slot.GetTopology() != candidate.GetTopology()))
            return false;
        bool IsAffectedByMaterialSwap(MaterialSlot slot) =>
            (slotSwapMaterials.ContainsKey((GetPathToRoot(slot.renderer), slot.index)))
            || (materialSlotRemap.TryGetValue((GetPathToRoot(slot.renderer), slot.index), out var remap) && slotSwapMaterials.ContainsKey(remap));
        if (IsAffectedByMaterialSwap(list[0]) || IsAffectedByMaterialSwap(candidate))
            return false;
        var listMaterials = list.Select(slot => slot.material).ToArray();
        bool allTheSameAsCandidate = listMaterials.All(mat => mat == candidateMat);
        if (allTheSameAsCandidate || !MergeDifferentPropertyMaterials)
            return allTheSameAsCandidate;
        if (list.Count > 1 && listMaterials.Any(mat => mat == candidateMat))
            return true;
        var parsedShader = ShaderAnalyzer.Parse(candidateMat.shader);
        if (parsedShader.parsedCorrectly == false)
            return false;
        if (firstMat.renderQueue != candidateMat.renderQueue)
            return false;
        #if UNITY_2022_1_OR_NEWER
            bool hasAnyMaterialVariant = listMaterials.Any(m => m.isVariant) || candidateMat.isVariant;
        #else
            bool hasAnyMaterialVariant = false;
        #endif
        if (!hasAnyMaterialVariant && firstMat.GetTag("VRCFallback", false, "None") != candidateMat.GetTag("VRCFallback", false, "None"))
            return false;
        foreach (var pass in parsedShader.passes)
        {
            if (pass.vertex == null)
                return false;
            if (pass.hull != null)
                return false;
            if (pass.domain != null)
                return false;
            if (pass.fragment == null)
                return false;
        }
        foreach (var keyword in parsedShader.shaderFeatureKeyWords)
        {
            if (firstMat.IsKeywordEnabled(keyword) ^ candidateMat.IsKeywordEnabled(keyword))
                return false;
        }
        listMaterials = new HashSet<Material>(listMaterials).ToArray();
        bool mergeTextures = MergeSameDimensionTextures && parsedShader.CanMergeTextures();
        foreach (var prop in parsedShader.propertiesToCheckWhenMerging)
        {
            switch (prop.type)
            {
                case ParsedShader.Property.Type.Int:
                case ParsedShader.Property.Type.Float:
                    var candidateValue = candidateMat.GetFloat(prop.name);
                    if (listMaterials[0].GetFloat(prop.name) != candidateValue)
                        return false;
                    break;
                case ParsedShader.Property.Type.Texture2D:
                case ParsedShader.Property.Type.Texture2DArray:
                case ParsedShader.Property.Type.Texture3D:
                case ParsedShader.Property.Type.TextureCube:
                case ParsedShader.Property.Type.TextureCubeArray:
                    bool mergeTexture = mergeTextures && (prop.name != "_MainTex" || MergeMainTex);
                    int propertyID = Shader.PropertyToID(prop.name);
                    var cTex = candidateMat.GetTexture(propertyID);
                    if (!mergeTexture && cTex != firstMat.GetTexture(propertyID))
                        return false;
                    if (mergeTexture && listMaterials.Any(mat => !CanCombineTextures(cTex, mat.GetTexture(propertyID))))
                        return false;
                    break;
            }
        }
        return true;
    }

    private void OptimizeMaterialsOnNonSkinnedMeshes()
    {
        var meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
        var exclusions = GetAllExcludedTransforms();
        foreach (var meshRenderer in meshRenderers)
        {
            if (exclusions.Contains(meshRenderer.transform) || meshRenderer.GetSharedMesh() == null)
                continue;
            DisplayProgressBar($"Optimizing materials on {meshRenderer.name}");
            var path = GetPathToRoot(meshRenderer);
            var mats = meshRenderer.sharedMaterials.Select((material, index) => (material, index)).Where(m => m.material != null).ToList();
            var alreadyOptimizedMaterials = new HashSet<Material>();
            foreach (var (material, index) in mats)
            {
                if (slotSwapMaterials.TryGetValue((path, index), out var matList))
                {
                    alreadyOptimizedMaterials.UnionWith(matList);
                }
            }
            var toOptimize = mats.Select(t => t.material).Where(m => !alreadyOptimizedMaterials.Contains(m)).Distinct().ToList();
            var optimizeMaterialWrapper = toOptimize.Select(m => new List<Material>() { m }).ToList();
            var optimizedMaterialsList = CreateOptimizedMaterials(optimizeMaterialWrapper, 0, GetPathToRoot(meshRenderer));
            var optimizedMaterials = toOptimize.Select((mat, index) => (mat, index))
                .ToDictionary(t => t.mat, t => optimizedMaterialsList[t.index]);
            var finalMaterials = new Material[meshRenderer.sharedMaterials.Length];
            foreach (var (material, index) in mats)
            {
                if (!optimizedMaterials.TryGetValue(material, out var optimized))
                {
                    optimized = material;
                    if (optimizedSlotSwapMaterials.TryGetValue((path, index), out var optimizedSwapMaterialMap))
                    {
                        if (!optimizedSwapMaterialMap.TryGetValue(material, out optimized))
                        {
                            optimized = material;
                        }
                    }
                }
                finalMaterials[index] = optimized;
            }
            meshRenderer.sharedMaterials = finalMaterials;
        }
    }

    public List<List<MaterialSlot>> FindAllMergeAbleMaterials(IEnumerable<Renderer> renderers)
    {
        var matched = new List<List<MaterialSlot>>();
        foreach (var renderer in renderers)
        {
            foreach (var candidate in MaterialSlot.GetAllSlotsFrom(renderer))
            {
                bool foundMatch = false;
                for (int i = 0; i < matched.Count; i++)
                {
                    if (CanCombineMaterialsWith(matched[i], candidate))
                    {
                        matched[i].Add(candidate);
                        foundMatch = true;
                        break;
                    }
                }
                if (!foundMatch)
                {
                    matched.Add(new List<MaterialSlot> { candidate });
                }
            }
        }
        return matched;
    }

    private void CreateTextureArrays()
    {
        textureArrayLists.Clear();
        textureArrays.Clear();

        var skinnedMeshRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var meshRenderer in skinnedMeshRenderers)
        {
            var mesh = meshRenderer.sharedMesh;

            if (mesh == null)
                continue;

            var matched = FindAllMergeAbleMaterials(new [] { meshRenderer });
            
            var matchedMaterials = matched.Select(list => list.Select(slot => slot.material).ToList()).ToList();
            var uniqueMatchedMaterials = matchedMaterials.Select(mm => mm.Distinct().ToList()).ToList();

            SearchForTextureArrayCreation(uniqueMatchedMaterials);
        }

        foreach (var textureList in textureArrayLists)
        {
            textureArrays.Add(CombineTextures(textureList));
        }
    }
    
    private void CombineAndOptimizeMaterials()
    {
        var exclusions = GetAllExcludedTransforms();
        var skinnedMeshRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(smr => !exclusions.Contains(smr.transform) && smr.sharedMesh != null).ToArray();
        for (int meshRenderIndex = 0; meshRenderIndex < skinnedMeshRenderers.Length; meshRenderIndex++)
        {
            var meshRenderer = skinnedMeshRenderers[meshRenderIndex];
            var mesh = meshRenderer.sharedMesh;
            
            DisplayProgressBar($"Combining materials on {meshRenderer.name} ({meshRenderIndex + 1}/{skinnedMeshRenderers.Length})");

            var props = new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(props);
            int meshCount = props.GetInt("d4rkAvatarOptimizer_CombinedMeshCount");
            string meshPath = GetPathToRoot(meshRenderer);

            var matchedSlots = FindAllMergeAbleMaterials(new [] { meshRenderer });
            var uniqueMatchedSlots = matchedSlots.Select(list => list.Select(slot => list.First(slot2 => slot.material == slot2.material)).Distinct().ToList()).ToList();
            var mergedMeshIndices = new List<List<int>>();

            var sourceVertices = mesh.vertices;
            var hasUvSet = new bool[8] {
                true,
                mesh.HasVertexAttribute(VertexAttribute.TexCoord1),
                mesh.HasVertexAttribute(VertexAttribute.TexCoord2),
                mesh.HasVertexAttribute(VertexAttribute.TexCoord3),
                mesh.HasVertexAttribute(VertexAttribute.TexCoord4),
                mesh.HasVertexAttribute(VertexAttribute.TexCoord5),
                mesh.HasVertexAttribute(VertexAttribute.TexCoord6),
                mesh.HasVertexAttribute(VertexAttribute.TexCoord7),
            };
            int highestUsedUvSet = hasUvSet.Select((b, i) => (b, i)).Where(t => t.b).Select(t => t.i).LastOrDefault();
            var sourceUv = Enumerable.Range(0, 8).Select(i => hasUvSet[i] ? new List<Vector4>(sourceVertices.Length) : null).ToArray();
            for(int i = 0; i <= highestUsedUvSet; i++)
            {
                if (!hasUvSet[i])
                    continue;
                mesh.GetUVs(i, sourceUv[i]);
                sourceUv[i] = sourceUv[i].Count != sourceVertices.Length ? Enumerable.Repeat(Vector4.zero, sourceVertices.Length).ToList() : sourceUv[i];
            }
            Color[] sourceColor = null;
            Color32[] sourceColor32 = null;
            var sourceNormals = mesh.normals;
            var sourceTangents = mesh.tangents;
            var sourceWeights = mesh.boneWeights;

            var targetUv = Enumerable.Range(0, 8).Select(i => hasUvSet[i] ? new List<Vector4>(sourceVertices.Length) : null).ToArray();
            List<Color> targetColor = null;
            List<Color32> targetColor32 = null;
            if (mesh.HasVertexAttribute(VertexAttribute.Color))
            {
                if (mesh.GetVertexAttributeFormat(VertexAttribute.Color) == VertexAttributeFormat.UNorm8)
                {
                    targetColor32 = new List<Color32>(sourceVertices.Length);
                    sourceColor32 = mesh.colors32;
                }
                else
                {
                    targetColor = new List<Color>(sourceVertices.Length);
                    sourceColor = mesh.colors;
                }
            }
            var targetVertices = new List<Vector3>(sourceVertices.Length);
            var targetIndices = new List<List<int>>();
            var targetTopology = new List<MeshTopology>();
            var targetNormals = new List<Vector3>(sourceVertices.Length);
            var targetTangents = new List<Vector4>(sourceVertices.Length);
            var targetWeights = new List<BoneWeight>(sourceVertices.Length);

            var targetOldVertexIndex = new List<int>();

            for (int i = 0; i < matchedSlots.Count; i++)
            {
                var uniqueMeshIndices = new HashSet<int>();
                var indexList = new List<int>();
                for (int k = 0; k < matchedSlots[i].Count; k++)
                {
                    var indexMap = new Dictionary<int, int>();
                    int internalMaterialID = uniqueMatchedSlots[i].Select((slot, index) => (slot, index)).First(t => t.slot.material == matchedSlots[i][k].material).index;
                    int materialSubMeshId = Math.Min(mesh.subMeshCount - 1, matchedSlots[i][k].index);
                    var sourceIndices = mesh.GetIndices(materialSubMeshId);
                    for (int j = 0; j < sourceIndices.Length; j++)
                    {
                        int oldIndex = sourceIndices[j];
                        if (indexMap.TryGetValue(oldIndex, out int newIndex))
                        {
                            indexList.Add(newIndex);
                        }
                        else
                        {
                            newIndex = targetVertices.Count;
                            indexList.Add(newIndex);
                            indexMap[oldIndex] = newIndex;
                            targetUv[0].Add(new Vector4(sourceUv[0][oldIndex].x, sourceUv[0][oldIndex].y, sourceUv[0][oldIndex].z + internalMaterialID, 0));
                            for (int a = 1; a <= highestUsedUvSet; a++)
                            {
                                targetUv[a]?.Add(sourceUv[a][oldIndex]);
                            }
                            targetColor?.Add(sourceColor[oldIndex]);
                            targetColor32?.Add(sourceColor32[oldIndex]);
                            targetVertices.Add(sourceVertices[oldIndex]);
                            targetNormals.Add(sourceNormals[oldIndex]);
                            targetTangents.Add(sourceTangents[oldIndex]);
                            targetWeights.Add(sourceWeights[oldIndex]);
                            targetOldVertexIndex.Add(oldIndex);
                            uniqueMeshIndices.Add((int)sourceUv[0][oldIndex].z >> 12);
                        }
                    }
                }
                targetIndices.Add(indexList);
                targetTopology.Add(mesh.GetTopology(Math.Min(matchedSlots[i][0].index, mesh.subMeshCount - 1)));
                mergedMeshIndices.Add(uniqueMeshIndices.ToList());
            }

            {
                Mesh newMesh = new Mesh();
                newMesh.name = mesh.name;
                newMesh.indexFormat = targetVertices.Count >= 65536
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16;
                newMesh.SetVertices(targetVertices);
                newMesh.bindposes = mesh.bindposes;
                newMesh.SetBoneWeights(targetWeights.ToArray());
                if (targetColor != null && targetColor.Any(c => !c.Equals(Color.white)))
                {
                    newMesh.colors = targetColor.ToArray();
                }
                else if (targetColor32 != null && targetColor32.Any(c => !c.Equals(new Color32(255, 255, 255, 255))))
                {
                    newMesh.colors32 = targetColor32.ToArray();
                }
                for (int i = 0; i <= highestUsedUvSet; i++)
                {
                    if (!hasUvSet[i])
                        continue;
                    if (targetUv[i].Any(uv => uv.w != 0))
                    {
                        newMesh.SetUVs(i, targetUv[i]);
                    }
                    else if (targetUv[i].Any(uv => uv.z != 0))
                    {
                        newMesh.SetUVs(i, targetUv[i].Select(uv => new Vector3(uv.x, uv.y, uv.z)).ToArray());
                    }
                    else if (targetUv[i].Any(uv => uv.x != 0 || uv.y != 0))
                    {
                        newMesh.SetUVs(i, targetUv[i].Select(uv => new Vector2(uv.x, uv.y)).ToArray());
                    }
                }
                newMesh.bounds = mesh.bounds;
                newMesh.SetNormals(targetNormals);
                if (targetTangents.Any(t => t != Vector4.zero))
                    newMesh.SetTangents(targetTangents.Select(t => t == Vector4.zero ? new Vector4(1, 0, 0, 1) : t).ToArray());
                newMesh.subMeshCount = matchedSlots.Count;
                for (int i = 0; i < matchedSlots.Count; i++)
                {
                    newMesh.SetIndices(targetIndices[i].ToArray(), targetTopology[i], i);
                }

                int meshVertexCount = mesh.vertexCount;
                int newMeshVertexCount = newMesh.vertexCount;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    for (int j = 0; j < mesh.GetBlendShapeFrameCount(i); j++)
                    {
                        var sourceDeltaVertices = new Vector3[meshVertexCount];
                        var sourceDeltaNormals = new Vector3[meshVertexCount];
                        var sourceDeltaTangents = new Vector3[meshVertexCount];
                        mesh.GetBlendShapeFrameVertices(i, j, sourceDeltaVertices, sourceDeltaNormals, sourceDeltaTangents);
                        var targetDeltaVertices = new Vector3[newMeshVertexCount];
                        var targetDeltaNormals = new Vector3[newMeshVertexCount];
                        var targetDeltaTangents = new Vector3[newMeshVertexCount];
                        for (int k = 0; k < newMeshVertexCount; k++)
                        {
                            var oldIndex = targetOldVertexIndex[k];
                            targetDeltaVertices[k] = sourceDeltaVertices[oldIndex];
                            targetDeltaNormals[k] = sourceDeltaNormals[oldIndex];
                            targetDeltaTangents[k] = sourceDeltaTangents[oldIndex];
                        }
                        var name = mesh.GetBlendShapeName(i);
                        var weight = mesh.GetBlendShapeFrameWeight(i, j);
                        newMesh.AddBlendShapeFrame(name, weight, targetDeltaVertices, targetDeltaNormals, targetDeltaTangents);
                    }
                }

                Profiler.StartSection("Mesh.Optimize()");

                if(!IsSPSPenetratorRoot(meshRenderer.transform)) {
					// this breaks sps...
                    newMesh.Optimize();
                }
                
                Profiler.EndSection();

                CreateUniqueAsset(newMesh, newMesh.name + ".asset");

                meshRenderer.sharedMesh = newMesh;
            }

            (string path, int index) GetOriginalSlot((string path, int index) slot) {
                if (!materialSlotRemap.TryGetValue(slot, out var remap)) {
                    Debug.LogWarning($"Could not find original material slot for {slot.path}.{slot.index}");
                    materialSlotRemap[slot] = remap = slot;
                }
                return remap;
            }

            var originalMeshPaths = matchedSlots.Select(list => list.Select(slot => GetOriginalSlot((meshPath, slot.index)).path).Distinct().ToList()).ToList();
            var uniqueMatchedMaterials = uniqueMatchedSlots.Select(list => list.Select(slot => slot.material).ToList()).ToList();
            var optimizedMaterials = CreateOptimizedMaterials(uniqueMatchedMaterials, meshCount > 1 ? meshCount : 0, meshPath, originalMeshPaths, mergedMeshIndices);

            for (int i = 0; i < uniqueMatchedMaterials.Count; i++)
            {
                if (uniqueMatchedMaterials[i].Count != 1 || uniqueMatchedMaterials[i][0] == null)
                    continue;
                var originalSlot = GetOriginalSlot((meshPath, matchedSlots[i][0].index));
                AddAnimationPathChange((originalSlot.path, $"m_Materials.Array.data[{originalSlot.index}]", typeof(SkinnedMeshRenderer)),
                    (meshPath, $"m_Materials.Array.data[{i}]", typeof(SkinnedMeshRenderer)));
                if (!optimizedSlotSwapMaterials.TryGetValue(originalSlot, out var optimizedSwapMaterials))
                {
                    optimizedSlotSwapMaterials[originalSlot] = optimizedSwapMaterials = new Dictionary<Material, Material>();
                }
                optimizedSwapMaterials[uniqueMatchedMaterials[i][0]] = optimizedMaterials[i];
            }

            meshRenderer.sharedMaterials = optimizedMaterials;
        }
    }

    private Vector3 CleanUpSmallValues(Vector3 value, float threshold = 1e-6f)
    {
        value.x = value.x < threshold && value.x > -threshold ? 0 : value.x;
        value.y = value.y < threshold && value.y > -threshold ? 0 : value.y;
        value.z = value.z < threshold && value.z > -threshold ? 0 : value.z;
        return value;
    }

    private Dictionary<Transform, Transform> FindMovingParent()
    {
        var nonMovingTransforms = FindAllUnmovingTransforms();
        var result = new Dictionary<Transform, Transform>();
        foreach (var transform in transform.GetAllDescendants())
        {
            var movingParent = transform;
            while (nonMovingTransforms.Contains(movingParent))
            {
                movingParent = movingParent.parent;
            }
            result[transform] = movingParent;
        }
        return result;
    }
    
    private int GetNewBoneIDFromTransform(List<Transform> bones, Dictionary<Transform, int> boneMap, List<Matrix4x4> bindPoses, Transform toMatch)
    {
        if (boneMap.TryGetValue(toMatch, out int index))
            return index;
        if (DeleteUnusedGameObjects)
            toMatch = movingParentMap[toMatch];
        foreach (var bone in transform.GetAllDescendants())
        {
            if (bone == toMatch)
            {
                bones.Add(bone);
                bindPoses.Add(bone.worldToLocalMatrix);
                boneMap[bone] = bones.Count - 1;
                return bones.Count - 1;
            }
        }
        bones.Add(transform);
        bindPoses.Add(transform.localToWorldMatrix);
        boneMap[transform] = bones.Count - 1;
        return bones.Count - 1;
    }

    private Dictionary<string, HashSet<string>> cache_SameAnimatedPropertiesOnMergedMesh = null;
    private HashSet<string> GetSameAnimatedPropertiesOnMergedMesh(string path)
    {
        if (cache_SameAnimatedPropertiesOnMergedMesh == null) {
            cache_SameAnimatedPropertiesOnMergedMesh = new Dictionary<string, HashSet<string>>();
        }
        if (cache_SameAnimatedPropertiesOnMergedMesh.TryGetValue(path, out var result)) {
            return result;
        }
        return cache_SameAnimatedPropertiesOnMergedMesh[path] = new HashSet<string>();
    }

    private void CombineSkinnedMeshes()
    {
        var avDescriptor = GetComponent<VRCAvatarDescriptor>();
        var combinableMeshList = FindPossibleSkinnedMeshMerges();
        oldPathToMergedPaths.Clear();
        oldPathToMergedPath.Clear();
        var exclusions = GetAllExcludedTransforms();
        movingParentMap = FindMovingParent();
        materialSlotRemap = new Dictionary<(string, int), (string, int)>();
        animatedMaterialProperties = FindAllAnimatedMaterialProperties();
        fusedAnimatedMaterialProperties = animatedMaterialProperties.ToDictionary(kvp => kvp.Key, kvp => new HashSet<string>(kvp.Value));
        var combinableSkinnedMeshList = combinableMeshList
            .Select(l => l.Select(m => m as SkinnedMeshRenderer).Where(m => m != null).ToList())
            .Where(l => l.Count > 0)
            .Where(l => l[0].sharedMesh != null)
            .Where(l => l.All(m => !exclusions.Contains(m.transform)))
            .ToArray();
        List<(Transform bone, Vector3 scale)> allBonesAndParentsWithOriginalScale = combinableSkinnedMeshList
            .SelectMany(l => l).SelectMany(smr => smr.bones).Where(b => b != null).Distinct()
            .Intersect(FindAllTransformsWithScaleAnimation())
            .Select(bone => (bone, bone.localScale)).ToList();
        allBonesAndParentsWithOriginalScale.ForEach(pair => pair.bone.localScale = Vector3.one);
        var originalRootPosition = transform.position;
        var originalRootRotation = transform.rotation;
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        int totalMeshCount = combinableSkinnedMeshList.Sum(l => l.Count);
        int currentMeshCount = 0;
        for (int combinedMeshID = 0; combinedMeshID < combinableSkinnedMeshList.Length; combinedMeshID++)
        {
            var combinableSkinnedMeshes = combinableSkinnedMeshList[combinedMeshID];

            var basicMergedMeshes = new List<List<Renderer>>();
            foreach (var renderer in combinableSkinnedMeshes)
            {
                bool foundMatch = false;
                foreach (var subList in basicMergedMeshes)
                {
                    if (CanCombineRendererWithBasicMerge(subList, renderer, false))
                    {
                        subList.Add(renderer);
                        foundMatch = true;
                        break;
                    }
                }
                if (!foundMatch)
                {
                    basicMergedMeshes.Add(new List<Renderer> { renderer });
                }
            }

            Profiler.StartSection("CombineMeshData");

            int totalVertexCount = combinableSkinnedMeshes.Sum(m => m.sharedMesh.vertexCount);

            var targetBones = new List<Transform>();
            var targetBoneMap = new Dictionary<Transform, int>();
            var hasUvSet = new bool[8] {
                true,
                combinableSkinnedMeshes.Any(m => m.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord1)),
                combinableSkinnedMeshes.Any(m => m.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord2)),
                combinableSkinnedMeshes.Any(m => m.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord3)),
                combinableSkinnedMeshes.Any(m => m.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord4)),
                combinableSkinnedMeshes.Any(m => m.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord5)),
                combinableSkinnedMeshes.Any(m => m.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord6)),
                combinableSkinnedMeshes.Any(m => m.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord7)),
            };
            var targetUv = Enumerable.Range(0, 8).Select(i => hasUvSet[i] ? new List<Vector4>(totalVertexCount) : null).ToArray();
            bool useColor32 = !combinableSkinnedMeshes.Any(m => m.sharedMesh.HasVertexAttribute(VertexAttribute.Color)
                && m.sharedMesh.GetVertexAttributeFormat(VertexAttribute.Color) != VertexAttributeFormat.UNorm8);
            var targetColor = new List<Color>(useColor32 ? 0 : totalVertexCount);
            var targetColor32 = new List<Color32>(useColor32 ? totalVertexCount : 0);
            var targetVertices = new List<Vector3>(totalVertexCount);
            var targetIndices = new List<int[]>();
            var targetTopology = new List<MeshTopology>();
            var targetNormals = new List<Vector3>(totalVertexCount);
            var targetTangents = new List<Vector4>(totalVertexCount);
            var targetWeights = new List<BoneWeight>(totalVertexCount);
            var targetBindPoses = new List<Matrix4x4>();
            var sourceToWorld = new List<Matrix4x4>(totalVertexCount);
            var targetBounds = combinableSkinnedMeshes[0].localBounds;
            var targetRootBone = combinableSkinnedMeshes[0].rootBone == null ? combinableSkinnedMeshes[0].transform : combinableSkinnedMeshes[0].rootBone;

            // if NaNimation is enabled check if target root bone is Head bone or a child of Head and if so reassign it to the Hip bone
            // we do this since NaNimation disables Update when Offscreen and the head gets scaled down locally
            // without this fix the merged mesh would disappear locally
            if (MergeSkinnedMeshesWithNaNimation && basicMergedMeshes.Count > 1)
            {
                var animator = GetComponent<Animator>();
                if (animator != null && animator.isHuman)
                {
                    var headBone = animator.GetBoneTransform(HumanBodyBones.Head);
                    if (headBone != null && (targetRootBone == headBone || targetRootBone.IsChildOf(headBone)))
                    {
                        targetRootBone = animator.GetBoneTransform(HumanBodyBones.Hips);
                        if (targetRootBone == null)
                        {
                            targetRootBone = transform;
                        }
                    }
                }
            }

            var toLocal = targetRootBone.worldToLocalMatrix;

            targetBones.Add(combinableSkinnedMeshes[0].transform);
            targetBoneMap[combinableSkinnedMeshes[0].transform] = targetBones.Count - 1;
            targetBindPoses.Add(combinableSkinnedMeshes[0].transform.worldToLocalMatrix);

            string newMeshName = combinableSkinnedMeshes[0].name;
            string newPath = GetPathToRoot(combinableSkinnedMeshes[0]);

            var basicMergedMeshesList = basicMergedMeshes.SelectMany(list => list.Cast<SkinnedMeshRenderer>()).ToList();
            var mergedMeshPaths = basicMergedMeshes.Select(list => list.Select(r => GetPathToRoot(r)).ToList()).ToList();
            basicMergedMeshesList.ForEach(r => oldPathToMergedPaths[GetPathToRoot(r)] = mergedMeshPaths);
            basicMergedMeshesList.ForEach(r => oldPathToMergedPath[GetPathToRoot(r)] = newPath);

            foreach (SkinnedMeshRenderer skinnedMesh in basicMergedMeshesList)
            {
                DisplayProgressBar($"Combining mesh ({++currentMeshCount}/{totalMeshCount}) {skinnedMesh.name}");

                var blobMeshID = basicMergedMeshes.FindIndex(blob => blob.Contains(skinnedMesh));
                var currentMeshPath = GetPathToRoot(skinnedMesh);
                var mesh = skinnedMesh.sharedMesh;
                var bindPoseIDMap = new Dictionary<int, int>();
                var indexOffset = targetVertices.Count;
                var sourceVertices = mesh.vertices;
                var sourceUv = mesh.uv;
                var sourceNormals = mesh.normals;
                var sourceTangents = mesh.tangents;
                var sourceWeights = mesh.boneWeights;
                var rootBone = skinnedMesh.rootBone == null ? skinnedMesh.transform : skinnedMesh.rootBone;
                var sourceBones = skinnedMesh.bones;
                for (int i = 0; i < sourceBones.Length; i++)
                {
                    if (sourceBones[i] == null)
                        sourceBones[i] = rootBone;
                }
                var sourceBindPoses = mesh.bindposes;
                var bindPoseCount = sourceBindPoses.Length;
                if (sourceBones.Length != bindPoseCount)
                {
                    Debug.LogWarning($"Bone count ({sourceBones.Length}) does not match bind pose count ({bindPoseCount}) on {skinnedMesh.name}");
                    bindPoseCount = Math.Min(sourceBones.Length, bindPoseCount);
                }
                var toWorldArray = Enumerable.Range(0, bindPoseCount).Select(i =>
                    sourceBones[i].localToWorldMatrix * sourceBindPoses[i]
                    ).ToArray();
                var aabb = skinnedMesh.localBounds;
                var m = toLocal * rootBone.localToWorldMatrix;
                targetBounds.Encapsulate(m.MultiplyPoint3x4(aabb.extents.Multiply(1, 1, 1) + aabb.center));
                targetBounds.Encapsulate(m.MultiplyPoint3x4(aabb.extents.Multiply(1, 1, -1) + aabb.center));
                targetBounds.Encapsulate(m.MultiplyPoint3x4(aabb.extents.Multiply(1, -1, 1) + aabb.center));
                targetBounds.Encapsulate(m.MultiplyPoint3x4(aabb.extents.Multiply(1, -1, -1) + aabb.center));
                targetBounds.Encapsulate(m.MultiplyPoint3x4(aabb.extents.Multiply(-1, 1, 1) + aabb.center));
                targetBounds.Encapsulate(m.MultiplyPoint3x4(aabb.extents.Multiply(-1, 1, -1) + aabb.center));
                targetBounds.Encapsulate(m.MultiplyPoint3x4(aabb.extents.Multiply(-1, -1, 1) + aabb.center));
                targetBounds.Encapsulate(m.MultiplyPoint3x4(aabb.extents.Multiply(-1, -1, -1) + aabb.center));
                Transform NaNimationBone = null;
                int NaNimationBoneIndex = -1;
                if (MergeSkinnedMeshesWithNaNimation && basicMergedMeshes.Count > 1
                        && FindAllRendererTogglePaths().Contains(currentMeshPath)
                        && CanUseNaNimationOnMesh(currentMeshPath)) {
                    NaNimationBone = new GameObject("NaNimationBone").transform;
                    var pathToRoot = currentMeshPath.Replace('/', '_');
                    var siblingNames = new HashSet<string>(transform.Cast<Transform>().Select(t => t.name));
                    var nameCandidate = "NaNimation " + pathToRoot;
                    int i = 1;
                    while (siblingNames.Contains(nameCandidate)) {
                        nameCandidate = "NaNimation " + pathToRoot + " " + i++;
                    }
                    NaNimationBone.name = nameCandidate;
                    NaNimationBone.parent = transform;
                    NaNimationBone.localPosition = Vector3.zero;
                    NaNimationBone.localRotation = Quaternion.identity;
                    NaNimationBone.localScale = Vector3.one;
                    targetBones.Add(NaNimationBone);
                    NaNimationBoneIndex = targetBoneMap[NaNimationBone] = targetBones.Count - 1;
                    targetBindPoses.Add(NaNimationBone.worldToLocalMatrix);
                    string key = "NaNimation";
                    if (MergeSkinnedMeshesWithShaderToggle) {
                        key += $";{blobMeshID};{newPath}";
                    }
                    AddAnimationPathChange((currentMeshPath, "m_IsActive", typeof(GameObject)),
                        (GetPathToRoot(NaNimationBone), key, typeof(Transform)));
                    AddAnimationPathChange((currentMeshPath, "m_Enabled", typeof(SkinnedMeshRenderer)),
                        (GetPathToRoot(NaNimationBone), key, typeof(Transform)));
                    var curveBinding = EditorCurveBinding.FloatCurve(newPath, typeof(SkinnedMeshRenderer), "m_UpdateWhenOffscreen");
                    constantAnimatedValuesToAdd[curveBinding] = 0f;
                    targetBounds.Encapsulate(toLocal.MultiplyPoint3x4(avDescriptor.ViewPosition + Vector3.forward * 0.3f + Vector3.up * 0.2f));
                    targetBounds.Encapsulate(toLocal.MultiplyPoint3x4(avDescriptor.ViewPosition + Vector3.forward * 0.3f - Vector3.up * 0.2f));
                    targetBounds.Encapsulate(toLocal.MultiplyPoint3x4(avDescriptor.ViewPosition + Vector3.forward * 0.3f + Vector3.right * 0.2f));
                    targetBounds.Encapsulate(toLocal.MultiplyPoint3x4(avDescriptor.ViewPosition + Vector3.forward * 0.3f - Vector3.right * 0.2f));
                } else if (basicMergedMeshes.Count > 1 && MergeSkinnedMeshesWithShaderToggle) {
                    AddAnimationPathChange((currentMeshPath, "m_IsActive", typeof(GameObject)),
                            (newPath, "material._IsActiveMesh" + blobMeshID, typeof(SkinnedMeshRenderer)));
                    AddAnimationPathChange((currentMeshPath, "m_Enabled", typeof(SkinnedMeshRenderer)),
                            (newPath, "material._IsActiveMesh" + blobMeshID, typeof(SkinnedMeshRenderer)));
                }
                
                if (sourceWeights.Length != sourceVertices.Length || bindPoseCount == 0)
                {
                    var defaultWeight = new BoneWeight
                    {
                        boneIndex0 = 0,
                        boneIndex1 = 0,
                        boneIndex2 = 0,
                        boneIndex3 = 0,
                        weight0 = 1,
                        weight1 = 0,
                        weight2 = 0,
                        weight3 = 0
                    };
                    sourceWeights = Enumerable.Repeat(defaultWeight, sourceVertices.Length).ToArray();
                    sourceBones = new Transform[1] { rootBone.transform };
                    toWorldArray = new Matrix4x4[1] { rootBone.transform.localToWorldMatrix };
                    keepTransforms.Add(rootBone.transform);
                    bindPoseCount = 1;
                }

                for (int i = 1; i < 8; i++)
                {
                    if (!hasUvSet[i])
                        continue;
                    var uvs = new List<Vector4>();
                    mesh.GetUVs(i, uvs);
                    targetUv[i].AddRange(uvs.Count == sourceVertices.Length ? uvs : Enumerable.Repeat(Vector4.zero, sourceVertices.Length));
                }

                if (mesh.HasVertexAttribute(VertexAttribute.Color))
                {
                    if (useColor32) {
                        targetColor32.AddRange(mesh.colors32);
                    } else {
                        targetColor.AddRange(mesh.colors);
                    }
                }
                else
                {
                    if (useColor32) {
                        targetColor32.AddRange(Enumerable.Repeat(new Color32(255, 255, 255, 255), sourceVertices.Length));
                    } else {
                        targetColor.AddRange(Enumerable.Repeat(Color.white, sourceVertices.Length));
                    }
                }

                sourceUv = sourceUv.Length != sourceVertices.Length ? new Vector2[sourceVertices.Length] : sourceUv;
                sourceNormals = sourceNormals.Length != sourceVertices.Length ? new Vector3[sourceVertices.Length] : sourceNormals;
                sourceTangents = sourceTangents.Length != sourceVertices.Length ? new Vector4[sourceVertices.Length] : sourceTangents;

                if (!blendShapesToBake.TryGetValue(skinnedMesh, out var blendShapeIDs))
                {
                    blendShapeIDs = new List<int>();
                }

                foreach (int blendShapeID in blendShapeIDs)
                {
                    var weight = Mathf.Clamp(skinnedMesh.GetBlendShapeWeight(blendShapeID) / 100f, 0, 1);
                    var deltaVertices = new Vector3[sourceVertices.Length];
                    var deltaNormals = new Vector3[sourceVertices.Length];
                    var deltaTangents = new Vector3[sourceVertices.Length];
                    mesh.GetBlendShapeFrameVertices(blendShapeID, 0, deltaVertices, deltaNormals, deltaTangents);
                    for (int i = 0; i < sourceVertices.Length; i++)
                    {
                        sourceVertices[i] += deltaVertices[i] * weight;
                        sourceNormals[i] += deltaNormals[i] * weight;
                        sourceTangents[i] += (Vector4)(deltaTangents[i] * weight);
                    }
                }

                for (int vertIndex = 0; vertIndex < sourceVertices.Length; vertIndex++)
                {
                    targetUv[0].Add(new Vector4(sourceUv[vertIndex].x, sourceUv[vertIndex].y, blobMeshID << 12, 0));
                    var boneWeight = sourceWeights[vertIndex];
                    boneWeight.boneIndex0 = boneWeight.boneIndex0 >= bindPoseCount ? 0 : boneWeight.boneIndex0;
                    boneWeight.boneIndex1 = boneWeight.boneIndex1 >= bindPoseCount ? 0 : boneWeight.boneIndex1;
                    boneWeight.boneIndex2 = boneWeight.boneIndex2 >= bindPoseCount ? 0 : boneWeight.boneIndex2;
                    boneWeight.boneIndex3 = boneWeight.boneIndex3 >= bindPoseCount ? 0 : boneWeight.boneIndex3;
                    Matrix4x4 toWorld = toWorldArray[boneWeight.boneIndex0];
                    if (boneWeight.weight0 != 1)
                    {
                        AddWeighted(ref toWorld, toWorldArray[boneWeight.boneIndex0], boneWeight.weight0 - 1);
                        AddWeighted(ref toWorld, toWorldArray[boneWeight.boneIndex1], boneWeight.weight1);
                        AddWeighted(ref toWorld, toWorldArray[boneWeight.boneIndex2], boneWeight.weight2);
                        AddWeighted(ref toWorld, toWorldArray[boneWeight.boneIndex3], boneWeight.weight3);
                    }
                    sourceToWorld.Add(toWorld);
                    var vertex = sourceVertices[vertIndex];
                    var normal = sourceNormals[vertIndex];
                    var tangent = (Vector3)sourceTangents[vertIndex];
                    targetVertices.Add(toWorld.MultiplyPoint3x4(vertex));
                    targetNormals.Add(toWorld.MultiplyVector(normal));
                    var t = toWorld.MultiplyVector(tangent);
                    targetTangents.Add(new Vector4(t.x, t.y, t.z, sourceTangents[vertIndex].w));
                    int GetNewBoneIndex(int oldIndex)
                    {
                        if (!bindPoseIDMap.TryGetValue(oldIndex, out int newIndex))
                        {
                            newIndex = GetNewBoneIDFromTransform(targetBones, targetBoneMap, targetBindPoses, sourceBones[oldIndex]);
                            bindPoseIDMap[oldIndex] = newIndex;
                        }
                        return newIndex;
                    }
                    boneWeight.boneIndex0 = GetNewBoneIndex(boneWeight.boneIndex0);
                    boneWeight.boneIndex1 = GetNewBoneIndex(boneWeight.boneIndex1);
                    boneWeight.boneIndex2 = GetNewBoneIndex(boneWeight.boneIndex2);
                    boneWeight.boneIndex3 = GetNewBoneIndex(boneWeight.boneIndex3);
                    if (NaNimationBoneIndex != -1) {
                        var sum = boneWeight.weight0 + boneWeight.weight1 + boneWeight.weight2;
                        sum = sum == 0 ? 1 : sum;
                        boneWeight.weight0 /= sum;
                        boneWeight.weight1 /= sum;
                        boneWeight.weight2 /= sum;
                        boneWeight.weight3 = 0;
                        if (boneWeight.weight1 == 0) {
                            boneWeight.boneIndex1 = NaNimationBoneIndex;
                            boneWeight.weight1 = 1e-35f;
                        } else if (boneWeight.weight2 == 0) {
                            boneWeight.boneIndex2 = NaNimationBoneIndex;
                            boneWeight.weight2 = 1e-35f;
                        } else {
                            boneWeight.boneIndex3 = NaNimationBoneIndex;
                            boneWeight.weight3 = 1e-35f;
                        }
                    }
                    targetWeights.Add(boneWeight);
                }
                
                for (var matID = 0; matID < skinnedMesh.sharedMaterials.Length; matID++)
                {
                    int clampedSubMeshID = Math.Min(matID, mesh.subMeshCount - 1);
                    int[] indices = mesh.GetIndices(clampedSubMeshID);
                    for (uint i = 0; i < indices.Length; i++)
                    {
                        indices[i] += indexOffset;
                    }
                    materialSlotRemap[(newPath, targetIndices.Count)] = (GetPathToRoot(skinnedMesh), matID);
                    targetIndices.Add(indices);
                    targetTopology.Add(mesh.GetTopology(clampedSubMeshID));
                }
            }
            Profiler.EndSection();

            var blendShapeWeights = new Dictionary<string, float>();

            var combinedMesh = new Mesh();
            combinedMesh.indexFormat = targetVertices.Count >= 65536
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            combinedMesh.SetVertices(targetVertices);
            combinedMesh.bindposes = targetBindPoses.ToArray();
            combinedMesh.SetBoneWeights(targetWeights.ToArray());
            if (!useColor32 && targetColor.Any(c => !c.Equals(Color.white)))
            {
                combinedMesh.colors = targetColor.ToArray();
            }
            else if (useColor32 && targetColor32.Any(c => !c.Equals(new Color32(255, 255, 255, 255))))
            {
                combinedMesh.colors32 = targetColor32.ToArray();
            }
            for (int i = 0; i < 8; i++)
            {
                if (hasUvSet[i] && targetUv[i].Any(uv => !uv.Equals(Vector4.zero)))
                {
                    combinedMesh.SetUVs(i, targetUv[i]);
                }
            }
            combinedMesh.bounds = combinableSkinnedMeshes[0].sharedMesh.bounds;
            combinedMesh.SetNormals(targetNormals);
            combinedMesh.SetTangents(targetTangents);
            combinedMesh.subMeshCount = targetIndices.Count;
            combinedMesh.name = newMeshName;
            for (int i = 0; i < targetIndices.Count; i++)
            {
                combinedMesh.SetIndices(targetIndices[i], targetTopology[i], i);
            }

            Profiler.StartSection("CopyCombinedMeshBlendShapes");
            var usedBlendShapeNames = new HashSet<string>();
            var blendShapeMeshIDtoNewName = new Dictionary<(int meshID, int blendShapeID), string>();
            var combinableMeshPaths = new HashSet<string>(basicMergedMeshesList.Select(s => GetPathToRoot(s)));
            var meshPathToID = basicMergedMeshesList.Select((s, i) => (GetPathToRoot(s), i)).ToDictionary(s => s.Item1, s => s.Item2);
            var usedBlendShapesInCombinedMesh = new HashSet<string>(
                usedBlendShapes.Where(s => combinableMeshPaths.Contains(s.Substring(0, s.IndexOf("/blendShape.")))));
            var allMergedBlendShapes = new List<List<(string blendshape, float weight)>>();
            if (MergeSameRatioBlendShapes)
            {
                allMergedBlendShapes.AddRange(FindMergeableBlendShapes(basicMergedMeshesList));
                var usedBlendShapesInMergedBlobs = new HashSet<string>(allMergedBlendShapes.SelectMany(s => s).Select(s => s.blendshape));
                allMergedBlendShapes.AddRange(usedBlendShapesInCombinedMesh.Where(s => !usedBlendShapesInMergedBlobs.Contains(s)).Select(s => new List<(string blendshape, float weight)> { (s, 1) }));
            }
            else
            {
                allMergedBlendShapes.AddRange(usedBlendShapesInCombinedMesh.Select(s => new List<(string blendshape, float weight)> { (s, 1) }));
            }
            var vertexOffset = new List<int>() {0};
            for (int i = 0; i < basicMergedMeshesList.Count - 1; i++)
            {
                vertexOffset.Add(vertexOffset[i] + basicMergedMeshesList[i].sharedMesh.vertexCount);
            }
            int combinedMeshVertexCount = combinedMesh.vertexCount;
            foreach (var mergedBlendShapes in allMergedBlendShapes)
            {
                if (mergedBlendShapes.Count == 1)
                {
                    var path = mergedBlendShapes[0].blendshape.Substring(0, mergedBlendShapes[0].blendshape.IndexOf("/blendShape."));
                    var skinnedMesh = GetTransformFromPath(path).GetComponent<SkinnedMeshRenderer>();
                    var mesh = skinnedMesh.sharedMesh;
                    var oldName = mergedBlendShapes[0].blendshape.Substring(path.Length + 12);
                    var name = GenerateUniqueName(oldName, usedBlendShapeNames);
                    var meshID = meshPathToID[path];
                    var blendShapeID = mesh.GetBlendShapeIndex(oldName);
                    if (blendShapeID == -1)
                        continue;
                    blendShapeMeshIDtoNewName[(meshID, blendShapeID)] = name;
                    blendShapeWeights[name] = skinnedMesh.GetBlendShapeWeight(blendShapeID);
                    AddAnimationPathChange(
                        (path, "blendShape." + oldName, typeof(SkinnedMeshRenderer)),
                        (newPath, "blendShape." + name, typeof(SkinnedMeshRenderer)));
                    for (int j = 0; j < mesh.GetBlendShapeFrameCount(blendShapeID); j++)
                    {
                        int meshVertexCount = mesh.vertexCount;
                        var sourceDeltaVertices = new Vector3[meshVertexCount];
                        var sourceDeltaNormals = new Vector3[meshVertexCount];
                        var sourceDeltaTangents = new Vector3[meshVertexCount];
                        mesh.GetBlendShapeFrameVertices(blendShapeID, j, sourceDeltaVertices, sourceDeltaNormals, sourceDeltaTangents);
                        var targetDeltaVertices = new Vector3[combinedMeshVertexCount];
                        var targetDeltaNormals = new Vector3[combinedMeshVertexCount];
                        var targetDeltaTangents = new Vector3[combinedMeshVertexCount];
                        for (int k = 0; k < meshVertexCount; k++)
                        {
                            int vertIndex = k + vertexOffset[meshID];
                            var toWorld = sourceToWorld[vertIndex];
                            targetDeltaVertices[vertIndex] = CleanUpSmallValues(toWorld.MultiplyVector(sourceDeltaVertices[k]));
                            targetDeltaNormals[vertIndex] = CleanUpSmallValues(toWorld.MultiplyVector(sourceDeltaNormals[k]));
                            targetDeltaTangents[vertIndex] = CleanUpSmallValues(toWorld.MultiplyVector(sourceDeltaTangents[k]));
                        }
                        var weight = mesh.GetBlendShapeFrameWeight(blendShapeID, j);
                        combinedMesh.AddBlendShapeFrame(name, weight, targetDeltaVertices, targetDeltaNormals, targetDeltaTangents);
                    }
                }
                else
                {
                    var oldPath = mergedBlendShapes[0].blendshape.Substring(0, mergedBlendShapes[0].blendshape.IndexOf("/blendShape."));
                    var oldName = mergedBlendShapes[0].blendshape.Substring(mergedBlendShapes[0].blendshape.IndexOf("/blendShape.") + 12);
                    var name = GenerateUniqueName(oldName, usedBlendShapeNames);
                    AddAnimationPathChange(
                        (oldPath, "blendShape." + oldName, typeof(SkinnedMeshRenderer)),
                        (newPath, "blendShape." + name, typeof(SkinnedMeshRenderer)));
                    var targetDeltaVertices = new Vector3[combinedMeshVertexCount];
                    var targetDeltaNormals = new Vector3[combinedMeshVertexCount];
                    var targetDeltaTangents = new Vector3[combinedMeshVertexCount];
                    bool first = true;
                    foreach (var toMerge in mergedBlendShapes)
                    {
                        var path = toMerge.blendshape.Substring(0, toMerge.blendshape.IndexOf("/blendShape."));
                        var skinnedMesh = GetTransformFromPath(path).GetComponent<SkinnedMeshRenderer>();
                        var mesh = skinnedMesh.sharedMesh;
                        var blendShapeID = mesh.GetBlendShapeIndex(toMerge.blendshape.Substring(path.Length + 12));
                        if (blendShapeID == -1)
                            continue;
                        var meshID = meshPathToID[path];
                        blendShapeMeshIDtoNewName[(meshID, blendShapeID)] = name;
                        if (first)
                        {
                            blendShapeWeights[name] = skinnedMesh.GetBlendShapeWeight(blendShapeID);
                            first = false;
                        }
                        int meshVertexCount = mesh.vertexCount;
                        var sourceDeltaVertices = new Vector3[meshVertexCount];
                        var sourceDeltaNormals = new Vector3[meshVertexCount];
                        var sourceDeltaTangents = new Vector3[meshVertexCount];
                        mesh.GetBlendShapeFrameVertices(blendShapeID, 0, sourceDeltaVertices, sourceDeltaNormals, sourceDeltaTangents);
                        for (int k = 0; k < meshVertexCount; k++)
                        {
                            int vertIndex = k + vertexOffset[meshID];
                            var toWorld = sourceToWorld[vertIndex];
                            targetDeltaVertices[vertIndex] += CleanUpSmallValues(toWorld.MultiplyVector(sourceDeltaVertices[k]) * toMerge.weight);
                            targetDeltaNormals[vertIndex] += CleanUpSmallValues(toWorld.MultiplyVector(sourceDeltaNormals[k]) * toMerge.weight);
                            targetDeltaTangents[vertIndex] += CleanUpSmallValues(toWorld.MultiplyVector(sourceDeltaTangents[k]) * toMerge.weight);
                        }
                    }
                    combinedMesh.AddBlendShapeFrame(name, 100, targetDeltaVertices, targetDeltaNormals, targetDeltaTangents);
                }
            }
            Profiler.EndSection();
            
            var meshRenderer = combinableSkinnedMeshes[0];
            meshRenderer.rootBone = targetRootBone;
            var materials = basicMergedMeshesList.SelectMany(r => r.sharedMaterials).ToArray();

            if (avDescriptor.customEyeLookSettings.eyelidType == VRCAvatarDescriptor.EyelidType.Blendshapes
                && avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh != null)
            {
                var eyeLookMeshRenderer = avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh;
                var ids = avDescriptor.customEyeLookSettings.eyelidsBlendshapes;
                for (int i = 0; i < ids.Length; i++)
                {
                    if (ids[i] < 0)
                        continue;
                    for (int meshID = 0; meshID < basicMergedMeshesList.Count; meshID++)
                    {
                        if (basicMergedMeshesList[meshID] == eyeLookMeshRenderer)
                        {
                            avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh = meshRenderer;
                            ids[i] = combinedMesh.GetBlendShapeIndex(blendShapeMeshIDtoNewName[(meshID, ids[i])]);
                        }
                    }
                }
                avDescriptor.customEyeLookSettings.eyelidsBlendshapes = ids;
            }

            for (int meshID = 0; meshID < basicMergedMeshesList.Count; meshID++)
            {
                var oldVisemeMesh = basicMergedMeshesList[meshID];
                if (avDescriptor.VisemeSkinnedMesh == oldVisemeMesh)
                {
                    avDescriptor.VisemeSkinnedMesh = meshRenderer;
                    string CalculateNewBlendShapeName(string blendShapeName) {
                        var blendShapeID = oldVisemeMesh.sharedMesh.GetBlendShapeIndex(blendShapeName ?? "");
                        return blendShapeMeshIDtoNewName.TryGetValue((meshID, blendShapeID), out string newName)
                            ? newName : $"MISSING \"{blendShapeName}\"";
                    }
                    avDescriptor.VisemeBlendShapes = avDescriptor.VisemeBlendShapes.Select(CalculateNewBlendShapeName).ToArray();
                    avDescriptor.MouthOpenBlendShapeName = CalculateNewBlendShapeName(avDescriptor.MouthOpenBlendShapeName);
                }
            }

            var sameAnimatedProperties = GetSameAnimatedPropertiesOnMergedMesh(newPath);
            if (basicMergedMeshes.Count > 1 && MergeSkinnedMeshesWithShaderToggle) {
                var pathA = GetPathToRoot(basicMergedMeshes[0][0]);
                sameAnimatedProperties.UnionWith(FindSameAnimatedMaterialProperties(pathA, GetPathToRoot(basicMergedMeshes[1][0])));
                for (int blobMeshID = 2; blobMeshID < basicMergedMeshes.Count; blobMeshID++) {
                    sameAnimatedProperties.IntersectWith(FindSameAnimatedMaterialProperties(pathA, GetPathToRoot(basicMergedMeshes[blobMeshID][0])));
                }
            }

            for (int blobMeshID = 0; blobMeshID < basicMergedMeshes.Count && basicMergedMeshes.Count > 1 && MergeSkinnedMeshesWithShaderToggle; blobMeshID++) {
                var skinnedMesh = basicMergedMeshes[blobMeshID][0];
                var oldPath = GetPathToRoot(skinnedMesh);
                var properties = new MaterialPropertyBlock();
                if (meshRenderer.HasPropertyBlock())
                    meshRenderer.GetPropertyBlock(properties);
                bool isActive = GetRendererDefaultEnabledState(skinnedMesh);
                properties.SetFloat($"_IsActiveMesh{blobMeshID}", isActive ? 1f : 0f);
                properties.SetInt("d4rkAvatarOptimizer_CombinedMeshCount", basicMergedMeshes.Count);
                var animatedMaterialPropertiesToAdd = new List<string>();
                if (animatedMaterialProperties.TryGetValue(oldPath, out var animatedProperties)) {
                    foreach (var animPropName in animatedProperties) {
                        var propName = animPropName;
                        bool isVector = propName.EndsWith(".x");
                        bool isColor = propName.EndsWith(".r");
                        if (isVector || isColor) {
                            propName = propName.Substring(0, propName.Length - 2);
                        } else if (propName[propName.Length - 2] == '.') {
                            continue;
                        }
                        if (sameAnimatedProperties.Contains(animPropName)) {
                            continue;
                        }
                        for (int mID = 0; mID < basicMergedMeshes.Count; mID++) {
                            string newPropertyName = $"material.d4rkAvatarOptimizer{propName}_ArrayIndex{mID}";
                            string path = GetPathToRoot(basicMergedMeshes[mID][0]);
                            var vectorEnd = isVector ? new [] { ".x", ".y", ".z", ".w" } : isColor ? new [] { ".r", ".g", ".b", ".a" } : new [] { "" };
                            foreach (var component in vectorEnd) {
                                AddAnimationPathChange(
                                    (path, "material." + propName + component, typeof(SkinnedMeshRenderer)),
                                    (newPath, newPropertyName + component, typeof(SkinnedMeshRenderer)));
                            }
                        }
                        animatedMaterialPropertiesToAdd.Add(animPropName);
                    }
                }
                if (animatedMaterialPropertiesToAdd.Count > 0) {
                    if (!fusedAnimatedMaterialProperties.TryGetValue(newPath, out animatedProperties)) {
                        fusedAnimatedMaterialProperties[newPath] = animatedProperties = new HashSet<string>();
                    }
                    animatedProperties.UnionWith(animatedMaterialPropertiesToAdd);
                }
                meshRenderer.SetPropertyBlock(properties);
            }

            for (int meshID = 1; meshID < combinableSkinnedMeshes.Count; meshID++)
            {
                var obj = combinableSkinnedMeshes[meshID].gameObject;
                DestroyImmediate(combinableSkinnedMeshes[meshID]);
                if (!keepTransforms.Contains(obj.transform) && (obj.transform.childCount == 0 && obj.GetNonNullComponents().Length == 1))
                    DestroyImmediate(obj);
            }

            meshRenderer.sharedMesh = combinedMesh;
            meshRenderer.sharedMaterials = materials;
            meshRenderer.bones = targetBones.ToArray();
            meshRenderer.localBounds = targetBounds;

            foreach (var blendShape in blendShapeWeights)
            {
                for (int j = 0; j < combinedMesh.blendShapeCount; j++)
                {
                    if (blendShape.Key == combinedMesh.GetBlendShapeName(j))
                    {
                        meshRenderer.SetBlendShapeWeight(j, blendShape.Value);
                        break;
                    }
                }
            }

            if (basicMergedMeshes.Count > 1)
            {
                if (MergeSkinnedMeshesSeparatedByDefaultEnabledState && !GetRendererDefaultEnabledState(meshRenderer))
                {
                    meshRenderer.gameObject.SetActive(true);
                    meshRenderer.enabled = false;
                    var curveBinding = EditorCurveBinding.FloatCurve(GetPathToRoot(meshRenderer), typeof(SkinnedMeshRenderer), "m_Enabled");
                    constantAnimatedValuesToAdd[curveBinding] = 1f;
                }
                else
                {
                    meshRenderer.gameObject.SetActive(true);
                    meshRenderer.enabled = true;
                }
            }

            Profiler.StartSection("AssetDatabase.SaveAssets()");
            AssetDatabase.SaveAssets();
            Profiler.EndSection();
        }
        allBonesAndParentsWithOriginalScale.ForEach(pair => pair.bone.localScale = pair.scale);
        transform.position = originalRootPosition;
        transform.rotation = originalRootRotation;
    }

    private HashSet<Transform> cache_GetAllExcludedTransforms;
    public HashSet<Transform> GetAllExcludedTransforms() {
        if (cache_GetAllExcludedTransforms != null)
            return cache_GetAllExcludedTransforms;
        var allExcludedTransforms = new HashSet<Transform>();
        var hardCodedExclusions = new List<string>() {
            "_VirtualLens_Root",
        }.Select(s => GetTransformFromPath(s)).ToList();
        foreach (var excludedTransform in ExcludeTransforms.Concat(hardCodedExclusions)) {
            if (excludedTransform == null)
                continue;
            allExcludedTransforms.Add(excludedTransform);
            allExcludedTransforms.UnionWith(excludedTransform.GetAllDescendants());
        }
        return cache_GetAllExcludedTransforms = allExcludedTransforms;
    }

    private HashSet<string> cache_GetAllExcludedTransformPaths;
    public HashSet<string> GetAllExcludedTransformPaths() {
        if (cache_GetAllExcludedTransformPaths != null)
            return cache_GetAllExcludedTransformPaths;
        return cache_GetAllExcludedTransformPaths = new HashSet<string>(GetAllExcludedTransforms().Select(t => GetPathToRoot(t)));
    }
    
    HashSet<Transform> FindReferencedTransforms(Component component)
    {
        using (var serializedObject = new SerializedObject(component))
        {
            var visitedIds = new HashSet<long>();
            var iterator = serializedObject.GetIterator();
            var referencedTransforms = new HashSet<Transform>();
            bool enterChildren = true;
            while (iterator.Next(enterChildren))
            {
                enterChildren = true;
                if (iterator.propertyType == SerializedPropertyType.ObjectReference && iterator.objectReferenceValue != null)
                {
                    if (iterator.objectReferenceValue is Transform transform)
                    {
                        referencedTransforms.Add(transform);
                    }
                }
                else if (iterator.propertyType == SerializedPropertyType.ManagedReference)
                {
                    #if UNITY_2022_1_OR_NEWER
                        if (!visitedIds.Add(iterator.managedReferenceId))
                        {
                            enterChildren = false;
                        }
                    #else
                        enterChildren = false;
                    #endif
                }
            }
            return referencedTransforms;
        }
    }

    private void DestroyEditorOnlyGameObjects()
    {
        var stack = new Stack<Transform>();
        stack.Push(transform);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.gameObject.CompareTag("EditorOnly"))
            {
                DestroyImmediate(current.gameObject);
                continue;
            }
            foreach (Transform child in current)
            {
                stack.Push(child);
            }
        }
    }

    private void DestroyUnusedComponents()
    {
        if (!DeleteUnusedComponents)
            return;
        var list = FindAllUnusedComponents();
        foreach (var component in list)
        {
            if (component == null)
                continue;
            if (component is AudioSource audio)
            {
                var vrcAudioSource = audio.GetComponent<VRCSpatialAudioSource>();
                if (vrcAudioSource != null)
                {
                    DestroyImmediate(vrcAudioSource);
                }
            }
            DestroyImmediate(component);
        }
    }

    private void DestroyUnusedGameObjects()
    {
        transformFromOldPath = new Dictionary<string, Transform>();
        foreach (var transform in transform.GetAllDescendants())
        {
            transformFromOldPath[GetPathToRoot(transform)] = transform;
        }

        if (!DeleteUnusedGameObjects)
            return;

        var used = new HashSet<Transform>();

        var movingTransforms = FindAllMovingTransforms();
        used.UnionWith(movingTransforms);
        used.UnionWith(movingTransforms.Select(t => t != null ? t.parent : null));

        used.Add(transform);
        used.UnionWith(GetComponentsInChildren<Animator>(true)
            .Select(a => a.transform.Find("Armature")).Where(t => t != null));
        used.UnionWith(transform.Cast<Transform>().Where(t => t.name.StartsWithSimple("NaNimation ")));

        foreach (var contact in GetComponentsInChildren<ContactBase>(true))
        {
            used.Add(contact.GetRootTransform());
            used.Add(contact.GetRootTransform().parent);
        }

        foreach (var physBone in GetComponentsInChildren<VRCPhysBoneBase>(true))
        {
            used.Add(physBone.GetRootTransform());
            used.Add(physBone.GetRootTransform().parent);
            used.UnionWith(physBone.ignoreTransforms);
        }

        foreach (var collider in GetComponentsInChildren<VRCPhysBoneColliderBase>(true))
        {
            used.Add(collider.GetRootTransform());
            used.Add(collider.GetRootTransform().parent);
        }

        foreach (var c in GetComponentsInChildren<Component>(true).Where(c => c != null && !(c is Transform)))
        {
            used.Add(c.transform);
            if (c.GetType().Name.Contains("Constraint"))
            {
                used.Add(c.transform.parent);
            }
            used.UnionWith(FindReferencedTransforms(c));
        }

        used.UnionWith(FindAllGameObjectTogglePaths().Select(p => GetTransformFromPath(p)).Where(t => t != null));

        foreach (var exclusion in GetAllExcludedTransforms())
        {
            if (exclusion == null)
                continue;
            used.Add(exclusion);
            var current = exclusion;
            while ((current = current.parent) != null)
            {
                used.Add(current);
            }
        }

        var queue = new Queue<Transform>();
        queue.Enqueue(transform);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (Transform child in current)
            {
                queue.Enqueue(child);
            }
            if (!used.Contains(current))
            {
                foreach (var child in current.Cast<Transform>().ToArray())
                {
                    child.parent = current.parent;
                    child.name = $"{current.name}_{child.name}";
                }
                DestroyImmediate(current.gameObject);
            }
        }
    }

    private void MoveRingFingerColliderToFeet()
    {
        if (!UseRingFingerAsFootCollider)
            return;
        var avDescriptor = GetComponent<VRCAvatarDescriptor>();

        var collider = avDescriptor.collider_footL;
        collider.state = VRCAvatarDescriptor.ColliderConfig.State.Custom;
        collider.height -= collider.radius * 2f;
        var parent = new GameObject("leftFootColliderRoot");
        parent.transform.parent = collider.transform;
        parent.transform.localRotation = collider.rotation;
        parent.transform.localPosition = collider.position + collider.rotation * (-(collider.height * 0.5f) * Vector3.up);
        parent.transform.localScale = Vector3.one;
        var leaf = new GameObject("leftFootColliderLeaf");
        leaf.transform.parent = parent.transform;
        leaf.transform.localPosition = new Vector3(0, collider.height, 0);
        leaf.transform.localRotation = Quaternion.identity;
        leaf.transform.localScale = Vector3.one;
        collider.transform = leaf.transform;
        avDescriptor.collider_fingerRingL = collider;

        collider = avDescriptor.collider_footR;
        collider.state = VRCAvatarDescriptor.ColliderConfig.State.Custom;
        collider.height -= collider.radius * 2f;
        parent = new GameObject("rightFootColliderRoot");
        parent.transform.parent = collider.transform;
        parent.transform.localRotation = collider.rotation;
        parent.transform.localPosition = collider.position + collider.rotation * (-(collider.height * 0.5f) * Vector3.up);
        parent.transform.localScale = Vector3.one;
        leaf = new GameObject("rightFootColliderLeaf");
        leaf.transform.parent = parent.transform;
        leaf.transform.localPosition = new Vector3(0, collider.height, 0);
        leaf.transform.localRotation = Quaternion.identity;
        leaf.transform.localScale = Vector3.one;
        collider.transform = leaf.transform;
        avDescriptor.collider_fingerRingR = collider;

        // disable collider foldout in the inspector because it resets the collider transform
        EditorPrefs.SetBool("VRCSDK3_AvatarDescriptorEditor3_CollidersFoldout", false);
    }

    private void ConvertStaticMeshesToSkinnedMeshes()
    {
        if (!MergeStaticMeshesAsSkinned)
            return;
        var staticMeshes = gameObject.GetComponentsInChildren<MeshFilter>(true)
            .Where(f => f.sharedMesh != null && f.gameObject.GetComponent<MeshRenderer>() != null)
            .Where(f => f.gameObject.layer != 12)
            .Select(f => f.gameObject).Distinct().ToList();
        var meshesThatGetCombinedWithOtherMeshes = new HashSet<Renderer>(FindPossibleSkinnedMeshMerges().Where(l => l.Count > 1).SelectMany(l => l));

        foreach (var obj in staticMeshes)
        {
            if (!meshesThatGetCombinedWithOtherMeshes.Contains(obj.GetComponent<Renderer>()))
                continue;
            bool isActive = obj.GetComponent<MeshRenderer>().enabled;
            var mats = obj.GetComponent<MeshRenderer>().sharedMaterials;
            var lightAnchor = obj.GetComponent<MeshRenderer>().probeAnchor;
            var mesh = obj.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(obj.GetComponent<MeshFilter>());
            var skinnedMeshRenderer = obj.AddComponent<SkinnedMeshRenderer>();
            skinnedMeshRenderer.enabled = isActive;
            skinnedMeshRenderer.sharedMesh = mesh;
            skinnedMeshRenderer.sharedMaterials = mats;
            skinnedMeshRenderer.probeAnchor = lightAnchor;
            convertedMeshRendererPaths.Add(GetPathToRoot(obj));
        }
    }
#endif
}
