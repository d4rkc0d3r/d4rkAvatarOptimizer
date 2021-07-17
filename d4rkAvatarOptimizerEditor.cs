#if UNITY_EDITOR
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
    private static string scriptPath = "Assets/d4rkAvatarOptimizer";
    private static string trashBinPath = "Assets/d4rkAvatarOptimizer/TrashBin/";
    private static HashSet<string> usedBlendShapes = new HashSet<string>();
    private static Dictionary<AnimationPath, AnimationPath> newAnimationPaths = new Dictionary<AnimationPath, AnimationPath>();
    private static List<Material> optimizedMaterials = new List<Material>();
    private static Dictionary<(string path, int slot), List<Material>> materialSlotsWithMaterialSwapAnimations = new Dictionary<(string, int), List<Material>>();
    private static Dictionary<Material, Material> optimizedMaterialSwapMaterial = new Dictionary<Material, Material>();
    private static Dictionary<string, HashSet<string>> usedMaterialProperties = new Dictionary<string, HashSet<string>>();
    private static List<List<Texture2D>> textureArrayLists = new List<List<Texture2D>>();
    private static List<Texture2DArray> textureArrays = new List<Texture2DArray>();
    private static Dictionary<Material, List<(string name, Texture2DArray array)>> texArrayPropertiesToSet = new Dictionary<Material, List<(string name, Texture2DArray array)>>();
 
    private static void ClearTrashBin()
    {
        Profiler.StartSection("ClearTrashBin()");
        trashBinPath = scriptPath + "/TrashBin/";
        AssetDatabase.DeleteAsset(scriptPath + "/TrashBin");
        AssetDatabase.CreateFolder(scriptPath, "TrashBin");
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
            if (!ShaderAnalyzer.Parse(material.shader).couldParse)
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
        var avDescriptor = root.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
        foreach (var subList in matchedSkinnedMeshes)
        {
            if (subList.Count == 1)
                continue;
            int index = subList.FindIndex(smr => smr == avDescriptor.VisemeSkinnedMesh);
            if (index == -1)
                continue;
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
        var fxLayer = avDescriptor?.baseAnimationLayers[4].animatorController as AnimatorController;
        if (fxLayer == null)
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
        var fxLayer = avDescriptor?.baseAnimationLayers[4].animatorController as AnimatorController;
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
                var index = (binding.path, slot);
                var curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                var materials = curve.Select(c => c.value as Material).Where(m => m != null).Distinct().ToList();
                if (!materialSlotsWithMaterialSwapAnimations.TryGetValue(index, out var oldMats))
                {
                    oldMats = new List<Material>();
                }
                materialSlotsWithMaterialSwapAnimations[index] = materials.Union(oldMats).Distinct().ToList();
                foreach (var mat in materials)
                {
                    if (!optimizedMaterialSwapMaterial.TryGetValue(mat, out var optimizedMaterial))
                    {
                        var matWrapper = new List<List<Material>>() { new List<Material>() { mat } };
                        optimizedMaterialSwapMaterial[mat] = CreateOptimizedMaterials(matWrapper, 0, binding.path)[0];
                    }
                }
            }
        }
    }

    private static void CalculateUsedBlendShapePaths()
    {
        usedBlendShapes.Clear();
        materialSlotsWithMaterialSwapAnimations.Clear();
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
        var avDescriptor = root.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
        if (avDescriptor == null)
            return;
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
        var fxLayer = avDescriptor?.baseAnimationLayers[4].animatorController as AnimatorController;
        if (fxLayer == null)
            return;
        foreach (var binding in fxLayer.animationClips.SelectMany(clip => AnimationUtility.GetCurveBindings(clip)))
        {
            if (binding.type != typeof(SkinnedMeshRenderer)
                || !binding.propertyName.StartsWith("blendShape."))
                continue;
            usedBlendShapes.Add(binding.path + "/" + binding.propertyName);
        }
    }

    private static void CalculateUsedMaterialProperties()
    {
        usedMaterialProperties.Clear();
        var avDescriptor = root.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
        var fxLayer = avDescriptor?.baseAnimationLayers[4].animatorController as AnimatorController;
        if (fxLayer == null)
            return;
        foreach (var binding in fxLayer.animationClips.SelectMany(clip => AnimationUtility.GetCurveBindings(clip)))
        {
            if (!binding.propertyName.StartsWith("material.") ||
                (binding.type != typeof(SkinnedMeshRenderer) && binding.type != typeof(MeshRenderer)))
                continue;
            if (!usedMaterialProperties.TryGetValue(binding.path, out var props))
            {
                usedMaterialProperties[binding.path] = (props = new HashSet<string>());
            }
            props.Add(binding.propertyName.Substring("material.".Length));
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
        CreateUniqueAsset(texArray, textures[0].width + "x" + textures[0].height + "_" + textures[0].format + "_2DArray.asset");
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
            var parsedShader = ShaderAnalyzer.Parse(source[0].shader);
            if (!parsedShader.couldParse)
            {
                continue;
            }
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
                    if (subList[0].texelSize == texArray[0].texelSize && subList[0].format == texArray[0].format)
                    {
                        list = subList;
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
        if (!usedMaterialProperties.TryGetValue(path, out var usedMaterialProps))
            usedMaterialProps = new HashSet<string>();
        var materials = new Material[sources.Count];
        int matIndex = 0;
        foreach (var source in sources)
        {
            var parsedShader = ShaderAnalyzer.Parse(source[0].shader);
            if (!parsedShader.couldParse)
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
                    if (prop.type == ParsedShader.Property.Type.Vector)
                    {
                        (string type, List<string> values) propertyArray;
                        if (!arrayPropertyValues.TryGetValue(prop.name, out propertyArray))
                        {
                            propertyArray.type = "float4";
                            propertyArray.values = new List<string>();
                            arrayPropertyValues[prop.name] = propertyArray;
                        }
                        propertyArray.values.Add("float4" + mat.GetVector(prop.name));
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
                if (usedMaterialProps.Contains(tuple.Key))
                {
                    arrayPropertyValues.Remove(tuple.Key);
                }
                else if (tuple.Value.values.All(v => v == tuple.Value.values[0]))
                {
                    arrayPropertyValues.Remove(tuple.Key);
                    replace[tuple.Key] = tuple.Value.values[0];
                }
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

            Profiler.StartSection("ShaderOptimizer.Run()");
            var optimizedShader = ShaderOptimizer.Run(parsedShader, replace, meshToggleCount,
                arrayPropertyValues, texturesToCheckNull, texturesToMerge);
            Profiler.EndSection();
            var name = System.IO.Path.GetFileName(source[0].shader.name);
            name = source[0].name + " " + name;
            var shaderFilePath = AssetDatabase.GenerateUniqueAssetPath(trashBinPath + name + ".shader");
            name = System.IO.Path.GetFileNameWithoutExtension(shaderFilePath);
            optimizedShader[0] = "Shader \"d4rkpl4y3r/Optimizer/" + name + "\"";
            System.IO.File.WriteAllLines(shaderFilePath, optimizedShader);
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
        if (parsedShader.couldParse == false)
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
        foreach (var prop in parsedShader.properties)
        {
            foreach (var mat in list)
            {
                switch (prop.type)
                {
                    case ParsedShader.Property.Type.Color:
                    case ParsedShader.Property.Type.Vector:
                        break;
                    case ParsedShader.Property.Type.Float:
                        if (prop.shaderLabParams.Any(s => s != "Cull" || !settings.MergeBackFaceCullingWithCullingOff)
                            && mat.GetFloat(prop.name) != candidate.GetFloat(prop.name))
                            return false;
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
            var path = GetTransformPathToRoot(meshRenderer.transform);
            var mats = meshRenderer.sharedMaterials.Select((material, index) => (material, index)).ToList();
            var alreadyOptimizedMaterials = new HashSet<Material>();
            foreach (var (material, index) in mats)
            {
                if (materialSlotsWithMaterialSwapAnimations.TryGetValue((path, index), out var matList))
                {
                    alreadyOptimizedMaterials.UnionWith(matList);
                }
            }
            var toOptimize = mats.Select(t => t.material).Where(m => !alreadyOptimizedMaterials.Contains(m)).Distinct().ToList();
            var optimizeMaterialWrapper = toOptimize.Select(m => new List<Material>() { m }).ToList();
            var optimizedMaterialsList = CreateOptimizedMaterials(optimizeMaterialWrapper, 0, GetTransformPathToRoot(meshRenderer.transform));
            var optimizedMaterials = toOptimize.Select((mat, index) => (mat, index))
                .ToDictionary(t => t.mat, t => optimizedMaterialsList[t.index]);
            var finalMaterials = new Material[meshRenderer.sharedMaterials.Length];
            foreach (var (material, index) in mats)
            {
                if (!optimizedMaterials.TryGetValue(material, out var optimized))
                {
                    if (!optimizedMaterialSwapMaterial.TryGetValue(material, out optimized))
                    {
                        optimized = material;
                    }
                }
                finalMaterials[index] = optimized;
            }
            meshRenderer.sharedMaterials = finalMaterials;
        }
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

            SearchForTextureArrayCreation(matchedMaterials.Select(mm => mm.Distinct().ToList()).ToList());
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

            meshRenderer.sharedMaterials = CreateOptimizedMaterials(uniqueMatchedMaterials, meshCount > 1 ? meshCount : 0, GetTransformPathToRoot(meshRenderer.transform));
        }
    }

    private static Vector3 ComponentMultiply(Vector3 vec, float x, float y, float z)
    {
        vec.x *= x;
        vec.y *= y;
        vec.z *= z;
        return vec;
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
            var sourceToWorld = new List<Matrix4x4>();
            var targetBounds = combinableSkinnedMeshes[0].localBounds;
            var toLocal = combinableSkinnedMeshes[0].rootBone.worldToLocalMatrix;

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
                var m = toLocal * skinnedMesh.rootBone.localToWorldMatrix;
                targetBounds.Encapsulate(m.MultiplyPoint3x4(ComponentMultiply(aabb.extents, 1, 1, 1) + aabb.center));
                targetBounds.Encapsulate(m.MultiplyPoint3x4(ComponentMultiply(aabb.extents, 1, 1, -1) + aabb.center));
                targetBounds.Encapsulate(m.MultiplyPoint3x4(ComponentMultiply(aabb.extents, 1, -1, 1) + aabb.center));
                targetBounds.Encapsulate(m.MultiplyPoint3x4(ComponentMultiply(aabb.extents, 1, -1, -1) + aabb.center));
                targetBounds.Encapsulate(m.MultiplyPoint3x4(ComponentMultiply(aabb.extents, -1, 1, 1) + aabb.center));
                targetBounds.Encapsulate(m.MultiplyPoint3x4(ComponentMultiply(aabb.extents, -1, 1, -1) + aabb.center));
                targetBounds.Encapsulate(m.MultiplyPoint3x4(ComponentMultiply(aabb.extents, -1, -1, 1) + aabb.center));
                targetBounds.Encapsulate(m.MultiplyPoint3x4(ComponentMultiply(aabb.extents, -1, -1, -1) + aabb.center));

                sourceUv = sourceUv.Length != sourceVertices.Length ? new Vector2[sourceVertices.Length] : sourceUv;
                sourceNormals = sourceNormals.Length != sourceVertices.Length ? new Vector3[sourceVertices.Length] : sourceNormals;
                sourceTangents = sourceTangents.Length != sourceVertices.Length ? new Vector4[sourceVertices.Length] : sourceTangents;

                for (int vertIndex = 0; vertIndex < sourceVertices.Length; vertIndex++)
                {
                    targetUv.Add(new Vector4(sourceUv[vertIndex].x, sourceUv[vertIndex].y, meshID, 0));
                    var boneWeight = sourceWeights[vertIndex];
                    Matrix4x4 toWorld = Matrix4x4.zero;
                    toWorld = AddWeighted(toWorld, toWorldArray[boneWeight.boneIndex0], boneWeight.weight0);
                    toWorld = AddWeighted(toWorld, toWorldArray[boneWeight.boneIndex1], boneWeight.weight1);
                    toWorld = AddWeighted(toWorld, toWorldArray[boneWeight.boneIndex2], boneWeight.weight2);
                    toWorld = AddWeighted(toWorld, toWorldArray[boneWeight.boneIndex3], boneWeight.weight3);
                    sourceToWorld.Add(toWorld);
                    targetVertices.Add(toWorld.MultiplyPoint3x4(sourceVertices[vertIndex]));
                    targetNormals.Add(toWorld.MultiplyVector(sourceNormals[vertIndex]).normalized);
                    var t = toWorld.MultiplyVector((Vector3)sourceTangents[vertIndex]);
                    targetTangents.Add(new Vector4(t.x, t.y, t.z, sourceTangents[vertIndex].w));
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
            Profiler.EndSection();

            string newMeshName = combinableSkinnedMeshes[0].name;
            string newPath = GetTransformPathToRoot(combinableSkinnedMeshes[0].transform);
            var blendShapeWeights = new Dictionary<string, float>();

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
            combinedMesh.name = newMeshName;
            for (int i = 0; i < targetIndices.Count; i++)
            {
                combinedMesh.SetIndices(targetIndices[i].ToArray(), MeshTopology.Triangles, i);
            }

            Profiler.StartSection("CopyCombinedMeshBlendShapes");
            int vertexOffset = 0;
            var usedBlendShapeNames = new HashSet<string>();
            foreach (var skinnedMesh in combinableSkinnedMeshes)
            {
                var mesh = skinnedMesh.sharedMesh;
                string path = GetTransformPathToRoot(skinnedMesh.transform) + "/blendShape.";
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    var oldName = mesh.GetBlendShapeName(i);
                    if (!usedBlendShapes.Contains(path + oldName))
                        continue;
                    var name = GenerateUniqueName(oldName, usedBlendShapeNames);
                    blendShapeWeights[name] = skinnedMesh.GetBlendShapeWeight(i);
                    AddAnimationPathChange(
                        (GetTransformPathToRoot(skinnedMesh.transform), "blendShape." + name, typeof(SkinnedMeshRenderer)),
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
            }
            Profiler.EndSection();

            CreateUniqueAsset(combinedMesh, combinedMesh.name + ".asset");
            Profiler.StartSection("AssetDatabase.SaveAssets()");
            AssetDatabase.SaveAssets();
            Profiler.EndSection();
            
            var meshRenderer = combinableSkinnedMeshes[0];
            var materials = combinableSkinnedMeshes.SelectMany(r => r.sharedMaterials).ToArray();
            var avDescriptor = root.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            
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
        texArrayPropertiesToSet.Clear();
        Profiler.StartSection("CalculateUsedBlendShapePaths()");
        CalculateUsedBlendShapePaths();
        Profiler.StartNextSection("CalculateUsedMaterialProperties()");
        CalculateUsedMaterialProperties();
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
        Profiler.StartNextSection("FixAllAnimationPaths()");
        FixAllAnimationPaths();
        Profiler.EndSection();
    }
    
    public override void OnInspectorGUI()
    {
        settings = (d4rkAvatarOptimizer)target;

        var path = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
        scriptPath = path.Substring(0, path.LastIndexOf('/'));

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
            DestroyImmediate(copy.GetComponent<d4rkAvatarOptimizer>());
            if (copy.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>() != null)
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