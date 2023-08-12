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
            EditorGUILayout.BeginHorizontal();
            bool isDefault = true;
            if (field.FieldType == typeof(bool))
            {
                GUILayoutUtility.GetRect(35, 15, GUILayout.Width(35));
                var value = EditorGUILayout.Toggle(0 != GetValue(field.Name), GUILayout.Width(15)) ? 1 : 0;
                SetValue(field.Name, value);
                isDefault = field.GetValue(defaultSettings).Equals(value != 0);
            }
            else
            {
                var value = EditorGUILayout.Popup(GetValue(field.Name), new string[] { "Off", "On", "Auto" }, GUILayout.Width(50));
                SetValue(field.Name, value);
                isDefault = field.GetValue(defaultSettings).Equals(value);
            }
            EditorGUILayout.LabelField(d4rkAvatarOptimizer.GetDisplayName(field.Name) + (isDefault ? "" : " *"));
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.Space();
        EditorGUILayout.EndScrollView();
    }

    public static int GetValue(string key)
    {
        var field = typeof(d4rkAvatarOptimizer.Settings).GetField(key, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (field == null)
        {
            throw new System.ArgumentException("Field " + key + " does not exist in d4rkAvatarOptimizer.Settings");
        }
        if (!EditorPrefs.HasKey(PrefsPrefix + key))
        {
            EditorPrefs.SetInt(PrefsPrefix + key, field.FieldType == typeof(bool) ? (bool)field.GetValue(defaultSettings) ? 1 : 0 : (int)field.GetValue(defaultSettings));
        }
        return EditorPrefs.GetInt(PrefsPrefix + key);
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

    public static bool IsAutoSetting(string key)
    {
        return GetValue(key) == 2;
    }
}
#endif