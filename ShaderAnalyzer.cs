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
        public class Pass
        {
            public string vertex;
            public string hull;
            public string domain;
            public string geometry;
            public string fragment;
        }
        public string name;
        public List<string> lines = new List<string>();
        public List<Property> properties = new List<Property>();
        public List<Pass> passes = new List<Pass>();
    }

    public static class ShaderAnalyzer
    {
        private static Dictionary<string, ParsedShader> parsedShaderCache = new Dictionary<string, ParsedShader>();

        public static void ClearParsedShaderCache()
        {
            parsedShaderCache.Clear();
        }

        public static ParsedShader Parse(Shader shader)
        {
            if (shader == null)
                return null;
            ParsedShader parsedShader;
            if (!parsedShaderCache.TryGetValue(shader.name, out parsedShader))
            {
                maxIncludes = 50;
                parsedShader = new ParsedShader();
                parsedShader.name = shader.name;
                RecursiveParseFile(AssetDatabase.GetAssetPath(shader), parsedShader.lines);
                SemanticParseShader(parsedShader);
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
            catch (IOException e)
            {
                Debug.LogError("Error reading shader file.  " + e.ToString());
                return false;
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

        private static ParsedShader.Property ParseProperty(string line)
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

        private enum ParseState
        {
            Init,
            PropertyBlock,
            ShaderLab,
            CGInclude,
            CGProgram
        }

        private static (string name, string returnType) ParseFunctionDefinition(string line)
        {
            var match = Regex.Match(line, @"^(inline\s+)?(\w+)\s+(\w+)\s*\(");
            if (match.Success && match.Groups[2].Value != "return" && match.Groups[2].Value != "else")
            {
                return (match.Groups[3].Value, match.Groups[2].Value);
            }
            return (null, null);
        }

        private static string ReplacePropertyDefinition(string line, Dictionary<string, string> properyValues)
        {
            var match = Regex.Match(line, @"(uniform\s+)?([a-zA-Z0-9_]+)\s+([a-zA-Z0-9_]+)\s*;");
            if (match.Success)
            {
                var name = match.Groups[3].Value;
                string value;
                if (properyValues.TryGetValue(name, out value))
                {
                    var type = match.Groups[2].Value;
                    return "static " + type + " " + name + " = " + value + ";";
                }
            }
            return line;
        }

        private static void InjectMeshToggleToVertexShader(List<string> source, ref int sourceLineIndex, List<string> output, (string name, string returnType) func, int meshToggleCount)
        {
            string line = source[sourceLineIndex];
            string funcParams = line.Substring(line.IndexOf('(') + 1);
            output.Add(func.returnType + " " + func.name + "(");
            output.Add("float4 d4rkAvatarOptimizer_UV0 : TEXCOORD0,");
            output.Add(funcParams);
            while (line != "{" && sourceLineIndex < source.Count - 1)
            {
                line = source[++sourceLineIndex];
                output.Add(line);
            }
            output.Add("if (d4rkAvatarOptimizer_Zero)");
            output.Add("{");
            string val = "float val = _IsActiveMesh0";
            for (int i = 1; i < meshToggleCount; i++)
            {
                val += " + _IsActiveMesh" + i;
            }
            output.Add(val + ";");
            output.Add("if (val) return (" + func.returnType + ")0;");
            output.Add("}");
            output.Add("if (!_IsActiveMesh[d4rkAvatarOptimizer_UV0.z])");
            output.Add("{");
            output.Add("return (v2f)0;");
            output.Add("}");
        }

        public static ParsedShader CreateOptimizedCopy(
            ParsedShader source,
            Dictionary<string, string> properyValues,
            int meshToggleCount)
        {
            var output = new ParsedShader();
            int passID = -1;
            var cgInclude = new List<string>();
            var state = ParseState.Init;
            for (int lineIndex = 0; lineIndex < source.lines.Count; lineIndex++)
            {
                string line = source.lines[lineIndex];
                switch (state)
                {
                    case ParseState.Init:
                        if (line == "Properties")
                        {
                            state = ParseState.PropertyBlock;
                        }
                        output.lines.Add(line);
                        break;
                    case ParseState.PropertyBlock:
                        output.lines.Add(line);
                        if (line == "}")
                        {
                            state = ParseState.ShaderLab;
                        }
                        else if (line == "{" && meshToggleCount > 0)
                        {
                            for (int i = 0; i < meshToggleCount; i++)
                            {
                                output.lines.Add("_IsActiveMesh" + i + "(\"Generated Mesh Toggle " + i +"\", Float) = 1");
                            }
                        }
                        break;
                    case ParseState.ShaderLab:
                        if (line == "CGINCLUDE")
                        {
                            state = ParseState.CGInclude;
                        }
                        else if (line == "CGPROGRAM")
                        {
                            passID++;
                            output.lines.Add(line);
                            if (meshToggleCount > 0)
                            {
                                output.lines.Add("uniform float d4rkAvatarOptimizer_Zero;");
                                output.lines.Add("cbuffer d4rkAvatarOptimizer_MeshToggles");
                                output.lines.Add("{");
                                output.lines.Add("float _IsActiveMesh[" + meshToggleCount + "] : packoffset(c0);");
                                for (int i = 0; i < meshToggleCount; i++)
                                {
                                    output.lines.Add("float _IsActiveMesh" + i + " : packoffset(c" + i + ");");
                                }
                                output.lines.Add("};");
                            }
                            for (int includeLineIndex = 0; includeLineIndex < cgInclude.Count; includeLineIndex++)
                            {
                                var includeLine = cgInclude[includeLineIndex];
                                var func = meshToggleCount > 0 ? ParseFunctionDefinition(includeLine) : (null, null);
                                if (func.name != null && func.name == source.passes[passID].vertex)
                                {
                                    InjectMeshToggleToVertexShader(cgInclude, ref includeLineIndex, output.lines, func, meshToggleCount);
                                }
                                else
                                {
                                    output.lines.Add(ReplacePropertyDefinition(includeLine, properyValues));
                                }
                            }
                            state = ParseState.CGProgram;
                        }
                        else
                        {
                            output.lines.Add(line);
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
                            output.lines.Add("ENDCG");
                        }
                        else
                        {
                            var func = meshToggleCount > 0 ? ParseFunctionDefinition(line) : (null, null);
                            if (func.name != null && func.name == source.passes[passID].vertex)
                            {
                                InjectMeshToggleToVertexShader(source.lines, ref lineIndex, output.lines, func, meshToggleCount);
                            }
                            else
                            {
                                output.lines.Add(ReplacePropertyDefinition(line, properyValues));
                            }
                        }
                        break;
                }
            }
            return output;
        }

        private static void ParsePragma(string line, ParsedShader.Pass pass)
        {
            if (!line.StartsWith("#pragma "))
                return;
            line = line.Substring("#pragma ".Length);
            var match = Regex.Match(line, @"(vertex|hull|domain|geometry|fragment)\s+(\w+)");
            if (!match.Success)
                return;
            switch (match.Groups[1].Value)
            {
                case "vertex":
                    pass.vertex = match.Groups[2].Value;
                    break;
                case "hull":
                    pass.hull = match.Groups[2].Value;
                    break;
                case "domain":
                    pass.domain = match.Groups[2].Value;
                    break;
                case "geometry":
                    pass.geometry = match.Groups[2].Value;
                    break;
                case "fragment":
                    pass.fragment = match.Groups[2].Value;
                    break;
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
                        ParsePragma(line, currentPass);
                        break;
                }
            }
        }
    }
}
#endif