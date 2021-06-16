#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using d4rkpl4y3r;

public class ShaderAnalyzerDebugger : EditorWindow
{
    private Material mat;
    private ShaderAnalyzer analyzer = new ShaderAnalyzer();
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
        analyzer.shader = mat?.shader;

        if (GUILayout.Button("Analyze"))
        {
            analyzer.Parse();
        }

        for (int i = 0; i < maxLines && i < analyzer.processedLines.Count; i++)
        {
            GUILayout.Label(analyzer.processedLines[i]);
        }

        GUILayout.Space(20);

        for (int i = 0; i < maxProperties && i < analyzer.properties.Count; i++)
        {
            GUILayout.Label(analyzer.properties[i]);
        }
    }
}
#endif