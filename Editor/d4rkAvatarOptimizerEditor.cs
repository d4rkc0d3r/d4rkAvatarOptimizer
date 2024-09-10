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
    private static long longestTimeUsed = -2;

    public override void OnInspectorGUI()
    {
        optimizer = (d4rkAvatarOptimizer)target;
        OnSelectionChange();
        // exclude OnSelectionChange from timing since it parses shaders which will stay cached after the first time
        var stopWatch = new System.Diagnostics.Stopwatch();
        stopWatch.Start();
        if (nullMaterial == null)
        {
            nullMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
            nullMaterial.name = "(null material slot)";
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
        }
        float currentViewWidth = GUILayoutUtility.GetLastRect().width + 23;

        var path = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
        EditorGUILayout.Space();

        Rect settingsRect = new Rect();

        using (new EditorGUI.IndentLevelScope())
        {
            EditorGUILayout.LabelField($"<size=20>d4rk{(currentViewWidth > 350 ? "pl4y3r's" : "")} Avatar Optimizer</size>", new GUIStyle(EditorStyles.label) { richText = true, alignment = TextAnchor.LowerCenter });
            settingsRect = GUILayoutUtility.GetLastRect();
            EditorGUILayout.LabelField($"v{packageInfo.version}", EditorStyles.centeredGreyMiniLabel);
        }

        settingsRect.width = 24;
        settingsRect.height = 24;
        bool pressedSettingsButton = GUI.Button(settingsRect, new GUIContent("", "Settings"));
        GUI.DrawTexture(settingsRect, EditorGUIUtility.IconContent("Settings@2x").image);
        if (pressedSettingsButton)
        {
            EditorWindow.GetWindow(typeof(AvatarOptimizerSettings));
        }

        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Exit play mode to use the optimizer.", MessageType.Info);
            return;
        }

        var presets = optimizer.GetPresetNames();
        if (presets.Count > 0)
        {
            using (new EditorGUILayout.HorizontalScope(GUI.skin.box))
            {
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
            }
        }

        ToggleOptimizerProperty(nameof(optimizer.ApplyOnUpload));
        if (d4rkAvatarOptimizer.HasCustomShaderSupport)
            ToggleOptimizerProperty(nameof(optimizer.WritePropertiesAsStaticValues));
        ToggleOptimizerProperty(nameof(optimizer.MergeSkinnedMeshes));
        using (new EditorGUI.IndentLevelScope())
        {
            if (d4rkAvatarOptimizer.HasCustomShaderSupport)
                ToggleOptimizerProperty(nameof(optimizer.MergeSkinnedMeshesWithShaderToggle));
            ToggleOptimizerProperty(nameof(optimizer.MergeSkinnedMeshesWithNaNimation));
            using (new EditorGUI.IndentLevelScope())
            {
                ToggleOptimizerProperty(nameof(optimizer.NaNimationAllow3BoneSkinning));
                ToggleOptimizerProperty(nameof(optimizer.MergeSkinnedMeshesSeparatedByDefaultEnabledState));
            }
            ToggleOptimizerProperty(nameof(optimizer.MergeStaticMeshesAsSkinned));
        }
        if (d4rkAvatarOptimizer.HasCustomShaderSupport)
        {
            ToggleOptimizerProperty(nameof(optimizer.MergeDifferentPropertyMaterials));
            using (new EditorGUI.IndentLevelScope())
            {
                ToggleOptimizerProperty(nameof(optimizer.MergeSameDimensionTextures));
                using (new EditorGUI.IndentLevelScope())
                {
                    ToggleOptimizerProperty(nameof(optimizer.MergeMainTex));
                }
            }
        }
        ToggleOptimizerProperty(nameof(optimizer.OptimizeFXLayer));
        using (new EditorGUI.IndentLevelScope())
        {
            ToggleOptimizerProperty(nameof(optimizer.CombineApproximateMotionTimeAnimations));
        }
        ToggleOptimizerProperty(nameof(optimizer.DisablePhysBonesWhenUnused));
        ToggleOptimizerProperty(nameof(optimizer.MergeSameRatioBlendShapes));
        ToggleOptimizerProperty(nameof(optimizer.MMDCompatibility));
        ToggleOptimizerProperty(nameof(optimizer.DeleteUnusedComponents));
        ToggleOptimizerProperty(nameof(optimizer.DeleteUnusedGameObjects));
        ToggleOptimizerProperty(nameof(optimizer.UseRingFingerAsFootCollider));

        if (optimizer.ExcludeTransforms == null)
            optimizer.ExcludeTransforms = new List<Transform>();
        if (Foldout($"Exclusions ({optimizer.ExcludeTransforms.Count})", ref optimizer.ShowExcludedTransforms))
        {
            using (new EditorGUI.IndentLevelScope())
            {
                DynamicTransformList(optimizer, nameof(optimizer.ExcludeTransforms));
            }
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

        if (longestTimeUsed > AvatarOptimizerSettings.AutoRefreshPreviewTimeout)
        {
            EditorGUILayout.HelpBox($"Preview auto refresh is disabled because it took {longestTimeUsed}ms which is longer than the threshold of {AvatarOptimizerSettings.AutoRefreshPreviewTimeout}ms to refresh.\n"
                + "The preview might still be refreshed manually by clicking the refresh button.", MessageType.Info);
            if (GUILayout.Button("Refresh Preview"))
            {
                longestTimeUsed = 0;
                ClearUICaches();
            }
        }

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
            var nonErrors = new HashSet<string>() {"toggle", "motion time", "blend tree", "multi toggle"};
            var mergedLayerCount = optimizer.OptimizeFXLayer ? optimizer.AnalyzeFXLayerMergeAbility().Count(list => list.All(e => nonErrors.Contains(e))) : 0;
            var layerCount = optimizer.GetFXLayerLayers().Length;
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
                var fxLayerLayers = optimizer.GetFXLayerLayers();
                var nonErrors = new HashSet<string>() {"toggle", "motion time", "useless", "blend tree", "multi toggle"};
                for (int i = 0; i < errorMessages.Count; i++)
                {
                    var perfRating = PerformanceRating.VeryPoor;
                    if (errorMessages[i].Count == 1 && (errorMessages[i][0] == "toggle" || errorMessages[i][0] == "multi toggle" || errorMessages[i][0] == "blend tree"))
                        perfRating = PerformanceRating.Good;
                    else if (errorMessages[i].Count == 1 && errorMessages[i][0] == "motion time")
                        perfRating = PerformanceRating.Medium;
                    if (uselessLayers.Contains(i))
                        perfRating = PerformanceRating.Excellent;

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(new GUIContent(GetPerformanceIconForRating(perfRating)), GUILayout.Width(20));
                        EditorGUILayout.LabelField(new GUIContent($"{i}{fxLayerLayers[i].name}", string.Join("\n", errorMessages[i])));
                    }
                    if (optimizer.ShowFXLayerMergeErrors)
                    {
                        using (new EditorGUI.IndentLevelScope(2))
                        {
                            foreach (var error in errorMessages[i].Where(e => !nonErrors.Contains(e)))
                            {
                                EditorGUILayout.LabelField(error);
                            }
                        }
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
            if (Foldout("Unmergable NaNimation by Animations", ref optimizer.DebugShowMeshesThatCantMergeNaNimationCausedByAnimations))
            {
                Profiler.StartSection("Unmergable NaNimation by Animations");
                DrawDebugList(CantMergeNaNimationBecauseOfWDONAnimations);
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
            if (optimizer.MergeSameRatioBlendShapes && Foldout("Same Ratio Blend Shapes", ref optimizer.DebugShowMergeableBlendShapes))
            {
                Profiler.StartSection("Same Ratio Blend Shapes");
                foreach (var list in MergeableBlendShapes)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.Space(15, false);
                        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
                        {
                            foreach (var ratio in list)
                            {
                                using (new EditorGUILayout.HorizontalScope())
                                {
                                    EditorGUILayout.LabelField($"{ratio.value * 100:F1}".Replace(".0", ""), GUILayout.Width(60));
                                    EditorGUILayout.LabelField(ratio.blendshape.Replace("/blendShape.", "."));
                                }
                            }
                        }
                    }
                    EditorGUILayout.Separator();
                }
                Profiler.EndSection();
            }
            if (Foldout("Mesh Bone Weight Stats", ref optimizer.DebugShowBoneWeightStats))
            {
                Profiler.StartSection("Mesh Bone Weight Stats");
                var statsList = optimizer.GetUsedComponentsInChildren<SkinnedMeshRenderer>()
                    .Select(r => r.sharedMesh).Distinct()
                    .Select(mesh => (mesh, GetMeshBoneWeightStats(mesh)))
                    .OrderByDescending(t => t.Item2[3].count)
                    .ThenByDescending(t => t.Item2[2].count)
                    .ThenByDescending(t => t.Item2[1].count)
                    .ThenByDescending(t => t.Item2[0].count)
                    .ToArray();
                foreach (var tuple in statsList)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.Space(15, false);
                        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
                        {
                            EditorGUILayout.ObjectField(tuple.mesh, typeof(Mesh), false);
                            var stats = tuple.Item2;
                            if (stats[0].count == 0)
                            {
                                EditorGUILayout.LabelField("No bone weights");
                            }
                            else
                            {
                                var entryWidth = GUILayout.Width(60f);
                                using (new EditorGUILayout.HorizontalScope())
                                {
                                    EditorGUILayout.LabelField($"Index", entryWidth);
                                    EditorGUILayout.LabelField($"Count", entryWidth);
                                    EditorGUILayout.LabelField($"Max", entryWidth);
                                    EditorGUILayout.LabelField($"Median", entryWidth);
                                }
                                for (int i = 0; i < 4; i++)
                                {
                                    if (stats[i].count == 0)
                                        continue;

                                    using (new EditorGUILayout.HorizontalScope())
                                    {
                                        EditorGUILayout.LabelField($"{i}", entryWidth);
                                        EditorGUILayout.LabelField($"{stats[i].count}", entryWidth);
                                        EditorGUILayout.LabelField($"{stats[i].maxValue:F2}", entryWidth);
                                        EditorGUILayout.LabelField($"{stats[i].medianValue:F2}", entryWidth);
                                    }
                                }
                            }
                        }
                    }
                    EditorGUILayout.Separator();
                }
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
                    using (new EditorGUI.IndentLevelScope())
                    {
                        DrawDebugList(pair.Value.ToArray());
                    }
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
                    using (new EditorGUI.IndentLevelScope())
                    {
                        DrawDebugList(pair.Value.ToArray());
                    }
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
        stopWatch.Stop();
        if (stopWatch.ElapsedMilliseconds > longestTimeUsed && stopWatch.ElapsedMilliseconds > AvatarOptimizerSettings.AutoRefreshPreviewTimeout)
        {
            // longestTimeUsed < 0 means it's one of the first times the optimizer is used
            // first couple times are always slower since the JIT compiler has to compile the code
            longestTimeUsed = longestTimeUsed < 0 ? longestTimeUsed + 1 : stopWatch.ElapsedMilliseconds;
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

        var isHumanoid = optimizer.IsHumanoid();
        if (avDescriptor.baseAnimationLayers == null || avDescriptor.baseAnimationLayers.Length != (isHumanoid ? 5 : 3))
        {
            if (isHumanoid)
            {
                EditorGUILayout.HelpBox("Humanoid rig but playable base layer count in the avatar descriptor is not 5.\n" +
                    "Try to reimport the avatar fbx.", MessageType.Error);
            }
            else
            {
                EditorGUILayout.HelpBox("Generic rig but playable base layer count in the avatar descriptor is not 3.\n" +
                    "Try to reimport the avatar fbx.", MessageType.Error);
            }
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

        if ((optimizer.MergeSkinnedMeshesWithNaNimation || optimizer.MergeSkinnedMeshesWithShaderToggle)
            && optimizer.GetPolyCount() > d4rkAvatarOptimizer.MaxPolyCountForAutoShaderToggle)
        {
            EditorGUILayout.HelpBox(
                $"For avatars with a high poly count ({(int)(d4rkAvatarOptimizer.MaxPolyCountForAutoShaderToggle / 1000)}k or more)" +
                " it might be disadvantageous to merge meshes with NaNimation or Shader toggles.", MessageType.Warning);
        }

        if (optimizer.WritePropertiesAsStaticValues)
        {
            var allMaterials = optimizer.GetUsedComponentsInChildren<Renderer>()
                .Where(r => !exclusions.Contains(r.transform))
                .SelectMany(r => r.sharedMaterials).Distinct().ToArray();

            var correctlyParsedMaterials = allMaterials
                .Select(m => ShaderAnalyzer.Parse(m?.shader))
                .Where(p => (p?.parsedCorrectly ?? false)).ToArray();

            var mergeInfoList = new List<string>();

            if (correctlyParsedMaterials.Length != allMaterials.Length)
            {
                mergeInfoList.Add("Some materials could not be parsed.\n");
            }

            if (optimizer.MergeDifferentPropertyMaterials && correctlyParsedMaterials.Any(p => !p.CanMerge()))
            {
                mergeInfoList.Add("Some materials do not support merging.\n");
            }

            if (optimizer.MergeSameDimensionTextures && correctlyParsedMaterials.Any(p => p.CanMerge() && !p.CanMergeTextures()))
            {
                mergeInfoList.Add("Some materials do not support merging textures.\n");
            }

            if (mergeInfoList.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    string.Join("", mergeInfoList) +
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

        if (optimizer.MergeSkinnedMeshesWithNaNimation && CantMergeNaNimationBecauseOfWDONAnimations.Length > 0)
        {
            EditorGUILayout.HelpBox(
                "Some meshes are missing the corresponding on or off toggle animation. This is likely due to a WD ON workflow.\n" +
                "This means they can't be merged with NaNimation and switching to a WD OFF workflow would help reduce mesh count further.\n" +
                "Check the Debug Info foldout for a full list at:\n\"Unmergable NaNimation by Animations\"", MessageType.Info);
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

        var furyType = Type.GetType("VF.Model.VRCFury, VRCFury");
        if (furyType != null && optimizer.GetComponentsInChildren(furyType, true).Any())
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
    private Renderer[] cantMergeNaNimationBecauseOfWDONAnimationsCache = null;
    private string[] animatedMaterialPropertyPathsCache = null;
    private HashSet<string> keptBlendShapePathsCache = null;
    private List<List<(string blendshape, float value)>> mergeableBlendShapesCache = null;
    private Dictionary<Mesh, (int count, float maxValue, float medianValue)[]> meshBoneWeightStatsCache = null;

    private void ClearUICaches()
    {
        if (longestTimeUsed > AvatarOptimizerSettings.AutoRefreshPreviewTimeout)
            return;
        mergedMaterialPreviewCache = null;
        unmovingBonesCache = null;
        gameObjectsWithToggleAnimationsCache = null;
        crunchedTexturesCache = null;
        nonBC5NormalMapsCache = null;
        cantMergeNaNimationBecauseOfWDONAnimationsCache = null;
        animatedMaterialPropertyPathsCache = null;
        keptBlendShapePathsCache = null;
        mergeableBlendShapesCache = null;
        optimizer.ClearCaches();
    }

    private void OnSelectionChange()
    {
        if (lastSelected == optimizer)
            return;
        if (longestTimeUsed > 0)
            longestTimeUsed = 0;
        lastSelected = optimizer;
        ClearUICaches();
        if (optimizer.DoAutoSettings)
        {
            AvatarOptimizerSettings.ApplyDefaults(optimizer);
            optimizer.ApplyAutoSettings();
        }
        ShaderAnalyzer.ParseAndCacheAllShaders(optimizer.FindAllUsedMaterials().Select(m => m.shader), false);
    }

    private (int count, float maxValue, float medianValue)[] GetMeshBoneWeightStats(Mesh mesh)
    {
        if (meshBoneWeightStatsCache == null)
            meshBoneWeightStatsCache = new Dictionary<Mesh, (int count, float maxValue, float medianValue)[]>();
        if (mesh == null)
            return new (int count, float maxValue, float medianValue)[4];
        if (!meshBoneWeightStatsCache.TryGetValue(mesh, out var stats))
        {
            stats = new (int count, float maxValue, float medianValue)[4];
            var nonZeroWeights = new List<float>[4]
            {
                new List<float>(),
                new List<float>(),
                new List<float>(),
                new List<float>(),
            };
            var boneWeights = mesh.boneWeights;
            for (int i = 0; i < boneWeights.Length; i++)
            {
                var weight = boneWeights[i];
                if (weight.weight0 > 0)
                    nonZeroWeights[0].Add(weight.weight0);
                if (weight.weight1 > 0)
                    nonZeroWeights[1].Add(weight.weight1);
                if (weight.weight2 > 0)
                    nonZeroWeights[2].Add(weight.weight2);
                if (weight.weight3 > 0)
                    nonZeroWeights[3].Add(weight.weight3);
            }
            for (int i = 0; i < 4; i++)
            {
                stats[i].count = nonZeroWeights[i].Count;
                if (stats[i].count == 0)
                {
                    stats[i].maxValue = 0;
                    stats[i].medianValue = 0;
                    continue;
                }
                nonZeroWeights[i].Sort();
                stats[i].maxValue = nonZeroWeights[i][stats[i].count - 1];
                stats[i].medianValue = nonZeroWeights[i][stats[i].count / 2];
            }
            meshBoneWeightStatsCache.Add(mesh, stats);
        }
        return stats;
    }

    private List<List<List<MaterialSlot>>> MergedMaterialPreview
    {
        get
        {
            if (mergedMaterialPreviewCache == null)
            {
                mergedMaterialPreviewCache = new List<List<List<MaterialSlot>>>();
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

    private List<List<(string blendshape, float value)>> MergeableBlendShapes
    {
        get
        {
            if (mergeableBlendShapesCache == null)
            {
                mergeableBlendShapesCache = new List<List<(string blendshape, float value)>>();
                if (optimizer.MergeSameRatioBlendShapes)
                {
                    foreach (var matched in MergedMaterialPreview)
                    {
                        var renderers = matched.SelectMany(m => m).Select(slot => slot.renderer).Distinct().ToArray();
                        var mergedBlendShapes = optimizer.FindMergeableBlendShapes(renderers);
                        mergeableBlendShapesCache.AddRange(mergedBlendShapes);
                    }
                }
            }
            return mergeableBlendShapesCache;
        }
    }

    private Renderer[] CantMergeNaNimationBecauseOfWDONAnimations
    {
        get
        {
            if (cantMergeNaNimationBecauseOfWDONAnimationsCache != null)
                return cantMergeNaNimationBecauseOfWDONAnimationsCache;
            return cantMergeNaNimationBecauseOfWDONAnimationsCache =
                optimizer.FindAllPathsWhereMeshOrGameObjectHasOnlyOnOrOffAnimation()
                    .Select(p => optimizer.GetTransformFromPath(p))
                    .Where(t => t != null)
                    .Select(t => t.GetComponent<Renderer>())
                    .Where(r => r != null && !optimizer.GetRendererDefaultEnabledState(r))
                    .ToArray();
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
                foreach (var list in MergeableBlendShapes)
                {
                    for (int i = 1; i < list.Count; i++)
                        keptBlendShapePathsCache.Remove(list[i].blendshape);
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
                    foreach (var prop in parsed.texture2DProperties)
                    {
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
                var materials = renderers
                    .Where(r => !exclusions.Contains(r.transform))
                    .SelectMany(r => r.sharedMaterials)
                    .Where(mat => mat != null && mat.shader != null)
                    .Distinct();
                foreach (var material in materials)
                {
                    var parsed = ShaderAnalyzer.Parse(material.shader);
                    if (parsed == null)
                        continue;
                    foreach (var prop in parsed.texture2DProperties)
                    {
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
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(15 * EditorGUI.indentLevel);
            return GUILayout.Button(label);
        }
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
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(indent);
            EditorGUILayout.ObjectField(slot.renderer, typeof(Renderer), true, GUILayout.Width(EditorGUIUtility.currentViewWidth / 2 - 20 - (indent)));
            int originalIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            EditorGUILayout.ObjectField(slot.material, typeof(Material), false);
            EditorGUI.indentLevel = originalIndent;
        }
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

    private void DynamicTransformList(Object obj, string propertyPath)
    {
        using (var serializedObject = new SerializedObject(obj))
        {
            // Find the SerializedProperty representing the list of Transforms
            SerializedProperty listProperty = serializedObject.FindProperty(propertyPath);

            // Add a null element at the end of the list for the user to add new elements
            listProperty.InsertArrayElementAtIndex(listProperty.arraySize);
            SerializedProperty newElement = listProperty.GetArrayElementAtIndex(listProperty.arraySize - 1);
            newElement.objectReferenceValue = null;

            for (int i = 0; i < listProperty.arraySize; i++)
            {
                SerializedProperty element = listProperty.GetArrayElementAtIndex(i);
                Transform output = null;

                using (new EditorGUILayout.HorizontalScope())
                {
                    output = EditorGUILayout.ObjectField(element.objectReferenceValue, typeof(Transform), true) as Transform;

                    if (i == listProperty.arraySize - 1)
                    {
                        GUILayout.Space(23);
                    }
                    else if (GUILayout.Button("X", GUILayout.Width(20)))
                    {
                        output = null;
                    }
                }

                if (element.objectReferenceValue != output)
                {
                    ClearUICaches();
                }

                if (output != null && optimizer.GetTransformPathToRoot(output) == null)
                {
                    output = null;
                }

                element.objectReferenceValue = output;
            }

            // Remove any null elements from the list
            for (int i = listProperty.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty element = listProperty.GetArrayElementAtIndex(i);
                if (element.objectReferenceValue == null)
                {
                    listProperty.DeleteArrayElementAtIndex(i);
                }
            }

            // Apply the modified properties to the serializedObject
            serializedObject.ApplyModifiedProperties();
        }
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

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(new GUIContent(GetPerformanceIconForRating(oldRating)), GUILayout.Width(20));
            EditorGUILayout.LabelField($"{oldValue}", GUILayout.Width(25));
            EditorGUILayout.LabelField($"->", GUILayout.Width(20));
            EditorGUILayout.LabelField(new GUIContent(GetPerformanceIconForRating(newRating)), GUILayout.Width(20));
            EditorGUILayout.LabelField($"{newValue}", GUILayout.Width(25));
            EditorGUILayout.LabelField(label);
        }
    }
}
#endif