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
    private static d4rkAvatarOptimizer.Settings defaultSettings = new d4rkAvatarOptimizer.Settings();
    
    private static readonly string PrefsPrefix = "d4rkpl4y3r_AvatarOptimizer_";

    public static bool DoOptimizeWithDefaultSettingsWhenNoComponent
    {
        get => EditorPrefs.GetBool(PrefsPrefix + "DoOptimizeWithDefaultSettingsWhenNoComponent", false);
        private set => EditorPrefs.SetBool(PrefsPrefix + "DoOptimizeWithDefaultSettingsWhenNoComponent", value);
    }

    public static int AutoRefreshPreviewTimeout
    {
        get => EditorPrefs.GetInt(PrefsPrefix + "AutoRefreshPreviewTimeout", 500);
        private set => EditorPrefs.SetInt(PrefsPrefix + "AutoRefreshPreviewTimeout", value);
    }

    public static int MotionTimeApproximationSampleCount
    {
        get => Mathf.Clamp(EditorPrefs.GetInt(PrefsPrefix + "MotionTimeApproximationSampleCount", 5), 2, 101);
        private set => EditorPrefs.SetInt(PrefsPrefix + "MotionTimeApproximationSampleCount", value);
    }
    
    [MenuItem("Tools/d4rkpl4y3r/Avatar Optimizer Settings")]
    static void Init()
    {
        GetWindow(typeof(AvatarOptimizerSettings));
    }

    private Vector2 scrollPos;

    public void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        BeginSection("Global Settings");
        DoOptimizeWithDefaultSettingsWhenNoComponent = EditorGUILayout.ToggleLeft(
            new GUIContent("Always Optimize on Upload", "If an Avatar does not have a d4rkAvatarOptimizer component attached, it will be optimized with the default settings below when uploading it to VRChat."),
            DoOptimizeWithDefaultSettingsWhenNoComponent);
        AutoRefreshPreviewTimeout = IntFieldLeft(
            new GUIContent("Auto Refresh Preview Timeout", "In milliseconds. If the preview takes longer than this to refresh, the auto refresh will be disabled."),
            AutoRefreshPreviewTimeout);
        MotionTimeApproximationSampleCount = IntFieldLeft(
            new GUIContent("Motion Time Approximation Sample Count", "The amount of samples used to approximate motion time states. Higher values are more accurate but generate more animation clips."),
            MotionTimeApproximationSampleCount);
        EndSection();
        EditorGUILayout.Space();
        BeginSection("Default Settings");
        EditorGUILayout.HelpBox("These settings are the default values when adding the Avatar Optimizer component to a model."
            +"You can change them here to suit your needs."
            +"\nA * indicates settings that are different from their default value.", MessageType.None);
        var fields = typeof(d4rkAvatarOptimizer.Settings).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
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
            if (field.FieldType == typeof(bool))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayoutUtility.GetRect(35, 15, GUILayout.Width(35));
                var value = EditorGUILayout.ToggleLeft(d4rkAvatarOptimizer.GetDisplayName(field.Name) + (field.GetValue(defaultSettings).Equals(0 != GetValue(field.Name)) ? "" : " *"), 0 != GetValue(field.Name)) ? 1 : 0;
                SetValue(field.Name, value);
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                var value = EditorGUILayout.Popup(GetValue(field.Name), new string[] { "Off", "On", "Auto" }, GUILayout.Width(50));
                SetValue(field.Name, value);
                var content = new GUIContent(d4rkAvatarOptimizer.GetDisplayName(field.Name) + (field.GetValue(defaultSettings).Equals(value) ? "" : " *"));
                var rect = GUILayoutUtility.GetRect(content, EditorStyles.label);
                rect.xMin -= 2;
                GUI.Label(rect, content);
                EditorGUILayout.Space();
                EditorGUILayout.EndHorizontal();
            }
        }
        EndSection();
        EditorGUILayout.EndScrollView();
    }

    private void BeginSection(string title)
    {
        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        GUILayoutUtility.GetRect(15, 15, GUILayout.Width(15));
        EditorGUILayout.BeginVertical();
    }

    private void EndSection()
    {
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();
        EditorGUILayout.EndVertical();
    }

    private int IntFieldLeft(GUIContent label, int value)
    {
        EditorGUILayout.BeginHorizontal();
        var val = EditorGUILayout.IntField(value, GUILayout.Width(50));
        EditorGUILayout.LabelField(label);
        EditorGUILayout.EndHorizontal();
        return val;
    }

    public static int GetValue(string key)
    {
        var field = typeof(d4rkAvatarOptimizer.Settings).GetField(key, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (field == null)
        {
            throw new System.ArgumentException("Field " + key + " does not exist in d4rkAvatarOptimizer.Settings");
        }
        return EditorPrefs.GetInt(PrefsPrefix + key, field.FieldType == typeof(bool) ? (bool)field.GetValue(defaultSettings) ? 1 : 0 : (int)field.GetValue(defaultSettings));
    }

    public static void SetValue(string key, int value)
    {
        var field = typeof(d4rkAvatarOptimizer.Settings).GetField(key, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (field == null)
        {
            throw new System.ArgumentException("Field " + key + " does not exist in d4rkAvatarOptimizer.Settings");
        }
        EditorPrefs.SetInt(PrefsPrefix + key, value);
    }

    public static void ApplyDefaults(d4rkAvatarOptimizer optimizer)
    {
        var fields = typeof(d4rkAvatarOptimizer.Settings).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Where(f => f.FieldType == typeof(bool) || f.FieldType == typeof(int)).ToArray();
        foreach (var field in fields)
        {
            var val = GetValue(field.Name);
            field.SetValue(optimizer.settings, field.FieldType == typeof(bool) ? (object)(val != 0) : (object)val);
        }
    }
}
#endif