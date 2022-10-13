#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;
using UnityEditor.Animations;
using UnityEditor;
using d4rkpl4y3r;
using d4rkpl4y3r.Util;
using d4rkpl4y3r.Util.Extensions;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Validation.Performance;

using Math = System.Math;
using Type = System.Type;
using AnimationPath = System.ValueTuple<string, string, System.Type>;

namespace d4rkpl4y3r.Util.Extensions
{
    public static class RendererExtensions
    {
        public static Mesh GetSharedMesh(this Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer)
            {
                return (renderer as SkinnedMeshRenderer).sharedMesh;
            }
            else if (renderer.TryGetComponent<MeshFilter>(out var filter))
            {
                return filter.sharedMesh;
            }
            else
            {
                return null;
            }
        }
    }

    public static class TransformExtensions
    {
        public static IEnumerable<Transform> GetAllDescendants(this Transform transform)
        {
            var stack = new Stack<Transform>();
            foreach (Transform child in transform)
            {
                stack.Push(child);
            }
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                yield return current;
                foreach (Transform child in current)
                {
                    stack.Push(child);
                }
            }
        }
    }

    public static class AnimationControllerExtensions
    {

        public static IEnumerable<AnimatorState> EnumerateAllStates(this AnimatorController controller)
        {
            var queue = new Queue<AnimatorStateMachine>();
            foreach (var layer in controller.layers)
            {
                queue.Enqueue(layer.stateMachine);
                while (queue.Count > 0)
                {
                    var stateMachine = queue.Dequeue();
                    foreach (var subStateMachine in stateMachine.stateMachines)
                    {
                        queue.Enqueue(subStateMachine.stateMachine);
                    }
                    foreach (var state in stateMachine.states.Select(s => s.state))
                    {
                        yield return state;
                    }
                }
            }
        }
    }

    public static class Vector3Extensions
    {
        public static Vector3 Multiply(this Vector3 vec, float x, float y, float z)
        {
            vec.x *= x;
            vec.y *= y;
            vec.z *= z;
            return vec;
        }
    }
}

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

[CustomEditor(typeof(d4rkAvatarOptimizer))]
public class d4rkAvatarOptimizerEditor : Editor
{
    private static GameObject root;
    private static d4rkAvatarOptimizer settings;
    private static string scriptPath = "Assets/d4rkAvatarOptimizer";
    private static string trashBinPath = "Assets/d4rkAvatarOptimizer/TrashBin/";
    private static HashSet<string> usedBlendShapes = new HashSet<string>();
    private static Dictionary<SkinnedMeshRenderer, List<int>> blendShapesToBake = new Dictionary<SkinnedMeshRenderer, List<int>>();
    private static Dictionary<AnimationPath, AnimationPath> newAnimationPaths = new Dictionary<AnimationPath, AnimationPath>();
    private static List<Material> optimizedMaterials = new List<Material>();
    private static Dictionary<(string path, int slot), HashSet<Material>> slotSwapMaterials = new Dictionary<(string, int), HashSet<Material>>();
    private static Dictionary<(string path, int slot), Dictionary<Material, Material>> optimizedSlotSwapMaterials = new Dictionary<(string, int), Dictionary<Material, Material>>();
    private static Dictionary<(string path, int index), (string path, int index)> materialSlotRemap = new Dictionary<(string, int), (string, int)>();
    private static Dictionary<string, HashSet<string>> animatedMaterialProperties = new Dictionary<string, HashSet<string>>();
    private static List<List<Texture2D>> textureArrayLists = new List<List<Texture2D>>();
    private static List<Texture2DArray> textureArrays = new List<Texture2DArray>();
    private static Dictionary<Material, List<(string name, Texture2DArray array)>> texArrayPropertiesToSet = new Dictionary<Material, List<(string name, Texture2DArray array)>>();
    private static HashSet<string> gameObjectTogglePaths = new HashSet<string>();
    private static Material nullMaterial = null;
    private static HashSet<Transform> keepTransforms = new HashSet<Transform>();
    private static HashSet<SkinnedMeshRenderer> hasUsedBlendShapes = new HashSet<SkinnedMeshRenderer>();
    private static HashSet<string> convertedMeshRendererPaths = new HashSet<string>();
    private static Dictionary<Transform, Transform> movingParentMap = new Dictionary<Transform, Transform>();
    private static Dictionary<string, Transform> transformFromOldPath = new Dictionary<string, Transform>();
    private static float progressBar = 0;

    private static void DisplayProgressBar(string text)
    {
        EditorUtility.DisplayProgressBar("Optimizing " + settings.name, text, progressBar);
    }

    private static void DisplayProgressBar(string text, float progress)
    {
        progressBar = progress;
        DisplayProgressBar(text);
    }

    private static void ClearTrashBin()
    {
        Profiler.StartSection("ClearTrashBin()");
        trashBinPath = scriptPath + "/TrashBin/";
        AssetDatabase.DeleteAsset(scriptPath + "/TrashBin");
        AssetDatabase.CreateFolder(scriptPath, "TrashBin");
        assetBundlePath = null;
        Profiler.EndSection();
    }

    private static string assetBundlePath = null;
    private static void CreateUniqueAsset(Object asset, string name)
    {
        Profiler.StartSection("AssetDatabase.CreateAsset()");
        bool assetIsBundleable = asset is Material || asset is AnimationClip;
        if (assetIsBundleable && assetBundlePath != null)
        {
            AssetDatabase.AddObjectToAsset(asset, assetBundlePath);
        }
        else
        {
            var path = AssetDatabase.GenerateUniqueAssetPath(trashBinPath + name);
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

    private static string GetTransformPathToRoot(Transform t)
    {
        return GetTransformPathTo(t, root.transform);
    }

    private static Transform GetTransformFromPath(string path)
    {
        if (path == "")
            return root.transform;
        string[] pathParts = path.Split('/');
        Transform t = root.transform;
        for (int i = 0; i < pathParts.Length; i++)
        {
            t = t.Find(pathParts[i]);
            if (t == null)
                return null;
        }
        return t;
    }

    private static string GetPathToRoot(GameObject obj)
    {
        return GetTransformPathToRoot(obj.transform);
    }

    private static string GetPathToRoot(Component component)
    {
        return GetTransformPathToRoot(component.transform);
    }

    private static bool IsMaterialReadyToCombineWithOtherMeshes(Material material)
    {
        return material == null ? false : ShaderAnalyzer.Parse(material.shader).CanMerge();
    }

    private static bool IsCombinableRenderer(Renderer candidate)
    {
        if (candidate.TryGetComponent(out Cloth cloth))
            return false;
        if (candidate is MeshRenderer && (candidate.gameObject.layer == 12 || !settings.MergeStaticMeshesAsSkinned))
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

    private static bool CanCombineRendererWith(List<Renderer> list, Renderer candidate)
    {
        if (!settings.MergeSkinnedMeshes)
            return false;
        if (!IsCombinableRenderer(list[0]))
            return false;
        if (!IsCombinableRenderer(candidate))
            return false;
        if (list[0].gameObject.layer != candidate.gameObject.layer)
            return false;
        if (!settings.ForceMergeBlendShapeMissMatch && (hasUsedBlendShapes.Contains(list[0]) ^ hasUsedBlendShapes.Contains(candidate)))
            return false;
        var paths = list.Select(smr => GetPathToRoot(smr.transform.parent)).ToList();
        var t = candidate.transform;
        while ((t = t.parent) != root.transform)
        {
            var path = GetPathToRoot(t);
            if (gameObjectTogglePaths.Contains(path) && paths.Any(p => !p.StartsWith(path)))
                return false;
        }
        return true;
    }

    private static void AddAnimationPathChange((string path, string name, Type type) source, (string path, string name, Type type) target)
    {
        if (source == target)
            return;
        newAnimationPaths[source] = target;
        if (settings.MergeStaticMeshesAsSkinned && source.type == typeof(SkinnedMeshRenderer))
        {
            source.type = typeof(MeshRenderer);
            newAnimationPaths[source] = target;
        }
    }

    private static HashSet<SkinnedMeshRenderer> FindAllUnusedSkinnedMeshRenderers()
    {
        var togglePaths = FindAllGameObjectTogglePaths();
        var unused = new HashSet<SkinnedMeshRenderer>();
        var skinnedMeshRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
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

    private static void DeleteAllUnusedSkinnedMeshRenderers()
    {
        foreach (var skinnedMeshRenderer in FindAllUnusedSkinnedMeshRenderers())
        {
            var obj = skinnedMeshRenderer.gameObject;
            DestroyImmediate(skinnedMeshRenderer);
            if (!keepTransforms.Contains(obj.transform) && (obj.transform.childCount == 0 && obj.GetComponents<Component>().Length == 1))
                DestroyImmediate(obj);
        }
    }

    private static List<List<Renderer>> FindPossibleSkinnedMeshMerges()
    {
        var unused = FindAllUnusedComponents();
        gameObjectTogglePaths = FindAllGameObjectTogglePaths();
        slotSwapMaterials = FindAllMaterialSwapMaterials();
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        var matchedSkinnedMeshes = new List<List<Renderer>>();
        var exclusions = GetAllExcludedTransforms();
        foreach (var renderer in renderers)
        {
            var mesh = renderer.GetSharedMesh();
            if (renderer.gameObject.CompareTag("EditorOnly") || unused.Contains(renderer))
                continue;

            bool foundMatch = false;
            foreach (var subList in matchedSkinnedMeshes)
            {
                if (exclusions.Contains(renderer.transform) || renderer is ParticleSystemRenderer)
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
        var avDescriptor = root.GetComponent<VRCAvatarDescriptor>();
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
        return matchedSkinnedMeshes;
    }
    
    private static int GetNewBoneIDFromTransform(List<Transform> bones, Dictionary<Transform, int> boneMap, List<Matrix4x4> bindPoses, Transform toMatch)
    {
        if (boneMap.TryGetValue(toMatch, out int index))
            return index;
        if (settings.DeleteUnusedGameObjects)
            toMatch = movingParentMap[toMatch];
        foreach (var bone in root.transform.GetAllDescendants())
        {
            if (bone == toMatch)
            {
                bones.Add(bone);
                bindPoses.Add(bone.worldToLocalMatrix);
                return bones.Count - 1;
            }
        }
        bones.Add(root.transform);
        bindPoses.Add(root.transform.localToWorldMatrix);
        return bones.Count - 1;
    }

    private static EditorCurveBinding FixAnimationBinding(EditorCurveBinding binding, ref bool changed)
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
    
    private static AnimationClip FixAnimationClipPaths(AnimationClip clip)
    {
        var newClip = Instantiate(clip);
        newClip.ClearCurves();
        newClip.name = clip.name;
        bool changed = false;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var newBinding = FixAnimationBinding(binding, ref changed);
            AnimationUtility.SetEditorCurve(newClip, newBinding,
                AnimationUtility.GetEditorCurve(clip, binding));
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

    private static Motion FixMotion(Motion motion, Dictionary<Motion, Motion> fixedMotions, string assetPath, AnimationClip dummyClip)
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
    
    private static void FixAllAnimationPaths()
    {
        var avDescriptor = root.GetComponent<VRCAvatarDescriptor>();
        if (avDescriptor == null)
            return;

        var dummyAnimationToFillEmptyStates = AssetDatabase.LoadAssetAtPath<AnimationClip>(scriptPath + "/data/DummyAnimationToFillEmptyStates.anim");
        
        var animations = new HashSet<AnimationClip>();
        for (int i = 0; i < avDescriptor.baseAnimationLayers.Length; i++)
        {
            var layer = avDescriptor.baseAnimationLayers[i].animatorController;
            if (layer == null)
                continue;
            animations.UnionWith(layer.animationClips);
        }

        var fixedMotions = new Dictionary<Motion, Motion>();
        foreach (var clip in animations)
        {
            fixedMotions[clip] = FixAnimationClipPaths(clip);
        }
        
        for (int i = 0; i < avDescriptor.baseAnimationLayers.Length; i++)
        {
            var layer = avDescriptor.baseAnimationLayers[i].animatorController as AnimatorController;
            if (layer == null)
                continue;
            Profiler.StartSection("AssetDatabase.CopyAsset()");
            string path = AssetDatabase.GenerateUniqueAssetPath(trashBinPath + layer.name + ".controller");
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(layer), path);
            var newLayer = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            Profiler.EndSection();

            foreach (var state in newLayer.EnumerateAllStates())
            {
                state.motion = FixMotion(state.motion, fixedMotions, path, dummyAnimationToFillEmptyStates);
            }

            avDescriptor.baseAnimationLayers[i].animatorController = newLayer;
        }
        Profiler.StartSection("AssetDatabase.SaveAssets()");
        AssetDatabase.SaveAssets();
        Profiler.EndSection();
    }

    private static Dictionary<(string path, int index), HashSet<Material>> FindAllMaterialSwapMaterials()
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
                if (GetTransformFromPath(binding.path) == null)
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

    private static void OptimizeMaterialSwapMaterials()
    {
        slotSwapMaterials = FindAllMaterialSwapMaterials();
        var mergedMeshes = FindPossibleSkinnedMeshMerges();
        optimizedSlotSwapMaterials.Clear();
        var exclusions = GetAllExcludedTransforms();
        foreach (var entry in slotSwapMaterials)
        {
            int meshToggleCount = 0;
            var current = GetTransformFromPath(entry.Key.path).GetComponent<Renderer>();
            if (exclusions.Contains(current.transform))
                continue;
            if (current != null)
            {
                meshToggleCount = mergedMeshes.FirstOrDefault(list => list.Any(renderer => renderer == current))?.Count ?? 0;
            }
            if (!optimizedSlotSwapMaterials.TryGetValue(entry.Key, out var optimizedMaterials))
            {
                optimizedSlotSwapMaterials[entry.Key] = optimizedMaterials = new Dictionary<Material, Material>();
            }
            foreach (var material in entry.Value)
            {
                if (!optimizedMaterials.TryGetValue(material, out var optimizedMaterial))
                {
                    var matWrapper = new List<List<Material>>() { new List<Material>() { material } };
                    optimizedMaterials[material] = CreateOptimizedMaterials(matWrapper, meshToggleCount, entry.Key.path)[0];
                }
            }
        }
    }

    private static AnimatorController GetFXLayer()
    {
        var avDescriptor = root.GetComponent<VRCAvatarDescriptor>();
        if (avDescriptor == null || avDescriptor.baseAnimationLayers.Length != 5)
            return null;
        return avDescriptor.baseAnimationLayers[4].animatorController as AnimatorController;
    }

    private static void CalculateUsedBlendShapePaths()
    {
        usedBlendShapes.Clear();
        hasUsedBlendShapes.Clear();
        blendShapesToBake.Clear();
        var avDescriptor = root.GetComponent<VRCAvatarDescriptor>();
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
            if (avDescriptor.customEyeLookSettings.eyelidType
                == VRCAvatarDescriptor.EyelidType.Blendshapes
                && avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh != null)
            {
                var meshRenderer = avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh;
                string path = GetPathToRoot(meshRenderer) + "/blendShape.";
                foreach (var blendShapeID in avDescriptor.customEyeLookSettings.eyelidsBlendshapes)
                {
                    if (blendShapeID >= 0)
                    {
                        usedBlendShapes.Add(path + meshRenderer.sharedMesh.GetBlendShapeName(blendShapeID));
                        hasUsedBlendShapes.Add(meshRenderer);
                    }
                }
            }
            var fxLayer = GetFXLayer();
            if (fxLayer != null)
            {
                foreach (var binding in fxLayer.animationClips.SelectMany(clip => AnimationUtility.GetCurveBindings(clip)))
                {
                    if (binding.type != typeof(SkinnedMeshRenderer)
                        || !binding.propertyName.StartsWith("blendShape."))
                        continue;
                    usedBlendShapes.Add(binding.path + "/" + binding.propertyName);
                    var t = GetTransformFromPath(binding.path);
                    if (t != null)
                    {
                        hasUsedBlendShapes.Add(t.GetComponent<SkinnedMeshRenderer>());
                    }
                }
            }
        }
        foreach (var skinnedMeshRenderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = skinnedMeshRenderer.sharedMesh;
            if (mesh == null)
                continue;
            var blendShapeIDs = new List<int>();
            blendShapesToBake[skinnedMeshRenderer] = blendShapeIDs;
            string path = GetPathToRoot(skinnedMeshRenderer) + "/blendShape.";
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                if (skinnedMeshRenderer.GetBlendShapeWeight(i) != 0 && !usedBlendShapes.Contains(path + mesh.GetBlendShapeName(i)))
                {
                    if (mesh.GetBlendShapeFrameCount(i) > 1)
                    {
                        usedBlendShapes.Add(path + mesh.GetBlendShapeName(i));
                        hasUsedBlendShapes.Add(skinnedMeshRenderer);
                    }
                    else
                    {
                        blendShapeIDs.Add(i);
                    }
                }
            }
        }
    }

    private static Dictionary<string, HashSet<string>> FindAllAnimatedMaterialProperties()
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

    private static HashSet<string> FindAllGameObjectTogglePaths()
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

    private static HashSet<Transform> FindAllAlwaysDisabledGameObjects()
    {
        var togglePaths = FindAllGameObjectTogglePaths();
        var disabledGameObjects = new HashSet<Transform>();
        var queue = new Queue<Transform>();
        var exclusions = GetAllExcludedTransforms();
        queue.Enqueue(root.transform);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (exclusions.Contains(current))
                continue;
            if (current != root.transform && !current.gameObject.activeSelf && !togglePaths.Contains(GetPathToRoot(current)))
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

    private static HashSet<Component> FindAllUnusedComponents()
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

        var alwaysDisabledBehaviours = new HashSet<Component>(root.GetComponentsInChildren<Behaviour>(true)
            .Where(b => !b.enabled)
            .Where(b => !(b is VRCPhysBoneColliderBase))
            .Where(b => !behaviourToggles.Contains(GetPathToRoot(b))));
        
        var usedPhysBoneColliders = root.GetComponentsInChildren<VRCPhysBoneBase>(true)
            .Where(pb => !alwaysDisabledBehaviours.Contains(pb))
            .SelectMany(pb => pb.colliders);

        alwaysDisabledBehaviours.UnionWith(root.GetComponentsInChildren<VRCPhysBoneColliderBase>(true)
            .Where(c => !usedPhysBoneColliders.Contains(c)));

        alwaysDisabledBehaviours.UnionWith(FindAllAlwaysDisabledGameObjects()
            .SelectMany(t => t.GetComponents<Component>().Where(c => !(c is Transform))));

        var exclusions = GetAllExcludedTransforms();
        alwaysDisabledBehaviours.RemoveWhere(c => exclusions.Contains(c.transform));

        return alwaysDisabledBehaviours;
    }

    private static HashSet<Transform> FindAllMovingTransforms()
    {
        var avDescriptor = root.GetComponent<VRCAvatarDescriptor>();
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

        var animators = root.GetComponentsInChildren<Animator>(true);
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
        var physBones = root.GetComponentsInChildren<VRCPhysBoneBase>(true)
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

        var constraints = root.GetComponentsInChildren<Behaviour>(true)
            .Where(b => !alwaysDisabledComponents.Contains(b))
            .Where(b => b is IConstraint).ToList();
        foreach (var constraint in constraints)
        {
            transforms.Add(constraint.transform);
        }

        return transforms;
    }

    private static HashSet<Transform> FindAllUnmovingTransforms()
    {
        var avDescriptor = root.GetComponent<VRCAvatarDescriptor>();
        if (avDescriptor == null)
            return new HashSet<Transform>();
        var moving = FindAllMovingTransforms();
        return new HashSet<Transform>(root.transform.GetAllDescendants().Where(t => !moving.Contains(t)));
    }

    private static Texture2DArray CombineTextures(List<Texture2D> textures)
    {
        Profiler.StartSection("CombineTextures()");
        bool isLinear = IsTextureLinear(textures[0]);
        var texArray = new Texture2DArray(textures[0].width, textures[0].height,
            textures.Count, textures[0].format, true, isLinear);
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

    private static Matrix4x4 AddWeighted(Matrix4x4 a, Matrix4x4 b, float weight)
    {
        if (weight == 0)
            return a;
        a.SetRow(0, a.GetRow(0) + b.GetRow(0) * weight);
        a.SetRow(1, a.GetRow(1) + b.GetRow(1) * weight);
        a.SetRow(2, a.GetRow(2) + b.GetRow(2) * weight);
        a.SetRow(3, a.GetRow(3) + b.GetRow(3) * weight);
        return a;
    }

    private static void SearchForTextureArrayCreation(List<List<Material>> sources)
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
                    if (subList[0].texelSize == texArray[0].texelSize && subList[0].format == texArray[0].format && IsTextureLinear(subList[0]) == IsTextureLinear(texArray[0]))
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

    private static string GenerateUniqueName(string name, HashSet<string> usedNames)
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

    private static Material[] CreateOptimizedMaterials(List<List<Material>> sources, int meshToggleCount, string path)
    {
        if (!animatedMaterialProperties.TryGetValue(path, out var usedMaterialProps))
            usedMaterialProps = new HashSet<string>();
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
                            propertyArray.values.Add("" + mat.GetFloat(prop.name));
                        break;
                        case ParsedShader.Property.Type.Vector:
                            if (!arrayPropertyValues[i].TryGetValue(prop.name, out propertyArray))
                            {
                                propertyArray.type = "float4";
                                propertyArray.values = new List<string>();
                                arrayPropertyValues[i][prop.name] = propertyArray;
                            }
                            propertyArray.values.Add("float4" + mat.GetVector(prop.name));
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
                            if (!arrayPropertyValues[i].TryGetValue(prop.name, out propertyArray))
                            {
                                propertyArray.type = "float4";
                                propertyArray.values = new List<string>();
                                arrayPropertyValues[i][prop.name] = propertyArray;
                            }
                            var col = mat.GetColor(prop.name);
                            col = prop.type == ParsedShader.Property.Type.ColorHDR ? col : col.linear;
                            propertyArray.values.Add($"float4({col.r}, {col.g}, {col.b}, {col.a})");
                            break;
                        case ParsedShader.Property.Type.Texture2D:
                            if (!arrayPropertyValues[i].TryGetValue("arrayIndex" + prop.name, out var textureArray))
                            {
                                arrayPropertyValues[i]["arrayIndex" + prop.name] = ("int", new List<string>());
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
                if (usedMaterialProps.Contains(tuple.Key) && !(meshToggleCount > 1 && settings.KeepMaterialPropertyAnimationsSeparate))
                {
                    arrayPropertyValues[i].Remove(tuple.Key);
                }
                else if (tuple.Value.values.All(v => v == tuple.Value.values[0]))
                {
                    arrayPropertyValues[i].Remove(tuple.Key);
                    replace[i][tuple.Key] = tuple.Value.values[0];
                }
            }
            if (!settings.WritePropertiesAsStaticValues)
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
            if (meshToggleCount > 1 && settings.KeepMaterialPropertyAnimationsSeparate)
            {
                foreach (var propName in usedMaterialProps)
                {
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

        var optimizedShader = new List<string>[sources.Count];
        Profiler.StartSection("ShaderOptimizer.Run()");
        Parallel.For(0, sources.Count, i =>
        {
            if (parsedShader[i] != null && parsedShader[i].parsedCorrectly)
            {
                optimizedShader[i] = ShaderOptimizer.Run(
                    parsedShader[i],
                    replace[i],
                    meshToggleCount,
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
            optimizedShader[i][0] = "Shader \"d4rkpl4y3r/Optimizer/" + name + "\"//" + optimizedShader[i][0];
            System.IO.File.WriteAllLines(shaderFilePath, optimizedShader[i]);
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

    private static void SaveOptimizedMaterials()
    {
        Profiler.StartSection("AssetDatabase.Refresh()");
        AssetDatabase.Refresh();
        Profiler.EndSection();

        foreach(var mat in optimizedMaterials)
        {
            DisplayProgressBar($"Loading optimized shader {mat.name}");
            Profiler.StartSection("AssetDatabase.LoadAssetAtPath<Shader>()");
            int renderQueue = mat.renderQueue;
            mat.shader = AssetDatabase.LoadAssetAtPath<Shader>(trashBinPath + mat.name + ".shader");
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
            CreateUniqueAsset(mat, mat.name + ".mat");
        }

        var skinnedMeshRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var meshRenderer in skinnedMeshRenderers)
        {
            var mesh = meshRenderer.sharedMesh;
            if (mesh == null)
                continue;

            var props = new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(props);
            int meshCount = props.GetInt("d4rkAvatarOptimizer_CombinedMeshCount");

            if (settings.KeepMaterialPropertyAnimationsSeparate
                && animatedMaterialProperties.TryGetValue(GetPathToRoot(meshRenderer), out var animatedProperties))
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
                            if (isVector || isColor)
                            {
                                mat.SetVector(propArrayName, props.GetVector(propArrayName));
                            }
                            else
                            {
                                mat.SetFloat(propArrayName, props.GetFloat(propArrayName));
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

            Profiler.StartSection("AssetDatabase.SaveAssets()");
            AssetDatabase.SaveAssets();
            Profiler.EndSection();
        }
    }

    private static bool IsTextureLinear(Texture2D tex)
    {
        if (tex == null)
            return false;
        var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) as TextureImporter;
        if (importer == null)
            return false;
        return importer.sRGBTexture == false;
    }

    private static bool CanCombineTextures(Texture a, Texture b)
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
        if (IsTextureLinear(a2D) != IsTextureLinear(b2D))
            return false;
        return true;
    }

    private static bool CanCombineMaterialsWith(List<MaterialSlot> list, MaterialSlot candidate)
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
        if (!settings.MergeDifferentPropertyMaterials)
            return list.All(t => t.renderer.sharedMaterials[t.index] == candidateMat);
        if (!settings.MergeDifferentRenderQueue && firstMat.renderQueue != candidateMat.renderQueue)
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
        bool mergeTextures = settings.MergeSameDimensionTextures && parsedShader.CanMergeTextures();
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
                        if (prop.shaderLabParams.Any(s => s != "Cull" || !settings.MergeBackFaceCullingWithCullingOff)
                            && mat.GetFloat(prop.name) != candidateMat.GetFloat(prop.name))
                            return false;
                        break;
                    case ParsedShader.Property.Type.Int:
                        if (prop.shaderLabParams.Any(s => s != "Cull" || !settings.MergeBackFaceCullingWithCullingOff)
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

    private static void OptimizeMaterialsOnNonSkinnedMeshes()
    {
        var meshRenderers = root.GetComponentsInChildren<MeshRenderer>(true);
        var exclusions = GetAllExcludedTransforms();
        foreach (var meshRenderer in meshRenderers)
        {
            if (exclusions.Contains(meshRenderer.transform))
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

    private static List<List<MaterialSlot>> FindAllMergeAbleMaterials(IEnumerable<Renderer> renderers)
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

    private static void CreateTextureArrays()
    {
        textureArrayLists.Clear();
        textureArrays.Clear();

        var skinnedMeshRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
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
    
    private static void CombineAndOptimizeMaterials()
    {
        var skinnedMeshRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var exclusions = GetAllExcludedTransforms();
        foreach (var meshRenderer in skinnedMeshRenderers)
        {
            var mesh = meshRenderer.sharedMesh;
            if (mesh == null || exclusions.Contains(meshRenderer.transform))
                continue;
            
            DisplayProgressBar("Combining mesh: " + meshRenderer.name);

            var props = new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(props);
            int meshCount = props.GetInt("d4rkAvatarOptimizer_CombinedMeshCount");
            string meshPath = GetPathToRoot(meshRenderer);

            var matchedSlots = FindAllMergeAbleMaterials(new [] { meshRenderer });
            var uniqueMatchedSlots = matchedSlots.Select(list => list.Select(slot => list.First(slot2 => slot.material == slot2.material)).Distinct().ToList()).ToList();

            var sourceVertices = mesh.vertices;
            var sourceIndices = mesh.triangles;
            var sourceUv = Enumerable.Range(0, 8).Select(i => new List<Vector4>()).ToArray();
            for(int i = 0; i < 8; i++)
            {
                mesh.GetUVs(i, sourceUv[i]);
                sourceUv[i] = sourceUv[i].Count != sourceVertices.Length ? Enumerable.Range(0, sourceVertices.Length).Select(r => Vector4.zero).ToList() : sourceUv[i];
            }
            var sourceColor = mesh.colors;
            var sourceNormals = mesh.normals;
            var sourceTangents = mesh.tangents;
            var sourceWeights = mesh.boneWeights;

            var targetUv = Enumerable.Range(0, 8).Select(i => new List<Vector4>()).ToArray();
            var targetColor = new List<Color>();
            if (sourceColor.Length != sourceVertices.Length)
                targetColor = null;
            var targetVertices = new List<Vector3>();
            var targetIndices = new List<List<int>>();
            var targetNormals = new List<Vector3>();
            var targetTangents = new List<Vector4>();
            var targetWeights = new List<BoneWeight>();

            var targetOldVertexIndex = new List<int>();

            for (int i = 0; i < matchedSlots.Count; i++)
            {
                var indexList = new List<int>();
                for (int k = 0; k < matchedSlots[i].Count; k++)
                {
                    var indexMap = new Dictionary<int, int>();
                    int internalMaterialID = uniqueMatchedSlots[i].Select((slot, index) => (slot, index)).First(t => t.slot.material == matchedSlots[i][k].material).index;
                    var originalSlot = materialSlotRemap[(meshPath, matchedSlots[i][k].index)];
                    AddAnimationPathChange((originalSlot.path, $"m_Materials.Array.data[{originalSlot.index}]", typeof(SkinnedMeshRenderer)),
                        (meshPath, $"m_Materials.Array.data[{targetIndices.Count}]", typeof(SkinnedMeshRenderer)));
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
                            targetUv[0].Add(new Vector4(sourceUv[0][oldIndex].x, sourceUv[0][oldIndex].y, sourceUv[0][oldIndex].z, internalMaterialID));
                            for (int a = 1; a < 8; a++)
                            {
                                targetUv[a].Add(sourceUv[a][oldIndex]);
                            }
                            targetColor?.Add(sourceColor[oldIndex]);
                            targetVertices.Add(sourceVertices[oldIndex]);
                            targetNormals.Add(sourceNormals[oldIndex]);
                            targetTangents.Add(sourceTangents[oldIndex]);
                            targetWeights.Add(sourceWeights[oldIndex]);
                            targetOldVertexIndex.Add(oldIndex);
                        }
                    }
                }
                targetIndices.Add(indexList);
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
                if (targetColor != null && targetColor.Any(c => !c.Equals(new Color())))
                {
                    newMesh.colors = targetColor.ToArray();
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
                    newMesh.SetTangents(targetTangents);
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

                CreateUniqueAsset(newMesh, newMesh.name + ".asset");
                Profiler.StartSection("AssetDatabase.SaveAssets()");
                AssetDatabase.SaveAssets();
                Profiler.EndSection();

                meshRenderer.sharedMesh = newMesh;
            }

            var uniqueMatchedMaterials = uniqueMatchedSlots.Select(list => list.Select(slot => slot.material).ToList()).ToList();
            meshRenderer.sharedMaterials = CreateOptimizedMaterials(uniqueMatchedMaterials, meshCount > 1 ? meshCount : 0, GetPathToRoot(meshRenderer));
        }
    }

    private static Dictionary<Transform, Transform> FindMovingParent()
    {
        var nonMovingTransforms = FindAllUnmovingTransforms();
        var result = new Dictionary<Transform, Transform>();
        foreach (var transform in root.transform.GetAllDescendants())
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

    private static void CombineSkinnedMeshes()
    {
        var combinableSkinnedMeshList = FindPossibleSkinnedMeshMerges();
        var exclusions = GetAllExcludedTransforms();
        movingParentMap = FindMovingParent();
        materialSlotRemap = new Dictionary<(string, int), (string, int)>();
        int combinedMeshID = 0;
        foreach (var combinableMeshes in combinableSkinnedMeshList)
        {
            var combinableSkinnedMeshes = combinableMeshes.Select(m => m as SkinnedMeshRenderer).Where(m => m != null).ToList();
            if (combinableSkinnedMeshes.Count < 1)
                continue;
            if (combinableMeshes.Any(m => exclusions.Contains(m.transform)))
                continue;

            DisplayProgressBar($"Combining mesh {combinableMeshes[0].name}");

            var targetBones = new List<Transform>();
            var targetBoneMap = new Dictionary<Transform, int>();
            var targetUv = Enumerable.Range(0, 8).Select(i => new List<Vector4>()).ToArray();
            var targetColor = new List<Color>();
            var targetVertices = new List<Vector3>();
            var targetIndices = new List<List<int>>();
            var targetNormals = new List<Vector3>();
            var targetTangents = new List<Vector4>();
            var targetWeights = new List<BoneWeight>();
            var targetBindPoses = new List<Matrix4x4>();
            var sourceToWorld = new List<Matrix4x4>();
            var targetBounds = combinableSkinnedMeshes[0].localBounds;
            var toLocal = (combinableSkinnedMeshes[0].rootBone ?? combinableSkinnedMeshes[0].transform).worldToLocalMatrix;

            string newMeshName = combinableSkinnedMeshes[0].name;
            string newPath = GetPathToRoot(combinableSkinnedMeshes[0]);

            Profiler.StartSection("CombineMeshData");
            int meshID = 0;
            foreach (var skinnedMesh in combinableSkinnedMeshes)
            {
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
                var toWorldArray = Enumerable.Range(0, skinnedMesh.bones.Length).Select(i =>
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
                
                if (sourceWeights.Length != sourceVertices.Length)
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
                    sourceBones = new Transform[1] { skinnedMesh.transform };
                    toWorldArray = new Matrix4x4[1] { skinnedMesh.transform.localToWorldMatrix };
                    keepTransforms.Add(skinnedMesh.transform);
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
                var sourceColor = mesh.colors;
                if (sourceColor.Length == sourceVertices.Length)
                {
                    targetColor.AddRange(mesh.colors);
                }
                else
                {
                    targetColor.AddRange(Enumerable.Range(0, sourceVertices.Length).Select(s => new Color()));
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
                    targetUv[0].Add(new Vector4(sourceUv[vertIndex].x, sourceUv[vertIndex].y, meshID, 0));
                    var boneWeight = sourceWeights[vertIndex];
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
            if (targetColor.Any(c => !c.Equals(new Color())))
            {
                combinedMesh.colors = targetColor.ToArray();
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
            int vertexOffset = 0;
            var usedBlendShapeNames = new HashSet<string>();
            var blendShapeMeshIDtoNewName = new Dictionary<(int meshID, int blendShapeID), string>();
            meshID = 0;
            foreach (var skinnedMesh in combinableSkinnedMeshes)
            {
                var mesh = skinnedMesh.sharedMesh;
                string path = GetPathToRoot(skinnedMesh) + "/blendShape.";
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    var oldName = mesh.GetBlendShapeName(i);
                    if (!usedBlendShapes.Contains(path + oldName))
                        continue;
                    var name = GenerateUniqueName(oldName, usedBlendShapeNames);
                    blendShapeMeshIDtoNewName[(meshID, i)] = name;
                    blendShapeWeights[name] = skinnedMesh.GetBlendShapeWeight(i);
                    AddAnimationPathChange(
                        (GetPathToRoot(skinnedMesh), "blendShape." + oldName, typeof(SkinnedMeshRenderer)),
                        (newPath, "blendShape." + name, typeof(SkinnedMeshRenderer)));
                    for (int j = 0; j < mesh.GetBlendShapeFrameCount(i); j++)
                    {
                        var sourceDeltaVertices = new Vector3[mesh.vertexCount];
                        var sourceDeltaNormals = new Vector3[mesh.vertexCount];
                        var sourceDeltaTangents = new Vector3[mesh.vertexCount];
                        mesh.GetBlendShapeFrameVertices(i, j, sourceDeltaVertices, sourceDeltaNormals, sourceDeltaTangents);
                        var targetDeltaVertices = new Vector3[combinedMesh.vertexCount];
                        var targetDeltaNormals = new Vector3[combinedMesh.vertexCount];
                        var targetDeltaTangents = new Vector3[combinedMesh.vertexCount];
                        for (int k = 0; k < mesh.vertexCount; k++)
                        {
                            int vertIndex = k + vertexOffset;
                            var toWorld = sourceToWorld[vertIndex];
                            targetDeltaVertices[vertIndex] = toWorld.MultiplyVector(sourceDeltaVertices[k]);
                            targetDeltaNormals[vertIndex] = toWorld.MultiplyVector(sourceDeltaNormals[k]);
                            targetDeltaTangents[vertIndex] = toWorld.MultiplyVector(sourceDeltaTangents[k]);
                        }
                        var weight = mesh.GetBlendShapeFrameWeight(i, j);
                        combinedMesh.AddBlendShapeFrame(name, weight, targetDeltaVertices, targetDeltaNormals, targetDeltaTangents);
                    }
                }
                vertexOffset += mesh.vertexCount;
                meshID++;
            }
            Profiler.EndSection();
            
            var meshRenderer = combinableSkinnedMeshes[0];
            var materials = combinableSkinnedMeshes.SelectMany(r => r.sharedMaterials).ToArray();
            var avDescriptor = root.GetComponent<VRCAvatarDescriptor>();

            if (avDescriptor?.customEyeLookSettings.eyelidType
                == VRCAvatarDescriptor.EyelidType.Blendshapes
                && avDescriptor?.customEyeLookSettings.eyelidsSkinnedMesh != null)
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
                                if (settings.KeepMaterialPropertyAnimationsSeparate)
                                {
                                    newPropertyName = $"material.d4rkAvatarOptimizer{propName}_ArrayIndex{mID}";
                                    float signal = System.BitConverter.ToSingle(new byte[] {0x55, 0x55, 0x55, 0xFF}, 0);
                                    if (isVector || isColor)
                                    {
                                        properties.SetVector($"d4rkAvatarOptimizer{propName}_ArrayIndex{mID}", new Vector4(signal, signal, signal, signal));
                                    }
                                    else
                                    {
                                        properties.SetFloat($"d4rkAvatarOptimizer{propName}_ArrayIndex{mID}", signal);
                                    }
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
                        if (!animatedMaterialProperties.TryGetValue(newPath, out animatedProperties))
                        {
                            animatedMaterialProperties[newPath] = animatedProperties = new HashSet<string>();
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

            meshRenderer.gameObject.SetActive(true);

            Profiler.StartSection("AssetDatabase.SaveAssets()");
            AssetDatabase.SaveAssets();
            Profiler.EndSection();

            combinedMeshID++;
        }
    }

    private static HashSet<Transform> GetAllExcludedTransforms()
    {
        var allExcludedTransforms = new HashSet<Transform>();
        foreach (var excludedTransform in settings.ExcludeTransforms)
        {
            var newTransform = GetTransformFromPath(GetTransformPathTo(excludedTransform, settings.transform));
            if (newTransform == null)
                continue;
            allExcludedTransforms.Add(newTransform);
            allExcludedTransforms.UnionWith(newTransform.GetAllDescendants());
        }
        return allExcludedTransforms;
    }

    private static void DestroyEditorOnlyGameObjects()
    {
        var stack = new Stack<Transform>();
        stack.Push(root.transform);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.gameObject.CompareTag("EditorOnly"))
            {
                DestroyImmediate(current.gameObject);
                continue;
            }
            foreach (var child in current.Cast<Transform>())
            {
                stack.Push(child);
            }
        }
    }

    private static void DestroyUnusedComponents()
    {
        if (!settings.DeleteUnusedComponents)
            return;
        var list = FindAllUnusedComponents();
        foreach (var component in list)
        {
            DestroyImmediate(component);
        }
    }

    private static void DestroyUnusedGameObjects()
    {
        transformFromOldPath = new Dictionary<string, Transform>();
        foreach (var transform in root.transform.GetAllDescendants())
        {
            transformFromOldPath[GetPathToRoot(transform)] = transform;
        }

        if (!settings.DeleteUnusedGameObjects)
            return;

        var used = new HashSet<Transform>(
            root.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(s => s.bones));

        used.UnionWith(FindAllMovingTransforms());
        
        foreach (var constraint in root.GetComponentsInChildren<Behaviour>(true).OfType<IConstraint>())
        {
            used.Add((constraint as Component).transform.parent);
            for (int i = 0; i < constraint.sourceCount; i++)
            {
                used.Add(constraint.GetSource(i).sourceTransform);
            }
            used.Add((constraint as AimConstraint)?.worldUpObject);
            used.Add((constraint as LookAtConstraint)?.worldUpObject);
        }

        used.Add(root.transform);
        used.UnionWith(root.GetComponentsInChildren<Animator>(true)
            .Select(a => a.transform.Find("Armature")).Where(t => t != null));

        foreach (var skinnedRenderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            used.Add(skinnedRenderer.rootBone);
        }

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            used.Add(renderer.probeAnchor);
        }

        foreach (var obj in root.transform.GetAllDescendants())
        {
            if (obj.GetComponents<Component>().Length > 1)
            {
                used.Add(obj);
            }
        }

        used.UnionWith(gameObjectTogglePaths.Select(p => GetTransformFromPath(p)).Where(t => t != null));

        foreach (var exclusionOnMainAvatar in settings.ExcludeTransforms)
        {
            var exclusion = GetTransformFromPath(GetTransformPathTo(exclusionOnMainAvatar, settings.transform));
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
        queue.Enqueue(root.transform);

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

    private static void MoveRingFingerColliderToFeet()
    {
        if (!settings.UseRingFingerAsFootCollider)
            return;
        var avDescriptor = root.GetComponent<VRCAvatarDescriptor>();

        var collider = avDescriptor.collider_footL;
        collider.state = VRCAvatarDescriptor.ColliderConfig.State.Custom;
        collider.height *= 0.5f;
        collider.height -= collider.radius;
        var parent = new GameObject("leftFootColliderRoot");
        parent.transform.parent = collider.transform;
        parent.transform.localRotation = collider.rotation;
        parent.transform.localPosition = collider.position + collider.rotation * (-collider.height * Vector3.up);
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
        collider.height *= 0.5f;
        collider.height -= collider.radius;
        parent = new GameObject("rightFootColliderRoot");
        parent.transform.parent = collider.transform;
        parent.transform.localRotation = collider.rotation;
        parent.transform.localPosition = collider.position + collider.rotation * (-collider.height * Vector3.up);
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

    private static void ConvertStaticMeshesToSkinnedMeshes()
    {
        if (!settings.MergeStaticMeshesAsSkinned)
            return;
        var staticMeshes = root.gameObject.GetComponentsInChildren<MeshFilter>(true)
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

    private bool Validate()
    {
        var avDescriptor = root.GetComponent<VRCAvatarDescriptor>();

        if (avDescriptor == null)
        {
            EditorGUILayout.HelpBox("No VRCAvatarDescriptor found on the root object.", MessageType.Error);
            return false;
        }

        if (avDescriptor.baseAnimationLayers == null || avDescriptor.baseAnimationLayers.Length != 5)
        {
            EditorGUILayout.HelpBox("Playable base layer count in the avatar descriptor is not 5.", MessageType.Error);
            return false;
        }

        if (root.name.EndsWith("(OptimizedCopy)"))
        {
            EditorGUILayout.HelpBox("Put the optimizer on the original avatar, not the optimized copy.", MessageType.Error);
            return false;
        }

        if (settings.UseRingFingerAsFootCollider)
        {
            if (avDescriptor.collider_footL.transform == null || avDescriptor.collider_footR.transform == null)
            {
                EditorGUILayout.HelpBox(
                    "Foot collider transform not set.\n" +
                    "Open the collider foldout in the avatar descriptor.", MessageType.Error);
            }
        }

        if (avDescriptor.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape
            && avDescriptor.VisemeSkinnedMesh != null)
        {
            var meshRenderer = avDescriptor.VisemeSkinnedMesh;
            if (root.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r => r != meshRenderer))
            {
                EditorGUILayout.HelpBox("Viseme SkinnedMeshRenderer is not a child of the avatar root.", MessageType.Error);
            }
        }

        if (avDescriptor.customEyeLookSettings.eyelidType == VRCAvatarDescriptor.EyelidType.Blendshapes
            && avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh != null)
        {
            var meshRenderer = avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh;
            if (root.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r => r != meshRenderer))
            {
                EditorGUILayout.HelpBox("Eyelid SkinnedMeshRenderer is not a child of the avatar root.", MessageType.Error);
            }
        }

        if (Object.FindObjectsOfType<VRCAvatarDescriptor>().Any(av => av != null && av.name.EndsWith("(OptimizedCopy)")))
        {
            EditorGUILayout.HelpBox(
                "Optimized copy of some avatar is present in the scene.\n" +
                "Its assets will be deleted when creating a new optimized copy.", MessageType.Error);
        }

        var exclusions = GetAllExcludedTransforms();

        var allMaterials = root.GetComponentsInChildren<Renderer>(true)
            .Where(r => !exclusions.Contains(r.transform))
            .SelectMany(r => r.sharedMaterials).Distinct().ToArray();

        var correctlyParsedMaterials = allMaterials
            .Select(m => ShaderAnalyzer.Parse(m?.shader))
            .Where(p => (p?.parsedCorrectly ?? false)).ToArray();

        if (correctlyParsedMaterials.Length != allMaterials.Length)
        {
            EditorGUILayout.HelpBox(
                "One or more materials could not be parsed.\n" +
                "Check the Debug Info foldout for more info.", MessageType.Warning);
        }

        if (settings.MergeDifferentPropertyMaterials && correctlyParsedMaterials.Any(p => !p.CanMerge()))
        {
            EditorGUILayout.HelpBox(
                "One or more materials do not support merging.\n" +
                "Check the Debug Info foldout for more info.", MessageType.Warning);
        }

        if (settings.MergeSameDimensionTextures && correctlyParsedMaterials.Any(p => p.CanMerge() && !p.CanMergeTextures()))
        {
            EditorGUILayout.HelpBox(
                "One or more materials do not support merging textures.\n" +
                "Check the Debug Info foldout for more info.", MessageType.Warning);
        }

        if ((settings.MergeDifferentPropertyMaterials || settings.MergeSkinnedMeshes) && allMaterials.Any(m => IsLockedIn(m)))
        {
            EditorGUILayout.HelpBox(
                "One or more materials are locked in.\n" +
                "It is recommended to unlock them so they can be merged.\n" +
                "Check the Debug Info foldout for a full list.", MessageType.Warning);
        }

        if (settings.MergeDifferentPropertyMaterials && settings.MergeSameDimensionTextures && CrunchedTextures.Length > 0)
        {
            EditorGUILayout.HelpBox(
                "One or more textures are crunch compressed.\n" +
                "Crunch compressed textures cannot be merged.\n" +
                "Check the Debug Info foldout for a full list.", MessageType.Warning);
        }

        bool hasExtraMaterialSlots = root.GetComponentsInChildren<Renderer>(true)
            .Where(r => !exclusions.Contains(r.transform))
            .Where(r => r.GetSharedMesh() != null)
            .Any(r => r.sharedMaterials.Length > r.GetSharedMesh().subMeshCount);

        if (hasExtraMaterialSlots)
        {
            EditorGUILayout.HelpBox(
                "One or more renderers have more material slots than sub meshes.\n" + 
                "Those extra materials & polys are not counted by VRChats performance system. " + 
                "After optimizing those extra slots and polys will get baked as real ones.\n" + 
                "You should expect your poly count to increase, this is working as intended!", MessageType.Info);
        }

        return true;
    }

    private static void AssignNewAvatarIDIfEmpty()
    {
        var avDescriptor = root.GetComponent<VRCAvatarDescriptor>();
        if (avDescriptor == null)
            return;
        var pm = root.GetOrAddComponent<VRC.Core.PipelineManager>();
        if (!string.IsNullOrEmpty(pm.blueprintId))
            return;
        pm.AssignId();
    }

    private static void Optimize(GameObject toOptimize)
    {
        root = toOptimize;
        DisplayProgressBar("Parsing Shaders", 0.05f);
        ShaderAnalyzer.ParseAndCacheAllShaders(root, true);
        DisplayProgressBar("Clear TrashBin Folder", 0.1f);
        ClearTrashBin();
        optimizedMaterials.Clear();
        newAnimationPaths.Clear();
        texArrayPropertiesToSet.Clear();
        keepTransforms.Clear();
        convertedMeshRendererPaths.Clear();
        DisplayProgressBar("Destroying unused components", 0.15f);
        Profiler.StartSection("DestroyEditorOnlyGameObjects()");
        DestroyEditorOnlyGameObjects();
        Profiler.StartNextSection("DestroyUnusedComponents()");
        DestroyUnusedComponents();
        Profiler.StartNextSection("ConvertStaticMeshesToSkinnedMeshes()");
        ConvertStaticMeshesToSkinnedMeshes();
        Profiler.StartNextSection("CalculateUsedBlendShapePaths()");
        CalculateUsedBlendShapePaths();
        Profiler.StartNextSection("DeleteAllUnusedSkinnedMeshRenderers()");
        DeleteAllUnusedSkinnedMeshRenderers();
        Profiler.StartNextSection("FindAllAnimatedMaterialProperties()");
        DisplayProgressBar("Optimizing swap materials", 0.2f);
        animatedMaterialProperties = FindAllAnimatedMaterialProperties();
        Profiler.StartNextSection("OptimizeMaterialSwapMaterials()");
        OptimizeMaterialSwapMaterials();
        Profiler.StartNextSection("CombineSkinnedMeshes()");
        DisplayProgressBar("Combining meshes", 0.25f);
        CombineSkinnedMeshes();
        Profiler.StartNextSection("CreateTextureArrays()");
        CreateTextureArrays();
        Profiler.StartNextSection("CombineAndOptimizeMaterials()");
        DisplayProgressBar("Optimizing materials", 0.3f);
        CombineAndOptimizeMaterials();
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
    }

    private GameObject lastSelected = null;
    private List<List<List<MaterialSlot>>> mergedMaterialPreviewCache = null;
    private Transform[] unmovingBonesCache = null;
    private Component[] unusedComponentsCache = null;
    private Transform[] alwaysDisabledGameObjectsCache = null;
    private GameObject[] gameObjectsWithToggleAnimationsCache = null;
    private Texture2D[] crunchedTexturesCache = null;

    private void ClearUICaches()
    {
        mergedMaterialPreviewCache = null;
        unmovingBonesCache = null;
        unusedComponentsCache = null;
        alwaysDisabledGameObjectsCache = null;
        gameObjectsWithToggleAnimationsCache = null;
        crunchedTexturesCache = null;
    }

    private void OnSelectionChange()
    {
        if (lastSelected == settings.gameObject)
            return;
        lastSelected = settings.gameObject;
        ShaderAnalyzer.ParseAndCacheAllShaders(lastSelected, false);
        ClearUICaches();
    }

    private List<List<List<MaterialSlot>>> MergedMaterialPreview
    {
        get
        {
            if (mergedMaterialPreviewCache == null)
            {
                mergedMaterialPreviewCache = new List<List<List<MaterialSlot>>>();
                CalculateUsedBlendShapePaths();
                var matchedSkinnedMeshes = FindPossibleSkinnedMeshMerges();
                foreach (var mergedMeshes in matchedSkinnedMeshes)
                {
                    var matched = FindAllMergeAbleMaterials(mergedMeshes);
                    mergedMaterialPreviewCache.Add(matched);
                }
            }
            return mergedMaterialPreviewCache;
        }
    }

    private Transform[] UnmovingBones
    {
        get
        {
            if (unmovingBonesCache == null)
            {
                var bones = new HashSet<Transform>();
                var unmoving = FindAllUnmovingTransforms();
                root.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToList().ForEach(
                    r => bones.UnionWith(r.bones.Where(b => unmoving.Contains(b))));
                unmovingBonesCache = bones.ToArray();
            }
            return unmovingBonesCache;
        }
    }

    private Component[] UnusedComponents
    {
        get
        {
            if (unusedComponentsCache == null)
            {
                unusedComponentsCache = FindAllUnusedComponents().ToArray();
            }
            return unusedComponentsCache;
        }
    }

    private Transform[] AlwaysDisabledGameObjects
    {
        get
        {
            if (alwaysDisabledGameObjectsCache == null)
            {
                alwaysDisabledGameObjectsCache = FindAllAlwaysDisabledGameObjects().ToArray();
            }
            return alwaysDisabledGameObjectsCache;
        }
    }

    private GameObject[] GameObjectsWithToggleAnimations
    {
        get
        {
            if (gameObjectsWithToggleAnimationsCache == null)
            {
                gameObjectsWithToggleAnimationsCache =
                    FindAllGameObjectTogglePaths()
                    .Select(p => GetTransformFromPath(p)?.gameObject)
                    .Where(obj => obj != null).ToArray();
            }
            return gameObjectsWithToggleAnimationsCache;
        }
    }

    private Texture2D[] CrunchedTextures
    {
        get
        {
            if (crunchedTexturesCache == null)
            {
                var exclusions = GetAllExcludedTransforms();
                var tuple = root.GetComponentsInChildren<Renderer>(true)
                    .Where(r => !r.gameObject.CompareTag("EditorOnly"))
                    .Where(r => !exclusions.Contains(r.transform))
                    .SelectMany(r => r.sharedMaterials).Distinct()
                    .Select(mat => (mat, ShaderAnalyzer.Parse(mat?.shader)))
                    .Where(t => t.Item2?.parsedCorrectly ?? false).ToArray();
                var textures = new HashSet<Texture2D>();
                foreach (var (mat, parsed) in tuple)
                {
                    if (!parsed.CanMergeTextures())
                        continue;
                    foreach (var prop in parsed.properties)
                    {
                        if (prop.type != ParsedShader.Property.Type.Texture2D)
                            continue;
                        var tex = mat.GetTexture(prop.name) as Texture2D;
                        if (tex != null && (tex.format == TextureFormat.DXT1Crunched || tex.format == TextureFormat.DXT5Crunched))
                            textures.Add(tex);
                    }
                }
                crunchedTexturesCache = textures.ToArray();
            }
            return crunchedTexturesCache;
        }
    }

    public bool IsLockedIn(Material material)
    {
        if (material == null)
            return false;
        if (material.HasProperty("_ShaderOptimizer") && material.GetInt("_ShaderOptimizer") == 1)
            return true;
        if (material.HasProperty("_ShaderOptimizerEnabled") && material.GetInt("_ShaderOptimizerEnabled") == 1)
            return true;
        if (material.HasProperty("__Baked") && material.GetInt("__Baked") == 1)
            return true;
        return false;
    }

    private bool Button(string label)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Space(15 * EditorGUI.indentLevel);
        var result = GUILayout.Button(label);
        GUILayout.EndHorizontal();
        return result;
    }

    private bool Toggle(string label, ref bool value)
    {
        bool output = EditorGUILayout.ToggleLeft(label, GUI.enabled ? value : false);
        if (GUI.enabled)
        {
            if (value != output)
            {
                ClearUICaches();
            }
            value = output;
        }
        return value;
    }

    private bool Foldout(string label, ref bool value)
    {
        bool output = EditorGUILayout.Foldout(value, label);
        if (value != output)
        {
            ClearUICaches();
        }
        return value = output;
    }

    private void DrawMatchedMaterialSlot(MaterialSlot slot, int indent)
    {
        indent *= 15;
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(indent);
        EditorGUILayout.ObjectField(slot.renderer, typeof(Renderer), true, GUILayout.Width(EditorGUIUtility.currentViewWidth / 2 - 20 - (indent)));
        int originalIndent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        EditorGUILayout.ObjectField(slot.material, typeof(Material), false);
        EditorGUI.indentLevel = originalIndent;
        EditorGUILayout.EndHorizontal();
    }

    public void DrawDebugList<T>(T[] array) where T : Object
    {
        foreach (var obj in array)
        {
            EditorGUILayout.ObjectField(obj, typeof(T), true);
        }
        if (array.Length == 0)
        {
            EditorGUILayout.LabelField("---");
        }
        else if (Button("Select All"))
        {
            if (typeof(Component).IsAssignableFrom(typeof(T)))
            {
                Selection.objects = array.Select(o => (o as Component).gameObject).ToArray();
            }
            else
            {
                Selection.objects = array;
            }
        }
    }

    private void DynamicTransformList(ref List<Transform> list)
    {
        list.Add(null);
        for (int i = 0; i < list.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            var output = EditorGUILayout.ObjectField(list[i], typeof(Transform), true) as Transform;
            if (i == list.Count - 1)
            {
                GUILayout.Space(23);
            }
            else if (GUILayout.Button("X", GUILayout.Width(20)))
            {
                output = null;
            }
            EditorGUILayout.EndHorizontal();
            if (list[i] != output)
            {
                ClearUICaches();
            }
            if (output != null && GetTransformPathToRoot(output) == null)
            {
                output = null;
            }
            list[i] = output;
        }
        list = list.Where(o => o != null).ToList();
    }

    static Texture _perfIcon_Excellent;
    static Texture _perfIcon_Good;
    static Texture _perfIcon_Medium;
    static Texture _perfIcon_Poor;
    static Texture _perfIcon_VeryPoor;

    private Texture GetPerformanceIconForRating(PerformanceRating value)
    {
        if (_perfIcon_Excellent == null)
            _perfIcon_Excellent = Resources.Load<Texture>("PerformanceIcons/Perf_Great_32");
        if (_perfIcon_Good == null)
            _perfIcon_Good = Resources.Load<Texture>("PerformanceIcons/Perf_Good_32");
        if (_perfIcon_Medium == null)
            _perfIcon_Medium = Resources.Load<Texture>("PerformanceIcons/Perf_Medium_32");
        if (_perfIcon_Poor == null)
            _perfIcon_Poor = Resources.Load<Texture>("PerformanceIcons/Perf_Poor_32");
        if (_perfIcon_VeryPoor == null)
            _perfIcon_VeryPoor = Resources.Load<Texture>("PerformanceIcons/Perf_Horrible_32");

        switch (value)
        {
            case PerformanceRating.Excellent:
                return _perfIcon_Excellent;
            case PerformanceRating.Good:
                return _perfIcon_Good;
            case PerformanceRating.Medium:
                return _perfIcon_Medium;
            case PerformanceRating.Poor:
                return _perfIcon_Poor;
            default:
                return _perfIcon_VeryPoor;
        }
    }

    PerformanceRating GetPerfRank(int count, int[] perfLevels)
    {
        int level = 0;
        while(level < perfLevels.Length && count > perfLevels[level])
        {
            level++;
        }
        level++;
        return (PerformanceRating)level;
    }

    private void PerfRankChangeLabel(string label, int oldValue, int newValue, AvatarPerformanceCategory category)
    {
        var oldRating = PerformanceRating.VeryPoor;
        var newRating = PerformanceRating.VeryPoor;
        switch (category)
        {
            case AvatarPerformanceCategory.SkinnedMeshCount:
                oldRating = GetPerfRank(oldValue, new int[] {1, 2, 8, 16, int.MaxValue});
                newRating = GetPerfRank(newValue, new int[] {1, 2, 8, 16, int.MaxValue});
                break;
            case AvatarPerformanceCategory.MeshCount:
                oldRating = GetPerfRank(oldValue, new int[] {4, 8, 16, 24, int.MaxValue});
                newRating = GetPerfRank(newValue, new int[] {4, 8, 16, 24, int.MaxValue});
                break;
            case AvatarPerformanceCategory.MaterialCount:
                oldRating = GetPerfRank(oldValue, new int[] {4, 8, 16, 32, int.MaxValue});
                newRating = GetPerfRank(newValue, new int[] {4, 8, 16, 32, int.MaxValue});
                break;
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(new GUIContent(GetPerformanceIconForRating(oldRating)), GUILayout.Width(15));
        EditorGUILayout.LabelField($"{oldValue}", GUILayout.Width(25));
        EditorGUILayout.LabelField($"->", GUILayout.Width(25));
        EditorGUILayout.LabelField(new GUIContent(GetPerformanceIconForRating(newRating)), GUILayout.Width(15));
        EditorGUILayout.LabelField($"{newValue}", GUILayout.Width(25));
        EditorGUILayout.LabelField(label);
        EditorGUILayout.EndHorizontal();
    }

    private void RemoveIllegalComponents(GameObject target)
    {
        // call VRC.SDK3.Validation.AvatarValidation.RemoveIllegalComponents(target, true) with reflection if it exists
        var RemoveIllegalComponents = Type.GetType("VRC.SDK3.Validation.AvatarValidation, Assembly-CSharp")
            ?.GetMethod("RemoveIllegalComponents", BindingFlags.Static | BindingFlags.Public);
        if (RemoveIllegalComponents != null)
        {
            RemoveIllegalComponents.Invoke(null, new object[] { target, true });
            return;
        }

        // if not found use newer sdk method with reflection
        // VRC.SDK3.Validation.AvatarValidation.GetComponentWhitelist()
        // VRC.SDKBase.Validation.ValidationUtils.RemoveIllegalComponents(GameObject target, HashSet<Type> whitelist, bool retry, bool onlySceneObjects, bool logStripping)
        var GetComponentWhitelist = Type.GetType("VRC.SDK3.Validation.AvatarValidation, Assembly-CSharp")
            ?.GetMethod("GetComponentWhitelist", BindingFlags.Static | BindingFlags.NonPublic);
        RemoveIllegalComponents = Type.GetType("VRC.SDKBase.Validation.ValidationUtils, Assembly-CSharp")
            ?.GetMethod("RemoveIllegalComponents", BindingFlags.Static | BindingFlags.Public);
        if (GetComponentWhitelist != null && RemoveIllegalComponents != null)
        {
            var whitelist = GetComponentWhitelist.Invoke(null, null) as HashSet<Type>;
            RemoveIllegalComponents.Invoke(null, new object[] { target, whitelist, true, false, true });
            return;
        }

        Debug.LogWarning("Could not find RemoveIllegalComponents method");
    }
    
    public override void OnInspectorGUI()
    {
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
        {
            EditorGUILayout.HelpBox("Quest avatars don't support custom shaders. As such this tool can't work for Quest.", MessageType.Error);
            return;
        }
        settings = (d4rkAvatarOptimizer)target;
        root = settings.gameObject;
        OnSelectionChange();
        if (nullMaterial == null)
        {
            nullMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
            nullMaterial.name = "(null material slot)";
        }

        var path = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
        scriptPath = path.Substring(0, path.LastIndexOf('/'));
        scriptPath = scriptPath.Substring(0, scriptPath.LastIndexOf('/'));

        Toggle("Write Properties as Static Values", ref settings.WritePropertiesAsStaticValues);
        GUI.enabled = Toggle("Merge Skinned Meshes", ref settings.MergeSkinnedMeshes);
        EditorGUI.indentLevel++;
        Toggle("Merge Static Meshes as Skinned", ref settings.MergeStaticMeshesAsSkinned);
        Toggle("Merge Regardless of Blend Shapes", ref settings.ForceMergeBlendShapeMissMatch);
        Toggle("Keep Material Animations Separate", ref settings.KeepMaterialPropertyAnimationsSeparate);
        EditorGUI.indentLevel--;
        GUI.enabled = true;
        GUI.enabled = Toggle("Merge Different Property Materials", ref settings.MergeDifferentPropertyMaterials);
        EditorGUI.indentLevel++;
        Toggle("Merge Same Dimension Textures", ref settings.MergeSameDimensionTextures);
        Toggle("Merge Cull Back with Cull Off", ref settings.MergeBackFaceCullingWithCullingOff);
        Toggle("Merge Different Render Queue", ref settings.MergeDifferentRenderQueue);
        EditorGUI.indentLevel--;
        GUI.enabled = true;
        Toggle("Delete Unused Components", ref settings.DeleteUnusedComponents);
        Toggle("Delete Unused Game Objects", ref settings.DeleteUnusedGameObjects);
        Toggle("Use Ring Finger as Foot Collider", ref settings.UseRingFingerAsFootCollider);
        Toggle("Profile Time Used", ref settings.ProfileTimeUsed);

        if (settings.ExcludeTransforms == null)
            settings.ExcludeTransforms = new List<Transform>();
        if (Foldout($"Exclusions ({settings.ExcludeTransforms.Count})", ref settings.ShowExcludedTransforms))
        {
            EditorGUI.indentLevel++;
            DynamicTransformList(ref settings.ExcludeTransforms);
            EditorGUI.indentLevel--;
        }

        Profiler.enabled = settings.ProfileTimeUsed;
        Profiler.Reset();

        Profiler.StartSection("Validate");
        GUI.enabled = Validate();
        Profiler.EndSection();

        if (GUILayout.Button("Create Optimized Copy"))
        {
            var oldCulture = Thread.CurrentThread.CurrentCulture;
            var oldUICulture = Thread.CurrentThread.CurrentUICulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
                Profiler.enabled = settings.ProfileTimeUsed;
                Profiler.Reset();
                DisplayProgressBar("Copy Avatar", 0);
                AssignNewAvatarIDIfEmpty();
                var copy = Instantiate(settings.gameObject);
                SceneManager.MoveGameObjectToScene(copy, settings.gameObject.scene);
                copy.name = settings.gameObject.name + "(BrokenCopy)";
                DestroyImmediate(copy.GetComponent<d4rkAvatarOptimizer>());
                if (copy.GetComponent<VRCAvatarDescriptor>() != null)
                    RemoveIllegalComponents(copy);
                Optimize(copy);
                copy.name = settings.gameObject.name + "(OptimizedCopy)";
                copy.SetActive(true);
                settings.gameObject.SetActive(false);
                Selection.objects = new Object[] { copy };
                Profiler.PrintTimeUsed();
                Profiler.Reset();
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = oldCulture;
                Thread.CurrentThread.CurrentUICulture = oldUICulture;
                EditorUtility.ClearProgressBar();
            }
            return;
        }

        EditorGUILayout.Separator();
        GUI.enabled = true;

        if (Foldout("Show Merge Preview", ref settings.ShowMeshAndMaterialMergePreview))
        {
            Profiler.StartSection("Show Perf Rank Change");
            var exclusions = GetAllExcludedTransforms();
            var particleSystemCount = root.GetComponentsInChildren<ParticleSystem>(true)
                .Where(r => !r.gameObject.CompareTag("EditorOnly")).Count();
            int skinnedMeshCount = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => !r.gameObject.CompareTag("EditorOnly")).Count();
            int meshCount = root.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => !r.gameObject.CompareTag("EditorOnly")).Count();
            int totalMaterialCount = root.GetComponentsInChildren<Renderer>(true)
                .Where(r => !r.gameObject.CompareTag("EditorOnly"))
                .Sum(r => r.GetSharedMesh() == null ? 0 : r.GetSharedMesh().subMeshCount) + particleSystemCount;
            int optimizedSkinnedMeshCount = 0;
            int optimizedMeshCount = 0;
            int optimizedTotalMaterialCount = 0;
            foreach (var matched in MergedMaterialPreview)
            {
                var renderers = matched.SelectMany(m => m).Select(slot => slot.renderer).Distinct().ToArray();
                if (renderers.Any(r => r is SkinnedMeshRenderer) || renderers.Length > 1)
                {
                    optimizedSkinnedMeshCount++;
                    if (exclusions.Contains(renderers[0].transform))
                        optimizedTotalMaterialCount += renderers[0].GetSharedMesh().subMeshCount;
                    else
                        optimizedTotalMaterialCount += matched.Count;
                }
                else if (renderers[0] is MeshRenderer)
                {
                    optimizedMeshCount++;
                    var mesh = renderers[0].GetSharedMesh();
                    optimizedTotalMaterialCount += mesh == null ? 0 : mesh.subMeshCount;
                }
                else // ParticleSystemRenderer
                {
                    optimizedTotalMaterialCount += 1;
                }
            }
            PerfRankChangeLabel("Skinned Mesh Renderers", skinnedMeshCount, optimizedSkinnedMeshCount, AvatarPerformanceCategory.SkinnedMeshCount);
            PerfRankChangeLabel("Mesh Renderers", meshCount, optimizedMeshCount, AvatarPerformanceCategory.MeshCount);
            PerfRankChangeLabel("Material Slots", totalMaterialCount, optimizedTotalMaterialCount, AvatarPerformanceCategory.MaterialCount);
            Profiler.EndSection();

            Profiler.StartSection("Show Merge Preview");
            foreach (var matched in MergedMaterialPreview)
            {
                EditorGUILayout.Space(8);
                for (int i = 0; i < matched.Count; i++)
                {
                    for (int j = 0; j < matched[i].Count; j++)
                    {
                        int indent = j == 0 ? 0 : 1;
                        DrawMatchedMaterialSlot(matched[i][j], indent);
                    }
                }
            }
            Profiler.EndSection();
        }

        EditorGUILayout.Separator();

        if (Foldout("Debug Info", ref settings.ShowDebugInfo))
        {
            EditorGUI.indentLevel++;
            if (Foldout("Unparsable Materials", ref settings.DebugShowUnparsableMaterials))
            {
                Profiler.StartSection("Unparsable Materials");
                var list = root.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(r => r.sharedMaterials).Distinct()
                    .Select(mat => (mat, ShaderAnalyzer.Parse(mat?.shader)))
                    .Where(t => !(t.Item2 != null && t.Item2.parsedCorrectly))
                    .Select(t => t.mat).ToArray();
                foreach (var shader in list.Select(mat => mat?.shader).Distinct())
                {
                    var parsed = ShaderAnalyzer.Parse(shader);
                    EditorGUILayout.HelpBox((shader?.name ?? "Missing shader") + "\n" +
                        (parsed?.errorMessage ?? "Missing shader can't be parsed."),
                        MessageType.Info);
                    var materialsWithThisShader = list.Where(mat => mat?.shader == shader).ToArray();
                    DrawDebugList(materialsWithThisShader);
                }
                Profiler.EndSection();
            }
            if (Foldout("Unmergable Materials", ref settings.DebugShowUnmergableMaterials))
            {
                Profiler.StartSection("Unmergable Materials");
                var list = root.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(r => r.sharedMaterials).Distinct()
                    .Select(mat => (mat, ShaderAnalyzer.Parse(mat?.shader)))
                    .Where(t => (t.Item2 != null && t.Item2.parsedCorrectly && !t.Item2.CanMerge()))
                    .Select(t => t.mat).ToArray();
                foreach (var shader in list.Select(mat => mat.shader).Distinct())
                {
                    var parsed = ShaderAnalyzer.Parse(shader);
                    EditorGUILayout.HelpBox(shader.name + "\n" + parsed.CantMergeReason(), MessageType.Info);
                    var materialsWithThisShader = list.Where(mat => mat.shader == shader).ToArray();
                    DrawDebugList(materialsWithThisShader);
                }
                Profiler.EndSection();
            }
            if (Foldout("Unmergable Texture Materials", ref settings.DebugShowUnmergableTextureMaterials))
            {
                Profiler.StartSection("Unmergable Texture Materials");
                var list = root.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(r => r.sharedMaterials).Distinct()
                    .Select(mat => (mat, ShaderAnalyzer.Parse(mat?.shader)))
                    .Where(t => (t.Item2 != null && t.Item2.CanMerge() && !t.Item2.CanMergeTextures()))
                    .Select(t => t.mat).ToArray();
                foreach (var shader in list.Select(mat => mat.shader).Distinct())
                {
                    var parsed = ShaderAnalyzer.Parse(shader);
                    EditorGUILayout.HelpBox(shader.name + "\n" + parsed.CantMergeTexturesReason(), MessageType.Info);
                    var materialsWithThisShader = list.Where(mat => mat.shader == shader).ToArray();
                    DrawDebugList(materialsWithThisShader);
                }
            }
            if (Foldout("Crunched Textures", ref settings.DebugShowCrunchedTextures))
            {
                Profiler.StartSection("Crunched Textures");
                DrawDebugList(CrunchedTextures);
                Profiler.EndSection();
            }
            if (Foldout("Locked in Materials", ref settings.DebugShowLockedInMaterials))
            {
                Profiler.StartSection("Locked in Materials");
                var list = root.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(r => r.sharedMaterials).Distinct()
                    .Where(mat => IsLockedIn(mat)).ToArray();
                DrawDebugList(list);
                Profiler.EndSection();
            }
            if (Foldout("Unused Components", ref settings.DebugShowUnusedComponents))
            {
                Profiler.StartSection("Unused Components");
                DrawDebugList(UnusedComponents);
                Profiler.EndSection();
            }
            if (Foldout("Always Disabled Game Objects", ref settings.DebugShowAlwaysDisabledGameObjects))
            {
                Profiler.StartSection("Always Disabled Game Objects");
                DrawDebugList(AlwaysDisabledGameObjects);
                Profiler.EndSection();
            }
            if (Foldout("Material Swaps", ref settings.DebugShowMaterialSwaps))
            {
                Profiler.StartSection("Material Swaps");
                var map = FindAllMaterialSwapMaterials();
                foreach (var pair in map)
                {
                    EditorGUILayout.LabelField(pair.Key.path + " -> " + pair.Key.index);
                    EditorGUI.indentLevel++;
                    DrawDebugList(pair.Value.ToArray());
                    EditorGUI.indentLevel--;
                }
                if (map.Count == 0)
                {
                    EditorGUILayout.LabelField("---");
                }
                Profiler.EndSection();
            }
            if (Foldout("Game Objects with Toggle Animation", ref settings.DebugShowGameObjectsWithToggle))
            {
                Profiler.StartSection("Game Objects with Toggle Animation");
                DrawDebugList(GameObjectsWithToggleAnimations);
                Profiler.EndSection();
            }
            if (Foldout("Unmoving Bones", ref settings.DebugShowUnmovingBones))
            {
                Profiler.StartSection("Unmoving Bones");
                DrawDebugList(UnmovingBones);
                Profiler.EndSection();
            }
            EditorGUI.indentLevel--;
        }
        if (settings.ProfileTimeUsed)
        {
            EditorGUILayout.Separator();
            var timeUsed = Profiler.FormatTimeUsed().Take(6).ToArray();
            foreach (var time in timeUsed)
            {
                EditorGUILayout.LabelField(time);
            }
        }
    }
}
#endif