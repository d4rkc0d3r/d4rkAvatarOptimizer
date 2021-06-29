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

    private static string FuncToString(ParsedShader.Function func)
    {
        string s = "";
        s += func.parameters[0].type + " ";
        s += func.name + "(";
        for (int i = 1; i < func.parameters.Count; i++)
        {
            var param = func.parameters[i];
            s += i > 1 ? ", " : "";
            s += param.type;
            s += " ";
            s += param.name;
            s += param.arraySize > 0 ? "[" + param.arraySize + "]" : "";
            s += param.semantic == null ? "" : " : " + param.semantic;
        }
        s += ")";
        s += func.parameters[0].semantic == null ? "" : " : " + func.parameters[0].semantic;
        s += ";";
        return s;
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
                if (prop.type == ParsedShader.Property.Type.Int)
                    replace[prop.name] = "" + mat.GetInt(prop.name);
                if (prop.type == ParsedShader.Property.Type.Color)
                    replace[prop.name] = mat.GetColor(prop.name).ToString("F6").Replace("RGBA", "float4");
            }
            var shadur = ShaderOptimizer.Run(parsedShader, replace);
        }

        GUI.enabled = true;

        if (parsedShader == null)
            return;

        for (int i = 0; i < parsedShader.passes.Count; i++)
        {
            var pass = parsedShader.passes[i];
            GUILayout.Space(10);
            if (pass.vertex != null)
                GUILayout.Label("vertex: " + FuncToString(pass.vertex));
            if (pass.hull != null)
                GUILayout.Label("hull: " + FuncToString(pass.hull));
            if (pass.domain != null)
                GUILayout.Label("domain: " + FuncToString(pass.domain));
            if (pass.geometry != null)
                GUILayout.Label("geometry: " + FuncToString(pass.geometry));
            if (pass.fragment != null)
                GUILayout.Label("fragment: " + FuncToString(pass.fragment));
        }

        GUILayout.Space(20);

        for (int i = 0; i < maxProperties && i < parsedShader.properties.Count; i++)
        {
            var prop = parsedShader.properties[i];
            EditorGUILayout.LabelField(prop.name, "" + prop.type +
                (prop.shaderLabParams.Count > 0 ? " {" + string.Join(",", prop.shaderLabParams) + "}" : ""));
        }

        GUILayout.Space(20);

        for (int i = 0; i < maxLines && i < parsedShader.lines.Count; i++)
        {
            GUILayout.Label(parsedShader.lines[i]);
        }
    }
}
#endif