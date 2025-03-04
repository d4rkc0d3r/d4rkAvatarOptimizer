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

    private class SectionScope : GUI.Scope
    {
        public SectionScope(string title)
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            GUILayoutUtility.GetRect(15, 15, GUILayout.Width(15));
            EditorGUILayout.BeginVertical();
        }
        protected override void CloseScope()
        {
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
            EditorGUILayout.EndVertical();
        }
    }

    public void OnGUI()
    {
        using (var scrollView = new EditorGUILayout.ScrollViewScope(scrollPos))
        {
            scrollPos = scrollView.scrollPosition;

            using (new SectionScope("Global Settings"))
            {
                DoOptimizeWithDefaultSettingsWhenNoComponent = BoolFieldLeft(
                    new GUIContent("Always Optimize on Upload", "If an Avatar does not have a d4rkAvatarOptimizer component attached, it will be optimized with the default settings below when uploading it to VRChat."),
                    DoOptimizeWithDefaultSettingsWhenNoComponent);
                AutoRefreshPreviewTimeout = IntFieldLeft(
                    new GUIContent("Auto Refresh Preview Timeout", "In milliseconds. If the preview takes longer than this to refresh, the auto refresh will be disabled."),
                    AutoRefreshPreviewTimeout);
                MotionTimeApproximationSampleCount = IntFieldLeft(
                    new GUIContent("Motion Time Approximation Sample Count", "The amount of samples used to approximate motion time states. Higher values are more accurate but generate more animation clips."),
                    MotionTimeApproximationSampleCount);
            }

            EditorGUILayout.Space();

            using (new SectionScope("Default Settings"))
            {
                EditorGUILayout.HelpBox("These settings are the default values when adding the Avatar Optimizer component to a model."
                    + "You can change them here to suit your needs."
                    + "\nA * indicates settings that are different from their default value.", MessageType.None);
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
                        var value = BoolFieldLeft(
                            new GUIContent(d4rkAvatarOptimizer.GetDisplayName(field.Name) +
                                           (field.GetValue(defaultSettings).Equals(0 != GetValue(field.Name)) ? "" : " *")),
                            0 != GetValue(field.Name));
                        SetValue(field.Name, value ? 1 : 0);
                    }
                    else
                    {
                        var value = PopupFieldLeft(
                            new GUIContent(d4rkAvatarOptimizer.GetDisplayName(field.Name) +
                                           (field.GetValue(defaultSettings).Equals(GetValue(field.Name)) ? "" : " *")),
                            GetValue(field.Name),
                            new string[] { "Off", "On", "Auto" });
                        SetValue(field.Name, value);
                    }
                }
            }
        }
    }

    private int IntFieldLeft(GUIContent label, int value)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            var val = EditorGUILayout.IntField(value, GUILayout.Width(50));
            var rect = GUILayoutUtility.GetRect(label, EditorStyles.label);
            rect.xMin -= 2;
            GUI.Label(rect, label);
            EditorGUILayout.Space();
            return val;
        }
    }

    private bool BoolFieldLeft(GUIContent label, bool value)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayoutUtility.GetRect(35, 15, GUILayout.Width(35));
            return EditorGUILayout.ToggleLeft(label, value);
        }
    }

    private int PopupFieldLeft(GUIContent label, int value, string[] options)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            var val = EditorGUILayout.Popup(value, options, GUILayout.Width(50));
            var rect = GUILayoutUtility.GetRect(label, EditorStyles.label);
            rect.xMin -= 2;
            GUI.Label(rect, label);
            EditorGUILayout.Space();
            return val;
        }
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