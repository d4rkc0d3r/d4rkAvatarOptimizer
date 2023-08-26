#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.Animations;
using d4rkpl4y3r.AvatarOptimizer;
using d4rkpl4y3r.AvatarOptimizer.Util;
using d4rkpl4y3r.AvatarOptimizer.Extensions;
using VRC.SDK3.Avatars.Components;
using VRC.Dynamics;
using VRC.SDKBase.Validation.Performance;

using Type = System.Type;
using MaterialSlot = d4rkAvatarOptimizer.MaterialSlot;
using Settings = d4rkAvatarOptimizer.Settings;

[CustomEditor(typeof(d4rkAvatarOptimizer))]
public class d4rkAvatarOptimizerEditor : Editor
{
    private static d4rkAvatarOptimizer optimizer;
    private static Material nullMaterial = null;
    
    public override void OnInspectorGUI()
    {
        optimizer = (d4rkAvatarOptimizer)target;
        OnSelectionChange();
        if (nullMaterial == null)
        {
            nullMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
            nullMaterial.name = "(null material slot)";
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        float currentViewWidth = GUILayoutUtility.GetLastRect().width + 23;

        var path = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
        EditorGUILayout.Space();
        EditorGUI.indentLevel++;
        EditorGUILayout.LabelField($"<size=20>d4rk{(currentViewWidth > 350 ? "pl4y3r's" : "")} Avatar Optimizer</size>", new GUIStyle(EditorStyles.label) { richText = true, alignment = TextAnchor.LowerCenter });
        var settingsRect = GUILayoutUtility.GetLastRect();
        EditorGUILayout.LabelField($"v{packageInfo.version}", EditorStyles.centeredGreyMiniLabel);
        EditorGUI.indentLevel--;
        settingsRect.width = 24;
        settingsRect.height = 24;
        bool pressedSettingsButton = GUI.Button(settingsRect, new GUIContent("", "Settings"));
        GUI.DrawTexture(settingsRect, EditorGUIUtility.IconContent("Settings@2x").image);
        if (pressedSettingsButton)
        {
            EditorWindow.GetWindow(typeof(AvatarOptimizerSettings));
        }

        var presets = optimizer.GetPresetNames();
        if (presets.Count > 0)
        {
            EditorGUILayout.BeginHorizontal(GUI.skin.box);
            EditorGUILayout.LabelField(GetLabelWithTooltip("Presets"), EditorStyles.boldLabel, GUILayout.Width(50));
            foreach (var preset in presets)
            {
                GUI.enabled = !optimizer.IsPresetActive(preset);
                if (GUILayout.Button(GetLabelWithTooltip(preset)))
                {
                    optimizer.SetPreset(preset);
                    ClearUICaches();
                    EditorUtility.SetDirty(optimizer);
                }
                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();
        }

        ToggleOptimizerProperty(nameof(optimizer.OptimizeOnUpload));
        if (d4rkAvatarOptimizer.HasCustomShaderSupport)
            ToggleOptimizerProperty(nameof(optimizer.WritePropertiesAsStaticValues));
        ToggleOptimizerProperty(nameof(optimizer.MergeSkinnedMeshes));
        EditorGUI.indentLevel++;
        if (d4rkAvatarOptimizer.HasCustomShaderSupport)
            ToggleOptimizerProperty(nameof(optimizer.MergeSkinnedMeshesWithShaderToggle));
        ToggleOptimizerProperty(nameof(optimizer.MergeStaticMeshesAsSkinned));
        ToggleOptimizerProperty(nameof(optimizer.ForceMergeBlendShapeMissMatch));
        EditorGUI.indentLevel--;
        if (d4rkAvatarOptimizer.HasCustomShaderSupport)
        {
            ToggleOptimizerProperty(nameof(optimizer.MergeDifferentPropertyMaterials));
            EditorGUI.indentLevel++;
            ToggleOptimizerProperty(nameof(optimizer.MergeSameDimensionTextures));
            ToggleOptimizerProperty(nameof(optimizer.MergeBackFaceCullingWithCullingOff));
            EditorGUI.indentLevel--;
        }
        ToggleOptimizerProperty(nameof(optimizer.OptimizeFXLayer));
        EditorGUI.indentLevel++;
        ToggleOptimizerProperty(nameof(optimizer.CombineApproximateMotionTimeAnimations));
        EditorGUI.indentLevel--;
        ToggleOptimizerProperty(nameof(optimizer.DisablePhysBonesWhenUnused));
        ToggleOptimizerProperty(nameof(optimizer.MergeSameRatioBlendShapes));
        ToggleOptimizerProperty(nameof(optimizer.KeepMMDBlendShapes));
        ToggleOptimizerProperty(nameof(optimizer.DeleteUnusedComponents));
        ToggleOptimizerProperty(nameof(optimizer.DeleteUnusedGameObjects));
        ToggleOptimizerProperty(nameof(optimizer.UseRingFingerAsFootCollider));

        if (optimizer.ExcludeTransforms == null)
            optimizer.ExcludeTransforms = new List<Transform>();
        if (Foldout($"Exclusions ({optimizer.ExcludeTransforms.Count})", ref optimizer.ShowExcludedTransforms))
        {
            EditorGUI.indentLevel++;
            DynamicTransformList(ref optimizer.ExcludeTransforms);
            EditorGUI.indentLevel--;
        }

        Profiler.enabled = optimizer.ProfileTimeUsed;
        Profiler.Reset();

        Profiler.StartSection("Validate");
        GUI.enabled = Validate();
        Profiler.EndSection();

        if (GUILayout.Button("<size=18>Create Optimized Copy</size>", new GUIStyle(GUI.skin.button) { richText = true }))
        {
            Profiler.enabled = optimizer.ProfileTimeUsed;
            Profiler.Reset();
            Profiler.StartSection("Assign New Avatar ID");
            AssignNewAvatarIDIfEmpty();
            Profiler.StartNextSection("Instantiate(optimizer.gameObject)");
            var copy = Instantiate(optimizer.gameObject);
            Profiler.StartNextSection("Move Copy to Scene");
            SceneManager.MoveGameObjectToScene(copy, optimizer.gameObject.scene);
            Profiler.StartNextSection("Optimize Copy");
            copy.name = optimizer.gameObject.name + "(BrokenCopy)";
            copy.GetComponent<d4rkAvatarOptimizer>().Optimize();
            copy.name = optimizer.gameObject.name + "(OptimizedCopy)";
            Profiler.StartNextSection("Select Copy");
            copy.SetActive(true);
            optimizer.gameObject.SetActive(false);
            Selection.objects = new Object[] { copy };
            Profiler.EndSection();
            Profiler.PrintTimeUsed();
            Profiler.Reset();
            return;
        }

        EditorGUILayout.Separator();
        GUI.enabled = true;

        Profiler.StartSection("Show Perf Rank Change");
        var exclusions = optimizer.GetAllExcludedTransforms();
        var particleSystemCount = optimizer.GetNonEditorOnlyComponentsInChildren<ParticleSystem>().Count;
        var skinnedMeshes = optimizer.GetNonEditorOnlyComponentsInChildren<SkinnedMeshRenderer>();
        int meshCount = optimizer.GetNonEditorOnlyComponentsInChildren<MeshRenderer>().Count;
        int totalMaterialCount = optimizer.GetNonEditorOnlyComponentsInChildren<Renderer>()
            .Sum(r => r.GetSharedMesh() == null ? 0 : r.GetSharedMesh().subMeshCount) + particleSystemCount;
        var totalBlendShapePaths = new HashSet<string>(skinnedMeshes.SelectMany(r => {
            if (r.sharedMesh == null)
                return new string[0];
            return Enumerable.Range(0, r.sharedMesh.blendShapeCount)
                .Select(i => $"{optimizer.GetPathToRoot(r.transform)}/blendShape.{r.sharedMesh.GetBlendShapeName(i)}");
        }));
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
        PerfRankChangeLabel("Skinned Mesh Renderers", skinnedMeshes.Count, optimizedSkinnedMeshCount, PerformanceCategory.SkinnedMeshCount);
        PerfRankChangeLabel("Mesh Renderers", meshCount, optimizedMeshCount, PerformanceCategory.MeshCount);
        PerfRankChangeLabel("Material Slots", totalMaterialCount, optimizedTotalMaterialCount, PerformanceCategory.MaterialCount);
        if (optimizer.GetFXLayer() != null)
        {
            var nonErrors = new HashSet<string>() {"toggle", "motion time"};
            var mergedLayerCount = optimizer.OptimizeFXLayer ? optimizer.AnalyzeFXLayerMergeAbility().Count(list => list.All(e => nonErrors.Contains(e))) : 0;
            var layerCount = optimizer.GetFXLayer().layers.Length;
            var optimizedLayerCount = mergedLayerCount > 1 ? layerCount - mergedLayerCount + 1 : layerCount;
            if (optimizer.OptimizeFXLayer)
                optimizedLayerCount -= optimizer.FindUselessFXLayers().Count;
            PerfRankChangeLabel("FX Layers", layerCount, optimizedLayerCount, PerformanceCategory.FXLayerCount);
        }
        PerfRankChangeLabel("Blend Shapes", totalBlendShapePaths.Count, KeptBlendShapePaths.Count, PerformanceCategory.BlendShapeCount);
        Profiler.EndSection();

        EditorGUILayout.Separator();

        if (Foldout("Show Mesh & Material Merge Preview", ref optimizer.ShowMeshAndMaterialMergePreview))
        {
            Profiler.StartSection("Show Merge Preview");
            foreach (var matched in MergedMaterialPreview)
            {
                for (int i = 0; i < matched.Count; i++)
                {
                    for (int j = 0; j < matched[i].Count; j++)
                    {
                        int indent = j == 0 ? 0 : 1;
                        DrawMatchedMaterialSlot(matched[i][j], indent);
                    }
                }
                EditorGUILayout.Space(8);
            }
            Profiler.EndSection();
        }

        EditorGUILayout.Separator();

        if (optimizer.OptimizeFXLayer && optimizer.GetFXLayer() != null)
        {
            if (Foldout("Show FX Layer Merge Result", ref optimizer.ShowFXLayerMergeResults))
            {
                Profiler.StartSection("Show FX Layer Merge Errors");
                ToggleOptimizerProperty(nameof(optimizer.ShowFXLayerMergeErrors));
                var errorMessages = optimizer.AnalyzeFXLayerMergeAbility();
                var uselessLayers = optimizer.FindUselessFXLayers();
                var fxLayer = optimizer.GetFXLayer();
                var nonErrors = new HashSet<string>() {"toggle", "motion time", "useless"};
                for (int i = 0; i < errorMessages.Count; i++)
                {
                    var perfRating = PerformanceRating.VeryPoor;
                    if (errorMessages[i].Count == 1 && errorMessages[i][0] == "toggle")
                        perfRating = PerformanceRating.Good;
                    else if (errorMessages[i].Count == 1 && errorMessages[i][0] == "motion time")
                        perfRating = PerformanceRating.Medium;
                    if (uselessLayers.Contains(i))
                        perfRating = PerformanceRating.Excellent;
                    
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(new GUIContent(GetPerformanceIconForRating(perfRating)), GUILayout.Width(15));
                    EditorGUILayout.LabelField(new GUIContent($"{i}{fxLayer.layers[i].name}", string.Join("\n", errorMessages[i])));
                    EditorGUILayout.EndHorizontal();
                    if (optimizer.ShowFXLayerMergeErrors)
                    {
                        EditorGUI.indentLevel+=2;
                        foreach (var error in errorMessages[i].Where(e => !nonErrors.Contains(e)))
                        {
                            EditorGUILayout.LabelField(error);
                        }
                        EditorGUI.indentLevel-=2;
                    }
                }
                Profiler.EndSection();
            }
        }

        EditorGUILayout.Separator();

        if (Foldout("Debug Info", ref optimizer.ShowDebugInfo))
        {
            ToggleOptimizerProperty(nameof(optimizer.ProfileTimeUsed));
            EditorGUI.indentLevel++;
            if (Foldout("Unparsable Materials", ref optimizer.DebugShowUnparsableMaterials))
            {
                Profiler.StartSection("Unparsable Materials");
                var list = optimizer.GetUsedComponentsInChildren<Renderer>()
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
            if (Foldout("Unmergable Materials", ref optimizer.DebugShowUnmergableMaterials))
            {
                Profiler.StartSection("Unmergable Materials");
                var list = optimizer.GetUsedComponentsInChildren<Renderer>()
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
            if (Foldout("Unmergable Texture Materials", ref optimizer.DebugShowUnmergableTextureMaterials))
            {
                Profiler.StartSection("Unmergable Texture Materials");
                var list = optimizer.GetUsedComponentsInChildren<Renderer>()
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
            if (Foldout("Crunched Textures", ref optimizer.DebugShowCrunchedTextures))
            {
                Profiler.StartSection("Crunched Textures");
                DrawDebugList(CrunchedTextures);
                Profiler.EndSection();
            }
            if (Foldout("NonBC5 Normal Maps", ref optimizer.DebugShowNonBC5NormalMaps))
            {
                Profiler.StartSection("NonBC5 Normal Maps");
                DrawDebugList(NonBC5NormalMaps);
                Profiler.EndSection();
            }
            if (optimizer.WritePropertiesAsStaticValues && Foldout("Locked in Materials", ref optimizer.DebugShowLockedInMaterials))
            {
                Profiler.StartSection("Locked in Materials");
                var list = optimizer.GetUsedComponentsInChildren<Renderer>()
                    .SelectMany(r => r.sharedMaterials).Distinct()
                    .Where(mat => IsLockedIn(mat)).ToArray();
                DrawDebugList(list);
                Profiler.EndSection();
            }
            if (!optimizer.WritePropertiesAsStaticValues && Foldout("Unlocked Materials", ref optimizer.DebugShowUnlockedMaterials))
            {
                Profiler.StartSection("Unlocked Materials");
                var list = optimizer.GetUsedComponentsInChildren<Renderer>()
                    .SelectMany(r => r.sharedMaterials).Distinct()
                    .Where(mat => CanLockIn(mat) && !IsLockedIn(mat)).ToArray();
                DrawDebugList(list);
                Profiler.EndSection();
            }
            if (optimizer.FindAllPenetrators().Count > 0 && Foldout("Penetrators", ref optimizer.DebugShowPenetrators))
            {
                Profiler.StartSection("Penetrators");
                DrawDebugList(optimizer.FindAllPenetrators().ToArray());
                Profiler.EndSection();
            }
            if (Foldout("Phys Bone Dependencies", ref optimizer.DebugShowPhysBoneDependencies))
            {
                Profiler.StartSection("Phys Bone Dependencies");
                foreach (var pair in optimizer.FindAllPhysBoneDependencies())
                {
                    if (pair.Key.gameObject.CompareTag("EditorOnly"))
                        continue;
                    EditorGUILayout.ObjectField(pair.Key, typeof(VRCPhysBoneBase), true);
                    EditorGUI.indentLevel++;
                    DrawDebugList(pair.Value.ToArray());
                    EditorGUI.indentLevel--;
                }
            }
            if (Foldout("Unused Components", ref optimizer.DebugShowUnusedComponents))
            {
                Profiler.StartSection("Unused Components");
                DrawDebugList(optimizer.FindAllUnusedComponents().ToArray());
                Profiler.EndSection();
            }
            if (Foldout("Always Disabled Game Objects", ref optimizer.DebugShowAlwaysDisabledGameObjects))
            {
                Profiler.StartSection("Always Disabled Game Objects");
                DrawDebugList(optimizer.FindAllAlwaysDisabledGameObjects().ToArray());
                Profiler.EndSection();
            }
            if (Foldout("Material Swaps", ref optimizer.DebugShowMaterialSwaps))
            {
                Profiler.StartSection("Material Swaps");
                var map = optimizer.FindAllMaterialSwapMaterials();
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
            if (Foldout("Animated Material Property Paths", ref optimizer.DebugShowAnimatedMaterialPropertyPaths))
            {
                Profiler.StartSection("Animated Material Property Paths");
                DrawDebugList(AnimatedMaterialPropertyPaths);
                Profiler.EndSection();
            }
            if (Foldout("Game Objects with Toggle Animation", ref optimizer.DebugShowGameObjectsWithToggle))
            {
                Profiler.StartSection("Game Objects with Toggle Animation");
                DrawDebugList(GameObjectsWithToggleAnimations);
                Profiler.EndSection();
            }
            if (Foldout("Unmoving Bones", ref optimizer.DebugShowUnmovingBones))
            {
                Profiler.StartSection("Unmoving Bones");
                DrawDebugList(UnmovingBones);
                Profiler.EndSection();
            }
            EditorGUI.indentLevel--;
        }
        if (optimizer.ProfileTimeUsed)
        {
            EditorGUILayout.Separator();
            var timeUsed = Profiler.FormatTimeUsed().Take(6).ToArray();
            foreach (var time in timeUsed)
            {
                EditorGUILayout.LabelField(time);
            }
        }
    }
    
    private bool Validate()
    {
        var avDescriptor = optimizer.GetComponent<VRCAvatarDescriptor>();

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

        if (optimizer.name.EndsWith("(OptimizedCopy)"))
        {
            EditorGUILayout.HelpBox("Put the optimizer on the original avatar, not the optimized copy.", MessageType.Error);
            return false;
        }

        #if !HAS_IEDITOR_ONLY
        EditorGUILayout.HelpBox("Your VRChat Avatar SDK is outdated.\n" +
            "The version you are using does not support the \"Optimize on Upload\" feature.\n" +
            "Please update your SDK to the latest version.", MessageType.Error);
        #endif

        if (optimizer.UseRingFingerAsFootCollider)
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
            if (optimizer.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r => r != meshRenderer))
            {
                EditorGUILayout.HelpBox("Viseme SkinnedMeshRenderer is not a child of the avatar root.", MessageType.Error);
            }
        }

        if (avDescriptor.customEyeLookSettings.eyelidType == VRCAvatarDescriptor.EyelidType.Blendshapes
            && avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh != null)
        {
            var meshRenderer = avDescriptor.customEyeLookSettings.eyelidsSkinnedMesh;
            if (optimizer.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r => r != meshRenderer))
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

        if (Object.FindObjectsOfType<VRCAvatarDescriptor>().Any(av => av != null && av.name.EndsWith("(BrokenCopy)")))
        {
            EditorGUILayout.HelpBox(
                "Seems like the last optimization attempt failed.\n" +
                "You can try to delete the broken copy and try again with different settings or adding parts to the exclusion list.\n" +
                "Click this message to find or create a bug report on github.", MessageType.Error);
            if (Event.current.type == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                Application.OpenURL("https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/issues");
        }

        var exclusions = optimizer.GetAllExcludedTransforms();

        var animatorsExcludingRoot = optimizer.GetComponentsInChildren<Animator>(true)
            .Where(a => a.gameObject != optimizer.gameObject)
            .Where(a => !exclusions.Contains(a.transform))
            .Where(a => a.runtimeAnimatorController != null)
            .ToArray();

        if (animatorsExcludingRoot.Length > 0)
        {
            EditorGUILayout.HelpBox(
                "Some animators exist that are not on the root object.\n" +
                "The optimizer only supports animators in the custom playable layers in the avatar descriptor.\n" +
                "If the optimized copy is broken, try to add the animators to the exclusion list.", MessageType.Warning);
            if (GUILayout.Button("Auto add extra animators to exclusion list"))
            {
                foreach (var animator in animatorsExcludingRoot)
                {
                    optimizer.ExcludeTransforms.Add(animator.transform);
                }
                optimizer.ShowExcludedTransforms = true;
                ClearUICaches();
            }
        }

        if (optimizer.DeleteUnusedGameObjects && optimizer.UsesAnyLayerMasks())
        {
            EditorGUILayout.HelpBox(
                "Animator layer masks are not supported when deleting unused game objects.\n" +
                "If the optimized copy is broken, try to disable the option.", MessageType.Warning);
        }

        if (optimizer.WritePropertiesAsStaticValues)
        {
            var allMaterials = optimizer.GetUsedComponentsInChildren<Renderer>()
                .Where(r => !exclusions.Contains(r.transform))
                .SelectMany(r => r.sharedMaterials).Distinct().ToArray();

            var correctlyParsedMaterials = allMaterials
                .Select(m => ShaderAnalyzer.Parse(m?.shader))
                .Where(p => (p?.parsedCorrectly ?? false)).ToArray();

            if (correctlyParsedMaterials.Length != allMaterials.Length)
            {
                EditorGUILayout.HelpBox(
                    "Some materials could not be parsed.\n" +
                    "Check the Debug Info foldout for more info.", MessageType.Warning);
            }

            if (optimizer.MergeDifferentPropertyMaterials && correctlyParsedMaterials.Any(p => !p.CanMerge()))
            {
                EditorGUILayout.HelpBox(
                    "Some materials do not support merging.\n" +
                    "Swapping their shaders to compatible ones might help reduce material count further.\n" + 
                    "Check the Debug Info foldout for more info.", MessageType.Info);
            }

            if (optimizer.MergeSameDimensionTextures && correctlyParsedMaterials.Any(p => p.CanMerge() && !p.CanMergeTextures()))
            {
                EditorGUILayout.HelpBox(
                    "Some materials do not support merging textures.\n" +
                    "Swapping their shaders to compatible ones might help reduce material count further.\n" + 
                    "Check the Debug Info foldout for more info.", MessageType.Info);
            }

            if (optimizer.MergeDifferentPropertyMaterials && allMaterials.Any(m => IsLockedIn(m)))
            {
                EditorGUILayout.HelpBox(
                    "Some materials are locked in.\n" +
                    "Write Properties as Static Values will do effectively the same as locking in while also having more potential to reduce material count.\n" +
                    "If you use \"Rename Animated\" on some locked in shaders keep them locked as the animations will break otherwise.\n" + 
                    "Check the Debug Info foldout for a full list.", MessageType.Info);
            }

            if (optimizer.MergeSameDimensionTextures && CrunchedTextures.Length > 1)
            {
                EditorGUILayout.HelpBox(
                    "Some textures are crunch compressed.\n" +
                    "Crunch compressed textures cannot be merged.\n" +
                    "Check the Debug Info foldout for a full list.", MessageType.Info);
            }
        }

        if (NonBC5NormalMaps.Length > 0)
        {
            EditorGUILayout.HelpBox(
                "Some normal maps are not BC5 compressed.\n" +
                "BC5 compressed normal maps are highest quality for the same VRAM size as the other compression options.\n" +
                "Check the Debug Info foldout for a full list or click the button to automatically change them all to BC5.", MessageType.Info);
            if (GUILayout.Button($"Convert all ({NonBC5NormalMaps.Length}) normal maps to BC5"))
            {
                foreach (var tex in NonBC5NormalMaps)
                {
                    var path = AssetDatabase.GetAssetPath(tex);
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    var platformSettings = importer.GetPlatformTextureSettings("Standalone");
                    platformSettings.resizeAlgorithm = TextureResizeAlgorithm.Bilinear;
                    platformSettings.overridden = true;
                    platformSettings.format = TextureImporterFormat.BC5;
                    platformSettings.maxTextureSize = Mathf.Max(tex.width, tex.height);
                    importer.SetPlatformTextureSettings(platformSettings);
                    importer.SaveAndReimport();
                }
                ClearUICaches();
            }
        }

        bool hasExtraMaterialSlots = optimizer.GetNonEditorOnlyComponentsInChildren<Renderer>()
            .Where(r => !exclusions.Contains(r.transform))
            .Where(r => r.GetSharedMesh() != null)
            .Any(r => r.sharedMaterials.Length > r.GetSharedMesh().subMeshCount);

        if (hasExtraMaterialSlots)
        {
            EditorGUILayout.HelpBox(
                "Some renderers have more material slots than sub meshes.\n" + 
                "Those extra materials & polys are not counted by VRChats performance system. " + 
                "After optimizing those extra slots and polys will get baked as real ones.\n" + 
                "You should expect your poly count to increase, this is working as intended!", MessageType.Info);
        }

        bool hasVRCFuryComponent = optimizer.GetComponents<Component>().Any(c => c.GetType().Name == "VRCFury");
        if (hasVRCFuryComponent)
        {
            EditorGUILayout.HelpBox(
                "VRCFury is used on the avatar. This means the perf rank change and merge result previews can be inaccurate as the optimizer does not take VRCFury into account for those.\n" +
                "To test in editor built a VRCFury test avatar and use the optimizer on that.\n" +
                "For uploading use the Optimize on Upload feature as that ensures fury and the optimizer get used in the correct order.", MessageType.Warning);
            return false;
        }

        #if MODULAR_AVATAR_EXISTS
        if (optimizer.GetComponentsInChildren<nadena.dev.modular_avatar.core.AvatarTagComponent>(true).Any())
        {
            EditorGUILayout.HelpBox(
                "Modular Avatar is used on the avatar. This means the perf rank change and merge result previews " + 
                "can be inaccurate as the optimizer does not take Modular Avatar into account for those.\n" +
                "To test in editor use \"Manual bake avatar\" before clicking the optimize button.\n" +
                "For uploading use the Optimize on Upload feature as that ensures Modular Avatar and the optimizer get used in the correct order.", MessageType.Warning);
            return false;
        }
        #endif

        return true;
    }

    private static void AssignNewAvatarIDIfEmpty()
    {
        var avDescriptor = optimizer.GetComponent<VRCAvatarDescriptor>();
        if (avDescriptor == null)
            return;
        var pm = optimizer.GetComponent<VRC.Core.PipelineManager>();
        if (pm == null)
        {
            pm = optimizer.gameObject.AddComponent<VRC.Core.PipelineManager>();
        }
        if (!string.IsNullOrEmpty(pm.blueprintId))
            return;
        pm.AssignId();
    }

    private d4rkAvatarOptimizer lastSelected = null;
    private List<List<List<MaterialSlot>>> mergedMaterialPreviewCache = null;
    private Transform[] unmovingBonesCache = null;
    private GameObject[] gameObjectsWithToggleAnimationsCache = null;
    private Texture2D[] crunchedTexturesCache = null;
    private Texture2D[] nonBC5NormalMapsCache = null;
    private string[] animatedMaterialPropertyPathsCache = null;
    private HashSet<string> keptBlendShapePathsCache = null;

    private void ClearUICaches()
    {
        mergedMaterialPreviewCache = null;
        unmovingBonesCache = null;
        gameObjectsWithToggleAnimationsCache = null;
        crunchedTexturesCache = null;
        nonBC5NormalMapsCache = null;
        animatedMaterialPropertyPathsCache = null;
        keptBlendShapePathsCache = null;
        optimizer.ClearCaches();
    }

    private void OnSelectionChange()
    {
        if (lastSelected == optimizer)
            return;
        lastSelected = optimizer;
        ClearUICaches();
        if (optimizer.DoAutoSettings)
        {
            AvatarOptimizerSettings.ApplyDefaults(optimizer);
            optimizer.ApplyAutoSettings();
        }
        ShaderAnalyzer.ParseAndCacheAllShaders(optimizer.FindAllUsedMaterials().Select(m => m.shader), false);
    }

    private List<List<List<MaterialSlot>>> MergedMaterialPreview
    {
        get
        {
            if (mergedMaterialPreviewCache == null)
            {
                mergedMaterialPreviewCache = new List<List<List<MaterialSlot>>>();
                optimizer.CalculateUsedBlendShapePaths();
                var matchedSkinnedMeshes = optimizer.FindPossibleSkinnedMeshMerges();
                foreach (var mergedMeshes in matchedSkinnedMeshes)
                {
                    var matched = optimizer.FindAllMergeAbleMaterials(mergedMeshes);
                    mergedMaterialPreviewCache.Add(matched);
                }
            }
            return mergedMaterialPreviewCache;
        }
    }

    private HashSet<string> KeptBlendShapePaths
    {
        get
        {
            if (keptBlendShapePathsCache == null)
            {
                optimizer.CalculateUsedBlendShapePaths();
                var skinnedMeshes = optimizer.GetUsedComponentsInChildren<SkinnedMeshRenderer>();
                keptBlendShapePathsCache = new HashSet<string>(skinnedMeshes.SelectMany(r => {
                    if (r.sharedMesh == null)
                        return new string[0];
                    return Enumerable.Range(0, r.sharedMesh.blendShapeCount)
                        .Select(i => $"{optimizer.GetPathToRoot(r.transform)}/blendShape.{r.sharedMesh.GetBlendShapeName(i)}");
                }));
                if (optimizer.MergeSameRatioBlendShapes)
                {
                    foreach (var matched in MergedMaterialPreview)
                    {
                        var renderers = matched.SelectMany(m => m).Select(slot => slot.renderer).Distinct().ToArray();
                        var mergedBlendShapes = optimizer.FindMergeableBlendShapes(renderers);
                        foreach (var list in mergedBlendShapes)
                        {
                            for (int i = 1; i < list.Count; i++)
                                keptBlendShapePathsCache.Remove(list[i].blendshape);
                        }
                    }
                }
                keptBlendShapePathsCache.IntersectWith(optimizer.GetUsedBlendShapePaths());
            }
            return keptBlendShapePathsCache;
        }
    }

    private Transform[] UnmovingBones
    {
        get
        {
            if (unmovingBonesCache == null)
            {
                var bones = new HashSet<Transform>();
                var unmoving = optimizer.FindAllUnmovingTransforms();
                optimizer.GetUsedComponentsInChildren<SkinnedMeshRenderer>().ToList().ForEach(
                    r => bones.UnionWith(r.bones.Where(b => unmoving.Contains(b))));
                unmovingBonesCache = bones.ToArray();
            }
            return unmovingBonesCache;
        }
    }

    private GameObject[] GameObjectsWithToggleAnimations
    {
        get
        {
            if (gameObjectsWithToggleAnimationsCache == null)
            {
                gameObjectsWithToggleAnimationsCache =
                    optimizer.FindAllGameObjectTogglePaths()
                    .Select(p => optimizer.GetTransformFromPath(p)?.gameObject)
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
                var exclusions = optimizer.GetAllExcludedTransforms();
                var tuple = optimizer.GetUsedComponentsInChildren<Renderer>()
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

    private Texture2D[] NonBC5NormalMaps
    {
        get
        {
            if (nonBC5NormalMapsCache == null)
            {
                var exclusions = optimizer.GetAllExcludedTransforms();
                var renderers = optimizer.GetUsedComponentsInChildren<Renderer>();
                var textures = new HashSet<Texture2D>();
                foreach (var renderer in renderers)
                {
                    if (exclusions.Contains(renderer.transform))
                        continue;
                    var materials = renderer.sharedMaterials;
                    foreach (var material in materials)
                    {
                        if (material == null || material.shader == null)
                            continue;
                        var parsed = ShaderAnalyzer.Parse(material.shader);
                        if (parsed == null)
                            continue;
                        foreach (var prop in parsed.properties)
                        {
                            if (prop.type != ParsedShader.Property.Type.Texture2D)
                                continue;
                            var tex = material.GetTexture(prop.name) as Texture2D;
                            if (tex != null && tex.format != TextureFormat.BC5)
                            {
                                var assetImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) as TextureImporter;
                                if (assetImporter != null && assetImporter.textureType == TextureImporterType.NormalMap)
                                {
                                    textures.Add(tex);
                                }
                            }
                        }
                    }
                }
                nonBC5NormalMapsCache = textures.ToArray();
            }
            return nonBC5NormalMapsCache;
        }
    }

    private string[] AnimatedMaterialPropertyPaths
    {
        get
        {
            if (animatedMaterialPropertyPathsCache == null)
            {
                animatedMaterialPropertyPathsCache = optimizer.FindAllAnimatedMaterialProperties()
                    .SelectMany(kv => kv.Value.Select(prop => $"{kv.Key}.{prop}")).ToArray();
            }
            return animatedMaterialPropertyPathsCache;
        }
    }

    public bool CanLockIn(Material material)
    {
        if (material == null)
            return false;
        if (material.HasProperty("_ShaderOptimizer"))
            return true;
        if (material.HasProperty("_ShaderOptimizerEnabled"))
            return true;
        if (material.HasProperty("__Baked"))
            return true;
        return false;
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

    private static Dictionary<string, List<string>> tooltipCache = null;
    private Dictionary<string, List<string>> TooltipCache
    {
        get
        {
            if (tooltipCache == null)
            {
                tooltipCache = new Dictionary<string, List<string>>();
                var path = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
                path = path.Substring(0, path.LastIndexOf('/'));
                using (var reader = new System.IO.StreamReader(path + "/../README.md"))
                {
                    string line;
                    string currentTooltip = "DUMMY_SECTION";
                    tooltipCache[currentTooltip] = new List<string>();
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith("#") && line.IndexOf(' ') != -1)
                        {
                            int index = line.IndexOf(' ');
                            currentTooltip = line.Substring(index + 1);
                            tooltipCache[currentTooltip] = new List<string>();
                        }
                        else
                        {
                            tooltipCache[currentTooltip].Add(line);
                        }
                    }
                }

                // trim empty lines at start and end of the tooltips
                foreach (var pair in tooltipCache)
                {
                    while (pair.Value.Count > 0 && string.IsNullOrWhiteSpace(pair.Value[0]))
                        pair.Value.RemoveAt(0);
                    while (pair.Value.Count > 0 && string.IsNullOrWhiteSpace(pair.Value[pair.Value.Count - 1]))
                        pair.Value.RemoveAt(pair.Value.Count - 1);
                }
            }
            return tooltipCache;
        }
    }

    private bool Button(string label)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Space(15 * EditorGUI.indentLevel);
        var result = GUILayout.Button(label);
        GUILayout.EndHorizontal();
        return result;
    }

    private bool ToggleOptimizerProperty(string propertyName)
    {
        var property = typeof(d4rkAvatarOptimizer).GetProperty(propertyName);
        if (property == null)
            return false;
        var value = (bool)property.GetValue(optimizer);
        var output = value;
        var content = GetLabelWithTooltip(d4rkAvatarOptimizer.GetDisplayName(propertyName));
        using (new EditorGUI.DisabledScope(!optimizer.CanChangeSetting(propertyName)))
        {
            output = EditorGUILayout.ToggleLeft(content, value);
        }
        if (!string.IsNullOrEmpty(content.tooltip))
        {
            var rect = GUILayoutUtility.GetLastRect();
            rect.x += rect.width - 20;
            rect.width = 20;
            GUI.DrawTexture(rect, EditorGUIUtility.IconContent("_Help").image);
        }
        if (value != output)
        {
            ClearUICaches();
            EditorUtility.SetDirty(optimizer);
            property.SetValue(optimizer, output);
        }
        return output;
    }

    private bool Foldout(string label, ref bool value)
    {
        var content = GetLabelWithTooltip(label);
        bool output = EditorGUILayout.Foldout(value, content, true);
        if (!string.IsNullOrEmpty(content.tooltip))
        {
            var rect = GUILayoutUtility.GetLastRect();
            rect.x += rect.width - 20;
            rect.width = 20;
            GUI.DrawTexture(rect, EditorGUIUtility.IconContent("_Help").image);
        }
        if (value != output)
        {
            ClearUICaches();
            EditorUtility.SetDirty(optimizer);
        }
        return value = output;
    }

    private GUIContent GetLabelWithTooltip(string label, string tooltipKey = null)
    {
        tooltipKey = tooltipKey ?? label;
        if (tooltipKey.EndsWith(")") && !TooltipCache.ContainsKey(tooltipKey))
        {
            tooltipKey = tooltipKey.Substring(0, tooltipKey.LastIndexOf('(')).TrimEnd();
        }
        if (TooltipCache.TryGetValue(tooltipKey, out var tooltip))
        {
            return new GUIContent(label, string.Join("\n", tooltip));
        }
        return new GUIContent(label);
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

    public void DrawDebugList(string[] array)
    {
        foreach (var obj in array)
        {
            EditorGUILayout.LabelField(obj);
        }
        if (array.Length == 0)
        {
            EditorGUILayout.LabelField("---");
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
            if (output != null && optimizer.GetTransformPathToRoot(output) == null)
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

    enum PerformanceCategory
    {
        SkinnedMeshCount,
        MeshCount,
        MaterialCount,
        FXLayerCount,
        BlendShapeCount,
    }

    static Dictionary<PerformanceCategory, int[]> _perfLevelsWindows = new Dictionary<PerformanceCategory, int[]>()
    {
        { PerformanceCategory.SkinnedMeshCount, new int[] {1, 2, 8, 16, int.MaxValue} },
        { PerformanceCategory.MeshCount, new int[] {4, 8, 16, 24, int.MaxValue} },
        { PerformanceCategory.MaterialCount, new int[] {4, 8, 16, 32, int.MaxValue} },
        { PerformanceCategory.FXLayerCount, new int[] {4, 8, 16, 32, int.MaxValue} },
        { PerformanceCategory.BlendShapeCount, new int[] {32, 48, 64, 128, int.MaxValue} },
    };
    static Dictionary<PerformanceCategory, int[]> _perfLevelsAndroid = new Dictionary<PerformanceCategory, int[]>()
    {
        { PerformanceCategory.SkinnedMeshCount, new int[] {1, 1, 2, 2, int.MaxValue} },
        { PerformanceCategory.MeshCount, new int[] {1, 1, 2, 2, int.MaxValue} },
        { PerformanceCategory.MaterialCount, new int[] {1, 1, 2, 4, int.MaxValue} },
        { PerformanceCategory.FXLayerCount, new int[] {2, 4, 8, 16, int.MaxValue} },
        { PerformanceCategory.BlendShapeCount, new int[] {24, 32, 48, 64, int.MaxValue} },
    };

    private void PerfRankChangeLabel(string label, int oldValue, int newValue, PerformanceCategory category)
    {
        var oldRating = PerformanceRating.VeryPoor;
        var newRating = PerformanceRating.VeryPoor;
        var perfLevels = d4rkAvatarOptimizer.HasCustomShaderSupport ? _perfLevelsWindows : _perfLevelsAndroid;
        if (perfLevels.ContainsKey(category))
        {
            oldRating = GetPerfRank(oldValue, perfLevels[category]);
            newRating = GetPerfRank(newValue, perfLevels[category]);
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
}
#endif