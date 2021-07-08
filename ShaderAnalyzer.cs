#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;
using System.Text;
using System.Globalization;
using System.Linq;
using d4rkpl4y3r.Util;

namespace d4rkpl4y3r
{
    public class ParsedShader
    {
        public class Property
        {
            public enum Type
            {
                Unknown,
                Color,
                Float,
                Float4,
                Int,
                Int4,
                Texture2D,
                Texture2DArray
            }
            public string name;
            public Type type = Type.Unknown;
            public List<string> shaderLabParams = new List<string>();
        }
        public class Function
        {
            public class Parameter
            {
                public string type;
                public string name;
                public string semantic;
                public bool isOutput = false;
                public bool isInput = false;
                public int arraySize = 0;
            }
            public string name;
            public List<Parameter> parameters = new List<Parameter>();
        }
        public class Pass
        {
            public Function vertex;
            public Function hull;
            public Function domain;
            public Function geometry;
            public Function fragment;
        }
        public string name;
        public bool couldParse = true;
        public List<string> lines = new List<string>();
        public List<Property> properties = new List<Property>();
        public List<Pass> passes = new List<Pass>();
        public Dictionary<string, Function> functions = new Dictionary<string, Function>();
    }

    public static class ShaderAnalyzer
    {
        private enum ParseState
        {
            Init,
            PropertyBlock,
            ShaderLab,
            CGInclude,
            CGProgram
        }

        private static Dictionary<string, ParsedShader> parsedShaderCache = new Dictionary<string, ParsedShader>();

        public static void ClearParsedShaderCache()
        {
            parsedShaderCache.Clear();
        }

        public static ParsedShader Parse(Shader shader)
        {
            if (shader == null)
                return null;
            if (!parsedShaderCache.TryGetValue(shader.name, out var parsedShader))
            {
                maxIncludes = 50;
                parsedShader = new ParsedShader();
                parsedShader.name = shader.name;
                Profiler.StartSection("ShaderAnalyzer.RecursiveParseFile()");
                try
                {
                    RecursiveParseFile(AssetDatabase.GetAssetPath(shader), parsedShader.lines);
                }
                catch (IOException)
                {
                    parsedShader.couldParse = false;
                }
                Profiler.StartNextSection("ShaderAnalyzer.SemanticParseShader()");
                try
                {
                    SemanticParseShader(parsedShader);
                }
                catch (System.Exception e)
                {
                    parsedShader.couldParse = false;
                    Debug.LogWarning(e);
                }
                Profiler.EndSection();
                parsedShaderCache[shader.name] = parsedShader;
            }
            return parsedShader;
        }

        private static int FindEndOfStringLiteral(string text, int startIndex)
        {
            for (int i = startIndex; i < text.Length; i++)
            {
                if (text[i] == '\\')
                {
                    i++;
                }
                else if (text[i] == '"')
                {
                    return i;
                }
            }
            return -1;
        }

        private static int maxIncludes = 50;
        private static bool RecursiveParseFile(string filePath, List<string> processedLines, List<string> alreadyIncludedFiles = null)
        {
            bool isTopLevelFile = false;
            if (alreadyIncludedFiles == null)
            {
                alreadyIncludedFiles = new List<string>();
                isTopLevelFile = true;
            }
            if (--maxIncludes < 0)
            {
                Debug.LogError("Reach max include depth");
                return false;
            }
            filePath = Path.GetFullPath(filePath);
            if (alreadyIncludedFiles.Contains(filePath))
            {
                return true;
            }
            alreadyIncludedFiles.Add(filePath);
            string[] rawLines = null;
            try
            {
                rawLines = File.ReadAllLines(filePath);
            }
            catch (FileNotFoundException)
            {
                return false; //this is probably a unity include file
            }

            for (int lineIndex = 0; lineIndex < rawLines.Length; lineIndex++)
            {
                string trimmedLine = rawLines[lineIndex].Trim();
                if (trimmedLine == "")
                    continue;
                for (int i = 0; i < trimmedLine.Length - 1; i++)
                {
                    if (trimmedLine[i] == '"')
                    {
                        int end = FindEndOfStringLiteral(trimmedLine, i + 1);
                        i = (end == -1) ? trimmedLine.Length : end;
                        continue;
                    }
                    else if (trimmedLine[i] != '/')
                    {
                        continue;
                    }
                    if (trimmedLine[i + 1] == '/')
                    {
                        trimmedLine = trimmedLine.Substring(0, i).TrimEnd();
                        break;
                    }
                    else if (trimmedLine[i + 1] == '*')
                    {
                        int endCommentBlock = trimmedLine.IndexOf("*/", i + 2);
                        bool isMultiLineCommentBlock = endCommentBlock == -1;
                        while (endCommentBlock == -1 && ++lineIndex < rawLines.Length)
                        {
                            endCommentBlock = rawLines[lineIndex].IndexOf("*/");
                        }
                        if (endCommentBlock != -1)
                        {
                            if (isMultiLineCommentBlock)
                            {
                                trimmedLine = trimmedLine.Substring(0, i)
                                    + rawLines[lineIndex].Substring(endCommentBlock + 2);
                            }
                            else
                            {
                                trimmedLine = trimmedLine.Substring(0, i)
                                    + trimmedLine.Substring(endCommentBlock + 2);
                            }
                            i -= 1;
                        }
                        else
                        {
                            trimmedLine = trimmedLine.Substring(0, i).TrimEnd();
                            break;
                        }
                    }
                }
                if (trimmedLine == "")
                    continue;
                if (isTopLevelFile && (trimmedLine == "CGINCLUDE" || trimmedLine == "CGPROGRAM"))
                {
                    alreadyIncludedFiles.Clear();
                }
                if (trimmedLine.StartsWith("#include "))
                {
                    int firstQuote = trimmedLine.IndexOf('"');
                    int lastQuote = trimmedLine.LastIndexOf('"');
                    string includePath = trimmedLine.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
                    includePath = Path.GetDirectoryName(filePath) + "/" + includePath;
                    if (!RecursiveParseFile(includePath, processedLines, alreadyIncludedFiles))
                    {
                        processedLines.Add(trimmedLine);
                    }
                    continue;
                }
                if (trimmedLine.EndsWith("{"))
                {
                    trimmedLine = trimmedLine.Substring(0, trimmedLine.Length - 1).TrimEnd();
                    if (trimmedLine != "")
                        processedLines.Add(trimmedLine);
                    processedLines.Add("{");
                    continue;
                }
                processedLines.Add(trimmedLine);
            }
            return true;
        }

        public static ParsedShader.Property ParseProperty(string line)
        {
            string modifiedLine = line;
            int openBracketIndex = line.IndexOf('[');
            while (openBracketIndex != -1)
            {
                int closeBracketIndex = modifiedLine.IndexOf(']') + 1;
                if (closeBracketIndex != 0)
                {
                    modifiedLine = modifiedLine.Substring(0, openBracketIndex)
                        + modifiedLine.Substring(closeBracketIndex);
                    openBracketIndex = modifiedLine.IndexOf('[');
                }
                else
                {
                    break;
                }
            }
            modifiedLine = modifiedLine.Trim();
            int parentheses = modifiedLine.IndexOf('(');
            if (parentheses != -1)
            {
                var output = new ParsedShader.Property();
                output.name = modifiedLine.Substring(0, parentheses).TrimEnd();
                int quoteIndex = modifiedLine.IndexOf('"', parentheses);
                quoteIndex = FindEndOfStringLiteral(modifiedLine, quoteIndex + 1);
                int colonIndex = modifiedLine.IndexOf(',', quoteIndex + 1);
                modifiedLine = modifiedLine.Substring(colonIndex + 1).TrimStart();
                if (modifiedLine.StartsWith("Range") || modifiedLine.StartsWith("Float"))
                {
                    output.type = ParsedShader.Property.Type.Float;
                }
                else if (modifiedLine.StartsWith("Int"))
                {
                    output.type = ParsedShader.Property.Type.Int;
                }
                else if (modifiedLine.StartsWith("Color"))
                {
                    output.type = ParsedShader.Property.Type.Color;
                }
                else if (modifiedLine.StartsWith("2DArray"))
                {
                    output.type = ParsedShader.Property.Type.Texture2DArray;
                }
                else if (modifiedLine.StartsWith("2D"))
                {
                    output.type = ParsedShader.Property.Type.Texture2D;
                }
                return output;
            }
            return null;
        }

        public static (string name, string returnType) ParseFunctionDefinition(string line)
        {
            var match = Regex.Match(line, @"^(inline\s+)?(\w+)\s+(\w+)\s*\(");
            if (match.Success && match.Groups[2].Value != "return" && match.Groups[2].Value != "else")
            {
                return (match.Groups[3].Value, match.Groups[2].Value);
            }
            return (null, null);
        }

        public static ParsedShader.Function ParseFunctionDefinition(List<string> source, ref int lineIndex)
        {
            var match = Regex.Match(source[lineIndex], @"^(inline\s+)?(\w+)\s+(\w+)\s*\(");
            if (match.Success && match.Groups[2].Value != "return" && match.Groups[2].Value != "else")
            {
                var func = new ParsedShader.Function();
                func.name = match.Groups[3].Value;
                var returnParam = new ParsedShader.Function.Parameter();
                returnParam.isOutput = true;
                returnParam.name = "return";
                returnParam.type = match.Groups[2].Value;
                func.parameters.Add(returnParam);
                string line = source[lineIndex].Substring(source[lineIndex].IndexOf('(') + 1);
                while (lineIndex < source.Count - 1)
                {
                    var matches = Regex.Matches(line, @"((in|out|inout)\s+)?((const|point|line|triangle)\s+)?(\w+(<[\w,\s]+>)?)\s+((\w+)(\[(\d+)\])?)(\s*:\s*(\w+))?");
                    foreach (Match m in matches)
                    {
                        var inout = m.Groups[2].Value;
                        var geomType = m.Groups[4].Value;
                        var param = new ParsedShader.Function.Parameter();
                        param.type = m.Groups[5].Value;
                        param.name = m.Groups[8].Value;
                        param.arraySize = m.Groups[10].Value != "" ? int.Parse(m.Groups[10].Value) : 0;
                        param.semantic = m.Groups[12].Value != "" ? m.Groups[12].Value : null;
                        param.isInput = inout != "out";
                        param.isOutput = inout == "out" || inout == "inout";
                        func.parameters.Add(param);
                    }
                    while (source[lineIndex + 1].StartsWith("#"))
                    {
                        lineIndex++;
                    }
                    if (source[lineIndex + 1] == "{" || line.EndsWith(";"))
                    {
                        var m = Regex.Match(line, @"\)\s*:\s*(\w+)");
                        if (m.Success)
                        {
                            returnParam.semantic = m.Groups[1].Value;
                        }
                        break;
                    }
                    line = source[++lineIndex];
                }
                return func;
            }
            return null;
        }

        private static void UpdateFunctionDefinition(ParsedShader.Function func, ParsedShader.Pass pass)
        {
            if (func.name == pass.vertex?.name)
                pass.vertex = func;
            if (func.name == pass.hull?.name)
                pass.hull = func;
            if (func.name == pass.domain?.name)
                pass.domain = func;
            if (func.name == pass.geometry?.name)
                pass.geometry = func;
            if (func.name == pass.fragment?.name)
                pass.fragment = func;
        }

        private static void ParsePragma(string line, ParsedShader.Pass pass, ParsedShader parsedShader)
        {
            if (!line.StartsWith("#pragma "))
                return;
            line = line.Substring("#pragma ".Length);
            var match = Regex.Match(line, @"(vertex|hull|domain|geometry|fragment)\s+(\w+)");
            if (!match.Success)
                return;
            var funcName = match.Groups[2].Value;
            parsedShader.functions.TryGetValue(funcName, out ParsedShader.Function func);
            func = func ?? new ParsedShader.Function() { name = funcName };
            switch (match.Groups[1].Value)
            {
                case "vertex":
                    pass.vertex = func;
                    return;
                case "hull":
                    pass.hull = func;
                    return;
                case "domain":
                    pass.domain = func;
                    return;
                case "geometry":
                    pass.geometry = func;
                    return;
                case "fragment":
                    pass.fragment = func;
                    return;
            }
        }

        private static void SemanticParseShader(ParsedShader parsedShader)
        {
            var cgIncludePragmas = new ParsedShader.Pass();
            ParsedShader.Pass currentPass = null;
            var state = ParseState.Init;
            for (int lineIndex = 0; lineIndex < parsedShader.lines.Count; lineIndex++)
            {
                string line = parsedShader.lines[lineIndex];
                switch (state)
                {
                    case ParseState.Init:
                        if (line == "Properties")
                        {
                            state = ParseState.PropertyBlock;
                        }
                        break;
                    case ParseState.PropertyBlock:
                        if (line == "}")
                        {
                            state = ParseState.ShaderLab;
                        }
                        else
                        {
                            var property = ParseProperty(line);
                            if (property != null)
                            {
                                parsedShader.properties.Add(property);
                            }
                        }
                        break;
                    case ParseState.ShaderLab:
                        if (line == "CGINCLUDE")
                        {
                            state = ParseState.CGInclude;
                            currentPass = cgIncludePragmas;
                        }
                        else if (line == "CGPROGRAM")
                        {
                            state = ParseState.CGProgram;
                            currentPass = new ParsedShader.Pass();
                            currentPass.vertex = cgIncludePragmas.vertex;
                            currentPass.hull = cgIncludePragmas.hull;
                            currentPass.domain = cgIncludePragmas.domain;
                            currentPass.geometry = cgIncludePragmas.geometry;
                            currentPass.fragment = cgIncludePragmas.fragment;
                            parsedShader.passes.Add(currentPass);
                        }
                        else
                        {
                            var matches = Regex.Matches(line, @"\[[_a-zA-Z0-9]+\]");
                            if (matches.Count > 0)
                            {
                                string shaderLabParam = Regex.Match(line, @"^[_a-zA-Z]+").Captures[0].Value;
                                foreach (Match match in matches)
                                {
                                    string propName = match.Value.Substring(1, match.Value.Length - 2);
                                    foreach (var prop in parsedShader.properties)
                                    {
                                        if (propName == prop.name)
                                        {
                                            prop.shaderLabParams.Add(shaderLabParam);
                                        }
                                    }
                                }
                            }
                        }
                        break;
                    case ParseState.CGInclude:
                    case ParseState.CGProgram:
                        if (line == "ENDCG")
                        {
                            state = ParseState.ShaderLab;
                        }
                        ParsePragma(line, currentPass, parsedShader);
                        var func = ParseFunctionDefinition(parsedShader.lines, ref lineIndex);
                        if (func != null)
                        {
                            parsedShader.functions[func.name] = func;
                            UpdateFunctionDefinition(func, currentPass);
                        }
                        break;
                }
            }
        }
    }

    public class ShaderOptimizer
    {
        private enum ParseState
        {
            Init,
            PropertyBlock,
            ShaderLab,
            CGInclude,
            CGProgram
        }

        private List<string> output;
        private ParsedShader parsedShader;
        private int meshToggleCount;
        private Dictionary<string, string> staticPropertyValues;
        private Dictionary<string, (string type, List<string> values)> arrayPropertyValues;
        private Dictionary<string, string> texturesToNullCheck;
        private HashSet<string> texturesToMerge;
        private HashSet<string> texturesToReplaceCalls;
        private string vertexInUv0Member;

        private ShaderOptimizer() {}

        public static List<string> Run(ParsedShader source,
            Dictionary<string, string> staticPropertyValues = null,
            int meshToggleCount = 0,
            Dictionary<string, (string type, List<string> values)> arrayPropertyValues = null,
            Dictionary<string, string> texturesToNullCheck = null,
            HashSet<string> texturesToMerge = null)
        {
            var optimizer = new ShaderOptimizer
            {
                meshToggleCount = meshToggleCount,
                staticPropertyValues = staticPropertyValues ?? new Dictionary<string, string>(),
                arrayPropertyValues = arrayPropertyValues ?? new Dictionary<string, (string type, List<string> values)>(),
                parsedShader = source,
                texturesToNullCheck = texturesToNullCheck ?? new Dictionary<string, string>(),
                texturesToMerge = texturesToMerge ?? new HashSet<string>()
            };
            optimizer.texturesToReplaceCalls = new HashSet<string>(
                optimizer.texturesToMerge.Union(optimizer.texturesToNullCheck.Keys));
            return optimizer.Run();
        }

        private void InjectArrayPropertyInitialization()
        {
            foreach (var arrayProperty in arrayPropertyValues)
            {
                string name = "d4rkAvatarOptimizerArray" + arrayProperty.Key;
                output.Add(arrayProperty.Key + " = " + name + "[d4rkAvatarOptimizer_MaterialID];");
            }
        }

        private string ReplaceTextureSamples(string line)
        {
            if (texturesToReplaceCalls.Count == 0)
                return line;
            line = Regex.Replace(line, @"tex2D(\w*)\s*\(\s*(\w+)\s*,", match => 
                texturesToReplaceCalls.Contains(match.Groups[2].Value)
                ? "tex2D" + match.Groups[1].Value + match.Groups[2].Value + "("
                : match.Value);
            line = Regex.Replace(line, @"(\w+)\s*\.\s*Sample(\w*)\s*\(", match => 
                texturesToReplaceCalls.Contains(match.Groups[1].Value)
                ? match.Groups[1].Value + "Sample" + match.Groups[2].Value + "("
                : match.Value);
            return line;
        }

        private void InjectVertexShaderCode(
            List<string> source,
            ref int sourceLineIndex,
            ParsedShader.Pass pass)
        {
            string line = source[sourceLineIndex];
            var outParam = pass.vertex.parameters.FirstOrDefault(p => p.isOutput && p.type != "void");
            var inParam = pass.vertex.parameters.FirstOrDefault(p => p.isInput && p.semantic == null);
            var isVoidReturn = pass.vertex.parameters[0].type == "void";
            output.Add(line.Substring(0, line.IndexOf('(') + 1));
            string uv0Name = "d4rkAvatarOptimizer_UV0";
            if (inParam == null)
            {
                output.Add("float4 d4rkAvatarOptimizer_UV0 : TEXCOORD0" + (pass.vertex.parameters.Count > 1 ? "," : ""));
            }
            else
            {
                uv0Name = inParam.name + "." + vertexInUv0Member;
            }
            output.Add(line.Substring(line.IndexOf('(') + 1));
            while (line != "{" && sourceLineIndex < source.Count - 1)
            {
                line = source[++sourceLineIndex];
                output.Add(line);
            }
            if (meshToggleCount > 1)
            {
                output.Add("if (d4rkAvatarOptimizer_Zero)");
                output.Add("{");
                string val = "float val = _IsActiveMesh0";
                for (int i = 1; i < meshToggleCount; i++)
                {
                    val += " + _IsActiveMesh" + i;
                }
                output.Add(val + ";");
                output.Add(isVoidReturn
                    ? "if (val) { " + outParam.name + " = (" + outParam.type + ")0; return; }"
                    : "if (val) return (" + outParam.type + ")0;");
                output.Add("}");
                output.Add("if (!_IsActiveMesh[" + uv0Name + ".z])");
                output.Add("{");
                output.Add(isVoidReturn
                    ? outParam.name + " = (" + outParam.type + ")0;return;"
                    : "return (" + outParam.type + ")0;");
                output.Add("}");
            }
            sourceLineIndex++;
            if (arrayPropertyValues.Count > 0)
            {
                output.Add("d4rkAvatarOptimizer_MaterialID = " + uv0Name + ".w;");
                InjectArrayPropertyInitialization();
            }
            int braceDepth = 0;
            for (; sourceLineIndex < source.Count; sourceLineIndex++)
            {
                line = source[sourceLineIndex];
                if (line == "}")
                {
                    output.Add(line);
                    if (braceDepth-- == 0)
                    {
                        break;
                    }
                }
                else if (line == "{")
                {
                    output.Add(line);
                    braceDepth++;
                }
                else if (line.StartsWith("return"))
                {
                    output.Add("{");
                    if (isVoidReturn)
                    {
                        if (arrayPropertyValues.Count > 0)
                            output.Add(outParam.name + ".d4rkAvatarOptimizer_MaterialID = d4rkAvatarOptimizer_MaterialID;");
                        if (pass.geometry != null && meshToggleCount > 1)
                            output.Add(outParam.name + ".d4rkAvatarOptimizer_NotCullVert = true;");
                        output.Add("return;");
                    }
                    else
                    {
                        output.Add(outParam.type + " d4rkAvatarOptimizer_vertexOutput = " + line.Substring("return ".Length));
                        if (arrayPropertyValues.Count > 0)
                            output.Add("d4rkAvatarOptimizer_vertexOutput.d4rkAvatarOptimizer_MaterialID = d4rkAvatarOptimizer_MaterialID;");
                        if (pass.geometry != null && meshToggleCount > 1)
                            output.Add("d4rkAvatarOptimizer_vertexOutput.d4rkAvatarOptimizer_NotCullVert = true;");
                        output.Add("return d4rkAvatarOptimizer_vertexOutput;");
                    }
                    output.Add("}");
                }
                else
                {
                    output.Add(ReplaceTextureSamples(line));
                }
            }
        }

        private void InjectGeometryShaderCode(
            List<string> source,
            ref int sourceLineIndex,
            ParsedShader.Pass pass)
        {
            string line = source[sourceLineIndex];
            output.Add(line);
            while (line != "{" && sourceLineIndex < source.Count - 1)
            {
                line = source[++sourceLineIndex];
                output.Add(line);
            }
            var inParam = pass.geometry.parameters.FirstOrDefault(p => p.isInput && p.arraySize > 0);
            if (meshToggleCount > 1)
            {
                output.Add("if (!" + inParam.name + "[0].d4rkAvatarOptimizer_NotCullVert)");
                output.Add("{");
                output.Add("return;");
                output.Add("}");
            }
            var outParam = pass.geometry.parameters.FirstOrDefault(p => p.type.Contains("Stream<"));
            var outParamType = outParam.type.Substring(outParam.type.IndexOf('<') + 1);
            outParamType = outParamType.Substring(0, outParamType.Length - 1);
            if (arrayPropertyValues.Count == 0 && (meshToggleCount <= 1 || outParamType != inParam.type))
            {
                return;
            }
            sourceLineIndex++;
            if (arrayPropertyValues.Count > 1)
            {
                output.Add("d4rkAvatarOptimizer_MaterialID = " + inParam.name + "[0].d4rkAvatarOptimizer_MaterialID;");
                InjectArrayPropertyInitialization();
            }
            int braceDepth = 0;
            for (; sourceLineIndex < source.Count; sourceLineIndex++)
            {
                line = source[sourceLineIndex];
                if (line == "}")
                {
                    output.Add(line);
                    if (braceDepth-- == 0)
                    {
                        return;
                    }
                }
                else if (line == "{")
                {
                    output.Add(line);
                    braceDepth++;
                }
                else if (line.Contains(outParam.name + ".Append("))
                {
                    output.Add("{");
                    output.Add(outParamType + " d4rkAvatarOptimizer_geomOutput = " + line.Substring(line.IndexOf(".Append(") + 7));
                    if (arrayPropertyValues.Count > 1)
                        output.Add("d4rkAvatarOptimizer_geomOutput.d4rkAvatarOptimizer_MaterialID = d4rkAvatarOptimizer_MaterialID;");
                    if (meshToggleCount > 1 && outParamType == inParam.type)
                        output.Add("d4rkAvatarOptimizer_geomOutput.d4rkAvatarOptimizer_NotCullVert = 1;");
                    output.Add(outParam.name + ".Append(d4rkAvatarOptimizer_geomOutput);");
                    output.Add("}");
                }
                else
                {
                    output.Add(ReplaceTextureSamples(line));
                }
            }
        }

        private void InjectFragmentShaderCode(
            List<string> source,
            ref int sourceLineIndex,
            ParsedShader.Pass pass)
        {
            string line = source[sourceLineIndex];
            output.Add(line);
            while (line != "{" && sourceLineIndex < source.Count - 1)
            {
                line = source[++sourceLineIndex];
                output.Add(line);
            }
            if (arrayPropertyValues.Count == 0)
            {
                return;
            }
            var fragIn = pass.fragment.parameters.FirstOrDefault(p => p.isInput && p.semantic == null);
            output.Add("d4rkAvatarOptimizer_MaterialID = " + fragIn.name + ".d4rkAvatarOptimizer_MaterialID;");
            InjectArrayPropertyInitialization();
        }

        private void InjectPropertyArrays()
        {
            output.Add("uniform float d4rkAvatarOptimizer_Zero;");
            if (arrayPropertyValues.Count > 0)
            {
                output.Add("static uint d4rkAvatarOptimizer_MaterialID = 0;");
                foreach (var arrayProperty in arrayPropertyValues)
                {
                    var (type, values) = arrayProperty.Value;
                    string name = "d4rkAvatarOptimizerArray" + arrayProperty.Key;
                    output.Add("static const " + type + " " + name + "[" + values.Count + "] = ");
                    output.Add("{");
                    for (int i = 0; i < values.Count; i++)
                    {
                        output.Add(values[i] + ",");
                    }
                    output.Add("};");
                    output.Add("static " + type + " " + arrayProperty.Key + ";");
                }
            }
            foreach (var property in staticPropertyValues)
            {
                if (property.Key.StartsWith("arrayIndex") && texturesToMerge.Contains(property.Key.Substring(10)))
                {
                    output.Add("static int " + property.Key + " = " + property.Value + ";");
                }
            }
            if (meshToggleCount > 1)
            {
                output.Add("cbuffer d4rkAvatarOptimizer_MeshToggles");
                output.Add("{");
                output.Add("float _IsActiveMesh[" + meshToggleCount + "] : packoffset(c0);");
                for (int i = 0; i < meshToggleCount; i++)
                {
                    output.Add("float _IsActiveMesh" + i + " : packoffset(c" + i + ");");
                }
                output.Add("};");
            }
            foreach (var texName in texturesToReplaceCalls)
            {
                if (texturesToNullCheck.TryGetValue(texName, out string nullCheck))
                {
                    nullCheck = "if (!shouldSample" + texName + ") return " + nullCheck + ";";
                }

                bool isArray = texturesToMerge.Contains(texName);
                string uv = isArray ? "float3(uv, arrayIndex" + texName + ")" : "uv";

                output.Add("uniform Texture2D" + (isArray ? "Array " : " ") + texName + ";");
                output.Add("uniform SamplerState sampler" + texName + ";");

                output.Add("float4 " + texName + "Sample(SamplerState sampl, float2 uv) {");
                if (nullCheck != null) output.Add(nullCheck);
                output.Add("return " + texName + ".Sample(sampl, " + uv + ");}");

                output.Add("float4 " + texName + "SampleGrad(SamplerState sampl, float2 uv, float2 ddxuv, float2 ddyuv) {");
                if (nullCheck != null) output.Add(nullCheck);
                output.Add("return " + texName + ".SampleGrad(sampl, " + uv + ", ddxuv, ddyuv);}");

                output.Add("float4 " + texName + "SampleLevel(SamplerState sampl, float2 uv, int mipLevel) {");
                if (nullCheck != null) output.Add(nullCheck);
                output.Add("return " + texName + ".SampleLevel(sampl, " + uv + ", mipLevel);}");

                output.Add("float4 " + texName + "SampleBias(SamplerState sampl, float2 uv, float bias) {");
                if (nullCheck != null) output.Add(nullCheck);
                output.Add("return " + texName + ".SampleBias(sampl, " + uv + ", bias);}");

                output.Add("float4 tex2D" + texName + "(float2 uv) {");
                output.Add("return " + texName + "Sample(sampler" + texName + ", uv);}");

                output.Add("float4 tex2Dproj" + texName + "(float4 uv) {");
                output.Add("return " + texName + "Sample(sampler" + texName + ", uv.xy / uv.w);}");

                output.Add("float4 tex2D" + texName + "(float2 uv, float2 ddxuv, float2 ddyuv) {");
                output.Add("return " + texName + "SampleGrad(sampler" + texName + ", uv, ddxuv, ddyuv);}");

                output.Add("float4 tex2Dgrad" + texName + "(float2 uv, float2 ddxuv, float2 ddyuv) {");
                output.Add("return " + texName + "SampleGrad(sampler" + texName + ", uv, ddxuv, ddyuv);}");

                output.Add("float4 tex2Dlod" + texName + "(float4 uv) {");
                output.Add("return " + texName + "SampleLevel(sampler" + texName + ", uv.xy, uv.w);}");

                output.Add("float4 tex2Dbias" + texName + "(float4 uv) {");
                output.Add("return " + texName + "SampleBias(sampler" + texName + ", uv.xy, uv.w);}");
            }
        }

        private void ParseCodeLines(List<string> source, ref int sourceLineIndex, ParsedShader.Pass pass)
        {
            var line = source[sourceLineIndex];
            var func = (arrayPropertyValues.Count > 0 || meshToggleCount > 1) ? ShaderAnalyzer.ParseFunctionDefinition(line) : (null, null);
            if (pass.vertex != null && func.name == pass.vertex.name)
            {
                InjectVertexShaderCode(source, ref sourceLineIndex, pass);
            }
            else if (pass.geometry != null && pass.fragment != null && pass.vertex != null && func.name == pass.geometry.name)
            {
                InjectGeometryShaderCode(source, ref sourceLineIndex, pass);
            }
            else if (pass.vertex != null && pass.fragment != null && func.name == pass.fragment.name)
            {
                InjectFragmentShaderCode(source, ref sourceLineIndex, pass);
            }
            else if ((arrayPropertyValues.Count > 0 || meshToggleCount > 1) && line.StartsWith("struct "))
            {
                output.Add(line);
                var match = Regex.Match(line, @"struct\s+(\w+)");
                if (match.Success)
                {
                    var structName = match.Groups[1].Value;
                    if (source[sourceLineIndex + 1] == "{")
                    {
                        var vertOut = pass.vertex.parameters.FirstOrDefault(p => p.isOutput && p.type != "void");
                        if (structName == vertOut.type)
                        {
                            sourceLineIndex++;
                            output.Add("{");
                            if (arrayPropertyValues.Count > 0)
                                output.Add("uint d4rkAvatarOptimizer_MaterialID : d4rkAvatarOptimizer_MATERIAL_ID;");
                            if (pass.geometry != null && meshToggleCount > 1)
                                output.Add("bool d4rkAvatarOptimizer_NotCullVert : d4rkAvatarOptimizer_NotCullVert;");
                        }
                        else if (pass.geometry != null)
                        {
                            var gsOut = pass.geometry.parameters.FirstOrDefault(p => p.type.Contains("Stream<"));
                            var gsOutType = gsOut.type.Substring(gsOut.type.IndexOf('<') + 1);
                            gsOutType = gsOutType.Substring(0, gsOutType.Length - 1);
                            if (structName == gsOutType)
                            {
                                sourceLineIndex++;
                                output.Add("{");
                                if (arrayPropertyValues.Count > 0)
                                    output.Add("uint d4rkAvatarOptimizer_MaterialID : d4rkAvatarOptimizer_MATERIAL_ID;");
                            }
                        }
                    }
                    var vertIn = pass.vertex.parameters.FirstOrDefault(p => p.isInput && p.semantic == null);
                    if (structName == vertIn?.type)
                    {
                        bool hasUv0 = false;
                        while (++sourceLineIndex < source.Count)
                        {
                            line = source[sourceLineIndex];
                            match = Regex.Match(line, @"^(\w+\s+)*(\w+)\s+(\w+)\s*:\s*(\w+)\s*;");
                            if (match.Success)
                            {
                                var semantic = match.Groups[4].Value.ToLowerInvariant();
                                var type = match.Groups[2].Value;
                                if (semantic == "texcoord" || semantic == "texcoord0")
                                {
                                    line = line.Replace(type, "float4");
                                    vertexInUv0Member = match.Groups[3].Value;
                                    hasUv0 = true;
                                }
                            }
                            if (line.StartsWith("}"))
                            {
                                if (!hasUv0)
                                {
                                    output.Add("float4 texcoord : TEXCOORD0;");
                                }
                                output.Add(line);
                                break;
                            }
                            output.Add(line);
                        }
                    }
                }
            }
            else
            {
                var match = Regex.Match(line, @"(uniform\s+)?([a-zA-Z0-9_]+)\s+([a-zA-Z0-9_]+)\s*;");
                if (match.Success)
                {
                    var name = match.Groups[3].Value;
                    var type = match.Groups[2].Value;
                    if (staticPropertyValues.TryGetValue(name, out string value))
                    {
                        output.Add("static " + type + " " + name + " = " + value + ";");
                    }
                    else if (!arrayPropertyValues.ContainsKey(name) && !texturesToReplaceCalls.Contains(name)
                        && !(type == "SamplerState" && texturesToReplaceCalls.Contains(name.Substring("sampler".Length))))
                    {
                        output.Add(line);
                    }
                }
                else
                {
                    output.Add(ReplaceTextureSamples(line));
                }
            }
        }

        private List<string> Run()
        {
            output = new List<string>();
            int passID = -1;
            var cgInclude = new List<string>();
            var state = ParseState.Init;
            for (int lineIndex = 0; lineIndex < parsedShader.lines.Count; lineIndex++)
            {
                string line = parsedShader.lines[lineIndex];
                switch (state)
                {
                    case ParseState.Init:
                        if (line == "Properties")
                        {
                            state = ParseState.PropertyBlock;
                        }
                        output.Add(line);
                        break;
                    case ParseState.PropertyBlock:
                        if (line == "}")
                        {
                            output.Add(line);
                            state = ParseState.ShaderLab;
                        }
                        else if (line == "{" && meshToggleCount > 1)
                        {
                            output.Add(line);
                            for (int i = 0; i < meshToggleCount; i++)
                            {
                                output.Add("_IsActiveMesh" + i + "(\"Generated Mesh Toggle " + i + "\", Float) = 1");
                            }
                        }
                        else if (texturesToMerge.Count > 0)
                        {
                            var prop = ShaderAnalyzer.ParseProperty(line);
                            if (texturesToMerge.Contains(prop?.name))
                            {
                                int index = line.LastIndexOf("2D");
                                line = line.Substring(0, index) + "2DArray" + line.Substring(index + 2);
                            }
                            output.Add(line);
                        }
                        else
                        {
                            output.Add(line);
                        }
                        break;
                    case ParseState.ShaderLab:
                        if (line == "CGINCLUDE")
                        {
                            state = ParseState.CGInclude;
                        }
                        else if (line == "CGPROGRAM")
                        {
                            state = ParseState.CGProgram;
                            var pass = parsedShader.passes[++passID];
                            output.Add(line);
                            vertexInUv0Member = "texcoord";
                            InjectPropertyArrays();
                            for (int includeLineIndex = 0; includeLineIndex < cgInclude.Count; includeLineIndex++)
                            {
                                ParseCodeLines(cgInclude, ref includeLineIndex, pass);
                            }
                        }
                        else
                        {
                            output.Add(line);
                        }
                        break;
                    case ParseState.CGInclude:
                        if (line == "ENDCG")
                        {
                            state = ParseState.ShaderLab;
                        }
                        else
                        {
                            cgInclude.Add(line);
                        }
                        break;
                    case ParseState.CGProgram:
                        if (line == "ENDCG")
                        {
                            state = ParseState.ShaderLab;
                            output.Add("ENDCG");
                        }
                        else
                        {
                            ParseCodeLines(parsedShader.lines, ref lineIndex, parsedShader.passes[passID]);
                        }
                        break;
                }
            }
            return output;
        }
    }
}
#endif