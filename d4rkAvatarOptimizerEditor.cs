#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor.Animations;
using UnityEditor;

[CustomEditor(typeof(d4rkAvatarOptimizer))]
public class d4rkAvatarOptimizerEditor : Editor
{
    private static d4rkAvatarOptimizer t;

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

    private static List<List<SkinnedMeshRenderer>> FindPossibleSkinnedMeshMerges(GameObject root)
    {
        var skinnedMeshRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var matchedSkinnedMeshes = new List<List<SkinnedMeshRenderer>>();
        foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
        {
            if (skinnedMeshRenderer == null)
                continue;

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
    
    private static IEnumerable<Transform> GetAllChildren(Transform root)
    {
        var queue = new Queue<Transform>(root.Cast<Transform>());
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
    
    private static int GetNewBoneIDFromOldID(GameObject root, List<Transform> bones, List<Matrix4x4> bindPoses, Transform toMatch)
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

    private static AnimationClip FixAnimationPath(AnimationClip clip, string newPath, string newPropertyName, string oldPath, string oldPropertyName)
    {
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.path == oldPath && binding.propertyName == oldPropertyName)
            {
                var newBinding = binding;
                newBinding.path = newPath;
                newBinding.propertyName = newPropertyName;
                var newClip = Instantiate(clip);
                newClip.ClearCurves();
                foreach (var b2 in AnimationUtility.GetCurveBindings(clip))
                {
                    if (b2 == binding)
                        continue;
                    AnimationUtility.SetEditorCurve(newClip, b2,
                        AnimationUtility.GetEditorCurve(clip, b2));
                }
                AnimationUtility.SetEditorCurve(newClip, newBinding,
                    AnimationUtility.GetEditorCurve(clip, binding));
                newClip.name = clip.name;
                if (!clip.name.EndsWith("(OptimizedCopy)"))
                    newClip.name += "(OptimizedCopy)";
                return newClip;
            }
        }
        return clip;
    }

    private static void FixBlendShapeAnimationPaths(AnimatorController fxLayer, string newPath, string oldPath, string blendShapeName)
    {
        blendShapeName = "blendShape." + blendShapeName;
        foreach (var state in EnumerateAllStates(fxLayer))
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
                        childNodes[i].motion = FixAnimationPath(clip, newPath, blendShapeName, oldPath, blendShapeName);
                    }
                }
                blendTree.children = childNodes;
            }
            else if (clip != null)
            {
                state.motion = FixAnimationPath(clip, newPath, blendShapeName, oldPath, blendShapeName);
            }
        }
    }

    private static void FixToggleAnimationPaths(AnimatorController fxLayer, string newPath, string oldPath, int meshID)
    {
        var oldPropertyName = "m_IsActive";
        var newPropertyName = "material._IsActiveMesh" + meshID;
        foreach (var state in EnumerateAllStates(fxLayer))
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
                        childNodes[i].motion = FixAnimationPath(clip, newPath, newPropertyName, oldPath, oldPropertyName);
                    }
                }
                blendTree.children = childNodes;
            }
            else if (clip != null)
            {
                state.motion = FixAnimationPath(clip, newPath, newPropertyName, oldPath, oldPropertyName);
            }
        }
    }

    private static void CombineSkinnedMeshes(GameObject root)
    {
        var combinableSkinnedMeshList = FindPossibleSkinnedMeshMerges(root);
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
                        newIndex = GetNewBoneIDFromOldID(root, targetBones, targetBindPoses,
                            sourceBones[boneWeight.boneIndex0]);
                        bindPoseIDMap[boneWeight.boneIndex0] = newIndex;
                    }
                    boneWeight.boneIndex0 = newIndex;
                    if (!bindPoseIDMap.TryGetValue(boneWeight.boneIndex1, out newIndex))
                    {
                        newIndex = GetNewBoneIDFromOldID(root, targetBones, targetBindPoses,
                            sourceBones[boneWeight.boneIndex1]);
                        bindPoseIDMap[boneWeight.boneIndex1] = newIndex;
                    }
                    boneWeight.boneIndex1 = newIndex;
                    if (!bindPoseIDMap.TryGetValue(boneWeight.boneIndex2, out newIndex))
                    {
                        newIndex = GetNewBoneIDFromOldID(root, targetBones, targetBindPoses,
                            sourceBones[boneWeight.boneIndex2]);
                        bindPoseIDMap[boneWeight.boneIndex2] = newIndex;
                    }
                    boneWeight.boneIndex2 = newIndex;
                    if (!bindPoseIDMap.TryGetValue(boneWeight.boneIndex3, out newIndex))
                    {
                        newIndex = GetNewBoneIDFromOldID(root, targetBones, targetBindPoses,
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
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
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
                        var name = mesh.GetBlendShapeName(i);
                        var weight = mesh.GetBlendShapeFrameWeight(i, j);
                        combinedMesh.AddBlendShapeFrame(name, weight, targetDeltaVertices, targetDeltaNormals, targetDeltaTangents);
                    }
                }
                vertexOffset += mesh.vertexCount;
            }

            var combinedMeshRenderer = new GameObject();
            combinedMeshRenderer.name = "CombinedSkinnedMesh" + combinedMeshID;
            combinedMeshRenderer.transform.SetParent(root.transform);
            combinedMeshRenderer.transform.localPosition = Vector3.zero;
            combinedMeshRenderer.transform.localRotation = Quaternion.identity;
            combinedMeshRenderer.transform.localScale = Vector3.one;
            var meshRenderer = combinedMeshRenderer.AddComponent<SkinnedMeshRenderer>();
            meshRenderer.sharedMesh = combinedMesh;
            meshRenderer.sharedMaterials = combinableSkinnedMeshes.SelectMany(r => r.sharedMaterials).ToArray();
            meshRenderer.bones = targetBones.ToArray();

            var avDescriptor = root.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            var fxLayer = (AnimatorController)avDescriptor?.baseAnimationLayers[4].animatorController;
            var newFxLayer = Instantiate(fxLayer);
            if (avDescriptor != null && fxLayer != null)
            {
                string path = "Assets/d4rkAvatarOptimizer/" + fxLayer.name + "(OptimizedCopy).controller";
                AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(fxLayer), path);
                newFxLayer = (AnimatorController)
                    AssetDatabase.LoadAssetAtPath(path, typeof(AnimatorController));
            }

            meshID = 0;
            foreach (var skinnedMesh in combinableSkinnedMeshes)
            {
                var t = skinnedMesh.transform;
                string oldPath = t.name;
                while ((t = t.parent) != root.transform)
                {
                    oldPath = t.name + "/" + oldPath;
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
                    if (avDescriptor != null && fxLayer != null)
                    {
                        FixBlendShapeAnimationPaths(newFxLayer, combinedMeshRenderer.name, oldPath, blendShapeName);
                    }
                }
                var properties = new MaterialPropertyBlock();
                if (meshRenderer.HasPropertyBlock())
                {
                    meshRenderer.GetPropertyBlock(properties);
                }
                properties.SetFloat("_IsActiveMesh" + meshID, skinnedMesh.gameObject.activeSelf ? 1f : 0f);
                meshRenderer.SetPropertyBlock(properties);
                FixToggleAnimationPaths(newFxLayer, combinedMeshRenderer.name, oldPath, meshID);
                DestroyImmediate(skinnedMesh.gameObject);
                meshID++;
            }

            if (avDescriptor != null && fxLayer != null)
            {
                newFxLayer.name = fxLayer.name + "(OptimizedCopy)";
                avDescriptor.baseAnimationLayers[4].animatorController = newFxLayer;
                AssetDatabase.SaveAssets();
            }

            combinedMeshID++;
        }
    }

    private static void Optimize(GameObject root)
    {
        CombineSkinnedMeshes(root);
    }
    
    public override void OnInspectorGUI()
    {
        t = (d4rkAvatarOptimizer)target;

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

        var matchedSkinnedMeshes = FindPossibleSkinnedMeshMerges(t.gameObject);

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