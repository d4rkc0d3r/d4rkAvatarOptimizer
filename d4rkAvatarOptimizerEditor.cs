#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor.Animations;
using UnityEditor;

using AnimationPath = System.ValueTuple<string, string, System.Type>;

[CustomEditor(typeof(d4rkAvatarOptimizer))]
public class d4rkAvatarOptimizerEditor : Editor
{
    private static GameObject root;
    private static string trashBinPath = "Assets/d4rkAvatarOptimizer/TrashBin/";
    private static HashSet<string> usedBlendShapes = new HashSet<string>();
    private static Dictionary<AnimationPath, AnimationPath> newAnimationPaths = new Dictionary<AnimationPath, AnimationPath>();

    private static void ClearTrashBin()
    {
        string[] folderPath = { "Assets/d4rkAvatarOptimizer/TrashBin" };
        foreach (var asset in AssetDatabase.FindAssets("", folderPath))
        {
            var path = AssetDatabase.GUIDToAssetPath(asset);
            AssetDatabase.DeleteAsset(path);
        }
    }

    private static void CreateUniqueAsset(Object asset, string path)
    {
        AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(trashBinPath + path));
    }

    private static string GetTransformPathToRoot(Transform t)
    {
        string path = t.name;
        while ((t = t.parent) != root.transform)
        {
            path = t.name + "/" + path;
        }
        return path;
    }

    private static bool IsCombinableSkinnedMesh(SkinnedMeshRenderer candidate)
    {
        return true;
        foreach (var material in candidate.sharedMaterials)
        {
            if (material.shader.name == "Standard")
            {
                return false;
            }
        }
        return true;
    }

    private static bool CanCombineWith(List<SkinnedMeshRenderer> list, SkinnedMeshRenderer candidate)
    {
        if (!IsCombinableSkinnedMesh(list[0]))
            return false;
        if (!IsCombinableSkinnedMesh(candidate))
            return false;
        return true;
    }

    private static void AddAnimationPathChange(string pathSource, string nameSource, System.Type typeSource, string pathTarget, string nameTarget, System.Type typeTarget)
    {
        AnimationPath source = (pathSource, nameSource, typeSource);
        AnimationPath target = (pathTarget, nameTarget, typeTarget);
        if (source == target)
            return;
        newAnimationPaths[source] = target;
    }

    private static List<List<SkinnedMeshRenderer>> FindPossibleSkinnedMeshMerges()
    {
        var skinnedMeshRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var matchedSkinnedMeshes = new List<List<SkinnedMeshRenderer>>();
        foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
        {
            var mesh = skinnedMeshRenderer.sharedMesh;
            if (mesh == null)
                continue;

            bool foundMatch = false;
            foreach (var subList in matchedSkinnedMeshes)
            {
                if (CanCombineWith(subList, skinnedMeshRenderer))
                {
                    subList.Add(skinnedMeshRenderer);
                    foundMatch = true;
                    break;
                }
            }
            if (!foundMatch)
            {
                matchedSkinnedMeshes.Add(new List<SkinnedMeshRenderer> { skinnedMeshRenderer });
            }
        }
        return matchedSkinnedMeshes;
    }
    
    private static IEnumerable<Transform> GetAllChildren(Transform t)
    {
        var queue = new Queue<Transform>(t.Cast<Transform>());
        while (queue.Count > 0)
        {
            Transform parent = queue.Dequeue();
            yield return parent;
            foreach (var child in parent.Cast<Transform>())
            {
                queue.Enqueue(child);
            }
        }
    }
    
    private static int GetNewBoneIDFromOldID(List<Transform> bones, List<Matrix4x4> bindPoses, Transform toMatch)
    {
        int index = 0;
        foreach (var bone in bones)
        {
            if (bone == toMatch)
            {
                return index;
            }
            index++;
        }
        foreach (var bone in GetAllChildren(root.transform))
        {
            if (bone == toMatch)
            {
                bones.Add(bone);
                bindPoses.Add(bone.worldToLocalMatrix * root.transform.localToWorldMatrix);
                return bones.Count - 1;
            }
        }
        bones.Add(root.transform);
        bindPoses.Add(root.transform.localToWorldMatrix);
        return bones.Count - 1;
    }

    private static IEnumerable<AnimatorState> EnumerateAllStates(AnimatorController controller)
    {
        foreach (var layer in controller.layers)
        {
            var queue = new Queue<AnimatorStateMachine>();
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
    
    private static AnimationClip FixAnimationClipPaths(AnimationClip clip)
    {
        var newClip = Instantiate(clip);
        newClip.ClearCurves();
        newClip.name = clip.name;
        bool changed = false;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var currentPath = new AnimationPath(binding.path, binding.propertyName, binding.type);
            AnimationPath modifiedPath;
            var newBinding = binding;
            if (newAnimationPaths.TryGetValue(currentPath, out modifiedPath))
            {
                newBinding.path = modifiedPath.Item1;
                newBinding.propertyName = modifiedPath.Item2;
                newBinding.type = modifiedPath.Item3;
                changed = true;
            }
            AnimationUtility.SetEditorCurve(newClip, newBinding,
                AnimationUtility.GetEditorCurve(clip, binding));
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
        var avDescriptor = root.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
        var fxLayer = (AnimatorController)avDescriptor?.baseAnimationLayers[4].animatorController;
        if (avDescriptor == null || fxLayer == null)
            return;

        string path = AssetDatabase.GenerateUniqueAssetPath(trashBinPath + fxLayer.name + ".controller");
        AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(fxLayer), path);
        var newFxLayer = (AnimatorController)
            AssetDatabase.LoadAssetAtPath(path, typeof(AnimatorController));

        foreach (var state in EnumerateAllStates(newFxLayer))
        {
            var clip = state.motion as AnimationClip;
            var blendTree = state.motion as BlendTree;
            if (blendTree != null)
            {
                var childNodes = blendTree.children;
                for (int i = 0; i < childNodes.Length; i++)
                {
                    clip = childNodes[i].motion as AnimationClip;
                    if (clip != null)
                    {
                        childNodes[i].motion = FixAnimationClipPaths(clip);
                    }
                }
                blendTree.children = childNodes;
            }
            else if (clip != null)
            {
                state.motion = FixAnimationClipPaths(clip);
            }
        }

        avDescriptor.baseAnimationLayers[4].animatorController = newFxLayer;
        AssetDatabase.SaveAssets();
    }

    private static void CalculateUsedBlendShapePaths()
    {
        usedBlendShapes.Clear();
        var avDescriptor = root.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
        var fxLayer = (AnimatorController)avDescriptor?.baseAnimationLayers[4].animatorController;
        if (avDescriptor == null || fxLayer == null)
            return;
        foreach (var binding in fxLayer.animationClips.SelectMany(clip => AnimationUtility.GetCurveBindings(clip)))
        {
            if (binding.type != typeof(SkinnedMeshRenderer)
                || !binding.propertyName.StartsWith("blendShape."))
                continue;
            usedBlendShapes.Add(binding.path + "/" + binding.propertyName);
        }
        if (avDescriptor.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape
            && avDescriptor.VisemeSkinnedMesh != null)
        {
            string path = GetTransformPathToRoot(avDescriptor.VisemeSkinnedMesh.transform) + "/blendShape.";
            foreach (var blendShapeName in avDescriptor.VisemeBlendShapes)
            {
                usedBlendShapes.Add(path + blendShapeName);
            }
        }
        if (avDescriptor.customEyeLookSettings.eyelidType
            == VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.EyelidType.Blendshapes
            && avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh != null)
        {
            var meshRenderer = avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh;
            string path = GetTransformPathToRoot(meshRenderer.transform) + "/blendShape.";
            foreach (var blendShapeID in avDescriptor.customEyeLookSettings.eyelidsBlendshapes)
            {
                usedBlendShapes.Add(path + meshRenderer.sharedMesh.GetBlendShapeName(blendShapeID));
            }
        }
        var skinnedMeshRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var matchedSkinnedMeshes = new List<List<SkinnedMeshRenderer>>();
        foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
        {
            var mesh = skinnedMeshRenderer.sharedMesh;
            if (mesh == null)
                continue;
            string path = GetTransformPathToRoot(skinnedMeshRenderer.transform) + "/blendShape.";
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                if (skinnedMeshRenderer.GetBlendShapeWeight(i) != 0)
                {
                    usedBlendShapes.Add(path + mesh.GetBlendShapeName(i));
                }
            }
        }
    }

    private static void CombineSkinnedMeshes()
    {
        var combinableSkinnedMeshList = FindPossibleSkinnedMeshMerges();
        int combinedMeshID = 0;
        foreach (var combinableSkinnedMeshes in combinableSkinnedMeshList)
        {
            var targetBones = new List<Transform>();
            var targetUv = new List<Vector4>();
            var targetVertices = new List<Vector3>();
            var targetIndices = new List<List<int>>();
            var targetNormals = new List<Vector3>();
            var targetTangents = new List<Vector4>();
            var targetWeights = new List<BoneWeight>();
            var targetBindPoses = new List<Matrix4x4>();

            int meshID = 0;
            foreach (var skinnedMesh in combinableSkinnedMeshes)
            {
                Matrix4x4 toRoot = root.transform.worldToLocalMatrix
                    * skinnedMesh.transform.localToWorldMatrix;
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

                for (int vertIndex = 0; vertIndex < sourceVertices.Length; vertIndex++)
                {
                    targetUv.Add(new Vector4(sourceUv[vertIndex].x, sourceUv[vertIndex].y, meshID, 0));
                    targetVertices.Add(toRoot.MultiplyPoint3x4(sourceVertices[vertIndex]));
                    targetNormals.Add(toRoot.MultiplyVector(sourceNormals[vertIndex]).normalized);
                    var t = toRoot.MultiplyVector((Vector3)sourceTangents[vertIndex]);
                    targetTangents.Add(new Vector4(t.x, t.y, t.z, sourceTangents[vertIndex].w));
                    var boneWeight = sourceWeights[vertIndex];
                    int newIndex;
                    if (!bindPoseIDMap.TryGetValue(boneWeight.boneIndex0, out newIndex))
                    {
                        newIndex = GetNewBoneIDFromOldID(targetBones, targetBindPoses,
                            sourceBones[boneWeight.boneIndex0]);
                        bindPoseIDMap[boneWeight.boneIndex0] = newIndex;
                    }
                    boneWeight.boneIndex0 = newIndex;
                    if (!bindPoseIDMap.TryGetValue(boneWeight.boneIndex1, out newIndex))
                    {
                        newIndex = GetNewBoneIDFromOldID(targetBones, targetBindPoses,
                            sourceBones[boneWeight.boneIndex1]);
                        bindPoseIDMap[boneWeight.boneIndex1] = newIndex;
                    }
                    boneWeight.boneIndex1 = newIndex;
                    if (!bindPoseIDMap.TryGetValue(boneWeight.boneIndex2, out newIndex))
                    {
                        newIndex = GetNewBoneIDFromOldID(targetBones, targetBindPoses,
                            sourceBones[boneWeight.boneIndex2]);
                        bindPoseIDMap[boneWeight.boneIndex2] = newIndex;
                    }
                    boneWeight.boneIndex2 = newIndex;
                    if (!bindPoseIDMap.TryGetValue(boneWeight.boneIndex3, out newIndex))
                    {
                        newIndex = GetNewBoneIDFromOldID(targetBones, targetBindPoses,
                            sourceBones[boneWeight.boneIndex3]);
                        bindPoseIDMap[boneWeight.boneIndex3] = newIndex;
                    }
                    boneWeight.boneIndex3 = newIndex;
                    targetWeights.Add(boneWeight);
                }
                
                for (var matID = 0; matID < mesh.subMeshCount; matID++)
                {
                    uint startIndex = mesh.GetIndexStart(matID);
                    uint endIndex = mesh.GetIndexCount(matID) + startIndex;
                    var indices = new List<int>();
                    for (uint i = startIndex; i < endIndex; i++)
                    {
                        indices.Add(sourceIndices[i] + indexOffset);
                    }
                    targetIndices.Add(indices);
                }

                meshID++;
            }

            var combinedMesh = new Mesh();
            combinedMesh.indexFormat = targetVertices.Count >= 65536
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            combinedMesh.SetVertices(targetVertices);
            combinedMesh.bindposes = targetBindPoses.ToArray();
            combinedMesh.boneWeights = targetWeights.ToArray();
            combinedMesh.SetUVs(0, targetUv);
            combinedMesh.bounds = combinableSkinnedMeshes[0].sharedMesh.bounds;
            combinedMesh.SetNormals(targetNormals);
            combinedMesh.SetTangents(targetTangents);
            combinedMesh.subMeshCount = targetIndices.Count;
            for (int i = 0; i < targetIndices.Count; i++)
            {
                combinedMesh.SetIndices(targetIndices[i].ToArray(), MeshTopology.Triangles, i);
            }

            int vertexOffset = 0;
            foreach (var skinnedMesh in combinableSkinnedMeshes)
            {
                Matrix4x4 toRoot = root.transform.worldToLocalMatrix
                    * skinnedMesh.transform.localToWorldMatrix;
                var mesh = skinnedMesh.sharedMesh;
                var t = skinnedMesh.transform;
                string path = GetTransformPathToRoot(skinnedMesh.transform) + "/blendShape.";
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    var name = mesh.GetBlendShapeName(i);
                    if (!usedBlendShapes.Contains(path + name))
                        continue;
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
                            targetDeltaVertices[k + vertexOffset] =
                                toRoot.MultiplyVector(sourceDeltaVertices[k]);
                            targetDeltaNormals[k + vertexOffset] =
                                toRoot.MultiplyVector(sourceDeltaNormals[k]);
                            targetDeltaTangents[k + vertexOffset] =
                                toRoot.MultiplyVector(sourceDeltaTangents[k]);
                        }
                        var weight = mesh.GetBlendShapeFrameWeight(i, j);
                        combinedMesh.AddBlendShapeFrame(name, weight, targetDeltaVertices, targetDeltaNormals, targetDeltaTangents);
                    }
                }
                vertexOffset += mesh.vertexCount;
            }

            string newMeshName = "CombinedSkinnedMesh" + combinedMeshID;
            foreach (var skinnedMesh in combinableSkinnedMeshes)
            {
                if (skinnedMesh.name == "Body")
                {
                    newMeshName = "Body";
                    skinnedMesh.name = "WasFormerlyKnownAsBody";
                }
            }

            combinedMesh.name = newMeshName;
            CreateUniqueAsset(combinedMesh, combinedMesh.name + ".asset");
            AssetDatabase.SaveAssets();

            var combinedMeshRenderer = new GameObject();
            var meshRenderer = combinedMeshRenderer.AddComponent<SkinnedMeshRenderer>();
            meshRenderer.sharedMesh = combinedMesh;
            meshRenderer.sharedMaterials = combinableSkinnedMeshes.SelectMany(r => r.sharedMaterials).ToArray();
            meshRenderer.bones = targetBones.ToArray();

            var avDescriptor = root.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            
            meshID = 0;
            foreach (var skinnedMesh in combinableSkinnedMeshes)
            {
                if (meshRenderer.rootBone == null)
                {
                    meshRenderer.rootBone = skinnedMesh.rootBone;
                }
                if (meshRenderer.probeAnchor == null)
                {
                    meshRenderer.probeAnchor = skinnedMesh.probeAnchor;
                }
                string oldPath = GetTransformPathToRoot(skinnedMesh.transform);
                if (oldPath == "WasFormerlyKnownAsBody")
                {
                    oldPath = "Body";
                }
                if (avDescriptor != null)
                {
                    if (avDescriptor.VisemeSkinnedMesh == skinnedMesh)
                    {
                        avDescriptor.VisemeSkinnedMesh = meshRenderer;
                    }
                }
                for (int i = 0; i < skinnedMesh.sharedMesh.blendShapeCount; i++)
                {
                    var blendShapeName = skinnedMesh.sharedMesh.GetBlendShapeName(i);
                    var blendShapeWeight = skinnedMesh.GetBlendShapeWeight(i);
                    for (int j = 0; j < combinedMesh.blendShapeCount; j++)
                    {
                        if (blendShapeName == combinedMesh.GetBlendShapeName(j))
                        {
                            meshRenderer.SetBlendShapeWeight(j, blendShapeWeight);
                            break;
                        }
                    }
                    AddAnimationPathChange(oldPath, "blendShape." + blendShapeName, typeof(SkinnedMeshRenderer),
                        newMeshName, "blendShape." + blendShapeName, typeof(SkinnedMeshRenderer));
                }
                var properties = new MaterialPropertyBlock();
                if (meshRenderer.HasPropertyBlock())
                {
                    meshRenderer.GetPropertyBlock(properties);
                }
                properties.SetFloat("_IsActiveMesh" + meshID, skinnedMesh.gameObject.activeSelf ? 1f : 0f);
                meshRenderer.SetPropertyBlock(properties);
                AddAnimationPathChange(oldPath, "m_IsActive", typeof(GameObject),
                        newMeshName, "material._IsActiveMesh" + meshID, typeof(SkinnedMeshRenderer));
                DestroyImmediate(skinnedMesh.gameObject);
                meshID++;
            }

            combinedMeshRenderer.transform.SetParent(root.transform);
            combinedMeshRenderer.transform.localPosition = Vector3.zero;
            combinedMeshRenderer.transform.localRotation = Quaternion.identity;
            combinedMeshRenderer.transform.localScale = Vector3.one;
            combinedMeshRenderer.name = newMeshName;

            AssetDatabase.SaveAssets();

            combinedMeshID++;
        }
    }

    private static void Optimize(GameObject toOptimize)
    {
        root = toOptimize;
        ClearTrashBin();
        newAnimationPaths.Clear();
        CalculateUsedBlendShapePaths();
        CombineSkinnedMeshes();
        FixAllAnimationPaths();
    }
    
    public override void OnInspectorGUI()
    {
        var t = (d4rkAvatarOptimizer)target;

        t.MergeBackFaceCullingWithCullingOff =
            EditorGUILayout.Toggle("Merge Cull Back with Cull Off", t.MergeBackFaceCullingWithCullingOff);
        
        if (GUILayout.Button("Create Optimized Copy"))
        {
            var copy = GameObject.Instantiate(t.gameObject);
            Optimize(copy);
            DestroyImmediate(copy.GetComponent<d4rkAvatarOptimizer>());
            var boneCapsule = copy.GetComponentInChildren<BoneCapsule>(true);
            if (boneCapsule != null)
                DestroyImmediate(boneCapsule);
            copy.name = t.gameObject.name + "(OptimizedCopy)";
            copy.SetActive(true);
            t.gameObject.SetActive(false);
            Selection.objects = new Object[] { copy };
        }

        root = t.gameObject;
        var matchedSkinnedMeshes = FindPossibleSkinnedMeshMerges();

        foreach (var mergedMeshes in matchedSkinnedMeshes)
        {
            EditorGUILayout.Separator();
            EditorGUILayout.LabelField(mergedMeshes[0].name);
            foreach (var mesh in mergedMeshes)
            {
                foreach (var material in mesh.sharedMaterials)
                {
                    EditorGUILayout.LabelField("  " + mesh.name + "." + material.name);
                }
            }
        }
    }
}
#endif