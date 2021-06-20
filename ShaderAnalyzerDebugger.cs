#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using d4rkpl4y3r;

public class ShaderAnalyzerDebugger : EditorWindow
{
    private Material mat = null;
    private ParsedShader parsedShader;
    private int maxLines = 5;
    private int maxProperties = 20;

    [MenuItem("Window/Shader Analyzer Debugger")]
    static void Init()
    {
        GetWindow(typeof(ShaderAnalyzerDebugger));
    }

    public void OnGUI()
    {
        mat = EditorGUILayout.ObjectField("Material", mat, typeof(Material), false) as Material;
        maxLines = EditorGUILayout.IntField("Max Lines", maxLines);
        maxProperties = EditorGUILayout.IntField("Max Properties", maxProperties);

        GUI.enabled = mat != null && mat.shader != null;

        if (GUILayout.Button("Analyze"))
        {
            parsedShader = ShaderAnalyzer.Parse(mat.shader);
        }

        if (GUILayout.Button("Optimize"))
        {
            parsedShader = ShaderAnalyzer.Parse(mat.shader);
            var replace = new Dictionary<string, string>();
            foreach (var prop in parsedShader.properties)
            {
                if (!mat.HasProperty(prop.name))
                    continue;
                if (prop.type == ParsedShader.Property.Type.Float)
                    replace[prop.name] = "" + mat.GetFloat(prop.name);
            }
            var shadur = ShaderAnalyzer.ReplacePropertysWithConstants(parsedShader, replace);

        }

        GUI.enabled = true;

        if (parsedShader == null)
            return;

        for (int i = 0; i < maxLines && i < parsedShader.lines.Count; i++)
        {
            GUILayout.Label(parsedShader.lines[i]);
        }

        GUILayout.Space(20);

        for (int i = 0; i < maxProperties && i < parsedShader.properties.Count; i++)
        {
            EditorGUILayout.LabelField(parsedShader.properties[i].name, "" + parsedShader.properties[i].type);
        }
    }
}
#endif