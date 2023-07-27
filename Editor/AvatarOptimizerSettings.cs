#if UNITY_EDITOR
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using d4rkpl4y3r.AvatarOptimizer;
using d4rkpl4y3r.AvatarOptimizer.Util;
using d4rkpl4y3r.AvatarOptimizer.Extensions;

public class AvatarOptimizerSettings : EditorWindow
{
    #pragma warning disable 0414
    private static bool OptimizeOnUpload = true;
    private static bool WritePropertiesAsStaticValues = true;
    private static bool MergeSkinnedMeshes = true;
    private static bool MergeStaticMeshesAsSkinned = true;
    private static int ForceMergeBlendShapeMissMatch = 2;
    private static bool KeepMaterialPropertyAnimationsSeparate = true;
    private static bool MergeDifferentPropertyMaterials = true;
    private static bool MergeSameDimensionTextures = true;
    private static bool MergeBackFaceCullingWithCullingOff = false;
    private static bool MergeDifferentRenderQueue = false;
    private static bool MergeSameRatioBlendShapes = true;
    private static bool MergeSimpleTogglesAsBlendTree = true;
    private static bool KeepMMDBlendShapes = false;
    private static bool DeleteUnusedComponents = true;
    private static int DeleteUnusedGameObjects = 2;
    private static bool UseRingFingerAsFootCollider = false;
    private static bool ProfileTimeUsed = false;
    #pragma warning restore 0414

    private static readonly string PrefsPrefix = "d4rkpl4y3r_AvatarOptimizer_";

    private static Dictionary<string, string> FieldDisplayName = new Dictionary<string, string>() {
        {"OptimizeOnUpload", "Optimize on Upload"},
        {"WritePropertiesAsStaticValues", "Write Properties as Static Values"},
        {"MergeSkinnedMeshes", "Merge Skinned Meshes"},
        {"MergeStaticMeshesAsSkinned", "Merge Static Meshes as Skinned"},
        {"ForceMergeBlendShapeMissMatch", "Merge Regardless of Blend Shapes"},
        {"KeepMaterialPropertyAnimationsSeparate", "Keep Material Animations Separate"},
        {"MergeDifferentPropertyMaterials", "Merge Different Property Materials"},
        {"MergeSameDimensionTextures", "Merge Same Dimension Textures"},
        {"MergeBackFaceCullingWithCullingOff", "Merge Cull Back with Cull Off"},
        {"MergeDifferentRenderQueue", "Merge Different Render Queue"},
        {"KeepMMDBlendShapes", "Keep MMD Blend Shapes"},
        {"DeleteUnusedComponents", "Delete Unused Components"},
        {"DeleteUnusedGameObjects", "Delete Unused GameObjects"},
        {"MergeSimpleTogglesAsBlendTree", "Merge Simple Toggles as Blend Tree"},
        {"MergeSameRatioBlendShapes", "Merge Same Ratio Blend Shapes"},
        {"UseRingFingerAsFootCollider", "Use Ring Finger as Foot Collider"},
        {"ProfileTimeUsed", "Profile Time Used"}
    };
    
    [MenuItem("Tools/d4rkpl4y3r/Avatar Optimizer Settings")]
    static void Init()
    {
        GetWindow(typeof(AvatarOptimizerSettings));
    }

    private Vector2 scrollPos;

    public void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        EditorGUILayout.HelpBox("These settings are the default values when adding the Avatar Optimizer component to a model."
            +"You can change them here to suit your needs."
            +"\nA * indicates settings that are different from their default value.", MessageType.Info);
        var fields = typeof(AvatarOptimizerSettings).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(bool) || f.FieldType == typeof(int)).ToArray();
        if (GUILayout.Button("Reset to Default"))
        {
            foreach (var field in fields)
            {
                EditorPrefs.DeleteKey(PrefsPrefix + field.Name);
            }
        }
        foreach (var field in fields)
        {
            EditorGUILayout.BeginHorizontal();
            bool isDefault = true;
            if (field.FieldType == typeof(bool))
            {
                GUILayoutUtility.GetRect(35, 15, GUILayout.Width(35));
                var value = EditorGUILayout.Toggle(0 != GetValue(field.Name), GUILayout.Width(15)) ? 1 : 0;
                SetValue(field.Name, value);
                isDefault = field.GetValue(null).Equals(value != 0);
            }
            else
            {
                var value = EditorGUILayout.Popup(GetValue(field.Name), new string[] { "Off", "On", "Auto" }, GUILayout.Width(50));
                SetValue(field.Name, value);
                isDefault = field.GetValue(null).Equals(value);
            }
            if (!FieldDisplayName.TryGetValue(field.Name, out string displayName))
            {
                displayName = field.Name;
            }
            EditorGUILayout.LabelField(displayName + (isDefault ? "" : " *"));
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.Space();
        EditorGUILayout.EndScrollView();
    }

    public static int GetValue(string key)
    {
        var field = typeof(AvatarOptimizerSettings).GetField(key, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (field == null)
        {
            throw new System.ArgumentException("Field " + key + " does not exist in AvatarOptimizerSettings");
        }
        if (!EditorPrefs.HasKey(PrefsPrefix + key))
        {
            EditorPrefs.SetInt(PrefsPrefix + key, field.FieldType == typeof(bool) ? (bool)field.GetValue(null) ? 1 : 0 : (int)field.GetValue(null));
        }
        return EditorPrefs.GetInt(PrefsPrefix + key);
    }

    public static void SetValue(string key, int value)
    {
        var field = typeof(AvatarOptimizerSettings).GetField(key, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (field == null)
        {
            throw new System.ArgumentException("Field " + key + " does not exist in AvatarOptimizerSettings");
        }
        EditorPrefs.SetInt(PrefsPrefix + key, value);
    }

    public static void ApplyDefaults(d4rkAvatarOptimizer optimizer)
    {
        var fields = typeof(AvatarOptimizerSettings).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(bool) || f.FieldType == typeof(int)).ToArray();
        foreach (var field in fields)
        {
            var targetField = typeof(d4rkAvatarOptimizer).GetField(field.Name);
            if (targetField == null)
            {
                throw new System.ArgumentException("Field " + field.Name + " does not exist in d4rkAvatarOptimizer");
            }
            targetField.SetValue(optimizer, GetValue(field.Name) != 0);
        }
    }

    public static bool IsAutoSetting(string key)
    {
        return GetValue(key) == 2;
    }
}
#endif