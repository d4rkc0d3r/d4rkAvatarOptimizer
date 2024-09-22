#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEditor;
using d4rkpl4y3r.AvatarOptimizer;
using System.Diagnostics;

public class ShaderAnalyzerDebugger : EditorWindow
{
    private Material material = null;
    private Shader shader = null;
    private ParsedShader parsedShader;
    private int maxLines = 20;
    private int maxProperties = 50;
    private int maxKeywords = 10;
    private bool showShaderLabParamsOnly = false;
    private long lastTime = 0;

    private bool showProperties = true;
    private bool showFailedIfex = true;

    private DefaultAsset folder = null;
    private List<Shader> shaders = null;
    private bool showMismatchedCurlyBraces = true;
    private bool showParseErrors = true;
    private bool showUnmergable = true;
    private bool showCustomTextureDeclarations = true;
    private bool showMultiIncludeFiles = true;
    private bool showErrorLess = true;
    private Vector2 scrollPos;
    private string propertyFilter = "";

    [MenuItem("Tools/d4rkpl4y3r/Shader Analyzer Debugger")]
    static void Init()
    {
        GetWindow(typeof(ShaderAnalyzerDebugger));
    }

    private bool Foldout(ref bool property, string name)
    {
        return property = EditorGUILayout.Foldout(property, name, true);
    }

    private bool ObjectField<T>(ref T obj, string label, bool allowSceneObjects = false) where T : Object
    {
        var result = EditorGUILayout.ObjectField(label, obj, typeof(T), allowSceneObjects) as T;
        if (result != obj)
        {
            obj = result;
            return true;
        }
        return false;
    }

    public void OnGUI()
    {
        if (ObjectField<Material>(ref material, "Material"))
        {
            shader = null;
            folder = null;
            lastTime = 0;
        }
        if (ObjectField<Shader>(ref shader, "Shader"))
        {
            material = null;
            folder = null;
            lastTime = 0;
        }
        if (ObjectField<DefaultAsset>(ref folder, "Folder"))
        {
            material = null;
            shader = null;
            if (folder != null)
            {
                var longPath = Path.GetFullPath(AssetDatabase.GetAssetPath(folder));
                var files = Directory.GetFiles(longPath, "*.shader", SearchOption.AllDirectories);
                int prefixLength = Application.dataPath.Length - "Assets".Length;
                shaders = new List<Shader>();
                foreach (var file in files.Select(s => s.Substring(prefixLength)))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<Shader>(file);
                    if (asset != null)
                        shaders.Add(asset);
                }
                lastTime = 0;
            }
        }

        GUILayout.Space(15);
        maxLines = EditorGUILayout.IntField("Max Lines", maxLines);
        maxProperties = EditorGUILayout.IntField("Max Properties", maxProperties);
        maxKeywords = EditorGUILayout.IntField("Max Keywords", maxKeywords);
        showShaderLabParamsOnly = EditorGUILayout.Toggle("Shader Lab Properties Only", showShaderLabParamsOnly);
        GUILayout.Space(15);

        if (GUILayout.Button("Clear Shader Cache"))
        {
            ShaderAnalyzer.ClearParsedShaderCache();
            lastTime = 0;
        }

        GUI.enabled = material != null && material.shader != null;

        if (GUILayout.Button("Optimize"))
        {
            ShaderAnalyzer.ClearParsedShaderCache();
            parsedShader = ShaderAnalyzer.Parse(material.shader);
            var replace = new Dictionary<string, string>();
            foreach (var prop in parsedShader.properties)
            {
                if (!material.HasProperty(prop.name))
                    continue;
                if (prop.type == ParsedShader.Property.Type.Float)
                    replace[prop.name] = "" + material.GetFloat(prop.name);
                if (prop.type == ParsedShader.Property.Type.Integer)
                    replace[prop.name] = "" + material.GetInteger(prop.name);
                if (prop.type == ParsedShader.Property.Type.Color)
                    replace[prop.name] = material.GetColor(prop.name).linear.ToString("F6").Replace("RGBA", "float4");
                if (prop.type == ParsedShader.Property.Type.ColorHDR)
                    replace[prop.name] = material.GetColor(prop.name).ToString("F6").Replace("RGBA", "float4");
            }
            var optimizeTimer = Stopwatch.StartNew();
            ShaderOptimizer.Run(parsedShader, replace);
            optimizeTimer.Stop();
            lastTime = optimizeTimer.ElapsedMilliseconds;
        }

        GUI.enabled = true;
        GUILayout.Space(15);

        Stopwatch timer;

        if (folder != null)
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            EditorGUILayout.LabelField("Total Shaders in Folder: " + shaders.Count);
            // time the parsing
            timer = Stopwatch.StartNew();
            var parsedShaders = ShaderAnalyzer.ParseAndCacheAllShaders(shaders, false);
            timer.Stop();
            if (lastTime == 0)
                lastTime = timer.ElapsedMilliseconds;
            EditorGUILayout.LabelField("Last Parse Time: " + lastTime + "ms");
            var mismatchedCurlyBraces = parsedShaders.Where(s => s.mismatchedCurlyBraces).ToList();
            if (Foldout(ref showMismatchedCurlyBraces, $"Mismatched Curly Braces ({mismatchedCurlyBraces.Count})"))
            {
                EditorGUI.indentLevel++;
                foreach (var shader in mismatchedCurlyBraces)
                {
                    EditorGUILayout.ObjectField(Shader.Find(shader.name), typeof(Shader), false);
                }
                EditorGUI.indentLevel--;
            }
            var parseErrors = parsedShaders.Where(s => !s.parsedCorrectly).ToList();
            void ShowShaderWithLabel(ParsedShader s, string label)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (label.Contains('\n'))
                        EditorGUILayout.LabelField(new GUIContent(label.Split('\n')[0], label));
                    else
                        EditorGUILayout.LabelField(label);
                    EditorGUILayout.ObjectField(Shader.Find(s.name), typeof(Shader), false);
                }
            }
            if (Foldout(ref showParseErrors, $"Parse Errors ({parseErrors.Count})"))
            {
                EditorGUI.indentLevel++;
                foreach (var shader in parseErrors)
                {
                    ShowShaderWithLabel(shader, shader.errorMessage);
                }
                EditorGUI.indentLevel--;
            }
            var unmergable = parsedShaders.Where(s => !s.CanMerge()).ToList();
            if (Foldout(ref showUnmergable, $"Unmergable ({unmergable.Count})"))
            {
                EditorGUI.indentLevel++;
                foreach (var shader in unmergable)
                {
                    ShowShaderWithLabel(shader, shader.CantMergeReason());
                }
                EditorGUI.indentLevel--;
            }
            var customTextureDeclarations = parsedShaders.Where(s => s.customTextureDeclarations.Count > 0).ToList();
            if (Foldout(ref showCustomTextureDeclarations, $"Custom Texture Declarations ({customTextureDeclarations.Count})"))
            {
                EditorGUI.indentLevel++;
                foreach (var shader in customTextureDeclarations)
                {
                    ShowShaderWithLabel(shader, $"Has {shader.customTextureDeclarations.Count} macros");
                    EditorGUI.indentLevel++;
                    foreach (var declaration in shader.customTextureDeclarations)
                    {
                        EditorGUILayout.LabelField(declaration);
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }
            var hasMultiIncludeFiles = parsedShaders.Where(s => s.multiIncludeFileCount.Values.Count(v => v > 1) > 0).ToList();
            if (Foldout(ref showMultiIncludeFiles, $"Multi Include Files ({hasMultiIncludeFiles.Count})"))
            {
                EditorGUI.indentLevel++;
                foreach (var shader in hasMultiIncludeFiles)
                {
                    ShowShaderWithLabel(shader, $"Has {shader.multiIncludeFileCount.Values.Count(v => v > 1)} multi include files");
                    EditorGUI.indentLevel++;
                    foreach (var file in shader.multiIncludeFileCount.Where(f => f.Value > 1))
                    {
                        var fileName = Path.GetFileName(file.Key);
                        EditorGUILayout.LabelField(new GUIContent($"{fileName}: {file.Value}", file.Key));
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }
            var errorLess = parsedShaders.Where(s => s.CanMerge() && !s.mismatchedCurlyBraces).ToList();
            if (Foldout(ref showErrorLess, $"Error Less ({errorLess.Count})"))
            {
                EditorGUI.indentLevel++;
                foreach (var shader in errorLess)
                {
                    ShowShaderWithLabel(shader, shader.name);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndScrollView();
            return;
        }

        timer = Stopwatch.StartNew();
        parsedShader = ShaderAnalyzer.Parse(material == null ? shader : material.shader);
        timer.Stop();
        if (lastTime == 0)
            lastTime = timer.ElapsedMilliseconds;
        EditorGUILayout.LabelField("Last Parse Time: " + lastTime + "ms");

        if (parsedShader == null)
            return;

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        GUILayout.Label(parsedShader.mismatchedCurlyBraces ? "Mismatched curly braces" : "No mismatched curly braces");

        GUILayout.Space(5);

        for (int i = 0; i < parsedShader.passes.Count; i++)
        {
            var pass = parsedShader.passes[i];
            GUILayout.Space(10);
            if (pass.vertex != null)
                GUILayout.Label("vertex: " + pass.vertex);
            if (pass.hull != null)
                GUILayout.Label("hull: " + pass.hull);
            if (pass.domain != null)
                GUILayout.Label("domain: " + pass.domain);
            if (pass.geometry != null)
                GUILayout.Label("geometry: " + pass.geometry);
            if (pass.fragment != null)
                GUILayout.Label("fragment: " + pass.fragment);
        }

        if (parsedShader.customTextureDeclarations.Count > 0)
        {
            GUILayout.Space(15);
            GUILayout.Label($"Has {parsedShader.customTextureDeclarations.Count} custom texture declaration macros:");
            EditorGUI.indentLevel++;
            foreach (var declaration in parsedShader.customTextureDeclarations)
            {
                EditorGUILayout.LabelField(declaration);
            }
            EditorGUI.indentLevel--;
        }

        if (parsedShader.shaderFeatureKeyWords.Count > 0 && material != null)
        {
            GUILayout.Space(15);
            GUILayout.Label("Shader keywords(" + parsedShader.shaderFeatureKeyWords.Count + "):");
            EditorGUI.indentLevel++;
            foreach (var keyword in parsedShader.shaderFeatureKeyWords.OrderBy(s => s))
            {
                EditorGUILayout.ToggleLeft(keyword, material.IsKeywordEnabled(keyword));
            }
            EditorGUI.indentLevel--;
        }

        GUILayout.Space(15);

        bool IsShownProperty(ParsedShader.Property prop)
        {
            if (showShaderLabParamsOnly && prop.shaderLabParams.Count == 0)
                return false;
            if (!string.IsNullOrEmpty(propertyFilter) && !prop.name.Contains(propertyFilter) && !(propertyFilter == "ifex" && parsedShader.ifexParameters.Contains(prop.name)))
                return false;
            return true;
        }

        if (Foldout(ref showProperties, $"Properties ({parsedShader.properties.Count(IsShownProperty)}/{parsedShader.properties.Count})"))
        {
            EditorGUI.indentLevel++;
            int shownProperties = 0;
            propertyFilter = EditorGUILayout.TextField("Property Filter", propertyFilter);
            for (int i = 0; shownProperties < maxProperties && i < parsedShader.properties.Count; i++)
            {
                var prop = parsedShader.properties[i];
                if (!IsShownProperty(prop))
                    continue;
                shownProperties++;
                EditorGUILayout.LabelField(prop.name, (prop.hasGammaTag ? "[gamma] " : "") + prop.type
                    + (prop.shaderLabParams.Count > 0 ? " {" + string.Join(",", prop.shaderLabParams) + "}" : "")
                    + " = " + prop.defaultValue);
            }
            EditorGUI.indentLevel--;
        }

        GUILayout.Space(15);

        if (Foldout(ref showFailedIfex, "Failed Ifex (" + parsedShader.unableToParseIfexStatements.Count + ")"))
        {
            EditorGUI.indentLevel++;
            foreach (var ifex in parsedShader.unableToParseIfexStatements)
            {
                EditorGUILayout.LabelField(ifex);
            }
            EditorGUI.indentLevel--;
        }

        GUILayout.Space(15);

        for (int i = 0; i < maxLines && i < parsedShader.text[".shader"].Count; i++)
        {
            GUILayout.Label(parsedShader.text[".shader"][i]);
        }

        EditorGUILayout.EndScrollView();
    }
}
#endif