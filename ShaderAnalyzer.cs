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
                Vector,
                Int,
                Int4,
                Texture2D,
                Texture2DArray
            }
            public string name;
            public Type type = Type.Unknown;
            public HashSet<string> shaderLabParams = new HashSet<string>();
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
                public override string ToString()
                {
                    return (isOutput ? (isInput ? "inout " : "out ") : "") + type + " " + name
                        + (arraySize > 0 ? "[" + arraySize + "]" : "") + (semantic != null ? " : " + semantic : "");
                }
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
                bool isPreprocessor = trimmedLine.StartsWith("#");
                while (trimmedLine.EndsWith("\\"))
                {
                    trimmedLine = trimmedLine.Substring(0, trimmedLine.Length - 1).TrimEnd() + " " + rawLines[++lineIndex].Trim();
                }
                for (int i = 0; i < trimmedLine.Length; i++)
                {
                    if (!isPreprocessor && trimmedLine[i] == ';')
                    {
                        processedLines.Add(trimmedLine.Substring(0, i + 1));
                        trimmedLine = trimmedLine.Substring(i + 1).TrimStart();
                        i = -1;
                        continue;
                    }
                    else if (!isPreprocessor && (trimmedLine[i] == '{' || trimmedLine[i] == '}'))
                    {
                        if (i != 0)
                            processedLines.Add(trimmedLine.Substring(0, i).TrimEnd());
                        processedLines.Add(trimmedLine[i].ToString());
                        trimmedLine = trimmedLine.Substring(i + 1).TrimStart();
                        i = -1;
                        continue;
                    }
                    else if (trimmedLine[i] == '"')
                    {
                        int end = FindEndOfStringLiteral(trimmedLine, i + 1);
                        i = (end == -1) ? trimmedLine.Length : end;
                        continue;
                    }
                    else if (trimmedLine[i] != '/' || i == trimmedLine.Length - 1)
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
                modifiedLine = modifiedLine.Substring(colonIndex + 1).TrimStart().ToLowerInvariant();
                if (modifiedLine.StartsWith("range") || modifiedLine.StartsWith("float"))
                {
                    output.type = ParsedShader.Property.Type.Float;
                }
                else if (modifiedLine.StartsWith("vector"))
                {
                    output.type = ParsedShader.Property.Type.Vector;
                }
                else if (modifiedLine.StartsWith("int"))
                {
                    output.type = ParsedShader.Property.Type.Int;
                }
                else if (modifiedLine.StartsWith("color"))
                {
                    output.type = ParsedShader.Property.Type.Color;
                }
                else if (modifiedLine.StartsWith("2darray"))
                {
                    output.type = ParsedShader.Property.Type.Texture2DArray;
                }
                else if (modifiedLine.StartsWith("2d"))
                {
                    output.type = ParsedShader.Property.Type.Texture2D;
                }
                return output;
            }
            return null;
        }

        private static Regex functionDefinition = new Regex(@"^(inline\s+)?(\w+)\s+(\w+)\s*\(", RegexOptions.Compiled);
        private static Regex functionParameter = new Regex(
            @"((in|out|inout)\s+)?((const|point|line|triangle)\s+)?(\w+(<[\w,\s]+>)?)\s+((\w+)(\[(\d+)\])?)(\s*:\s*(\w+))?",
            RegexOptions.Compiled);

        public static (string name, string returnType) ParseFunctionDefinition(string line)
        {
            var match = functionDefinition.Match(line);
            if (match.Success && match.Groups[2].Value != "return" && match.Groups[2].Value != "else")
            {
                return (match.Groups[3].Value, match.Groups[2].Value);
            }
            return (null, null);
        }

        public static ParsedShader.Function ParseFunctionDefinition(List<string> source, ref int lineIndex)
        {
            var match = functionDefinition.Match(source[lineIndex]);
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
                    var matches = functionParameter.Matches(line);
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

        private static void PreprocessCodeLines(List<string> lines, ref int lineIndex, List<string> output)
        {
            string line;
            while (lineIndex < lines.Count - 1 && (line = lines[++lineIndex]) != "ENDCG")
            {
                while (!line.EndsWith(";")
                    && !line.EndsWith("]")
                    && !line.StartsWith("#")
                    && !line.StartsWith("{")
                    && !line.StartsWith("}")
                    && lineIndex < lines.Count - 1
                    && lines[lineIndex + 1] != "{"
                    && lines[lineIndex + 1] != "}"
                    && !lines[lineIndex + 1].StartsWith("#"))
                {
                    line = line + " " + lines[++lineIndex];
                }
                output.Add(line);
            }
        }

        private static void SemanticParseShader(ParsedShader parsedShader)
        {
            var cgIncludePragmas = new ParsedShader.Pass();
            ParsedShader.Pass currentPass = null;
            List<string> output = new List<string>();
            List<string> cgInclude = new List<string>();
            List<string> lines = parsedShader.lines;
            parsedShader.lines = output;
            var state = ParseState.Init;
            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                string line = lines[lineIndex];
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
                        if (line == "{" && lines[lineIndex + 1] == "}")
                        {
                            lineIndex++;
                            output[output.Count - 1] += " {}";
                        }
                        else if (line == "}")
                        {
                            state = ParseState.ShaderLab;
                            output.Add(line);
                        }
                        else
                        {
                            var property = ParseProperty(line);
                            if (property != null)
                            {
                                parsedShader.properties.Add(property);
                            }
                            output.Add(line);
                        }
                        break;
                    case ParseState.ShaderLab:
                        if (line == "CGINCLUDE")
                        {
                            PreprocessCodeLines(lines, ref lineIndex, cgInclude);
                        }
                        else if (line == "CGPROGRAM")
                        {
                            currentPass = new ParsedShader.Pass();
                            parsedShader.passes.Add(currentPass);
                            var cgProgram = new List<string>(cgInclude);
                            PreprocessCodeLines(lines, ref lineIndex, cgProgram);
                            for (int programLineIndex = 0; programLineIndex < cgProgram.Count; programLineIndex++)
                            {
                                ParsePragma(cgProgram[programLineIndex], currentPass, parsedShader);
                                var func = ParseFunctionDefinition(cgProgram, ref programLineIndex);
                                if (func != null)
                                {
                                    parsedShader.functions[func.name] = func;
                                    UpdateFunctionDefinition(func, currentPass);
                                }
                            }
                            output.Add("CGPROGRAM");
                            output.AddRange(cgProgram);
                            output.Add("ENDCG");
                        }
                        else
                        {
                            output.Add(line);
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
        private HashSet<string> texturesToCallSoTheSamplerDoesntDissapear;

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
                texturesToMerge = texturesToMerge ?? new HashSet<string>(),
                texturesToCallSoTheSamplerDoesntDissapear = new HashSet<string>()
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

        private List<string> ParseFunctionParametersWithPreprocessorStatements(List<string> source, ref int sourceLineIndex)
        {
            var output = new List<string>();
            string line = source[sourceLineIndex].Split('(')[1];
            while (line != "{" && sourceLineIndex < source.Count - 1)
            {
                if (line.StartsWith("#"))
                {
                    output.Add(line);
                }
                else
                {
                    output.AddRange(line.Split(',').Select(s => s.Trim()).Where(s => s != ""));
                }
                line = source[++sourceLineIndex];
            }
            output[output.Count - 1] = output[output.Count - 1].Split(')')[0];
            if (output[output.Count - 1].Length == 0)
                output.RemoveAt(output.Count - 1);
            return output;
        }

        private void AddParameterStructWrapper(List<string> funcParams, List<string> output, string name, bool addMaterialID, bool addMeshToggle, bool isInput)
        {
            output.Add("struct " + name + "Wrapper");
            output.Add("{");
            if (addMaterialID)
                output.Add("int d4rkAvatarOptimizer_MaterialID : d4rkAvatarOptimizer_MaterialID;");
            if (addMeshToggle)
                output.Add("bool d4rkAvatarOptimizer_NotCullVert : d4rkAvatarOptimizer_NotCullVert;");
            foreach (var line in funcParams)
            {
                if (line.StartsWith("#"))
                {
                    output.Add(line);
                    continue;
                }
                var match = Regex.Match(line, @"((in|out|inout)\s)?\s*(\w+\s+\w+(\s*:\s*\w+)?)");
                if (match.Success)
                {
                    if (isInput ^ match.Groups[2].Value != "out")
                        continue;
                    output.Add(match.Groups[3].Value + ";");
                }
                else
                {
                    output.Add("// Uh oh: " + line);
                }
            }
            output.Add("};");
        }
        
        private void InitializeParameterFromWrapper(List<string> funcParams, List<string> output, string wrapperName, bool isInput)
        {
            foreach (var line in funcParams)
            {
                if (line.StartsWith("#"))
                {
                    output.Add(line);
                    continue;
                }
                var match = Regex.Match(line, @"((in|out|inout)\s)?\s*(\w+)\s+(\w+)(\s*:\s*\w+)?");
                if (match.Success)
                {
                    if (isInput && match.Groups[2].Value != "out")
                    {
                        var type = match.Groups[3].Value;
                        var name = match.Groups[4].Value;
                        output.Add(type + " " + name + " = " + wrapperName + "." + name + ";");
                    }
                    else if (!isInput && match.Groups[2].Value == "out")
                    {
                        var type = match.Groups[3].Value;
                        var name = match.Groups[4].Value;
                        output.Add(wrapperName + "." + name + " = " + name + ";");
                    }
                }
                else
                {
                    output.Add("// Uh oh: " + line);
                }
            }
        }

        private void InitializeOutputParameter(List<string> funcParams, List<string> output)
        {
            foreach (var line in funcParams)
            {
                if (line.StartsWith("#"))
                {
                    output.Add(line);
                    continue;
                }
                var match = Regex.Match(line, @"((in|out|inout)\s)?\s*(\w+)\s+(\w+)(\s*:\s*\w+)?");
                if (match.Success)
                {
                    if (match.Groups[2].Value == "out")
                    {
                        var type = match.Groups[3].Value;
                        var name = match.Groups[4].Value;
                        output.Add(type + " " + name + " = (" + type + ")0;");
                    }
                }
                else
                {
                    output.Add("// Uh oh: " + line);
                }
            }
        }

        private void InjectVertexShaderCode(
            List<string> source,
            ref int sourceLineIndex,
            ParsedShader.Pass pass)
        {
            var func = pass.vertex;
            var outParam = func.parameters.FirstOrDefault(p => p.isOutput && p.type != "void");
            var inParam = func.parameters.FirstOrDefault(p => p.isInput && p.semantic == null);
            var returnParam = func.parameters[0];
            var isVoidReturn = returnParam.type == "void";
            List<string> funcParams = null;
            List<string> originalVertexShader = null;
            bool needToPassOnMeshToggle = pass.geometry != null && meshToggleCount > 1;
            if (arrayPropertyValues.Count > 0 || needToPassOnMeshToggle)
            {
                int startLineIndex = sourceLineIndex;
                funcParams = ParseFunctionParametersWithPreprocessorStatements(source, ref sourceLineIndex);
                originalVertexShader = new List<string>(source.GetRange(startLineIndex, sourceLineIndex - startLineIndex + 1));
                if (returnParam.type != "void")
                    funcParams = funcParams.Prepend("out " + returnParam.type + " returnWrappedStruct"
                        + (returnParam.semantic != null ? " : " + returnParam.semantic : "")).ToList();
                AddParameterStructWrapper(funcParams, output, "vertexOutput", arrayPropertyValues.Count > 0, needToPassOnMeshToggle, false);
                AddParameterStructWrapper(funcParams, output, "vertexInput", false, false, true);
                output.Add("vertexOutputWrapper d4rkAvatarOptimizer_vertexWithWrapper(vertexInputWrapper d4rkAvatarOptimizer_vertexInput)");
                output.Add("{");
                InitializeParameterFromWrapper(funcParams, output, "d4rkAvatarOptimizer_vertexInput", true);
            }
            else
            {
                string line = source[sourceLineIndex];
                output.Add(line);
                while (line != "{" && sourceLineIndex < source.Count - 1)
                {
                    line = source[++sourceLineIndex];
                    output.Add(line);
                }
            }
            string nullReturn = isVoidReturn ? "return;" : "return (" + outParam.type + ")0;";
            if (funcParams != null)
            {
                InitializeOutputParameter(funcParams, output);
                nullReturn = "return (vertexOutputWrapper)0;";
            }
            string uv0Name = inParam.name + "." + vertexInUv0Member;
            if (meshToggleCount > 1)
            {
                output.Add("if (d4rkAvatarOptimizer_Zero)");
                output.Add("{");
                string val = "float d4rkAvatarOptimizer_val = _IsActiveMesh0";
                for (int i = 1; i < meshToggleCount; i++)
                {
                    val += " + _IsActiveMesh" + i;
                }
                output.Add(val + ";");
                output.Add("if (d4rkAvatarOptimizer_val) " + nullReturn);
                output.Add("}");
                output.Add("if (!_IsActiveMesh[" + uv0Name + ".z]) " + nullReturn);
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
                string line = source[sourceLineIndex];
                originalVertexShader?.Add(line);
                if (line == "}")
                {
                    if (braceDepth-- == 0)
                    {
                        if (isVoidReturn)
                        {
                            output.Add("{");
                            var outParamName = outParam.name;
                            if (funcParams != null)
                            {
                                outParamName = "d4rkAvatarOptimizer_vertexOutput";
                                output.Add("vertexOutputWrapper d4rkAvatarOptimizer_vertexOutput;");
                                InitializeParameterFromWrapper(funcParams, output, "d4rkAvatarOptimizer_vertexOutput", false);
                            }
                            if (arrayPropertyValues.Count > 0)
                                output.Add(outParamName + ".d4rkAvatarOptimizer_MaterialID = d4rkAvatarOptimizer_MaterialID;");
                            if (needToPassOnMeshToggle)
                                output.Add(outParamName + ".d4rkAvatarOptimizer_NotCullVert = true;");
                            if (funcParams != null)
                                output.Add("return d4rkAvatarOptimizer_vertexOutput;");
                            output.Add("}");
                        }
                        break;
                    }
                    output.Add(line);
                }
                else if (line == "{")
                {
                    output.Add(line);
                    braceDepth++;
                }
                else if (line.StartsWith("return"))
                {
                    output.Add("{");
                    var outParamName = "d4rkAvatarOptimizer_vertexOutput";
                    if (funcParams == null && isVoidReturn)
                    {
                        outParamName = outParam.name;
                    }
                    else if (funcParams != null)
                    {
                        output.Add("vertexOutputWrapper d4rkAvatarOptimizer_vertexOutput;");
                        if (!isVoidReturn)
                            output.Add("returnWrappedStruct = " + line.Substring("return ".Length));
                        InitializeParameterFromWrapper(funcParams, output, "d4rkAvatarOptimizer_vertexOutput", false);
                    }
                    else if (!isVoidReturn)
                    {
                        output.Add(outParam.type + " d4rkAvatarOptimizer_vertexOutput = " + line.Substring("return ".Length));
                    }
                    if (arrayPropertyValues.Count > 0)
                        output.Add(outParamName + ".d4rkAvatarOptimizer_MaterialID = d4rkAvatarOptimizer_MaterialID;");
                    if (needToPassOnMeshToggle)
                        output.Add(outParamName + ".d4rkAvatarOptimizer_NotCullVert = true;");
                    output.Add((funcParams == null && isVoidReturn) ? "return;" : "return d4rkAvatarOptimizer_vertexOutput;");
                    output.Add("}");
                }
                else
                {
                    output.Add(ReplaceTextureSamples(line));
                }
            }
            output.Add("}");
            if (originalVertexShader != null)
            {
                output.AddRange(originalVertexShader);
            }
        }

        private void InjectGeometryShaderCode(
            List<string> source,
            ref int sourceLineIndex,
            ParsedShader.Pass pass)
        {
            var func = pass.geometry;
            var outParam = func.parameters.FirstOrDefault(p => p.type.Contains("Stream<"));
            var outParamType = outParam.type.Substring(outParam.type.IndexOf('<') + 1);
            outParamType = outParamType.Substring(0, outParamType.Length - 1);
            var inParam = func.parameters.FirstOrDefault(p => p.isInput && p.arraySize > 0);
            var wrapperStructs = new List<string>();
            bool usesInputWrapper = arrayPropertyValues.Count > 0 || meshToggleCount > 1;
            bool usesOuputWrapper = arrayPropertyValues.Count > 0;
            if (usesInputWrapper)
            {
                wrapperStructs.Add("struct geometryInputWrapper");
                wrapperStructs.Add("{");
                if (arrayPropertyValues.Count > 0)
                    wrapperStructs.Add("int d4rkAvatarOptimizer_MaterialID : d4rkAvatarOptimizer_MaterialID;");
                if (meshToggleCount > 1)
                    wrapperStructs.Add("bool d4rkAvatarOptimizer_NotCullVert : d4rkAvatarOptimizer_NotCullVert;");
                wrapperStructs.Add(inParam.type + " d4rkAvatarOptimizer_geometryInput;");
                wrapperStructs.Add("};");
            }
            if (usesOuputWrapper)
            {
                wrapperStructs.Add("struct geometryOutputWrapper");
                wrapperStructs.Add("{");
                wrapperStructs.Add("int d4rkAvatarOptimizer_MaterialID : d4rkAvatarOptimizer_MaterialID;");
                wrapperStructs.Add(outParamType + " d4rkAvatarOptimizer_geometryOutput;");
                wrapperStructs.Add("};");
            }
            int insertIndex = output.FindLastIndex(s => !s.StartsWith("#") && !s.StartsWith("[")) + 1;
            output.InsertRange(insertIndex, wrapperStructs);
            string line = source[sourceLineIndex];
            while (line != "{" && sourceLineIndex < source.Count - 1)
            {
                line = source[++sourceLineIndex];
            }
            output.Add("void " + func.name + "(triangle "
                + (usesInputWrapper ? "geometryInputWrapper d4rkAvatarOptimizer_inputWrapper" : inParam.type + " " + inParam.name)
                + "[" + inParam.arraySize + "], inout "
                + outParam.type.Substring(0, 7 + outParam.type.IndexOf('S'))
                + (usesOuputWrapper ? "geometryOutputWrapper" : outParamType)
                + "> " + outParam.name + ")");
            output.Add("{");
            sourceLineIndex++;
            if (usesInputWrapper)
            {
                output.Add(inParam.type + " " + inParam.name + "[" + inParam.arraySize + "];");
                for (int i = 0; i < inParam.arraySize; i++)
                {
                    output.Add(inParam.name + "[" + i + "] = d4rkAvatarOptimizer_inputWrapper[" + i + "].d4rkAvatarOptimizer_geometryInput;");
                }
            }
            if (meshToggleCount > 1)
            {
                output.Add("if (!d4rkAvatarOptimizer_inputWrapper[0].d4rkAvatarOptimizer_NotCullVert) return;");
            }
            if (arrayPropertyValues.Count > 0)
            {
                output.Add("d4rkAvatarOptimizer_MaterialID = d4rkAvatarOptimizer_inputWrapper[0].d4rkAvatarOptimizer_MaterialID;");
                InjectArrayPropertyInitialization();
            }
            if (!usesOuputWrapper)
            {
                return;
            }
            int braceDepth = 0;
            for (; sourceLineIndex < source.Count; sourceLineIndex++)
            {
                line = source[sourceLineIndex];
                if (line == "}")
                {
                    if (braceDepth-- == 0)
                    {
                        break;
                    }
                    output.Add(line);
                }
                else if (line == "{")
                {
                    output.Add(line);
                    braceDepth++;
                }
                else if (line.Contains(outParam.name + ".Append("))
                {
                    output.Add("{");
                    output.Add("geometryOutputWrapper d4rkAvatarOptimizer_geomOutput;");
                    output.Add("d4rkAvatarOptimizer_geomOutput.d4rkAvatarOptimizer_geometryOutput = " + line.Substring(line.IndexOf(".Append(") + 7));
                    if (arrayPropertyValues.Count > 1)
                        output.Add("d4rkAvatarOptimizer_geomOutput.d4rkAvatarOptimizer_MaterialID = d4rkAvatarOptimizer_MaterialID;");
                    output.Add(outParam.name + ".Append(d4rkAvatarOptimizer_geomOutput);");
                    output.Add("}");
                }
                else
                {
                    output.Add(ReplaceTextureSamples(line));
                }
            }
            output.Add("}");
        }

        private void InjectFragmentShaderCode(
            List<string> source,
            ref int sourceLineIndex,
            ParsedShader.Pass pass)
        {
            if (arrayPropertyValues.Count > 0)
            {
                var funcParams = ParseFunctionParametersWithPreprocessorStatements(source, ref sourceLineIndex);
                AddParameterStructWrapper(funcParams, output, "fragmentInput", true, false, true);
                output.Add(pass.fragment.parameters[0].type + " " + pass.fragment.name + "(");
                output.Add("fragmentInputWrapper d4rkAvatarOptimizer_fragmentInput");
                if (pass.fragment.parameters[0].semantic != null)
                    output.Add(") : " + pass.fragment.parameters[0].semantic);
                else
                    output.Add(")");
                output.Add("{");
                InitializeParameterFromWrapper(funcParams, output, "d4rkAvatarOptimizer_fragmentInput", true);
                output.Add("d4rkAvatarOptimizer_MaterialID = d4rkAvatarOptimizer_fragmentInput.d4rkAvatarOptimizer_MaterialID;");
                InjectArrayPropertyInitialization();
            }
            else
            {
                string line = source[sourceLineIndex];
                output.Add(line);
                while (line != "{" && sourceLineIndex < source.Count - 1)
                {
                    line = source[++sourceLineIndex];
                    output.Add(line);
                }
            }
            var outParam = pass.fragment.parameters.FirstOrDefault(p => p.isOutput && p.type != "void");
            if (texturesToCallSoTheSamplerDoesntDissapear.Count > 0)
            {
                output.Add("if (d4rkAvatarOptimizer_Zero)");
                output.Add("{");
                output.Add("float d4rkAvatarOptimizer_sum = 0;");
                foreach (var tex in texturesToCallSoTheSamplerDoesntDissapear)
                {
                    output.Add("#ifdef DUMMY_USE_TEXTURE_TO_PRESERVE_SAMPLER_" + tex);
                    output.Add("d4rkAvatarOptimizer_sum += " + tex + ".Load(0);");
                    output.Add("#endif");
                }
                output.Add(pass.fragment.parameters[0].type == "void"
                    ? "if (d4rkAvatarOptimizer_sum) return;"
                    : "if (d4rkAvatarOptimizer_sum) return (" + outParam.type + ")0;");
                output.Add("}");
            }
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

                string newTexName = texName;

                if (isArray && texName == "_MainTex")
                {
                    newTexName = "_MainTexButNotQuiteSoThatUnityDoesntCry";
                    output.Add("#define _MainTex_ST _MainTexButNotQuiteSoThatUnityDoesntCry_ST");
                    output.Add("#define sampler_MainTex sampler_MainTexButNotQuiteSoThatUnityDoesntCry");
                }

                texturesToCallSoTheSamplerDoesntDissapear.Add(newTexName);
                output.Add("#define DUMMY_USE_TEXTURE_TO_PRESERVE_SAMPLER_" + newTexName);

                output.Add("uniform Texture2D" + (isArray ? "Array " : " ") + newTexName + ";");
                output.Add("uniform SamplerState sampler" + newTexName + ";");

                output.Add("#define UNITY_SAMPLE_TEX2D" + texName + "(uv) tex2D" + texName + "(uv)");
                output.Add("#define UNITY_SAMPLE_TEX2D_SAMPLER" + texName + "(sampl, uv) " + texName + "Sample(sampler##sampl, (uv))");

                output.Add("float4 " + texName + "Sample(SamplerState sampl, float2 uv) {");
                if (nullCheck != null) output.Add(nullCheck);
                output.Add("return " + newTexName + ".Sample(sampl, " + uv + ");}");

                output.Add("float4 " + texName + "SampleGrad(SamplerState sampl, float2 uv, float2 ddxuv, float2 ddyuv) {");
                if (nullCheck != null) output.Add(nullCheck);
                output.Add("return " + newTexName + ".SampleGrad(sampl, " + uv + ", ddxuv, ddyuv);}");

                output.Add("float4 " + texName + "SampleLevel(SamplerState sampl, float2 uv, int mipLevel) {");
                if (nullCheck != null) output.Add(nullCheck);
                output.Add("return " + newTexName + ".SampleLevel(sampl, " + uv + ", mipLevel);}");

                output.Add("float4 " + texName + "SampleBias(SamplerState sampl, float2 uv, float bias) {");
                if (nullCheck != null) output.Add(nullCheck);
                output.Add("return " + newTexName + ".SampleBias(sampl, " + uv + ", bias);}");

                output.Add("float4 tex2D" + texName + "(float2 uv) {");
                output.Add("return " + texName + "Sample(sampler" + newTexName + ", uv);}");

                output.Add("float4 tex2Dproj" + texName + "(float4 uv) {");
                output.Add("return " + texName + "Sample(sampler" + newTexName + ", uv.xy / uv.w);}");

                output.Add("float4 tex2D" + texName + "(float2 uv, float2 ddxuv, float2 ddyuv) {");
                output.Add("return " + texName + "SampleGrad(sampler" + newTexName + ", uv, ddxuv, ddyuv);}");

                output.Add("float4 tex2Dgrad" + texName + "(float2 uv, float2 ddxuv, float2 ddyuv) {");
                output.Add("return " + texName + "SampleGrad(sampler" + newTexName + ", uv, ddxuv, ddyuv);}");

                output.Add("float4 tex2Dlod" + texName + "(float4 uv) {");
                output.Add("return " + texName + "SampleLevel(sampler" + newTexName + ", uv.xy, uv.w);}");

                output.Add("float4 tex2Dbias" + texName + "(float4 uv) {");
                output.Add("return " + texName + "SampleBias(sampler" + newTexName + ", uv.xy, uv.w);}");
            }
        }

        private static Regex tex2DAndUnityMacroCalls = new Regex(@"(tex2D\w*|UNITY_SAMPLE_TEX2D\w*)\s*\(\s*(\w+)\s*,", RegexOptions.Compiled);
        private static Regex textureSampleFunctionCalls = new Regex(@"(\w+)\s*\.\s*(Sample\w*)\s*\(", RegexOptions.Compiled);
        
        private string ReplaceTextureSamples(string line)
        {
            if (texturesToReplaceCalls.Count == 0)
                return line;
            line = tex2DAndUnityMacroCalls.Replace(line, match =>
                texturesToReplaceCalls.Contains(match.Groups[2].Value)
                ? match.Groups[1].Value + match.Groups[2].Value + "("
                : match.Value);
            line = textureSampleFunctionCalls.Replace(line, match =>
                texturesToReplaceCalls.Contains(match.Groups[1].Value)
                ? match.Groups[1].Value + match.Groups[2].Value + "("
                : match.Value);
            return line;
        }

        private static Regex variableDeclaration = new Regex(@"^(uniform\s+)?(\w+)\s+(\w+)(\s*,\s*(\w+))*\s*;", RegexOptions.Compiled);

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
                var match = variableDeclaration.Match(line);
                if (match.Success)
                {
                    var type = match.Groups[2].Value;
                    var names = match.Groups[5].Captures.Cast<Capture>().Select(c => c.Value).ToList();
                    names.Add(match.Groups[3].Value);
                    foreach (var name in names)
                    {
                        if (type == "SamplerState" && !texturesToReplaceCalls.Contains(name.Substring("sampler".Length)))
                        {
                            if (parsedShader.properties.Any(p => p.name == name.Substring("sampler".Length)))
                            {
                                texturesToCallSoTheSamplerDoesntDissapear.Add(name.Substring("sampler".Length));
                                output.Add("#define DUMMY_USE_TEXTURE_TO_PRESERVE_SAMPLER_" + name.Substring("sampler".Length));
                            }
                            output.Add(type + " " + name + ";");
                        }
                        else if (staticPropertyValues.TryGetValue(name, out string value))
                        {
                            output.Add("static " + type + " " + name + " = " + value + ";");
                        }
                        else if (!arrayPropertyValues.ContainsKey(name) && !texturesToReplaceCalls.Contains(name)
                            && !(type == "SamplerState" && texturesToReplaceCalls.Contains(name.Substring("sampler".Length))))
                        {
                            output.Add(type + " " + name + ";");
                        }
                    }
                }
                else if (line.StartsWith("UNITY_DECLARE_TEX2D"))
                {
                    var texName = line.Split('(')[1].Split(')')[0].Trim();
                    bool hasSampler = !line.Contains("_NOSAMPLER");
                    if (!texturesToReplaceCalls.Contains(texName))
                    {
                        if (hasSampler && parsedShader.properties.Any(p => p.name == texName))
                        {
                            texturesToCallSoTheSamplerDoesntDissapear.Add(texName);
                            output.Add("#define DUMMY_USE_TEXTURE_TO_PRESERVE_SAMPLER_" + texName);
                        }
                        output.Add(line);
                    }
                }
                else
                {
                    if (Regex.IsMatch(line, @"#pragma\s+vertex\s+\w+")
                        && ((pass.geometry != null && meshToggleCount > 1) || arrayPropertyValues.Count > 0))
                    {
                        output.Add("#pragma vertex d4rkAvatarOptimizer_vertexWithWrapper");
                    }
                    else
                    {
                        output.Add(ReplaceTextureSamples(line));
                    }
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
                                if (prop.name == "_MainTex")
                                    line = line.Replace("_MainTex", "_MainTexButNotQuiteSoThatUnityDoesntCry");
                            }
                            output.Add(line);
                        }
                        else
                        {
                            output.Add(line);
                        }
                        break;
                    case ParseState.ShaderLab:
                        if (line == "CGPROGRAM")
                        {
                            var pass = parsedShader.passes[++passID];
                            vertexInUv0Member = "texcoord";
                            texturesToCallSoTheSamplerDoesntDissapear.Clear();
                            output.Add("CGPROGRAM");
                            InjectPropertyArrays();
                            while (parsedShader.lines[++lineIndex] != "ENDCG")
                            {
                                ParseCodeLines(parsedShader.lines, ref lineIndex, pass);
                            }
                            output.Add("ENDCG");
                        }
                        else
                        {
                            output.Add(line);
                        }
                        break;
                }
            }
            return output;
        }
    }
}
#endif