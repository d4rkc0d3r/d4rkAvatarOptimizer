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
            if (optimizer != null)
                optimizer.ClearCaches();
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
                GUI.Label(GUILayoutUtility.GetLastRect(), new GUIContent("", $"The renderer of material slot {label[^1]}"));
                slot.index = EditorGUILayout.IntField(slot.index, GUILayout.Width(32));
                GUI.Label(GUILayoutUtility.GetLastRect(), new GUIContent("", $"The material index of material slot {label[^1]}"));
                EditorGUILayout.ObjectField(slot.material, typeof(Material), false, GUILayout.MinWidth(100));
                GUI.Label(GUILayoutUtility.GetLastRect(), new GUIContent("", $"The material of material slot {label[^1]}\nThis is only for showing which material is assigned to this slot."));
            }
            finally
            {
                EditorGUI.indentLevel = indentLevel;
            }
            return slot;
        }

        private void HelpBox(string message, int fontSize, MessageType type)
        {
            var style = new GUIStyle(EditorStyles.helpBox) { fontSize = fontSize };
            Texture icon = type switch
            {
                MessageType.Info => EditorGUIUtility.IconContent("console.infoicon").image,
                MessageType.Warning => EditorGUIUtility.IconContent("console.warnicon").image,
                MessageType.Error => EditorGUIUtility.IconContent("console.erroricon").image,
                _ => null
            };
            EditorGUILayout.LabelField(new GUIContent(message, icon), style);
        }

        public void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                var lockLabel = new GUIContent("Lock Avatar Auto Selection", "When enabled, the avatar and optimizer will not auto select based on your selection in the hierarchy.");
                var descLabel = new GUIContent("", "The avatar descriptor to analyze.\nThis will auto select based on your selection in the hierarchy.");
                var optLabel = new GUIContent("", "The d4rkAvatarOptimizer component on the avatar to analyze.\nThis will auto select based on the selected avatar.");

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(15 * EditorGUI.indentLevel);
                    lockAvatarSelection = GUILayout.Toggle(lockAvatarSelection, lockLabel, GUI.skin.button, GUILayout.ExpandWidth(false));
                    var prevIndent = EditorGUI.indentLevel;
                    EditorGUI.indentLevel = 0;
                    avatar = EditorGUILayout.ObjectField(avatar, typeof(VRCAvatarDescriptor), true) as VRCAvatarDescriptor;
                    GUI.Label(GUILayoutUtility.GetLastRect(), descLabel);
                    optimizer = EditorGUILayout.ObjectField(optimizer, typeof(d4rkAvatarOptimizer), true) as d4rkAvatarOptimizer;
                    GUI.Label(GUILayoutUtility.GetLastRect(), optLabel);
                    EditorGUI.indentLevel = prevIndent;
                }

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
            }
            if (optimizer == null || avatar == null)
            {
                HelpBox("Select an avatar with d4rkAvatarOptimizer to see why material merging is not possible.", EditorStyles.helpBox.fontSize + 2, MessageType.Warning);
                return;
            }
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                if (showInfoBoxes = EditorGUILayout.Foldout(showInfoBoxes, "Show Info", toggleOnLabelClick: true))
                {
                    HelpBox(
                        "This tool allows you to analyze why certain material slots on your avatar cannot be merged by the d4rkAvatarOptimizer.\n\n" +
                        "1. Assign two material slots (Slot A and Slot B) from the avatar's renderers.\n" +
                        "2. The tool will analyze if these material slots can be merged based on the optimizer's settings.\n\n" +
                        "You can select material slots directly from the merge preview below by clicking on the A or B buttons respectively. \n" +
                        "This tool does not check if the renderers using these slots can be merged in the first place, only if the material slots themselves can.",
                        EditorStyles.helpBox.fontSize + 1, MessageType.Info);
                    var tools = optimizer.GetNonDestructiveToolsUsedOnAvatar();
                    if (tools.Count > 0)
                    {
                        HelpBox(
                            "The following non-destructive tools are found on the avatar:\n" +
                            string.Join(", ", tools) +
                            "\nThis means the merge analysis can be wrong as these tools can change the avatar at build time which the optimizer can't see before it happens.",
                            EditorStyles.helpBox.fontSize + 1, MessageType.Warning);
                    }
                }
            }
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                slotA = MaterialSlotField("Slot A", slotA);
                slotB = MaterialSlotField("Slot B", slotB);
                (string message, MessageType icon) msg;
                if (slotA.renderer == null || slotB.renderer == null)
                {
                    msg = ("Please assign both material slots to analyze.", MessageType.Info);
                }
                else
                {
                    var result = optimizer.CanCombineMaterialsError(new List<MaterialSlot>() { slotA }, slotB);
                    if (string.IsNullOrEmpty(result))
                    {
                        msg = ("Materials can be merged!", MessageType.Info);
                    }
                    else
                    {
                        msg = (result, MessageType.Warning);
                    }
                }
                HelpBox(msg.message, EditorStyles.helpBox.fontSize + 2, msg.icon);
            }
            using var scrollView = new EditorGUILayout.ScrollViewScope(scrollPos);
            scrollPos = scrollView.scrollPosition;
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    selectSlotsFromMergePreview = EditorGUILayout.ToggleLeft("Select Slots From Optimizer Merge Preview", selectSlotsFromMergePreview, GUILayout.MinWidth(260));
                    using var _ = new EditorGUI.DisabledScope(!selectSlotsFromMergePreview);
                    var buttonRect = EditorGUILayout.GetControlRect(GUILayout.Width(24));
                    buttonRect.height = 24;
                    if (GUI.Button(buttonRect, EditorGUIUtility.IconContent("Refresh")))
                    {
                        mergedMaterialPreviewCache = null;
                        optimizer.ClearCaches();
                    }
                    GUI.Label(buttonRect, new GUIContent("", "Clears merge preview cache.\nClick this if you changed any material properties."));
                }
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
                if (GUILayout.Toggle(slot == slotA, new GUIContent("A", $"Select ({slot.renderer.name}, {slot.index}) as Slot A"), GUI.skin.button, GUILayout.Width(20)))
                    slotA = slot;
                if (GUILayout.Toggle(slot == slotB, new GUIContent("B", $"Select ({slot.renderer.name}, {slot.index}) as Slot B"), GUI.skin.button, GUILayout.Width(20)))
                    slotB = slot;
            }
        }

        private void OnSelectionChange()
        {
            Repaint();
        }
    }
}
#endif