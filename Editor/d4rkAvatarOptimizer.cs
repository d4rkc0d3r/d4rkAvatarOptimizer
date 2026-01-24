using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using System.Text.RegularExpressions;
using Array = System.Array;
using System.IO;

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
[AddComponentMenu("d4rk Avatar Optimizer")]
public class d4rkAvatarOptimizer : MonoBehaviour, VRC.SDKBase.IEditorOnly
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
    private bool isOptimizing = false;
    #endregion

    public struct MaterialSlot
    {
        public Renderer renderer;
        public int index;
        public readonly Material material
        {
            get
            {
                if (renderer == null || index < 0)
                    return null;
                var materials = renderer.sharedMaterials;
                if (index >= materials.Length)
                    return null;
                return materials[index];
            }
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
        public override bool Equals(object obj)
        {
            if (obj is MaterialSlot other)
            {
                return renderer == other.renderer && index == other.index;
            }
            return false;
        }
        public override int GetHashCode()
        {
            return (renderer == null ? 0 : renderer.GetHashCode()) ^ index.GetHashCode();
        }
        public static bool operator ==(MaterialSlot a, MaterialSlot b) => a.Equals(b);
        public static bool operator !=(MaterialSlot a, MaterialSlot b) => !a.Equals(b);
    }

#if UNITY_EDITOR

    public void Optimize()
    {
        using var _ = new InvariantCultureScope();
        try
        {
            isOptimizing = true;
            Profiler.enabled = true;
            Profiler.Reset();
            DisplayProgressBar("Clear TrashBin Folder", 0.01f);
            ClearTrashBin();
            Profiler.StartSection("ClearCaches()");
            optimizedMaterials.Clear();
            optimizedMaterialImportPaths.Clear();
            optimizedSlotSwapMaterials.Clear();
            newAnimationPaths.Clear();
            pathsToDeleteGameObjectTogglesOn.Clear();
            texArrayPropertiesToSet.Clear();
            keepTransforms.Clear();
            convertedMeshRendererPaths.Clear();
            constantAnimatedValuesToAdd.Clear();
            animatedMaterialPropertyDefaultValues.Clear();
            ClearCaches();
            LogAvatarStats($"Avatar stats for '{GetAvatarDescriptor().name}' before optimization:");
            DisplayProgressBar("Destroying unused components", 0.2f);
            Profiler.StartNextSection("DestroyEditorOnlyGameObjects()");
            DestroyEditorOnlyGameObjects();
            Profiler.StartNextSection("DestroyUnusedComponents()");
            DestroyUnusedComponents();
            DisplayProgressBar("Removing duplicate materials", 0.05f);
            Profiler.StartNextSection("DeduplicateMaterials()");
            DeduplicateMaterials();
            if (WritePropertiesAsStaticValues)
            {
                DisplayProgressBar("Parsing Shaders", 0.05f);
                Profiler.StartNextSection("ParseAndCacheAllShaders()");
                var shaders = ShaderAnalyzer.ParseAndCacheAllShaders(FindAllUsedMaterials().Select(m => m.shader), true,
                    (done, total) => DisplayProgressBar($"Parsing Shaders ({done}/{total})", 0.05f + 0.15f * done / total));
                LogShaderParseResult(shaders);
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
            LogAvatarStats("Avatar stats after optimization:");
            Profiler.StartNextSection("DestroyImmediate(this)");
            var t = transform;
            DestroyImmediate(this);
            if (t.childCount == 0 && t.GetComponents<Component>().Length == 1)
                DestroyImmediate(t.gameObject);
            Profiler.EndSection();
            if (settings.ProfileTimeUsed)
                Profiler.PrintTimeUsed();
            LogToFile(string.Join("\n  - ", Profiler.FormatTimeUsed()));
        }
        catch (System.Exception e)
        {
            LogToFile("An error occurred during optimization:\n" + e.ToString());
            throw;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            ImportLogFile();
        }
    }

    public static bool HasCustomShaderSupport { get => EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64; }
    public bool ApplyOnUpload { get { return settings.ApplyOnUpload; } set { settings.ApplyOnUpload = value; } }
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
    private HashSet<string> pathsToDeleteGameObjectTogglesOn = new HashSet<string>();
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
    private Dictionary<string, Dictionary<string, Vector4>> animatedMaterialPropertyDefaultValues = new Dictionary<string, Dictionary<string, Vector4>>();
    private List<List<Texture2D>> textureArrayLists = new List<List<Texture2D>>();
    private List<Texture2DArray> textureArrays = new List<Texture2DArray>();
    private Dictionary<Material, List<(string name, Texture2DArray array)>> texArrayPropertiesToSet = new Dictionary<Material, List<(string name, Texture2DArray array)>>();
    private HashSet<Transform> keepTransforms = new HashSet<Transform>();
    private HashSet<string> convertedMeshRendererPaths = new HashSet<string>();
    private Dictionary<Transform, Transform> movingParentMap = new Dictionary<Transform, Transform>();
    private Dictionary<string, Transform> transformFromOldPath = new Dictionary<string, Transform>();
    private Dictionary<EditorCurveBinding, float> constantAnimatedValuesToAdd = new Dictionary<EditorCurveBinding, float>();
    private Dictionary<string, List<MaterialSlot>> materialSlotsToDisableWhenOriginalPathMeshIsDisabled = new();
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
        "Grin",
        "Blink",
        "Blink Happy",
        "Pupil",
        "Wink",
        "Wink Right",
        "Wink 2",
        "Wink 2 Right",
        "Calm",
        "Stare",
        "Cheerful",
        "Sadness",
        "Anger",
        "Upper",
        "Lower"
    };

    private static float progressBar = 0;

    private void DisplayProgressBar(string text)
    {
        var name = GetRootTransform().name;
        var titleName = name.EndsWith("(BrokenCopy)") ? name.Substring(0, name.Length - "(BrokenCopy)".Length) : name;
        EditorUtility.DisplayProgressBar("Optimizing " + titleName, text, progressBar);
    }

    private void DisplayProgressBar(string text, float progress)
    {
        progressBar = progress;
        DisplayProgressBar(text);
    }

    private d4rkpl4y3r.AvatarOptimizer.Util.Logger log = null;

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
        log = new (Path.Combine(trashBinPath, "_log.txt"));
        LogToFile($"d4rk Avatar Optimizer v{packageInfo.version}");
        LogToFile($"Unity Version: {Application.unityVersion}");
        LogToFile($"Application.isPlaying: {Application.isPlaying}");
        LogToFile($"Build Target: {EditorUserBuildSettings.activeBuildTarget}");
        LogToFile($"SystemInfo.graphicsDeviceType: {SystemInfo.graphicsDeviceType}");
        LogToFile("Global Settings:");
        using (log.IndentScope())
        {
            LogToFile($"- Always Optimize on Upload: {AvatarOptimizerSettings.DoOptimizeWithDefaultSettingsWhenNoComponent}");
            LogToFile($"- Optimize in Play Mode: {AvatarOptimizerSettings.DoOptimizeInPlayMode}");
            LogToFile($"- Auto Refresh Preview Timeout: {AvatarOptimizerSettings.AutoRefreshPreviewTimeout} ms");
            LogToFile($"- Motion Time Approximation Sample Count: {AvatarOptimizerSettings.MotionTimeApproximationSampleCount}");
        }
        LogToFile("Settings:");
        using (log.IndentScope())
        {
            foreach (var field in typeof(Settings).GetFields())
            {
                LogToFile($"- {GetDisplayName(field.Name)}: {field.GetValue(settings)}");
            }
        }
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

    private void LogToFile(string message, int extraIndent = 0)
    {
        if (!isOptimizing || log == null)
            return;
        using var _ = new Profiler.Section("LogToFile()");
        log.indentLevel += extraIndent;
        log.Append(message);
        log.indentLevel -= extraIndent;
    }

    private void ImportLogFile()
    {
        if (log == null)
            return;
        log.Flush();
        AssetDatabase.ImportAsset(log.filePath);
    }

    private void LogShaderParseResult(List<ParsedShader> shaders)
    {
        if (shaders.Count == 0)
            return;
        var filtered = shaders.Distinct().OrderBy(s => s.name).ToList();
        var unmergeable = filtered.Where(s => !s.CanMerge()).ToList();
        var mergeable = filtered.Where(s => s.CanMerge()).ToList();
        LogToFile($"Parsed {filtered.Count} shaders:");
        if (mergeable.Count > 0)
        {
            LogToFile($"- {mergeable.Count} mergeable shaders:", 1);
            foreach (var shader in mergeable)
            {
                LogToFile($"- {shader.name}", 2);
            }
        }
        if (unmergeable.Count > 0)
        {
            LogToFile($"- {unmergeable.Count} unmergeable shaders:", 1);
            var groupedByMessage = unmergeable.GroupBy(s => s.CantMergeReason()).OrderBy(g => g.Count()).ToList();
            foreach (var group in groupedByMessage)
            {
                LogToFile($"- {group.Key}", 2);
                foreach (var shader in group)
                {
                    LogToFile($"- {shader.name}", 3);
                }
            }
        }
    }

    private void LogAvatarStats(string header)
    {
        using var _ = new Profiler.Section("LogAvatarStats()");
        LogToFile(header);
        using var __ = log.IndentScope();
        var av = GetAvatarDescriptor();
        var components = av.GetComponentsInChildren<Component>(true).GroupBy(c => c.GetType()).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.FullName).ToArray();
        LogToFile($"- Total Component Types: {components.Length}");
        foreach (var group in components)
        {
            LogToFile($"- {group.Key}: {group.Count()}", 1);
        }
        var renderers = components.Where(g => g.Key == typeof(MeshRenderer) || g.Key == typeof(SkinnedMeshRenderer)).SelectMany(g => g).Cast<Renderer>().ToArray();
        var skinnedMeshRenderers = components.Where(g => g.Key == typeof(SkinnedMeshRenderer)).SelectMany(g => g).Cast<SkinnedMeshRenderer>().ToArray();
        LogToFile($"- Total Poly Count: {renderers.Sum(r => GetRendererPolyCount(r))}");
        LogToFile($"- Total BlendShapes: {skinnedMeshRenderers.Sum(r => r.sharedMesh == null ? 0 : r.sharedMesh.blendShapeCount)}");
        LogToFile($"- Renderer Material Slots: {renderers.Sum(r => r.sharedMaterials.Length)}");

        var animatorControllers = av.baseAnimationLayers.Concat(av.specialAnimationLayers).Select(l => l.animatorController).Where(c => c != null).Distinct().Cast<AnimatorController>().ToArray();
        var totalAnimatorLayerCount = animatorControllers.Sum(c => c.layers.Length);
        LogToFile($"- Animator Layers: {totalAnimatorLayerCount}");
        var animationClipCount = animatorControllers.SelectMany(c => c.animationClips).Distinct().Count();
        LogToFile($"- Animation Clip Count: {animationClipCount}");
    }

    private VRCAvatarDescriptor cache_avatarDescriptor = null;
    public VRCAvatarDescriptor GetAvatarDescriptor()
    {
        if (cache_avatarDescriptor != null)
            return cache_avatarDescriptor;
        cache_avatarDescriptor = null;
        var current = transform;
        while (current != null)
        {
            if (current.TryGetComponent<VRCAvatarDescriptor>(out var descriptor))
            {
                cache_avatarDescriptor = descriptor;
                break;
            }
            current = current.parent;
        }
        return cache_avatarDescriptor;
    }

    private HashSet<string> cache_toolsUsedOnAvatar = null;
    private long lastToolsCheckTime = 0;
    public HashSet<string> GetNonDestructiveToolsUsedOnAvatar()
    {
        if (System.DateTime.Now.Ticks - lastToolsCheckTime > System.TimeSpan.TicksPerMinute * 2)
        {
            cache_toolsUsedOnAvatar = null;
            lastToolsCheckTime = System.DateTime.Now.Ticks;
        }
        if (cache_toolsUsedOnAvatar != null)
            return cache_toolsUsedOnAvatar;
        var tools = new HashSet<string>();
        var descriptor = GetAvatarDescriptor();
        if (descriptor == null)
            return tools;
        var allComponentTypes = new HashSet<Type>(descriptor.GetComponentsInChildren<Component>(true).Where(c => c != null).Select(c => c.GetType()));
        if (allComponentTypes.Any(t => t.FullName.StartsWithSimple("nadena.dev.modular_avatar.core.")))
        {
            tools.Add("Modular Avatar");
        }
        if (allComponentTypes.Any(t => t.FullName == "VF.Model.VRCFury" || t.FullName.StartsWithSimple("VF.Component.")))
        {
            tools.Add("VRCFury");
        }
        if (allComponentTypes.Any(t => t.FullName.StartsWithSimple("Prefabulous.Universal.Common.Runtime.")))
        {
            tools.Add("Prefabulous Avatar");
        }
        return cache_toolsUsedOnAvatar = tools;
    }

    public Transform GetRootTransform()
    {
        var descriptor = GetAvatarDescriptor();
        return descriptor == null ? transform : descriptor.transform;
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
        return GetTransformPathTo(t, GetRootTransform());
    }

    public Transform GetTransformFromPath(string path)
    {
        if (path == "")
            return GetRootTransform();
        string[] pathParts = path.Split('/');
        Transform t = GetRootTransform();
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
        MaterialAssetComparer.ClearCache();
    }

    public long GetRendererPolyCount(Renderer renderer)
    {
        if (renderer == null || !(renderer is SkinnedMeshRenderer || renderer is MeshRenderer))
            return 0;
        var mesh = renderer.GetSharedMesh();
        if (mesh == null)
            return 0;
        return Enumerable.Range(0, mesh.subMeshCount).Sum(i => mesh.GetIndexCount(i) / (mesh.GetTopology(i) == MeshTopology.Quads ? 2 : 3));
    }

    public long GetPolyCount()
    {
        return GetUsedComponentsInChildren<Renderer>().Sum(r => GetRendererPolyCount(r));
    }

    private Dictionary<Renderer, List<ParticleSystem>> cache_ParticleSystemsUsingRenderer = null;
    private List<ParticleSystem> GetParticleSystemsUsingRenderer(Renderer candidate)
    {
        if (cache_ParticleSystemsUsingRenderer == null)
        {
            cache_ParticleSystemsUsingRenderer = new Dictionary<Renderer, List<ParticleSystem>>();
            foreach (var ps in GetUsedComponentsInChildren<ParticleSystem>())
            {
                Renderer renderer = ps.shape.shapeType == ParticleSystemShapeType.SkinnedMeshRenderer ? ps.shape.skinnedMeshRenderer : null;
                renderer = ps.shape.shapeType == ParticleSystemShapeType.MeshRenderer ? ps.shape.meshRenderer : renderer;
                if (renderer != null)
                {
                    if (!cache_ParticleSystemsUsingRenderer.TryGetValue(renderer, out var list))
                    {
                        list = new List<ParticleSystem>();
                        cache_ParticleSystemsUsingRenderer[renderer] = list;
                    }
                    list.Add(ps);
                }
            }
        }
        if (cache_ParticleSystemsUsingRenderer.TryGetValue(candidate, out var result))
            return result;
        return cache_ParticleSystemsUsingRenderer[candidate] = new List<ParticleSystem>();
    }

    private static bool IsMaterialReadyToCombineWithOtherMeshes(Material material)
    {
        return material != null && ShaderAnalyzer.Parse(material.shader).CanMerge();
    }

    private bool IsBasicCombinableRenderer(Renderer candidate)
    {
        if (candidate.TryGetComponent(out Cloth cloth))
            return false;
        if (candidate is MeshRenderer && (candidate.gameObject.layer == 12 || !MergeStaticMeshesAsSkinned))
            return false;
        if (GetParticleSystemsUsingRenderer(candidate).Any(ps => !ps.shape.useMeshMaterialIndex || candidate is MeshRenderer))
            return false;
        return true;
    }

    private Dictionary<Renderer, bool> cache_UsesUV0ZW = null;
    private bool UsesUV0ZW(Renderer renderer)
    {
        if (renderer == null)
            return false;
        cache_UsesUV0ZW ??= new Dictionary<Renderer, bool>();
        if (cache_UsesUV0ZW.TryGetValue(renderer, out var cachedResult))
            return cachedResult;
        var mesh = renderer.GetSharedMesh();
        if (mesh == null)
            return cache_UsesUV0ZW[renderer] = false;
        for (int i = 0; i < mesh.vertexAttributeCount; i++)
        {
            var attr = mesh.GetVertexAttribute(i);
            if (attr.attribute == VertexAttribute.TexCoord0 && (attr.dimension == 3 || attr.dimension == 4))
                return cache_UsesUV0ZW[renderer] = true;
        }
        return cache_UsesUV0ZW[renderer] = false;
    }

    private bool IsShaderToggleCombinableRenderer(Renderer candidate)
    {
        if (!IsBasicCombinableRenderer(candidate))
            return false;
        if (UsesUV0ZW(candidate))
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

    private bool RenderersHaveSameRootBoneScaleSign(Renderer a, Renderer b)
    {
        if (a == null || b == null)
            return true;
        var scaleA = a.GetRootBone().lossyScale;
        var scaleB = b.GetRootBone().lossyScale;
        return Mathf.Sign(scaleA.x) == Mathf.Sign(scaleB.x) && Mathf.Sign(scaleA.y) == Mathf.Sign(scaleB.y) && Mathf.Sign(scaleA.z) == Mathf.Sign(scaleB.z);
    }

    private bool CanCombineRendererWith(List<Renderer> list, Renderer candidate)
    {
        if (!MergeSkinnedMeshes)
            return false;
        if (list[0].gameObject.layer != candidate.gameObject.layer)
            return false;
        if (list[0].shadowCastingMode != candidate.shadowCastingMode)
            return false;
        if (list[0].receiveShadows != candidate.receiveShadows)
            return false;
        if (!RenderersHaveSameRootBoneScaleSign(list[0], candidate))
            return false;
        bool OneOfParentsHasGameObjectToggleThatTheOthersArentChildrenOf(Transform t, string[] otherPaths)
        {
            while ((t = t.parent) != GetRootTransform())
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
        if (!list.All(r => UsesUV0ZW(r) == UsesUV0ZW(candidate)))
            return false;
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
        return cache_CanUseNaNimationOnMesh[path] = GetRendererDefaultEnabledState(renderer) || !FindAllPathsWhereMeshOrGameObjectHasOnlyOnAnimation().Contains(path);
    }

    private HashSet<string> cache_FindAllPathsWhereMeshOrGameObjectHasOnlyOnAnimation = null;
    public HashSet<string> FindAllPathsWhereMeshOrGameObjectHasOnlyOnAnimation()
    {
        if (cache_FindAllPathsWhereMeshOrGameObjectHasOnlyOnAnimation == null) {
            cache_FindAllPathsWhereMeshOrGameObjectHasOnlyOnAnimation = new HashSet<string>();
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
                cache_FindAllPathsWhereMeshOrGameObjectHasOnlyOnAnimation.UnionWith(goOnPaths.Except(goOffPaths));
                cache_FindAllPathsWhereMeshOrGameObjectHasOnlyOnAnimation.UnionWith(meshOnPaths.Except(meshOffPaths));
            }
            foreach (var path in cache_FindAllPathsWhereMeshOrGameObjectHasOnlyOnAnimation.ToList()) {
                var t = GetTransformFromPath(path);
                if (t == null || (t.GetComponent<MeshRenderer>() == null && t.GetComponent<SkinnedMeshRenderer>() == null))
                    cache_FindAllPathsWhereMeshOrGameObjectHasOnlyOnAnimation.Remove(path);
            }
        }
        return cache_FindAllPathsWhereMeshOrGameObjectHasOnlyOnAnimation;
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
            if(typeof(Renderer).IsAssignableFrom(binding.type) && binding.propertyName.StartsWithSimple("material.")) {
                // ignore bindings for material properties that do not exist for any materials or material swaps on this renderer

                Renderer renderer = GetTransformFromPath(binding.path)?.GetComponent<Renderer>();
                if(renderer) {
                    int materialSlotCount = renderer.sharedMaterials.Length;

                    var swaps = FindAllMaterialSwapMaterials();
                    string materialProperty = binding.propertyName.Substring("material.".Length).Split('.', 2)[0];

                    bool propertyExists = false;

                    for(int i = 0; i < materialSlotCount && !propertyExists; ++i) {
                        // check the default materials
                        if(renderer.sharedMaterials[i] != null && renderer.sharedMaterials[i].HasProperty(materialProperty)) {
                            propertyExists = true;
                            break;
                        }

                        // check the material swaps
                        if(swaps.TryGetValue((binding.path, i), out var mats)) {
                            foreach(Material mat in mats) {
                                if(mat != null && mat.HasProperty(materialProperty)) {
                                    propertyExists = true;
                                    break;
                                }
                            }
                        }
                    }

                    // if the property does not exist, this binding is not relevant
                    if(!propertyExists)
                        return false;
                }
            }
            
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
            foreach (var clip in GetAllUsedAnimationClips())
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
            foreach (var clip in GetAllUsedAnimationClips()) {
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
        var avDescriptor = GetAvatarDescriptor();
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
        var skinnedMeshRenderers = GetRootTransform().GetComponentsInChildren<SkinnedMeshRenderer>(true);
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
        var avDescriptor = GetAvatarDescriptor();
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
                // merged meshes move all their sibling components to a new child object
                // the general remap in transformFromOldPath points to that new child object
                // which means transform and renderer animations should still point to the original parent object
                // while gameobject toggles as well as other component animations should not
                if (path.EndsWith("/d4rkAO_mergeTargetRoot") &&
                    (binding.type == typeof(Transform) || typeof(Renderer).IsAssignableFrom(binding.type)))
                {
                    path = path.Substring(0, path.Length - "/d4rkAO_mergeTargetRoot".Length);
                }
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

    private static readonly Dictionary<char, char> otherVectorOrColorComponent = new Dictionary<char, char> {
        { 'x', 'r' }, { 'y', 'g' }, { 'z', 'b' }, { 'w', 'a' },
        { 'r', 'x' }, { 'g', 'y' }, { 'b', 'z' }, { 'a', 'w' },
    };
    Dictionary<string, Dictionary<Type, HashSet<string>>> cache_IsAnimatableBinding = null;
    public bool IsAnimatableBinding(EditorCurveBinding binding) {
        if (cache_IsAnimatableBinding == null)
            cache_IsAnimatableBinding = new Dictionary<string, Dictionary<Type, HashSet<string>>>();

        if (!cache_IsAnimatableBinding.TryGetValue(binding.path, out var animatableBindings)) {
            animatableBindings = new Dictionary<Type, HashSet<string>>();
            GameObject targetObject = GetTransformFromPath(binding.path)?.gameObject;
            if (targetObject != null) {
                foreach (var animatableBinding in AnimationUtility.GetAnimatableBindings(targetObject, GetRootTransform().gameObject)) {
                    var name = animatableBinding.propertyName;
                    var type = animatableBinding.type;
                    if (!animatableBindings.TryGetValue(type, out var animatableProperties)) {
                        animatableProperties = new HashSet<string>();
                        animatableBindings[type] = animatableProperties;
                    }
                    animatableProperties.Add(name);
                    if (name.Length > 2 && name[name.Length - 2] == '.' && otherVectorOrColorComponent.TryGetValue(name[name.Length - 1], out var otherComponent)) {
                        // Color & Vector properties can both be animated by .xyzw or .rgba but only one of them gets returned by GetAnimatableBindings
                        animatableProperties.Add(name.Substring(0, name.Length - 1) + otherComponent);
                    }
                }
                if (!animatableBindings.ContainsKey(typeof(GameObject))) {
                    animatableBindings[typeof(GameObject)] = new HashSet<string>();
                }
                animatableBindings[typeof(GameObject)].Add("ComponentExists");
                foreach (var component in targetObject.GetNonNullComponents()) {
                    var componentType = component.GetType();
                    if (!animatableBindings.ContainsKey(componentType)) {
                        animatableBindings[componentType] = new HashSet<string>();
                    }
                    animatableBindings[componentType].Add("ComponentExists");
                }
                if (targetObject.TryGetComponent(out VRCStation station)) {
                    // even if box collider doesn't exist right now, the station script will create one at runtime
                    var boxColliderType = typeof(BoxCollider);
                    if (!animatableBindings.ContainsKey(boxColliderType)) {
                        animatableBindings[boxColliderType] = new HashSet<string>();
                    }
                    animatableBindings[boxColliderType].UnionWith(new string[] {
                        "ComponentExists", "m_IsTrigger", "m_Enabled",
                        "m_Center.x", "m_Center.y", "m_Center.z",
                        "m_Size.x", "m_Size.y", "m_Size.z"
                    });
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
        foreach (var kvp in animatableBindings) {
            if (binding.type.IsAssignableFrom(kvp.Key) && (!typeof(Renderer).IsAssignableFrom(binding.type) || kvp.Value.Contains(binding.propertyName))) {
                return true;
            }
        }

        return false;
    }

    private Material cache_DisabledMaterial = null;
    private Material GetDisabledMaterial()
    {
        if (cache_DisabledMaterial == null)
        {
            cache_DisabledMaterial = new Material(Shader.Find(
                HasCustomShaderSupport ? "d4rkpl4y3r/Optimizer/DisabledMaterial" : "VRChat/Mobile/Particles/Additive"));
            cache_DisabledMaterial.name = "d4rkAvatarOptimizer_DisabledMaterial";
            cache_DisabledMaterial.SetShaderPassEnabled("Always", false);
            CreateUniqueAsset(cache_DisabledMaterial, "d4rkAvatarOptimizer_DisabledMaterial.mat");
        }
        return cache_DisabledMaterial;
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
        var removedBindings = new List<EditorCurveBinding>();
        var addedBindings = new List<EditorCurveBinding>();
        var changedBindings = new List<(EditorCurveBinding from, EditorCurveBinding to)>();
        float lastUsedKeyframeTime = -1;
        float lastUnusedKeyframeTime = -1;
        void SetFloatCurve(AnimationClip clipToSet, EditorCurveBinding bindingToSet, AnimationCurve curveToSet) {
            if (IsAnimatableBinding(bindingToSet)) {
                lastUsedKeyframeTime = Mathf.Max(curveToSet.length > 0 ? curveToSet.keys[^1].time : 0, lastUsedKeyframeTime);
                AnimationUtility.SetEditorCurve(clipToSet, bindingToSet, curveToSet);
            } else {
                lastUnusedKeyframeTime = Mathf.Max(curveToSet.length > 0 ? curveToSet.keys[^1].time : 0, lastUnusedKeyframeTime);
                changed = true;
                removedBindings.Add(bindingToSet);
            }
        }
        void SetObjectReferenceCurve(AnimationClip clipToSet, EditorCurveBinding bindingToSet, ObjectReferenceKeyframe[] curveToSet) {
            if (IsAnimatableBinding(bindingToSet)) {
                lastUsedKeyframeTime = Mathf.Max(curveToSet.Length > 0 ? curveToSet[^1].time : 0, lastUsedKeyframeTime);
                AnimationUtility.SetObjectReferenceCurve(clipToSet, bindingToSet, curveToSet);
            } else {
                lastUnusedKeyframeTime = Mathf.Max(curveToSet.Length > 0 ? curveToSet[^1].time : 0, lastUnusedKeyframeTime);
                changed = true;
                removedBindings.Add(bindingToSet);
            }
        }
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            var fixedBinding = FixAnimationBinding(binding, ref changed);
            bool isRendererToggleBinding = (typeof(Renderer).IsAssignableFrom(binding.type) && binding.propertyName == "m_Enabled")
                || (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive");
            if (isRendererToggleBinding
                && materialSlotsToDisableWhenOriginalPathMeshIsDisabled.TryGetValue(binding.path, out var slotsToDisable))
            {
                foreach (var slot in slotsToDisable)
                {
                    var matSwapBinding = EditorCurveBinding.PPtrCurve(GetPathToRoot(slot.renderer), typeof(SkinnedMeshRenderer), $"m_Materials.Array.data[{slot.index}]");
                    var keyframes = curve.keys.Select((key, i) => new ObjectReferenceKeyframe {
                        time = key.time,
                        value = key.value == 0 ? GetDisabledMaterial() : slot.material
                    }).ToArray();
                    SetObjectReferenceCurve(newClip, matSwapBinding, keyframes);
                    changed = true;
                    addedBindings.Add(matSwapBinding);
                }
            }
            if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive" && !pathsToDeleteGameObjectTogglesOn.Contains(binding.path))
            {
                SetFloatCurve(newClip, FixAnimationBindingPath(binding, ref changed), curve);
            }
            if (fixedBinding.propertyName.StartsWithSimple("NaNimation")) {
                var shaderToggleInfo = fixedBinding.propertyName.Substring("NaNimation".Length);
                var propertyNames = new string[] { "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" };
                var NaNCurve = ReplaceZeroWithNaN(curve);
                for (int i = 0; i < propertyNames.Length; i++) {
                    fixedBinding.propertyName = propertyNames[i];
                    SetFloatCurve(newClip, fixedBinding, NaNCurve);
                    addedBindings.Add(fixedBinding);
                }
                if (shaderToggleInfo.Length > 0) {
                    shaderToggleInfo = shaderToggleInfo.Substring(1);
                    var semicolonIndex = shaderToggleInfo.IndexOf(';');
                    fixedBinding.path = shaderToggleInfo.Substring(semicolonIndex + 1);
                    fixedBinding.propertyName = $"material._IsActiveMesh{shaderToggleInfo.Substring(0, semicolonIndex)}";
                    fixedBinding.type = typeof(SkinnedMeshRenderer);
                    var b = FixAnimationBindingPath(fixedBinding, ref changed);
                    SetFloatCurve(newClip, b, curve);
                    addedBindings.Add(b);
                }
            } else {
                if (fixedBinding != binding)
                    changedBindings.Add((binding, fixedBinding));
                SetFloatCurve(newClip, fixedBinding, curve);
                if (fixedBinding.propertyName.StartsWithSimple($"material.d4rkAvatarOptimizer") && MergeSkinnedMeshesWithNaNimation) {
                    var otherBinding = fixedBinding;
                    var match = Regex.Match(fixedBinding.propertyName, @"material\.d4rkAvatarOptimizer(.+)_ArrayIndex\d+(\.[a-z])?");
                    otherBinding.propertyName = $"material.{match.Groups[1].Value}{match.Groups[2].Value}";
                    SetFloatCurve(newClip, otherBinding, curve);
                    addedBindings.Add(otherBinding);
                }
            }
            if (isRendererToggleBinding && physBonesToDisable.TryGetValue(binding.path, out var physBonePaths))
            {
                var physBoneBinding = binding;
                physBoneBinding.propertyName = "m_Enabled";
                physBoneBinding.type = typeof(VRCPhysBone);
                foreach (var physBonePath in physBonePaths)
                {
                    physBoneBinding.path = physBonePath;
                    var b = FixAnimationBindingPath(physBoneBinding, ref changed);
                    SetFloatCurve(newClip, b, curve);
                    addedBindings.Add(b);
                    changed = true;
                }
            }
        }
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
        {
            var curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            if (GetAllMaterialSwapBindingsToRemove().Contains(binding))
            {
                lastUnusedKeyframeTime = Mathf.Max(curve.Length > 0 ? curve[^1].time : 0, lastUnusedKeyframeTime);
                changed = true;
                removedBindings.Add(binding);
                continue;
            }
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
                LogToFile($"- clip '{clip.name}' has no used keyframes but unused keyframes up to time {lastUnusedKeyframeTime}, using dummy clip");
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
            LogToFile($"- clip '{clip.name}' got modified");
            foreach (var (from, to) in changedBindings)
            {
                if (from.type == to.type)
                {
                    LogToFile($"  * {from.path}.{from.propertyName} => {to.path}.{to.propertyName} ({from.type})");
                }
                else
                {
                    LogToFile($"  * {from.path}.{from.propertyName} ({from.type})  =>  {to.path}.{to.propertyName} ({to.type})");
                }
            }
            foreach (var binding in removedBindings)
            {
                LogToFile($"  - {binding.path}.{binding.propertyName} ({binding.type})");
            }
            foreach (var binding in addedBindings)
            {
                LogToFile($"  + {binding.path}.{binding.propertyName} ({binding.type})");
            }
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
        var avDescriptor = GetAvatarDescriptor();
        if (avDescriptor == null)
            return;
        
        int totalControllerCount = avDescriptor.baseAnimationLayers.Length + avDescriptor.specialAnimationLayers.Length;
        var layerCopyPaths = new string[totalControllerCount];
        var optimizedControllers = new AnimatorController[totalControllerCount];

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
            LogToFile($"Optimizing FX Layer with {GetFXLayerLayers().Length} original layers");
            if (fxLayersToMerge.Count > 0)
            {
                LogToFile($"- Merging {fxLayersToMerge.Count} layers:", 1);
                for (int i = 0; i < fxLayersToMerge.Count; i++)
                {
                    LogToFile($"- ({fxLayersToMerge[i]}) {GetFXLayerLayers()[fxLayersToMerge[i]].name}", 2);
                }
            }
            if (fxLayersToDestroy.Count > 0)
            {
                LogToFile($"- Removing {fxLayersToDestroy.Count} layers:", 1);
                for (int i = 0; i < fxLayersToDestroy.Count; i++)
                {
                    LogToFile($"- ({fxLayersToDestroy[i]}) {GetFXLayerLayers()[fxLayersToDestroy[i]].name}", 2);
                }
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
            avDescriptor.baseAnimationLayers[i].animatorController = optimizedControllers[i];
        }
        for (int i = 0; i < avDescriptor.specialAnimationLayers.Length; i++)
        {
            var controller = avDescriptor.specialAnimationLayers[i].animatorController as AnimatorController;
            if (controller == null)
                continue;
            var index = i + avDescriptor.baseAnimationLayers.Length;
            layerCopyPaths[index] = $"{trashBinPath}SpecialAnimationLayer{index}{controller.name}(OptimizedCopy).controller";
            optimizedControllers[index] = AnimatorOptimizer.Copy(controller, layerCopyPaths[index], fxLayerMap);
            optimizedControllers[index].name = $"SpecialAnimationLayer{index}{controller.name}(OptimizedCopy)";
            avDescriptor.specialAnimationLayers[i].animatorController = optimizedControllers[index];
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
        LogToFile($"Fixing animation paths in {animations.Count} animation clips");
        using (log.IndentScope())
        {
            foreach (var clip in animations)
            {
                fixedMotions[clip] = FixAnimationClipPaths(clip);
            }
        }
        
        for (int i = 0; i < optimizedControllers.Length; i++)
        {
            var newController = optimizedControllers[i];
            if (newController == null)
                continue;

            foreach (var state in newController.EnumerateAllStates())
            {
                state.motion = FixMotion(state.motion, fixedMotions, layerCopyPaths[i]);
            }

            var layers = newController.layers;
            var syncedLayerIndices = layers.Select((layer, index) => (layer, index)).Where(p => p.layer != null && p.layer.syncedLayerIndex >= 0).Select(p => p.index).ToArray();
            foreach (var syncedLayerIndex in syncedLayerIndices)
            {
                var syncedLayer = layers[syncedLayerIndex];
                foreach (var stateMotionPair in syncedLayer.EnumerateAllMotionOverrides())
                {
                    syncedLayer.SetOverrideMotion(stateMotionPair.state, FixMotion(stateMotionPair.motion, fixedMotions, layerCopyPaths[i]));
                }
            }
            if (syncedLayerIndices.Length > 0)
            {
                newController.layers = layers;
            }

            if (DeleteUnusedGameObjects)
            {
                foreach (var behavior in newController.layers.SelectMany(layer => layer.stateMachine.EnumerateAllBehaviours()))
                {
                    if (behavior is VRC.SDKBase.VRC_AnimatorPlayAudio playAudio)
                    {
                        var path = playAudio.SourcePath ?? "";
                        if (transformFromOldPath.TryGetValue(path, out var transform) && transform != null)
                        {
                            playAudio.SourcePath = GetPathToRoot(transform);
                        }
                    }
                }
            }
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
        var avDescriptor = GetAvatarDescriptor();

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
            if (layer.syncedLayerIndex != -1)
            {
                errorMessages[i].Add($"synced with layer {layer.syncedLayerIndex}");
                if (layer.syncedLayerIndex >= 0 && layer.syncedLayerIndex < fxLayerLayers.Length)
                {
                    errorMessages[layer.syncedLayerIndex].Insert(0, $"layer {i} is synced with this layer");
                }
                continue;
            }
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
                int singleTransitionStateIndex = Array.FindIndex(states, s => s.state.transitions.Length == 1);
                bool usesAnyStateTransitions = false;
                if (singleTransitionStateIndex == -1)
                {
                    if (states.Sum(s => s.state.transitions.Length) > 0)
                    {
                        errorMessages[i].Add($"no single transition state");
                        continue;
                    }
                    var anyStateTransitionDestinationIndices = stateMachine.anyStateTransitions.Select(t => Array.FindIndex(states, s => s.state == t.destinationState)).ToList();
                    if (anyStateTransitionDestinationIndices.Any(i => i < 0 || i >= states.Length))
                    {
                        errorMessages[i].Add($"any state transition destination state is not in the states array");
                        continue;
                    }
                    var state0transitions = anyStateTransitionDestinationIndices.Count(i => i == 0);
                    var state1transitions = anyStateTransitionDestinationIndices.Count(i => i == 1);
                    if (state0transitions != 1 && state1transitions != 1)
                    {
                        errorMessages[i].Add($"no single transition state");
                        continue;
                    }
                    singleTransitionStateIndex = state0transitions == 1 ? 1 : 0;
                    usesAnyStateTransitions = true;
                }
                else if (stateMachine.anyStateTransitions.Length != 0)
                {
                    errorMessages[i].Add($"has any state transitions");
                    continue;
                }
                var orState = states[singleTransitionStateIndex].state;
                var andState = states[1 - singleTransitionStateIndex].state;
                AnimatorStateTransition[] orStateTransitions = orState.transitions;
                AnimatorStateTransition[] andStateTransitions = andState.transitions;
                if (usesAnyStateTransitions)
                {
                    orStateTransitions = stateMachine.anyStateTransitions.Where(t => t.destinationState == andState).ToArray();
                    andStateTransitions = stateMachine.anyStateTransitions.Where(t => t.destinationState == orState).ToArray();
                }
                var stateTransitions = singleTransitionStateIndex == 0
                    ? new AnimatorStateTransition[][] { orStateTransitions, andStateTransitions }
                    : new AnimatorStateTransition[][] { andStateTransitions, orStateTransitions };
                if (orStateTransitions[0].conditions.Length != andStateTransitions.Length)
                {
                    errorMessages[i].Add($"or state has {orStateTransitions[0].conditions.Length} conditions but and state has {andStateTransitions.Length} transitions");
                    continue;
                }
                if (andStateTransitions.Length == 0) {
                    errorMessages[i].Add($"and state has no transitions");
                    continue;
                }
                if (andStateTransitions.Any(t => t.conditions.Length != 1)) {
                    errorMessages[i].Add($"a and state transition has multiple conditions");
                    continue;
                }
                bool conditionError = false;
                foreach (var condition in orStateTransitions[0].conditions) {
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
                        && !andStateTransitions.Any(t => t.conditions.Any(c => c.parameter == condition.parameter && c.mode == inverseConditionMode))) {
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
                        && !andStateTransitions.Any(t => t.conditions.Any(c => c.parameter == condition.parameter && c.mode == inverseConditionMode && c.threshold == inverseThreshold))) {
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
                    if (stateTransitions[j].Any(t => t.destinationState != otherState)) {
                        errorMessages[i].Add($"{state} transition destination state is not the other state");
                        break;
                    }
                    if (stateTransitions[j].Any(t => t.hasExitTime && t.exitTime != 0.0f)) {
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
                if (stateTransitions.Any(s => s.Any(t => t.duration != 0.0f)) && !onlyBoolBindings) {
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
        var avDescriptor = GetAvatarDescriptor();

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

        var possibleBindingTypes = new Dictionary<string, Type[]>();
        bool IsPossibleBinding(EditorCurveBinding binding)
        {
            if (!possibleBindingTypes.TryGetValue(binding.path, out var possibleTypes))
            {
                var uniquePossibleTypes = new HashSet<Type>();
                var transform = GetTransformFromPath(binding.path);
                if (transform != null)
                {
                    // AnimationUtility.GetAnimatableBindings(transform.gameObject, gameObject)
                    // is too slow, so we just check if the components mentioned in the bindings exist at that path
                    uniquePossibleTypes.UnionWith(transform.GetNonNullComponents().Select(c => c.GetType()));
                    uniquePossibleTypes.Add(typeof(GameObject));
                }
                possibleTypes = possibleBindingTypes[binding.path] = uniquePossibleTypes.ToArray();
            }
            return possibleTypes.Any(t => binding.type.IsAssignableFrom(t));
        }

        var fxLayerLayers = GetFXLayerLayers();
        int lastNonUselessLayer = fxLayerLayers.Length;
        for (int i = fxLayerLayers.Length - 1; i >= 0; i--)
        {
            if (i <= 2 && MMDCompatibility)
                break;
            var layer = fxLayerLayers[i];
            if (layer.syncedLayerIndex != -1)
                continue;
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
            var clips = stateMachine.EnumerateAllStates()
                .SelectMany(s => s.motion == null ? new AnimationClip[0] : s.motion.EnumerateAllClips()).Distinct().ToList();
            var layerBindings = clips.SelectMany(c => AnimationUtility.GetCurveBindings(c).Concat(AnimationUtility.GetObjectReferenceCurveBindings(c))).Distinct();
            if (layerBindings.All(b => IsMaterialSwapBinding(b) ? ShouldRemoveMaterialSwapBinding(b) : !IsPossibleBinding(b)))
            {
                uselessLayers.Add(i);
                continue;
            }
            lastNonUselessLayer = i;
        }
        for (int i = 0; i < fxLayerLayers.Length; i++)
        {
            if (fxLayerLayers[i].syncedLayerIndex != -1)
            {
                uselessLayers.Remove(fxLayerLayers[i].syncedLayerIndex);
            }
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
            usedClips.UnionWith(fxLayerLayers[i].EnumerateAllMotionOverrides().Select(p => p.motion as AnimationClip).Where(c => c != null));
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

    private bool? cache_DoesFXLayerUseWriteDefaults = null;
    public bool DoesFXLayerUseWriteDefaults()
    {
        if (cache_DoesFXLayerUseWriteDefaults != null)
            return cache_DoesFXLayerUseWriteDefaults.Value;
        var fxLayer = GetFXLayer();
        if (fxLayer == null)
            return false;
        var fxLayerLayers = GetFXLayerLayers();
        for (int i = 0; i < fxLayerLayers.Length; i++)
        {
            var stateMachine = fxLayerLayers[i].stateMachine;
            if (stateMachine == null || (OptimizeFXLayer && IsMergeableFXLayer(i)))
                continue;
            foreach (var state in stateMachine.EnumerateAllStates())
            {
                if (state.motion is BlendTree blendTree && blendTree.blendType == BlendTreeType.Direct)
                    continue;
                if (state.writeDefaultValues)
                {
                    cache_DoesFXLayerUseWriteDefaults = true;
                    return true;
                }
            }
        }
        cache_DoesFXLayerUseWriteDefaults = false;
        return false;
    }


    private HashSet<AnimationClip> cache_GetAllUsedAnimationClips = null;
    private HashSet<AnimationClip> GetAllUsedAnimationClips()
    {
        if (cache_GetAllUsedAnimationClips != null)
            return cache_GetAllUsedAnimationClips;
        var usedClips = new HashSet<AnimationClip>();
        var avDescriptor = GetAvatarDescriptor();
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
        foreach (var layer in avDescriptor.specialAnimationLayers)
        {
            var controller = layer.animatorController as AnimatorController;
            if (controller == null)
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
        var avDescriptor = GetAvatarDescriptor();
        var physBones = avDescriptor.GetComponentsInChildren<VRCPhysBoneBase>(true);
        foreach (var physBone in physBones)
        {
            result.Add(physBone, new HashSet<Object>());
            physBonePath[GetPathToRoot(physBone)] = physBone;
        }
        var parameterSuffixes = new string[] { "_IsGrabbed", "_IsPosed", "_Angle", "_Stretch", "_Squish" };
        foreach (var controller in avDescriptor.baseAnimationLayers.Select(l => l.animatorController as AnimatorController).Where(c => c != null))
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
        foreach (var clip in GetAllUsedAnimationClips())
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
        foreach (var skinnedMesh in avDescriptor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
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
            int outOfRangeBoneCount = 0;
            void MarkUsedBone(int boneIndex) {
                if (boneIndex < 0 || boneIndex >= meshBones.Length) {
                    outOfRangeBoneCount++;
                    return;
                }
                usedBoneIDs[boneIndex] = true;
            }
            for (int i = 0; i < boneWeights.Length; i++)
            {
                MarkUsedBone(boneWeights[i].boneIndex0);
                if (boneWeights[i].weight1 > 0) {
                    MarkUsedBone(boneWeights[i].boneIndex1);
                    if (boneWeights[i].weight2 > 0) {
                        MarkUsedBone(boneWeights[i].boneIndex2);
                        if (boneWeights[i].weight3 > 0) {
                            MarkUsedBone(boneWeights[i].boneIndex3);
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
            if (outOfRangeBoneCount > 0)
            {
                Debug.LogWarning($"Skinned mesh renderer {GetPathToRoot(skinnedMesh)} has {outOfRangeBoneCount} out of range bone indices");
            }
        }
        foreach (var behavior in avDescriptor.GetComponentsInChildren<Behaviour>(true)
            .Where(b => b != null && (b.GetType().Name.Contains("Constraint") || b.GetType().FullName.StartsWithSimple("RootMotion.FinalIK"))))
        {
            foreach (var t in FindReferencedTransforms(behavior))
            {
                AddDependency(t, behavior);
            }
        }
        foreach (var skinnedRenderer in avDescriptor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            AddDependency(skinnedRenderer.rootBone, skinnedRenderer);
        }
        foreach (var renderer in avDescriptor.GetComponentsInChildren<Renderer>(true))
        {
            AddDependency(renderer.probeAnchor, renderer);
            AddDependency(renderer.transform, renderer);
        }
        foreach (var contact in avDescriptor.GetComponentsInChildren<ContactBase>(true))
        {
            AddDependency(contact.GetRootTransform(), contact);
        }

        var componentTypesToIgnore = new HashSet<string>() {
            "UnityEngine.Transform",
            "nadena.dev.ndmf.multiplatform.components.PortableDynamicBone",
            "nadena.dev.ndmf.multiplatform.components.PortableDynamicBoneCollider",
        };

        foreach (var physBone in physBones)
        {
            var root = physBone.GetRootTransform();
            foreach (Transform current in root.GetAllDescendants().Concat(new Transform[] { root }))
            {
                if (transformToDependency.TryGetValue(current, out var dependencies))
                {
                    result[physBone].UnionWith(dependencies);
                }
                result[physBone].UnionWith(current.GetNonNullComponents()
                    .Where(c => c != physBone && !componentTypesToIgnore.Contains(c.GetType().FullName)));
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
        foreach (var dependencies in physBoneDependencies.Values)
        {
            dependencies.RemoveWhere(o => o == null);
        }
        foreach ((var physBone, var dependencies) in physBoneDependencies)
        {
            if (physBone != null && dependencies.Count(o => !(o is AnimatorController)) == 1 && dependencies.First(o => !(o is AnimatorController)) is SkinnedMeshRenderer target)
            {
                var targetPath = GetPathToRoot(target);
                if (!result.TryGetValue(targetPath, out var physBonePaths))
                {
                    result[targetPath] = physBonePaths = new List<string>();
                }
                physBonePaths.Add(GetPathToRoot(physBone));
            }
        }
        if (result.Count > 0)
        {
            LogToFile($"Found {result.Count} meshes with exclusive physbone dependencies:");
            foreach ((var meshPath, var physBonePaths) in result)
            {
                LogToFile($"- {physBonePaths.Count} physbones owned by '{meshPath}':", 1);
                foreach (var physBonePath in physBonePaths)
                {
                    LogToFile($"- {physBonePath}", 2);
                }
            }
        }
        return result;
    }

    private bool IsMaterialSwapBinding(EditorCurveBinding binding)
    {
        return typeof(Renderer).IsAssignableFrom(binding.type)
            && binding.propertyName.StartsWithSimple("m_Materials.Array.data[");
    }

    private Dictionary<EditorCurveBinding, bool> cache_MaterialSwapBindingsToRemove = null;
    private bool ShouldRemoveMaterialSwapBinding(EditorCurveBinding binding)
    {
        if (cache_MaterialSwapBindingsToRemove == null)
            cache_MaterialSwapBindingsToRemove = new Dictionary<EditorCurveBinding, bool>();
        if (cache_MaterialSwapBindingsToRemove.TryGetValue(binding, out var result))
            return result;
        int slot = int.Parse(binding.propertyName.Substring("m_Materials.Array.data[".Length, binding.propertyName.Length - "m_Materials.Array.data[]".Length));
        var renderer = GetTransformFromPath(binding.path)?.GetComponent<Renderer>();
        return cache_MaterialSwapBindingsToRemove[binding]
            = renderer == null || renderer.sharedMaterials.Length <= slot;
    }

    private HashSet<EditorCurveBinding> cache_GetAllMaterialSwapBindingsToRemove = null;
    private HashSet<EditorCurveBinding> GetAllMaterialSwapBindingsToRemove()
    {
        if (cache_GetAllMaterialSwapBindingsToRemove != null)
            return cache_GetAllMaterialSwapBindingsToRemove;
        var result = new HashSet<EditorCurveBinding>();
        if (cache_MaterialSwapBindingsToRemove != null)
        {
            foreach (var entry in cache_MaterialSwapBindingsToRemove)
            {
                if (entry.Value)
                    result.Add(entry.Key);
            }
        }
        return cache_GetAllMaterialSwapBindingsToRemove = result;
    }

    private Dictionary<(string path, int index), HashSet<Material>> cache_FindAllMaterialSwapMaterials;
    public Dictionary<(string path, int index), HashSet<Material>> FindAllMaterialSwapMaterials()
    {
        if (cache_FindAllMaterialSwapMaterials != null)
            return cache_FindAllMaterialSwapMaterials;
        var result = new Dictionary<(string path, int index), HashSet<Material>>();
        foreach (var clip in GetAllUsedAnimationClips())
        {
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (!IsMaterialSwapBinding(binding))
                    continue;
                int start = binding.propertyName.IndexOf('[') + 1;
                int end = binding.propertyName.IndexOf(']') - start;
                int slot = int.Parse(binding.propertyName.Substring(start, end));
                if (ShouldRemoveMaterialSwapBinding(binding))
                    continue;
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
        bool didLogInitialMessage = false;
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
                    if (!didLogInitialMessage)
                    {
                        LogToFile("Optimizing material swap materials:");
                        didLogInitialMessage = true;
                    }
                    using var _ = log.IndentScope();
                    DisplayProgressBar("Optimizing swap material: " + material.name);
                    var matWrapper = new List<List<(Material, List<string>)>>() { new List<(Material, List<string>)>() { (material, new List<string> { entry.Key.path } ) } };
                    var mergedMeshIndexWrapper = new List<List<int>>() { new List<int>() { meshIndex } };
                    optimizedMaterials[material] = CreateOptimizedMaterials(matWrapper, mergedMeshCount, targetPath, mergedMeshIndexWrapper)[0];
                }
            }
        }
    }

    public bool IsHumanoid()
    {
        var rootAnimator = GetAvatarDescriptor().GetComponent<Animator>();
        return rootAnimator != null && rootAnimator.avatar != null && rootAnimator.avatar.isHuman;
    }

    public AnimatorController GetFXLayer()
    {
        var avDescriptor = GetAvatarDescriptor();
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
        var avDescriptor = GetAvatarDescriptor();
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
            foreach (var clip in GetAllUsedAnimationClips())
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
        foreach (var skinnedMeshRenderer in avDescriptor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
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
        var avDescriptor = GetAvatarDescriptor();
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
        foreach (var clip in GetAllUsedAnimationClips())
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
        var root = GetRootTransform();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (exclusions.Contains(current))
                continue;
            if (current != root && !current.gameObject.activeSelf && !togglePaths.Contains(GetPathToRoot(current)))
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
        var root = GetRootTransform();

        var alwaysDisabledBehaviours = new HashSet<Component>(root.GetComponentsInChildren<Behaviour>(true)
            .Where(b => b != null && !b.enabled)
            .Where(b => !(b is VRCPhysBoneColliderBase))
            .Where(b => !behaviourToggles.Contains(GetPathToRoot(b)))
            .Where(b => !b.GetType().FullName.StartsWithSimple("RootMotion.FinalIK")));

        alwaysDisabledBehaviours.UnionWith(root.GetComponentsInChildren<Renderer>(true)
            .Where(r => r != null && !r.enabled && !(r is ParticleSystemRenderer))
            .Where(r => !behaviourToggles.Contains(GetPathToRoot(r))));

        alwaysDisabledBehaviours.UnionWith(FindAllAlwaysDisabledGameObjects()
            .SelectMany(t => t.GetNonNullComponents()
                .Where(c => !(c is Transform)))
                .Where(c => !c.GetType().FullName.StartsWithSimple("RootMotion.FinalIK")));
        
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

        var usedPhysBoneColliders = root.GetComponentsInChildren<VRCPhysBoneBase>(true)
            .Where(pb => !alwaysDisabledBehaviours.Contains(pb) || exclusions.Contains(pb.transform))
            .SelectMany(pb => pb.colliders);

        alwaysDisabledBehaviours.UnionWith(root.GetComponentsInChildren<VRCPhysBoneColliderBase>(true)
            .Where(c => !usedPhysBoneColliders.Contains(c)));

        alwaysDisabledBehaviours.RemoveWhere(c => exclusions.Contains(c.transform) || c.transform == root);

        return cache_FindAllUnusedComponents = alwaysDisabledBehaviours;
    }

    private HashSet<Transform> cache_FindAllMovingTransforms = null;
    private HashSet<Transform> FindAllMovingTransforms()
    {
        if (cache_FindAllMovingTransforms != null)
            return cache_FindAllMovingTransforms;
        var avDescriptor = GetAvatarDescriptor();
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

        foreach (var clip in GetAllUsedAnimationClips())
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type == typeof(Transform))
                {
                    transforms.Add(GetTransformFromPath(binding.path));
                }
            }
        }

        var animators = avDescriptor.GetComponentsInChildren<Animator>(true);
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
        var physBones = avDescriptor.GetComponentsInChildren<VRCPhysBoneBase>(true)
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

        var constraints = avDescriptor.GetComponentsInChildren<Behaviour>(true)
            .Where(b => b != null && !alwaysDisabledComponents.Contains(b))
            .Where(b => b.GetType().Name.Contains("Constraint")).ToList();
        foreach (var constraint in constraints)
        {
            transforms.Add(constraint.transform);
            if (constraint.GetType().Name.StartsWithSimple("VRC"))
            {
                using var so = new SerializedObject(constraint);
                var targetTransformProperty = so.FindProperty("TargetTransform");
                if (targetTransformProperty != null)
                {
                    var targetTransform = targetTransformProperty.objectReferenceValue as Transform;
                    if (targetTransform != null)
                    {
                        transforms.Add(targetTransform);
                    }
                }
            }
        }

        var finalIKScripts = avDescriptor.GetComponentsInChildren<Behaviour>(true)
            .Where(b => b != null && !alwaysDisabledComponents.Contains(b))
            .Where(b => b.GetType().FullName.StartsWithSimple("RootMotion.FinalIK")).ToList();
        foreach (var finalIKScript in finalIKScripts)
        {
            transforms.UnionWith(FindReferencedTransforms(finalIKScript));
        }

        foreach (var headChop in avDescriptor.GetComponentsInChildren<VRCHeadChop>(true))
        {
            foreach (var targetBone in headChop.targetBones)
            {
                if (targetBone.transform != null)
                {
                    transforms.Add(targetBone.transform);
                }
            }
        }

        transforms.UnionWith(avDescriptor.transform.GetAllDescendants().Where(t => t.localScale != Vector3.one));

        transforms.UnionWith(avDescriptor.GetComponentsInChildren<Rigidbody>(true)
            .Where(rb => !alwaysDisabledComponents.Contains(rb)).Select(rb => rb.transform));

        return cache_FindAllMovingTransforms = transforms;
    }

    private HashSet<Transform> cache_FindAllUnmovingTransforms = null;
    public  HashSet<Transform> FindAllUnmovingTransforms()
    {
        if (cache_FindAllUnmovingTransforms != null)
            return cache_FindAllUnmovingTransforms;
        var avDescriptor = GetAvatarDescriptor();
        if (avDescriptor == null)
            return new HashSet<Transform>();
        var moving = FindAllMovingTransforms();
        return cache_FindAllUnmovingTransforms = new HashSet<Transform>(GetRootTransform().GetAllDescendants().Where(t => !moving.Contains(t)));
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
        var renderers = t.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers.Length != 1)
            return false;
        if (renderers[0].sharedMaterials.Length == 0)
            return false;
        var material = renderers[0].sharedMaterials[0];
        if (material == null)
            return false;
        return material.HasProperty("_Length");
    }

    private bool IsTPSPenetratorRoot(Transform t)
    {
        if (t == null)
            return false;
        var renderers = t.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length != 1)
            return false;
        if (renderers[0].sharedMaterials.Length == 0)
            return false;
        var material = renderers[0].sharedMaterials[0];
        if (material == null)
            return false;
        return material.HasProperty("_TPSPenetratorEnabled") && material.GetFloat("_TPSPenetratorEnabled") > 0.5f;
    }

    private bool IsSPSPenetratorRoot(Transform t) {
		if(t == null)
			return false;
        var renderers = t.GetComponentsInChildren<Renderer>(true);
		if (renderers.Length != 1)
            return false;
		if(renderers[0].sharedMaterials.Length == 0)
			return false;
		var material = renderers[0].sharedMaterials[0];
		if(material == null)
			return false;
		return material.HasProperty("_SPS_Length");
	}

    private HashSet<Renderer> cache_FindAllPenetrators = null;
    public HashSet<Renderer> FindAllPenetrators()
    {
        if (cache_FindAllPenetrators != null)
            return cache_FindAllPenetrators;
        var root = GetRootTransform();
        var penetratorTipLights = root.GetComponentsInChildren<Light>(true)
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
        penetrators.UnionWith(root.GetComponentsInChildren<Renderer>(true).Where(m => IsTPSPenetratorRoot(m.transform) || IsSPSPenetratorRoot(m.transform)));
        return cache_FindAllPenetrators = penetrators;
    }

    public List<T> GetNonEditorOnlyComponentsInChildren<T>() where T : Component
    {
        var components = new List<T>();
        var stack = new Stack<Transform>();
        stack.Push(GetRootTransform());
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
        stack.Push(GetRootTransform());
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
        using var _ = log.IndentScope();
        LogToFile($"- '{texArray.name}' from {textures.Count} textures:");
        foreach (var tex in textures)
        {
            LogToFile($"- '{tex.name}' at path '{AssetDatabase.GetAssetPath(tex)}'", 1);
        }
        return texArray;
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
                    if (CanCombineTexturesError(subList[0], texArray[0]) == null)
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

    private Dictionary<string, Material> cache_GetFirstMaterialOnPath = null;
    public Material GetFirstMaterialOnPath(string path)
    {
        if (cache_GetFirstMaterialOnPath == null)
            cache_GetFirstMaterialOnPath = new Dictionary<string, Material>();
        if (cache_GetFirstMaterialOnPath.TryGetValue(path, out var mat))
            return mat;
        var renderer = GetTransformFromPath(path)?.GetComponent<Renderer>();
        if (renderer == null || renderer.sharedMaterials.Length == 0)
            return cache_GetFirstMaterialOnPath[path] = null;
        return cache_GetFirstMaterialOnPath[path] = renderer.sharedMaterials[0];
    }

    private Material[] CreateOptimizedMaterials(
        List<List<(Material mat, List<string> paths)>> sources,
        int meshToggleCount,
        string path,
        List<List<int>> mergedMeshIndices = null)
    {
        if (!(WritePropertiesAsStaticValues || sources.Any(list => list.Count > 1) || (meshToggleCount > 1 && MergeSkinnedMeshesWithShaderToggle)))
        {
            return sources.Select(list => list[0].mat).ToArray();
        }
        if (!fusedAnimatedMaterialProperties.TryGetValue(path, out var usedMaterialProps))
            usedMaterialProps = new HashSet<string>();
        if (mergedMeshIndices == null)
            mergedMeshIndices = sources.Select(s => Enumerable.Range(0, meshToggleCount).ToList()).ToList();
        HashSet<(string name, bool isVector)> defaultAnimatedProperties = null;
        var animatedPropertyOnMeshID = new Dictionary<string, bool[]>();
        oldPathToMergedPaths.TryGetValue(path, out var allOriginalMeshPaths);
        var sameAnimatedProperties = GetSameAnimatedPropertiesOnMergedMesh(path);
        var originalMeshPaths = sources.Select(l => l.SelectMany(t => t.paths).Distinct().ToList()).ToList();
        if (allOriginalMeshPaths != null && (sources.Count != 1 || sources[0].Count != 1)) {
            defaultAnimatedProperties = new HashSet<(string name, bool isVector)>();
            for (int i = 0; i < allOriginalMeshPaths.Count; i++) {
                Dictionary<string, Vector4> defaultValuesForCurrentPath = null;
                Material defaultMaterialForCurrentPath = GetFirstMaterialOnPath(allOriginalMeshPaths[i][0]);
                if (!animatedMaterialPropertyDefaultValues.TryGetValue(path, out defaultValuesForCurrentPath))
                {
                    defaultValuesForCurrentPath = new Dictionary<string, Vector4>();
                    animatedMaterialPropertyDefaultValues[path] = defaultValuesForCurrentPath;
                }
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
                        if (!animatedPropertyOnMeshID.TryGetValue(name, out var animatedOnMesh)) {
                            animatedOnMesh = new bool[allOriginalMeshPaths.Count];
                            animatedPropertyOnMeshID[name] = animatedOnMesh;
                        }
                        animatedOnMesh[i] = true;
                        if (defaultMaterialForCurrentPath != null && defaultMaterialForCurrentPath.HasProperty(name)) 
                        {
                            defaultValuesForCurrentPath[$"d4rkAvatarOptimizer{name}_ArrayIndex{i}"] = isVector
                                ? defaultMaterialForCurrentPath.GetVector(name)
                                : new Vector4(defaultMaterialForCurrentPath.GetFloat(name), 0, 0, 0);
                        }
                    }
                }
                defaultAnimatedProperties.Add(($"_IsActiveMesh{i}", false));
            }
        }
        if (!DoesFXLayerUseWriteDefaults())
            animatedPropertyOnMeshID = null;
        var materials = new Material[sources.Count];
        var parsedShader = new ParsedShader[sources.Count];
        var setShaderKeywords = new List<string>[sources.Count];
        var replace = new Dictionary<string, string>[sources.Count];
        var texturesToMerge = new HashSet<string>[sources.Count];
        var propertyTextureArrayIndex = new Dictionary<string, int>[sources.Count];
        var arrayPropertyValues = new Dictionary<string, (string type, List<string> values)>[sources.Count];
        var texturesToCheckNull = new Dictionary<string, string>[sources.Count];
        var animatedPropertyValues = new Dictionary<string, string>[sources.Count];
        var poiUsedPropertyDefines = new Dictionary<string, bool>[sources.Count];
        var stripShadowVariants = new bool[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            var source = sources[i].Select(t => t.mat).ToList();
            parsedShader[i] = ShaderAnalyzer.Parse(source[0]?.shader);
            if (parsedShader[i] == null || !parsedShader[i].parsedCorrectly)
            {
                materials[i] = source[0];
                continue;
            }
            
            stripShadowVariants[i] = source[0].renderQueue > 2500;
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
                        case ParsedShader.Property.Type.Integer:
                            if (!arrayPropertyValues[i].TryGetValue(prop.name, out propertyArray))
                            {
                                propertyArray.type = "int";
                                propertyArray.values = new List<string>();
                                arrayPropertyValues[i][prop.name] = propertyArray;
                            }
                            propertyArray.values.Add("" + mat.GetInteger(prop.name));
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
                        if (prop.type == ParsedShader.Property.Type.Integer)
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
                    stripShadowVariants[i],
                    animatedPropertyOnMeshID
                );
            }
        });
        Profiler.EndSection();

        LogToFile($"- Optimizing shaders for {sources.Count} material blobs on '{path}':");
        for (int i = 0; i < sources.Count; i++)
        {
            using var _ = log.IndentScope();
            var source = sources[i].Select(t => t.mat).ToList();
            if (parsedShader[i] == null || !parsedShader[i].parsedCorrectly)
            {
                var reason = parsedShader[i] == null ? "parsedShader is null" : parsedShader[i].errorMessage;
                LogToFile($"- Skipped optimization for material {(source[0] == null ? "null" : source[0].name)}: '{reason}'");
                continue;
            }

            DisplayProgressBar($"Optimizing shader {source[0].shader.name} ({i + 1}/{sources.Count})");
            var shaderName = optimizedShader[i].name;
            var shaderFilePath = $"{trashBinPath}{shaderName}.shader";
            if (!File.Exists(shaderFilePath))
            {
                foreach (var opt in optimizedShader[i].files)
                {
                    var filePath = shaderFilePath;
                    if (opt.name != "Shader")
                    {
                        filePath = $"{trashBinPath}{opt.name}";
                    }
                    File.WriteAllLines(filePath, opt.lines);
                    optimizedMaterialImportPaths.Add(filePath);
                }
            }
            var optimizedMaterial = new Material(Shader.Find("Unlit/Texture"));
            optimizedMaterial.shader = null;
            optimizedMaterial.name = $"m_{source[0].name}_{shaderName[2..]}";
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

            LogToFile($"- {(source.Count > 1 ? $"{source.Count} source materials for " : "")}{optimizedMaterial.name} with shader {shaderName}");
            for (int j = 0; j < source.Count && source.Count > 1; j++)
            {
                LogToFile($"- {source[j].name}", 1);
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
            mat.enableInstancing = source.enableInstancing;
            Profiler.StartNextSection("CopyMaterialProperties");
            for (int j = 0; j < source.shader.passCount; j++)
            {
                var lightModeValue = source.shader.FindPassTagValue(j, new ShaderTagId("LightMode"));
                if (!string.IsNullOrEmpty(lightModeValue.name))
                {
                    if (!source.GetShaderPassEnabled(lightModeValue.name))
                    {
                        mat.SetShaderPassEnabled(lightModeValue.name, false);
                    }
                }
            }
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
                mat.SetInteger(prop, source.GetInteger(prop));
            }
            var vrcFallback = source.GetTag("VRCFallback", false, "not_set");
            if (vrcFallback != "not_set")
            {
                mat.SetOverrideTag("VRCFallback", vrcFallback);
            }
            Profiler.EndSection();
        }

        var skinnedMeshRenderers = GetRootTransform().GetComponentsInChildren<SkinnedMeshRenderer>(true);
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
                        bool isVector = propName.EndsWith(".x") || propName.EndsWith(".r");
                        if (isVector) {
                            propName = propName.Substring(0, propName.Length - 2);
                        } else if (propName[propName.Length - 2] == '.') {
                            continue;
                        } else if (animatedProperties.Contains($"{propName}.x") || animatedProperties.Contains($"{propName}.r")) {
                            isVector = true;
                        }
                        for (int mID = 0; mID < meshCount; mID++)
                        {
                            var propArrayName = $"d4rkAvatarOptimizer{propName}_ArrayIndex{mID}";
                            if (!mat.HasProperty(propArrayName))
                                continue;
                            var signal = DoesFXLayerUseWriteDefaults() ? 0.0f : float.NaN;
                            if (isVector) {
                                mat.SetVector(propArrayName, new Vector4(signal, signal, signal, signal));
                            } else {
                                mat.SetFloat(propArrayName, signal);
                            }
                        }
                    }
                    if (DoesFXLayerUseWriteDefaults() && animatedMaterialPropertyDefaultValues.TryGetValue(GetPathToRoot(meshRenderer), out var defaultValues))
                    {
                        foreach (var defaultProp in defaultValues)
                        {
                            if (mat.HasFloat(defaultProp.Key))
                            {
                                mat.SetFloat(defaultProp.Key, defaultProp.Value.x);
                            }
                            else if (mat.HasVector(defaultProp.Key))
                            {
                                mat.SetVector(defaultProp.Key, defaultProp.Value);
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

    private string CanCombineTexturesError(Texture a, Texture b)
    {
        if (a == b)
            return null;
        if (a == null && b is Texture2D)
            return null;
        if (a is Texture2D && b == null)
            return null;
        if (!(a is Texture2D) || !(b is Texture2D))
            return a is Texture2D ? $"{b.name} is not a Texture2D" : $"{a.name} is not a Texture2D";
        if (a.texelSize != b.texelSize)
            return $"{a.name} and {b.name} have different texel sizes";
        var a2D = a as Texture2D;
        var b2D = b as Texture2D;
        if (a2D.format != b2D.format)
            return $"{a.name} and {b.name} have different texture formats";
        if (a2D.format == TextureFormat.DXT1Crunched || a2D.format == TextureFormat.DXT5Crunched)
            return $"{a.name} is using a crunched texture format which is not supported";
        if (b2D.format == TextureFormat.DXT1Crunched || b2D.format == TextureFormat.DXT5Crunched)
            return $"{b.name} is using a crunched texture format which is not supported";
        if (a2D.mipmapCount != b2D.mipmapCount)
            return $"{a.name} and {b.name} have different mipmap counts";
        if (a2D.filterMode != b2D.filterMode)
            return $"{a.name} and {b.name} have different filter modes";
        if (a2D.wrapMode != b2D.wrapMode)
            return $"{a.name} and {b.name} have different wrap modes";
        if (IsTextureLinear(a2D) != IsTextureLinear(b2D))
            return $"{a.name} and {b.name} have different color spaces";
        return null;
    }

    public string CanCombineMaterialsError(List<MaterialSlot> list, MaterialSlot candidate)
    {
        var candidateMat = candidate.material;
        var firstMat = list[0].material;
        if (candidateMat == null || firstMat == null)
            return "One of the materials is null";
        if (firstMat.shader != candidateMat.shader)
            return "Shaders do not match";
        if (list.Any(slot => slot.GetTopology() != candidate.GetTopology()))
            return "Topologies do not match";
        bool IsAffectedByMaterialSwap(MaterialSlot slot) =>
            slotSwapMaterials.ContainsKey((GetPathToRoot(slot.renderer), slot.index))
            || (materialSlotRemap.TryGetValue((GetPathToRoot(slot.renderer), slot.index), out var remap) && slotSwapMaterials.ContainsKey(remap));
        if (IsAffectedByMaterialSwap(list[0]) || IsAffectedByMaterialSwap(candidate))
            return "Affected by material swap";
        if (GetParticleSystemsUsingRenderer(candidate.renderer).Any(ps => ps.shape.useMeshMaterialIndex && ps.shape.meshMaterialIndex == candidate.index))
            return "Affected by particle system using mesh material index";
        if (GetParticleSystemsUsingRenderer(list[0].renderer).Any(ps => ps.shape.useMeshMaterialIndex && ps.shape.meshMaterialIndex == list[0].index))
            return "Affected by particle system using mesh material index";
        var listMaterials = list.Select(slot => slot.material).ToArray();
        var materialComparer = new MaterialAssetComparer();
        bool allTheSameAsCandidate = listMaterials.All(mat => materialComparer.Equals(mat, candidateMat));
        if (allTheSameAsCandidate || !MergeDifferentPropertyMaterials)
            return allTheSameAsCandidate ? null : "Not the same materials and merging different property materials is disabled";
        if (UsesUV0ZW(candidate.renderer) || list.Any(slot => UsesUV0ZW(slot.renderer)))
            return $"Renderer {GetPathToRoot(list.Concat(new[] { candidate }).First(slot => UsesUV0ZW(slot.renderer)).renderer)} uses uv0.zw";
        if (list.Count > 1 && listMaterials.Any(mat => mat == candidateMat))
            return null;
        for (int j = 0; j < firstMat.shader.passCount; j++)
        {
            var lightModeValue = firstMat.shader.FindPassTagValue(j, new ShaderTagId("LightMode"));
            if (!string.IsNullOrEmpty(lightModeValue.name))
            {
                if (firstMat.GetShaderPassEnabled(lightModeValue.name) != candidateMat.GetShaderPassEnabled(lightModeValue.name))
                {
                    return $"Shader pass enabled states for '{lightModeValue.name}' do not match";
                }
            }
        }
        var parsedShader = ShaderAnalyzer.Parse(candidateMat.shader);
        if (parsedShader.parsedCorrectly == false)
            return "Failed to parse shader correctly";
        if (firstMat.renderQueue != candidateMat.renderQueue)
            return "Render queues do not match";
        if (firstMat.enableInstancing != candidateMat.enableInstancing)
            return "Instancing settings do not match";
        bool hasAnyMaterialVariant = listMaterials.Any(m => m.isVariant) || candidateMat.isVariant;
        if (!hasAnyMaterialVariant && firstMat.GetTag("VRCFallback", false, "None") != candidateMat.GetTag("VRCFallback", false, "None"))
            return "VRCFallback tags do not match";
        foreach (var pass in parsedShader.passes)
        {
            if (pass.vertex == null)
                return "Vertex shader is missing";
            if (pass.hull != null || pass.domain != null)
                return "Tessellation is unsupported for merging with different properties";
            if (pass.fragment == null)
                return "Fragment shader is missing";
        }
        foreach (var keyword in parsedShader.shaderFeatureKeyWords)
        {
            if (firstMat.IsKeywordEnabled(keyword) == candidateMat.IsKeywordEnabled(keyword))
                continue;
            if (parsedShader.keywordToProperty.TryGetValue(keyword, out var prop))
                return $"Shader keyword '{keyword}' does not match\nKeyword is tied to property '{prop.name}' with display name '{prop.displayName}'";
            return $"Shader keyword '{keyword}' does not match";
        }
        listMaterials = new HashSet<Material>(listMaterials).ToArray();
        bool mergeTextures = MergeSameDimensionTextures && parsedShader.CanMergeTextures();
        foreach (var prop in parsedShader.propertiesToCheckWhenMerging)
        {
            switch (prop.type)
            {
                case ParsedShader.Property.Type.Float:
                    var candidateValue = candidateMat.GetFloat(prop.name);
                    if (listMaterials[0].GetFloat(prop.name) != candidateValue)
                        return $"Float property '{prop.name}' with display name '{prop.displayName}' does not match";
                    break;
                case ParsedShader.Property.Type.Integer:
                    var candidateIntValue = candidateMat.GetInteger(prop.name);
                    if (listMaterials[0].GetInteger(prop.name) != candidateIntValue)
                        return $"Integer property '{prop.name}' with display name '{prop.displayName}' does not match";
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
                        return $"Texture property '{prop.name}' with display name '{prop.displayName}' does not match";
                    if (mergeTexture)
                    {
                        var error = listMaterials.Select(mat => CanCombineTexturesError(cTex, mat.GetTexture(propertyID))).FirstOrDefault(err => err != null);
                        if (error != null)
                            return $"Texture property '{prop.name}' with display name '{prop.displayName}' cannot be combined: {error}";
                    }
                    break;
            }
        }
        return null;
    }

    private void OptimizeMaterialsOnNonSkinnedMeshes()
    {
        var meshRenderers = GetRootTransform().GetComponentsInChildren<MeshRenderer>(true);
        var exclusions = GetAllExcludedTransforms();
        meshRenderers = meshRenderers.Where(mr => !exclusions.Contains(mr.transform) && mr.GetSharedMesh() != null).ToArray();
        if (meshRenderers.Length == 0)
            return;
        LogToFile($"Optimizing materials on {meshRenderers.Length} MeshRenderers:");
        using var _ = log.IndentScope();
        foreach (var meshRenderer in meshRenderers)
        {
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
            var optimizeMaterialWrapper = toOptimize.Select(m => new List<(Material, List<string>)>() { (m, new List<string> { path } ) }).ToList();
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
        using var _ = new Profiler.Section("FindAllMergeAbleMaterials()");
        var matched = new List<List<MaterialSlot>>();
        foreach (var renderer in renderers)
        {
            foreach (var candidate in MaterialSlot.GetAllSlotsFrom(renderer))
            {
                bool foundMatch = false;
                for (int i = 0; i < matched.Count; i++)
                {
                    if (CanCombineMaterialsError(matched[i], candidate) == null)
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

        var skinnedMeshRenderers = GetRootTransform().GetComponentsInChildren<SkinnedMeshRenderer>(true);
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

        if (textureArrayLists.Count == 0)
            return;
        LogToFile($"Creating {textureArrayLists.Count} texture arrays:");
        foreach (var textureList in textureArrayLists)
        {
            textureArrays.Add(CombineTextures(textureList));
        }
    }
    
    private void CombineAndOptimizeMaterials()
    {
        var exclusions = GetAllExcludedTransforms();
        var skinnedMeshRenderers = GetRootTransform().GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(smr => !exclusions.Contains(smr.transform) && smr.sharedMesh != null).ToArray();
        if (skinnedMeshRenderers.Length == 0)
            return;
        LogToFile($"Combining and optimizing materials on {skinnedMeshRenderers.Length} SkinnedMeshRenderers:");
        using var _ = log.IndentScope();
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

            LogToFile($"- '{meshPath}' ({meshRenderer.sharedMaterials.Length} => {matchedSlots.Count})");
            using var __ = log.IndentScope();
            LogToFile($"- Material slot merge layout (slot id, material name):");
            foreach (var slotGroup in matchedSlots)
            {
                for (int k = 0; k < slotGroup.Count; k++)
                {
                    var slot = slotGroup[k];
                    LogToFile($"- {slot.index,2} {(slot.material == null ? "null" : slot.material.name)}", k == 0 ? 1 : 2);
                }
            }

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
                            targetUv[0].Add(new Vector4(sourceUv[0][oldIndex].x, sourceUv[0][oldIndex].y, sourceUv[0][oldIndex].z + internalMaterialID, sourceUv[0][oldIndex].w));
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
                bool particleSystemUsesMeshColor = GetParticleSystemsUsingRenderer(meshRenderer).Any(ps => ps.shape.useMeshColors);
                if (targetColor != null && (particleSystemUsesMeshColor || targetColor.Any(c => !c.Equals(Color.white))))
                {
                    newMesh.colors = targetColor.ToArray();
                }
                else if (targetColor32 != null && (particleSystemUsesMeshColor || targetColor32.Any(c => !c.Equals(new Color32(255, 255, 255, 255)))))
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

            var allSlots = matchedSlots.SelectMany(list => list).ToList();
            var uniqueMatchedMaterials = uniqueMatchedSlots.Select(list => list.Select(slot =>
                (slot.material, allSlots.Where(slot2 => slot2.material == slot.material).Select(slot2 => GetOriginalSlot((meshPath, slot2.index)).path).ToList())
            ).ToList()).ToList();
            var optimizedMaterials = CreateOptimizedMaterials(uniqueMatchedMaterials, meshCount > 1 ? meshCount : 0, meshPath, mergedMeshIndices);

            bool swapMaterialsOnMeshDisable = (MergeSkinnedMeshesWithShaderToggle || MergeSkinnedMeshesWithNaNimation)
                && mergedMeshIndices.SelectMany(l => l).Distinct().Count() > 1;

            for (int i = 0; i < uniqueMatchedMaterials.Count; i++)
            {
                if (uniqueMatchedMaterials[i][0].material == null)
                    continue;
                var originalSlot = GetOriginalSlot((meshPath, matchedSlots[i][0].index));
                if (uniqueMatchedMaterials[i].Count == 1)
                {
                    AddAnimationPathChange((originalSlot.path, $"m_Materials.Array.data[{originalSlot.index}]", typeof(SkinnedMeshRenderer)),
                        (meshPath, $"m_Materials.Array.data[{i}]", typeof(SkinnedMeshRenderer)));
                    if (!optimizedSlotSwapMaterials.TryGetValue(originalSlot, out var optimizedSwapMaterials))
                    {
                        optimizedSlotSwapMaterials[originalSlot] = optimizedSwapMaterials = new Dictionary<Material, Material>();
                    }
                    optimizedSwapMaterials[uniqueMatchedMaterials[i][0].material] = optimizedMaterials[i];
                }
                if (swapMaterialsOnMeshDisable && !slotSwapMaterials.ContainsKey(originalSlot) && mergedMeshIndices[i].Count == 1)
                {
                    if (!materialSlotsToDisableWhenOriginalPathMeshIsDisabled.TryGetValue(originalSlot.path, out var slotList))
                    {
                        materialSlotsToDisableWhenOriginalPathMeshIsDisabled[originalSlot.path] = slotList = new List<MaterialSlot>();
                    }
                    slotList.Add(new MaterialSlot(meshRenderer, i));
                }
            }

            meshRenderer.sharedMaterials = optimizedMaterials;

            foreach (var ps in GetParticleSystemsUsingRenderer(meshRenderer))
            {
                var shape = ps.shape;
                if (shape.useMeshMaterialIndex)
                {
                    shape.meshMaterialIndex = uniqueMatchedSlots.FindIndex(l => l.Any(slot => slot.index == shape.meshMaterialIndex));
                }
            }
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
        foreach (var transform in GetRootTransform().GetAllDescendants())
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
        transformFromOldPath = new Dictionary<string, Transform>();
        foreach (var t in GetRootTransform().GetAllDescendants())
        {
            transformFromOldPath[GetPathToRoot(t)] = t;
        }
        var avDescriptor = GetAvatarDescriptor();
        var combinableMeshList = FindPossibleSkinnedMeshMerges();
        oldPathToMergedPaths.Clear();
        oldPathToMergedPath.Clear();
        var exclusions = GetAllExcludedTransforms();
        // TODO: this map is currently unused.
        // It should be used to reparent bones that are non moving to the first parent that does when using "Delete Unused GameObjects"
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
        if (combinableSkinnedMeshList.Length == 0)
            return;
        var originalRootPosition = GetRootTransform().position;
        var originalRootRotation = GetRootTransform().rotation;
        GetRootTransform().SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        int totalMeshCount = combinableSkinnedMeshList.Sum(l => l.Count);
        int currentMeshCount = 0;
        LogToFile($"Combining {totalMeshCount} SkinnedMeshRenderers into {combinableSkinnedMeshList.Length} combined meshes:");
        using var _ = log.IndentScope();
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
            var targetBindPoses = new List<Matrix4x4>();
            var targetBoneIndexMap = new Dictionary<(int meshID, int boneID), int>();
            var transformToTargetIndices = new Dictionary<Transform, HashSet<int>>();

            int AddNewBone(Transform boneTransform, Matrix4x4 bindPose)
            {
                targetBones.Add(boneTransform);
                targetBindPoses.Add(bindPose);
                if (!transformToTargetIndices.TryGetValue(boneTransform, out var existingTransformIndices))
                {
                    transformToTargetIndices[boneTransform] = existingTransformIndices = new HashSet<int>();
                }
                existingTransformIndices.Add(targetBones.Count - 1);
                return targetBones.Count - 1;
            }
            int GetNewBoneIndex(int boneID, int meshID, Transform boneTransform, Matrix4x4 bindPose)
            {
                if (targetBoneIndexMap.TryGetValue((meshID, boneID), out int index))
                    return index;
                if (!transformToTargetIndices.TryGetValue(boneTransform, out var existingTransformIndices))
                {
                    transformToTargetIndices[boneTransform] = existingTransformIndices = new HashSet<int>();
                }
                foreach (var i in existingTransformIndices)
                {
                    if (targetBones[i] == boneTransform && targetBindPoses[i] == bindPose)
                    {
                        return targetBoneIndexMap[(meshID, boneID)] = i;
                    }
                }
                return targetBoneIndexMap[(meshID, boneID)] = AddNewBone(boneTransform, bindPose);
            }

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
            var targetBounds = combinableSkinnedMeshes[0].localBounds;
            var targetRootBone = combinableSkinnedMeshes[0].rootBone == null ? combinableSkinnedMeshes[0].transform : combinableSkinnedMeshes[0].rootBone;

            // if NaNimation is enabled check if target root bone is Head bone or a child of Head and if so reassign it to the Hip bone
            // we do this since NaNimation disables Update when Offscreen and the head gets scaled down locally
            // without this fix the merged mesh would disappear locally
            if (MergeSkinnedMeshesWithNaNimation && basicMergedMeshes.Count > 1)
            {
                var animator = avDescriptor.GetComponent<Animator>();
                if (animator != null && animator.isHuman)
                {
                    var headBone = animator.GetBoneTransform(HumanBodyBones.Head);
                    if (headBone != null && (targetRootBone == headBone || targetRootBone.IsChildOf(headBone)))
                    {
                        targetRootBone = animator.GetBoneTransform(HumanBodyBones.Hips);
                        if (targetRootBone == null)
                        {
                            targetRootBone = GetRootTransform();
                        }
                    }
                }
            }

            var toLocal = targetRootBone.worldToLocalMatrix;

            AddNewBone(combinableSkinnedMeshes[0].transform, combinableSkinnedMeshes[0].transform.worldToLocalMatrix);

            string newMeshName = combinableSkinnedMeshes[0].name;
            string newPath = GetPathToRoot(combinableSkinnedMeshes[0]);

            var basicMergedMeshesList = basicMergedMeshes.SelectMany(list => list.Cast<SkinnedMeshRenderer>()).ToList();
            var mergedMeshPaths = basicMergedMeshes.Select(list => list.Select(r => GetPathToRoot(r)).ToList()).ToList();
            basicMergedMeshesList.ForEach(r => oldPathToMergedPaths[GetPathToRoot(r)] = mergedMeshPaths);
            basicMergedMeshesList.ForEach(r => oldPathToMergedPath[GetPathToRoot(r)] = newPath);

            // this caches the results for later use when optimizing the materials
            basicMergedMeshesList.ForEach(r => GetFirstMaterialOnPath(GetPathToRoot(r)));

            int bindPoseMeshID = -1;

            LogToFile($"- Target path '{newPath}':");
            using var __ = log.IndentScope();
            if (combinableSkinnedMeshes.Count > 1)
            {
                LogToFile($"- Merging {combinableSkinnedMeshes.Count} skinned meshes:");
                foreach (var blob in basicMergedMeshes)
                {
                    for (int i = 0; i < blob.Count; i++)
                    {
                        LogToFile($"- {GetPathToRoot(blob[i])}", i == 0 ? 1 : 2);
                    }
                }
            }
            LogToFile($"- Basic mesh info:");
            using (log.IndentScope())
            {
                LogToFile($"- Total vertices: {totalVertexCount}");
                LogToFile($"- Total submeshes: {basicMergedMeshesList.Sum(m => m.sharedMesh.subMeshCount)}");
                LogToFile($"- Target root bone: '{GetPathToRoot(targetRootBone)}'");
                var usedAttributes = new Dictionary<string, int>();
                var sourceRootBones = new HashSet<string>();
                var sourceLightAnchors = new HashSet<string>();
                var sourcesWithExtraMaterialSlots = new HashSet<string>();
                foreach (var skinnedMesh in basicMergedMeshesList)
                {
                    var path = GetPathToRoot(skinnedMesh);
                    sourceRootBones.Add(skinnedMesh.rootBone == null ? path : GetPathToRoot(skinnedMesh.rootBone));
                    sourceLightAnchors.Add(skinnedMesh.probeAnchor == null ? path : GetPathToRoot(skinnedMesh.probeAnchor));
                    var mesh = skinnedMesh.sharedMesh;
                    for (int i = 0; i < mesh.vertexAttributeCount; i++)
                    {
                        var a = mesh.GetVertexAttribute(i);
                        var s = $"- {a.attribute,-12}  {a.dimension}x{a.format,-8}";
                        if (!usedAttributes.TryGetValue(s, out int count))
                            count = 0;
                        usedAttributes[s] = count + 1;
                    }
                    if (skinnedMesh.sharedMaterials.Length > mesh.subMeshCount)
                    {
                        sourcesWithExtraMaterialSlots.Add(path);
                    }
                }
                LogToFile($"- Source vertex attributes:");
                foreach ((var attr, var count) in usedAttributes.OrderBy(s => s.Key))
                {
                    LogToFile($"{attr} {count,3}", 1);
                }
                void LogList(HashSet<string> list, string title)
                {
                    if (list.Count == 1)
                    {
                        LogToFile($"- {title}: '{list.First()}'");
                    }
                    else
                    {
                        LogToFile($"- {title} ({list.Count}):");
                        foreach (var s in list)
                        {
                            LogToFile($"- {s}", 1);
                        }
                    }
                }
                LogList(sourceRootBones, "Source root bone");
                LogList(sourceLightAnchors, "Source light anchor");
                if (sourcesWithExtraMaterialSlots.Count > 0)
                    LogList(sourcesWithExtraMaterialSlots, "Extra material slot mesh");
            }

            foreach (SkinnedMeshRenderer skinnedMesh in basicMergedMeshesList)
            {
                DisplayProgressBar($"Combining mesh ({++currentMeshCount}/{totalMeshCount}) {skinnedMesh.name}");

                bindPoseMeshID++;
                var blobMeshID = basicMergedMeshes.FindIndex(blob => blob.Contains(skinnedMesh));
                var currentMeshPath = GetPathToRoot(skinnedMesh);
                var mesh = skinnedMesh.sharedMesh;
                var bindPoseIDMap = new Dictionary<int, int>();
                var indexOffset = targetVertices.Count;
                var sourceVertices = mesh.vertices;
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
                    Debug.LogWarning($"Bone count ({sourceBones.Length}) does not match bind pose count ({bindPoseCount}) on {currentMeshPath}");
                    LogToFile($"- Warning: Bone count ({sourceBones.Length}) does not match bind pose count ({bindPoseCount}) on {currentMeshPath}");
                    bindPoseCount = Math.Min(sourceBones.Length, bindPoseCount);
                }
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
                        && CanUseNaNimationOnMesh(currentMeshPath))
                {
                    NaNimationBone = new GameObject("NaNimationBone").transform;
                    var pathToRoot = currentMeshPath.Replace('/', '_');
                    var siblingNames = new HashSet<string>(GetRootTransform().Cast<Transform>().Select(t => t.name));
                    var nameCandidate = "NaNimation " + pathToRoot;
                    int i = 1;
                    while (siblingNames.Contains(nameCandidate))
                    {
                        nameCandidate = "NaNimation " + pathToRoot + " " + i++;
                    }
                    NaNimationBone.name = nameCandidate;
                    NaNimationBone.parent = GetRootTransform();
                    NaNimationBone.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                    NaNimationBone.localScale = Vector3.one;
                    NaNimationBoneIndex = AddNewBone(NaNimationBone, NaNimationBone.worldToLocalMatrix);
                    string key = "NaNimation";
                    if (MergeSkinnedMeshesWithShaderToggle)
                    {
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
                }
                else if (basicMergedMeshes.Count > 1 && MergeSkinnedMeshesWithShaderToggle)
                {
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
                    sourceBindPoses = new Matrix4x4[1] { Matrix4x4.identity };
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

                var sourceUv = new List<Vector4>();
                mesh.GetUVs(0, sourceUv);
                if (sourceUv.Count != sourceVertices.Length)
                {
                    sourceUv = Enumerable.Repeat(Vector4.zero, sourceVertices.Length).ToList();
                }

                if (mesh.HasVertexAttribute(VertexAttribute.Color))
                {
                    if (useColor32)
                    {
                        targetColor32.AddRange(mesh.colors32);
                    }
                    else
                    {
                        targetColor.AddRange(mesh.colors);
                    }
                }
                else
                {
                    if (useColor32)
                    {
                        targetColor32.AddRange(Enumerable.Repeat(new Color32(255, 255, 255, 255), sourceVertices.Length));
                    }
                    else
                    {
                        targetColor.AddRange(Enumerable.Repeat(Color.white, sourceVertices.Length));
                    }
                }

                sourceNormals = sourceNormals.Length != sourceVertices.Length ? new Vector3[sourceVertices.Length] : sourceNormals;
                sourceTangents = sourceTangents.Length != sourceVertices.Length ? new Vector4[sourceVertices.Length] : sourceTangents;

                if (!blendShapesToBake.TryGetValue(skinnedMesh, out var blendShapeIDs))
                {
                    blendShapeIDs = new List<int>();
                }

                if (blendShapeIDs.Count > 0)
                {
                    LogToFile($"- Baking {blendShapeIDs.Count} blendshapes from '{currentMeshPath}':");
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
                    LogToFile($"- {mesh.GetBlendShapeName(blendShapeID)} ({weight * 100f:F1}%)", 1);
                }

                for (int vertIndex = 0; vertIndex < sourceVertices.Length; vertIndex++)
                {
                    int GetNewBoneIndexForCurrentMesh(int oldIndex)
                    {
                        oldIndex = oldIndex >= bindPoseCount ? 0 : Math.Max(0, oldIndex);
                        if (!bindPoseIDMap.TryGetValue(oldIndex, out int newIndex))
                        {
                            newIndex = GetNewBoneIndex(oldIndex, bindPoseMeshID, sourceBones[oldIndex], sourceBindPoses[oldIndex]);
                            bindPoseIDMap[oldIndex] = newIndex;
                        }
                        return newIndex;
                    }
                    var boneWeight = sourceWeights[vertIndex];
                    boneWeight.boneIndex0 = GetNewBoneIndexForCurrentMesh(boneWeight.boneIndex0);
                    boneWeight.boneIndex1 = GetNewBoneIndexForCurrentMesh(boneWeight.boneIndex1);
                    boneWeight.boneIndex2 = GetNewBoneIndexForCurrentMesh(boneWeight.boneIndex2);
                    boneWeight.boneIndex3 = GetNewBoneIndexForCurrentMesh(boneWeight.boneIndex3);
                    if (NaNimationBoneIndex != -1)
                    {
                        var sum = boneWeight.weight0 + boneWeight.weight1 + boneWeight.weight2;
                        sum = sum == 0 ? 1 : sum;
                        boneWeight.weight0 /= sum;
                        boneWeight.weight1 /= sum;
                        boneWeight.weight2 /= sum;
                        boneWeight.weight3 = 0;
                        if (boneWeight.weight1 == 0)
                        {
                            boneWeight.boneIndex1 = NaNimationBoneIndex;
                            boneWeight.weight1 = 1e-35f;
                        }
                        else if (boneWeight.weight2 == 0)
                        {
                            boneWeight.boneIndex2 = NaNimationBoneIndex;
                            boneWeight.weight2 = 1e-35f;
                        }
                        else
                        {
                            boneWeight.boneIndex3 = NaNimationBoneIndex;
                            boneWeight.weight3 = 1e-35f;
                        }
                    }
                    targetWeights.Add(boneWeight);
                    targetVertices.Add(sourceVertices[vertIndex]);
                    targetNormals.Add(sourceNormals[vertIndex]);
                    targetTangents.Add(sourceTangents[vertIndex]);
                    targetUv[0].Add(new Vector4(sourceUv[vertIndex].x, sourceUv[vertIndex].y, sourceUv[vertIndex].z + (blobMeshID << 12), sourceUv[vertIndex].w));
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
            combinedMesh.indexFormat = targetVertices.Count >= 65536 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            combinedMesh.SetVertices(targetVertices);
            combinedMesh.bindposes = targetBindPoses.ToArray();
            combinedMesh.SetBoneWeights(targetWeights.ToArray());
            bool hasParticleSystemUsingMeshColor = basicMergedMeshesList.Any(r => GetParticleSystemsUsingRenderer(r).Any(ps => ps.shape.useMeshColors));
            if (!useColor32 && (hasParticleSystemUsingMeshColor || targetColor.Any(c => !c.Equals(Color.white))))
            {
                combinedMesh.colors = targetColor.ToArray();
            }
            else if (useColor32 && (hasParticleSystemUsingMeshColor || targetColor32.Any(c => !c.Equals(new Color32(255, 255, 255, 255)))))
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
                            targetDeltaVertices[vertIndex] = sourceDeltaVertices[k];
                            targetDeltaNormals[vertIndex] = sourceDeltaNormals[k];
                            targetDeltaTangents[vertIndex] = sourceDeltaTangents[k];
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
                    LogToFile($"- Merging {mergedBlendShapes.Count} blendshapes into {name}");
                    bool first = true;
                    foreach (var toMerge in mergedBlendShapes)
                    {
                        var path = toMerge.blendshape[..toMerge.blendshape.IndexOf("/blendShape.")];
                        var blendShapeName = toMerge.blendshape[(path.Length + 12)..];
                        LogToFile($"- {path}.{blendShapeName} with weight {toMerge.weight * 100:F2}%", 1);
                        var skinnedMesh = GetTransformFromPath(path).GetComponent<SkinnedMeshRenderer>();
                        var mesh = skinnedMesh.sharedMesh;
                        var blendShapeID = mesh.GetBlendShapeIndex(blendShapeName);
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
                            targetDeltaVertices[vertIndex] += sourceDeltaVertices[k] * toMerge.weight;
                            targetDeltaNormals[vertIndex] += sourceDeltaNormals[k] * toMerge.weight;
                            targetDeltaTangents[vertIndex] += sourceDeltaTangents[k] * toMerge.weight;
                        }
                    }
                    combinedMesh.AddBlendShapeFrame(name, 100, targetDeltaVertices, targetDeltaNormals, targetDeltaTangents);
                }
            }
            Profiler.EndSection();
            
            var targetRenderer = combinableSkinnedMeshes[0];

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
                            avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh = targetRenderer;
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
                    avDescriptor.VisemeSkinnedMesh = targetRenderer;
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
                if (targetRenderer.HasPropertyBlock())
                    targetRenderer.GetPropertyBlock(properties);
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
                targetRenderer.SetPropertyBlock(properties);
            }

            var materials = basicMergedMeshesList.SelectMany(r => r.sharedMaterials).ToArray();
            var originalMeshSlots = basicMergedMeshesList.SelectMany(r => MaterialSlot.GetAllSlotsFrom(r)).ToList();
            foreach (var renderer in basicMergedMeshesList)
            {
                foreach (var ps in GetParticleSystemsUsingRenderer(renderer))
                {
                    var shape = ps.shape;
                    shape.skinnedMeshRenderer = targetRenderer;
                    if (shape.useMeshMaterialIndex)
                    {
                        shape.meshMaterialIndex = originalMeshSlots.FindIndex(s => s.renderer == renderer && s.index == shape.meshMaterialIndex);
                    }
                }
            }
            targetRenderer.rootBone = targetRootBone;
            targetRenderer.sharedMesh = combinedMesh;
            targetRenderer.sharedMaterials = materials;
            targetRenderer.bones = targetBones.ToArray();
            targetRenderer.localBounds = targetBounds;

            foreach (var blendShape in blendShapeWeights)
            {
                for (int j = 0; j < combinedMesh.blendShapeCount; j++)
                {
                    if (blendShape.Key == combinedMesh.GetBlendShapeName(j))
                    {
                        targetRenderer.SetBlendShapeWeight(j, blendShape.Value);
                        break;
                    }
                }
            }

            if (basicMergedMeshes.Count > 1)
            {
                var go = targetRenderer.gameObject;
                var children = go.transform.Cast<Transform>().ToList();
                var componentsToMove = go.GetComponents<Component>().Where(c => !(c is Transform) && !(c is SkinnedMeshRenderer)).ToList();
                if (children.Count > 0 || componentsToMove.Count > 0)
                {
                    var subContainer = new GameObject("d4rkAO_mergeTargetRoot");
                    subContainer.transform.parent = go.transform;
                    subContainer.transform.localPosition = Vector3.zero;
                    subContainer.transform.localRotation = Quaternion.identity;
                    subContainer.transform.localScale = Vector3.one;
                    subContainer.SetActive(targetRenderer.gameObject.activeSelf);
                    transformFromOldPath[GetPathToRoot(go)] = subContainer.transform;

                    foreach (Transform child in children)
                    {
                        child.parent = subContainer.transform;
                    }

                    foreach (Component comp in componentsToMove)
                    {
                        UnityEditorInternal.ComponentUtility.CopyComponent(comp);
                        UnityEditorInternal.ComponentUtility.PasteComponentAsNew(subContainer);
                        DestroyImmediate(comp);
                    }
                }
                else
                {
                    pathsToDeleteGameObjectTogglesOn.Add(GetPathToRoot(go));
                }

                if (MergeSkinnedMeshesSeparatedByDefaultEnabledState && !GetRendererDefaultEnabledState(targetRenderer))
                {
                    targetRenderer.gameObject.SetActive(true);
                    targetRenderer.enabled = false;
                    var curveBinding = EditorCurveBinding.FloatCurve(GetPathToRoot(targetRenderer), typeof(SkinnedMeshRenderer), "m_Enabled");
                    constantAnimatedValuesToAdd[curveBinding] = 1f;
                }
                else
                {
                    targetRenderer.gameObject.SetActive(true);
                    targetRenderer.enabled = true;
                }
            }

            for (int meshID = 1; meshID < combinableSkinnedMeshes.Count; meshID++)
            {
                var obj = combinableSkinnedMeshes[meshID].gameObject;
                DestroyImmediate(combinableSkinnedMeshes[meshID]);
                if (!keepTransforms.Contains(obj.transform) && obj.transform.childCount == 0 && obj.GetNonNullComponents().Length == 1)
                    DestroyImmediate(obj);
            }

            Profiler.StartSection("AssetDatabase.SaveAssets()");
            AssetDatabase.SaveAssets();
            Profiler.EndSection();
        }
        GetRootTransform().SetPositionAndRotation(originalRootPosition, originalRootRotation);

        // flush particle system cache since we merged meshes
        cache_ParticleSystemsUsingRenderer = null;
    }

    private HashSet<Transform> cache_GetAllExcludedTransforms;
    public HashSet<Transform> GetAllExcludedTransforms() {
        if (cache_GetAllExcludedTransforms != null)
            return cache_GetAllExcludedTransforms;
        var allExcludedTransforms = new HashSet<Transform>();
        var automaticExclusions = new List<string>() {
            "_VirtualLens_Root",
        }.Select(s => GetTransformFromPath(s)).ToList();
        automaticExclusions.AddRange(GetRootTransform().GetComponentsInChildren<VRCContactSender>(true)
            .Where(c => c.collisionTags.Any(t => t == "superneko.realkiss.contact.mouth"))
            .Select(c => c.transform.parent)
            .Where(t => t != null)
            .Select(t => t.Cast<Transform>().FirstOrDefault(child => child.TryGetComponent(out SkinnedMeshRenderer _)))
            .Where(t => t != null));
        automaticExclusions.AddRange(FindAllPenetrators().Select(p => p.transform));
        automaticExclusions = automaticExclusions.Where(t => t != null).ToList();
        if (automaticExclusions.Count > 0) {
            LogToFile($"Automatically excluding {automaticExclusions.Count} transforms from optimization:");
            foreach (var t in automaticExclusions) {
                LogToFile($"- {GetPathToRoot(t)}", 1);
            }
        }
        var manualExclusions = ExcludeTransforms.Where(t => t != null).ToList();
        if (manualExclusions.Count > 0) {
            LogToFile($"Excluding {manualExclusions.Count} user-specified transforms from optimization:");
            foreach (var t in manualExclusions) {
                LogToFile($"- {GetPathToRoot(t)}", 1);
            }
        }
        foreach (var excludedTransform in manualExclusions.Concat(automaticExclusions)) {
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
                    if (!visitedIds.Add(iterator.managedReferenceId))
                    {
                        enterChildren = false;
                    }
                }
            }
            return referencedTransforms;
        }
    }

    private void DestroyEditorOnlyGameObjects()
    {
        var stack = new Stack<Transform>();
        stack.Push(GetRootTransform());
        var deletedPaths = new List<string>();
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.gameObject.CompareTag("EditorOnly"))
            {
                deletedPaths.Add(GetPathToRoot(current));
                DestroyImmediate(current.gameObject);
                continue;
            }
            foreach (Transform child in current)
            {
                stack.Push(child);
            }
        }
        if (deletedPaths.Count > 0)
        {
            LogToFile($"Deleted {deletedPaths.Count} EditorOnly GameObjects:");
            foreach (var path in deletedPaths)
            {
                LogToFile($"- {path}", 1);
            }
        }
    }

    private void DestroyUnusedComponents()
    {
        if (!DeleteUnusedComponents)
            return;
        var list = FindAllUnusedComponents().Where(c => c != null).ToList();
        if (list.Count == 0)
            return;
        var typesToDeleteInSecondPass = new HashSet<Type>() {
            typeof(Rigidbody),
            typeof(AudioSource),
        };
        LogToFile($"Deleting {list.Count} unused components:");
        foreach (var component in list.Where(c => c != null && !typesToDeleteInSecondPass.Contains(c.GetType()))
                            .Concat(list.Where(c => c != null && typesToDeleteInSecondPass.Contains(c.GetType()))))
        {
            if (component == null)
                continue;
            LogToFile($"- {component.GetType().Name} on {GetPathToRoot(component.transform)}", 1);
            DestroyImmediate(component);
        }
    }

    private void DestroyUnusedGameObjects()
    {
        if (!DeleteUnusedGameObjects)
            return;

        var used = new HashSet<Transform>();

        var movingTransforms = FindAllMovingTransforms();
        used.UnionWith(movingTransforms);
        used.UnionWith(movingTransforms.Select(t => t != null ? t.parent : null));

        var root = GetRootTransform();
        used.Add(root);
        used.UnionWith(root.GetComponentsInChildren<Animator>(true)
            .Select(a => a.transform.Find("Armature")).Where(t => t != null));
        used.UnionWith(root.Cast<Transform>().Where(t => t.name.StartsWithSimple("NaNimation ")));

        foreach (var contact in root.GetComponentsInChildren<ContactBase>(true))
        {
            used.Add(contact.GetRootTransform());
            used.Add(contact.GetRootTransform().parent);
        }

        foreach (var physBone in root.GetComponentsInChildren<VRCPhysBoneBase>(true))
        {
            used.Add(physBone.GetRootTransform());
            used.Add(physBone.GetRootTransform().parent);
            used.UnionWith(physBone.ignoreTransforms);
        }

        foreach (var collider in root.GetComponentsInChildren<VRCPhysBoneColliderBase>(true))
        {
            used.Add(collider.GetRootTransform());
            used.Add(collider.GetRootTransform().parent);
        }

        foreach (var c in root.GetComponentsInChildren<Component>(true).Where(c => c != null && !(c is Transform)))
        {
            used.Add(c.transform);
            if (c.GetType().Name.Contains("Constraint"))
            {
                used.Add(c.transform.parent);
            }
            used.UnionWith(FindReferencedTransforms(c));
        }

        // the vrc finger colliders depend on their relative position to their parent, so we need to keep their parents around too
        var avDescriptor = GetAvatarDescriptor();
        var fingerColliders = new List<VRCAvatarDescriptor.ColliderConfig>() {
            avDescriptor.collider_fingerIndexL,
            avDescriptor.collider_fingerIndexR,
            avDescriptor.collider_fingerMiddleL,
            avDescriptor.collider_fingerMiddleR,
            avDescriptor.collider_fingerRingL,
            avDescriptor.collider_fingerRingR,
            avDescriptor.collider_fingerLittleL,
            avDescriptor.collider_fingerLittleR,
        }.Select(c => c.transform).Where(t => t != null);
        used.UnionWith(fingerColliders.Select(c => c.parent).Where(t => t != null));

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
        queue.Enqueue(root);

        var deletedPaths = new List<string>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (Transform child in current)
            {
                queue.Enqueue(child);
            }
            if (!used.Contains(current))
            {
                deletedPaths.Add(GetPathToRoot(current));
                foreach (var child in current.Cast<Transform>().ToArray())
                {
                    child.parent = current.parent;
                    child.name = $"{current.name}_{child.name}";
                }
                DestroyImmediate(current.gameObject);
            }
        }

        if (deletedPaths.Count > 0)
        {
            LogToFile($"Deleted {deletedPaths.Count} unused GameObjects:");
            foreach (var path in deletedPaths)
            {
                LogToFile($"- {path}", 1);
            }
        }
    }

    private void MoveRingFingerColliderToFeet()
    {
        if (!UseRingFingerAsFootCollider)
            return;
        var avDescriptor = GetAvatarDescriptor();

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
        LogToFile($"Moved left ring finger collider to '{GetPathToRoot(collider.transform)}'");

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
        LogToFile($"Moved right ring finger collider to '{GetPathToRoot(collider.transform)}'");

        // disable collider foldout in the inspector because it resets the collider transform
        EditorPrefs.SetBool("VRCSDK3_AvatarDescriptorEditor3_CollidersFoldout", false);
    }

    private void ConvertStaticMeshesToSkinnedMeshes()
    {
        if (!MergeStaticMeshesAsSkinned)
            return;
        var staticMeshes = GetRootTransform().GetComponentsInChildren<MeshFilter>(true)
            .Where(f => f.sharedMesh != null && f.gameObject.GetComponent<MeshRenderer>() != null)
            .Where(f => f.gameObject.layer != 12)
            .Select(f => f.gameObject).Distinct().ToList();
        var meshesThatGetCombinedWithOtherMeshes = new HashSet<Renderer>(FindPossibleSkinnedMeshMerges().Where(l => l.Count > 1).SelectMany(l => l));
        var meshesToConvert = staticMeshes.Where(obj => meshesThatGetCombinedWithOtherMeshes.Contains(obj.GetComponent<Renderer>())).ToList();
        if (meshesToConvert.Count > 0)
            LogToFile($"Converting {meshesToConvert.Count} static meshes to skinned meshes for merging:");

        foreach (var obj in meshesToConvert)
        {
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
            LogToFile($"- {GetPathToRoot(obj)}", 1);
        }
    }

    public class MaterialAssetComparer : IEqualityComparer<Material> {
        private static Dictionary<(Material a, Material b), bool> comparisonCache = new ();
        public static void ClearCache() => comparisonCache.Clear();
        public bool Equals(Material a, Material b) {
            if (a == b)
                return true;
            if (a == null || b == null)
                return false;
            if (a.shader != b.shader)
                return false;
            if (comparisonCache.TryGetValue((a, b), out bool cachedResult))
                return cachedResult;
            
            try {
            if (a.renderQueue != b.renderQueue)
                return false;
            if (a.doubleSidedGI != b.doubleSidedGI)
                return false;
            if (a.enableInstancing != b.enableInstancing)
                return false;
            if (a.globalIlluminationFlags != b.globalIlluminationFlags)
                return false;
            
            var aKeywords = a.shaderKeywords;
            var bKeywords = b.shaderKeywords;
            if (aKeywords.Length != bKeywords.Length)
                return false;
            Array.Sort(aKeywords);
            Array.Sort(bKeywords);
            if (!aKeywords.SequenceEqual(bKeywords))
                return false;

            for (int i = 0; i < a.shader.passCount; i++) {
                var lightModeValue = a.shader.FindPassTagValue(i, new ShaderTagId("LightMode"));
                if (!string.IsNullOrEmpty(lightModeValue.name)) {
                    if (a.GetShaderPassEnabled(lightModeValue.name) != b.GetShaderPassEnabled(lightModeValue.name)) {
                        return false;
                    }
                }
            }

            string[] aFloats = a.GetPropertyNames(MaterialPropertyType.Float);
            string[] bFloats = b.GetPropertyNames(MaterialPropertyType.Float);
            if (!aFloats.SequenceEqual(bFloats))
                return false;
            if (!aFloats.Select(x => a.GetFloat(x)).SequenceEqual(bFloats.Select(x => b.GetFloat(x))))
                return false;

            string[] aInts = a.GetPropertyNames(MaterialPropertyType.Int);
            string[] bInts = b.GetPropertyNames(MaterialPropertyType.Int);
            if (!aInts.SequenceEqual(bInts))
                return false;
            if (!aInts.Select(x => a.GetInteger(x)).SequenceEqual(bInts.Select(x => b.GetInteger(x))))
                return false;

            string[] aVectors = a.GetPropertyNames(MaterialPropertyType.Vector);
            string[] bVectors = b.GetPropertyNames(MaterialPropertyType.Vector);
            if (!aVectors.SequenceEqual(bVectors))
                return false;
            if (!aVectors.Select(x => a.GetVector(x)).SequenceEqual(bVectors.Select(x => b.GetVector(x))))
                return false;

            string[] aTextures = a.GetPropertyNames(MaterialPropertyType.Texture);
            string[] bTextures = b.GetPropertyNames(MaterialPropertyType.Texture);
            if (!aTextures.SequenceEqual(bTextures))
                return false;
            if (!aTextures.Select(x => a.GetTexture(x)).SequenceEqual(bTextures.Select(x => b.GetTexture(x))))
                return false;

            string[] aMatrices = a.GetPropertyNames(MaterialPropertyType.Matrix);
            string[] bMatrices = b.GetPropertyNames(MaterialPropertyType.Matrix);
            if (!aMatrices.SequenceEqual(bMatrices))
                return false;
            if (!aMatrices.Select(x => a.GetMatrix(x)).SequenceEqual(bMatrices.Select(x => b.GetMatrix(x))))
                return false;
            } finally {
                comparisonCache[(a, b)] = comparisonCache[(b, a)] = false;
            }

            return comparisonCache[(a, b)] = comparisonCache[(b, a)] = true;
        }

        public int GetHashCode(Material m) {
            int hash = m.shader.GetHashCode() ^ m.renderQueue;
            if (m.HasTexture("_MainTex")) {
                var tex = m.GetTexture("_MainTex");
                if (tex != null) {
                    hash ^= tex.GetHashCode();
                }
            }
            if (m.HasProperty("_Color")) {
                hash ^= m.GetColor("_Color").GetHashCode();
            }
            return hash;
        }
    }

    private void DeduplicateMaterials()
    {
        var allRenderers = GetRootTransform().GetComponentsInChildren<Renderer>(true);
        var exclusions = GetAllExcludedTransforms();
        allRenderers = allRenderers.Where(r => !exclusions.Contains(r.transform)).ToArray();
        
        var allUsedMaterials = allRenderers.SelectMany(x => x.sharedMaterials).Where(m => m != null).Distinct().ToArray();
        var materialGroups = allUsedMaterials.GroupBy(x => x, new MaterialAssetComparer()).ToList();

        var deduplicatedMaterialLookup = new Dictionary<Material, Material>();
        foreach (var group in materialGroups) {
            Material finalMaterial = group.Key;
            foreach (var mat in group) {
                deduplicatedMaterialLookup[mat] = finalMaterial;
            }
        }

        foreach (var renderer in allRenderers) {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) {
                if (materials[i] != null && deduplicatedMaterialLookup.TryGetValue(materials[i], out var newMaterial)) {
                    materials[i] = newMaterial;
                }
            }
            renderer.sharedMaterials = materials;
        }
    }
#endif
}
