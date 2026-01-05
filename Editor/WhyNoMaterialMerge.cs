#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Avatars.Components;
using MaterialSlot = d4rkAvatarOptimizer.MaterialSlot;
using System.Collections.Generic;
using System.Linq;

namespace d4rkpl4y3r.d4rkavataroptimizer
{
    public class WhyNoMaterialMerge : EditorWindow
    {
        private Vector2 scrollPos;
        private d4rkAvatarOptimizer optimizer;
        private VRCAvatarDescriptor avatar;
        private bool lockAvatarSelection = false;
        private MaterialSlot slotA;
        private MaterialSlot slotB;
        private bool selectSlotsFromMergePreview = false;
        private bool showInfoBoxes = true;

        [MenuItem("Tools/d4rkpl4y3r/Why No Material Merge")]
        static void Init()
        {
            GetWindow<WhyNoMaterialMerge>().Show();
        }

        private List<List<List<MaterialSlot>>> mergedMaterialPreviewCache;
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

        private void AvatarSelectionChanged()
        {
            mergedMaterialPreviewCache = null;
            slotA = new MaterialSlot();
            slotB = new MaterialSlot();
        }

        private MaterialSlot MaterialSlotField(string label, MaterialSlot slot)
        {
            using var _ = new EditorGUILayout.HorizontalScope();
            EditorGUILayout.LabelField(label, GUILayout.MinWidth(50));
            int indentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                slot.renderer = EditorGUILayout.ObjectField(slot.renderer, typeof(Renderer), true, GUILayout.MinWidth(120)) as Renderer;
                slot.index = EditorGUILayout.IntField(slot.index, GUILayout.Width(32));
                EditorGUILayout.ObjectField(slot.material, typeof(Material), false, GUILayout.MinWidth(100));
            }
            finally
            {
                EditorGUI.indentLevel = indentLevel;
            }
            return slot;
        }

        public void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                var lockLabel = new GUIContent("Lock Avatar Auto Selection", "When enabled, the avatar and optimizer will not auto select based on your selection in the hierarchy.");
                lockAvatarSelection = EditorGUILayout.ToggleLeft(lockLabel, lockAvatarSelection);
                if (!lockAvatarSelection && Selection.activeGameObject != null)
                {
                    var currentTransform = Selection.activeGameObject.transform;
                    while (currentTransform != null)
                    {
                        if (currentTransform.TryGetComponent(out VRCAvatarDescriptor descriptor))
                        {
                            if (descriptor != avatar)
                                AvatarSelectionChanged();
                            avatar = descriptor;
                            break;
                        }
                        currentTransform = currentTransform.parent;
                    }
                    if (avatar != null)
                    {
                        optimizer = avatar.GetComponentInChildren<d4rkAvatarOptimizer>(includeInactive: false);
                    }
                }
                var descLabel = new GUIContent("Avatar", "The avatar descriptor to analyze.\nThis will auto select based on your selection in the hierarchy.");
                avatar = EditorGUILayout.ObjectField(descLabel, avatar, typeof(VRCAvatarDescriptor), true) as VRCAvatarDescriptor;
                var optLabel = new GUIContent("Optimizer", "The d4rkAvatarOptimizer component on the avatar to analyze.\nThis will auto select based on the selected avatar.");
                optimizer = EditorGUILayout.ObjectField(optLabel, optimizer, typeof(d4rkAvatarOptimizer), true) as d4rkAvatarOptimizer;
            }
            if (optimizer == null || avatar == null)
            {
                EditorGUILayout.HelpBox("Select an avatar with d4rkAvatarOptimizer to see why material merging is not possible.", MessageType.Warning);
                return;
            }
            if (showInfoBoxes = EditorGUILayout.Foldout(showInfoBoxes, "Show Info", toggleOnLabelClick: true))
            {
                using (new EditorGUILayout.VerticalScope(GUI.skin.box))
                {
                    var tools = optimizer.GetNonDestructiveToolsUsedOnAvatar();
                    if (tools.Count > 0)
                    {
                        EditorGUILayout.HelpBox(
                            "The following non-destructive tools are found on the avatar:\n" +
                            string.Join(", ", tools) +
                            "\nThis means the merge analysis can be wrong as these tools can change the avatar at build time which the optimizer can't see before it happens.",
                            MessageType.Warning);
                    }
                }
            }
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                slotA = MaterialSlotField("Slot A", slotA);
                slotB = MaterialSlotField("Slot B", slotB);
                if (slotA.renderer == null || slotB.renderer == null)
                {
                    EditorGUILayout.HelpBox("Please assign both material slots to analyze.", MessageType.Info);
                }
                else
                {
                    var result = optimizer.CanCombineMaterialsError(new List<MaterialSlot>() { slotA }, slotB);
                    if (string.IsNullOrEmpty(result))
                    {
                        EditorGUILayout.HelpBox("Materials can be merged!", MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(result, MessageType.Warning);
                    }
                }
            }
            using var scrollView = new EditorGUILayout.ScrollViewScope(scrollPos);
            scrollPos = scrollView.scrollPosition;
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                selectSlotsFromMergePreview = EditorGUILayout.ToggleLeft("Select Slots From Optimizer Merge Preview", selectSlotsFromMergePreview);
                EditorGUILayout.Space(8);
                if (selectSlotsFromMergePreview)
                {
                    foreach (var matched in MergedMaterialPreview)
                    {
                        for (int i = 0; i < matched.Count; i++)
                        {
                            for (int j = 0; j < matched[i].Count; j++)
                            {
                                int indent = j == 0 ? 0 : 1;
                                DrawMaterialSlot(matched[i][j], indent);
                            }
                        }
                        EditorGUILayout.Space(8);
                    }
                }
                else
                {
                    var renderers = avatar.GetComponentsInChildren<Renderer>(includeInactive: true);
                    var renderersWithSlots = renderers.Select(r => (r, MaterialSlot.GetAllSlotsFrom(r)))
                        .Where(p => p.Item2.Length > 0)
                        .OrderByDescending(p => p.Item2.Length)
                        .ToArray();
                    foreach ((var _, var slots) in renderersWithSlots)
                    {
                        foreach (var slot in slots)
                        {
                            DrawMaterialSlot(slot, EditorGUI.indentLevel);
                        }
                        EditorGUILayout.Space(8);
                    }
                }
            }
        }
        
        private void DrawMaterialSlot(MaterialSlot slot, int indent)
        {
            indent *= 15;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(indent);
                EditorGUILayout.ObjectField(slot.renderer, typeof(Renderer), true, GUILayout.Width(System.Math.Max(150, EditorGUIUtility.currentViewWidth / 2 - 40) - indent));
                int originalIndent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                EditorGUILayout.ObjectField(slot.material, typeof(Material), false);
                EditorGUI.indentLevel = originalIndent;
                using (new EditorGUI.DisabledScope(slot == slotA))
                {
                    if (GUILayout.Button("A", GUILayout.Width(20)))
                        slotA = slot;
                }
                using (new EditorGUI.DisabledScope(slot == slotB))
                {
                    if (GUILayout.Button("B", GUILayout.Width(20)))
                        slotB = slot;
                }
            }
        }

        private void OnSelectionChange()
        {
            Repaint();
        }
    }
}
#endif