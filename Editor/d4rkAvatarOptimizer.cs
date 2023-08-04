using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

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
using AnimationPath = System.ValueTuple<string, string, System.Type>;
using BlendableLayer = VRC.SDKBase.VRC_AnimatorLayerControl.BlendableLayer;
#endif

[HelpURL("https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/blob/main/README.md")]
public class d4rkAvatarOptimizer : MonoBehaviour
#if HAS_IEDITOR_ONLY
, VRC.SDKBase.IEditorOnly
#endif
{
    public bool DoAutoSettings = true;
    public bool OptimizeOnUpload = true;
    public bool WritePropertiesAsStaticValues = true;
    public bool MergeSkinnedMeshes = true;
    public bool MergeStaticMeshesAsSkinned = true;
    public bool ForceMergeBlendShapeMissMatch = false;
    public bool KeepMaterialPropertyAnimationsSeparate = true;
    public bool MergeDifferentPropertyMaterials = true;
    public bool MergeSameDimensionTextures = true;
    public bool MergeBackFaceCullingWithCullingOff = false;
    public bool MergeDifferentRenderQueue = false;
    public bool KeepMMDBlendShapes = false;
    public bool DeleteUnusedComponents = true;
    public bool DeleteUnusedGameObjects = false;
    public bool OptimizeFXLayer = true;
    public bool CombineApproximateMotionTimeAnimations = false;
    public bool DisablePhysBonesWhenUnused = true;
    public bool MergeSameRatioBlendShapes = true;
    public bool UseRingFingerAsFootCollider = false;
    public bool ProfileTimeUsed = false;
    public bool ShowExcludedTransforms = false;
    public List<Transform> ExcludeTransforms = new List<Transform>();
    public bool ShowMeshAndMaterialMergePreview = true;
    public bool ShowFXLayerMergeResults = true;
    public bool ShowFXLayerMergeErrors = false;
    public bool ShowDebugInfo = false;
    public bool DebugShowUnparsableMaterials = true;
    public bool DebugShowUnmergableMaterials = true;
    public bool DebugShowUnmergableTextureMaterials = true;
    public bool DebugShowCrunchedTextures = true;
    public bool DebugShowNonBC5NormalMaps = true;
    public bool DebugShowLockedInMaterials = true;
    public bool DebugShowUnlockedMaterials = true;
    public bool DebugShowPenetrators = true;
    public bool DebugShowPhysBoneDependencies = true;
    public bool DebugShowUnusedComponents = true;
    public bool DebugShowAlwaysDisabledGameObjects = true;
    public bool DebugShowMaterialSwaps = true;
    public bool DebugShowAnimatedMaterialPropertyPaths = true;
    public bool DebugShowGameObjectsWithToggle = true;
    public bool DebugShowUnmovingBones = false;

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
            DisplayProgressBar("Parsing Shaders", 0.01f);
            ShaderAnalyzer.ParseAndCacheAllShaders(gameObject, true, (done, total) => DisplayProgressBar($"Parsing Shaders ({done}/{total})", 0.01f + 0.14f * done / total));
            DisplayProgressBar("Clear TrashBin Folder", 0.15f);
            ClearTrashBin();
            optimizedMaterials.Clear();
            optimizedMaterialImportPaths.Clear();
            optimizedSlotSwapMaterials.Clear();
            newAnimationPaths.Clear();
            texArrayPropertiesToSet.Clear();
            keepTransforms.Clear();
            convertedMeshRendererPaths.Clear();
            DisplayProgressBar("Destroying unused components", 0.19f);
            Profiler.StartSection("DestroyEditorOnlyGameObjects()");
            DestroyEditorOnlyGameObjects();
            Profiler.StartNextSection("DestroyUnusedComponents()");
            physBonesToDisable = FindAllPhysBonesToDisable();
            DestroyUnusedComponents();
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
            Profiler.EndSection();
            DisplayProgressBar("Done", 1.0f);
            MoveRingFingerColliderToFeet();
            DestroyImmediate(this);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = oldCulture;
            Thread.CurrentThread.CurrentUICulture = oldUICulture;
            EditorUtility.ClearProgressBar();
        }
    }

    private static string packageRootPath = "Assets/d4rkAvatarOptimizer";
    private static string trashBinPath = "Assets/d4rkAvatarOptimizer/TrashBin/";
    private static HashSet<string> usedBlendShapes = new HashSet<string>();
    private static Dictionary<SkinnedMeshRenderer, List<int>> blendShapesToBake = new Dictionary<SkinnedMeshRenderer, List<int>>();
    private static Dictionary<AnimationPath, AnimationPath> newAnimationPaths = new Dictionary<AnimationPath, AnimationPath>();
    private static List<Material> optimizedMaterials = new List<Material>();
    private static List<string> optimizedMaterialImportPaths = new List<string>();
    private static List<List<string>> mergedMeshPaths = new List<List<string>>();
    private static Dictionary<string, List<string>> physBonesToDisable = new Dictionary<string, List<string>>();
    private static Dictionary<(string path, int slot), HashSet<Material>> slotSwapMaterials = new Dictionary<(string, int), HashSet<Material>>();
    private static Dictionary<(string path, int slot), Dictionary<Material, Material>> optimizedSlotSwapMaterials = new Dictionary<(string, int), Dictionary<Material, Material>>();
    private static Dictionary<(string path, int index), (string path, int index)> materialSlotRemap = new Dictionary<(string, int), (string, int)>();
    private static Dictionary<string, HashSet<string>> animatedMaterialProperties = new Dictionary<string, HashSet<string>>();
    private static Dictionary<string, HashSet<string>> fusedAnimatedMaterialProperties = new Dictionary<string, HashSet<string>>();
    private static List<List<Texture2D>> textureArrayLists = new List<List<Texture2D>>();
    private static List<Texture2DArray> textureArrays = new List<Texture2DArray>();
    private static Dictionary<Material, List<(string name, Texture2DArray array)>> texArrayPropertiesToSet = new Dictionary<Material, List<(string name, Texture2DArray array)>>();
    private static HashSet<string> gameObjectTogglePaths = new HashSet<string>();
    private static HashSet<Transform> keepTransforms = new HashSet<Transform>();
    private static HashSet<SkinnedMeshRenderer> hasUsedBlendShapes = new HashSet<SkinnedMeshRenderer>();
    private static HashSet<string> convertedMeshRendererPaths = new HashSet<string>();
    private static Dictionary<Transform, Transform> movingParentMap = new Dictionary<Transform, Transform>();
    private static Dictionary<string, Transform> transformFromOldPath = new Dictionary<string, Transform>();
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
        assetBundlePath = null;
        Profiler.EndSection();
    }

    private static string assetBundlePath = null;
    private void CreateUniqueAsset(Object asset, string name)
    {
        Profiler.StartSection("AssetDatabase.CreateAsset()");
        var invalids = System.IO.Path.GetInvalidFileNameChars();
        var sanitizedName = string.Join("_", name.Split(invalids, System.StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        bool assetIsBundleable = asset is Material || asset is AnimationClip;
        if (assetIsBundleable && assetBundlePath != null)
        {
            AssetDatabase.AddObjectToAsset(asset, assetBundlePath);
        }
        else
        {
            var path = AssetDatabase.GenerateUniqueAssetPath(trashBinPath + sanitizedName);
            if (assetIsBundleable && assetBundlePath == null)
            {
                assetBundlePath = path;
            }
            AssetDatabase.CreateAsset(asset, path);
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
            path = t.name + "/" + path;
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

    private static bool IsMaterialReadyToCombineWithOtherMeshes(Material material)
    {
        return material == null ? false : ShaderAnalyzer.Parse(material.shader).CanMerge();
    }

    private bool IsCombinableRenderer(Renderer candidate)
    {
        if (candidate.TryGetComponent(out Cloth cloth))
            return false;
        if (candidate is MeshRenderer && (candidate.gameObject.layer == 12 || !MergeStaticMeshesAsSkinned))
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

    private bool CanCombineRendererWith(List<Renderer> list, Renderer candidate)
    {
        if (!MergeSkinnedMeshes)
            return false;
        if (!IsCombinableRenderer(list[0]))
            return false;
        if (!IsCombinableRenderer(candidate))
            return false;
        if (list[0].gameObject.layer != candidate.gameObject.layer)
            return false;
        if (!ForceMergeBlendShapeMissMatch && (hasUsedBlendShapes.Contains(list[0]) ^ hasUsedBlendShapes.Contains(candidate)))
            return false;
        var paths = list.Select(smr => GetPathToRoot(smr.transform.parent)).ToList();
        var t = candidate.transform;
        while ((t = t.parent) != transform)
        {
            var path = GetPathToRoot(t);
            if (gameObjectTogglePaths.Contains(path) && paths.Any(p => !p.StartsWith(path)))
                return false;
        }
        return true;
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
            foreach (var layer in controller.layers)
            {
                if (layer.avatarMask != null)
                    return true;
            }
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
            if (!keepTransforms.Contains(obj.transform) && (obj.transform.childCount == 0 && obj.GetComponents<Component>().Length == 1))
                DestroyImmediate(obj);
        }
    }

    public List<List<Renderer>> FindPossibleSkinnedMeshMerges()
    {
        var unused = FindAllUnusedComponents();
        gameObjectTogglePaths = FindAllGameObjectTogglePaths();
        slotSwapMaterials = FindAllMaterialSwapMaterials();
        var renderers = GetComponentsInChildren<Renderer>(true);
        var matchedSkinnedMeshes = new List<List<Renderer>>();
        var exclusions = GetAllExcludedTransforms();
        var penetrators = FindAllPenetrators();
        foreach (var renderer in renderers)
        {
            if (renderer.gameObject.CompareTag("EditorOnly") || unused.Contains(renderer) || renderer.sharedMaterials.Length == 0)
                continue;

            bool foundMatch = false;
            foreach (var subList in matchedSkinnedMeshes)
            {
                if (exclusions.Contains(renderer.transform) || renderer is ParticleSystemRenderer || renderer.GetSharedMesh() == null || penetrators.Contains(renderer))
                    break;
                if (exclusions.Contains(subList[0].transform))
                    continue;
                if (CanCombineRendererWith(subList, renderer))
                {
                    subList.Add(renderer);
                    foundMatch = true;
                    break;
                }
            }
            if (!foundMatch)
            {
                matchedSkinnedMeshes.Add(new List<Renderer> { renderer });
            }
        }
        var avDescriptor = GetComponent<VRCAvatarDescriptor>();
        foreach (var subList in matchedSkinnedMeshes)
        {
            if (subList.Count == 1)
                continue;
            int index = subList.FindIndex(smr => smr == avDescriptor?.VisemeSkinnedMesh);
            if (index == -1)
            {
                var obj = subList.OrderBy(smr => GetPathToRoot(smr).Count(c => c == '/')).First();
                index = subList.IndexOf(obj);
            }
            var oldFirst = subList[0];
            subList[0] = subList[index];
            subList[index] = oldFirst;
        }
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
        return matchedSkinnedMeshes;
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
                return bones.Count - 1;
            }
        }
        bones.Add(transform);
        bindPoses.Add(transform.localToWorldMatrix);
        return bones.Count - 1;
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
        return FixAnimationBindingPath(newBinding, ref changed);
    }
    
    private AnimationClip FixAnimationClipPaths(AnimationClip clip)
    {

        var newClip = Instantiate(clip);
        newClip.ClearCurves();
        newClip.name = clip.name;
        bool changed = false;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            AnimationUtility.SetEditorCurve(newClip, FixAnimationBinding(binding, ref changed), curve);
            bool addPhysBoneCurves = binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName == "m_Enabled";
            if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive")
            {
                AnimationUtility.SetEditorCurve(newClip, FixAnimationBindingPath(binding, ref changed), curve);
                addPhysBoneCurves = true;
            }
            if (addPhysBoneCurves && physBonesToDisable.ContainsKey(binding.path))
            {
                var physBoneBinding = binding;
                physBoneBinding.propertyName = "m_Enabled";
                physBoneBinding.type = typeof(VRCPhysBone);
                foreach (var physBonePath in physBonesToDisable[binding.path])
                {
                    physBoneBinding.path = physBonePath;
                    AnimationUtility.SetEditorCurve(newClip, FixAnimationBindingPath(physBoneBinding, ref changed), curve);
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
            AnimationUtility.SetObjectReferenceCurve(newClip, newBinding, curve);
        }
        if (changed)
        {
            CreateUniqueAsset(newClip, newClip.name + ".anim");
            return newClip;
        }
        return clip;
    }

    private Motion FixMotion(Motion motion, Dictionary<Motion, Motion> fixedMotions, string assetPath, AnimationClip dummyClip)
    {
        if (motion == null)
            return dummyClip;
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
                childNodes[j].motion = FixMotion(childNodes[j].motion, fixedMotions, assetPath, dummyClip);
            }
            newTree.children = childNodes;
            fixedMotions[motion] = newTree;
            newTree.hideFlags = HideFlags.HideInHierarchy;
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

        var dummyAnimationToFillEmptyStates = AssetDatabase.LoadAssetAtPath<AnimationClip>(packageRootPath + "/Editor/Assets/DummyAnimationToFillEmptyStates.anim");
        
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
            var errors = AnalyzeFXLayerMergeAbility();
            var uselessLayers = FindUselessFXLayers();
            int currentLayer = 0;
            for (int i = 0; i < GetFXLayer().layers.Length; i++)
            {
                fxLayerMap[i] = currentLayer;
                if (uselessLayers.Contains(i))
                {
                    fxLayersToDestroy.Add(i);
                    continue;
                }
                if (errors[i].Count == 0)
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

        for (int i = 0; i < avDescriptor.baseAnimationLayers.Length; i++)
        {
            var controller = avDescriptor.baseAnimationLayers[i].animatorController as AnimatorController;
            if (controller == null)
                continue;
            layerCopyPaths[i] = $"{trashBinPath}BaseAnimationLayer{i}{controller.name}(OptimizedCopy).controller";
            optimizedControllers[i] = i == 4
                ? AnimatorOptimizer.Run(controller, layerCopyPaths[i], fxLayerMap, fxLayersToMerge, fxLayersToDestroy)
                : AnimatorOptimizer.Copy(controller, layerCopyPaths[i], fxLayerMap);
        }
        AssetDatabase.SaveAssets();

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
                state.motion = FixMotion(state.motion, fixedMotions, layerCopyPaths[i], dummyAnimationToFillEmptyStates);
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

    public List<List<string>> AnalyzeFXLayerMergeAbility()
    {
        var fxLayer = GetFXLayer();
        if (fxLayer == null)
            return new List<List<string>>();
        var avDescriptor = GetComponent<VRCAvatarDescriptor>();

        var errorMessages = fxLayer.layers.Select(layer => new List<string>()).ToList();

        for (int i = 0; i < avDescriptor.baseAnimationLayers.Length; i++)
        {
            var controller = avDescriptor.baseAnimationLayers[i].animatorController as AnimatorController;
            if (controller == null)
                continue;
            for (int j = 0; j < controller.layers.Length; j++)
            {
                var layer = controller.layers[j];
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

        var fxLayerBindings = fxLayer.layers.Select(layer => GetAllCurveBindings(layer.stateMachine)).ToList();

        for (int i = 0; i < fxLayer.layers.Length; i++)
        {
            var layer = fxLayer.layers[i];
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
            if (states.Length == 0 || states.Length > 2)
            {
                errorMessages[i].Add($"{states.Length} states");
                continue;
            }

            if (states.Length == 2) // check for toggle layer
            {
                var param = states.SelectMany(s => s.state.transitions).SelectMany(t => t.conditions).Select(c => c.parameter).Distinct().ToList();
                var matchedParams = param.Select(p => fxLayer.parameters.FirstOrDefault(p2 => p2.name == p)).ToList();
                if (matchedParams.Any(p => p == null))
                {
                    errorMessages[i].Add($"some parameter not found");
                    continue;
                }
                if (matchedParams.Any(p => p.type != AnimatorControllerParameterType.Bool))
                {
                    errorMessages[i].Add($"some parameter is not bool");
                    continue;
                }
                if (states.Any(s => s.state.transitions.Length != 1)) // check for 2 bool toggle
                {
                    if (states.Sum(s => s.state.transitions.Length) != 3)
                    {
                        errorMessages[i].Add($"not exactly 3 transitions");
                        continue;
                    }
                    if (param.Count != 2)
                    {
                        errorMessages[i].Add($"not exactly 2 parameters");
                        continue;
                    }
                    var singleTransitionState = states.First(s => s.state.transitions.Length == 1).state;
                    var doubleTransitionState = states.First(s => s.state.transitions.Length == 2).state;
                    if (singleTransitionState.transitions[0].conditions.Length != 2)
                    {
                        errorMessages[i].Add($"single transition has not exactly 2 conditions");
                        continue;
                    }
                    if (doubleTransitionState.transitions.Any(t => t.conditions.Length != 1))
                    {
                        errorMessages[i].Add($"double transition has not exactly 1 condition");
                        continue;
                    }
                    foreach (var condition in singleTransitionState.transitions[0].conditions)
                    {
                        var oppositeCondition = doubleTransitionState.transitions.FirstOrDefault(t => t.conditions[0].parameter == condition.parameter);
                        if (oppositeCondition == null)
                        {
                            errorMessages[i].Add($"double transition {condition.parameter} does not have opposite condition");
                            continue;
                        }
                        if (oppositeCondition.conditions[0].mode == condition.mode)
                        {
                            errorMessages[i].Add($"double transition {condition.parameter} has same condition mode as single transition");
                            continue;
                        }
                    }
                }
                else // simple 1 bool toggle
                {
                    if (states.Any(s => s.state.transitions[0].conditions.Length != 1))
                    {
                        errorMessages[i].Add($"multiple transition conditions");
                        continue;
                    }
                    if (states[0].state.transitions[0].conditions[0].parameter != states[1].state.transitions[0].conditions[0].parameter)
                    {
                        errorMessages[i].Add($"transition condition parameter is not the same");
                        continue;
                    }
                    if (states[0].state.transitions[0].conditions[0].mode == states[1].state.transitions[0].conditions[0].mode)
                    {
                        errorMessages[i].Add($"transition condition condition is the same");
                        continue;
                    }
                }
                bool onlyBoolBindings = true;
                bool reliesOnWriteDefaults = false;
                for (int j = 0; j < 2; j++)
                {
                    var state = states[j].state;
                    if (!state.transitions.All(t => t.destinationState == states[1 - j].state))
                    {
                        errorMessages[i].Add($"{state} transition destination state is not the other state");
                        break;
                    }
                    if (state.transitions.Any(t => t.hasExitTime && t.exitTime != 0.0f))
                    {
                        errorMessages[i].Add($"{state} transition has exit time");
                        break;
                    }
                    if (state.motion == null)
                    {
                        if (states[1 - j].state.motion == null)
                        {
                            errorMessages[i].Add($"both states have no motion");
                            break;
                        }
                        else if (!states[1 - j].state.writeDefaultValues)
                        {
                            errorMessages[i].Add($"state {j} has no motion but {1 - j} does not have write defaults");
                            break;
                        }
                        else
                        {
                            reliesOnWriteDefaults = true;
                        }
                        continue;
                    }
                    var clip = state.motion as AnimationClip;
                    if (clip == null)
                    {
                        errorMessages[i].Add($"{state.name} is not an animation clip");
                        continue;
                    }
                    if (state.timeParameterActive)
                    {
                        errorMessages[i].Add($"{state.name} has motion time");
                        continue;
                    }
                    var bindings = AnimationUtility.GetCurveBindings(clip);
                    foreach (var binding in bindings)
                    {
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        var initialValue = curve.keys[0].value;
                        if (curve.keys.Any(k => k.value != initialValue))
                        {
                            errorMessages[i].Add($"{state.name} {binding.path}.{binding.propertyName} ({binding.type.Name}) is not a constant curve");
                        }
                        if (binding.propertyName != "m_Enabled" && binding.propertyName != "m_IsActive")
                        {
                            onlyBoolBindings = false;
                        }
                    }
                    var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                    foreach (var binding in objectBindings)
                    {
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        errorMessages[i].Add($"{state.name} {binding.path}.{binding.propertyName} ({binding.type.Name}) is an object reference curve");
                    }
                }
                if (reliesOnWriteDefaults && !onlyBoolBindings)
                {
                    errorMessages[i].Add($"relies on write defaults and animates something other than m_Enabled/m_IsActive");
                }
                if (states.Any(s => s.state.transitions[0].duration != 0.0f) && !onlyBoolBindings)
                {
                    errorMessages[i].Add($"transition has non 0 duration and animates something other than m_Enabled/m_IsActive");
                }
            }
            
            if (states.Length == 1) // check for motion time layer
            {
                if (!CombineApproximateMotionTimeAnimations)
                {
                    errorMessages[i].Add($"no motion time combination enabled");
                    continue;
                }
                var state = states[0].state;
                var clip = state.motion as AnimationClip;
                if (clip == null)
                {
                    errorMessages[i].Add($"{state.name} is not an animation clip");
                    continue;
                }
                if (!state.timeParameterActive)
                {
                    errorMessages[i].Add($"{state.name} has no motion time");
                    continue;
                }
                if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Any())
                {
                    errorMessages[i].Add($"{state.name} has object reference curves");
                    continue;
                }
                var bindings = AnimationUtility.GetCurveBindings(clip);
                var keyframes = new HashSet<float>();
                foreach (var binding in bindings)
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    keyframes.UnionWith(curve.keys.Select(k => k.time));
                }
                if (keyframes.Count < 2)
                {
                    errorMessages[i].Add($"{state.name} has less than 2 keyframes");
                    continue;
                }
            }

            for (int j = 0; j < fxLayer.layers.Length; j++)
            {
                if (i == j)
                    continue;
                foreach (var binding in fxLayerBindings[i])
                {
                    if (fxLayerBindings[j].Contains(binding))
                    {
                        errorMessages[i].Add($"{binding.path} ({binding.type.Name}) is used in {j} {fxLayer.layers[j].name}");
                    }
                }
            }
        }
        return errorMessages;
    }

    public HashSet<int> FindUselessFXLayers()
    {
        var fxLayer = GetFXLayer();
        if (fxLayer == null)
            return new HashSet<int>();
        var avDescriptor = GetComponent<VRCAvatarDescriptor>();

        var isAffectedByLayerWeightControl = new HashSet<int>();

        for (int i = 0; i < avDescriptor.baseAnimationLayers.Length; i++)
        {
            var controller = avDescriptor.baseAnimationLayers[i].animatorController as AnimatorController;
            if (controller == null)
                continue;
            for (int j = 0; j < controller.layers.Length; j++)
            {
                var stateMachine = controller.layers[j].stateMachine;
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

        int lastNonUselessLayer = fxLayer.layers.Length;
        for (int i = fxLayer.layers.Length - 1; i >= 0; i--)
        {
            var layer = fxLayer.layers[i];
            var stateMachine = layer.stateMachine;
            bool isNotFirstLayerOrLastNonUselessLayerCanBeFirst = i != 0 || (lastNonUselessLayer < fxLayer.layers.Length && fxLayer.layers[lastNonUselessLayer].defaultWeight == 1 && !isAffectedByLayerWeightControl.Contains(lastNonUselessLayer));
            if (stateMachine == null)
            {
                if (isNotFirstLayerOrLastNonUselessLayerCanBeFirst)
                {
                    uselessLayers.Add(i);
                }
                continue;
            }
            if (stateMachine.EnumerateAllBehaviours().Any())
            {
                lastNonUselessLayer = i;
                continue;
            }
            if (i != 0 && layer.defaultWeight == 0 && !isAffectedByLayerWeightControl.Contains(i))
            {
                uselessLayers.Add(i);
                continue;
            }
            if (isNotFirstLayerOrLastNonUselessLayerCanBeFirst && stateMachine.stateMachines.Length == 0 && stateMachine.states.Length == 0)
            {
                uselessLayers.Add(i);
                continue;
            }
            lastNonUselessLayer = i;
        }

        return uselessLayers;
    }

    private HashSet<AnimationClip> GetAllUsedFXLayerAnimationClips()
    {
        var fxLayer = GetFXLayer();
        if (fxLayer == null)
            return new HashSet<AnimationClip>();
        var unusedLayers = FindUselessFXLayers();
        var usedClips = new HashSet<AnimationClip>();
        for (int i = 0; i < fxLayer.layers.Length; i++)
        {
            var stateMachine = fxLayer.layers[i].stateMachine;
            if (stateMachine == null || unusedLayers.Contains(i))
                continue;
            foreach (var state in stateMachine.EnumerateAllStates())
            {
                if (state.motion == null)
                    continue;
                usedClips.UnionWith(state.motion.EnumerateAllClips());
            }
        }
        return usedClips;
    }

    public Dictionary<VRCPhysBoneBase, HashSet<Object>> FindAllPhysBoneDependencies()
    {
        var result = new Dictionary<VRCPhysBoneBase, HashSet<Object>>();
        var physBonePath = new Dictionary<string, VRCPhysBoneBase>();
        var physBones = GetComponentsInChildren<VRCPhysBoneBase>(true);
        foreach (var physBone in physBones)
        {
            result.Add(physBone, new HashSet<Object>());
            physBonePath[GetPathToRoot(physBone)] = physBone;
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
            var bones = new HashSet<Transform>();
            var meshBones = skinnedMesh.bones;
            var boneWeights = skinnedMesh.sharedMesh.boneWeights;
            for (int i = 0; i < boneWeights.Length; i++)
            {
                if (boneWeights[i].weight0 > 0)
                    bones.Add(meshBones[boneWeights[i].boneIndex0]);
                if (boneWeights[i].weight1 > 0)
                    bones.Add(meshBones[boneWeights[i].boneIndex1]);
                if (boneWeights[i].weight2 > 0)
                    bones.Add(meshBones[boneWeights[i].boneIndex2]);
                if (boneWeights[i].weight3 > 0)
                    bones.Add(meshBones[boneWeights[i].boneIndex3]);
            }
            foreach (var bone in bones)
            {
                AddDependency(bone, skinnedMesh);
            }
        }
        foreach (var constraint in GetComponentsInChildren<Behaviour>(true).OfType<IConstraint>())
        {
            for (int i = 0; i < constraint.sourceCount; i++)
            {
                AddDependency(constraint.GetSource(i).sourceTransform, constraint as Object);
            }
            AddDependency((constraint as LookAtConstraint)?.worldUpObject, constraint as Object);
            AddDependency((constraint as AimConstraint)?.worldUpObject, constraint as Object);
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
            var exclusions = new HashSet<Transform>(physBone.ignoreTransforms);
            var stack = new Stack<Transform>();
            if (physBone.multiChildType == VRCPhysBoneBase.MultiChildType.Ignore && root.childCount > 1)
            {
                foreach (Transform child in root)
                {
                    stack.Push(child);
                }
            }
            else
            {
                stack.Push(root);
            }
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (exclusions.Contains(current))
                    continue;
                if (transformToDependency.TryGetValue(current, out var dependencies))
                {
                    result[physBone].UnionWith(dependencies);
                }
                foreach (Transform child in current)
                {
                    stack.Push(child);
                }
            }
        }

        return result;
    }

    public Dictionary<string, List<string>> FindAllPhysBonesToDisable()
    {
        var result = new Dictionary<string, List<string>>();
        if (!DisablePhysBonesWhenUnused)
            return result;
        var physBoneDependencies = FindAllPhysBoneDependencies();
        foreach(var entry in physBoneDependencies)
        {
            if (entry.Value.Count == 1 && entry.Value.First() is SkinnedMeshRenderer)
            {
                var targetPath = GetPathToRoot(entry.Value.First() as SkinnedMeshRenderer);
                if (!result.TryGetValue(targetPath, out var physBones))
                {
                    result[targetPath] = physBones = new List<string>();
                }
                physBones.Add(GetPathToRoot(entry.Key));
            }
        }
        return result;
    }

    public Dictionary<(string path, int index), HashSet<Material>> FindAllMaterialSwapMaterials()
    {
        var result = new Dictionary<(string path, int index), HashSet<Material>>();
        var fxLayer = GetFXLayer();
        if (fxLayer == null)
            return result;
        foreach (var clip in fxLayer.animationClips)
        {
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (!typeof(Renderer).IsAssignableFrom(binding.type)
                    || !binding.propertyName.StartsWith("m_Materials.Array.data["))
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
        return result;
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
            var currentMergedMeshes = mergedMeshPaths.FirstOrDefault(list => list.Any(path => path == entry.Key.path));
            if (currentMergedMeshes != null)
            {
                mergedMeshCount = currentMergedMeshes.Count;
                meshIndex = currentMergedMeshes.IndexOf(entry.Key.path);
                targetPath = currentMergedMeshes[0];
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

    public AnimatorController GetFXLayer()
    {
        var avDescriptor = GetComponent<VRCAvatarDescriptor>();
        if (avDescriptor == null || avDescriptor.baseAnimationLayers.Length != 5)
            return null;
        return avDescriptor.baseAnimationLayers[4].animatorController as AnimatorController;
    }

    public void CalculateUsedBlendShapePaths()
    {
        usedBlendShapes.Clear();
        hasUsedBlendShapes.Clear();
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
                        hasUsedBlendShapes.Add(meshRenderer);
                    }
                }
            }
            var fxLayer = GetFXLayer();
            if (fxLayer != null)
            {
                foreach (var clip in fxLayer.animationClips)
                {
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type != typeof(SkinnedMeshRenderer)
                            || !binding.propertyName.StartsWith("blendShape."))
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
                        hasUsedBlendShapes.Add(smr);
                    }
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
                if (KeepMMDBlendShapes && MMDBlendShapes.Contains(name))
                {
                    usedBlendShapes.Add(path + name);
                    hasUsedBlendShapes.Add(skinnedMeshRenderer);
                    continue;
                }
                if (skinnedMeshRenderer.GetBlendShapeWeight(i) != 0 && !usedBlendShapes.Contains(path + name))
                {
                    if (mesh.GetBlendShapeFrameCount(i) > 1)
                    {
                        usedBlendShapes.Add(path + name);
                        hasUsedBlendShapes.Add(skinnedMeshRenderer);
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
        var mergeableBlendShapes = new List<List<(string blendshape, float value)>>();
        var avDescriptor = GetComponent<VRCAvatarDescriptor>();
        var fxLayer = GetFXLayer();
        if (avDescriptor == null || fxLayer == null)
            return mergeableBlendShapes;
        var exclusions = GetAllExcludedTransforms();
        var validPaths = new HashSet<string>();
        var ratios = new List<Dictionary<string, float>>() { new Dictionary<string, float>() };
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
                if (KeepMMDBlendShapes && MMDBlendShapes.Contains(name))
                    continue;
                if (mesh.GetBlendShapeFrameCount(i) == 1)
                {
                    validPaths.Add(path + name);
                    ratios[0][path + name] = skinnedMeshRenderer.GetBlendShapeWeight(i);
                }
            }
        }
        if (validPaths.Count == 0)
            return mergeableBlendShapes;
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
        foreach (var clip in fxLayer.animationClips)
        {
            var blendShapes = new List<(string path, EditorCurveBinding binding)>();
            var keyframes = new HashSet<float>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(SkinnedMeshRenderer)
                    || !binding.propertyName.StartsWith("blendShape."))
                    continue;
                var path = $"{binding.path}/{binding.propertyName}";
                if (!validPaths.Contains(path))
                    continue;
                blendShapes.Add((path, binding));
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                keyframes.UnionWith(curve.keys.Select(x => x.time));
            }
            foreach (var key in keyframes)
            {
                var blendShapeValues = new Dictionary<string, float>();
                foreach (var blendShape in blendShapes)
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, blendShape.binding);
                    blendShapeValues[blendShape.path] = curve.Evaluate(key);
                }
                NormalizeBlendShapeValues(blendShapeValues);
                if (!ratios.Any(list => list.SequenceEqual(blendShapeValues)))
                    ratios.Add(blendShapeValues);
            }
            foreach (var blendshape in blendShapes)
            {
                if (!mergeableBlendShapes.Any(x => x[0].blendshape == blendshape.path))
                    mergeableBlendShapes.Add(new List<(string blendshape, float value)>() { (blendshape.path, 1) });
            }
        }
        for (int i = 0; i < mergeableBlendShapes.Count - 1; i++)
        {
            for (int j = i + 1; j < mergeableBlendShapes.Count; j++)
            {
                var subList = mergeableBlendShapes[i];
                var candidate = mergeableBlendShapes[j][0].blendshape;
                float value = -1;
                if (ratios.All(x => TryAddBlendShapeToSubList(subList, candidate, ref value, x)) && value != -1)
                {
                    subList.Add((candidate, value));
                    NormalizeBlendShapeValues(subList);
                    mergeableBlendShapes.RemoveAt(j);
                    j--;
                }
            }
        }
        mergeableBlendShapes.RemoveAll(x => x.Count == 1);
        return mergeableBlendShapes.Select(x => x.OrderByDescending(y => y.value).ToList()).ToList();
    }

    private void NormalizeBlendShapeValues(List<(string blendshape, float value)> blendShapeValues)
    {
        var maxValue = blendShapeValues.Max(x => x.value);
        if (maxValue == 0 || maxValue == 1)
            return;
        for (int i = 0; i < blendShapeValues.Count; i++)
        {
            blendShapeValues[i] = (blendShapeValues[i].blendshape, blendShapeValues[i].value / maxValue);
        }
    }

    private void NormalizeBlendShapeValues(Dictionary<string, float> blendShapeValues)
    {
        var maxValue = blendShapeValues.Max(x => x.Value);
        if (maxValue == 0 || maxValue == 1)
            return;
        foreach (var key in blendShapeValues.Keys.ToList())
        {
            blendShapeValues[key] /= maxValue;
        }
    }

    private bool TryAddBlendShapeToSubList(List<(string blendshape, float value)> subList, string blendshape, ref float value, Dictionary<string, float> list)
    {
        int intersectionCount = 0;
        float intersectionMax = 0;
        for (int i = 0; i < subList.Count; i++)
        {
            if (list.TryGetValue(subList[i].blendshape, out var match))
            {
                intersectionCount++;
                intersectionMax = Mathf.Max(intersectionMax, match);
            }
            else if (intersectionCount > 0)
            {
                return false;
            }
        }
        bool hasCandidate = list.ContainsKey(blendshape);
        if (intersectionCount == 0 && !hasCandidate)
            return true;
        if (intersectionCount != subList.Count || !hasCandidate)
            return false;
        if (intersectionMax == 0)
            return true;
        var candidateValue = list[blendshape];
        if (candidateValue == 0)
            return false;
        for (int i = 0; i < subList.Count; i++)
        {
            var match = list[subList[i].blendshape];
            if (Mathf.Abs(subList[i].value - match / intersectionMax) > 0.01f)
                return false;
        }
        if (value < 0)
            value = candidateValue / intersectionMax;
        else if (Mathf.Abs(value - candidateValue / intersectionMax) > 0.01f)
            return false;
        return true;
    }

    public Dictionary<string, HashSet<string>> FindAllAnimatedMaterialProperties()
    {
        var map = new Dictionary<string, HashSet<string>>();
        var fxLayer = GetFXLayer();
        if (fxLayer == null)
            return map;
        foreach (var binding in fxLayer.animationClips.SelectMany(clip => AnimationUtility.GetCurveBindings(clip)))
        {
            if (!binding.propertyName.StartsWith("material.") ||
                (binding.type != typeof(SkinnedMeshRenderer) && binding.type != typeof(MeshRenderer)))
                continue;
            if (!map.TryGetValue(binding.path, out var props))
            {
                map[binding.path] = (props = new HashSet<string>());
            }
            var propName = binding.propertyName.Substring(9);
            if (propName.Length > 2 && propName[propName.Length - 2] == '.')
            {
                props.Add(propName.Substring(0, propName.Length - 2));
            }
            props.Add(propName);
        }
        return map;
    }

    public HashSet<string> FindAllGameObjectTogglePaths()
    {
        var togglePaths = new HashSet<string>();
        var fxLayer = GetFXLayer();
        if (fxLayer == null)
            return new HashSet<string>();
        foreach (var binding in fxLayer.animationClips.SelectMany(clip => AnimationUtility.GetCurveBindings(clip)))
        {
            if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive")
                togglePaths.Add(binding.path);
        }
        return togglePaths;
    }

    public HashSet<Transform> FindAllAlwaysDisabledGameObjects()
    {
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
        return disabledGameObjects;
    }

    public HashSet<Component> FindAllUnusedComponents()
    {
        var fxLayer = GetFXLayer();
        if (fxLayer == null)
            return new HashSet<Component>();
        var behaviourToggles = new HashSet<string>();
        foreach (var binding in fxLayer.animationClips.SelectMany(clip => AnimationUtility.GetCurveBindings(clip)))
        {
            if (typeof(Behaviour).IsAssignableFrom(binding.type) && binding.propertyName == "m_Enabled")
            {
                behaviourToggles.Add(binding.path);
            }
        }

        var alwaysDisabledBehaviours = new HashSet<Component>(GetComponentsInChildren<Behaviour>(true)
            .Where(b => b != null && !b.enabled)
            .Where(b => !(b is VRCPhysBoneColliderBase))
            .Where(b => !behaviourToggles.Contains(GetPathToRoot(b))));
        
        var usedPhysBoneColliders = GetComponentsInChildren<VRCPhysBoneBase>(true)
            .Where(pb => !alwaysDisabledBehaviours.Contains(pb))
            .SelectMany(pb => pb.colliders);

        alwaysDisabledBehaviours.UnionWith(GetComponentsInChildren<VRCPhysBoneColliderBase>(true)
            .Where(c => !usedPhysBoneColliders.Contains(c)));

        alwaysDisabledBehaviours.UnionWith(FindAllAlwaysDisabledGameObjects()
            .SelectMany(t => t.GetComponents<Component>().Where(c => c != null && !(c is Transform))));

        var exclusions = GetAllExcludedTransforms();
        alwaysDisabledBehaviours.RemoveWhere(c => exclusions.Contains(c.transform));

        return alwaysDisabledBehaviours;
    }

    private HashSet<Transform> FindAllMovingTransforms()
    {
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
            if (physBone.multiChildType == VRCPhysBoneBase.MultiChildType.Ignore && root.childCount > 1)
            {
                foreach (Transform child in root)
                {
                    stack.Push(child);
                }
            }
            else
            {
                stack.Push(root);
            }
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (exclusions.Contains(current))
                    continue;
                transforms.Add(current);
                foreach (Transform child in current)
                {
                    stack.Push(child);
                }
            }
        }

        var constraints = GetComponentsInChildren<Behaviour>(true)
            .Where(b => !alwaysDisabledComponents.Contains(b))
            .Where(b => b is IConstraint).ToList();
        foreach (var constraint in constraints)
        {
            transforms.Add(constraint.transform);
        }

        return transforms;
    }

    public  HashSet<Transform> FindAllUnmovingTransforms()
    {
        var avDescriptor = GetComponent<VRCAvatarDescriptor>();
        if (avDescriptor == null)
            return new HashSet<Transform>();
        var moving = FindAllMovingTransforms();
        return new HashSet<Transform>(transform.GetAllDescendants().Where(t => !moving.Contains(t)));
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

    public HashSet<Renderer> FindAllPenetrators()
    {
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
        penetrators.UnionWith(GetComponentsInChildren<Renderer>(true).Where(m => IsTPSPenetratorRoot(m.transform)));
        return penetrators;
    }

    private Texture2DArray CombineTextures(List<Texture2D> textures)
    {
        Profiler.StartSection("CombineTextures()");
        bool isLinear = IsTextureLinear(textures[0]);
        var texArray = new Texture2DArray(textures[0].width, textures[0].height,
            textures.Count, textures[0].format, textures[0].mipmapCount > 1, isLinear);
        texArray.anisoLevel = textures[0].anisoLevel;
        texArray.wrapMode = textures[0].wrapMode;
        for (int i = 0; i < textures.Count; i++)
        {
            Graphics.CopyTexture(textures[i], 0, texArray, i);
        }
        Profiler.EndSection();
        CreateUniqueAsset(texArray, textures[0].width + "x" + textures[0].height + "_" + textures[0].format + (isLinear ? "_linear" : "_sRGB") + "_2DArray.asset");
        return texArray;
    }

    private Matrix4x4 AddWeighted(Matrix4x4 a, Matrix4x4 b, float weight)
    {
        if (weight == 0)
            return a;
        a.SetRow(0, a.GetRow(0) + b.GetRow(0) * weight);
        a.SetRow(1, a.GetRow(1) + b.GetRow(1) * weight);
        a.SetRow(2, a.GetRow(2) + b.GetRow(2) * weight);
        a.SetRow(3, a.GetRow(3) + b.GetRow(3) * weight);
        return a;
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
        List<List<int>> mergedMeshIndices = null,
        List<string> allOriginalMeshPaths = null)
    {
        if (!fusedAnimatedMaterialProperties.TryGetValue(path, out var usedMaterialProps))
            usedMaterialProps = new HashSet<string>();
        if (mergedMeshIndices == null)
            mergedMeshIndices = sources.Select(s => Enumerable.Range(0, meshToggleCount).ToList()).ToList();
        HashSet<(string name, bool isVector)> defaultAnimatedProperties = null;
        if (allOriginalMeshPaths != null && (sources.Count != 1 || sources[0].Count != 1))
        {
            defaultAnimatedProperties = new HashSet<(string name, bool isVector)>();
            for (int i = 0; i < allOriginalMeshPaths.Count; i++)
            {
                if (animatedMaterialProperties.TryGetValue(allOriginalMeshPaths[i], out var animatedProps))
                {
                    foreach (var prop in animatedProps)
                    {
                        string name = prop;
                        bool isVector = name.EndsWith(".x") || name.EndsWith(".r");
                        if (isVector)
                        {
                            name = name.Substring(0, name.Length - 2);
                        }
                        else if ((name.Length > 2 && name[name.Length - 2] == '.')
                            || (!isVector && (animatedProps.Contains($"{name}.x") || animatedProps.Contains($"{name}.r"))))
                        {
                            continue;
                        }
                        defaultAnimatedProperties.Add(($"d4rkAvatarOptimizer{name}_ArrayIndex{i}", isVector));
                    }
                }
                defaultAnimatedProperties.Add(($"_IsActiveMesh{i}", false));
            }
        }
        var materials = new Material[sources.Count];
        var parsedShader = new ParsedShader[sources.Count];
        var setShaderKeywords = new List<string>[sources.Count];
        var replace = new Dictionary<string, string>[sources.Count];
        var cullReplace = new string[sources.Count];
        var texturesToMerge = new HashSet<string>[sources.Count];
        var propertyTextureArrayIndex = new Dictionary<string, int>[sources.Count];
        var arrayPropertyValues = new Dictionary<string, (string type, List<string> values)>[sources.Count];
        var texturesToCheckNull = new Dictionary<string, string>[sources.Count];
        var animatedPropertyValues = new Dictionary<string, string>[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            parsedShader[i] = ShaderAnalyzer.Parse(source[0]?.shader);
            if (parsedShader[i] == null || !parsedShader[i].parsedCorrectly)
            {
                materials[i] = source[0];
                continue;
            }
            texturesToMerge[i] = new HashSet<string>();
            propertyTextureArrayIndex[i] = new Dictionary<string, int>();
            arrayPropertyValues[i] = new Dictionary<string, (string type, List<string> values)>();
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
                            if (!arrayPropertyValues[i].TryGetValue(prop.name + "_ST", out propertyArray))
                            {
                                propertyArray.type = "float4";
                                propertyArray.values = new List<string>();
                                arrayPropertyValues[i][prop.name + "_ST"] = propertyArray;
                            }
                            var scale = mat.GetTextureScale(prop.name);
                            var offset = mat.GetTextureOffset(prop.name);
                            propertyArray.values.Add("float4(" + scale.x + "," + scale.y + "," + offset.x + "," + offset.y + ")");
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

            cullReplace[i] = null;
            var cullProp = parsedShader[i].properties.FirstOrDefault(p => p.shaderLabParams.Count == 1 && p.shaderLabParams.First() == "Cull");
            if (cullProp != null)
            {
                int firstCull = source[0].GetInt(cullProp.name);
                if (source.Any(m => m.GetInt(cullProp.name) != firstCull))
                {
                    cullReplace[i] = cullProp.name;
                }
            }

            replace[i] = new Dictionary<string, string>();
            foreach (var tuple in arrayPropertyValues[i].ToList())
            {
                if (usedMaterialProps.Contains(tuple.Key) && !(meshToggleCount > 1 && KeepMaterialPropertyAnimationsSeparate))
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
                foreach (string key in replace[i].Keys.Where(k => !k.StartsWith("arrayIndex")).ToArray())
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
            }

            animatedPropertyValues[i] = new Dictionary<string, string>();
            if (meshToggleCount > 1 && KeepMaterialPropertyAnimationsSeparate)
            {
                foreach (var propName in usedMaterialProps)
                {
                    if (originalMeshPaths != null)
                    {
                        bool skipProp = true;
                        foreach (var originalPath in originalMeshPaths[i])
                        {
                            if (animatedMaterialProperties.TryGetValue(originalPath, out var props))
                            {
                                if (props.Contains(propName) || props.Contains(propName + ".x") || props.Contains(propName + ".r"))
                                {
                                    skipProp = false;
                                    break;
                                }
                            }
                        }
                        if (skipProp)
                            continue;
                    }
                    if (parsedShader[i].propertyTable.TryGetValue(propName, out var prop))
                    {
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

        var optimizedShader = new List<(string name, List<string> lines)>[sources.Count];
        Profiler.StartSection("ShaderOptimizer.Run()");
        Parallel.For(0, sources.Count, i =>
        {
            if (parsedShader[i] != null && parsedShader[i].parsedCorrectly)
            {
                optimizedShader[i] = ShaderOptimizer.Run(
                    parsedShader[i],
                    replace[i],
                    meshToggleCount,
                    allOriginalMeshPaths,
                    i == 0 ? defaultAnimatedProperties : null,
                    mergedMeshIndices[i],
                    arrayPropertyValues[i],
                    texturesToCheckNull[i],
                    texturesToMerge[i],
                    animatedPropertyValues[i],
                    setShaderKeywords[i]);
            }
        });
        Profiler.EndSection();

        for (int i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            if (parsedShader[i] == null || !parsedShader[i].parsedCorrectly)
                continue;

            DisplayProgressBar($"Optimizing shader {source[0].shader.name} ({i + 1}/{sources.Count})");
            var name = System.IO.Path.GetFileName(source[0].shader.name);
            name = source[0].name + " " + name;
            var shaderFilePath = AssetDatabase.GenerateUniqueAssetPath(trashBinPath + name + ".shader");
            name = System.IO.Path.GetFileNameWithoutExtension(shaderFilePath);
            optimizedShader[i][0].lines[0] = "Shader \"d4rkpl4y3r/Optimizer/" + name + "\"//" + optimizedShader[i][0].lines[0];
            foreach (var opt in optimizedShader[i])
            {
                var filePath = shaderFilePath;
                if (opt.name != "Shader")
                {
                    filePath = trashBinPath + opt.name;
                }
                System.IO.File.WriteAllLines(filePath, opt.lines);
                optimizedMaterialImportPaths.Add(filePath);
            }
            var optimizedMaterial = Instantiate(source[0]);
            foreach (var keyword in setShaderKeywords[i])
            {
                optimizedMaterial.DisableKeyword(keyword);
            }
            if (cullReplace[i] != null)
            {
                optimizedMaterial.SetInt(cullReplace[i], 0);
            }
            optimizedMaterial.name = name;
            materials[i] = optimizedMaterial;
            optimizedMaterials.Add(optimizedMaterial);
            int renderQueue = optimizedMaterial.renderQueue;
            optimizedMaterial.shader = null;
            optimizedMaterial.renderQueue = renderQueue;
            foreach (var prop in parsedShader[i].properties)
            {
                if (prop.type != ParsedShader.Property.Type.Texture2D)
                    continue;
                var tex = source.Select(m => m.GetTexture(prop.name)).FirstOrDefault(t => t != null);
                optimizedMaterial.SetTexture(prop.name, tex);
            }
            var arrayList = new List<(string name, Texture2DArray array)>();
            foreach (var texArray in propertyTextureArrayIndex[i])
            {
                optimizedMaterial.SetTexture(texArray.Key, null);
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
            var mat = optimizedMaterials[i];
            DisplayProgressBar($"Loading optimized shader {mat.name}", 0.7f + 0.2f * (i / (float)optimizedMaterials.Count));
            Profiler.StartSection("AssetDatabase.LoadAssetAtPath<Shader>()");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(trashBinPath + mat.name + ".shader");
            Profiler.StartNextSection("mat.shader = shader");
            int renderQueue = mat.renderQueue;
            mat.shader = shader;
            mat.renderQueue = renderQueue;
            Profiler.StartNextSection("SetTextureArrayProperties");
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
                    mat.SetTextureOffset(texArrayName, mat.GetTextureOffset(texArray.name));
                    mat.SetTextureScale(texArrayName, mat.GetTextureScale(texArray.name));
                }
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

            if (KeepMaterialPropertyAnimationsSeparate
                && fusedAnimatedMaterialProperties.TryGetValue(GetPathToRoot(meshRenderer), out var animatedProperties))
            {
                foreach (var mat in meshRenderer.sharedMaterials)
                {
                    foreach (var animPropName in animatedProperties)
                    {
                        var propName = "d4rkAvatarOptimizer" + animPropName;
                        bool isVector = propName.EndsWith(".x");
                        bool isColor = propName.EndsWith(".r");
                        if (isColor || isVector)
                        {
                            propName = propName.Substring(0, propName.Length - 2);
                        }
                        else if (propName[propName.Length - 2] == '.')
                        {
                            continue;
                        }
                        for (int mID = 0; mID < meshCount; mID++)
                        {
                            var propArrayName = $"{propName}_ArrayIndex{mID}";
                            var signal = System.Single.NaN;
                            if (isVector || isColor)
                            {
                                mat.SetVector(propArrayName, new Vector4(signal, signal, signal, signal));
                            }
                            else
                            {
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
                    for (int mID = 0; mID < meshCount; mID++)
                    {
                        var propName = $"_IsActiveMesh{mID}";
                        mat.SetFloat(propName, props.GetFloat(propName));
                    }
                }
            }
        }

        foreach (var mat in optimizedMaterials)
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
        var parsedShader = ShaderAnalyzer.Parse(candidateMat.shader);
        if (parsedShader.parsedCorrectly == false)
            return false;
        if (slotSwapMaterials.ContainsKey((GetPathToRoot(candidate.renderer), candidate.index)))
            return false;
        if (materialSlotRemap.TryGetValue((GetPathToRoot(candidate.renderer), candidate.index), out var remap))
        {
            if (slotSwapMaterials.ContainsKey(remap))
                return false;
        }
        if (!MergeDifferentPropertyMaterials)
            return list.All(t => t.renderer.sharedMaterials[t.index] == candidateMat);
        if (!MergeDifferentRenderQueue && firstMat.renderQueue != candidateMat.renderQueue)
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
        bool mergeTextures = MergeSameDimensionTextures && parsedShader.CanMergeTextures();
        foreach (var prop in parsedShader.properties)
        {
            foreach (var slot in list)
            {
                var mat = slot.material;
                switch (prop.type)
                {
                    case ParsedShader.Property.Type.Color:
                    case ParsedShader.Property.Type.ColorHDR:
                    case ParsedShader.Property.Type.Vector:
                        break;
                    case ParsedShader.Property.Type.Float:
                        if (prop.shaderLabParams.Any(s => s != "Cull" || !MergeBackFaceCullingWithCullingOff)
                            && mat.GetFloat(prop.name) != candidateMat.GetFloat(prop.name))
                            return false;
                        break;
                    case ParsedShader.Property.Type.Int:
                        if (prop.shaderLabParams.Any(s => s != "Cull" || !MergeBackFaceCullingWithCullingOff)
                            && mat.GetInt(prop.name) != candidateMat.GetInt(prop.name))
                            return false;
                        break;
                    case ParsedShader.Property.Type.Texture2D:
                    case ParsedShader.Property.Type.Texture2DArray:
                    case ParsedShader.Property.Type.Texture3D:
                    case ParsedShader.Property.Type.TextureCube:
                    case ParsedShader.Property.Type.TextureCubeArray:
                        {
                            var mTex = mat.GetTexture(prop.name);
                            var cTex = candidateMat.GetTexture(prop.name);
                            if (mergeTextures && !CanCombineTextures(mTex, cTex))
                                return false;
                            if (!mergeTextures && cTex != mTex)
                                return false;
                        }
                        break;
                }
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
            var mats = meshRenderer.sharedMaterials.Select((material, index) => (material, index)).ToList();
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
            var sourceIndices = mesh.triangles;
            var sourceUv = Enumerable.Range(0, 8).Select(i => new List<Vector4>()).ToArray();
            for(int i = 0; i < 8; i++)
            {
                mesh.GetUVs(i, sourceUv[i]);
                sourceUv[i] = sourceUv[i].Count != sourceVertices.Length ? Enumerable.Range(0, sourceVertices.Length).Select(r => Vector4.zero).ToList() : sourceUv[i];
            }
            var sourceColor = mesh.colors;
            var sourceColor32 = mesh.colors32;
            var sourceNormals = mesh.normals;
            var sourceTangents = mesh.tangents;
            var sourceWeights = mesh.boneWeights;

            var targetUv = Enumerable.Range(0, 8).Select(i => new List<Vector4>()).ToArray();
            var targetColor = new List<Color>();
            var targetColor32 = new List<Color32>();
            if (mesh.HasVertexAttribute(VertexAttribute.Color))
            {
                if (mesh.GetVertexAttributeFormat(VertexAttribute.Color) == VertexAttributeFormat.UNorm8)
                {
                    targetColor = null;
                }
                else
                {
                    targetColor32 = null;
                }
            }
            else
            {
                targetColor = null;
                targetColor32 = null;
            }
            var targetVertices = new List<Vector3>();
            var targetIndices = new List<List<int>>();
            var targetNormals = new List<Vector3>();
            var targetTangents = new List<Vector4>();
            var targetWeights = new List<BoneWeight>();

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
                    int startIndex = (int)mesh.GetIndexStart(materialSubMeshId);
                    int endIndex = (int)mesh.GetIndexCount(materialSubMeshId) + startIndex;
                    for (int j = startIndex; j < endIndex; j++)
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
                            for (int a = 1; a < 8; a++)
                            {
                                targetUv[a].Add(sourceUv[a][oldIndex]);
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
                newMesh.boneWeights = targetWeights.ToArray();
                if (targetColor != null && targetColor.Any(c => !c.Equals(Color.white)))
                {
                    newMesh.colors = targetColor.ToArray();
                }
                else if (targetColor32 != null && targetColor32.Any(c => !c.Equals(new Color32(255, 255, 255, 255))))
                {
                    newMesh.colors32 = targetColor32.ToArray();
                }
                for (int i = 0; i < 8; i++)
                {
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
                    newMesh.SetIndices(targetIndices[i].ToArray(), MeshTopology.Triangles, i);
                }

                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    for (int j = 0; j < mesh.GetBlendShapeFrameCount(i); j++)
                    {
                        var sourceDeltaVertices = new Vector3[mesh.vertexCount];
                        var sourceDeltaNormals = new Vector3[mesh.vertexCount];
                        var sourceDeltaTangents = new Vector3[mesh.vertexCount];
                        mesh.GetBlendShapeFrameVertices(i, j, sourceDeltaVertices, sourceDeltaNormals, sourceDeltaTangents);
                        var targetDeltaVertices = new Vector3[newMesh.vertexCount];
                        var targetDeltaNormals = new Vector3[newMesh.vertexCount];
                        var targetDeltaTangents = new Vector3[newMesh.vertexCount];
                        for (int k = 0; k < newMesh.vertexCount; k++)
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
                newMesh.Optimize();
                Profiler.EndSection();

                CreateUniqueAsset(newMesh, newMesh.name + ".asset");
                Profiler.StartSection("AssetDatabase.SaveAssets()");
                AssetDatabase.SaveAssets();
                Profiler.EndSection();

                meshRenderer.sharedMesh = newMesh;
            }

            var originalMeshPaths = matchedSlots.Select(list => list.Select(slot => materialSlotRemap[(meshPath, slot.index)].path).Distinct().ToList()).ToList();
            var uniqueMatchedMaterials = uniqueMatchedSlots.Select(list => list.Select(slot => slot.material).ToList()).ToList();
            var allOriginalMeshPaths = Enumerable.Range(0, meshRenderer.sharedMaterials.Length).Select(i => materialSlotRemap[(meshPath, i)].path).Distinct().ToList();
            var optimizedMaterials = CreateOptimizedMaterials(uniqueMatchedMaterials, meshCount > 1 ? meshCount : 0, meshPath, originalMeshPaths, mergedMeshIndices, allOriginalMeshPaths);

            for (int i = 0; i < uniqueMatchedMaterials.Count; i++)
            {
                if (uniqueMatchedMaterials[i].Count != 1 || uniqueMatchedMaterials[i][0] == null)
                    continue;
                var originalSlot = materialSlotRemap[(meshPath, matchedSlots[i][0].index)];
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
        value.x = Mathf.Abs(value.x) < threshold ? 0 : value.x;
        value.y = Mathf.Abs(value.y) < threshold ? 0 : value.y;
        value.z = Mathf.Abs(value.z) < threshold ? 0 : value.z;
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

    private void CombineSkinnedMeshes()
    {
        var combinableMeshList = FindPossibleSkinnedMeshMerges();
        mergedMeshPaths = combinableMeshList.Select(list => list.Select(r => GetPathToRoot(r)).ToList()).ToList();
        var exclusions = GetAllExcludedTransforms();
        movingParentMap = FindMovingParent();
        materialSlotRemap = new Dictionary<(string, int), (string, int)>();
        fusedAnimatedMaterialProperties = FindAllAnimatedMaterialProperties();
        animatedMaterialProperties = FindAllAnimatedMaterialProperties();
        var combinableSkinnedMeshList = combinableMeshList
            .Select(l => l.Select(m => m as SkinnedMeshRenderer).Where(m => m != null).ToList())
            .Where(l => l.Count > 0)
            .Where(l => l[0].sharedMesh != null)
            .Where(l => l.All(m => !exclusions.Contains(m.transform)))
            .ToArray();
        int totalMeshCount = combinableSkinnedMeshList.Sum(l => l.Count);
        int currentMeshCount = 0;
        for (int combinedMeshID = 0; combinedMeshID < combinableSkinnedMeshList.Length; combinedMeshID++)
        {
            var combinableSkinnedMeshes = combinableSkinnedMeshList[combinedMeshID];

            var targetBones = new List<Transform>();
            var targetBoneMap = new Dictionary<Transform, int>();
            var targetUv = Enumerable.Range(0, 8).Select(i => new List<Vector4>()).ToArray();
            var targetColor = new List<Color>();
            var targetColor32 = new List<Color32>();
            bool useColor32 = true;
            var targetVertices = new List<Vector3>();
            var targetIndices = new List<List<int>>();
            var targetNormals = new List<Vector3>();
            var targetTangents = new List<Vector4>();
            var targetWeights = new List<BoneWeight>();
            var targetBindPoses = new List<Matrix4x4>();
            var sourceToWorld = new List<Matrix4x4>();
            var targetBounds = combinableSkinnedMeshes[0].localBounds;
            var toLocal = (combinableSkinnedMeshes[0].rootBone == null ? combinableSkinnedMeshes[0].transform : combinableSkinnedMeshes[0].rootBone).worldToLocalMatrix;

            string newMeshName = combinableSkinnedMeshes[0].name;
            string newPath = GetPathToRoot(combinableSkinnedMeshes[0]);

            Profiler.StartSection("CombineMeshData");
            int meshID = 0;
            foreach (var skinnedMesh in combinableSkinnedMeshes)
            {
                DisplayProgressBar($"Combining mesh ({++currentMeshCount}/{totalMeshCount}) {skinnedMesh.name}");

                var mesh = skinnedMesh.sharedMesh;
                var bindPoseIDMap = new Dictionary<int, int>();
                var indexOffset = targetVertices.Count;
                var sourceVertices = mesh.vertices;
                var sourceIndices = mesh.triangles;
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
                var bindPoseCount = mesh.bindposes.Length;
                if (sourceBones.Length != bindPoseCount)
                {
                    Debug.LogWarning($"Bone count ({sourceBones.Length}) does not match bind pose count ({bindPoseCount}) on {skinnedMesh.name}");
                    bindPoseCount = Math.Min(sourceBones.Length, bindPoseCount);
                }
                var toWorldArray = Enumerable.Range(0, bindPoseCount).Select(i =>
                    sourceBones[i].localToWorldMatrix * skinnedMesh.sharedMesh.bindposes[i]
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
                    sourceWeights = Enumerable.Range(0, sourceVertices.Length).Select(s => defaultWeight).ToArray();
                    sourceBones = new Transform[1] { rootBone.transform };
                    toWorldArray = new Matrix4x4[1] { rootBone.transform.localToWorldMatrix };
                    keepTransforms.Add(rootBone.transform);
                    bindPoseCount = 1;
                }

                for (int i = 1; i < 8; i++)
                {
                    var uvs = new List<Vector4>();
                    mesh.GetUVs(i, uvs);
                    if (uvs.Count == sourceVertices.Length)
                    {
                        targetUv[i].AddRange(uvs);
                    }
                    else
                    {
                        targetUv[i].AddRange(Enumerable.Range(0, sourceVertices.Length).Select(s => Vector4.zero));
                    }
                }

                if (mesh.HasVertexAttribute(VertexAttribute.Color))
                {
                    if (mesh.GetVertexAttributeFormat(VertexAttribute.Color) == VertexAttributeFormat.UNorm8)
                    {
                        var colors = mesh.colors32;
                        targetColor32.AddRange(colors);
                        targetColor.AddRange(colors.Select(s => (Color)s));
                    }
                    else
                    {
                        var colors = mesh.colors;
                        targetColor.AddRange(colors);
                        useColor32 = false;
                    }
                }
                else
                {
                    targetColor.AddRange(Enumerable.Range(0, sourceVertices.Length).Select(s => Color.white));
                    targetColor32.AddRange(Enumerable.Range(0, sourceVertices.Length).Select(s => new Color32(255, 255, 255, 255)));
                }

                sourceUv = sourceUv.Length != sourceVertices.Length ? new Vector2[sourceVertices.Length] : sourceUv;
                sourceNormals = sourceNormals.Length != sourceVertices.Length ? new Vector3[sourceVertices.Length] : sourceNormals;
                sourceTangents = sourceTangents.Length != sourceVertices.Length ? new Vector4[sourceVertices.Length] : sourceTangents;

                var bakedBlendShapeVertexDelta = new Vector3[sourceVertices.Length];
                var bakedBlendShapeNormalDelta = new Vector3[sourceVertices.Length];
                var bakedBlendShapeTangentDelta = new Vector3[sourceVertices.Length];

                if (!blendShapesToBake.TryGetValue(skinnedMesh, out var blendShapeIDs))
                {
                    blendShapeIDs = new List<int>();
                }

                foreach (int blendShapeID in blendShapeIDs)
                {
                    var weight = skinnedMesh.GetBlendShapeWeight(blendShapeID) / 100f;
                    var deltaVertices = new Vector3[sourceVertices.Length];
                    var deltaNormals = new Vector3[sourceVertices.Length];
                    var deltaTangents = new Vector3[sourceVertices.Length];
                    mesh.GetBlendShapeFrameVertices(blendShapeID, 0, deltaVertices, deltaNormals, deltaTangents);
                    for (int i = 0; i < sourceVertices.Length; i++)
                    {
                        bakedBlendShapeVertexDelta[i] += deltaVertices[i] * weight;
                        bakedBlendShapeNormalDelta[i] += deltaNormals[i] * weight;
                        bakedBlendShapeTangentDelta[i] += deltaTangents[i] * weight;
                    }
                }

                for (int vertIndex = 0; vertIndex < sourceVertices.Length; vertIndex++)
                {
                    targetUv[0].Add(new Vector4(sourceUv[vertIndex].x, sourceUv[vertIndex].y, meshID << 12, 0));
                    var boneWeight = sourceWeights[vertIndex];
                    boneWeight.boneIndex0 = boneWeight.boneIndex0 >= bindPoseCount ? 0 : boneWeight.boneIndex0;
                    boneWeight.boneIndex1 = boneWeight.boneIndex1 >= bindPoseCount ? 0 : boneWeight.boneIndex1;
                    boneWeight.boneIndex2 = boneWeight.boneIndex2 >= bindPoseCount ? 0 : boneWeight.boneIndex2;
                    boneWeight.boneIndex3 = boneWeight.boneIndex3 >= bindPoseCount ? 0 : boneWeight.boneIndex3;
                    Matrix4x4 toWorld = Matrix4x4.zero;
                    toWorld = AddWeighted(toWorld, toWorldArray[boneWeight.boneIndex0], boneWeight.weight0);
                    toWorld = AddWeighted(toWorld, toWorldArray[boneWeight.boneIndex1], boneWeight.weight1);
                    toWorld = AddWeighted(toWorld, toWorldArray[boneWeight.boneIndex2], boneWeight.weight2);
                    toWorld = AddWeighted(toWorld, toWorldArray[boneWeight.boneIndex3], boneWeight.weight3);
                    sourceToWorld.Add(toWorld);
                    var vertex = sourceVertices[vertIndex] + bakedBlendShapeVertexDelta[vertIndex];
                    var normal = sourceNormals[vertIndex] + bakedBlendShapeNormalDelta[vertIndex];
                    var tangent = (Vector3)sourceTangents[vertIndex] + bakedBlendShapeTangentDelta[vertIndex];
                    targetVertices.Add(toWorld.MultiplyPoint3x4(vertex));
                    targetNormals.Add(toWorld.MultiplyVector(normal).normalized);
                    var t = toWorld.MultiplyVector(tangent).normalized;
                    targetTangents.Add(new Vector4(t.x, t.y, t.z, sourceTangents[vertIndex].w));
                    int newIndex;
                    if (!bindPoseIDMap.TryGetValue(boneWeight.boneIndex0, out newIndex))
                    {
                        newIndex = GetNewBoneIDFromTransform(targetBones, targetBoneMap, targetBindPoses,
                            sourceBones[boneWeight.boneIndex0]);
                        bindPoseIDMap[boneWeight.boneIndex0] = newIndex;
                    }
                    boneWeight.boneIndex0 = newIndex;
                    if (!bindPoseIDMap.TryGetValue(boneWeight.boneIndex1, out newIndex))
                    {
                        newIndex = GetNewBoneIDFromTransform(targetBones, targetBoneMap, targetBindPoses,
                            sourceBones[boneWeight.boneIndex1]);
                        bindPoseIDMap[boneWeight.boneIndex1] = newIndex;
                    }
                    boneWeight.boneIndex1 = newIndex;
                    if (!bindPoseIDMap.TryGetValue(boneWeight.boneIndex2, out newIndex))
                    {
                        newIndex = GetNewBoneIDFromTransform(targetBones, targetBoneMap, targetBindPoses,
                            sourceBones[boneWeight.boneIndex2]);
                        bindPoseIDMap[boneWeight.boneIndex2] = newIndex;
                    }
                    boneWeight.boneIndex2 = newIndex;
                    if (!bindPoseIDMap.TryGetValue(boneWeight.boneIndex3, out newIndex))
                    {
                        newIndex = GetNewBoneIDFromTransform(targetBones, targetBoneMap, targetBindPoses,
                            sourceBones[boneWeight.boneIndex3]);
                        bindPoseIDMap[boneWeight.boneIndex3] = newIndex;
                    }
                    boneWeight.boneIndex3 = newIndex;
                    targetWeights.Add(boneWeight);
                }
                
                for (var matID = 0; matID < skinnedMesh.sharedMaterials.Length; matID++)
                {
                    uint startIndex = mesh.GetIndexStart(Math.Min(matID, mesh.subMeshCount - 1));
                    uint endIndex = mesh.GetIndexCount(Math.Min(matID, mesh.subMeshCount - 1)) + startIndex;
                    var indices = new List<int>();
                    for (uint i = startIndex; i < endIndex; i++)
                    {
                        indices.Add(sourceIndices[i] + indexOffset);
                    }
                    materialSlotRemap[(newPath, targetIndices.Count)] = (GetPathToRoot(skinnedMesh), matID);
                    targetIndices.Add(indices);
                }

                meshID++;
            }
            Profiler.EndSection();

            var blendShapeWeights = new Dictionary<string, float>();

            var combinedMesh = new Mesh();
            combinedMesh.indexFormat = targetVertices.Count >= 65536
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            combinedMesh.SetVertices(targetVertices);
            combinedMesh.bindposes = targetBindPoses.ToArray();
            combinedMesh.boneWeights = targetWeights.ToArray();
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
                if (targetUv[i].Any(uv => !uv.Equals(Vector4.zero)))
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
                combinedMesh.SetIndices(targetIndices[i].ToArray(), MeshTopology.Triangles, i);
            }

            Profiler.StartSection("CopyCombinedMeshBlendShapes");
            var usedBlendShapeNames = new HashSet<string>();
            var blendShapeMeshIDtoNewName = new Dictionary<(int meshID, int blendShapeID), string>();
            var combinableMeshPaths = new HashSet<string>(combinableSkinnedMeshes.Select(s => GetPathToRoot(s)));
            var meshPathToID = combinableSkinnedMeshes.Select((s, i) => (GetPathToRoot(s), i)).ToDictionary(s => s.Item1, s => s.Item2);
            var usedBlendShapesInCombinedMesh = new HashSet<string>(
                usedBlendShapes.Where(s => combinableMeshPaths.Contains(s.Substring(0, s.IndexOf("/blendShape.")))));
            var allMergedBlendShapes = new List<List<(string blendshape, float weight)>>();
            if (MergeSameRatioBlendShapes)
            {
                allMergedBlendShapes.AddRange(FindMergeableBlendShapes(combinableSkinnedMeshes));
                var usedBlendShapesInMergedBlobs = new HashSet<string>(allMergedBlendShapes.SelectMany(s => s).Select(s => s.blendshape));
                allMergedBlendShapes.AddRange(usedBlendShapesInCombinedMesh.Where(s => !usedBlendShapesInMergedBlobs.Contains(s)).Select(s => new List<(string blendshape, float weight)> { (s, 1) }));
            }
            else
            {
                allMergedBlendShapes.AddRange(usedBlendShapesInCombinedMesh.Select(s => new List<(string blendshape, float weight)> { (s, 1) }));
            }
            var vertexOffset = new List<int>() {0};
            for (int i = 0; i < combinableSkinnedMeshes.Count - 1; i++)
            {
                vertexOffset.Add(vertexOffset[i] + combinableSkinnedMeshes[i].sharedMesh.vertexCount);
            }
            foreach (var mergedBlendShapes in allMergedBlendShapes)
            {
                if (mergedBlendShapes.Count == 1)
                {
                    var path = mergedBlendShapes[0].blendshape.Substring(0, mergedBlendShapes[0].blendshape.IndexOf("/blendShape."));
                    var skinnedMesh = GetTransformFromPath(path).GetComponent<SkinnedMeshRenderer>();
                    var mesh = skinnedMesh.sharedMesh;
                    var oldName = mergedBlendShapes[0].blendshape.Substring(path.Length + 12);
                    var name = GenerateUniqueName(oldName, usedBlendShapeNames);
                    meshID = meshPathToID[path];
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
                        var sourceDeltaVertices = new Vector3[mesh.vertexCount];
                        var sourceDeltaNormals = new Vector3[mesh.vertexCount];
                        var sourceDeltaTangents = new Vector3[mesh.vertexCount];
                        mesh.GetBlendShapeFrameVertices(blendShapeID, j, sourceDeltaVertices, sourceDeltaNormals, sourceDeltaTangents);
                        var targetDeltaVertices = new Vector3[combinedMesh.vertexCount];
                        var targetDeltaNormals = new Vector3[combinedMesh.vertexCount];
                        var targetDeltaTangents = new Vector3[combinedMesh.vertexCount];
                        for (int k = 0; k < mesh.vertexCount; k++)
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
                    var targetDeltaVertices = new Vector3[combinedMesh.vertexCount];
                    var targetDeltaNormals = new Vector3[combinedMesh.vertexCount];
                    var targetDeltaTangents = new Vector3[combinedMesh.vertexCount];
                    foreach (var toMerge in mergedBlendShapes)
                    {
                        var path = toMerge.blendshape.Substring(0, toMerge.blendshape.IndexOf("/blendShape."));
                        var skinnedMesh = GetTransformFromPath(path).GetComponent<SkinnedMeshRenderer>();
                        var mesh = skinnedMesh.sharedMesh;
                        var blendShapeID = mesh.GetBlendShapeIndex(toMerge.blendshape.Substring(path.Length + 12));
                        if (blendShapeID == -1)
                            continue;
                        meshID = meshPathToID[path];
                        blendShapeMeshIDtoNewName[(meshID, blendShapeID)] = name;
                        var sourceDeltaVertices = new Vector3[mesh.vertexCount];
                        var sourceDeltaNormals = new Vector3[mesh.vertexCount];
                        var sourceDeltaTangents = new Vector3[mesh.vertexCount];
                        mesh.GetBlendShapeFrameVertices(blendShapeID, 0, sourceDeltaVertices, sourceDeltaNormals, sourceDeltaTangents);
                        for (int k = 0; k < mesh.vertexCount; k++)
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
            var materials = combinableSkinnedMeshes.SelectMany(r => r.sharedMaterials).ToArray();
            var avDescriptor = GetComponent<VRCAvatarDescriptor>();

            if (avDescriptor.customEyeLookSettings.eyelidType == VRCAvatarDescriptor.EyelidType.Blendshapes
                && avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh != null)
            {
                var eyeLookMeshRenderer = avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh;
                var ids = avDescriptor.customEyeLookSettings.eyelidsBlendshapes;
                for (int i = 0; i < ids.Length; i++)
                {
                    if (ids[i] < 0)
                        continue;
                    for (meshID = 0; meshID < combinableSkinnedMeshes.Count; meshID++)
                    {
                        if (combinableSkinnedMeshes[meshID] == eyeLookMeshRenderer)
                        {
                            avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh = meshRenderer;
                            ids[i] = combinedMesh.GetBlendShapeIndex(blendShapeMeshIDtoNewName[(meshID, ids[i])]);
                        }
                    }
                }
                avDescriptor.customEyeLookSettings.eyelidsBlendshapes = ids;
            }

            meshID = 0;
            foreach (var skinnedMesh in combinableSkinnedMeshes)
            {
                var oldPath = GetPathToRoot(skinnedMesh);
                if (combinableSkinnedMeshes.Count > 1)
                {
                    var properties = new MaterialPropertyBlock();
                    if (meshRenderer.HasPropertyBlock())
                    {
                        meshRenderer.GetPropertyBlock(properties);
                    }
                    bool isActive = skinnedMesh.gameObject.activeSelf && skinnedMesh.enabled;
                    properties.SetFloat("_IsActiveMesh" + meshID, isActive ? 1f : 0f);
                    properties.SetInt("d4rkAvatarOptimizer_CombinedMeshCount", combinableSkinnedMeshes.Count);
                    AddAnimationPathChange((oldPath, "m_IsActive", typeof(GameObject)),
                            (newPath, "material._IsActiveMesh" + meshID, typeof(SkinnedMeshRenderer)));
                    AddAnimationPathChange((oldPath, "m_Enabled", typeof(SkinnedMeshRenderer)),
                            (newPath, "material._IsActiveMesh" + meshID, typeof(SkinnedMeshRenderer)));
                    var animatedMaterialPropertiesToAdd = new List<string>();
                    if (animatedMaterialProperties.TryGetValue(oldPath, out var animatedProperties))
                    {
                        foreach (var animPropName in animatedProperties)
                        {
                            var propName = animPropName;
                            bool isVector = propName.EndsWith(".x");
                            bool isColor = propName.EndsWith(".r");
                            if (isVector || isColor)
                            {
                                propName = propName.Substring(0, propName.Length - 2);
                            }
                            else if (propName[propName.Length - 2] == '.')
                            {
                                continue;
                            }
                            for (int mID = 0; mID < combinableSkinnedMeshes.Count; mID++)
                            {
                                string newPropertyName = $"material.{propName}";
                                if (KeepMaterialPropertyAnimationsSeparate)
                                {
                                    newPropertyName = $"material.d4rkAvatarOptimizer{propName}_ArrayIndex{mID}";
                                }
                                string path = GetPathToRoot(combinableSkinnedMeshes[mID]);
                                var vectorEnd = isVector ? new [] { ".x", ".y", ".z", ".w" } : isColor ? new [] { ".r", ".g", ".b", ".a" } : new [] { "" };
                                foreach (var component in vectorEnd)
                                {
                                    AddAnimationPathChange(
                                        (path, "material." + propName + component, typeof(SkinnedMeshRenderer)),
                                        (newPath, newPropertyName + component, typeof(SkinnedMeshRenderer)));
                                }
                            }
                            animatedMaterialPropertiesToAdd.Add(animPropName);
                        }
                    }
                    if (animatedMaterialPropertiesToAdd.Count > 0)
                    {
                        if (!fusedAnimatedMaterialProperties.TryGetValue(newPath, out animatedProperties))
                        {
                            fusedAnimatedMaterialProperties[newPath] = animatedProperties = new HashSet<string>();
                        }
                        animatedProperties.UnionWith(animatedMaterialPropertiesToAdd);
                    }
                    meshRenderer.SetPropertyBlock(properties);
                    meshID++;
                }
                if (avDescriptor != null)
                {
                    if (avDescriptor.VisemeSkinnedMesh == skinnedMesh)
                    {
                        avDescriptor.VisemeSkinnedMesh = meshRenderer;
                    }
                }
            }
            for (meshID = 1; meshID < combinableSkinnedMeshes.Count; meshID++)
            {
                var obj = combinableSkinnedMeshes[meshID].gameObject;
                DestroyImmediate(combinableSkinnedMeshes[meshID]);
                if (!keepTransforms.Contains(obj.transform) && (obj.transform.childCount == 0 && obj.GetComponents<Component>().Length == 1))
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

            if (combinableSkinnedMeshes.Count > 1)
            {
                meshRenderer.gameObject.SetActive(true);
                meshRenderer.enabled = true;
            }

            Profiler.StartSection("AssetDatabase.SaveAssets()");
            AssetDatabase.SaveAssets();
            Profiler.EndSection();
        }
    }

    public HashSet<Transform> GetAllExcludedTransforms()
    {
        var allExcludedTransforms = new HashSet<Transform>();
        foreach (var excludedTransform in ExcludeTransforms)
        {
            allExcludedTransforms.Add(excludedTransform);
            allExcludedTransforms.UnionWith(excludedTransform.GetAllDescendants());
        }
        return allExcludedTransforms;
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

        var used = new HashSet<Transform>(
            GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(s => s.bones));

        var movingTransforms = FindAllMovingTransforms();
        used.UnionWith(movingTransforms);
        used.UnionWith(movingTransforms.Select(t => t != null ? t.parent : null));
        
        foreach (var constraint in GetComponentsInChildren<Behaviour>(true).OfType<IConstraint>())
        {
            used.Add((constraint as Component).transform.parent);
            for (int i = 0; i < constraint.sourceCount; i++)
            {
                used.Add(constraint.GetSource(i).sourceTransform);
            }
            used.Add((constraint as AimConstraint)?.worldUpObject);
            used.Add((constraint as LookAtConstraint)?.worldUpObject);
        }

        used.Add(transform);
        used.UnionWith(GetComponentsInChildren<Animator>(true)
            .Select(a => a.transform.Find("Armature")).Where(t => t != null));

        foreach (var skinnedRenderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            used.Add(skinnedRenderer.rootBone);
        }

        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            used.Add(renderer.probeAnchor);
        }

        foreach (var contact in GetComponentsInChildren<ContactBase>(true))
        {
            used.Add(contact.GetRootTransform());
        }

        foreach (var obj in transform.GetAllDescendants())
        {
            if (obj.GetComponents<Component>().Length > 1)
            {
                used.Add(obj);
            }
        }

        used.UnionWith(gameObjectTogglePaths.Select(p => GetTransformFromPath(p)).Where(t => t != null));

        foreach (var exclusionOnMainAvatar in ExcludeTransforms)
        {
            var exclusion = GetTransformFromPath(GetTransformPathTo(exclusionOnMainAvatar, transform));
            if (exclusion == null)
                continue;
            used.Add(exclusion);
            used.UnionWith(exclusion.GetAllDescendants());
            while ((exclusion = exclusion.parent) != null)
            {
                used.Add(exclusion);
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