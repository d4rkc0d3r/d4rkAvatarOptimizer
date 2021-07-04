﻿#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor.Animations;
using UnityEditor;
using d4rkpl4y3r;
using d4rkpl4y3r.Util;

using AnimationPath = System.ValueTuple<string, string, System.Type>;

[CustomEditor(typeof(d4rkAvatarOptimizer))]
public class d4rkAvatarOptimizerEditor : Editor
{
    private static GameObject root;
    private static d4rkAvatarOptimizer settings;
    private static string trashBinPath = "Assets/d4rkAvatarOptimizer/TrashBin/";
    private static HashSet<string> usedBlendShapes = new HashSet<string>();
    private static Dictionary<AnimationPath, AnimationPath> newAnimationPaths = new Dictionary<AnimationPath, AnimationPath>();
    private static List<Material> optimizedMaterials = new List<Material>();
    private static HashSet<(string path, int slot)> materialSlotsWithMaterialSwapAnimations = new HashSet<(string, int)>();
    private static Dictionary<Material, Material> optimizedMaterialSwapMaterial = new Dictionary<Material, Material>();

    private static void ClearTrashBin()
    {
        Profiler.StartSection("ClearTrashBin()");
        AssetDatabase.DeleteAsset("Assets/d4rkAvatarOptimizer/TrashBin");
        AssetDatabase.CreateFolder("Assets/d4rkAvatarOptimizer", "TrashBin");
        Profiler.EndSection();
    }

    private static void CreateUniqueAsset(Object asset, string name)
    {
        Profiler.StartSection("AssetDatabase.CreateAsset()");
        AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(trashBinPath + name));
        Profiler.EndSection();
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
        if (list[0].gameObject.layer != candidate.gameObject.layer)
            return false;
        return true;
    }

    private static void AddAnimationPathChange((string path, string name, System.Type type) source, (string path, string name, System.Type type) target)
    {
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
        foreach (var subList in matchedSkinnedMeshes)
        {
            if (subList.Count == 1)
                continue;
            int max = subList.Max(smr => smr.sharedMesh.blendShapeCount);
            int index = subList.FindIndex(smr => smr.sharedMesh.blendShapeCount == max);
            var oldFirst = subList[0];
            subList[0] = subList[index];
            subList[index] = oldFirst;
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
                bindPoses.Add(bone.worldToLocalMatrix);
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
            var currentPath = (binding.path, binding.propertyName, binding.type);
            var newBinding = binding;
            if (newAnimationPaths.TryGetValue(currentPath, out var modifiedPath))
            {
                newBinding.path = modifiedPath.Item1;
                newBinding.propertyName = modifiedPath.Item2;
                newBinding.type = modifiedPath.Item3;
                changed = true;
            }
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
                if (optimizedMaterialSwapMaterial.TryGetValue(oldMat, out var newMat))
                {
                    curve[i].value = newMat;
                    changed = true;
                }
            }
            AnimationUtility.SetObjectReferenceCurve(newClip, binding, curve);
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

        var optimizedAnims = new Dictionary<AnimationClip, AnimationClip>();
        foreach (var clip in fxLayer.animationClips.Distinct())
        {
            optimizedAnims[clip] = FixAnimationClipPaths(clip);
        }

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
                        childNodes[i].motion = optimizedAnims[clip];
                    }
                }
                blendTree.children = childNodes;
            }
            else if (clip != null)
            {
                state.motion = optimizedAnims[clip];
            }
        }

        avDescriptor.baseAnimationLayers[4].animatorController = newFxLayer;
        AssetDatabase.SaveAssets();
    }

    private static void OptimizeMaterialSwapMaterials()
    {
        materialSlotsWithMaterialSwapAnimations.Clear();
        var avDescriptor = root.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
        var fxLayer = (AnimatorController)avDescriptor?.baseAnimationLayers[4].animatorController;
        if (fxLayer == null)
            return;
        foreach (var clip in fxLayer.animationClips)
        {
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (binding.type != typeof(MeshRenderer)
                    || !binding.propertyName.StartsWith("m_Materials.Array.data["))
                    continue;
                int start = binding.propertyName.IndexOf('[') + 1;
                int end = binding.propertyName.IndexOf(']') - start;
                int slot = int.Parse(binding.propertyName.Substring(start, end));
                materialSlotsWithMaterialSwapAnimations.Add((binding.path, slot));
                var curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                foreach (var mat in curve.Select(c => c.value as Material).Where(m => m != null))
                {
                    if (!optimizedMaterialSwapMaterial.TryGetValue(mat, out var optimizedMaterial))
                    {
                        var matWrapper = new List<List<Material>>() { new List<Material>() { mat } };
                        optimizedMaterialSwapMaterial[mat] = CreateOptimizedMaterials(matWrapper, 0)[0];
                    }
                }
            }
        }
    }

    private static void CalculateUsedBlendShapePaths()
    {
        usedBlendShapes.Clear();
        materialSlotsWithMaterialSwapAnimations.Clear();
        var avDescriptor = root.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
        var fxLayer = (AnimatorController)avDescriptor?.baseAnimationLayers[4].animatorController;
        if (avDescriptor == null)
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

    private static Texture2DArray CombineTextures(List<Texture2D> textures)
    {
        Profiler.StartSection("CombineTextures()");
        var texArray = new Texture2DArray(textures[0].width, textures[0].height,
            textures.Count, textures[0].format, true);
        texArray.anisoLevel = textures[0].anisoLevel;
        texArray.wrapMode = textures[0].wrapMode;
        for (int i = 0; i < textures.Count; i++)
        {
            Graphics.CopyTexture(textures[i], 0, texArray, i);
        }
        Profiler.EndSection();
        CreateUniqueAsset(texArray, "texArray.asset");
        return texArray;
    }

    private static Material[] CreateOptimizedMaterials(List<List<Material>> sources, int meshToggleCount)
    {
        var materials = new Material[sources.Count];
        int matIndex = 0;
        foreach (var source in sources)
        {
            var parsedShader = ShaderAnalyzer.Parse(source[0].shader);
            var arrayPropertyValues = new Dictionary<string, (string type, List<string> values)>();
            var textureArrays = new Dictionary<string, List<Texture2D>>();
            foreach (var mat in source)
            {
                foreach (var prop in parsedShader.properties)
                {
                    if (!mat.HasProperty(prop.name))
                        continue;
                    if (prop.type == ParsedShader.Property.Type.Float)
                    {
                        (string type, List<string> values) propertyArray;
                        if (!arrayPropertyValues.TryGetValue(prop.name, out propertyArray))
                        {
                            propertyArray.type = "float";
                            propertyArray.values = new List<string>();
                            arrayPropertyValues[prop.name] = propertyArray;
                        }
                        propertyArray.values.Add("" + mat.GetFloat(prop.name));
                    }
                    if (prop.type == ParsedShader.Property.Type.Int)
                    {
                        (string type, List<string> values) propertyArray;
                        if (!arrayPropertyValues.TryGetValue(prop.name, out propertyArray))
                        {
                            propertyArray.type = "int";
                            propertyArray.values = new List<string>();
                            arrayPropertyValues[prop.name] = propertyArray;
                        }
                        propertyArray.values.Add("" + mat.GetInt(prop.name));
                    }
                    if (prop.type == ParsedShader.Property.Type.Color)
                    {
                        (string type, List<string> values) propertyArray;
                        if (!arrayPropertyValues.TryGetValue(prop.name, out propertyArray))
                        {
                            propertyArray.type = "float4";
                            propertyArray.values = new List<string>();
                            arrayPropertyValues[prop.name] = propertyArray;
                        }
                        propertyArray.values.Add(mat.GetColor(prop.name).ToString("F6").Replace("RGBA", "float4"));
                    }
                    if (prop.type == ParsedShader.Property.Type.Texture2D)
                    {
                        List<Texture2D> textureArray;
                        if (!textureArrays.TryGetValue(prop.name, out textureArray))
                        {
                            textureArray = new List<Texture2D>();
                            textureArrays[prop.name] = textureArray;
                            arrayPropertyValues["arrayIndex" + prop.name] = ("int", new List<string>());
                            arrayPropertyValues["shouldSample" + prop.name] = ("bool", new List<string>());
                        }
                        var tex = mat.GetTexture(prop.name);
                        var tex2D = tex as Texture2D;
                        int index = textureArray.IndexOf(tex2D);
                        if (index == -1 && tex != null)
                        {
                            index = textureArray.Count;
                            textureArray.Add(tex2D);
                        }
                        arrayPropertyValues["arrayIndex" + prop.name].values.Add("" + index);
                        arrayPropertyValues["shouldSample" + prop.name].values.Add((tex != null).ToString().ToLowerInvariant());
                    }
                }
            }

            string cullReplace = null;
            var cullProp = parsedShader.properties.FirstOrDefault(p => p.shaderLabParams.Count == 1 && p.shaderLabParams[0] == "Cull");
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
                if (tuple.Value.values.All(v => v == tuple.Value.values[0]))
                {
                    arrayPropertyValues.Remove(tuple.Key);
                    replace[tuple.Key] = tuple.Value.values[0];
                }
            }

            var texturesToMerge = new HashSet<string>(
                textureArrays.Where(a => a.Value.Count > 1).Select(a => a.Key));

            var texturesToCheckNull = new Dictionary<string, string>();
            foreach (var prop in parsedShader.properties)
            {
                if (prop.type == ParsedShader.Property.Type.Texture2D)
                {
                    if (arrayPropertyValues.ContainsKey("shouldSample" + prop.name))
                    {
                        texturesToCheckNull[prop.name] = "float4(1,1,1,1)";
                    }
                }
            }

            Profiler.StartSection("ShaderOptimizer.Run()");
            var optimizedShader = ShaderOptimizer.Run(parsedShader, replace, meshToggleCount,
                arrayPropertyValues, texturesToCheckNull, texturesToMerge);
            Profiler.EndSection();
            var name = System.IO.Path.GetFileName(source[0].shader.name);
            name = source[0].name + " " + name;
            var path = AssetDatabase.GenerateUniqueAssetPath(trashBinPath + name + ".shader");
            name = System.IO.Path.GetFileNameWithoutExtension(path);
            optimizedShader[0] = "Shader \"d4rkpl4y3r/Optimizer/" + name + "\"";
            System.IO.File.WriteAllLines(path, optimizedShader);
            var optimizedMaterial = Instantiate(source[0]);
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
            foreach (var texArray in textureArrays.Where(t => t.Value.Count > 1))
            {
                optimizedMaterial.SetTexture(texArray.Key, CombineTextures(texArray.Value));
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
            mat.shader = AssetDatabase.LoadAssetAtPath<Shader>(trashBinPath + mat.name + ".shader");
            mat.renderQueue = renderQueue;
            CreateUniqueAsset(mat, mat.name + ".mat");
        }
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
        return true;
    }

    private static bool CanCombineWith(List<Material> list, Material candidate)
    {
        if (list[0].shader != candidate.shader)
            return false;
        var parsedShader = ShaderAnalyzer.Parse(candidate.shader);
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
        foreach (var prop in parsedShader.properties)
        {
            foreach (var mat in list)
            {
                switch (prop.type)
                {
                    case ParsedShader.Property.Type.Color:
                    case ParsedShader.Property.Type.Float:
                        break;
                    case ParsedShader.Property.Type.Int:
                        if (prop.shaderLabParams.Any(s => s != "Cull" || !settings.MergeBackFaceCullingWithCullingOff)
                            && mat.GetInt(prop.name) != candidate.GetInt(prop.name))
                            return false;
                        break;
                    case ParsedShader.Property.Type.Texture2D:
                        {
                            var mTex = mat.GetTexture(prop.name);
                            var cTex = candidate.GetTexture(prop.name);
                            if (settings.MergeSameDimensionTextures && !CanCombineTextures(mTex, cTex))
                                return false;
                            if (!settings.MergeSameDimensionTextures && cTex != mTex)
                                return false;
                        }
                        break;
                    default:
                        return false;
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
            var mats = meshRenderer.sharedMaterials.Select(m => new List<Material>() { m }).ToList();
            meshRenderer.sharedMaterials = CreateOptimizedMaterials(mats, 0);
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

            var matchedMaterials = new List<List<Material>>();
            var matchedMaterialsIndex = new List<List<int>>();
            for (int i = 0; i < meshRenderer.sharedMaterials.Length; i++)
            {
                var material = meshRenderer.sharedMaterials[i];
                bool foundMatch = false;
                for (int j = 0; j < matchedMaterials.Count; j++)
                {
                    if (CanCombineWith(matchedMaterials[j], material))
                    {
                        matchedMaterials[j].Add(material);
                        matchedMaterialsIndex[j].Add(i);
                        foundMatch = true;
                        break;
                    }
                }
                if (!foundMatch)
                {
                    matchedMaterials.Add(new List<Material> { material });
                    matchedMaterialsIndex.Add(new List<int> { i });
                }
            }
            
            var uniqueMatchedMaterials = matchedMaterials.Select(mm => mm.Distinct().ToList()).ToList();

            var sourceVertices = mesh.vertices;
            var sourceIndices = mesh.triangles;
            var sourceUv = new List<Vector3>();
            mesh.GetUVs(0, sourceUv);
            var sourceNormals = mesh.normals;
            var sourceTangents = mesh.tangents;
            var sourceWeights = mesh.boneWeights;

            var targetUv = new List<Vector4>();
            var targetVertices = new List<Vector3>();
            var targetIndices = new List<List<int>>();
            var targetNormals = new List<Vector3>();
            var targetTangents = new List<Vector4>();
            var targetWeights = new List<BoneWeight>();

            var targetOldVertexIndex = new List<int>();

            for (int i = 0; i < matchedMaterials.Count; i++)
            {
                var indexList = new List<int>();
                for (int k = 0; k < matchedMaterials[i].Count; k++)
                {
                    var indexMap = new Dictionary<int, int>();
                    int internalMaterialID = uniqueMatchedMaterials[i].IndexOf(matchedMaterials[i][k]);
                    int materialSubMeshId = matchedMaterialsIndex[i][k];
                    int startIndex = (int)mesh.GetIndexStart(materialSubMeshId);
                    int endIndex = (int)mesh.GetIndexCount(materialSubMeshId) + startIndex;
                    for (int j = startIndex; j < endIndex; j++)
                    {
                        int oldIndex = sourceIndices[j];
                        int newIndex;
                        if (indexMap.TryGetValue(oldIndex, out newIndex))
                        {
                            indexList.Add(newIndex);
                        }
                        else
                        {
                            newIndex = targetVertices.Count;
                            indexList.Add(newIndex);
                            indexMap[oldIndex] = newIndex;
                            targetUv.Add(new Vector4(sourceUv[oldIndex].x, sourceUv[oldIndex].y, sourceUv[oldIndex].z, internalMaterialID));
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
                newMesh.SetUVs(0, targetUv);
                newMesh.bounds = mesh.bounds;
                newMesh.SetNormals(targetNormals);
                newMesh.SetTangents(targetTangents);
                newMesh.subMeshCount = matchedMaterials.Count;
                for (int i = 0; i < matchedMaterials.Count; i++)
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

            meshRenderer.sharedMaterials = CreateOptimizedMaterials(uniqueMatchedMaterials, meshCount > 1 ? meshCount : 0);
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
                Matrix4x4 toWorld = skinnedMesh.bones[0].localToWorldMatrix * skinnedMesh.sharedMesh.bindposes[0];
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

                sourceUv = sourceUv.Length != sourceVertices.Length ? new Vector2[sourceVertices.Length] : sourceUv;
                sourceNormals = sourceNormals.Length != sourceVertices.Length ? new Vector3[sourceVertices.Length] : sourceNormals;
                sourceTangents = sourceTangents.Length != sourceVertices.Length ? new Vector4[sourceVertices.Length] : sourceTangents;

                for (int vertIndex = 0; vertIndex < sourceVertices.Length; vertIndex++)
                {
                    targetUv.Add(new Vector4(sourceUv[vertIndex].x, sourceUv[vertIndex].y, meshID, 0));
                    targetVertices.Add(toWorld.MultiplyPoint3x4(sourceVertices[vertIndex]));
                    targetNormals.Add(toWorld.MultiplyVector(sourceNormals[vertIndex]).normalized);
                    var t = toWorld.MultiplyVector((Vector3)sourceTangents[vertIndex]);
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
                Matrix4x4 toWorld = skinnedMesh.bones[0].localToWorldMatrix * skinnedMesh.sharedMesh.bindposes[0];
                var mesh = skinnedMesh.sharedMesh;
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
                                toWorld.MultiplyVector(sourceDeltaVertices[k]);
                            targetDeltaNormals[k + vertexOffset] =
                                toWorld.MultiplyVector(sourceDeltaNormals[k]);
                            targetDeltaTangents[k + vertexOffset] =
                                toWorld.MultiplyVector(sourceDeltaTangents[k]);
                        }
                        var weight = mesh.GetBlendShapeFrameWeight(i, j);
                        combinedMesh.AddBlendShapeFrame(name, weight, targetDeltaVertices, targetDeltaNormals, targetDeltaTangents);
                    }
                }
                vertexOffset += mesh.vertexCount;
            }

            string newMeshName = combinableSkinnedMeshes[0].name;
            string newPath = GetTransformPathToRoot(combinableSkinnedMeshes[0].transform);

            combinedMesh.name = newMeshName;
            CreateUniqueAsset(combinedMesh, combinedMesh.name + ".asset");
            Profiler.StartSection("AssetDatabase.SaveAssets()");
            AssetDatabase.SaveAssets();
            Profiler.EndSection();
            
            var meshRenderer = combinableSkinnedMeshes[0];
            var materials = combinableSkinnedMeshes.SelectMany(r => r.sharedMaterials).ToArray();
            var avDescriptor = root.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            var blendShapeWeights = new Dictionary<string, float>();
            
            meshID = 0;
            foreach (var skinnedMesh in combinableSkinnedMeshes)
            {
                string oldPath = GetTransformPathToRoot(skinnedMesh.transform);
                if (combinableSkinnedMeshes.Count > 1)
                {
                    var properties = new MaterialPropertyBlock();
                    if (meshRenderer.HasPropertyBlock())
                    {
                        meshRenderer.GetPropertyBlock(properties);
                    }
                    properties.SetFloat("_IsActiveMesh" + meshID, skinnedMesh.gameObject.activeSelf ? 1f : 0f);
                    properties.SetInt("d4rkAvatarOptimizer_CombinedMeshCount", combinableSkinnedMeshes.Count);
                    meshRenderer.SetPropertyBlock(properties);
                    AddAnimationPathChange((oldPath, "m_IsActive", typeof(GameObject)),
                            (newPath, "material._IsActiveMesh" + meshID, typeof(SkinnedMeshRenderer)));
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
                    var name = skinnedMesh.sharedMesh.GetBlendShapeName(i);
                    var weight = skinnedMesh.GetBlendShapeWeight(i);
                    blendShapeWeights[name] = weight;
                    AddAnimationPathChange((oldPath, "blendShape." + name, typeof(SkinnedMeshRenderer)),
                        (newPath, "blendShape." + name, typeof(SkinnedMeshRenderer)));
                }
                if (meshID++ > 0)
                {
                    var obj = skinnedMesh.gameObject;
                    DestroyImmediate(skinnedMesh);
                    if (obj.transform.childCount == 0 || obj.GetComponents<Component>().Length == 0)
                        DestroyImmediate(obj);
                }
            }

            meshRenderer.sharedMesh = combinedMesh;
            meshRenderer.sharedMaterials = materials;
            meshRenderer.bones = targetBones.ToArray();

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

            Profiler.StartSection("AssetDatabase.SaveAssets()");
            AssetDatabase.SaveAssets();
            Profiler.EndSection();

            combinedMeshID++;
        }
    }

    private static void Optimize(GameObject toOptimize)
    {
        root = toOptimize;
        ShaderAnalyzer.ClearParsedShaderCache();
        ClearTrashBin();
        optimizedMaterials.Clear();
        newAnimationPaths.Clear();
        CalculateUsedBlendShapePaths();
        OptimizeMaterialSwapMaterials();
        CombineSkinnedMeshes();
        CombineAndOptimizeMaterials();
        OptimizeMaterialsOnNonSkinnedMeshes();
        SaveOptimizedMaterials();
        FixAllAnimationPaths();
    }
    
    public override void OnInspectorGUI()
    {
        settings = (d4rkAvatarOptimizer)target;

        settings.MergeBackFaceCullingWithCullingOff =
            EditorGUILayout.Toggle("Merge Cull Back with Cull Off", settings.MergeBackFaceCullingWithCullingOff);

        settings.MergeSameDimensionTextures =
            EditorGUILayout.Toggle("Merge Same Dimension Textures", settings.MergeSameDimensionTextures);

        settings.ProfileTimeUsed =
            EditorGUILayout.Toggle("Profile Time Used", settings.ProfileTimeUsed);

        if (GUILayout.Button("Create Optimized Copy"))
        {
            Profiler.enabled = settings.ProfileTimeUsed;
            Profiler.Reset();
            var copy = Instantiate(settings.gameObject);
            VRC.SDK3.Validation.AvatarValidation.RemoveIllegalComponents(copy);
            Optimize(copy);
            copy.name = settings.gameObject.name + "(OptimizedCopy)";
            copy.SetActive(true);
            settings.gameObject.SetActive(false);
            Selection.objects = new Object[] { copy };
            Profiler.PrintTimeUsed();
        }

        root = settings.gameObject;
        var matchedSkinnedMeshes = FindPossibleSkinnedMeshMerges();

        foreach (var mergedMeshes in matchedSkinnedMeshes)
        {
            EditorGUILayout.Separator();
            var matchedMaterials = new List<List<Material>>();
            var matchedMaterialMeshes = new List<List<Mesh>>();
            foreach (var meshMat in mergedMeshes.SelectMany(mesh =>
                mesh.sharedMaterials.Select(mat => (mesh.sharedMesh, mat))))
            {
                bool foundMatch = false;
                for (int i = 0; i < matchedMaterials.Count; i++)
                {
                    if (CanCombineWith(matchedMaterials[i], meshMat.mat))
                    {
                        matchedMaterials[i].Add(meshMat.mat);
                        matchedMaterialMeshes[i].Add(meshMat.sharedMesh);
                        foundMatch = true;
                        break;
                    }
                }
                if (!foundMatch)
                {
                    matchedMaterials.Add(new List<Material> { meshMat.mat });
                    matchedMaterialMeshes.Add(new List<Mesh> { meshMat.sharedMesh });
                }
            }
            for (int i = 0; i < matchedMaterials.Count; i++)
            {
                for (int j = 0; j < matchedMaterials[i].Count; j++)
                {
                    string indent = (i == 0  && j == 0 ? "" : "  ") + (j == 0 ? "" : "  ");
                    EditorGUILayout.LabelField(indent + matchedMaterialMeshes[i][j].name + "." + matchedMaterials[i][j].name);
                }
            }
        }
    }
}
#endif