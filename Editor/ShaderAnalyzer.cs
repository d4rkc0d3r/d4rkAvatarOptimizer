#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using d4rkpl4y3r.AvatarOptimizer.Util;
using System.Security.Cryptography;

namespace d4rkpl4y3r.AvatarOptimizer
{
    public class ParsedShader
    {
        public class Property
        {
            public enum Type
            {
                Unknown,
                Color,
                ColorHDR,
                Float,
                Vector,
                Int,
                Texture2D,
                Texture2DArray,
                Texture3D,
                TextureCube,
                TextureCubeArray
            }
            public string name;
            public Type type = Type.Unknown;
            public HashSet<string> shaderLabParams = new HashSet<string>();
            public string defaultValue;
            public bool hasGammaTag = false;
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
                public int arraySize = -1;
                public override string ToString()
                {
                    return (isOutput ? (isInput ? "inout " : "out ") : "") + type + " " + name
                        + (arraySize > -1 ? "[" + arraySize + "]" : "") + (semantic != null ? " : " + semantic : "");
                }
            }
            public string name;
            public List<Parameter> parameters = new List<Parameter>();
            public override string ToString()
            {
                if (parameters.Count == 0)
                    return name;
                string s = "";
                s += parameters[0].type + " ";
                s += name + "(";
                for (int i = 1; i < parameters.Count; i++)
                {
                    var param = parameters[i];
                    s += i > 1 ? ", " : "";
                    s += (param.isInput ? "in" : "") + (param.isOutput ? "out " : " ");
                    s += param.type;
                    s += " ";
                    s += param.name;
                    s += param.arraySize > 0 ? "[" + param.arraySize + "]" : "";
                    s += param.semantic == null ? "" : " : " + param.semantic;
                }
                s += ")";
                s += parameters[0].semantic == null ? "" : " : " + parameters[0].semantic;
                s += ";";
                return s;
            }
        }
        public class Pass
        {
            public Function vertex;
            public Function hull;
            public Function domain;
            public Function geometry;
            public Function fragment;
            public HashSet<string> shaderFeatureKeyWords = new HashSet<string>();
        }
        public string name;
        public bool parsedCorrectly = false;
        public string errorMessage = "";
        public bool hasDisableBatchingTag = false;
        public List<string> customTextureDeclarations = new List<string>();
        public Dictionary<string, int> multiIncludeFileCount = new Dictionary<string, int>();
        public bool mismatchedCurlyBraces = false;
        public Dictionary<string, List<string>> text = new Dictionary<string, List<string>>();
        public List<Property> properties = new List<Property>();
        public Dictionary<string, Property> propertyTable = new Dictionary<string, Property>();
        public List<Pass> passes = new List<Pass>();
        public Dictionary<string, Function> functions = new Dictionary<string, Function>();
        public HashSet<string> shaderFeatureKeyWords = new HashSet<string>();

        public bool CanMerge()
        {
            if (!parsedCorrectly)
                return false;
            if (passes.Any(p => p.hull != null || p.domain != null))
                return false;
            if (hasDisableBatchingTag)
                return false;
            return true;
        }

        public string CantMergeReason()
        {
            if (!parsedCorrectly)
                return errorMessage == "" ? "Shader has not parsed correctly." : errorMessage;
            if (passes.Any(p => p.hull != null || p.domain != null))
                return "Shader has a pass with tessellation.";
            if (hasDisableBatchingTag)
                return "Shader has DisableBatching set to true.";
            return "";
        }

        public bool CanMergeTextures()
        {
            if (!CanMerge())
                return false;
            if (customTextureDeclarations.Count > 0)
                return false;
            if (mismatchedCurlyBraces)
                return false;
            return true;
        }

        public string CantMergeTexturesReason()
        {
            if (!CanMerge())
                return CantMergeReason();
            if (customTextureDeclarations.Count > 0)
                return "Shader has custom texture declaration macros.";
            if (mismatchedCurlyBraces)
                return "Shader has mismatched curly braces.";
            return "";
        }
    }

    public class ShaderAnalyzer
    {
        private enum ParseState
        {
            Init,
            PropertyBlock,
            ShaderLab,
            Tags,
            CGInclude,
            CGProgram
        }

        private class ParserException : System.Exception
        {
            public ParserException(string message) : base(message) { }
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
                var analyzer = new ShaderAnalyzer(shader.name, AssetDatabase.GetAssetPath(shader));
                Profiler.StartSection("ShaderAnalyzer.Parse()");
                parsedShader = analyzer.Parse();
                Profiler.EndSection();
                parsedShaderCache[shader.name] = parsedShader;
            }
            return parsedShader;
        }

        public static List<ParsedShader> ParseAndCacheAllShaders(IEnumerable<Shader> shaders, bool overrideAlreadyCached, System.Action<int, int> progressCallback = null)
        {
            var analyzers = shaders.Distinct()
                .Where(s => overrideAlreadyCached || !parsedShaderCache.ContainsKey(s.name))
                .Select(s => new ShaderAnalyzer(s.name, AssetDatabase.GetAssetPath(s)))
                .ToArray();
            Profiler.StartSection("ShaderAnalyzer.Parse()");
            var tasks = analyzers.Select(a => Task.Run(() => a.Parse())).ToArray();
            int done = 0;
            while (done < tasks.Length)
            {
                done = tasks.Count(t => t.IsCompleted);
                progressCallback?.Invoke(done, tasks.Length);
                Thread.Sleep(1);
            }
            Profiler.EndSection();
            foreach (var a in analyzers)
                parsedShaderCache[a.parsedShader.name] = a.parsedShader;
            return shaders.Select(s => parsedShaderCache[s.name]).ToList();
        }

        private ParsedShader parsedShader;
        private int maxIncludes;
        private string filePath;
        private bool doneParsing;
        private string[] shaderFileLines;
        private HashSet<string> alreadyIncludedThisPass;

        private ShaderAnalyzer(string shaderName, string shaderPath)
        {
            parsedShader = new ParsedShader();
            parsedShader.name = shaderName;
            filePath = shaderPath;
            maxIncludes = 1000;
            doneParsing = false;
            if (shaderPath.EndsWith(".orlshader"))
            {
                #if ORLSHADER_EXISTS
                Profiler.StartSection("ORL.ShaderGenerator");
                var fileName = Path.GetFileNameWithoutExtension(shaderName);
                var shaderNameHash = "";
                using (var md5 = MD5.Create())
                {
                    md5.ComputeHash(Encoding.UTF8.GetBytes(shaderName));
                    shaderNameHash = string.Join("", md5.Hash.Select(b => b.ToString("x2")));
                }
                var trashBinPath = d4rkAvatarOptimizer.GetTrashBinPath();
                var tempShaderPath = Path.Combine(trashBinPath, $"{fileName}_{shaderNameHash}.shader");
                if (!File.Exists(tempShaderPath))
                {
                    ORL.ShaderGenerator.ShaderDefinitionImporter.GenerateShader(shaderPath, tempShaderPath);
                }
                try
                {
                    shaderFileLines = File.ReadAllLines(tempShaderPath);
                }
                catch (IOException e)
                {
                    parsedShader.parsedCorrectly = false;
                    doneParsing = true;
                    parsedShader.errorMessage = e.Message;
                }
                Profiler.EndSection();
                #else
                parsedShader.parsedCorrectly = false;
                doneParsing = true;
                parsedShader.errorMessage = "ORLShader Generator is not installed.";
                #endif
            }
        }

        private ParsedShader Parse()
        {
            if (doneParsing)
                return parsedShader;
            var oldCulture = Thread.CurrentThread.CurrentCulture;
            var oldUICulture = Thread.CurrentThread.CurrentUICulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            try
            {
                RecursiveParseFile(filePath, true, filePath);
                SemanticParseShader();
                parsedShader.parsedCorrectly = true;
                if (parsedShader.text[".shader"].Count == 0)
                {
                    parsedShader.parsedCorrectly = false;
                    parsedShader.errorMessage = "Parsed shader is empty.";
                }
            }
            catch (IOException e)
            {
                parsedShader.parsedCorrectly = false;
                parsedShader.errorMessage = e.Message;
            }
            catch (ParserException e)
            {
                parsedShader.parsedCorrectly = false;
                parsedShader.errorMessage = e.Message;
            }
            catch (System.Exception e)
            {
                parsedShader.parsedCorrectly = false;
                parsedShader.errorMessage = e.Message;
                Debug.LogWarning(e);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = oldCulture;
                Thread.CurrentThread.CurrentUICulture = oldUICulture;
            }
            doneParsing = true;
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

        private bool RecursiveParseFile(string currentFileName, bool isTopLevelFile, string callerPath)
        {
            var processedLines = new List<string>();
            if (alreadyIncludedThisPass == null)
                alreadyIncludedThisPass = new HashSet<string>();
            var fileID = currentFileName;
            var currentFilePath = currentFileName;
            string[] rawLines = null;
            if (isTopLevelFile)
            {
                rawLines = shaderFileLines;
                fileID = ".shader";
            }
            if (rawLines == null)
            {
                if (currentFilePath.StartsWith("/Assets/"))
                {
                    var path = Path.GetDirectoryName(callerPath);
                    var assetFolderPath = path.IndexOf("Assets") != -1 ? path.Substring(0, path.IndexOf("Assets") - 1) : path;
                    currentFilePath = assetFolderPath + currentFilePath;
                }
                else if (!isTopLevelFile)
                {
                    currentFilePath = Path.GetDirectoryName(callerPath) + "/" + currentFilePath;
                }
                currentFilePath = Path.GetFullPath(currentFilePath);
                var fileName = Path.GetFileName(currentFilePath);
                if (fileName == "UnityLightingCommon.cginc")
                {
                    currentFilePath = Path.Combine(EditorApplication.applicationContentsPath, $"CGIncludes\\{fileName}");
                }
                else if (File.Exists(Path.Combine(EditorApplication.applicationContentsPath, $"CGIncludes\\{fileName}")))
                {
                    // we don't want to include and parse unity cg includes
                    return false;
                }
                if (alreadyIncludedThisPass.Contains(fileID))
                {
                    if (!parsedShader.multiIncludeFileCount.ContainsKey(fileID))
                        parsedShader.multiIncludeFileCount[fileID] = 0;
                    parsedShader.multiIncludeFileCount[fileID]++;
                    return true;
                }
                if (--maxIncludes < 0)
                {
                    throw new ParserException("Reached max include depth");
                }
                try
                {
                    rawLines = File.ReadAllLines(currentFilePath);
                }
                catch (FileNotFoundException)
                {
                    if (fileName != "UnityLightingCommon.cginc")
                        Debug.LogWarning("Could not find include file: " + currentFilePath);
                    return false;
                }
                catch (DirectoryNotFoundException)
                {
                    if (isTopLevelFile)
                    {
                        // unity shader files are not assets in the project so we just throw the error again to mark
                        // the parsed shader as failed to read
                        throw new ParserException("This is a unity build in shader. It is not a normal asset and can't be read.");
                    }
                    // happens for example if audio link is not in the project but the shader has a reference to the include file
                    // returning false here will cause the #include directive to be kept in the shader instead of getting inlined
                    Debug.LogWarning("Could not find directory for include file: " + currentFilePath);
                    return false; 
                }
            }
            parsedShader.text[fileID] = processedLines;
            alreadyIncludedThisPass.Add(fileID);
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
                        while (end == -1 && ++lineIndex < rawLines.Length)
                        {
                            trimmedLine += System.Environment.NewLine + rawLines[lineIndex].Trim();
                            end = FindEndOfStringLiteral(trimmedLine, i + 1);
                        }
                        i = end;
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
                        if (endCommentBlock == -1)
                        {
                            while (endCommentBlock == -1 && ++lineIndex < rawLines.Length)
                            {
                                endCommentBlock = rawLines[lineIndex].IndexOf("*/");
                            }
                            if (endCommentBlock != -1)
                            {
                                trimmedLine = trimmedLine.Substring(0, i)
                                    + rawLines[lineIndex].Substring(endCommentBlock + 2);
                                i -= 1;
                            }
                            else
                            {
                                trimmedLine = trimmedLine.Substring(0, i).TrimEnd();
                                break;
                            }
                        }
                        else
                        {
                            trimmedLine = trimmedLine.Substring(0, i)
                                + trimmedLine.Substring(endCommentBlock + 2);
                            i -= 1;
                        }
                    }
                }
                if (trimmedLine == "")
                    continue;
                if (isTopLevelFile && (trimmedLine == "CGINCLUDE" || trimmedLine == "CGPROGRAM" ||trimmedLine == "HLSLINCLUDE" || trimmedLine == "HLSLPROGRAM"))
                {
                    processedLines.Add(trimmedLine);
                    alreadyIncludedThisPass.Clear();
                    // include UnityLightingCommon.cginc at the start of each code block since that declares _SpecColor and we don't want to just include all unity cg includes
                    RecursiveParseFile("UnityLightingCommon.cginc", false, currentFilePath);
                    processedLines.Add("#include \"UnityLightingCommon.cginc\"");
                    continue;
                }
                if (trimmedLine.StartsWith("UsePass"))
                {
                    throw new ParserException("UsePass is not supported.");
                }
                if (trimmedLine.StartsWith("#include "))
                {
                    RecursiveParseFile(ParseIncludeDirective(trimmedLine), false, currentFilePath);
                }
                processedLines.Add(trimmedLine);
            }
            return true;
        }

        public static string ParseIncludeDirective(string line)
        {
            if (!line.StartsWith("#include "))
                return "";
            int firstQuote = line.IndexOf('"');
            int lastQuote = line.LastIndexOf('"');
            if (firstQuote == -1 || lastQuote == -1 || firstQuote == lastQuote)
            {
                firstQuote = line.IndexOf('<');
                lastQuote = line.LastIndexOf('>');
            }
            if (firstQuote == -1 || lastQuote == -1 || firstQuote == lastQuote)
                throw new ParserException("Invalid #include directive.");
            return line.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
        }

        public static (string name, string stringLiteral, string type, string defaultValue)?
        ParsePropertyRaw(string line, List<string> tags)
        {
            int charIndex = 0;
            int startTagIndex = 0;
            int endTagIndex = 0;
            string name = null;
            while (charIndex < line.Length)
            {
                char c = line[charIndex];
                if (c == '[')
                {
                    startTagIndex = charIndex;
                    while (charIndex < line.Length && line[charIndex] != ']')
                    {
                        charIndex++;
                    }
                    tags.Add(line.Substring(startTagIndex + 1, charIndex - startTagIndex - 1).Trim());
                    endTagIndex = charIndex + 1;
                }
                else if (c == '(')
                {
                    name = line.Substring(endTagIndex, charIndex - endTagIndex);
                    break;
                }
                charIndex++;
            }
            if (name == null)
            {
                return null;
            }
            int quoteIndexStart = line.IndexOf('"', charIndex);
            int quoteIndexEnd = FindEndOfStringLiteral(line, quoteIndexStart + 1);
            if (quoteIndexStart == -1 || quoteIndexEnd == -1)
            {
                return null;
            }
            string stringLiteral = line.Substring(quoteIndexStart, quoteIndexEnd - quoteIndexStart + 1);
            int commaIndex = line.IndexOf(',', quoteIndexEnd);
            if (commaIndex == -1)
            {
                return null;
            }
            int equalsIndex = line.LastIndexOf('=');
            if (equalsIndex == -1)
            {
                return null;
            }
            int parenthesesCloseIndex = line.LastIndexOf(')', equalsIndex, equalsIndex - commaIndex);
            if (parenthesesCloseIndex == -1)
            {
                return null;
            }
            string type = line.Substring(commaIndex + 1, parenthesesCloseIndex - commaIndex - 1).Trim();
            string defaultValue = line.Substring(equalsIndex + 1).Trim();
            return (name.Trim(), stringLiteral, type, defaultValue);
        }

        public static ParsedShader.Property ParseProperty(string line, List<string> tags, bool clearTagsOnPropertyParse = true)
        {
            var prop = ParsePropertyRaw(line, tags);
            if (prop == null)
            {
                return null;
            }
            var output = new ParsedShader.Property();
            output.name = prop.Value.name;
            string typeDefinition = prop.Value.type.ToLowerInvariant();
            output.defaultValue = prop.Value.defaultValue;
            if (typeDefinition.StartsWith("range") || typeDefinition.StartsWith("float"))
            {
                output.type = ParsedShader.Property.Type.Float;
                if (output.defaultValue.StartsWith("("))
                {
                    output.type = ParsedShader.Property.Type.Vector;
                    output.defaultValue = "float4" + output.defaultValue;
                }
            }
            else if (typeDefinition.StartsWith("vector"))
            {
                output.type = ParsedShader.Property.Type.Vector;
                output.defaultValue = "float4" + output.defaultValue;
            }
            else if (typeDefinition.StartsWith("int"))
            {
                output.type = ParsedShader.Property.Type.Int;
            }
            else if (typeDefinition.StartsWith("color"))
            {
                output.type = tags.Any(t => t.ToLowerInvariant() == "hdr") ? ParsedShader.Property.Type.ColorHDR : ParsedShader.Property.Type.Color;
                output.defaultValue = "float4" + output.defaultValue;
            }
            else if (typeDefinition.StartsWith("2darray"))
            {
                output.type = ParsedShader.Property.Type.Texture2DArray;
            }
            else if (typeDefinition.StartsWith("2d"))
            {
                output.type = ParsedShader.Property.Type.Texture2D;
                var d = output.defaultValue.Substring(1);
                d = d.Substring(0, d.IndexOf('"'));
                switch (d)
                {
                    case "white": output.defaultValue = "float4(1,1,1,1)"; break;
                    case "black": output.defaultValue = "float4(0,0,0,1)"; break;
                    case "red": output.defaultValue = "float4(1,0,0,1)"; break;
                    case "lineargrey":
                    case "lineargray": output.defaultValue = "float4(0.5,0.5,0.5,1)"; break;
                    case "bump": output.defaultValue = "float4(0.5,0.5,1,1)"; break;
                    case "grey":
                    case "gray":
                    default: output.defaultValue = "float4(0.21582022,0.21582022,0.21582022,1)"; break;
                }
            }
            else if (typeDefinition.StartsWith("3d"))
            {
                output.type = ParsedShader.Property.Type.Texture3D;
                output.defaultValue = "float4(0.21582022,0.21582022,0.21582022,1)";
            }
            else if (typeDefinition.StartsWith("cube"))
            {
                output.type = ParsedShader.Property.Type.TextureCube;
                output.defaultValue = "float4(0.21582022,0.21582022,0.21582022,1)";
            }
            else if (typeDefinition.StartsWith("cubearray"))
            {
                output.type = ParsedShader.Property.Type.TextureCubeArray;
                output.defaultValue = "float4(0.21582022,0.21582022,0.21582022,1)";
            }
            output.hasGammaTag = tags.Any(t => t.ToLowerInvariant() == "gamma");
            if (clearTagsOnPropertyParse)
                tags.Clear();
            return output;
        }

        private static Regex functionParameter = new Regex(
            @"((in|out|inout)\s+)?((const|point|line|triangle)\s+)?(\w+)(\s*<[\w,\s]+>)?\s+((\w+)(\[(\d+)\])?)(\s*:\s*(\w+))?",
            RegexOptions.Compiled);

        public static string ParseIdentifierAndTrailingWhitespace(string str, ref int index)
        {
            int startIndex = index;
            while (index < str.Length && (char.IsLetterOrDigit(str[index]) || str[index] == '_'))
                index++;
            if (index == startIndex || index == str.Length)
                return null;
            int endIndex = index;
            while (index < str.Length && char.IsWhiteSpace(str[index]))
                index++;
            if (index < str.Length && str[index] == '<')
            {
                for (int depth = 1; depth > 0;)
                {
                    if (++index == str.Length)
                        return null;
                    if (str[index] == '<')
                        depth++;
                    else if (str[index] == '>')
                        depth--;
                }
                endIndex = ++index;
                while (index < str.Length && char.IsWhiteSpace(str[index]))
                    index++;
            }
            return str.Substring(startIndex, endIndex - startIndex);
        }

        public static (string name, string returnType) ParseFunctionDefinition(string line)
        {
            int index = 0;
            string returnType = ParseIdentifierAndTrailingWhitespace(line, ref index);
            if (returnType == "inline")
            {
                returnType = ParseIdentifierAndTrailingWhitespace(line, ref index);
            }
            if (returnType == null || returnType == "return" || returnType == "else" || !char.IsWhiteSpace(line[index - 1]))
            {
                return (null, null);
            }
            string name = ParseIdentifierAndTrailingWhitespace(line, ref index);
            if (name == null || index == line.Length || line[index] != '(')
            {
                return (null, null);
            }
            return (name, returnType);
        }

        public static ParsedShader.Function ParseFunctionDefinition(List<string> source, ref int lineIndex)
        {
            var match = ParseFunctionDefinition(source[lineIndex]);
            if (match.name == null)
            {
                return null;
            }
            var func = new ParsedShader.Function();
            func.name = match.name;
            var returnParam = new ParsedShader.Function.Parameter();
            returnParam.isOutput = true;
            returnParam.name = "return";
            returnParam.type = match.returnType;
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
                    if (m.Groups[6].Value != "")
                    {
                        string s = m.Groups[6].Value;
                        param.type += $"<{s.Substring(s.IndexOf('<') + 1, s.IndexOf('>') - s.IndexOf('<') - 1).Trim()}>";
                    }
                    param.name = m.Groups[8].Value;
                    param.arraySize = m.Groups[10].Value != "" ? int.Parse(m.Groups[10].Value) : -1;
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

        public static List<string> ParseFunctionParametersWithPreprocessorStatements(List<string> source, ref int sourceLineIndex)
        {
            var output = new List<string>();
            string line = source[sourceLineIndex].Substring(source[sourceLineIndex].IndexOf('(') + 1);
            while (line != "{" && sourceLineIndex < source.Count - 1)
            {
                line = Regex.Replace(line, @"UNITY_POSITION\s*\(\s*(\w+)\s*\)", "float4 $1 : SV_POSITION");
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
            foreach (var declaration in output)
            {
                if (declaration.StartsWith("#"))
                    continue;
                var match = Regex.Match(declaration, @"^((in|out|inout)\s)?\s*(\w+)\s+(\w+)(\s*:\s*\w+)?");
                if (!match.Success)
                {
                    throw new ParserException("Unknown function parameter declaration: " + declaration);
                }
            }
            return output;
        }

        private static void UpdateFunctionDefinition(ref ParsedShader.Function passFunction, ParsedShader.Function candidate, string name)
        {
            if (candidate.name == passFunction?.name)
            {
                passFunction = candidate;
            }
        }

        private static void UpdateFunctionDefinition(ParsedShader.Function func, ParsedShader.Pass pass)
        {
            UpdateFunctionDefinition(ref pass.vertex, func, "vertex");
            UpdateFunctionDefinition(ref pass.hull, func, "hull");
            UpdateFunctionDefinition(ref pass.domain, func, "domain");
            UpdateFunctionDefinition(ref pass.geometry, func, "geometry");
            UpdateFunctionDefinition(ref pass.fragment, func, "fragment");
        }

        private void ParsePragma(string line, ParsedShader.Pass pass)
        {
            if (!line.StartsWith("#pragma "))
                return;
            line = line.Substring("#pragma ".Length).TrimStart();
            if (line.StartsWith("surface"))
                throw new ParserException("Surface shader is not supported.");
            var match = Regex.Match(line, @"^(vertex|hull|domain|geometry|fragment)\s+(\w+)");
            if (match.Success)
            {
                var funcName = match.Groups[2].Value;
                parsedShader.functions.TryGetValue(funcName, out ParsedShader.Function func);
                func = func ?? new ParsedShader.Function() { name = funcName };
                switch (match.Groups[1].Value)
                {
                    case "vertex":
                        pass.vertex = func;
                        break;
                    case "hull":
                        pass.hull = func;
                        break;
                    case "domain":
                        pass.domain = func;
                        break;
                    case "geometry":
                        pass.geometry = func;
                        break;
                    case "fragment":
                        pass.fragment = func;
                        break;
                }
            }
            match = Regex.Match(line, @"^shader_feature(?:_local)?(?:\s+(\w+))+");
            if (match.Success)
            {
                parsedShader.shaderFeatureKeyWords.UnionWith(
                    match.Groups[1].Captures.Cast<Capture>().Select(c => c.Value));
                pass.shaderFeatureKeyWords.UnionWith(
                    match.Groups[1].Captures.Cast<Capture>().Select(c => c.Value));
            }
        }

        private void ParseCustomFunctionDeclarationMacro(string line)
        {
            if (!line.StartsWith("#define "))
                return;
            if (line.Contains("Texture2D ") || line.Contains("sampler2D "))
            {
                if (!parsedShader.customTextureDeclarations.Contains(line))
                    parsedShader.customTextureDeclarations.Add(line);
            }
        }

        private void ParseFunctionDeclarationsRecursive(List<string> lines, ParsedShader.Pass currentPass, HashSet<string> alreadyParsed = null)
        {
            if (alreadyParsed == null)
                alreadyParsed = new HashSet<string>();
            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var currentLine = lines[lineIndex];
                if (currentLine.StartsWith("#include "))
                {
                    var includeName = ParseIncludeDirective(currentLine);
                    if (!alreadyParsed.Contains(includeName) && parsedShader.text.TryGetValue(includeName, out var includeLines))
                    {
                        alreadyParsed.Add(includeName);
                        ParseFunctionDeclarationsRecursive(includeLines, currentPass, alreadyParsed);
                    }
                    continue;
                }
                ParseCustomFunctionDeclarationMacro(currentLine);
                int tempIndex = lineIndex;
                var func = ParseFunctionDefinition(lines, ref tempIndex);
                if (func != null)
                {
                    parsedShader.functions[func.name] = func;
                    UpdateFunctionDefinition(func, currentPass);
                    if (func.name == currentPass.vertex?.name || func.name == currentPass.fragment?.name)
                    {
                        ParseFunctionParametersWithPreprocessorStatements(lines, ref lineIndex);
                    }
                }
                lineIndex = tempIndex;
            }
        }

        private void PreprocessCodeLines(List<string> lines, ref int lineIndex, List<string> output, ref int curlyBraceDepth)
        {
            while (lineIndex < lines.Count - 1)
            {
                string line = lines[++lineIndex];
                if (line == "ENDCG" || line == "ENDHLSL")
                    break;
                while (!line.EndsWith(";")
                    && !line.EndsWith("]")
                    && !line.StartsWith("#")
                    && !line.StartsWith("{")
                    && !line.StartsWith("}")
                    && lineIndex < lines.Count - 1
                    && lines[lineIndex + 1] != "{"
                    && lines[lineIndex + 1] != "}"
                    && !lines[lineIndex + 1].StartsWith("#")
                    && !lines[lineIndex + 1].StartsWith("return"))
                {
                    line = line + " " + lines[++lineIndex];
                }
                var returnIndex = line.IndexOf("return");
                if (returnIndex > 0 && line.Length > returnIndex + 6 && !line.StartsWith("#"))
                if ((line[returnIndex - 1] == ' ' || line[returnIndex - 1] == '\t' || line[returnIndex - 1] == ';' || line[returnIndex - 1] == ')')
                    && (line[returnIndex + 6] == ' ' || line[returnIndex + 6] == '\t' || line[returnIndex + 6] == ';') || line[returnIndex + 6] == '(')
                {
                    output.Add(line.Substring(0, returnIndex).TrimEnd());
                    line = line.Substring(returnIndex);   
                }
                curlyBraceDepth += line == "{" ? 1 : (line == "}" ? -1 : 0);
                output.Add(line);
            }
        }

        private void SemanticParseShader()
        {
            ParsedShader.Pass currentPass = null;
            List<string> output = new List<string>();
            List<string> cgInclude = new List<string>();
            List<string> hlslInclude = new List<string>();
            List<string> lines = parsedShader.text[".shader"];
            List<string> tags = new List<string>();
            parsedShader.text[".shader"] = output;
            parsedShader.mismatchedCurlyBraces = false;
            int curlyBraceDepth = 0;
            foreach (var key in parsedShader.text.Keys.ToList())
            {
                if (key == ".shader")
                    continue;
                var unprocessedLines = parsedShader.text[key];
                var processedLines = new List<string>();
                int lineIndex = -1;
                PreprocessCodeLines(unprocessedLines, ref lineIndex, processedLines, ref curlyBraceDepth);
                parsedShader.text[key] = processedLines;
                parsedShader.mismatchedCurlyBraces |= curlyBraceDepth != 0;
            }
            var state = ParseState.ShaderLab;
            curlyBraceDepth = 0;
            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                string line = lines[lineIndex];
                switch (state)
                {
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
                            var property = ParseProperty(line, tags);
                            if (property != null)
                            {
                                parsedShader.properties.Add(property);
                            }
                            output.Add(line);
                        }
                        break;
                    case ParseState.Tags:
                        output.Add(line);
                        if (line == "}")
                        {
                            state = ParseState.ShaderLab;
                        }
                        else
                        {
                            var lower = line.ToLower();
                            if (Regex.IsMatch(lower, @"""disablebatching""\s*=\s*""true"""))
                            {
                                parsedShader.hasDisableBatchingTag = true;
                            }
                        }
                        break;
                    case ParseState.ShaderLab:
                        if (line == "Properties")
                        {
                            state = ParseState.PropertyBlock;
                            output.Add(line);
                        }
                        else if (line == "Tags")
                        {
                            state = ParseState.Tags;
                            output.Add(line);
                        }
                        else if (line == "GLSLPROGRAM")
                        {
                            throw new ParserException("GLSLPROGRAM is not supported.");
                        }
                        else if (line == "CGINCLUDE")
                        {
                            PreprocessCodeLines(lines, ref lineIndex, cgInclude, ref curlyBraceDepth);
                        }
                        else if (line == "HLSLINCLUDE")
                        {
                            PreprocessCodeLines(lines, ref lineIndex, hlslInclude, ref curlyBraceDepth);
                        }
                        else if (line == "CGPROGRAM" || line == "HLSLPROGRAM")
                        {
                            currentPass = new ParsedShader.Pass();
                            parsedShader.passes.Add(currentPass);
                            var program = new List<string>(line == "CGPROGRAM" ? cgInclude : hlslInclude);
                            PreprocessCodeLines(lines, ref lineIndex, program, ref curlyBraceDepth);
                            for (int programLineIndex = 0; programLineIndex < program.Count; programLineIndex++)
                            {
                                ParsePragma(program[programLineIndex], currentPass);
                                if (program[programLineIndex].StartsWith("UNITY_INSTANCING_BUFFER_START"))
                                {
                                    throw new ParserException("Shader with instancing is not supported.");
                                }
                            }
                            ParseFunctionDeclarationsRecursive(program, currentPass);
                            output.Add(line);
                            output.AddRange(program);
                            output.Add(line == "CGPROGRAM" ? "ENDCG" : "ENDHLSL");
                        }
                        else
                        {
                            output.Add(line);
                            var matches = Regex.Matches(line, @"\[(\w+)\]");
                            if (matches.Count > 0)
                            {
                                string shaderLabParam = Regex.Match(line, @"^[_a-zA-Z]+").Value;
                                foreach (Match match in matches)
                                {
                                    string propName = match.Groups[1].Value;
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
            foreach (var prop in parsedShader.properties)
            {
                parsedShader.propertyTable[prop.name] = prop;
            }
            parsedShader.mismatchedCurlyBraces |= curlyBraceDepth != 0;
            if (parsedShader.passes.Any(p => p.vertex == null || p.fragment == null))
            {
                throw new ParserException("A pass is missing a vertex or fragment shader.");
            }
        }
    }

    public class ShaderOptimizer
    {
        private List<string> output;
        private Stack<string> includeStack;
        private Stack<bool> canSkipElseStack;
        private List<(string name, List<string> lines)> outputIncludes = new List<(string name, List<string> lines)>();
        private List<string> pragmaOutput;
        private ParsedShader parsedShader;
        private int mergedMeshCount;
        private List<string> mergedMeshNames;
        private HashSet<(string name, bool isVector)> defaultAnimatedProperties;
        private List<int> mergedMeshIndices;
        private int localMeshCount;
        private Dictionary<string, string> staticPropertyValues;
        private Dictionary<string, (string type, List<string> values)> arrayPropertyValues;
        private Dictionary<string, string> animatedPropertyValues;
        private Dictionary<string, string> texturesToNullCheck;
        private HashSet<string> texturesToMerge;
        private HashSet<string> texturesToReplaceCalls;
        private string vertexInUv0Member;
        private string vertexInUv0EndSwizzle = "";
        private HashSet<string> texturesToCallSoTheSamplerDoesntDisappear;
        private List<string> setKeywords;

        private ShaderOptimizer() {}

        public static List<(string name, List<string> lines)> Run(ParsedShader source,
            Dictionary<string, string> staticPropertyValues = null,
            int mergedMeshCount = 0,
            List<string> mergedMeshNames = null,
            HashSet<(string name, bool isVector)> defaultAnimatedProperties = null,
            List<int> mergedMeshIndices = null,
            Dictionary<string, (string type, List<string> values)> arrayPropertyValues = null,
            Dictionary<string, string> texturesToNullCheck = null,
            HashSet<string> texturesToMerge = null,
            Dictionary<string, string> animatedPropertyValues = null,
            List<string> setKeywords = null)
        {
            if (source == null || !source.parsedCorrectly)
                return null;
            mergedMeshIndices = mergedMeshIndices ?? new List<int>();
            if (mergedMeshIndices.Count == 0)
                mergedMeshIndices.Add(0);
            mergedMeshIndices = mergedMeshIndices.Distinct().OrderBy(i => i).ToList();
            if (mergedMeshNames == null)
                mergedMeshNames = Enumerable.Range(0, mergedMeshCount).Select(i => "").ToList();
            var oldCulture = Thread.CurrentThread.CurrentCulture;
            var oldUICulture = Thread.CurrentThread.CurrentUICulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            var optimizer = new ShaderOptimizer
            {
                mergedMeshCount = mergedMeshCount,
                mergedMeshNames = mergedMeshNames,
                defaultAnimatedProperties = defaultAnimatedProperties ?? new HashSet<(string name, bool isVector)>(),
                mergedMeshIndices = mergedMeshIndices,
                localMeshCount = mergedMeshIndices.Last() - mergedMeshIndices.First() + 1,
                staticPropertyValues = staticPropertyValues ?? new Dictionary<string, string>(),
                arrayPropertyValues = arrayPropertyValues ?? new Dictionary<string, (string type, List<string> values)>(),
                parsedShader = source,
                texturesToNullCheck = texturesToNullCheck ?? new Dictionary<string, string>(),
                texturesToMerge = texturesToMerge ?? new HashSet<string>(),
                texturesToCallSoTheSamplerDoesntDisappear = new HashSet<string>(),
                animatedPropertyValues = animatedPropertyValues ?? new Dictionary<string, string>(),
                setKeywords = setKeywords ?? new List<string>()
            };
            optimizer.texturesToReplaceCalls = new HashSet<string>(
                optimizer.texturesToMerge.Union(optimizer.texturesToNullCheck.Keys));
            try
            {
                optimizer.Run();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error optimizing shader {source.name}: {e.Message}\n{e.StackTrace}");
                throw e;
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = oldCulture;
                Thread.CurrentThread.CurrentUICulture = oldUICulture;
            }
            var outputFiles = new List<(string name, List<string> lines)>();
            outputFiles.Add(("Shader", optimizer.output));
            outputFiles.AddRange(optimizer.outputIncludes);
            return outputFiles;
        }

        private void InjectArrayPropertyInitialization()
        {
            foreach (var arrayProperty in arrayPropertyValues)
            {
                var values = arrayProperty.Value.values;
                var seenOnce = new HashSet<string>();
                var seenMultiple = new HashSet<string>();
                foreach (var value in values)
                {
                    if (seenOnce.Contains(value))
                    {
                        seenOnce.Remove(value);
                        seenMultiple.Add(value);
                    }
                    else if (!seenMultiple.Contains(value))
                    {
                        seenOnce.Add(value);
                    }
                }
                if (seenOnce.Count == 1 && seenMultiple.Count == 1)
                {
                    // all values but one are the same, so we can use a ternary operator
                    int index = values.IndexOf(seenOnce.First());
                    output.Add($"{arrayProperty.Key} = d4rkAvatarOptimizer_MaterialID == {index} ? {seenOnce.First()} : {seenMultiple.First()};");
                }
                else
                {
                    // we need to index into the array
                    output.Add($"{arrayProperty.Key} = d4rkAvatarOptimizerArray{arrayProperty.Key}[d4rkAvatarOptimizer_MaterialID];");
                }
            }
        }

        private void InjectAnimatedPropertyInitialization()
        {
            foreach (var animatedProperty in animatedPropertyValues)
            {
                string name = animatedProperty.Key;
                string value = localMeshCount > 1
                    ? $"{CBufferAliasArray[name].name}[{CBufferAliasArray[name].offset} + d4rkAvatarOptimizer_MeshID]"
                    : $"d4rkAvatarOptimizer{name}_ArrayIndex{mergedMeshIndices.First()}";
                output.Add($"{name} = isnan(asfloat(asuint({value}.x) ^ asuint(d4rkAvatarOptimizer_Zero))) ? {name} : {value};");
            }
        }

        private void AddParameterStructWrapper(List<string> funcParams, List<string> output, string name, bool addMeshMaterialID, bool isInput)
        {
            output.Add("struct " + name + "Wrapper");
            output.Add("{");
            if (addMeshMaterialID)
                output.Add("uint d4rkAvatarOptimizer_MeshMaterialID : d4rkAvatarOptimizer_MeshMaterialID;");
            foreach (var line in funcParams)
            {
                if (line.StartsWith("#"))
                {
                    output.Add(line);
                    continue;
                }
                var match = Regex.Match(line, @"^((in|out|inout)\s+)?(?:const\s)?\s*(\w+\s+\w+(\s*:\s*\w+)?)");
                if (match.Success)
                {
                    if (isInput ^ match.Groups[2].Value != "out")
                        continue;
                    if (match.Groups[4].Value.ToLowerInvariant().Contains("vface"))
                        output.Add("uint d4rkAvatarOptimizer_SV_IsFrontFace : SV_IsFrontFace;");
                    else
                        output.Add(match.Groups[3].Value + ";");
                }
                else
                {
                    output.Add($"// raw line: {line}");
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
                var match = Regex.Match(line, @"^((in|out|inout)\s+)?(?:const\s)?\s*(\w+)\s+(\w+)(\s*:\s*\w+)?");
                if (match.Success)
                {
                    var type = match.Groups[3].Value;
                    var name = match.Groups[4].Value;
                    if (match.Groups[5].Value.ToLowerInvariant().Contains("vface"))
                    {
                        output.Add($"{type} {name} = {wrapperName}.d4rkAvatarOptimizer_SV_IsFrontFace ? 1 : -1;");
                    }
                    else if (isInput && match.Groups[2].Value != "out")
                    {
                        output.Add($"{type} {name} = {wrapperName}.{name};");
                    }
                    else if (!isInput && match.Groups[2].Value == "out")
                    {
                        output.Add($"{wrapperName}.{name} = {name};");
                    }
                }
                else
                {
                    output.Add($"// raw line: {line}");
                }
            }
        }

        private void AddOutParametersToFunctionDeclaration(List<string> funcParams, List<string> output)
        {
            foreach (var line in funcParams)
            {
                if (line.StartsWith("#"))
                {
                    output.Add(line);
                    continue;
                }
                var match = Regex.Match(line, @"^((in|out|inout)\s+)?(?:const\s)?\s*(\w+\s+\w+(\s*:\s*\w+))?");
                if (match.Success)
                {
                    if (match.Groups[2].Value == "out")
                        output.Add($", out {match.Groups[3].Value}");
                }
                else
                {
                    output.Add($"// raw line: {line}");
                }
            }
        }

        private void InitializeOutputParameter(List<string> funcParams, List<string> output, bool initOnly)
        {
            foreach (var line in funcParams)
            {
                if (line.StartsWith("#"))
                {
                    output.Add(line);
                    continue;
                }
                var match = Regex.Match(line, @"^((in|out|inout)\s+)?(?:const\s)?\s*(\w+)\s+(\w+)(\s*:\s*\w+)?");
                if (match.Success)
                {
                    if (match.Groups[2].Value == "out")
                    {
                        var type = match.Groups[3].Value;
                        var name = match.Groups[4].Value;
                        output.Add((initOnly ? "" : type + " ") + name + " = (" + type + ")0;");
                    }
                }
                else
                {
                    output.Add(line + " // raw line");
                }
            }
        }

        private void InjectVertexShaderCode(
            List<string> source,
            ref int sourceLineIndex,
            ParsedShader.Pass pass)
        {
            var dummyLineIndex = sourceLineIndex;
            var func = ShaderAnalyzer.ParseFunctionDefinition(source, ref dummyLineIndex);
            var outParam = func.parameters.FirstOrDefault(p => p.isOutput && p.type != "void");
            var inParam = func.parameters.FirstOrDefault(p => p.isInput && p.semantic == null);
            var returnParam = func.parameters[0];
            var isVoidReturn = returnParam.type == "void";
            string nullReturn = isVoidReturn ? "return;" : "return (" + outParam.type + ")0;";
            List<string> funcParams = null;
            List<string> originalVertexShader = null;
            bool needToPassOnMeshOrMaterialID =
                arrayPropertyValues.Count > 0
                || (pass.geometry != null && mergedMeshCount > 1)
                || animatedPropertyValues.Count > 0;
            if (needToPassOnMeshOrMaterialID)
            {
                int startLineIndex = sourceLineIndex;
                funcParams = ShaderAnalyzer.ParseFunctionParametersWithPreprocessorStatements(source, ref sourceLineIndex);
                originalVertexShader = new List<string>(source.GetRange(startLineIndex, sourceLineIndex - startLineIndex + 1));
                if (returnParam.type != "void")
                    funcParams = funcParams.Prepend("out " + returnParam.type + " returnWrappedStruct"
                        + (returnParam.semantic != null ? " : " + returnParam.semantic : "")).ToList();
                AddParameterStructWrapper(funcParams, output, "vertexOutput", true, false);
                AddParameterStructWrapper(funcParams, output, "vertexInput", false, true);
                output.Add("vertexOutputWrapper d4rkAvatarOptimizer_vertexWithWrapper(vertexInputWrapper d4rkAvatarOptimizer_vertexInput)");
                output.Add("{");
                InitializeParameterFromWrapper(funcParams, output, "d4rkAvatarOptimizer_vertexInput", true);
                output.Add($"d4rkAvatarOptimizer_MeshID = ((uint){inParam.name}.{vertexInUv0Member}.z >> 12) - {mergedMeshIndices.First()};");
                output.Add($"d4rkAvatarOptimizer_MaterialID = 0xFFF & (uint){inParam.name}.{vertexInUv0Member}.z;");
                output.Add("vertexOutputWrapper d4rkAvatarOptimizer_vertexOutput = (vertexOutputWrapper)0;");
                output.Add("d4rkAvatarOptimizer_vertexOutput.d4rkAvatarOptimizer_MeshMaterialID = d4rkAvatarOptimizer_MaterialID | (d4rkAvatarOptimizer_MeshID << 16);");
                nullReturn = "return d4rkAvatarOptimizer_vertexOutput;";
            }
            else
            {
                int startLineIndex = sourceLineIndex;
                funcParams = ShaderAnalyzer.ParseFunctionParametersWithPreprocessorStatements(source, ref startLineIndex);
                string line = source[sourceLineIndex];
                output.Add(line);
                while (line != "{" && sourceLineIndex < source.Count - 1)
                {
                    line = source[++sourceLineIndex];
                    output.Add(line);
                }
                if (mergedMeshCount > 1)
                    output.Add($"d4rkAvatarOptimizer_MeshID = ((uint){inParam.name}.{vertexInUv0Member}.z >> 12) - {mergedMeshIndices.First()};");
                if (arrayPropertyValues.Count > 0)
                    output.Add($"d4rkAvatarOptimizer_MaterialID = 0xFFF & (uint){inParam.name}.{vertexInUv0Member}.z;");
            }
            InitializeOutputParameter(funcParams, output, !needToPassOnMeshOrMaterialID);
            InjectDummyCBufferUsage(nullReturn);
            InjectIsActiveMeshCheck(nullReturn);
            InjectArrayPropertyInitialization();
            InjectAnimatedPropertyInitialization();
            int braceDepth = 0;
            while (++sourceLineIndex < source.Count)
            {
                string line = source[sourceLineIndex];
                if (inParam != null && vertexInUv0EndSwizzle != "")
                {
                    line = Regex.Replace(line, $"({inParam.name}\\s*\\.\\s*{vertexInUv0Member})([^0-9a-zA-Z])", $"$1{vertexInUv0EndSwizzle}$2");
                }
                originalVertexShader?.Add(line);
                if (line == "}")
                {
                    if (braceDepth-- == 0)
                    {
                        if (isVoidReturn && needToPassOnMeshOrMaterialID)
                        {
                            output.Add("{");
                            InitializeParameterFromWrapper(funcParams, output, "d4rkAvatarOptimizer_vertexOutput", false);
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
                else if (needToPassOnMeshOrMaterialID && line.StartsWith("return"))
                {
                    output.Add("{");
                    if (!isVoidReturn)
                        output.Add("returnWrappedStruct = " + line.Substring("return ".Length));
                    InitializeParameterFromWrapper(funcParams, output, "d4rkAvatarOptimizer_vertexOutput", false);
                    output.Add("return d4rkAvatarOptimizer_vertexOutput;");
                    output.Add("}");
                }
                else
                {
                    output.Add(line);
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
            ref int sourceLineIndex)
        {
            var dummyLineIndex = sourceLineIndex;
            var func = ShaderAnalyzer.ParseFunctionDefinition(source, ref dummyLineIndex);
            var outParam = func.parameters.FirstOrDefault(p => p.type.Contains("Stream<"));
            var outParamType = outParam.type.Substring(outParam.type.IndexOf('<') + 1);
            outParamType = outParamType.Substring(0, outParamType.Length - 1);
            var inParam = func.parameters.FirstOrDefault(p => p.isInput && p.arraySize >= 0);
            var wrapperStructs = new List<string>();
            bool usesOutputWrapper = animatedPropertyValues.Count > 0 || arrayPropertyValues.Count > 0;
            bool usesInputWrapper = usesOutputWrapper || mergedMeshCount > 1;
            if (usesInputWrapper)
            {
                wrapperStructs.Add("struct geometryInputWrapper");
                wrapperStructs.Add("{");
                wrapperStructs.Add("uint d4rkAvatarOptimizer_MeshMaterialID : d4rkAvatarOptimizer_MeshMaterialID;");
                wrapperStructs.Add(inParam.type + " d4rkAvatarOptimizer_geometryInput;");
                wrapperStructs.Add("};");
            }
            if (usesOutputWrapper)
            {
                wrapperStructs.Add("struct geometryOutputWrapper");
                wrapperStructs.Add("{");
                wrapperStructs.Add("uint d4rkAvatarOptimizer_MeshMaterialID : d4rkAvatarOptimizer_MeshMaterialID;");
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
            string geometryType = inParam.arraySize == 1 ? "point" : inParam.arraySize == 2 ? "line" : "triangle";
            output.Add($"void {func.name}({geometryType} "
                + (usesInputWrapper ? "geometryInputWrapper d4rkAvatarOptimizer_inputWrapper" : inParam.type + " " + inParam.name)
                + $"[{inParam.arraySize}], inout "
                + outParam.type.Substring(0, 7 + outParam.type.IndexOf('S'))
                + (usesOutputWrapper ? "geometryOutputWrapper" : outParamType)
                + "> " + outParam.name
                + string.Join("", func.parameters.Where(p => p.isInput && p != inParam && p != outParam).Select(p => ", " + p))
                + ")");
            output.Add("{");
            if (usesInputWrapper)
            {
                output.Add("d4rkAvatarOptimizer_MaterialID = d4rkAvatarOptimizer_inputWrapper[0].d4rkAvatarOptimizer_MeshMaterialID & 0xFFFF;");
                output.Add("d4rkAvatarOptimizer_MeshID = d4rkAvatarOptimizer_inputWrapper[0].d4rkAvatarOptimizer_MeshMaterialID >> 16;");
                output.Add(inParam.type + " " + inParam.name + "[" + inParam.arraySize + "];");
                for (int i = 0; i < inParam.arraySize; i++)
                {
                    output.Add(inParam.name + "[" + i + "] = d4rkAvatarOptimizer_inputWrapper[" + i + "].d4rkAvatarOptimizer_geometryInput;");
                }
            }
            InjectDummyCBufferUsage("return;");
            InjectIsActiveMeshCheck("return;");
            InjectArrayPropertyInitialization();
            InjectAnimatedPropertyInitialization();
            if (!usesOutputWrapper)
            {
                return;
            }
            int braceDepth = 0;
            while (++sourceLineIndex < source.Count)
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
                    output.Add("d4rkAvatarOptimizer_geomOutput.d4rkAvatarOptimizer_MeshMaterialID = "
                        + "d4rkAvatarOptimizer_MaterialID | (d4rkAvatarOptimizer_MeshID << 16);");
                    output.Add(outParam.name + ".Append(d4rkAvatarOptimizer_geomOutput);");
                    output.Add("}");
                }
                else
                {
                    output.Add(line);
                }
            }
            output.Add("}");
        }

        private void InjectFragmentShaderCode(
            List<string> source,
            ref int sourceLineIndex)
        {
            var dummyLineIndex = sourceLineIndex;
            var func = ShaderAnalyzer.ParseFunctionDefinition(source, ref dummyLineIndex);
            var returnParam = func.parameters[0];
            string nullReturn = returnParam.type == "void" ? "return;" : "return (" + returnParam.type + ")0;";
            if (arrayPropertyValues.Count > 0 || animatedPropertyValues.Count > 0)
            {
                var funcParams = ShaderAnalyzer.ParseFunctionParametersWithPreprocessorStatements(source, ref sourceLineIndex);
                AddParameterStructWrapper(funcParams, output, "fragmentInput", true, true);
                output.Add(func.parameters[0].type + " " + func.name + "(");
                output.Add("fragmentInputWrapper d4rkAvatarOptimizer_fragmentInput");
                AddOutParametersToFunctionDeclaration(funcParams, output);
                if (func.parameters[0].semantic != null)
                    output.Add(") : " + func.parameters[0].semantic);
                else
                    output.Add(")");
                output.Add("{");
                InitializeParameterFromWrapper(funcParams, output, "d4rkAvatarOptimizer_fragmentInput", true);
                InitializeOutputParameter(funcParams, output, true);
                output.Add("d4rkAvatarOptimizer_MaterialID = d4rkAvatarOptimizer_fragmentInput.d4rkAvatarOptimizer_MeshMaterialID & 0xFFFF;");
                output.Add("d4rkAvatarOptimizer_MeshID = d4rkAvatarOptimizer_fragmentInput.d4rkAvatarOptimizer_MeshMaterialID >> 16;");
                InjectDummyCBufferUsage(nullReturn);
                InjectArrayPropertyInitialization();
                InjectAnimatedPropertyInitialization();
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
            if (texturesToCallSoTheSamplerDoesntDisappear.Count > 0)
            {
                output.Add("if (d4rkAvatarOptimizer_Zero)");
                output.Add("{");
                output.Add("float d4rkAvatarOptimizer_sum = 0;");
                foreach (var tex in texturesToCallSoTheSamplerDoesntDisappear)
                {
                    output.Add($"#ifdef DUMMY_USE_TEXTURE_TO_PRESERVE_SAMPLER_{tex}");
                    var texType = parsedShader.properties.Find(p => p.name == tex)?.type;
                    if (texType == ParsedShader.Property.Type.TextureCube
                        || texType == ParsedShader.Property.Type.TextureCubeArray)
                    {
                        output.Add($"d4rkAvatarOptimizer_sum += {tex}.Sample(sampler{tex}, 0).x;");
                    }
                    else
                    {
                        output.Add($"d4rkAvatarOptimizer_sum += {tex}.Load(0).x;");
                    }
                    output.Add("#endif");
                }
                output.Add($"if (d4rkAvatarOptimizer_sum) {nullReturn}");
                output.Add("}");
            }
        }

        private void DuplicateFunctionWithTextureParameter(List<string> source, ref int sourceLineIndex)
        {
            int lineIndex = sourceLineIndex;
            var func = ShaderAnalyzer.ParseFunctionDefinition(source, ref lineIndex);
            if (func == null || texturesToReplaceCalls.Count == 0)
            {
                output.Add(source[sourceLineIndex]);
                return;
            }

            if (!func.parameters.Any(p => p.type == "sampler2D" || p.type.StartsWith("Texture2D")))
            {
                output.Add(source[sourceLineIndex]);
                return;
            }

            string functionDefinitionStart = source[sourceLineIndex].Split('(')[0] + "(";
            var functionDefinition = ShaderAnalyzer.ParseFunctionParametersWithPreprocessorStatements(source, ref sourceLineIndex);
            for (int i = 0; i < functionDefinition.Count; i++)
            {
                if (!functionDefinition[i].StartsWith("#"))
                {
                    functionDefinition[i] = functionDefinition[i] + ",";
                }
            }
            for (int i = functionDefinition.Count - 1; i >= 0; i--)
            {
                if (!functionDefinition[i].StartsWith("#"))
                {
                    functionDefinition[i] = functionDefinition[i].Substring(0, functionDefinition[i].Length - 1);
                    break;
                }
            }
            functionDefinition.Insert(0, functionDefinitionStart);
            functionDefinition.Add(")");
            functionDefinition.Add("{");

            output.AddRange(functionDefinition);
            
            int braceDepth = 0;
            var functionBody = new List<string>();
            while (++sourceLineIndex < source.Count)
            {
                string line = source[sourceLineIndex];
                functionBody.Add(line);
                output.Add(line);
                if (line == "}")
                {
                    if (braceDepth-- == 0)
                    {
                        break;
                    }
                }
                else if (line == "{")
                {
                    braceDepth++;
                }
            }
            foreach (var tex in texturesToReplaceCalls)
            {
                foreach (var line in functionDefinition)
                {
                    output.Add(Regex.Replace(line, "(sampler2D|Texture2D(<[^<>]*>)?) ", tex + "_Wrapper "));
                }
                output.AddRange(functionBody);
            }
        }

        private void InjectDummyCBufferUsage(string nullReturn)
        {
            if (localMeshCount <= 1)
                return;

            var valuesToDummyUse = new Stack<string>();
            valuesToDummyUse.Push("d4rkAvatarOptimizerAnimatedScalars[d4rkAvatarOptimizer_MeshID]");
            if (hasVectorCBufferAliasArray)
                valuesToDummyUse.Push("d4rkAvatarOptimizerAnimatedVectors[d4rkAvatarOptimizer_MeshID].x");
            for (int i = 0; i < mergedMeshIndices.Count; i++)
            {
                valuesToDummyUse.Push($"_IsActiveMesh{mergedMeshIndices[i]}");
            }
            foreach (var animatedProperty in animatedPropertyValues.Keys)
            {
                string propName = $"d4rkAvatarOptimizer{animatedProperty}_ArrayIndex";
                for (int i = 0; i < mergedMeshIndices.Count; i++)
                {
                    valuesToDummyUse.Push($"{propName}{mergedMeshIndices[i]}.x");
                }
            }

            output.Add("if (d4rkAvatarOptimizer_Zero)");
            output.Add("{");
            output.Add($"float d4rkAvatarOptimizer_val = {valuesToDummyUse.Pop()};");
            while (valuesToDummyUse.Count >= 2)
            {
                output.Add($"d4rkAvatarOptimizer_val += {valuesToDummyUse.Pop()} * {valuesToDummyUse.Pop()};");
            }
            if (valuesToDummyUse.Count == 1)
            {
                output.Add($"d4rkAvatarOptimizer_val += {valuesToDummyUse.Pop()};");
            }
            output.Add("if (d4rkAvatarOptimizer_val) " + nullReturn);
            output.Add("}");
        }

        private void InjectIsActiveMeshCheck(string nullReturn)
        {
            if (mergedMeshCount <= 1)
                return;
            if (localMeshCount > 1)
                output.Add($"if (!d4rkAvatarOptimizerAnimatedScalars[d4rkAvatarOptimizer_MeshID]) {nullReturn}");
            else
                output.Add($"if (!_IsActiveMesh{mergedMeshIndices.First()}) {nullReturn}");
        }

        private bool hasVectorCBufferAliasArray = false;
        private Dictionary<string, (string name, int offset)> CBufferAliasArray = new Dictionary<string, (string name, int offset)>();

        private int AllocateCBufferRegisters(int index, HashSet<int> usedRegisters)
        {
            while(mergedMeshIndices.Any(i => usedRegisters.Contains(index + i - mergedMeshIndices.First())))
            {
                index++;
            }
            foreach (int i in mergedMeshIndices)
            {
                usedRegisters.Add(index + i - mergedMeshIndices.First());
            }
            return index;
        }

        private static readonly List<string> SkippedShaderVariants = new List<string> () {
            "DYNAMICLIGHTMAP_ON",
            "LIGHTMAP_ON",
            "LIGHTMAP_SHADOW_MIXING",
            "DIRLIGHTMAP_COMBINED",
            "SHADOWS_SHADOWMASK"
        };

        private void InjectWarningDisables(List<string> target)
        {
            target.Add($"#pragma warning (disable : 3557) // loop only executes for 1 iteration(s), forcing loop to unroll");
            target.Add($"#pragma warning (disable : 4008) // A floating point division by zero occurred.");
        }

        private void InjectPropertyArrays(ParsedShader.Pass pass)
        {
            pragmaOutput.Add($"#pragma skip_variants {string.Join(" ", SkippedShaderVariants)}");
            InjectWarningDisables(pragmaOutput);
            InjectWarningDisables(output);
            hasVectorCBufferAliasArray = false;
            CBufferAliasArray.Clear();
            if (mergedMeshCount > 1)
            {
                var usedScalarRegisters = new HashSet<int>();
                var usedVectorRegisters = new HashSet<int>();
                var scalarOutput = new List<string>();
                var vectorOutput = new List<string>();
                foreach (int i in mergedMeshIndices)
                {
                    scalarOutput.Add($"float _IsActiveMesh{i} : packoffset(c{i - mergedMeshIndices.First()});");
                }
                CBufferAliasArray.Add("_IsActiveMesh", ("d4rkAvatarOptimizerAnimatedScalars", 0));
                int currentScalarPackOffset = AllocateCBufferRegisters(0, usedScalarRegisters);
                int currentVectorPackOffset = 0;
                foreach (var animatedProperty in animatedPropertyValues)
                {
                    string name = animatedProperty.Key;
                    string type = animatedProperty.Value;
                    var currentOutput = scalarOutput;
                    var currentPackOffset = 0;
                    type = Regex.Replace(type, "^(bool|int|uint)([1-4]?)$", "float$2");
                    if (type[type.Length - 1] == '4')
                    {
                        hasVectorCBufferAliasArray = true;
                        currentOutput = vectorOutput;
                        currentPackOffset = currentVectorPackOffset = AllocateCBufferRegisters(currentVectorPackOffset, usedVectorRegisters);
                        CBufferAliasArray.Add(name, ("d4rkAvatarOptimizerAnimatedVectors", currentPackOffset));
                    }
                    else
                    {
                        currentPackOffset = currentScalarPackOffset = AllocateCBufferRegisters(currentScalarPackOffset, usedScalarRegisters);
                        CBufferAliasArray.Add(name, ("d4rkAvatarOptimizerAnimatedScalars", currentPackOffset));
                    }
                    foreach (int i in mergedMeshIndices)
                    {
                        currentOutput.Add($"{type} d4rkAvatarOptimizer{name}_ArrayIndex{i} : packoffset(c{currentPackOffset + i - mergedMeshIndices.First()});");
                    }
                }
                output.Add("cbuffer d4rkAvatarOptimizerAnimatedScalars");
                output.Add("{");
                if (localMeshCount > 1)
                    output.Add($"float d4rkAvatarOptimizerAnimatedScalars[{usedScalarRegisters.Max() + 1}] : packoffset(c0);");
                output.AddRange(scalarOutput);
                output.Add("};");
                if (hasVectorCBufferAliasArray)
                {
                    output.Add("cbuffer d4rkAvatarOptimizerAnimatedVectors");
                    output.Add("{");
                    if (localMeshCount > 1)
                        output.Add($"float4 d4rkAvatarOptimizerAnimatedVectors[{usedVectorRegisters.Max() + 1}] : packoffset(c0);");
                    output.AddRange(vectorOutput);
                    output.Add("};");
                }
            }
            var staticParamDefines = new HashSet<(string type, string name)>();
            staticParamDefines.UnionWith(
                animatedPropertyValues.Select(p => (p.Value, p.Key))
                .Union(arrayPropertyValues.Select(p => (p.Value.type, p.Key)))
            );
            foreach (var (type, name) in staticParamDefines)
            {
                if (!staticPropertyValues.TryGetValue(name, out var value))
                {
                    value = "0";
                }
                var varName = name;
                if (name == "_MainTex_ST" && texturesToMerge.Contains("_MainTex"))
                    varName = "_MainTexButNotQuiteSoThatUnityDoesntCry_ST";
                output.Add("static " + type + " " + varName + " = " + value + ";");
            }
            foreach(var keyword in setKeywords.Where(k => pass.shaderFeatureKeyWords.Contains(k)))
            {
                output.Add("#define " + keyword);
            }
            output.Add("uniform float d4rkAvatarOptimizer_Zero;");
            output.Add("static uint d4rkAvatarOptimizer_MaterialID = 0;");
            output.Add("static uint d4rkAvatarOptimizer_MeshID = 0;");
            if (arrayPropertyValues.Count > 0)
            {
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
                }
            }
            foreach (var property in staticPropertyValues)
            {
                if (property.Key.StartsWith("arrayIndex") && texturesToMerge.Contains(property.Key.Substring(10)))
                {
                    output.Add("static float " + property.Key + " = " + property.Value + ";");
                }
            }
            int textureWrapperCount = 0;
            foreach (var texName in texturesToReplaceCalls)
            {
                if (texturesToNullCheck.TryGetValue(texName, out string nullCheck))
                {
                    nullCheck = "if (!shouldSample" + texName + ") return " + nullCheck + ";";
                }

                bool isArray = texturesToMerge.Contains(texName);
                string uv = isArray ? "float3(uv, arrayIndex" + texName + ")" : "uv";

                string newTexName = texName;
                string type = "float4";

                if (isArray && texName == "_MainTex")
                {
                    newTexName = "_MainTexButNotQuiteSoThatUnityDoesntCry";
                    output.Add("#define _MainTex_ST _MainTexButNotQuiteSoThatUnityDoesntCry_ST");
                    output.Add("#define sampler_MainTex sampler_MainTexButNotQuiteSoThatUnityDoesntCry");
                }

                texturesToCallSoTheSamplerDoesntDisappear.Add(newTexName);
                output.Add("#define DUMMY_USE_TEXTURE_TO_PRESERVE_SAMPLER_" + newTexName);

                string textureType = $"Texture2D" + (isArray ? "Array" : "") + $"<{type}>";
                output.Add($"{textureType} {newTexName};");
                output.Add($"SamplerState sampler{newTexName};");

                output.Add($"class {texName}_Wrapper {{");
                output.Add($"float memberToDifferentiateWrapperClasses[{++textureWrapperCount}];");

                output.Add($"{type} Sample(SamplerState sampl, float2 uv) {{");
                if (nullCheck != null) output.Add(nullCheck);
                output.Add($"return {newTexName}.Sample(sampl, {uv});}}");

                output.Add($"{type} SampleGrad(SamplerState sampl, float2 uv, float2 ddxuv, float2 ddyuv) {{");
                if (nullCheck != null) output.Add(nullCheck);
                output.Add($"return {newTexName}.SampleGrad(sampl, {uv}, ddxuv, ddyuv);}}");

                output.Add($"{type} SampleLevel(SamplerState sampl, float2 uv, int mipLevel) {{");
                if (nullCheck != null) output.Add(nullCheck);
                output.Add($"return {newTexName}.SampleLevel(sampl, {uv}, mipLevel);}}");

                output.Add($"{type} SampleBias(SamplerState sampl, float2 uv, float bias) {{");
                if (nullCheck != null) output.Add(nullCheck);
                output.Add($"return {newTexName}.SampleBias(sampl, {uv}, bias);}}");

                output.Add($"{type} Load(uint3 uv) {{");
                if (nullCheck != null) output.Add(nullCheck);
                if (isArray) output.Add($"return {newTexName}.Load(int4(uv.xy, arrayIndex{texName}, uv.z));}}");
                else output.Add($"return {newTexName}.Load(uv);}}");

                output.Add("void GetDimensions(out float width, out float height) {");
                if (nullCheck != null) output.Add($"if (!shouldSample{texName}) {{ width = 4; height = 4; return; }}");
                if (isArray) output.Add($"float dummy;{newTexName}.GetDimensions(width, height, dummy);}}");
                else output.Add($"{newTexName}.GetDimensions(width, height);}}");

                output.Add("void GetDimensions(out uint width, out uint height) {");
                if (nullCheck != null) output.Add($"if (!shouldSample{texName}) {{ width = 4; height = 4; return; }}");
                if (isArray) output.Add($"uint dummy;{newTexName}.GetDimensions(width, height, dummy);}}");
                else output.Add($"{newTexName}.GetDimensions(width, height);}}");

                output.Add("void GetDimensions(uint mipLevel, out float width, out float height, out float numberOfLevels) {");
                if (nullCheck != null) output.Add($"if (!shouldSample{texName}) {{ width = 4; height = 4; numberOfLevels = 1; return; }}");
                if (isArray) output.Add($"float dummy;{newTexName}.GetDimensions(mipLevel, width, height, dummy, numberOfLevels);}}");
                else output.Add($"{newTexName}.GetDimensions(mipLevel, width, height, numberOfLevels);}}");

                output.Add("void GetDimensions(uint mipLevel, out uint width, out uint height, out uint numberOfLevels) {");
                if (nullCheck != null) output.Add($"if (!shouldSample{texName}) {{ width = 4; height = 4; numberOfLevels = 1; return; }}");
                if (isArray) output.Add($"uint dummy;{newTexName}.GetDimensions(mipLevel, width, height, dummy, numberOfLevels);}}");
                else output.Add($"{newTexName}.GetDimensions(mipLevel, width, height, numberOfLevels);}}");

                output.Add("};");

                output.Add($"{type} tex2D({texName}_Wrapper wrapper, float2 uv) {{");
                output.Add($"return wrapper.Sample(sampler{texName}, uv);}}");

                output.Add($"{type} tex2Dproj({texName}_Wrapper wrapper, float4 uv) {{");
                output.Add($"return wrapper.Sample(sampler{texName}, uv.xy / uv.w);}}");

                output.Add($"{type} tex2D({texName}_Wrapper wrapper, float2 uv, float2 ddxuv, float2 ddyuv) {{");
                output.Add($"return wrapper.SampleGrad(sampler{texName}, uv, ddxuv, ddyuv);}}");

                output.Add($"{type} tex2Dgrad({texName}_Wrapper wrapper, float2 uv, float2 ddxuv, float2 ddyuv) {{");
                output.Add($"return wrapper.SampleGrad(sampler{texName}, uv, ddxuv, ddyuv);}}");

                output.Add($"{type} tex2Dlod({texName}_Wrapper wrapper, float4 uv) {{");
                output.Add($"return wrapper.SampleLevel(sampler{texName}, uv.xy, uv.w);}}");

                output.Add($"{type} tex2Dbias({texName}_Wrapper wrapper, float4 uv) {{");
                output.Add($"return wrapper.SampleBias(sampler{texName}, uv.xy, uv.w);}}");

                output.Add($"static {texName}_Wrapper {texName}_Wrapper_Instance;");
                output.Add($"#define {texName} {texName}_Wrapper_Instance");
                output.Add($"#define {texName}_Wrapper_Instance_ST {texName}_ST");
                output.Add($"#define sampler{texName}_Wrapper_Instance sampler{texName}");
                output.Add($"#define {texName}_Wrapper_Instance_TexelSize {texName}_TexelSize");
            }
        }

        private static (string type, List<string> names) ParseVariableDeclaration(string line)
        {
            if (line[line.Length - 1] != ';')
                return (null, null);
            int index = 0;
            string identifier = ShaderAnalyzer.ParseIdentifierAndTrailingWhitespace(line, ref index);
            if (identifier == "uniform")
            {
                identifier = ShaderAnalyzer.ParseIdentifierAndTrailingWhitespace(line, ref index);
            }
            if (identifier == null || identifier == "return")
                return (null, null);
            string type = identifier;
            identifier = ShaderAnalyzer.ParseIdentifierAndTrailingWhitespace(line, ref index);
            if (identifier == null)
                return (null, null);
            var names = new List<string>() { identifier };
            while (line[index] == ',')
            {
                index++;
                while (char.IsWhiteSpace(line[index]))
                    index++;
                identifier = ShaderAnalyzer.ParseIdentifierAndTrailingWhitespace(line, ref index);
                if (identifier == null)
                    return (null, null);
                names.Add(identifier);
            }
            if (line[index] != ';')
                return (null, null);
            return (type, names);
        }

        private Dictionary<string, (bool defined, int? value)> knownConstants;

        private void ParseCodeLinesRecursive(List<string> source, ref int sourceLineIndex, ParsedShader.Pass pass, string endSymbol)
        {
            for (; sourceLineIndex < source.Count; sourceLineIndex++)
            {
                var line = source[sourceLineIndex];
                if (line == endSymbol)
                    return;
                if (line[0] == '#')
                {
                    (bool value, bool error) EvalPreprocessorCondition(string expr)
                    {
                        // only parse flat expressions like "defined(SYMBOL)" or "!defined(SYMBOL)" for now
                        if (expr.StartsWith("defined(") && expr.EndsWith(")"))
                        {
                            var symbol = expr.Substring(8, expr.Length - 9).Trim();
                            if (knownConstants.TryGetValue(symbol, out var known))
                                return (known.defined, false);
                            output.Add($"// Unknown symbol: {symbol}");
                            return (false, true);
                        }
                        if (expr.StartsWith("!defined(") && expr.EndsWith(")"))
                        {
                            var symbol = expr.Substring(9, expr.Length - 10).Trim();
                            if (knownConstants.TryGetValue(symbol, out var known))
                                return (!known.defined, false);
                            output.Add($"// Unknown symbol: {symbol}");
                            return (false, true);
                        }
                        output.Add($"// Could not evaluate: {expr}");
                        return (false, true);
                    }
                    void SkipUntilElseOrEndif(ref int lineIndex)
                    {
                        int depth = 0;
                        int startLineIndex = lineIndex;
                        while (lineIndex < source.Count)
                        {
                            var innerLine = source[++lineIndex];
                            if (innerLine[0] != '#')
                                continue;
                            var innerSubLine = innerLine.Substring(1);
                            if (innerSubLine.StartsWith("if"))
                            {
                                depth++;
                            }
                            else if (innerSubLine.StartsWith("endif"))
                            {
                                if (depth == 0)
                                {
                                    lineIndex--;
                                    output.Add($"// Skipped {lineIndex - startLineIndex} lines");
                                    return;
                                }
                                depth--;
                            }
                            else if ((innerSubLine.StartsWith("else") || innerSubLine.StartsWith("elif")) && depth == 0)
                            {
                                lineIndex--;
                                output.Add($"// Skipped {lineIndex - startLineIndex} lines");
                                return;
                            }
                        }
                    }
                    var subLine = line.Substring(1);
                    if (subLine.StartsWith("include "))
                    {
                        var includeName = ShaderAnalyzer.ParseIncludeDirective(line);
                        if (!includeStack.Contains(includeName) && parsedShader.text.TryGetValue(includeName, out var includeSource))
                        {
                            int innerLineIndex = 0;
                            output.Add($"// Include {includeName}");
                            includeStack.Push(includeName);
                            ParseCodeLinesRecursive(includeSource, ref innerLineIndex, pass, endSymbol);
                            includeStack.Pop();
                        }
                        else
                        {
                            output.Add(line);
                        }
                    }
                    else if (subLine.StartsWith("if"))
                    {
                        string expr = "";
                        if (subLine.StartsWith("ifdef"))
                            expr = $"defined({subLine.Substring("ifdef".Length).Trim()})";
                        else if (subLine.StartsWith("ifndef"))
                            expr = $"!defined({subLine.Substring("ifndef".Length).Trim()})";
                        else
                            expr = subLine.Substring("if".Length).Trim();
                        var (value, error) = EvalPreprocessorCondition(expr);
                        canSkipElseStack.Push(!error && value);
                        bool skip = false;
                        if (!error && !value)
                        {
                            SkipUntilElseOrEndif(ref sourceLineIndex);
                            if (source[sourceLineIndex + 1].StartsWith("#endif"))
                            {
                                output[output.Count - 1] += $" | {line}";
                                sourceLineIndex++;
                                skip = true;
                            }
                        }
                        if (!skip)
                            output.Add(line);
                    }
                    else if (subLine.StartsWith("else") || subLine.StartsWith("elif"))
                    {
                        if (canSkipElseStack.Peek())
                        {
                            SkipUntilElseOrEndif(ref sourceLineIndex);
                        }
                        if (subLine.StartsWith("elif"))
                        {
                            string expr = subLine.Substring("elif".Length).Trim();
                            var (value, error) = EvalPreprocessorCondition(expr);
                            canSkipElseStack.Pop();
                            canSkipElseStack.Push(!error && value);
                            if (!error && !value)
                            {
                                SkipUntilElseOrEndif(ref sourceLineIndex);
                            }
                        }
                        output.Add(line);
                    }
                    else if (subLine.StartsWith("endif"))
                    {
                        canSkipElseStack.Pop();
                        output.Add(line);
                    }
                    else if (subLine.StartsWith("pragma"))
                    {
                        if (((pass.geometry != null && mergedMeshCount > 1) || arrayPropertyValues.Count > 0 || animatedPropertyValues.Count > 0)
                            &&  Regex.IsMatch(line, @"^#pragma\s+vertex\s+\w+"))
                        {
                            pragmaOutput.Add("#pragma vertex d4rkAvatarOptimizer_vertexWithWrapper");
                        }
                        else if (!Regex.IsMatch(line, @"^#pragma\s+shader_feature") && !Regex.IsMatch(line, @"^#pragma\s+skip_optimizations"))
                        {
                            pragmaOutput.Add(line);
                        }
                    }
                    else
                    {
                        output.Add(line);
                    }
                    continue;
                }
                var func = ShaderAnalyzer.ParseFunctionDefinition(line);
                if (pass.vertex != null && func.name == pass.vertex.name)
                {
                    InjectVertexShaderCode(source, ref sourceLineIndex, pass);
                }
                else if (pass.geometry != null && pass.fragment != null && pass.vertex != null && func.name == pass.geometry.name)
                {
                    InjectGeometryShaderCode(source, ref sourceLineIndex);
                }
                else if (pass.vertex != null && pass.fragment != null && func.name == pass.fragment.name)
                {
                    InjectFragmentShaderCode(source, ref sourceLineIndex);
                }
                else if (func.name != null)
                {
                    DuplicateFunctionWithTextureParameter(source, ref sourceLineIndex);
                }
                else if ((arrayPropertyValues.Count > 0 || mergedMeshCount > 1) && line.StartsWith("struct "))
                {
                    output.Add(line);
                    var match = Regex.Match(line, @"struct\s+(\w+)");
                    if (match.Success)
                    {
                        var structName = match.Groups[1].Value;
                        var vertIn = pass.vertex.parameters.FirstOrDefault(p => p.isInput && p.semantic == null);
                        if (structName == vertIn?.type)
                        {
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
                                        if (type == "float2") vertexInUv0EndSwizzle = ".xy";
                                        else if (type == "float3") vertexInUv0EndSwizzle = ".xyz";
                                        else vertexInUv0EndSwizzle = "";
                                        vertexInUv0Member = match.Groups[3].Value;
                                        output.Add($"//{line}");
                                        continue;
                                    }
                                }
                                if (line.StartsWith("}"))
                                {
                                    output.Add($"float4 {vertexInUv0Member} : TEXCOORD0;");
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
                    var match = ParseVariableDeclaration(line);
                    if (match.type != null)
                    {
                        var type = match.type;
                        foreach (var name in match.names)
                        {
                            bool isTextureSamplerState = (type == "SamplerState" || type == "sampler") && name.StartsWith("sampler");
                            if (isTextureSamplerState && name.StartsWith("sampler") && !texturesToReplaceCalls.Contains(name.Substring("sampler".Length)))
                            {
                                if (parsedShader.properties.Any(p => p.name == name.Substring("sampler".Length)))
                                {
                                    texturesToCallSoTheSamplerDoesntDisappear.Add(name.Substring("sampler".Length));
                                    output.Add("#define DUMMY_USE_TEXTURE_TO_PRESERVE_SAMPLER_" + name.Substring("sampler".Length));
                                }
                                output.Add(type + " " + name + ";");
                            }
                            else if (staticPropertyValues.TryGetValue(name, out string value)
                                && !animatedPropertyValues.ContainsKey(name)
                                && !arrayPropertyValues.ContainsKey(name))
                            {
                                output.Add("static " + type + " " + name + " = " + value + ";");
                            }
                            else if (!arrayPropertyValues.ContainsKey(name)
                                && !animatedPropertyValues.ContainsKey(name)
                                && !texturesToReplaceCalls.Contains(name)
                                && !(isTextureSamplerState && texturesToReplaceCalls.Contains(name.Substring("sampler".Length))))
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
                                texturesToCallSoTheSamplerDoesntDisappear.Add(texName);
                                output.Add("#define DUMMY_USE_TEXTURE_TO_PRESERVE_SAMPLER_" + texName);
                            }
                            output.Add(line);
                        }
                    }
                    else
                    {
                        output.Add(line);
                    }
                }
            }
        }

        private string GetMD5Hash(List<string> lines)
        {
            using (var md5 = MD5.Create())
            {
                md5.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
                return string.Join("", md5.Hash.Select(b => b.ToString("x2")));
            }
        }

        private List<string> Run()
        {
            includeStack = new Stack<string>();
            canSkipElseStack = new Stack<bool>();
            knownConstants = new Dictionary<string, (bool defined, int? value)>();
            output = new List<string>();
            var tags = new List<string>();
            var lines = parsedShader.text[".shader"];
            var propertyBlock = new List<string>();
            int propertyBlockInsertionIndex = 0;
            int propertyBlockStartParseIndex = 0; 
            int lineIndex = 0;
            while (lineIndex < lines.Count)
            {
                string line = lines[lineIndex++];
                output.Add(line);
                if (line == "Properties")
                {
                    break;
                }
            }
            while (lineIndex < lines.Count)
            {
                string line = lines[lineIndex++];
                if (line == "}")
                {
                    output.Add(line);
                    break;
                }
                else if (line == "{")
                {
                    output.Add(line);
                    propertyBlockStartParseIndex = lineIndex;
                    propertyBlockInsertionIndex = output.Count;
                    var alreadyAdded = new HashSet<string>();
                    if (mergedMeshCount > 1)
                    foreach (int i in mergedMeshIndices)
                    {
                        var name = $"_IsActiveMesh{i}";
                        propertyBlock.Add($"{name}(\"{name} {mergedMeshNames[i]}\", Float) = 1");
                        alreadyAdded.Add(name);
                    }
                    foreach (var animatedProperty in animatedPropertyValues)
                    {
                        var prop = parsedShader.properties.FirstOrDefault(p => p.name == animatedProperty.Key);
                        string defaultValue = "0";
                        string type = prop.type.ToString();
                        if (prop.type == ParsedShader.Property.Type.Color || prop.type == ParsedShader.Property.Type.ColorHDR || prop.type == ParsedShader.Property.Type.Vector)
                        {
                            defaultValue = "(0,0,0,0)";
                            type = "Vector";
                        }
                        foreach (int i in mergedMeshIndices)
                        {
                            var fullPropertyName = $"d4rkAvatarOptimizer{prop.name}_ArrayIndex{i}";
                            propertyBlock.Add($"{fullPropertyName}(\"{prop.name} {i}\", {type}) = {defaultValue}");
                            alreadyAdded.Add(fullPropertyName);
                        }
                    }
                    foreach (var defaultAnimatedProperty in defaultAnimatedProperties)
                    {
                        if (alreadyAdded.Contains(defaultAnimatedProperty.name))
                            continue;
                        string defaultValue = defaultAnimatedProperty.isVector ? "(0,0,0,0)" : "0";
                        string type = defaultAnimatedProperty.isVector ? "Vector" : "Float";
                        string name = defaultAnimatedProperty.name;
                        if (name.StartsWith("_IsActiveMesh"))
                        {
                            int meshIndex = int.Parse(name.Substring("_IsActiveMesh".Length));
                            name = $"_IsActiveMesh{meshIndex} {mergedMeshNames[meshIndex]}";
                        }
                        propertyBlock.Add($"{defaultAnimatedProperty.name}(\"{name}\", {type}) = {defaultValue}");
                    }
                }
            }
            int passID = -1;
            var lightModeToDefine = new  Dictionary<string, string>()
            {
                {"ForwardBase", "UNITY_PASS_FORWARDBASE"},
                {"ForwardAdd", "UNITY_PASS_FORWARDADD"},
                {"ShadowCaster", "UNITY_PASS_SHADOWCASTER"},
                {"Meta", "UNITY_PASS_META"},
            };
            for (; lineIndex < lines.Count; lineIndex++)
            {
                string line = lines[lineIndex];
                if (line.StartsWith("CustomEditor"))
                    continue;
                output.Add(line);
                if (line == "Pass")
                {
                    knownConstants.Clear();
                    knownConstants["UNITY_COLORSPACE_GAMMA"] = (false, null);
                }
                else if (line.IndexOf("\"LightMode\"") != -1)
                {
                    var lightMode = line.Substring(line.IndexOf("\"LightMode\"") + "\"LightMode\"".Length).Trim();
                    lightMode = lightMode.Substring(lightMode.IndexOf('"') + 1);
                    lightMode = lightMode.Substring(0, lightMode.IndexOf('"'));
                    foreach (var lightModeDefine in lightModeToDefine)
                    {
                        knownConstants[lightModeDefine.Value] = (lightMode == lightModeDefine.Key, null);
                    }
                }
                else if (line == "CGPROGRAM" || line == "HLSLPROGRAM")
                {
                    var pass = parsedShader.passes[++passID];
                    vertexInUv0Member = "texcoord";
                    texturesToCallSoTheSamplerDoesntDisappear.Clear();
                    pragmaOutput = output;
                    output = new List<string>();
                    InjectPropertyArrays(pass);
                    foreach (var keyword in pass.shaderFeatureKeyWords)
                    {
                        knownConstants[keyword] = (setKeywords.Contains(keyword), null);
                    }
                    foreach (var skippedShaderVariant in SkippedShaderVariants)
                    {
                        knownConstants[skippedShaderVariant] = (false, null);
                    }
                    includeStack.Clear();
                    string endSymbol = line == "CGPROGRAM" ? "ENDCG" : "ENDHLSL";
                    lineIndex++;
                    ParseCodeLinesRecursive(lines, ref lineIndex, pass, endSymbol);
                    var includeName = $"ZZZ{GetMD5Hash(output)}" + (line == "CGPROGRAM" ? ".cginc" : ".hlsl");
                    outputIncludes.Add((includeName, output));
                    output = pragmaOutput;
                    output.Add($"#include \"{includeName}\"");
                    output.Add(endSymbol);
                }
            }
            tags.Clear();
            for (lineIndex = propertyBlockStartParseIndex; lineIndex < lines.Count; lineIndex++)
            {
                string line = lines[lineIndex];
                if (line == "}")
                    break;
                var parsedProperty = ShaderAnalyzer.ParsePropertyRaw(line, tags);
                if (parsedProperty == null)
                    continue;
                var prop = parsedProperty.Value;
                if (prop.name.StartsWith("_ShaderOptimizer"))
                    continue;
                if (texturesToMerge.Contains(prop.name))
                {
                    int index = prop.type.LastIndexOf("2D");
                    prop.type = prop.type.Substring(0, index) + "2DArray" + prop.type.Substring(index + 2);
                    if (prop.name == "_MainTex")
                        prop.name = "_MainTexButNotQuiteSoThatUnityDoesntCry";
                }
                string tagString = "";
                foreach (var tag in tags)
                {
                    if (tag.Length > 5 || (tag.ToLowerInvariant() != "hdr" && tag.ToLowerInvariant() != "gamma"))
                        continue;
                    tagString += $"[{tag}] ";
                }
                propertyBlock.Add($"{tagString}{prop.name}(\"{prop.name}\", {prop.type}) = {prop.defaultValue}");
                tags.Clear();
            }
            output.InsertRange(propertyBlockInsertionIndex, propertyBlock);
            return output;
        }
    }
}
#endif