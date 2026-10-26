#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using d4rkpl4y3r.AvatarOptimizer.Util;
using d4rkpl4y3r.AvatarOptimizer.Extensions;

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
                Integer,
                Texture2D,
                Texture2DArray,
                Texture3D,
                TextureCube,
                TextureCubeArray
            }
            public string name;
            public string displayName;
            public Type type = Type.Unknown;
            public HashSet<string> shaderLabParams = new();
            public string defaultValue;
            public bool hasGammaTag = false;
            public bool doNotLock = false;
            public HashSet<string> shaderKeywords = new();
        }
        public class Function
        {
            public class Parameter
            {
                public string type;
                public string name;
                public string semantic;
                public string defaultValue;
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
            public List<Parameter> parameters = new();
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
            public HashSet<string> shaderFeatureKeyWords = new();
            public Dictionary<string, string> tags = new();
            public string name;
            public int startLineIndex;
            public int lineCount;
            // relative to the start of the pass
            public int codeBlockStartIndex;
            public int codeBlockLineCount;
        }
        public string name;
        public string filePath;
        public bool parsedCorrectly = false;
        public bool couldFindAllVertexGeomFragmentFunctions = true;
        public bool hasEmptyVertexFunction = false;
        public string errorMessage = "";
        public List<string> customTextureDeclarations = new();
        public Dictionary<string, int> multiIncludeFileCount = new();
        public bool mismatchedCurlyBraces = false;
        public Dictionary<string, List<string>> text = new();
        public List<Property> properties = new();
        public List<Property> propertiesToCheckWhenMerging = new();
        public List<Property> texture2DProperties = new();
        public Dictionary<string, Property> propertyTable = new();
        public Dictionary<string, Property> keywordToProperty = new();
        public List<Pass> passes = new();
        public Dictionary<string, Function> functions = new();
        public HashSet<string> shaderFeatureKeyWords = new();
        public HashSet<string> ifexParameters = new();
        public HashSet<string> unableToParseIfexStatements = new();
        public List<string> unknownOptimizerComments = new();
        public HashSet<string> requiredConstantProperties = new();
        public HashSet<string> noConstantFoldingProperties = new();
        public List<string> parserWarnings = new();

        public bool CanMerge()
        {
            if (!parsedCorrectly)
                return false;
            if (passes.Any(p => p.hull != null || p.domain != null))
                return false;
            if (!couldFindAllVertexGeomFragmentFunctions)
                return false;
            if (hasEmptyVertexFunction)
                return false;
            return true;
        }

        public string CantMergeReason()
        {
            if (!parsedCorrectly)
                return errorMessage == "" ? "Shader has not parsed correctly." : errorMessage;
            if (passes.Any(p => p.hull != null || p.domain != null))
                return "Shader has a pass with tessellation.";
            if (!couldFindAllVertexGeomFragmentFunctions)
                return "Optimizer could not find all vertex/geometry/fragment functions.";
            if (hasEmptyVertexFunction)
                return "Shader has a vertex function with no input & output.";
            return "";
        }

        public bool CanMergeTextures()
        {
            if (!CanMerge())
                return false;
            if (customTextureDeclarations.Count > 0)
                return false;
            return true;
        }

        public string CantMergeTexturesReason()
        {
            if (!CanMerge())
                return CantMergeReason();
            if (customTextureDeclarations.Count > 0)
                return "Shader has custom texture declaration macros.";
            return "";
        }
    }

    public class ShaderAnalyzer
    {
        public class ParserException : System.Exception
        {
            public ParserException(string message) : base(message) { }
        }

        private static Dictionary<string, ParsedShader> parsedShaderCache = new();

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
        private bool replaceCoreTextureMacros = false;

        private ShaderAnalyzer(string shaderName, string shaderPath)
        {
            parsedShader = new ParsedShader();
            parsedShader.name = shaderName;
            filePath = Path.GetFullPath(shaderPath);
            parsedShader.filePath = filePath;
            maxIncludes = 1000;
            if (shaderPath.EndsWith(".orlshader") || shaderPath.EndsWith(".orlconfshader"))
            {
                #if ORLSHADER_EXISTS
                Profiler.StartSection("ORL.ShaderGenerator");
                try
                {
                    shaderFileLines = ORL.ShaderGenerator.ShaderDefinitionImporter.GenerateShader(shaderPath)
                        .Split(new string[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.RemoveEmptyEntries);
                }
                catch (IOException e)
                {
                    parsedShader.errorMessage = e.Message;
                }
                replaceCoreTextureMacros = true;
                Profiler.EndSection();
                #else
                parsedShader.errorMessage = "ORLShader Generator 7.1+ is not installed.";
                #endif
            }
            else if (shaderPath.StartsWithSimple("Resources/unity_builtin") || shaderName == "Hidden/InternalErrorShader")
            {
                parsedShader.errorMessage = "Built-in Unity shaders are not supported.";
            }
            else if (!shaderPath.EndsWith(".shader"))
            {
                parsedShader.errorMessage = $"Unsupported shader file type: {Path.GetExtension(shaderPath)}";
            }
            parsedShader.parsedCorrectly = string.IsNullOrEmpty(parsedShader.errorMessage);
            doneParsing = !parsedShader.parsedCorrectly;
        }

        private ParsedShader Parse()
        {
            if (doneParsing)
                return parsedShader;
            using var _ = new InvariantCultureScope();
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

        private static string FormatWarningPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            int assetsIndex = path.IndexOf("Assets", System.StringComparison.OrdinalIgnoreCase);
            int packagesIndex = path.IndexOf("Packages", System.StringComparison.OrdinalIgnoreCase);
            int startIndex = -1;
            if (assetsIndex >= 0 && packagesIndex >= 0)
                startIndex = System.Math.Min(assetsIndex, packagesIndex);
            else if (assetsIndex >= 0)
                startIndex = assetsIndex;
            else if (packagesIndex >= 0)
                startIndex = packagesIndex;
            return startIndex >= 0 ? path[startIndex..] : path;
        }

        private bool RecursiveParseFile(string currentFileName, bool isTopLevelFile, string callerPath)
        {
            var processedLines = new List<string>();
            var spaceAndTab = new[] { ' ', '\t' };
            void AddLine(string l)
            {
                bool keepAsIs = true;
                for (int i = 1; i < l.Length - 1; i++)
                {
                    if (l[i] == '\t' || (l[i] == ' ' && l[i + 1] == ' '))
                    {
                        keepAsIs = false;
                        break;
                    }
                }
                processedLines.Add(keepAsIs || l.StartsWithSimple("#include") ? l :
                    string.Join(' ', l.Split(spaceAndTab, System.StringSplitOptions.RemoveEmptyEntries)));
            }
            alreadyIncludedThisPass ??= new HashSet<string>();
            var fileID = currentFileName;
            var currentFilePath = currentFileName;
            string[] rawLines = null;
            if (isTopLevelFile)
            {
                rawLines = shaderFileLines;
                fileID = ".shader";
                if (parsedShader.name.Contains("lilToon"))
                {
                    throw new ParserException("lilToon shaders are not supported.");
                }
                if (parsedShader.name.StartsWithSimple("VRChat/Mobile/Toon Standard"))
                {
                    throw new ParserException("VRChat/Mobile/Toon Standard shaders are not supported.");
                }
            }
            if (rawLines == null)
            {
                if (currentFilePath.StartsWithSimple("/Assets/") || currentFilePath.StartsWithSimple("Assets/"))
                {
                    var path = Path.GetDirectoryName(callerPath);
                    var rootPath = path;
                    if (path.IndexOf("Assets") != -1)
                    {
                        rootPath = path.Substring(0, path.IndexOf("Assets") - 1);
                    }
                    else if (path.IndexOf("Packages") != -1)
                    {
                        rootPath = path.Substring(0, path.IndexOf("Packages") - 1);
                    }
                    currentFilePath = Path.Combine(rootPath, currentFilePath.TrimStart('/'));
                }
                else if (currentFilePath.StartsWithSimple("/Packages/") || currentFilePath.StartsWithSimple("Packages/"))
                {
                    var path = Path.GetDirectoryName(callerPath);
                    var rootPath = path;
                    if (path.IndexOf("Packages") != -1)
                    {
                        rootPath = path.Substring(0, path.IndexOf("Packages") - 1);
                    }
                    else if (path.IndexOf("Assets") != -1)
                    {
                        rootPath = path.Substring(0, path.IndexOf("Assets") - 1);
                    }
                    currentFilePath = Path.Combine(rootPath, currentFilePath.TrimStart('/'));
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
                    if (fileName == "UnityLightingCommon.cginc") {
                        // UnityLightingCommon.cginc has two fixed4 declarations which won't work in HLSLPROGRAM since it's a type defined in HLSLSupport.cginc
                        rawLines = rawLines.Select(l => l.Replace("fixed4", "float4")).ToArray();
                    }
                }
                catch (FileNotFoundException)
                {
                    if (isTopLevelFile)
                    {
                        // unity shader files are not assets in the project so we just throw the error again to mark
                        // the parsed shader as failed to read
                        throw new ParserException("This is a unity build in shader. It is not a normal asset and can't be read.");
                    }
                    if (fileName != "UnityLightingCommon.cginc")
                        parsedShader.parserWarnings.Add($"Could not find include file: '{FormatWarningPath(currentFilePath)}'");
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
                    parsedShader.parserWarnings.Add($"Could not find directory for include file: '{FormatWarningPath(currentFilePath)}'");
                    return false;
                }
            }
            var trimWhiteSpaceChars = new char[] { ' ', '\t', '\r', '\n' };
            void ParseOptimizerComment(string commentLine)
            {
                var commands = commentLine.Split(':');
                foreach (var command in commands)
                {
                    var cmd = command.Trim(trimWhiteSpaceChars);
                    if (cmd == "incompatible_shader")
                    {
                        throw new ParserException("Shader is explicitly marked as incompatible.");
                    }
                    if (cmd.StartsWithSimple("require_constant(") && cmd.EndsWith(")"))
                    {
                        var paramName = cmd.Substring("require_constant(".Length, cmd.Length - "require_constant(".Length - 1).Trim(trimWhiteSpaceChars);
                        parsedShader.requiredConstantProperties.Add(paramName);
                        continue;
                    }
                    if (cmd.StartsWithSimple("no_constant_folding(") && cmd.EndsWith(")"))
                    {
                        var paramName = cmd.Substring("no_constant_folding(".Length, cmd.Length - "no_constant_folding(".Length - 1).Trim(trimWhiteSpaceChars);
                        parsedShader.noConstantFoldingProperties.Add(paramName);
                        continue;
                    }
                    parsedShader.unknownOptimizerComments.Add(cmd);
                }
            }
            parsedShader.text[fileID] = processedLines;
            alreadyIncludedThisPass.Add(fileID);
            for (int lineIndex = 0; lineIndex < rawLines.Length; lineIndex++)
            {
                string trimmedLine = rawLines[lineIndex].Trim(trimWhiteSpaceChars);
                if (trimmedLine.Length == 0)
                    continue;
                bool isPreprocessor = trimmedLine[0] == '#';
                while (trimmedLine[^1] == '\\')
                {
                    trimmedLine = trimmedLine[..^1].TrimEnd(trimWhiteSpaceChars) + " " + rawLines[++lineIndex].Trim(trimWhiteSpaceChars);
                }
                if (isPreprocessor && trimmedLine.Length > 1 && char.IsWhiteSpace(trimmedLine[1]))
                {
                    trimmedLine = "#" + trimmedLine[1..].TrimStart(trimWhiteSpaceChars);
                }
                if (trimmedLine.StartsWithSimple("//"))
                {
                    if (trimmedLine.StartsWithSimple("ifex", 2))
                    {
                        string ifexLine = $"#{trimmedLine[2..]}";
                        AddLine(ifexLine);
                        var ifexStatement = IfexStatement.Parse(ifexLine);
                        if (ifexStatement != null) {
                            ifexStatement.CollectPropertyNames(parsedShader.ifexParameters);
                        } else {
                            parsedShader.unableToParseIfexStatements.Add(trimmedLine);
                        }
                    }
                    else if (trimmedLine.StartsWithSimple("endex", 2))
                    {
                        AddLine("#endex");
                    }
                    else if (trimmedLine.StartsWithSimple("d4rkAO:", 2))
                    {
                        ParseOptimizerComment(trimmedLine["//d4rkAO:".Length..]);
                    }
                    continue;
                }
                for (int i = 0; i < trimmedLine.Length; i++)
                {
                    if (!isPreprocessor && trimmedLine[i] == ';')
                    {
                        AddLine(trimmedLine[..(i + 1)]);
                        trimmedLine = trimmedLine[(i + 1)..].TrimStart(trimWhiteSpaceChars);
                        i = -1;
                        continue;
                    }
                    else if (!isPreprocessor && (trimmedLine[i] == '{' || trimmedLine[i] == '}'))
                    {
                        if (i != 0)
                            AddLine(trimmedLine[..i].TrimEnd(trimWhiteSpaceChars));
                        AddLine(trimmedLine[i].ToString());
                        trimmedLine = trimmedLine[(i + 1)..].TrimStart(trimWhiteSpaceChars);
                        i = -1;
                        continue;
                    }
                    else if (trimmedLine[i] == '"')
                    {
                        int end = FindEndOfStringLiteral(trimmedLine, i + 1);
                        while (end == -1 && ++lineIndex < rawLines.Length)
                        {
                            trimmedLine += System.Environment.NewLine + rawLines[lineIndex].Trim(trimWhiteSpaceChars);
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
                        if (trimmedLine.StartsWithSimple("d4rkAO:", i + 2))
                        {
                            ParseOptimizerComment(trimmedLine[(i + 2 + "d4rkAO:".Length)..]);
                        }
                        trimmedLine = trimmedLine[..i].TrimEnd(trimWhiteSpaceChars);
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
                                trimmedLine = (trimmedLine[..i] + " " + rawLines[lineIndex][(endCommentBlock + 2)..])
                                    .Trim(trimWhiteSpaceChars);
                            }
                            else
                            {
                                trimmedLine = trimmedLine[..i].TrimEnd(trimWhiteSpaceChars);
                                break;
                            }
                        }
                        else
                        {
                            trimmedLine = (trimmedLine[..i] + " " + trimmedLine[(endCommentBlock + 2)..])
                                .Trim(trimWhiteSpaceChars);
                        }
                    }
                }
                if (trimmedLine.Length == 0)
                    continue;
                if (isTopLevelFile && (trimmedLine == "CGINCLUDE" || trimmedLine == "CGPROGRAM" || trimmedLine == "HLSLINCLUDE" || trimmedLine == "HLSLPROGRAM"))
                {
                    AddLine(trimmedLine);
                    alreadyIncludedThisPass.Clear();
                    // include UnityLightingCommon.cginc at the start of each code block since that declares _SpecColor and we don't want to just include all unity cg includes
                    RecursiveParseFile("UnityLightingCommon.cginc", false, currentFilePath);
                    AddLine("#include \"UnityLightingCommon.cginc\"");
                    continue;
                }
                if (isPreprocessor && trimmedLine.Length > 9 && trimmedLine[3] == 'c') {
                    var includeFile = ParseIncludeDirective(trimmedLine);
                    if (includeFile.Length > 0) {
                        RecursiveParseFile(includeFile, false, currentFilePath);
                        AddLine($"#include \"{includeFile}\"");
                        continue;
                    }
                }
                AddLine(trimmedLine);
            }
            return true;
        }

        public static string ParseIncludeDirective(string line)
        {
            if (!line.StartsWithSimple("#include"))
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
            int endTagIndex = 0;
            string name = null;
            while (charIndex < line.Length) {
                char c = line[charIndex];
                if (c == '[') {
                    charIndex++;
                    while (charIndex < line.Length && (line[charIndex] == ' ' || line[charIndex] == '\t'))
                        charIndex++;
                    int startInsideTagIndex = charIndex;
                    int endInsideTagIndex = startInsideTagIndex;
                    while (charIndex < line.Length && line[charIndex] != ']') {
                        if (line[charIndex] != ' ' && line[charIndex] != '\t')
                            endInsideTagIndex = charIndex + 1;
                        charIndex++;
                    }
                    tags.Add(line[startInsideTagIndex..endInsideTagIndex]);
                    charIndex++;
                    while (charIndex < line.Length && (line[charIndex] == ' ' || line[charIndex] == '\t'))
                        charIndex++;
                    endTagIndex = charIndex;
                } else if (c == '(' || c == ' ' || c == '\t') {
                    name = line.Substring(endTagIndex, charIndex - endTagIndex);
                    break;
                } else {
                    charIndex++;
                }
            }
            if (name == null) {
                return null;
            }
            int quoteIndexStart = line.IndexOf('"', charIndex);
            int quoteIndexEnd = FindEndOfStringLiteral(line, quoteIndexStart + 1);
            if (quoteIndexStart == -1 || quoteIndexEnd == -1) {
                return null;
            }
            int commaIndex = line.IndexOf(',', quoteIndexEnd);
            if (commaIndex == -1) {
                return null;
            }
            int equalsIndex = line.LastIndexOf('=');
            if (equalsIndex == -1) {
                return null;
            }
            int parenthesesCloseIndex = line.LastIndexOf(')', equalsIndex, equalsIndex - commaIndex);
            if (parenthesesCloseIndex == -1) {
                return null;
            }
            int afterCommaIndex = commaIndex + 1;
            while (afterCommaIndex < line.Length && (line[afterCommaIndex] == ' ' || line[afterCommaIndex] == '\t'))
                afterCommaIndex++;
            int preParenthesesCloseIndex = parenthesesCloseIndex;
            while (preParenthesesCloseIndex > 0 && (line[preParenthesesCloseIndex - 1] == ' ' || line[preParenthesesCloseIndex - 1] == '\t'))
                preParenthesesCloseIndex--;
            string type = line.Substring(afterCommaIndex, preParenthesesCloseIndex - afterCommaIndex);
            int afterEqualsIndex = equalsIndex + 1;
            while (afterEqualsIndex < line.Length && (line[afterEqualsIndex] == ' ' || line[afterEqualsIndex] == '\t'))
                afterEqualsIndex++;
            string defaultValue = line.Substring(afterEqualsIndex);
            string stringLiteral = line.Substring(quoteIndexStart, quoteIndexEnd - quoteIndexStart + 1);
            return (name, stringLiteral, type, defaultValue);
        }

        public static ParsedShader.Property ParseProperty(string line, List<string> tags)
        {
            var prop = ParsePropertyRaw(line, tags);
            if (prop == null) {
                return null;
            }
            var output = new ParsedShader.Property();
            output.name = prop.Value.name;
            output.displayName = prop.Value.stringLiteral.Trim('"');
            if (output.displayName.IndexOf("--") != -1) {
                output.displayName = output.displayName[..output.displayName.IndexOf("--")].Trim();
            }
            string typeDefinition = prop.Value.type.ToLowerInvariant();
            output.defaultValue = prop.Value.defaultValue;
            output.hasGammaTag = false;
            bool hasHdrTag = false;
            for (int i = 0; i < tags.Count; i++) {
                var tag = tags[i].Trim();
                switch (tag.ToLowerInvariant())
                {
                    case "gamma":
                        output.hasGammaTag = true;
                        break;
                    case "hdr":
                        hasHdrTag = true;
                        break;
                    case "donotlock":
                        output.doNotLock = true;
                        break;
                    case "texturekeyword":
                        output.shaderKeywords.Add("PROP_" + (output.name[0] == '_' ? output.name[1..] : output.name).ToUpperInvariant());
                        break;
                    case "toggle":
                    case "thrytoggle":
                        output.shaderKeywords.Add(output.name.ToUpperInvariant() + "_ON");
                        break;
                    case "toggleoff":
                        output.shaderKeywords.Add(output.name.ToUpperInvariant() + "_OFF");
                        break;
                }
                if (tag.StartsWithSimple("ThryToggle("))
                {
                    var param = tag["ThryToggle(".Length..^1].Split(',').Select(s => s.Trim()).FirstOrDefault(s => s != "true" && s != "false");
                    if (string.IsNullOrEmpty(param))
                        param = output.name.ToUpperInvariant() + "_ON";
                    output.shaderKeywords.Add(param);
                }
                else if (tag.StartsWithSimple("Toggle("))
                {
                    var param = tag["Toggle(".Length..^1].Trim();
                    if (string.IsNullOrEmpty(param))
                        param = output.name.ToUpperInvariant() + "_ON";
                    output.shaderKeywords.Add(param);
                }
                else if (tag.StartsWithSimple("ToggleOff("))
                {
                    var param = tag["ToggleOff(".Length..^1].Trim();
                    if (string.IsNullOrEmpty(param))
                        param = output.name.ToUpperInvariant() + "_OFF";
                    output.shaderKeywords.Add(param);
                }
                else if (tag.StartsWithSimple("KeywordEnum("))
                {
                    output.shaderKeywords.UnionWith(tag["KeywordEnum(".Length..^1].Split(',').Select(s => $"{output.name}_{s.Trim()}".ToUpperInvariant().Replace(' ', '_')));
                }
            }
            switch (typeDefinition) {
                case "int":
                case "float":
                    output.type = ParsedShader.Property.Type.Float;
                    if (output.defaultValue[0] == '(') {
                        output.type = ParsedShader.Property.Type.Vector;
                        output.defaultValue = "float4" + output.defaultValue;
                    }
                    break;
                case "vector":
                    output.type = ParsedShader.Property.Type.Vector;
                    output.defaultValue = "float4" + output.defaultValue;
                    break;
                case "color":
                    output.type = hasHdrTag ? ParsedShader.Property.Type.ColorHDR : ParsedShader.Property.Type.Color;
                    output.defaultValue = "float4" + output.defaultValue;
                    break;
                case "integer":
                    output.type = ParsedShader.Property.Type.Integer;
                    break;
                case "2d":
                    output.type = ParsedShader.Property.Type.Texture2D;
                    switch (output.defaultValue.Trim('"')) {
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
                    break;
                case "2darray":
                    output.type = ParsedShader.Property.Type.Texture2DArray;
                    output.defaultValue = "float4(0.21582022,0.21582022,0.21582022,1)";
                    break;
                case "3d":
                    output.type = ParsedShader.Property.Type.Texture3D;
                    output.defaultValue = "float4(0.21582022,0.21582022,0.21582022,1)";
                    break;
                case "cube":
                    output.type = ParsedShader.Property.Type.TextureCube;
                    output.defaultValue = "float4(0.21582022,0.21582022,0.21582022,1)";
                    break;
                case "cubearray":
                    output.type = ParsedShader.Property.Type.TextureCubeArray;
                    output.defaultValue = "float4(0.21582022,0.21582022,0.21582022,1)";
                    break;
                default:
                    if (typeDefinition.StartsWithSimple("range")) {
                        output.type = ParsedShader.Property.Type.Float;
                        if (output.defaultValue[0] == '(') {
                            output.type = ParsedShader.Property.Type.Vector;
                            output.defaultValue = "float4" + output.defaultValue;
                        }
                    } else {
                        output.type = ParsedShader.Property.Type.Unknown;
                    }
                    break;
            }
            tags.Clear();
            return output;
        }

        public static bool IsIdentifierLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || c == '_';
        }

        public static bool IsIdentifierFirstLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
        }

        public static bool LineStartsWithReturnStatement(string line)
        {
            return line.StartsWithSimple("return")
                && (line.Length == "return".Length || !IsIdentifierLetter(line["return".Length]));
        }

        public static void SkipWhitespace(string str, ref int index)
        {
            while (index < str.Length && char.IsWhiteSpace(str[index]))
                index++;
        }

        public static string ParseIdentifierAndTrailingWhitespace(string str, ref int index)
        {
            if (index >= str.Length || !IsIdentifierFirstLetter(str[index]))
                return null;
            int startIndex = index;
            while (index < str.Length && IsIdentifierLetter(str[index]))
                index++;
            int endIndex = index;
            SkipWhitespace(str, ref index);
            return str[startIndex..endIndex];
        }

        public static string ParseTypeAndTrailingWhitespace(string str, ref int index)
        {
            if (index >= str.Length || !IsIdentifierFirstLetter(str[index]))
                return null;
            int startIndex = index;
            while (index < str.Length && IsIdentifierLetter(str[index]))
                index++;
            int endIndex = index;
            SkipWhitespace(str, ref index);
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
                SkipWhitespace(str, ref index);
            }
            return str[startIndex..endIndex];
        }

        public static (string name, string returnType) ParseFunctionDefinition(string line)
        {
            int index = 0;
            string returnType = ParseTypeAndTrailingWhitespace(line, ref index);
            if (returnType == "inline")
            {
                returnType = ParseTypeAndTrailingWhitespace(line, ref index);
            }
            if (returnType == null || returnType == "return" || returnType == "else" || !(line[index - 1] == ' ' || line[index - 1] == '\t'))
            {
                return (null, null);
            }
            string name = ParseIdentifierAndTrailingWhitespace(line, ref index);
            if (name == null || name == "if" || name == "for" || name == "while" || name == "switch" || index == line.Length || line[index] != '(')
            {
                return (null, null);
            }
            return (name, returnType);
        }

        private static HashSet<string> FunctionParameterModifiers = new() {
            "in", "out", "inout",
            "point", "line", "triangle",
            "precise", "const", "uniform",
            "centroid", "linear", "sample", "noperspective", "nointerpolation"
        };

        public static ParsedShader.Function.Parameter ParseNextFunctionParameter(string line, ref int index)
        {
            SkipWhitespace(line, ref index);
            if (index == line.Length)
                return null;
            if (line[index] == ',') {
                index++;
                SkipWhitespace(line, ref index);
            }
            var potentialType = ParseTypeAndTrailingWhitespace(line, ref index);
            if (potentialType == null)
                return null;
            var param = new ParsedShader.Function.Parameter();
            param.isInput = true;
            while (FunctionParameterModifiers.Contains(potentialType)) {
                if (potentialType == "out")
                    param.isInput = false;
                else if (potentialType == "inout") {
                    param.isOutput = true;
                }
                potentialType = ParseTypeAndTrailingWhitespace(line, ref index);
                if (potentialType == null)
                    return null;
            }
            for (int i = 0; i < potentialType.Length; i++) {
                if (char.IsWhiteSpace(potentialType[i])) {
                    potentialType = potentialType.Remove(i, 1);
                    i--;
                }
            }
            param.type = potentialType;
            var name = ParseIdentifierAndTrailingWhitespace(line, ref index);
            if (name == null)
                return null;
            param.name = name;
            param.arraySize = -1;
            if (index < line.Length && line[index] == '[') {
                index++;
                var endIndex = line.IndexOf(']', index);
                if (!int.TryParse(line.Substring(index, endIndex - index).Trim(), out param.arraySize))
                    return null;
                index = endIndex + 1;
                SkipWhitespace(line, ref index);
            }
            param.semantic = null;
            if (index == line.Length)
                return param;
            if (line[index] == ':') {
                index++;
                SkipWhitespace(line, ref index);
                param.semantic = ParseIdentifierAndTrailingWhitespace(line, ref index);
            }
            if (index == line.Length || line[index] != '=')
                return param;
            index++;
            int defaultValueStart = index;
            int parenthesisDepth = 0;
            while (index < line.Length) {
                if (line[index] == '(')
                    parenthesisDepth++;
                else if (line[index] == ')')
                    parenthesisDepth--;
                if ((parenthesisDepth == 0 && line[index] == ',') || parenthesisDepth < 0)
                    break;
                index++;
            }
            param.defaultValue = line.Substring(defaultValueStart, index - defaultValueStart);
            return param;
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
            string line = source[lineIndex][(source[lineIndex].IndexOf('(') + 1)..];
            while (lineIndex < source.Count - 1)
            {
                int charIndex = 0;
                while (charIndex < line.Length)
                {
                    int previousCharIndex = charIndex;
                    var param = ParseNextFunctionParameter(line, ref charIndex);
                    if (param != null)
                    {
                        func.parameters.Add(param);
                    }
                    if (charIndex == line.Length || line[charIndex] == ')')
                    {
                        break;
                    }
                    if (charIndex == previousCharIndex)
                    {
                        break;
                    }
                }
                while (source[lineIndex + 1][0] == '#')
                {
                    lineIndex++;
                }
                if (source[lineIndex + 1] == "{" || line.EndsWith(";"))
                {
                    int lastParenthesesIndex = line.LastIndexOf(')');
                    if (lastParenthesesIndex != -1) {
                        int colonIndex = line.IndexOf(':', lastParenthesesIndex + 1);
                        if (colonIndex != -1) {
                            int startIndex = colonIndex + 1;
                            while (startIndex < line.Length && (line[startIndex] == ' ' || line[startIndex] == '\t'))
                                startIndex++;
                            int endIndex = startIndex;
                            returnParam.semantic = ParseIdentifierAndTrailingWhitespace(line, ref endIndex);
                        }
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
            string line = source[sourceLineIndex][(source[sourceLineIndex].IndexOf('(') + 1)..];
            while (line != "{" && sourceLineIndex < source.Count - 1)
            {
                if (line.StartsWithSimple("//"))
                {
                    line = source[++sourceLineIndex];
                    continue;
                }
                if (line.Length != 0 && line[^1] == ';')
                {
                    return new List<string>();
                }
                if (line.StartsWithSimple("#"))
                {
                    output.Add(line);
                }
                else
                {
                    if (line.Contains("UNITY_POSITION"))
                    {
                        line = Regex.Replace(line, @"UNITY_POSITION\s*\(\s*(\w+)\s*\)", "float4 $1 : SV_POSITION");
                    }
                    for (int i = 0; i < line.Length; i++)
                    {
                        while (i < line.Length && (line[i] == ',' || line[i] == ' ' || line[i] == '\t'))
                            i++;
                        if (i == line.Length || line[i] == ')')
                        {
                            break;
                        }
                        int previousCharIndex = i;
                        var param = ParseNextFunctionParameter(line, ref i);
                        if (param != null)
                        {
                            output.Add(line[previousCharIndex..i].Trim(' ', '\t'));
                        }
                        else if (line[previousCharIndex..i] != "void")
                        {
                            throw new ParserException($"Failed to parse function parameter : {line[previousCharIndex..]} in:\n"
                                + string.Join("\n", source.GetRange(sourceLineIndex-5, 6)));
                        }
                        if (i == line.Length || line[i] == ')')
                        {
                            break;
                        }
                    }
                }
                line = source[++sourceLineIndex];
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
            if (!line.StartsWithSimple("#pragma "))
                return;
            int index = 8;
            while (index < line.Length && (line[index] == ' ' || line[index] == '\t'))
                index++;
            var pragmaName = ParseIdentifierAndTrailingWhitespace(line, ref index);
            if (pragmaName == null)
                return;
            var nextIdentifier = ParseIdentifierAndTrailingWhitespace(line, ref index);
            if (nextIdentifier == null)
                return;
            ParsedShader.Function func;
            switch (pragmaName) {
                case "vertex":
                    if (pass.vertex != null && pass.vertex.name != nextIdentifier)
                        throw new ParserException($"Multiple vertex pragmas in the same pass with different function names: '{pass.vertex.name}' and '{nextIdentifier}'.");
                    parsedShader.functions.TryGetValue(nextIdentifier, out func);
                    pass.vertex = func ?? new ParsedShader.Function() { name = nextIdentifier };
                    break;
                case "hull":
                    if (pass.hull != null && pass.hull.name != nextIdentifier)
                        throw new ParserException($"Multiple hull pragmas in the same pass with different function names: '{pass.hull.name}' and '{nextIdentifier}'.");
                    parsedShader.functions.TryGetValue(nextIdentifier, out func);
                    pass.hull = func ?? new ParsedShader.Function() { name = nextIdentifier };
                    break;
                case "domain":
                    if (pass.domain != null && pass.domain.name != nextIdentifier)
                        throw new ParserException($"Multiple domain pragmas in the same pass with different function names: '{pass.domain.name}' and '{nextIdentifier}'.");
                    parsedShader.functions.TryGetValue(nextIdentifier, out func);
                    pass.domain = func ?? new ParsedShader.Function() { name = nextIdentifier };
                    break;
                case "geometry":
                    if (pass.geometry != null && pass.geometry.name != nextIdentifier)
                        throw new ParserException($"Multiple geometry pragmas in the same pass with different function names: '{pass.geometry.name}' and '{nextIdentifier}'.");
                    parsedShader.functions.TryGetValue(nextIdentifier, out func);
                    pass.geometry = func ?? new ParsedShader.Function() { name = nextIdentifier };
                    break;
                case "fragment":
                    if (pass.fragment != null && pass.fragment.name != nextIdentifier)
                        throw new ParserException($"Multiple fragment pragmas in the same pass with different function names: '{pass.fragment.name}' and '{nextIdentifier}'.");
                    parsedShader.functions.TryGetValue(nextIdentifier, out func);
                    pass.fragment = func ?? new ParsedShader.Function() { name = nextIdentifier };
                    break;
                case "surface":
                    throw new ParserException("Surface shader is not supported.");
                default:
                    if (pragmaName.StartsWithSimple("shader_feature"))
                    {
                        while (nextIdentifier != null) {
                            pass.shaderFeatureKeyWords.Add(nextIdentifier);
                            parsedShader.shaderFeatureKeyWords.Add(nextIdentifier);
                            nextIdentifier = ParseIdentifierAndTrailingWhitespace(line, ref index);
                        }
                    }
                    break;

            }
        }

        private HashSet<string> unityTextureDeclarationMacros = new() {
            "UNITY_DECLARE_TEX2D",
            "UNITY_DECLARE_TEX2D_NOSAMPLER",
            "UNITY_DECLARE_TEX2D_NOSAMPLER_INT",
            "UNITY_DECLARE_TEX2D_NOSAMPLER_UINT",
            "UNITY_DECLARE_TEX2D_HALF",
            "UNITY_DECLARE_TEX2D_FLOAT",
            "UNITY_DECLARE_TEX2D_NOSAMPLER_HALF",
            "UNITY_DECLARE_TEX2D_NOSAMPLER_FLOAT",
            "UNITY_DECLARE_TEX2DARRAY_MS",
            "UNITY_DECLARE_TEX2DARRAY_MS_NOSAMPLER",
            "UNITY_DECLARE_TEX2DARRAY",
            "UNITY_DECLARE_TEX2DARRAY_NOSAMPLER",
            "UNITY_DECLARE_FRAMEBUFFER_INPUT_FLOAT_MS",
            "UNITY_DECLARE_FRAMEBUFFER_INPUT_HALF_MS",
            "UNITY_DECLARE_FRAMEBUFFER_INPUT_INT_MS",
            "UNITY_DECLARE_FRAMEBUFFER_INPUT_UINT_MS",
            "UNITY_DECLARE_DEPTH_TEXTURE_MS",
            "UNITY_DECLARE_DEPTH_TEXTURE",
            "UNITY_DECLARE_SCREENSPACE_SHADOWMAP",
            "UNITY_DECLARE_SCREENSPACE_TEXTURE",
            "TEXTURE2D", "TEXTURE2D_PARAM",
        };

        private void ParseFunctionDeclarationsRecursive(List<string> lines, ParsedShader.Pass currentPass, int startIndex, HashSet<string> alreadyParsed = null)
        {
            alreadyParsed ??= new HashSet<string>();
            for (int lineIndex = startIndex; lineIndex < lines.Count; lineIndex++) {
                var currentLine = lines[lineIndex];
                if (currentLine[0] == '#') {
                    if (currentLine.StartsWithSimple("#include ")) {
                        var includeName = ParseIncludeDirective(currentLine);
                        if (!alreadyParsed.Contains(includeName) && parsedShader.text.TryGetValue(includeName, out var includeLines)) {
                            alreadyParsed.Add(includeName);
                            ParseFunctionDeclarationsRecursive(includeLines, currentPass, 0, alreadyParsed);
                        }
                    }
                    else if (currentLine.StartsWithSimple("#define ")) {
                        if (currentLine.Contains("Texture2D ") || currentLine.Contains("sampler2D ") || currentLine.Contains("##_ST")) {
                            int defineIndex = "#define ".Length;
                            SkipWhitespace(currentLine, ref defineIndex);
                            var identifier = ParseIdentifierAndTrailingWhitespace(currentLine, ref defineIndex);
                            if (!parsedShader.customTextureDeclarations.Contains(currentLine) && !unityTextureDeclarationMacros.Contains(identifier))
                                parsedShader.customTextureDeclarations.Add(currentLine);
                        }
                    }
                    continue;
                }
                int tempIndex = lineIndex;
                var func = ParseFunctionDefinition(lines, ref tempIndex);
                if (func != null) {
                    parsedShader.functions[func.name] = func;
                    UpdateFunctionDefinition(func, currentPass);
                    if (func.name == currentPass.vertex?.name || func.name == currentPass.fragment?.name) {
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
                if (line[0] == 'E' && (line == "ENDCG" || line == "ENDHLSL"))
                    break;
                if (line[0] == '{')
                {
                    curlyBraceDepth++;
                }
                else if (line[0] == '}')
                {
                    curlyBraceDepth--;
                }
                else if (line[0] != '#')
                {
                    int startIndex = lineIndex;
                    while (lines[lineIndex][^1] != ';'
                        && lines[lineIndex][^1] != ']'
                        && lineIndex < lines.Count - 1
                        && lines[lineIndex + 1][0] != '{'
                        && lines[lineIndex + 1][0] != '}'
                        && lines[lineIndex + 1][0] != '#'
                        && !LineStartsWithReturnStatement(lines[lineIndex + 1]))
                    {
                        lineIndex++;
                    }
                    if (startIndex != lineIndex)
                    {
                        line = string.Join(" ", lines.GetRange(startIndex, lineIndex - startIndex + 1));
                    }
                    var returnIndex = line.IndexOf("return");
                    if (returnIndex > 0 && line.Length > returnIndex + 6)
                    {
                        char prev = line[returnIndex - 1];
                        char next = line[returnIndex + 6];
                        if ((prev == ' ' || prev == '\t' || prev == ';' || prev == ')')
                            && (next == ' ' || next == '\t' || next == ';') || next == '(')
                        {
                            output.Add(line[..returnIndex].TrimEnd());
                            line = line[returnIndex..];   
                        }
                    }
                }
                output.Add(line);
                if (line.StartsWithSimple("UNITY_INSTANCING_BUFFER_START"))
                {
                    throw new ParserException("Shader with instancing is not supported.");
                }
            }
        }

        Regex tex2DDeclaration = new(@"(?:^|(?<!\w))TEXTURE2D(?:_FLOAT|_HALF)?\s*\(([\w\s]+)\)");
        Regex tex2DParam = new(@"(?:^|(?<!\w))TEXTURE2D_PARAM\s*\(([\w\s]+),([\w\s]+)\)");
        Regex samplerDeclaration = new(@"(?:^|(?<!\w))SAMPLER\s*\(([\w\s]+)\)");

        private void ReplaceCoreTextureMacros(List<string> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (line.StartsWithSimple("#"))
                    continue;
                if (line.Contains("TEXTURE2D"))
                {
                    line = tex2DDeclaration.Replace(line, "Texture2D $1");
                    line = tex2DParam.Replace(line, "Texture2D $1, SamplerState $2");
                }
                if (line.Contains("SAMPLER"))
                {
                    line = samplerDeclaration.Replace(line, "SamplerState $1");
                }
                lines[i] = line;
            }
        }

        private void SemanticParseShader()
        {
            ParsedShader.Pass currentPass = null;
            List<string> output = new();
            List<string> cgInclude = new();
            List<string> hlslInclude = new();
            List<string> lines = parsedShader.text[".shader"];
            List<string> tags = new();
            if (replaceCoreTextureMacros)
            {
                foreach (var fileLines in parsedShader.text.Values)
                {
                    ReplaceCoreTextureMacros(fileLines);
                }
            }
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
            bool foundProperties = false;
            curlyBraceDepth = 0;
            int passCurlyBraceDepth = 0;
            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                string line = lines[lineIndex];
                string lowerLine = line.ToLowerInvariant();
                if (lowerLine == "properties")
                {
                    foundProperties = true;
                    if (lines[lineIndex + 1] != "{")
                    {
                        throw new ParserException($"Expected '{{' after Properties, but found '{lines[lineIndex + 1]}' instead.");
                    }
                    output.Add(line);
                    output.Add("{");
                    lineIndex += 2;
                    for (; lineIndex < lines.Count; lineIndex++)
                    {
                        line = lines[lineIndex];
                        if (line == "{" && lines[lineIndex + 1] == "}")
                        {
                            lineIndex++;
                            output[output.Count - 1] += " {}";
                            continue;
                        }
                        output.Add(line);
                        if (line == "}")
                        {
                            break;
                        }
                        var property = ParseProperty(line, tags);
                        if (property != null)
                        {
                            parsedShader.properties.Add(property);
                            parsedShader.propertyTable[property.name] = property;
                            foreach (var keyword in property.shaderKeywords)
                            {
                                parsedShader.keywordToProperty[keyword] = property;
                            }
                            if (property.type == ParsedShader.Property.Type.Texture2D)
                            {
                                parsedShader.texture2DProperties.Add(property);
                            }
                            if (property.type == ParsedShader.Property.Type.Texture2D || property.type == ParsedShader.Property.Type.Texture2DArray)
                            {
                                var ST_property = new ParsedShader.Property();
                                ST_property.name = property.name + "_ST";
                                ST_property.type = ParsedShader.Property.Type.Vector;
                                ST_property.defaultValue = "float4(1,1,0,0)";
                                parsedShader.properties.Add(ST_property);
                                parsedShader.propertyTable[ST_property.name] = ST_property;
                            }
                        }
                    }
                }
                else if (lowerLine == "tags")
                {
                    if (lines[lineIndex + 1] != "{")
                    {
                        throw new ParserException($"Expected '{{' after Tags, but found '{lines[lineIndex + 1]}' instead.");
                    }
                    output.Add(line);
                    output.Add("{");
                    lineIndex += 2;
                    for (; lineIndex < lines.Count; lineIndex++)
                    {
                        line = lines[lineIndex];
                        output.Add(line);
                        if (line == "}")
                        {
                            break;
                        }
                        var matches = Regex.Matches(line, @"""([^""]+)""\s*=\s*""([^""]+)""");
                        foreach (Match match in matches)
                        {
                            if (currentPass != null)
                            {
                                currentPass.tags[match.Groups[1].Value] = match.Groups[2].Value;
                            }
                        }
                    }
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
                    if (currentPass == null)
                    {
                        throw new ParserException($"{line} found outside of Pass.");
                    }
                    currentPass.codeBlockStartIndex = output.Count - currentPass.startLineIndex;
                    output.Add(line);
                    int programLineIndexStart = output.Count;
                    output.AddRange(line == "CGPROGRAM" ? cgInclude : hlslInclude);
                    PreprocessCodeLines(lines, ref lineIndex, output, ref curlyBraceDepth);
                    for (int programLineIndex = programLineIndexStart; programLineIndex < output.Count; programLineIndex++)
                    {
                        ParsePragma(output[programLineIndex], currentPass);
                    }
                    ParseFunctionDeclarationsRecursive(output, currentPass, programLineIndexStart);
                    output.Add(line == "CGPROGRAM" ? "ENDCG" : "ENDHLSL");
                    currentPass.codeBlockLineCount = output.Count - currentPass.startLineIndex - currentPass.codeBlockStartIndex;
                }
                else if (lowerLine == "pass")
                {
                    if (lines[lineIndex + 1] != "{")
                    {
                        throw new ParserException($"Expected '{{' after Pass, but found '{lines[lineIndex + 1]}' instead.");
                    }
                    currentPass = new ParsedShader.Pass();
                    parsedShader.passes.Add(currentPass);
                    currentPass.startLineIndex = output.Count;
                    passCurlyBraceDepth = 1;
                    lineIndex++;
                    output.Add($"pass//{parsedShader.passes.Count-1}");
                    output.Add("{");
                }
                else if (lowerLine.StartsWithSimple("usepass"))
                {
                    throw new ParserException("UsePass is not supported.");
                }
                else if (currentPass != null && line == "{")
                {
                    passCurlyBraceDepth++;
                    output.Add(line);
                }
                else if (currentPass != null && line == "}")
                {
                    passCurlyBraceDepth--;
                    if (passCurlyBraceDepth == 0)
                    {
                        if (currentPass.codeBlockLineCount == 0)
                        {
                            bool hasColorMask0 = false;
                            for (int i = currentPass.startLineIndex; i < output.Count; i++)
                            {
                                if (output[i].ToLowerInvariant().StartsWithSimple("colormask") && output[i].EndsWith("0"))
                                {
                                    hasColorMask0 = true;
                                    break;
                                }
                            }
                            if (hasColorMask0)
                            {
                                currentPass.codeBlockStartIndex = output.Count - currentPass.startLineIndex;
                                int programLineIndexStart = output.Count;
                                output.AddRange(new string[] {
                                    "CGPROGRAM",
                                    "#pragma vertex vert",
                                    "#pragma fragment frag",
                                    "struct appdata", "{", "float4 vertex : POSITION;", "}", ";",
                                    "struct v2f", "{", "float4 vertex : SV_POSITION;", "}", ";",
                                    "v2f vert(appdata v)", "{", "v2f o;", "o.vertex = UnityObjectToClipPos(v.vertex);", "return o;", "}",
                                    "float4 frag(v2f i) : SV_Target", "{", "return float4(0,0,0,0);", "}",
                                    "ENDCG"
                                });
                                for (int programLineIndex = programLineIndexStart; programLineIndex < output.Count; programLineIndex++)
                                {
                                    ParsePragma(output[programLineIndex], currentPass);
                                }
                                ParseFunctionDeclarationsRecursive(output, currentPass, programLineIndexStart);
                                currentPass.codeBlockLineCount = output.Count - currentPass.startLineIndex - currentPass.codeBlockStartIndex;
                            }
                        }
                        output.Add(line);
                        currentPass.lineCount = output.Count - currentPass.startLineIndex;
                        currentPass = null;
                    }
                    else
                    {
                        output.Add(line);
                    }
                }
                else if (currentPass != null && lowerLine.StartsWithSimple("name"))
                {
                    currentPass.name = line.Substring(5).Trim('\t', ' ', '"').ToUpperInvariant();
                }
                else
                {
                    output.Add(line);
                    if (line.IndexOf('[') != -1)
                    {
                        var matches = Regex.Matches(line, @"\[\s*(\w+)\s*\]");
                        if (matches.Count > 0)
                        {
                            string shaderLabParam = Regex.Match(line, @"^[_a-zA-Z]+").Value;
                            foreach (Match match in matches)
                            {
                                string propName = match.Groups[1].Value;
                                if (parsedShader.propertyTable.TryGetValue(propName, out var prop))
                                {
                                    prop.shaderLabParams.Add(shaderLabParam.ToLowerInvariant());
                                }
                            }
                        }
                    }
                }
            }
            if (!foundProperties)
            {
                output.Insert(2, "properties");
                output.Insert(3, "{");
                output.Insert(4, "}");
                foreach (var pass in parsedShader.passes)
                {
                    pass.startLineIndex += 3;
                }
            }
            if (parsedShader.requiredConstantProperties.Count == 0)
            {
                foreach (var ifexPropName in parsedShader.ifexParameters)
                {
                    if (parsedShader.propertyTable.TryGetValue(ifexPropName, out var prop))
                    {
                        prop.shaderLabParams.Add("ifex");
                    }
                }
            }
            foreach (var requiredConstantProperty in parsedShader.requiredConstantProperties)
            {
                if (parsedShader.propertyTable.TryGetValue(requiredConstantProperty, out var prop))
                {
                    prop.shaderLabParams.Add("require_constant");
                }
            }
            foreach (var noConstantFoldingProperty in parsedShader.noConstantFoldingProperties)
            {
                if (parsedShader.propertyTable.TryGetValue(noConstantFoldingProperty, out var prop))
                {
                    prop.shaderLabParams.Add("no_constant_folding");
                }
            }
            foreach (var prop in parsedShader.properties)
            {
                switch (prop.type)
                {
                    case ParsedShader.Property.Type.Integer:
                    case ParsedShader.Property.Type.Float:
                        if (prop.shaderLabParams.Count == 0)
                            break;
                        parsedShader.propertiesToCheckWhenMerging.Add(prop);
                        break;
                    case ParsedShader.Property.Type.Texture2D:
                    case ParsedShader.Property.Type.Texture2DArray:
                    case ParsedShader.Property.Type.Texture3D:
                    case ParsedShader.Property.Type.TextureCube:
                    case ParsedShader.Property.Type.TextureCubeArray:
                        parsedShader.propertiesToCheckWhenMerging.Add(prop);
                        break;
                }
            }
            parsedShader.mismatchedCurlyBraces |= curlyBraceDepth != 0;
            if (parsedShader.mismatchedCurlyBraces)
            {
                throw new ParserException("Mismatched curly braces.");
            }
            if (parsedShader.passes.Count == 0)
            {
                throw new ParserException("No passes found.");
            }
            foreach ((var pass, int index) in parsedShader.passes.Select((p, i) => (p, i)))
            {
                if (pass.tags.TryGetValue("LightMode", out var lightMode) && lightMode == "Meta")
                    continue;
                if (pass.vertex == null)
                    throw new ParserException($"Could not find vertex pragma in pass {index}.");
                if (pass.fragment == null)
                    throw new ParserException($"Could not find fragment pragma in pass {index}.");
                foreach (var func in new[] { pass.vertex, pass.geometry, pass.fragment })
                {
                    if (func == null || func.parameters.Count > 0)
                        continue;
                    parsedShader.parserWarnings.Add($"The {func.name} function in pass {index} could not be found but is declared by a pragma.");
                    parsedShader.couldFindAllVertexGeomFragmentFunctions = false;
                }
                if (pass.vertex.parameters.Count == 1 && pass.vertex.parameters[0].type == "void")
                {
                    parsedShader.hasEmptyVertexFunction = true;
                }
            }
        }
    }
}
#endif
