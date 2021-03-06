#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEditor.Animations;
using UnityEditor;
using d4rkpl4y3r;
using d4rkpl4y3r.Util;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;

using Math = System.Math;
using Type = System.Type;
using AnimationPath = System.ValueTuple<string, string, System.Type>;

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

    private static string GetTransformPathToRoot(Transform t)
    {
        if (t == root.transform)
            return "";
        string path = t.name;
        while ((t = t.parent) != root.transform)
        {
            path = t.name + "/" + path;
        }
        return path;
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
        if (material == null)
            return false;
        var parsedShader = ShaderAnalyzer.Parse(material.shader);
        if (!parsedShader.couldParse)
            return false;
        if (parsedShader.passes.Any(pass => pass.vertex == null || pass.fragment == null))
            return false;
        if (parsedShader.passes.Any(pass => pass.domain != null || pass.hull != null))
            return false;
        return true;
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
        foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
        {
            if (skinnedMeshRenderer.gameObject.activeSelf)
                continue;
            if (togglePaths.Contains(GetPathToRoot(skinnedMeshRenderer)))
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
        var unused = FindAllUnusedSkinnedMeshRenderers();
        gameObjectTogglePaths = FindAllGameObjectTogglePaths();
        slotSwapMaterials = FindAllMaterialSwapMaterials();
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        var matchedSkinnedMeshes = new List<List<Renderer>>();
        foreach (var renderer in renderers)
        {
            var mesh = renderer.GetSharedMesh();
            if (mesh == null || renderer.gameObject.CompareTag("EditorOnly") || unused.Contains(renderer))
                continue;

            bool foundMatch = false;
            foreach (var subList in matchedSkinnedMeshes)
            {
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

        var optimizedAnimations = new Dictionary<AnimationClip, AnimationClip>();
        foreach (var clip in animations)
        {
            optimizedAnimations[clip] = FixAnimationClipPaths(clip);
        }
        
        for (int i = 0; i < avDescriptor.baseAnimationLayers.Length; i++)
        {
            var layer = avDescriptor.baseAnimationLayers[i].animatorController as AnimatorController;
            if (layer == null)
                continue;
            string path = AssetDatabase.GenerateUniqueAssetPath(trashBinPath + layer.name + ".controller");
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(layer), path);
            var newLayer = (AnimatorController)
                AssetDatabase.LoadAssetAtPath(path, typeof(AnimatorController));

            foreach (var state in newLayer.EnumerateAllStates())
            {
                var clip = state.motion as AnimationClip;
                var blendTree = state.motion as BlendTree;
                if (blendTree != null)
                {
                    var childNodes = blendTree.children;
                    for (int j = 0; j < childNodes.Length; j++)
                    {
                        clip = childNodes[j].motion as AnimationClip;
                        if (clip != null)
                        {
                            childNodes[j].motion = optimizedAnimations[clip];
                        }
                    }
                    blendTree.children = childNodes;
                }
                else if (clip != null)
                {
                    state.motion = optimizedAnimations[clip];
                }
                else
                {
                    state.motion = dummyAnimationToFillEmptyStates;
                }
            }

            avDescriptor.baseAnimationLayers[i].animatorController = newLayer;
        }
        AssetDatabase.SaveAssets();
    }

    private static Dictionary<(string path, int index), HashSet<Material>> FindAllMaterialSwapMaterials()
    {
        var result = new Dictionary<(string path, int index), HashSet<Material>>();
        var avDescriptor = root.GetComponent<VRCAvatarDescriptor>();
        var fxLayer = avDescriptor?.baseAnimationLayers[4].animatorController as AnimatorController;
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

    private static void OptimizeMaterialSwapMaterials()
    {
        slotSwapMaterials = FindAllMaterialSwapMaterials();
        var mergedMeshes = FindPossibleSkinnedMeshMerges();
        optimizedSlotSwapMaterials.Clear();
        foreach (var entry in slotSwapMaterials)
        {
            int meshToggleCount = 0;
            var current = GetTransformFromPath(entry.Key.path).GetComponent<Renderer>();
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
                if (root.GetComponentsInChildren<SkinnedMeshRenderer>().All(r => r != meshRenderer))
                {
                    Debug.LogWarning("Viseme SkinnedMeshRenderer is not a child of the avatar root.");
                }
                else
                {
                    string path = GetPathToRoot(meshRenderer) + "/blendShape.";
                    foreach (var blendShapeName in avDescriptor.VisemeBlendShapes)
                    {
                        usedBlendShapes.Add(path + blendShapeName);
                    }
                }
            }
            if (avDescriptor.customEyeLookSettings.eyelidType
                == VRCAvatarDescriptor.EyelidType.Blendshapes
                && avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh != null)
            {
                var meshRenderer = avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh;
                if (root.GetComponentsInChildren<SkinnedMeshRenderer>().All(r => r != meshRenderer))
                {
                    Debug.LogWarning("Eyelid SkinnedMeshRenderer is not a child of the avatar root.");
                }
                else
                {
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
            }
            var fxLayer = avDescriptor.baseAnimationLayers[4].animatorController as AnimatorController;
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
        var avDescriptor = root.GetComponent<VRCAvatarDescriptor>();
        var fxLayer = avDescriptor?.baseAnimationLayers[4].animatorController as AnimatorController;
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
        var avDescriptor = root.GetComponent<VRCAvatarDescriptor>();
        var fxLayer = avDescriptor?.baseAnimationLayers[4].animatorController as AnimatorController;
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
        queue.Enqueue(root.transform);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
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
        var avDescriptor = root.GetComponent<VRCAvatarDescriptor>();
        var fxLayer = avDescriptor?.baseAnimationLayers[4].animatorController as AnimatorController;
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
        var alwaysDisabledBehaviours = new HashSet<Component>(root.transform.GetComponentsInChildren<Behaviour>(true)
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

        return alwaysDisabledBehaviours;
    }

    private static HashSet<Transform> FindAllUnmovingTransforms()
    {
        var avDescriptor = root.GetComponent<VRCAvatarDescriptor>();
        if (avDescriptor == null)
            return new HashSet<Transform>();
        var transforms = new HashSet<Transform>(root.transform.GetAllDescendants());

        if (avDescriptor.enableEyeLook)
        {
            transforms.Remove(avDescriptor.customEyeLookSettings.leftEye);
            transforms.Remove(avDescriptor.customEyeLookSettings.rightEye);
        }
        if (avDescriptor.customEyeLookSettings.eyelidType == VRCAvatarDescriptor.EyelidType.Bones)
        {
            transforms.Remove(avDescriptor.customEyeLookSettings.lowerLeftEyelid);
            transforms.Remove(avDescriptor.customEyeLookSettings.lowerRightEyelid);
            transforms.Remove(avDescriptor.customEyeLookSettings.upperLeftEyelid);
            transforms.Remove(avDescriptor.customEyeLookSettings.upperRightEyelid);
        }

        var layers = avDescriptor.baseAnimationLayers.Select(a => a.animatorController).ToList();
        layers.AddRange(avDescriptor.specialAnimationLayers.Select(a => a.animatorController));
        foreach (var layer in layers.Where(a => a != null))
        {
            foreach (var binding in layer.animationClips.SelectMany(clip => AnimationUtility.GetCurveBindings(clip)))
            {
                if (binding.type == typeof(Transform))
                {
                    transforms.Remove(GetTransformFromPath(binding.path));
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
                transforms.Remove(animator.GetBoneTransform(boneId));
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
                transforms.Remove(current);
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
            transforms.Remove(constraint.transform);
        }

        return transforms;
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
            if (parsedShader == null || !parsedShader.couldParse)
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
        int matIndex = 0;
        foreach (var source in sources)
        {
            var parsedShader = ShaderAnalyzer.Parse(source[0]?.shader);
            if (parsedShader == null || !parsedShader.couldParse)
            {
                materials[matIndex++] = source[0];
                continue;
            }
            var texturesToMerge = new HashSet<string>();
            var propertyTextureArrayIndex = new Dictionary<string, int>();
            var arrayPropertyValues = new Dictionary<string, (string type, List<string> values)>();
            foreach (var mat in source)
            {
                foreach (var prop in parsedShader.properties)
                {
                    if (!mat.HasProperty(prop.name))
                        continue;
                    switch (prop.type)
                    {
                        case ParsedShader.Property.Type.Float:
                            (string type, List<string> values) propertyArray;
                            if (!arrayPropertyValues.TryGetValue(prop.name, out propertyArray))
                            {
                                propertyArray.type = "float";
                                propertyArray.values = new List<string>();
                                arrayPropertyValues[prop.name] = propertyArray;
                            }
                            propertyArray.values.Add("" + mat.GetFloat(prop.name));
                        break;
                        case ParsedShader.Property.Type.Vector:
                            if (!arrayPropertyValues.TryGetValue(prop.name, out propertyArray))
                            {
                                propertyArray.type = "float4";
                                propertyArray.values = new List<string>();
                                arrayPropertyValues[prop.name] = propertyArray;
                            }
                            propertyArray.values.Add("float4" + mat.GetVector(prop.name));
                        break;
                        case ParsedShader.Property.Type.Int:
                            if (!arrayPropertyValues.TryGetValue(prop.name, out propertyArray))
                            {
                                propertyArray.type = "int";
                                propertyArray.values = new List<string>();
                                arrayPropertyValues[prop.name] = propertyArray;
                            }
                            propertyArray.values.Add("" + mat.GetInt(prop.name));
                        break;
                        case ParsedShader.Property.Type.Color:
                            if (!arrayPropertyValues.TryGetValue(prop.name, out propertyArray))
                            {
                                propertyArray.type = "float4";
                                propertyArray.values = new List<string>();
                                arrayPropertyValues[prop.name] = propertyArray;
                            }
                            propertyArray.values.Add(mat.GetColor(prop.name).ToString("F6").Replace("RGBA", "float4"));
                            break;
                        case ParsedShader.Property.Type.Texture2D:
                            if (!arrayPropertyValues.TryGetValue("arrayIndex" + prop.name, out var textureArray))
                            {
                                arrayPropertyValues["arrayIndex" + prop.name] = ("int", new List<string>());
                                arrayPropertyValues["shouldSample" + prop.name] = ("bool", new List<string>());
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
                                    texturesToMerge.Add(prop.name);
                                    propertyTextureArrayIndex[prop.name] = texArrayIndex;
                                }
                            }
                            arrayPropertyValues["arrayIndex" + prop.name].values.Add("" + index);
                            arrayPropertyValues["shouldSample" + prop.name].values.Add((tex != null).ToString().ToLowerInvariant());
                            if (!arrayPropertyValues.TryGetValue(prop.name + "_ST", out propertyArray))
                            {
                                propertyArray.type = "float4";
                                propertyArray.values = new List<string>();
                                arrayPropertyValues[prop.name + "_ST"] = propertyArray;
                            }
                            var scale = mat.GetTextureScale(prop.name);
                            var offset = mat.GetTextureOffset(prop.name);
                            propertyArray.values.Add("float4(" + scale.x + "," + scale.y + "," + offset.x + "," + offset.y + ")");
                            if (!arrayPropertyValues.TryGetValue(prop.name + "_TexelSize", out propertyArray))
                            {
                                propertyArray.type = "float4";
                                propertyArray.values = new List<string>();
                                arrayPropertyValues[prop.name + "_TexelSize"] = propertyArray;
                            }
                            var texelSize = new Vector2(tex?.width ?? 8, tex?.height ?? 8);
                            propertyArray.values.Add($"float4(1.0 / {texelSize.x}, 1.0 / {texelSize.y}, {texelSize.x}, {texelSize.y})");
                            break;
                    }
                }
            }

            string cullReplace = null;
            var cullProp = parsedShader.properties.FirstOrDefault(p => p.shaderLabParams.Count == 1 && p.shaderLabParams.First() == "Cull");
            if (cullProp != null)
            {
                int firstCull = source[0].GetInt(cullProp.name);
                if (source.Any(m => m.GetInt(cullProp.name) != firstCull))
                {
                    cullReplace = cullProp.name;
                }
            }

            var replace = new Dictionary<string, string>();
            foreach (var tuple in arrayPropertyValues.ToList())
            {
                if (usedMaterialProps.Contains(tuple.Key) && !(meshToggleCount > 1 && settings.KeepMaterialPropertyAnimationsSeparate))
                {
                    arrayPropertyValues.Remove(tuple.Key);
                }
                else if (tuple.Value.values.All(v => v == tuple.Value.values[0]))
                {
                    arrayPropertyValues.Remove(tuple.Key);
                    replace[tuple.Key] = tuple.Value.values[0];
                }
            }
            if (!settings.WritePropertiesAsStaticValues)
            {
                replace = null;
            }

            var texturesToCheckNull = new Dictionary<string, string>();
            foreach (var prop in parsedShader.properties)
            {
                if (prop.type == ParsedShader.Property.Type.Texture2D)
                {
                    if (arrayPropertyValues.ContainsKey("shouldSample" + prop.name))
                    {
                        texturesToCheckNull[prop.name] = prop.defaultValue;
                    }
                }
            }

            var animatedPropertyValues = new Dictionary<string, string>();
            if (meshToggleCount > 1 && settings.KeepMaterialPropertyAnimationsSeparate)
            {
                foreach (var propName in usedMaterialProps)
                {
                    if (parsedShader.propertyTable.TryGetValue(propName, out var prop))
                    {
                        string type = "float4";
                        if (prop.type == ParsedShader.Property.Type.Float)
                            type = "float";
                        if (prop.type == ParsedShader.Property.Type.Int)
                            type = "int";
                        animatedPropertyValues[propName] = type;
                    }
                }
            }

            var setShaderKeywords = parsedShader.shaderFeatureKeyWords.Where(k => source[0].IsKeywordEnabled(k)).ToList();

            Profiler.StartSection("ShaderOptimizer.Run()");
            var optimizedShader = ShaderOptimizer.Run(parsedShader, replace, meshToggleCount,
                arrayPropertyValues, texturesToCheckNull, texturesToMerge, animatedPropertyValues, setShaderKeywords);
            Profiler.EndSection();
            var name = System.IO.Path.GetFileName(source[0].shader.name);
            name = source[0].name + " " + name;
            var shaderFilePath = AssetDatabase.GenerateUniqueAssetPath(trashBinPath + name + ".shader");
            name = System.IO.Path.GetFileNameWithoutExtension(shaderFilePath);
            optimizedShader[0] = "Shader \"d4rkpl4y3r/Optimizer/" + name + "\"";
            System.IO.File.WriteAllLines(shaderFilePath, optimizedShader);
            var optimizedMaterial = Instantiate(source[0]);
            foreach (var keyword in setShaderKeywords)
            {
                optimizedMaterial.DisableKeyword(keyword);
            }
            if (cullReplace != null)
            {
                optimizedMaterial.SetInt(cullReplace, 0);
            }
            optimizedMaterial.name = name;
            materials[matIndex++] = optimizedMaterial;
            optimizedMaterials.Add(optimizedMaterial);
            int renderQueue = optimizedMaterial.renderQueue;
            optimizedMaterial.shader = null;
            optimizedMaterial.renderQueue = renderQueue;
            foreach (var prop in parsedShader.properties)
            {
                if (prop.type != ParsedShader.Property.Type.Texture2D)
                    continue;
                var tex = source.Select(m => m.GetTexture(prop.name)).FirstOrDefault(t => t != null);
                optimizedMaterial.SetTexture(prop.name, tex);
            }
            var arrayList = new List<(string name, Texture2DArray array)>();
            foreach (var texArray in propertyTextureArrayIndex)
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
            int renderQueue = mat.renderQueue;
            Profiler.StartSection("AssetDatabase.LoadAssetAtPath<Shader>()");
            mat.shader = AssetDatabase.LoadAssetAtPath<Shader>(trashBinPath + mat.name + ".shader");
            Profiler.EndSection();
            mat.renderQueue = renderQueue;
            if (texArrayPropertiesToSet.TryGetValue(mat, out var texArrays))
            {
                Profiler.StartSection("SetTextureArrayProperties");
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
                Profiler.EndSection();
            }
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
                            if (isVector || isColor)
                            {
                                mat.SetVector(propName + mID, props.GetVector(propName + mID));
                            }
                            else
                            {
                                mat.SetFloat(propName + mID, props.GetFloat(propName + mID));
                            }
                        }
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
        if (a == null || b == null)
            return true;
        if (a.texelSize != b.texelSize)
            return false;
        if (!(a is Texture2D) || !(b is Texture2D))
            return false;
        var a2D = a as Texture2D;
        var b2D = b as Texture2D;
        if (a2D.format != b2D.format)
            return false;
        if (IsTextureLinear(a2D) != IsTextureLinear(b2D))
            return false;
        return true;
    }

    private static bool CanCombineMaterialsWith(List<MaterialSlot> list, MaterialSlot candidate)
    {
        var candidateMat = candidate.material;
        var firstMat = list[0].material;
        if (firstMat.shader != candidateMat.shader)
            return false;
        var parsedShader = ShaderAnalyzer.Parse(candidateMat.shader);
        if (parsedShader.couldParse == false)
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
        bool mergeTextures = settings.MergeSameDimensionTextures && !parsedShader.misMatchedCurlyBraces;
        foreach (var prop in parsedShader.properties)
        {
            foreach (var slot in list)
            {
                var mat = slot.material;
                switch (prop.type)
                {
                    case ParsedShader.Property.Type.Color:
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
        foreach (var meshRenderer in meshRenderers)
        {
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
        foreach (var meshRenderer in skinnedMeshRenderers)
        {
            var mesh = meshRenderer.sharedMesh;
            if (mesh == null)
                continue;

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
        movingParentMap = FindMovingParent();
        materialSlotRemap = new Dictionary<(string, int), (string, int)>();
        int combinedMeshID = 0;
        foreach (var combinableMeshes in combinableSkinnedMeshList)
        {
            var combinableSkinnedMeshes = combinableMeshes.Select(m => m as SkinnedMeshRenderer).Where(m => m != null).ToList();
            if (combinableSkinnedMeshes.Count < 1)
                continue;

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
                var sourceBones = skinnedMesh.bones;
                var toWorldArray = Enumerable.Range(0, skinnedMesh.bones.Length).Select(i =>
                    skinnedMesh.bones[i].localToWorldMatrix * skinnedMesh.sharedMesh.bindposes[i]
                    ).ToArray();
                var aabb = skinnedMesh.localBounds;
                var m = toLocal * (skinnedMesh.rootBone ?? skinnedMesh.transform).localToWorldMatrix;
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
                    properties.SetFloat("_IsActiveMesh" + meshID, skinnedMesh.gameObject.activeSelf ? 1f : 0f);
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
                                    newPropertyName = $"material.d4rkAvatarOptimizer{propName}{mID}";
                                    float signal = System.BitConverter.ToSingle(new byte[] {0x55, 0x55, 0x55, 0xFF}, 0);
                                    if (isVector || isColor)
                                    {
                                        properties.SetVector("d4rkAvatarOptimizer" + propName + mID, new Vector4(signal, signal, signal, signal));
                                    }
                                    else
                                    {
                                        properties.SetFloat("d4rkAvatarOptimizer" + propName + mID, signal);
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
        
        foreach (var constraint in root.GetComponentsInChildren<Behaviour>(true).OfType<IConstraint>())
        {
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

        foreach (var animator in root.GetComponentsInChildren<Animator>(true))
        {
            foreach (var boneId in System.Enum.GetValues(typeof(HumanBodyBones)).Cast<HumanBodyBones>())
            {
                if (boneId < 0 || boneId >= HumanBodyBones.LastBone)
                    continue;
                used.Add(animator.GetBoneTransform(boneId));
            }
        }

        foreach (var skinnedRenderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            used.Add(skinnedRenderer.rootBone);
        }

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            used.Add(renderer.probeAnchor);
        }

        foreach (var physBone in root.GetComponentsInChildren<VRCPhysBoneBase>(true))
        {
            var root = physBone.GetRootTransform();
            var exclusions = new HashSet<Transform>(physBone.ignoreTransforms);
            var stack = new Stack<Transform>();
            used.Add(root);
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (exclusions.Contains(current))
                    continue;
                used.Add(current);
                foreach (Transform child in current)
                {
                    stack.Push(child);
                }
            }
        }

        foreach (var obj in root.transform.GetAllDescendants())
        {
            if (obj.GetComponents<Component>().Length > 1)
            {
                used.Add(obj);
            }
        }

        used.UnionWith(gameObjectTogglePaths.Select(p => GetTransformFromPath(p)).Where(t => t != null));

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
            var mats = obj.GetComponent<MeshRenderer>().sharedMaterials;
            var lightAnchor = obj.GetComponent<MeshRenderer>().probeAnchor;
            var mesh = obj.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(obj.GetComponent<MeshFilter>());
            var skinnedMeshRenderer = obj.AddComponent<SkinnedMeshRenderer>();
            skinnedMeshRenderer.sharedMesh = mesh;
            skinnedMeshRenderer.sharedMaterials = mats;
            skinnedMeshRenderer.probeAnchor = lightAnchor;
            convertedMeshRendererPaths.Add(GetPathToRoot(obj));
        }
    }

    private static void Validate()
    {
        var avDescriptor = root.GetComponent<VRCAvatarDescriptor>();

        if (avDescriptor == null)
        {
            EditorGUILayout.HelpBox("No VRCAvatarDescriptor found on the root object", MessageType.Error);
            return;
        }

        if (settings.UseRingFingerAsFootCollider)
        {
            if (avDescriptor.collider_footL.transform == null || avDescriptor.collider_footR.transform == null)
            {
                EditorGUILayout.HelpBox("Foot collider transform not set.\nOpen the collider foldout in the avatar descriptor.", MessageType.Error);
            }
        }

        var allMaterials = root.GetComponentsInChildren<Renderer>()
            .SelectMany(r => r.sharedMaterials).Distinct().ToArray();

        var correctlyParsedMaterials = allMaterials
            .Where(m => (ShaderAnalyzer.Parse(m?.shader)?.HasParsedCorrectly() ?? false)).ToArray();

        if (correctlyParsedMaterials.Length != allMaterials.Length)
        {
            EditorGUILayout.HelpBox("One or more materials could not be parsed.\nCheck the Debug Info foldout for more info.", MessageType.Warning);
        }

        if (correctlyParsedMaterials.Any(m => !(ShaderAnalyzer.Parse(m?.shader)?.CanMerge() ?? false)))
        {
            EditorGUILayout.HelpBox("One or more materials do not support merging.\nCheck the Debug Info foldout for more info.", MessageType.Warning);
        }
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
        ShaderAnalyzer.ClearParsedShaderCache();
        ClearTrashBin();
        optimizedMaterials.Clear();
        newAnimationPaths.Clear();
        texArrayPropertiesToSet.Clear();
        keepTransforms.Clear();
        convertedMeshRendererPaths.Clear();
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
        animatedMaterialProperties = FindAllAnimatedMaterialProperties();
        Profiler.StartNextSection("OptimizeMaterialSwapMaterials()");
        OptimizeMaterialSwapMaterials();
        Profiler.StartNextSection("CombineSkinnedMeshes()");
        CombineSkinnedMeshes();
        Profiler.StartNextSection("CreateTextureArrays()");
        CreateTextureArrays();
        Profiler.StartNextSection("CombineAndOptimizeMaterials()");
        CombineAndOptimizeMaterials();
        Profiler.StartNextSection("OptimizeMaterialsOnNonSkinnedMeshes()");
        OptimizeMaterialsOnNonSkinnedMeshes();
        Profiler.StartNextSection("SaveOptimizedMaterials()");
        SaveOptimizedMaterials();
        Profiler.StartNextSection("DestroyUnusedGameObjects()");
        DestroyUnusedGameObjects();
        Profiler.StartNextSection("FixAllAnimationPaths()");
        FixAllAnimationPaths();
        Profiler.EndSection();
        MoveRingFingerColliderToFeet();
    }

    public bool Button(string label)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Space(15 * EditorGUI.indentLevel);
        var result = GUILayout.Button(label);
        GUILayout.EndHorizontal();
        return result;
    }

    public bool Toggle(string label, ref bool value)
    {
        bool output = EditorGUILayout.ToggleLeft(label, GUI.enabled ? value : false);
        if (GUI.enabled)
        {
            value = output;
        }
        return value;
    }

    public bool Foldout(string label, ref bool value)
    {
        return value = EditorGUILayout.Foldout(value, label);
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
    
    public override void OnInspectorGUI()
    {
        settings = (d4rkAvatarOptimizer)target;
        root = settings.gameObject;
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

        Validate();

        if (GUILayout.Button("Create Optimized Copy"))
        {
            Profiler.enabled = settings.ProfileTimeUsed;
            Profiler.Reset();
            AssignNewAvatarIDIfEmpty();
            var copy = Instantiate(settings.gameObject);
            copy.name = settings.gameObject.name + "(BrokenCopy)";
            DestroyImmediate(copy.GetComponent<d4rkAvatarOptimizer>());
            if (copy.GetComponent<VRCAvatarDescriptor>() != null)
                VRC.SDK3.Validation.AvatarValidation.RemoveIllegalComponents(copy);
            Optimize(copy);
            copy.name = settings.gameObject.name + "(OptimizedCopy)";
            copy.SetActive(true);
            settings.gameObject.SetActive(false);
            Selection.objects = new Object[] { copy };
            Profiler.PrintTimeUsed();
        }

        EditorGUILayout.Separator();

        if (Foldout("Show Merge Preview", ref settings.ShowMeshAndMaterialMergePreview))
        {
            CalculateUsedBlendShapePaths();
            var matchedSkinnedMeshes = FindPossibleSkinnedMeshMerges();
            foreach (var mergedMeshes in matchedSkinnedMeshes)
            {
                EditorGUILayout.Space(8);
                var matched = FindAllMergeAbleMaterials(mergedMeshes);
                for (int i = 0; i < matched.Count; i++)
                {
                    for (int j = 0; j < matched[i].Count; j++)
                    {
                        int indent = j == 0 ? 0 : 1;
                        DrawMatchedMaterialSlot(matched[i][j], indent);
                    }
                }
            }
        }

        EditorGUILayout.Separator();

        if (Foldout("Debug Info", ref settings.ShowDebugInfo))
        {
            EditorGUI.indentLevel++;
            if (Foldout("Unparsable Materials", ref settings.DebugShowUnparsableMaterials))
            {
                var list = root.GetComponentsInChildren<Renderer>()
                    .SelectMany(r => r.sharedMaterials).Distinct()
                    .Select(mat => (mat, ShaderAnalyzer.Parse(mat?.shader)))
                    .Where(t => !(t.Item2 != null && t.Item2.HasParsedCorrectly()))
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
            }
            if (Foldout("Unmergable Materials", ref settings.DebugShowUnmergableMaterials))
            {
                var list = root.GetComponentsInChildren<Renderer>()
                    .SelectMany(r => r.sharedMaterials).Distinct()
                    .Select(mat => (mat, ShaderAnalyzer.Parse(mat?.shader)))
                    .Where(t => (t.Item2 != null && t.Item2.HasParsedCorrectly()))
                    .Where(t => !t.Item2.CanMerge())
                    .Select(t => t.mat).ToArray();
                foreach (var shader in list.Select(mat => mat.shader).Distinct())
                {
                    var parsed = ShaderAnalyzer.Parse(shader);
                    EditorGUILayout.HelpBox(shader.name + "\n" + parsed.CantMergeReason(), MessageType.Info);
                    var materialsWithThisShader = list.Where(mat => mat.shader == shader).ToArray();
                    DrawDebugList(materialsWithThisShader);
                }
            }
            if (Foldout("Unused Components", ref settings.DebugShowUnusedComponents))
            {
                DrawDebugList(FindAllUnusedComponents().ToArray());
            }
            if (Foldout("Always Disabled Game Objects", ref settings.DebugShowAlwaysDisabledGameObjects))
            {
                DrawDebugList(FindAllAlwaysDisabledGameObjects().ToArray());
            }
            if (Foldout("Material Swaps", ref settings.DebugShowMaterialSwaps))
            {
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
            }
            if (Foldout("Game Objects with Toggle Animation", ref settings.DebugShowGameObjectsWithToggle))
            {
                var list = FindAllGameObjectTogglePaths().Select(p => GetTransformFromPath(p)?.gameObject)
                    .Where(obj => obj != null).ToArray();
                DrawDebugList(list);
            }
            if (Foldout("Unmoving Bones", ref settings.DebugShowUnmovingBones))
            {
                var bones = new HashSet<Transform>();
                var unmoving = FindAllUnmovingTransforms();
                root.GetComponentsInChildren<SkinnedMeshRenderer>().ToList().ForEach(
                    r => bones.UnionWith(r.bones.Where(b => unmoving.Contains(b))));
                DrawDebugList(bones.ToArray());
            }
            EditorGUI.indentLevel--;
        }
    }
}
#endif