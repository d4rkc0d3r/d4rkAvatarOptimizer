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
using d4rkpl4y3r.AvatarOptimizer.Extensions;
using System.Security.Cryptography;
using System.Configuration;

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
        public string filePath;
        public bool parsedCorrectly = false;
        public string errorMessage = "";
        public bool hasDisableBatchingTag = false;
        public List<string> customTextureDeclarations = new List<string>();
        public Dictionary<string, int> multiIncludeFileCount = new Dictionary<string, int>();
        public bool mismatchedCurlyBraces = false;
        public Dictionary<string, List<string>> text = new Dictionary<string, List<string>>();
        public List<Property> properties = new List<Property>();
        public List<Property> propertiesToCheckWhenMerging = new List<Property>();
        public List<Property> texture2DProperties = new List<Property>();
        public Dictionary<string, Property> propertyTable = new Dictionary<string, Property>();
        public List<Pass> passes = new List<Pass>();
        public Dictionary<string, Function> functions = new Dictionary<string, Function>();
        public HashSet<string> shaderFeatureKeyWords = new HashSet<string>();
        public HashSet<string> ifexParameters = new HashSet<string>();
        public HashSet<string> unableToParseIfexStatements = new HashSet<string>();

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

        public class ParserException : System.Exception
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
            filePath = Path.GetFullPath(shaderPath);
            parsedShader.filePath = filePath;
            maxIncludes = 1000;
            doneParsing = false;
            if (shaderPath.EndsWith(".orlshader"))
            {
                #if ORLSHADER_EXISTS
                Profiler.StartSection("ORL.ShaderGenerator");
                try
                {
                    shaderFileLines = ORL.ShaderGenerator.ShaderDefinitionImporter.GenerateShader(shaderPath, false)
                        .Split(new string[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.RemoveEmptyEntries);
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
                parsedShader.errorMessage = "ORLShader Generator 6.2 is not installed.";
                #endif
            }
            else if (!shaderPath.EndsWith(".shader"))
            {
                parsedShader.parsedCorrectly = false;
                doneParsing = true;
                parsedShader.errorMessage = $"Unsupported shader file type: {Path.GetExtension(shaderPath)}";
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

        public static List<(string name, bool notEquals, float value)> ParseIfexConditions(string line)
        {
            var conditions = new List<(string name, bool notEquals, float value)>();
            int index = 5;
            while (index < line.Length) {
                while (index < line.Length && char.IsWhiteSpace(line[index]))
                    index++;
                if (index == line.Length)
                    break;
                if (line[index] == '&' && line[index + 1] == '&') {
                    index += 2;
                }
                while (index < line.Length && char.IsWhiteSpace(line[index]))
                    index++;
                var name = ShaderAnalyzer.ParseIdentifierAndTrailingWhitespace(line, ref index);
                if (name == null) {
                    return null;
                }
                while (index < line.Length && char.IsWhiteSpace(line[index]))
                    index++;
                if (index == line.Length) {
                    return null;
                }
                bool notEquals = line[index] == '!';
                if ((line[index] != '!' && line[index] != '=') || line[index + 1] != '=') {
                    return null;
                }
                index += 2;
                while (index < line.Length && char.IsWhiteSpace(line[index]))
                    index++;
                if (index == line.Length) {
                    return null;
                }
                int valueStart = index;
                int valueEnd = index;
                while (valueEnd < line.Length && (char.IsDigit(line[valueEnd]))) {
                    valueEnd++;
                }
                if (valueStart == valueEnd) {
                    return null;
                }
                index = valueEnd;
                var value = float.Parse(line.Substring(valueStart, valueEnd - valueStart));
                conditions.Add((name, notEquals, value));
            }
            return conditions;
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
                if (parsedShader.name.Contains("lilToon"))
                {
                    throw new ParserException("lilToon shaders are not supported.");
                }
            }
            if (rawLines == null)
            {
                if (currentFilePath.StartsWithSimple("/Assets/") || currentFilePath.StartsWithSimple("Assets/"))
                {
                    var path = Path.GetDirectoryName(callerPath);
                    var assetFolderPath = path.IndexOf("Assets") != -1 ? path.Substring(0, path.IndexOf("Assets") - 1) : path;
                    currentFilePath = Path.Combine(assetFolderPath, currentFilePath.TrimStart('/'));
                }
                else if (currentFilePath.StartsWithSimple("/Packages/") || currentFilePath.StartsWithSimple("Packages/"))
                {
                    var path = Path.GetDirectoryName(callerPath);
                    var packageFolderPath = path.IndexOf("Packages") != -1 ? path.Substring(0, path.IndexOf("Packages") - 1) : path;
                    currentFilePath = Path.Combine(packageFolderPath, currentFilePath.TrimStart('/'));
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
            var trimWhiteSpaceChars = new char[] { ' ', '\t', '\r', '\n' };
            for (int lineIndex = 0; lineIndex < rawLines.Length; lineIndex++)
            {
                string trimmedLine = rawLines[lineIndex].Trim(trimWhiteSpaceChars);
                if (trimmedLine.Length == 0)
                    continue;
                bool isPreprocessor = trimmedLine[0] == '#';
                while (trimmedLine[trimmedLine.Length - 1] == '\\')
                {
                    trimmedLine = trimmedLine.Substring(0, trimmedLine.Length - 1).TrimEnd(trimWhiteSpaceChars) + " " + rawLines[++lineIndex].Trim(trimWhiteSpaceChars);
                }
                if (trimmedLine.Length >= 6 && trimmedLine[0] == '/' && trimmedLine[1] == '/')
                {
                    if (trimmedLine[2] == 'i' && trimmedLine[3] == 'f' && trimmedLine[4] == 'e' && trimmedLine[5] == 'x')
                    {
                        string ifexLine = $"#{trimmedLine.Substring(2)}";
                        processedLines.Add(ifexLine);
                        var conditions = ParseIfexConditions(ifexLine);
                        if (conditions != null) {
                            conditions.ForEach(p => parsedShader.ifexParameters.Add(p.name));
                        } else {
                            parsedShader.unableToParseIfexStatements.Add(trimmedLine);
                        }
                    }
                    else if (trimmedLine.Length > 6 && trimmedLine[2] == 'e' && trimmedLine[3] == 'n' && trimmedLine[4] == 'd' && trimmedLine[5] == 'e' && trimmedLine[6] == 'x')
                    {
                        processedLines.Add("#endex");
                    }
                    continue;
                }
                for (int i = 0; i < trimmedLine.Length; i++)
                {
                    if (!isPreprocessor && trimmedLine[i] == ';')
                    {
                        processedLines.Add(trimmedLine.Substring(0, i + 1));
                        trimmedLine = trimmedLine.Substring(i + 1).TrimStart(trimWhiteSpaceChars);
                        i = -1;
                        continue;
                    }
                    else if (!isPreprocessor && (trimmedLine[i] == '{' || trimmedLine[i] == '}'))
                    {
                        if (i != 0)
                            processedLines.Add(trimmedLine.Substring(0, i).TrimEnd(trimWhiteSpaceChars));
                        processedLines.Add(trimmedLine[i].ToString());
                        trimmedLine = trimmedLine.Substring(i + 1).TrimStart(trimWhiteSpaceChars);
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
                        trimmedLine = trimmedLine.Substring(0, i).TrimEnd(trimWhiteSpaceChars);
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
                                trimmedLine = trimmedLine.Substring(0, i).TrimEnd(trimWhiteSpaceChars);
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
                if (trimmedLine.Length == 0)
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
                if (isPreprocessor && trimmedLine.Length > 9 && trimmedLine[3] == 'c') {
                    var includeFile = ParseIncludeDirective(trimmedLine);
                    if (includeFile.Length > 0) {
                        RecursiveParseFile(includeFile, false, currentFilePath);
                        processedLines.Add($"#include \"{includeFile}\"");
                        continue;
                    }
                }
                processedLines.Add(trimmedLine);
            }
            return true;
        }

        public static string ParseIncludeDirective(string line)
        {
            if (!line.StartsWith("#include"))
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
                    if (endInsideTagIndex - startInsideTagIndex <= 5) {
                        // currently we only care about [hdr] and [gamma] tags
                        tags.Add(line.Substring(startInsideTagIndex, endInsideTagIndex - startInsideTagIndex));
                    }
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

        public static ParsedShader.Property ParseProperty(string line, List<string> tags, bool clearTagsOnPropertyParse = true)
        {
            var prop = ParsePropertyRaw(line, tags);
            if (prop == null) {
                return null;
            }
            var output = new ParsedShader.Property();
            output.name = prop.Value.name;
            string typeDefinition = prop.Value.type.ToLowerInvariant();
            output.defaultValue = prop.Value.defaultValue;
            output.hasGammaTag = false;
            bool hasHdrTag = false;
            for (int i = 0; i < tags.Count; i++) {
                switch (tags[i].ToLowerInvariant()) {
                    case "gamma":
                        output.hasGammaTag = true;
                        break;
                    case "hdr":
                        hasHdrTag = true;
                        break;
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
                    output.type = ParsedShader.Property.Type.Int;
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
                    if (typeDefinition[0] == 'r' && typeDefinition[1] == 'a' && typeDefinition[2] == 'n' && typeDefinition[3] == 'g' && typeDefinition[4] == 'e') {
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
            if (clearTagsOnPropertyParse)
                tags.Clear();
            return output;
        }

        public static bool IsIdentifierLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || c == '_';
        }

        public static string ParseIdentifierAndTrailingWhitespace(string str, ref int index)
        {
            int startIndex = index;
            while (index < str.Length && IsIdentifierLetter(str[index]))
                index++;
            if (index == startIndex)
                return null;
            int endIndex = index;
            while (index < str.Length && (str[index] == ' ' || str[index] == '\t'))
                index++;
            return str.Substring(startIndex, endIndex - startIndex);
        }

        public static string ParseTypeAndTrailingWhitespace(string str, ref int index)
        {
            int startIndex = index;
            while (index < str.Length && IsIdentifierLetter(str[index]))
                index++;
            if (index == startIndex)
                return null;
            int endIndex = index;
            while (index < str.Length && (str[index] == ' ' || str[index] == '\t'))
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
                while (index < str.Length && (str[index] == ' ' || str[index] == '\t'))
                    index++;
            }
            return str.Substring(startIndex, endIndex - startIndex);
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

        private static HashSet<string> FunctionParameterModifiers = new HashSet<string> {
            "in", "out", "inout",
            "point", "line", "triangle",
            "precise", "const", "uniform",
            "centroid", "linear", "sample", "noperspective", "nointerpolation" };

        public static ParsedShader.Function.Parameter ParseNextFunctionParameter(string line, ref int index)
        {
            while (index < line.Length && char.IsWhiteSpace(line[index]))
                index++;
            if (index == line.Length)
                return null;
            if (line[index] == ',') {
                index++;
                while (index < line.Length && char.IsWhiteSpace(line[index]))
                    index++;
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
                while (index < line.Length && char.IsWhiteSpace(line[index]))
                    index++;
            }
            param.semantic = null;
            if (index == line.Length)
                return param;
            if (line[index] == ':') {
                index++;
                while (index < line.Length && char.IsWhiteSpace(line[index]))
                    index++;
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
            string line = source[lineIndex].Substring(source[lineIndex].IndexOf('(') + 1);
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
            string line = source[sourceLineIndex].Substring(source[sourceLineIndex].IndexOf('(') + 1);
            while (line != "{" && sourceLineIndex < source.Count - 1)
            {
                if (line.Length != 0 && line[line.Length - 1] == ';')
                {
                    return new List<string>();
                }
                line = Regex.Replace(line, @"UNITY_POSITION\s*\(\s*(\w+)\s*\)", "float4 $1 : SV_POSITION");
                if (line.StartsWithSimple("#"))
                {
                    output.Add(line);
                }
                else
                {
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
                            output.Add(line.Substring(previousCharIndex, i - previousCharIndex).Trim(' ', '\t'));
                        }
                        else if (line.Substring(previousCharIndex, i - previousCharIndex) != "void")
                        {
                            throw new ParserException($"Failed to parse function parameter : {line.Substring(previousCharIndex)} in:\n"
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
            foreach (var declaration in output)
            {
                if (declaration.StartsWithSimple("#"))
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
            if (line.Length < 8 || line[0] != '#' || line[1] != 'p' || !line.StartsWith("#pragma "))
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
                    parsedShader.functions.TryGetValue(nextIdentifier, out func);
                    pass.vertex = func ?? new ParsedShader.Function() { name = nextIdentifier };
                    break;
                case "hull":
                    parsedShader.functions.TryGetValue(nextIdentifier, out func);
                    pass.hull = func ?? new ParsedShader.Function() { name = nextIdentifier };
                    break;
                case "domain":
                    parsedShader.functions.TryGetValue(nextIdentifier, out func);
                    pass.domain = func ?? new ParsedShader.Function() { name = nextIdentifier };
                    break;
                case "geometry":
                    parsedShader.functions.TryGetValue(nextIdentifier, out func);
                    pass.geometry = func ?? new ParsedShader.Function() { name = nextIdentifier };
                    break;
                case "fragment":
                    parsedShader.functions.TryGetValue(nextIdentifier, out func);
                    pass.fragment = func ?? new ParsedShader.Function() { name = nextIdentifier };
                    break;
                case "shader_feature":
                case "shader_feature_local":
                    while (nextIdentifier != null) {
                        pass.shaderFeatureKeyWords.Add(nextIdentifier);
                        parsedShader.shaderFeatureKeyWords.Add(nextIdentifier);
                        nextIdentifier = ParseIdentifierAndTrailingWhitespace(line, ref index);
                    }
                    break;
                case "surface":
                    throw new ParserException("Surface shader is not supported.");
            }
        }

        private void ParseFunctionDeclarationsRecursive(List<string> lines, ParsedShader.Pass currentPass, int startIndex, HashSet<string> alreadyParsed = null)
        {
            if (alreadyParsed == null)
                alreadyParsed = new HashSet<string>();
            for (int lineIndex = startIndex; lineIndex < lines.Count; lineIndex++) {
                var currentLine = lines[lineIndex];
                if (currentLine[0] == '#') {
                    if (currentLine.Length > 8 && currentLine[3] == 'c' && currentLine.StartsWith("#include ")) {
                        var includeName = ParseIncludeDirective(currentLine);
                        if (!alreadyParsed.Contains(includeName) && parsedShader.text.TryGetValue(includeName, out var includeLines)) {
                            alreadyParsed.Add(includeName);
                            ParseFunctionDeclarationsRecursive(includeLines, currentPass, 0, alreadyParsed);
                        }
                    }
                    else if (currentLine.Length > 8 && currentLine[3] == 'f' && currentLine.StartsWith("#define ")) {
                        if (currentLine.Contains("Texture2D ") || currentLine.Contains("sampler2D ") || currentLine.Contains("##_ST")) {
                            if (!parsedShader.customTextureDeclarations.Contains(currentLine))
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
                    while (lines[lineIndex][lines[lineIndex].Length - 1] != ';'
                        && lines[lineIndex][lines[lineIndex].Length - 1] != ']'
                        && lineIndex < lines.Count - 1
                        && lines[lineIndex + 1][0] != '{'
                        && lines[lineIndex + 1][0] != '}'
                        && lines[lineIndex + 1][0] != '#'
                        && (lines[lineIndex + 1][0] != 'r' || !lines[lineIndex + 1].StartsWith("return")))
                    {
                        lineIndex++;
                    }
                    if (startIndex != lineIndex)
                    {
                        line = string.Join(" ", lines.GetRange(startIndex, lineIndex - startIndex + 1));
                    }
                    var returnIndex = line.IndexOf("return");
                    if (returnIndex > 0 && line.Length > returnIndex + 6)
                    if ((line[returnIndex - 1] == ' ' || line[returnIndex - 1] == '\t' || line[returnIndex - 1] == ';' || line[returnIndex - 1] == ')')
                        && (line[returnIndex + 6] == ' ' || line[returnIndex + 6] == '\t' || line[returnIndex + 6] == ';') || line[returnIndex + 6] == '(')
                    {
                        output.Add(line.Substring(0, returnIndex).TrimEnd());
                        line = line.Substring(returnIndex);   
                    }
                }
                output.Add(line);
                if (line.Length >= 29 && line[0] == 'U' && line[6] == 'I' && line.StartsWith("UNITY_INSTANCING_BUFFER_START"))
                {
                    throw new ParserException("Shader with instancing is not supported.");
                }
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
            bool foundProperties = false;
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
                            if (property != null) {
                                parsedShader.properties.Add(property);
                                parsedShader.propertyTable[property.name] = property;
                                if (property.type == ParsedShader.Property.Type.Texture2D) {
                                    parsedShader.texture2DProperties.Add(property);
                                }
                                if (property.type == ParsedShader.Property.Type.Texture2D || property.type == ParsedShader.Property.Type.Texture2DArray) {
                                    var ST_property = new ParsedShader.Property();
                                    ST_property.name = property.name + "_ST";
                                    ST_property.type = ParsedShader.Property.Type.Vector;
                                    ST_property.defaultValue = "float4(1,1,0,0)";
                                    parsedShader.properties.Add(ST_property);
                                    parsedShader.propertyTable[ST_property.name] = ST_property;
                                }   
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
                            foundProperties = true;
                            output.Add(line);
                            if (lines[lineIndex + 1] == "{")
                            {
                                lineIndex++;
                                output.Add("{");
                            }
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
                            output.Add(line);
                            int programLineIndexStart = output.Count;
                            currentPass = new ParsedShader.Pass();
                            parsedShader.passes.Add(currentPass);
                            output.AddRange(line == "CGPROGRAM" ? cgInclude : hlslInclude);
                            PreprocessCodeLines(lines, ref lineIndex, output, ref curlyBraceDepth);
                            for (int programLineIndex = programLineIndexStart; programLineIndex < output.Count; programLineIndex++)
                            {
                                ParsePragma(output[programLineIndex], currentPass);
                            }
                            ParseFunctionDeclarationsRecursive(output, currentPass, programLineIndexStart);
                            output.Add(line == "CGPROGRAM" ? "ENDCG" : "ENDHLSL");
                        }
                        else if (line.StartsWith("UsePass"))
                        {
                            throw new ParserException("UsePass is not supported.");
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
                                            prop.shaderLabParams.Add(shaderLabParam);
                                        }
                                    }
                                }
                            }
                        }
                        break;
                }
            }
            if (!foundProperties)
            {
                output.Insert(2, "Properties");
                output.Insert(3, "{");
                output.Insert(4, "}");
            }
            if (state != ParseState.ShaderLab)
            {
                throw new ParserException("Parse state is not ShaderLab at the end of the file.");
            }
            foreach (var ifexPropName in parsedShader.ifexParameters)
            {
                if (parsedShader.propertyTable.TryGetValue(ifexPropName, out var prop))
                {
                    prop.shaderLabParams.Add("ifex");
                }
            }
            foreach (var prop in parsedShader.properties)
            {
                switch (prop.type)
                {
                    case ParsedShader.Property.Type.Int:
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
            if (parsedShader.passes.Any(p => p.vertex == null || p.fragment == null))
            {
                throw new ParserException("A pass is missing a vertex or fragment shader.");
            }
        }
    }

    public class ShaderOptimizer
    {
        public class OptimizedShader
        {
            public string name;
            public List<(string name, List<string> lines)> files;
            public List<string> floatProperties = new List<string>();
            public List<string> colorProperties = new List<string>();
            public List<string> integerProperties = new List<string>();
            public List<string> tex2DProperties = new List<string>();
            public List<string> tex3DCubeProperties = new List<string>();
            public List<string> unknownTypeProperties = new List<string>();
            public ParsedShader originalShader;

            public void AddProperty(string name, string type)
            {
                switch (type.ToLowerInvariant())
                {
                    case "float":
                    case "int":
                        floatProperties.Add(name);
                        break;
                    case "integer":
                        integerProperties.Add(name);
                        break;
                    case "color":
                    case "vector":
                        colorProperties.Add(name);
                        break;
                    case "2d":
                    case "2darray":
                        tex2DProperties.Add(name);
                        break;
                    case "3d":
                    case "cube":
                    case "cubearray":
                        tex3DCubeProperties.Add(name);
                        break;
                    default:
                        if (type.StartsWithSimple("range"))
                        {
                            floatProperties.Add(name);
                        }
                        else
                        {
                            unknownTypeProperties.Add(name);
                        }
                        break;
                }
            }

            public void SetName(string name)
            {
                this.name = name;
                files[0].lines[0] = $"Shader \"d4rkpl4y3r/Optimizer/{name}\" // {originalShader.name}";
            }
        }

        private List<string> output;
        private ParsedShader.Pass currentPass;
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
        private Dictionary<string, bool> poiUsedPropertyDefines;
        private string vertexInUv0Member;
        private string vertexInUv0EndSwizzle = "";
        private HashSet<string> texturesToCallSoTheSamplerDoesntDisappear;
        private List<string> setKeywords;
        private int curlyBraceDepth = 0;
        private string sanitizedMaterialName;
        private OptimizedShader optimizedShader = new OptimizedShader();

        private ShaderOptimizer() {}

        public static OptimizedShader Run(ParsedShader source,
            Dictionary<string, string> staticPropertyValues = null,
            int mergedMeshCount = 0,
            List<string> mergedMeshNames = null,
            HashSet<(string name, bool isVector)> defaultAnimatedProperties = null,
            List<int> mergedMeshIndices = null,
            Dictionary<string, (string type, List<string> values)> arrayPropertyValues = null,
            Dictionary<string, string> texturesToNullCheck = null,
            HashSet<string> texturesToMerge = null,
            Dictionary<string, string> animatedPropertyValues = null,
            List<string> setKeywords = null,
            Dictionary<string, bool> poiUsedPropertyDefines = null,
            string sanitizedMaterialName = null
            )
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
                poiUsedPropertyDefines = poiUsedPropertyDefines ?? new Dictionary<string, bool>(),
                animatedPropertyValues = animatedPropertyValues ?? new Dictionary<string, string>(),
                setKeywords = setKeywords ?? new List<string>(),
                sanitizedMaterialName = sanitizedMaterialName ?? Path.GetFileNameWithoutExtension(source.filePath)
            };
            optimizer.texturesToReplaceCalls = new HashSet<string>(
                optimizer.texturesToMerge.Union(optimizer.texturesToNullCheck.Keys));
            optimizer.optimizedShader.originalShader = source;
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
            return optimizer.optimizedShader;
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
                if ((seenOnce.Count == 1 && seenMultiple.Count == 1) || values.Count == 2)
                {
                    // all values but one are the same, so we can use a ternary operator
                    int index = values.IndexOf(seenOnce.First());
                    var secondValue = values.Count == 2 ? values[1 - index] : seenMultiple.First();
                    output.Add($"{arrayProperty.Key} = d4rkAvatarOptimizer_MaterialID == {index} ? {seenOnce.First()} : {secondValue};");
                }
                else if (values.Count <= 32 && seenOnce.Count + seenMultiple.Count == 2)
                {
                    // we can use a ternary operator to select between two values based on a bit field
                    var firstValue = seenMultiple.First();
                    var secondValue = seenMultiple.Last();
                    uint bitField = 0;
                    for (int i = 0; i < values.Count; i++)
                    {
                        if (values[i] == firstValue)
                            bitField |= 1u << i;
                    }
                    output.Add($"{arrayProperty.Key} = ((1u << d4rkAvatarOptimizer_MaterialID) & {bitField}) != 0 ? {firstValue} : {secondValue};");
                }
                else
                {
                    // we need to index into the array
                    output.Add($"{arrayProperty.Key} = d4rkAvatarOptimizerArray{arrayProperty.Key}[d4rkAvatarOptimizer_MaterialID];");
                }
            }
        }

        private bool ArrayPropertyNeedsIndexing(List<string> values)
        {
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
            if ((seenOnce.Count == 1 && seenMultiple.Count == 1) || values.Count == 2)
                return false;
            if (values.Count <= 32 && seenOnce.Count + seenMultiple.Count == 2)
                return false;
            return true;
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

        private void InjectVertexShaderCode(List<string> source, ref int sourceLineIndex)
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
                || (currentPass.geometry != null && mergedMeshCount > 1)
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
            curlyBraceDepth++;
            while (++sourceLineIndex < source.Count)
            {
                ParseAndEvaluateIfex(source, ref sourceLineIndex, output);
                string line = source[sourceLineIndex];
                if (line[0] == '#')
                {
                    line = PartialEvalPreprocessorLine(source, ref sourceLineIndex);
                    if (line == null)
                        continue;
                    output.Add(line);
                    originalVertexShader?.Add(line);
                    continue;
                }
                if (inParam != null && vertexInUv0EndSwizzle != "")
                {
                    line = Regex.Replace(line, $"({inParam.name}\\s*\\.\\s*{vertexInUv0Member})([^0-9a-zA-Z])", $"$1{vertexInUv0EndSwizzle}$2");
                }
                originalVertexShader?.Add(line);
                if (line == "}")
                {
                    if (--curlyBraceDepth == 0)
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
                    curlyBraceDepth++;
                }
                else if (needToPassOnMeshOrMaterialID && line.StartsWithSimple("return"))
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
            curlyBraceDepth++;
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
            while (++sourceLineIndex < source.Count)
            {
                ParseAndEvaluateIfex(source, ref sourceLineIndex, output);
                line = source[sourceLineIndex];
                if (line == "}")
                {
                    output.Add(line);
                    if (--curlyBraceDepth == 0)
                    {
                        break;
                    }
                }
                else if (line == "{")
                {
                    output.Add(line);
                    curlyBraceDepth++;
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
            curlyBraceDepth++;
            if (texturesToCallSoTheSamplerDoesntDisappear.Count > 0)
            {
                shouldInjectDummyTextureUsage = true;
                dummyTextureNullReturn = nullReturn;
            }
        }

        private bool shouldInjectDummyTextureUsage = false;
        private string dummyTextureNullReturn = null;
        private void InjectDummyTextureUsage()
        {
            if (!shouldInjectDummyTextureUsage)
                return;
            shouldInjectDummyTextureUsage = false;
            string returnStatement = null;
            output.RemoveAt(output.Count - 1);
            if (output[output.Count - 1].StartsWithSimple("return"))
            {
                returnStatement = output[output.Count - 1];
                output.RemoveAt(output.Count - 1);
            }
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
            output.Add($"if (d4rkAvatarOptimizer_sum) {dummyTextureNullReturn}");
            output.Add("}");
            if (returnStatement != null)
                output.Add(returnStatement);
            output.Add("}");
        }

        private void DuplicateFunctionWithTextureParameter(List<string> source, ref int sourceLineIndex)
        {
            if (texturesToReplaceCalls.Count == 0)
            {
                output.Add(source[sourceLineIndex]);
                return;
            }

            int lineIndex = sourceLineIndex;
            var func = ShaderAnalyzer.ParseFunctionDefinition(source, ref lineIndex);

            if (!func.parameters.Any(p => p.type == "sampler2D" || (p.type.StartsWithSimple("Texture2D") && !p.type.StartsWithSimple("Texture2DArray"))))
            {
                output.Add(source[sourceLineIndex]);
                return;
            }

            string functionDefinitionStart = source[sourceLineIndex].Split('(')[0] + "(";
            List<string> functionDefinition;
            try {
                lineIndex = sourceLineIndex;
                functionDefinition = ShaderAnalyzer.ParseFunctionParametersWithPreprocessorStatements(source, ref lineIndex);
                sourceLineIndex = lineIndex;
            } catch (ShaderAnalyzer.ParserException) {
                Debug.LogWarning($"Failed to parse function parameters for function {func.name}. Skipping duplication.");
                output.Add(source[sourceLineIndex]);
                return;
            }
            for (int i = 0; i < functionDefinition.Count; i++)
            {
                if (functionDefinition[i][0] != '#')
                {
                    functionDefinition[i] = functionDefinition[i] + ",";
                }
            }
            for (int i = functionDefinition.Count - 1; i >= 0; i--)
            {
                if (functionDefinition[i][0] != '#')
                {
                    functionDefinition[i] = functionDefinition[i].Substring(0, functionDefinition[i].Length - 1);
                    break;
                }
            }
            functionDefinition.Insert(0, functionDefinitionStart);
            functionDefinition.Add(")");
            functionDefinition.Add("{");

            output.AddRange(functionDefinition);
            
            var functionBody = new List<string>();
            int exitCurlyBraceDepth = curlyBraceDepth++;
            while (++sourceLineIndex < source.Count)
            {
                ParseAndEvaluateIfex(source, ref sourceLineIndex, output);
                string line = source[sourceLineIndex];
                if (line[0] == '#')
                {
                    line = PartialEvalPreprocessorLine(source, ref sourceLineIndex);
                    if (line != null)
                        functionBody.Add(line);
                    continue;
                }
                functionBody.Add(line);
                if (line == "}")
                {
                    if (--curlyBraceDepth == exitCurlyBraceDepth)
                    {
                        break;
                    }
                }
                else if (line == "{")
                {
                    curlyBraceDepth++;
                }
            }
            output.AddRange(functionBody);
            foreach (var tex in texturesToReplaceCalls)
            {
                foreach (var line in functionDefinition)
                {
                    output.Add(line.Contains("2D") ? Regex.Replace(line, "(sampler2D|Texture2D(<[^<>]*>)?) ", tex + "_Wrapper ") : line);
                }
                output.AddRange(functionBody);
            }
        }

        private void InjectDummyCBufferUsage(string nullReturn)
        {
            if (localMeshCount <= 1 || mergedMeshCount <= 1)
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
                output.Add($"if (0.5 > d4rkAvatarOptimizerAnimatedScalars[d4rkAvatarOptimizer_MeshID]) {nullReturn}");
            else
                output.Add($"if (0.5 > _IsActiveMesh{mergedMeshIndices.First()}) {nullReturn}");
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

        private void InjectPropertyArrays()
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
                    if (type[type.Length - 1] == '4')
                    {
                        type = "float4";
                        hasVectorCBufferAliasArray = true;
                        currentOutput = vectorOutput;
                        currentPackOffset = currentVectorPackOffset = AllocateCBufferRegisters(currentVectorPackOffset, usedVectorRegisters);
                        CBufferAliasArray.Add(name, ("d4rkAvatarOptimizerAnimatedVectors", currentPackOffset));
                    }
                    else
                    {
                        type = "float";
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
            foreach(var keyword in setKeywords.Where(k => currentPass.shaderFeatureKeyWords.Contains(k)))
            {
                output.Add($"#define {keyword} 1");
            }
            output.Add("uniform float d4rkAvatarOptimizer_Zero;");
            output.Add("static uint d4rkAvatarOptimizer_MaterialID = 0;");
            output.Add("static uint d4rkAvatarOptimizer_MeshID = 0;");
            if (arrayPropertyValues.Count > 0)
            {
                foreach (var arrayProperty in arrayPropertyValues)
                {
                    var (type, values) = arrayProperty.Value;
                    if (!ArrayPropertyNeedsIndexing(values))
                        continue;
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
                string name = property.Key;
                if (name.Length > 10 && name[0] == 'a' && name[5] == 'I' && name.StartsWith("arrayIndex") && texturesToMerge.Contains(name.Substring(10)))
                {
                    output.Add($"static float {name} = {property.Value};");
                }
            }
            int textureWrapperCount = 0;
            foreach (var texName in texturesToReplaceCalls)
            {
                string nullCheck = null;
                if (texturesToNullCheck.TryGetValue(texName, out string textureDefaultValue)) {
                    nullCheck = $"if (!shouldSample{texName}) return {textureDefaultValue};";
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
                output.Add("float2 ddxuv = ddx(uv);");
                output.Add("float2 ddyuv = ddy(uv);");
                if (nullCheck != null) output.Add(nullCheck);
                output.Add($"return {newTexName}.SampleGrad(sampl, {uv}, ddxuv, ddyuv);}}");

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
            string type = ShaderAnalyzer.ParseTypeAndTrailingWhitespace(line, ref index);
            if (type == "uniform")
            {
                type = ShaderAnalyzer.ParseTypeAndTrailingWhitespace(line, ref index);
            }
            if (type == null || type == "return")
                return (null, null);
            string identifier = ShaderAnalyzer.ParseIdentifierAndTrailingWhitespace(line, ref index);
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
        
        enum ConditionResult
        {
            True,
            False,
            Unknown
        }
        private Stack<ConditionResult> lastIfEvalResultStack;
        private Stack<Dictionary<string, (bool defined, int? value)>> knownDefines = new Stack<Dictionary<string, (bool defined, int? value)>>();
        private Stack<HashSet<string>> alreadyIncludedFiles = new Stack<HashSet<string>>();
        private void PushPreprocessorScope()
        {
            knownDefines.Push(new Dictionary<string, (bool defined, int? value)>(knownDefines.Peek()));
            alreadyIncludedFiles.Push(new HashSet<string>(alreadyIncludedFiles.Peek()));
        }
        private void PopPreprocessorScope()
        {
            knownDefines.Pop();
            alreadyIncludedFiles.Pop();
        }
        string PartialEvalPreprocessorLine(List<string> source, ref int sourceLineIndex)
        {
            var line = source[sourceLineIndex];
            if (line[0] != '#')
                return line;
            void SkipWhitespace(string s, ref int index) { while (index < s.Length && char.IsWhiteSpace(s[index])) index++; }
            ConditionResult EvalPreprocessorCondition(string expr, ref int index)
            {
                // hardcoded parse of poiyomi texture prop guards as OPTIMIZER_ENABLED is also rarely used for other cases which could break when properties are not inline replaced
                if (index == 0 && expr.Length > 44 && expr[0] == 'd' && expr[8] == 'P') {
                    var match = Regex.Match(expr, @"defined\((PROP\w+)\) || !defined\(OPTIMIZER_ENABLED\)");
                    if (match.Success) {
                        if (poiUsedPropertyDefines.TryGetValue(match.Groups[1].Value, out var used))
                            return used ? ConditionResult.True : ConditionResult.False;
                        return ConditionResult.Unknown;
                    }
                }
                // parse flat lists of defined() and !defined() calls that are either all || or all && connected. no nesting.
                var values = new List<ConditionResult>();
                bool allAnd = false;
                bool allOr = false;
                while (index < expr.Length)
                {
                    SkipWhitespace(expr, ref index);
                    if (index == expr.Length)
                        break;
                    if (expr[index] == '|' && expr[index + 1] == '|')
                    {
                        if (allAnd)
                        {
                            output.Add($"// Mixed && and || at {index}");
                            return ConditionResult.Unknown;
                        }
                        allOr = true;
                        index += 2;
                    }
                    else if (expr[index] == '&' && expr[index + 1] == '&')
                    {
                        if (allOr)
                        {
                            output.Add($"// Mixed && and || at {index}");
                            return ConditionResult.Unknown;
                        }
                        allAnd = true;
                        index += 2;
                    }
                    SkipWhitespace(expr, ref index);
                    bool isNegated = false;
                    if (expr[index] == '!')
                    {
                        isNegated = true;
                        index++;
                    }
                    SkipWhitespace(expr, ref index);
                    if (index == expr.Length)
                    {
                        output.Add($"// Unexpected end of expression at {index}");
                        return ConditionResult.Unknown;
                    }
                    if (expr[index] == '(')
                    {
                        index++;
                        var res = EvalPreprocessorCondition(expr, ref index);
                        if (isNegated && res != ConditionResult.Unknown)
                            res = res == ConditionResult.True ? ConditionResult.False : ConditionResult.True;
                        values.Add(res);
                        continue;
                    }
                    if (expr.Length - index < 7 || expr[index] != 'd' || expr[index + 1] != 'e' || expr[index + 2] != 'f' || expr[index + 3] != 'i' || expr[index + 4] != 'n' || expr[index + 5] != 'e' || expr[index + 6] != 'd')
                    {
                        output.Add($"// Expected defined at {index}, got {expr.Substring(index, System.Math.Min(10, expr.Length - index))}");
                        return ConditionResult.Unknown;
                    }
                    index += 7;
                    SkipWhitespace(expr, ref index);
                    if (expr[index] != '(')
                    {
                        output.Add($"// Expected ( at {index}, got {expr.Substring(index, System.Math.Min(10, expr.Length - index))}");
                        return ConditionResult.Unknown;
                    }
                    index++;
                    SkipWhitespace(expr, ref index);
                    string constantName = ShaderAnalyzer.ParseIdentifierAndTrailingWhitespace(expr, ref index);
                    if (constantName == null)
                    {
                        output.Add($"// Expected identifier at {index}, got {expr.Substring(index, System.Math.Min(10, expr.Length - index))}");
                        return ConditionResult.Unknown;
                    }
                    if (expr[index] != ')')
                    {
                        output.Add($"// Expected ) at {index}, got {expr.Substring(index, System.Math.Min(10, expr.Length - index))}");
                        return ConditionResult.Unknown;
                    }
                    index++;
                    if (knownDefines.Peek().TryGetValue(constantName, out var constant))
                    {
                        values.Add(constant.defined ^ isNegated ? ConditionResult.True : ConditionResult.False);
                    }
                    else
                    {
                        values.Add(ConditionResult.Unknown);
                    }
                    SkipWhitespace(expr, ref index);
                    if (index == expr.Length)
                        break;
                    if (expr[index] == ')')
                    {
                        index++;
                        break;
                    }
                }
                if (!allAnd && !allOr)
                {
                    return values[0];
                }
                if (allAnd)
                {
                    if (values.Contains(ConditionResult.False))
                        return ConditionResult.False;
                    if (values.Contains(ConditionResult.Unknown))
                        return ConditionResult.Unknown;
                    return ConditionResult.True;
                }
                if (allOr)
                {
                    if (values.Contains(ConditionResult.True))
                        return ConditionResult.True;
                    if (values.Contains(ConditionResult.Unknown))
                        return ConditionResult.Unknown;
                    return ConditionResult.False;
                }
                return ConditionResult.Unknown;
            }
            int SkipUntilElseOrEndif(ref int lineIndex)
            {
                int depth = 0;
                int startLineIndex = lineIndex;
                while (++lineIndex < source.Count)
                {
                    var innerLine = source[lineIndex];
                    if (innerLine[0] != '#')
                        continue;
                    if (innerLine.Length > 3 && innerLine[1] == 'i' && innerLine[2] == 'f' && innerLine[3] != 'e')
                    {
                        depth++;
                    }
                    else if (innerLine.Length > 5 && innerLine[1] == 'e' && innerLine[2] == 'n' && innerLine[3] == 'd' && innerLine[4] == 'i' && innerLine[5] == 'f')
                    {
                        if (depth == 0)
                        {
                            return --lineIndex - startLineIndex;
                        }
                        depth--;
                    }
                    else if (depth == 0 && innerLine.Length > 4 && innerLine[1] == 'e' && innerLine[2] == 'l' && ((innerLine[3] == 's' && innerLine[4] == 'e') || (innerLine[3] == 'i' && innerLine[4] == 'f')))
                    {
                        return --lineIndex - startLineIndex;
                    }
                }
                return lineIndex - startLineIndex;
            }
            string TryPoiFurInstanceCountOptimization(ref int lineIndex)
            {
                if (source[lineIndex] != "#if !defined(OPTIMIZER_ENABLED)")
                    return null;
                if (source[lineIndex + 1] != "[instance(32)]")
                    return null;
                if (source[lineIndex + 2] != "#else")
                    return null;
                if (!source[lineIndex + 3].StartsWithSimple("[instance("))
                    return null;
                if (source[lineIndex + 4] != "#endif") 
                    return null;
                int charIndex = 10;
                var instanceCountLine = source[lineIndex + 3];
                SkipWhitespace(instanceCountLine, ref charIndex);
                string instanceParameter = ShaderAnalyzer.ParseIdentifierAndTrailingWhitespace(instanceCountLine, ref charIndex);
                if (animatedPropertyValues.ContainsKey(instanceParameter) || arrayPropertyValues.ContainsKey(instanceParameter))
                    return null;
                if (!staticPropertyValues.TryGetValue(instanceParameter, out var instanceValue))
                    return null;
                lineIndex += 4;
                return instanceCountLine.Replace(instanceParameter, instanceValue);
            }
            if (line.Length > 3 && line[1] == 'i' && line[2] == 'f')
            {
                var poiFurInstanceOptimizedLine = TryPoiFurInstanceCountOptimization(ref sourceLineIndex);
                if (poiFurInstanceOptimizedLine != null)
                    return poiFurInstanceOptimizedLine;
                string expr = "";
                if (line.Length > 6 && line[3] == 'd' && line[4] == 'e' && line[5] == 'f')
                    expr = $"defined({line.Substring(6).TrimStart()})";
                else if (line.Length > 7 && line[3] == 'n' && line[4] == 'd' && line[5] == 'e' && line[6] == 'f')
                    expr = $"!defined({line.Substring(7).TrimStart()})";
                else
                    expr = line.Substring(4).TrimStart();
                var exprIndex = 0;
                var evalResult = EvalPreprocessorCondition(expr, ref exprIndex);
                lastIfEvalResultStack.Push(evalResult);
                if (evalResult == ConditionResult.True)
                {
                    return null;
                }
                if (evalResult == ConditionResult.False)
                {
                    int skipped = SkipUntilElseOrEndif(ref sourceLineIndex);
                    return $"// Skipped {skipped} lines | {line}";
                }
                PushPreprocessorScope();
                return line;
            }
            else if (line.Length > 4 && line[1] == 'e' && line[2] == 'l' && ((line[3] == 's' && line[4] == 'e') || (line[3] == 'i' && line[4] == 'f')))
            {
                var lastEval = lastIfEvalResultStack.Pop();
                if (lastEval == ConditionResult.True)
                {
                    int skipped = SkipUntilElseOrEndif(ref sourceLineIndex);
                    lastIfEvalResultStack.Push(ConditionResult.True);
                    return $"// Skipped {skipped} lines";
                }
                if (lastEval == ConditionResult.Unknown)
                {
                    PopPreprocessorScope();
                    PushPreprocessorScope();
                }
                if (line[3] == 'i')
                {
                    string expr = line.Substring(6).TrimStart();
                    var exprIndex = 0;
                    var evalResult = EvalPreprocessorCondition(expr, ref exprIndex);
                    if (evalResult == ConditionResult.Unknown)
                    {
                        lastIfEvalResultStack.Push(ConditionResult.Unknown);
                        if (lastEval == ConditionResult.False)
                        {
                            PushPreprocessorScope();
                            return $"#{line.Substring(3)}";
                        }
                        return line;
                    }
                    if (evalResult == ConditionResult.True)
                    {
                        if (lastEval == ConditionResult.Unknown)
                        {
                            lastIfEvalResultStack.Push(ConditionResult.Unknown);
                            return line;
                        }
                        lastIfEvalResultStack.Push(ConditionResult.True);
                        return null;
                    }
                    int skipped = SkipUntilElseOrEndif(ref sourceLineIndex);
                    if (lastEval == ConditionResult.Unknown)
                    {
                        lastIfEvalResultStack.Push(ConditionResult.Unknown);
                        return line;
                    }
                    lastIfEvalResultStack.Push(ConditionResult.False);
                    return $"// Skipped {skipped} lines | {line}";
                }
                else
                {
                    if (lastEval == ConditionResult.Unknown)
                    {
                        lastIfEvalResultStack.Push(ConditionResult.Unknown);
                        return line;
                    }
                    lastIfEvalResultStack.Push(lastEval == ConditionResult.False ? ConditionResult.True : ConditionResult.False);
                    return null;
                }
            }
            else if (line.Length > 5 && line[1] == 'e' && line[2] == 'n' && line[3] == 'd' && line[4] == 'i' && line[5] == 'f')
            {
                if (lastIfEvalResultStack.Pop() == ConditionResult.Unknown)
                {
                    PopPreprocessorScope();
                    return line;
                }
                return null;
            }
            else if (line.Length > 7 && line[1] == 'd' && line[2] == 'e' && line[3] == 'f' && line[4] == 'i' && line[5] == 'n' && line[6] == 'e')
            {
                var index = 7;
                SkipWhitespace(line, ref index);
                var identifier = ShaderAnalyzer.ParseIdentifierAndTrailingWhitespace(line, ref index);
                if (identifier == null)
                {
                    output.Add($"// Expected identifier at {index}, got {line.Substring(index, System.Math.Min(10, line.Length - index))}");
                    return line;
                }
                knownDefines.ToList().ForEach(d => d.Remove(identifier));
                knownDefines.Peek()[identifier] = (true, null);
                return line;
            }
            else if (line.Length > 5 && line[1] == 'u' && line[2] == 'n' && line[3] == 'd' && line[4] == 'e' && line[5] == 'f')
            {
                var index = 6;
                SkipWhitespace(line, ref index);
                var identifier = ShaderAnalyzer.ParseIdentifierAndTrailingWhitespace(line, ref index);
                if (identifier == null)
                {
                    output.Add($"// Expected identifier at {index}, got {line.Substring(index, System.Math.Min(10, line.Length - index))}");
                    return line;
                }
                knownDefines.ToList().ForEach(d => d.Remove(identifier));
                knownDefines.Peek()[identifier] = (false, null);
                return line;
            }
            else if (line.Length > 6 && line[1] == 'p' && line[2] == 'r' && line[3] == 'a' && line[4] == 'g' && line[5] == 'm' && line[6] == 'a')
            {
                var index = 7;
                SkipWhitespace(line, ref index);
                var identifier = ShaderAnalyzer.ParseIdentifierAndTrailingWhitespace(line, ref index);
                if (identifier == null) {
                    return null;
                }
                switch (identifier) {
                    case "vertex":
                        if (((currentPass.geometry != null && mergedMeshCount > 1) || arrayPropertyValues.Count > 0 || animatedPropertyValues.Count > 0)) {
                            pragmaOutput.Add("#pragma vertex d4rkAvatarOptimizer_vertexWithWrapper");
                        } else {
                            pragmaOutput.Add(line);
                        }
                        break;
                    case "shader_feature":
                    case "shader_feature_local":
                    case "skip_optimizations":
                        break;
                    default:
                        pragmaOutput.Add(line);
                        break;
                }
                return null;
            }
            else
            {
                return line;
            }
        }

        private void ParseCodeLinesRecursive(List<string> source, ref int sourceLineIndex, string endSymbol)
        {
            for (; sourceLineIndex < source.Count; sourceLineIndex++)
            {
                ParseAndEvaluateIfex(source, ref sourceLineIndex, output);
                var line = source[sourceLineIndex];
                if (line == endSymbol)
                    return;
                if (line[0] == '#')
                {
                    if (line.Length > 7 && line[3] == 'c' && line.StartsWith("#include "))
                    {
                        var includeName = ShaderAnalyzer.ParseIncludeDirective(line);
                        if (parsedShader.text.TryGetValue(includeName, out var includeSource))
                        {
                            if (alreadyIncludedFiles.Peek().Contains(includeName))
                            {
                                output.Add($"// Already included {includeName}");
                                continue;
                            }
                            int innerLineIndex = 0;
                            output.Add($"// Include {includeName}");
                            alreadyIncludedFiles.Peek().Add(includeName);
                            ParseCodeLinesRecursive(includeSource, ref innerLineIndex, endSymbol);
                        }
                        else
                        {
                            output.Add(line);
                        }
                    }
                    else
                    {
                        line = PartialEvalPreprocessorLine(source, ref sourceLineIndex);
                        if (line != null)
                            output.Add(line);
                    }
                    continue;
                }
                if (line == "{")
                {
                    output.Add(line);
                    curlyBraceDepth++;
                    continue;
                }
                if (line == "}")
                {
                    output.Add(line);
                    if (--curlyBraceDepth == 0 && shouldInjectDummyTextureUsage)
                        InjectDummyTextureUsage();
                    continue;
                }
                var func = ShaderAnalyzer.ParseFunctionDefinition(line);
                if (currentPass.vertex != null && func.name == currentPass.vertex.name)
                {
                    InjectVertexShaderCode(source, ref sourceLineIndex);
                }
                else if (currentPass.geometry != null && currentPass.fragment != null && currentPass.vertex != null && func.name == currentPass.geometry.name)
                {
                    InjectGeometryShaderCode(source, ref sourceLineIndex);
                }
                else if (currentPass.vertex != null && currentPass.fragment != null && func.name == currentPass.fragment.name)
                {
                    InjectFragmentShaderCode(source, ref sourceLineIndex);
                }
                else if (func.name != null)
                {
                    DuplicateFunctionWithTextureParameter(source, ref sourceLineIndex);
                }
                else if ((arrayPropertyValues.Count > 0 || mergedMeshCount > 1) && line.StartsWithSimple("struct "))
                {
                    output.Add(line);
                    int dummyIndex = 0;
                    ShaderAnalyzer.ParseIdentifierAndTrailingWhitespace(line, ref dummyIndex);
                    var structName = ShaderAnalyzer.ParseIdentifierAndTrailingWhitespace(line, ref dummyIndex);
                    var vertIn = currentPass.vertex.parameters.FirstOrDefault(p => p.isInput && p.semantic == null);
                    if (structName == vertIn?.type)
                    {
                        while (++sourceLineIndex < source.Count)
                        {
                            line = source[sourceLineIndex];
                            var match = Regex.Match(line, @"^(\w+\s+)*(\w+)\s+(\w+)\s*:\s*(\w+)\s*;");
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
                            if (line[0] == '}')
                            {
                                output.Add($"float4 {vertexInUv0Member} : TEXCOORD0;");
                                output.Add(line);
                                break;
                            }
                            output.Add(line);
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
                            bool isTextureSamplerState = (type == "SamplerState" || type == "sampler") && name.StartsWithSimple("sampler");
                            string referencedTexture = isTextureSamplerState ? name.Substring(7) : null;
                            if (isTextureSamplerState && !texturesToReplaceCalls.Contains(referencedTexture))
                            {
                                if (parsedShader.propertyTable.ContainsKey(referencedTexture))
                                {
                                    texturesToCallSoTheSamplerDoesntDisappear.Add(referencedTexture);
                                    output.Add("#define DUMMY_USE_TEXTURE_TO_PRESERVE_SAMPLER_" + referencedTexture);
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
                                && !(isTextureSamplerState && texturesToReplaceCalls.Contains(referencedTexture))
                                && ((name != "_LightColor0" && name != "_SpecColor")
                                    || alreadyIncludedFiles.Peek().LastOrDefault() == "UnityLightingCommon.cginc"))
                            {
                                output.Add(type + " " + name + ";");
                            }
                        }
                    }
                    else if (line.StartsWithSimple("UNITY_DECLARE_TEX2D"))
                    {
                        var texName = line.Split('(')[1].Split(')')[0].Trim();
                        if (!texturesToReplaceCalls.Contains(texName))
                        {
                            if (!line.Contains("_NOSAMPLER") && parsedShader.properties.Any(p => p.name == texName))
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

        private void ParseAndEvaluateIfex(List<string> lines, ref int lineIndex, List<string> debugOutput)
        {
            string line = lines[lineIndex];
            if (line[0] != '#')
                return;
            if (line == "#endex")
            {
                lineIndex++;
                ParseAndEvaluateIfex(lines, ref lineIndex, debugOutput);
                return;
            }
            if (!line.StartsWithSimple("#ifex"))
                return;
            var conditions = ShaderAnalyzer.ParseIfexConditions(line);
            if (conditions == null) {
                lineIndex++;
                debugOutput?.Add($"// {line} failed to parse conditions, just skip statement");
                ParseAndEvaluateIfex(lines, ref lineIndex, debugOutput);
                return;
            }
            var outputString = $"// #ifex ";
            var firstCondition = true;
            foreach (var condition in conditions) {
                var name = condition.name;
                var notEquals = condition.notEquals;
                var compValue = condition.value;
                if (!staticPropertyValues.TryGetValue(name, out var valueString)) {
                    lineIndex++;
                    debugOutput?.Add($"// #ifex {name} not found in static properties");
                    ParseAndEvaluateIfex(lines, ref lineIndex, debugOutput);
                    return;
                }
                var value = float.Parse(valueString);
                outputString += $"{(firstCondition ? "" : " && ")}{name}({value}) {(notEquals ? '!' : '=')}= {compValue}";
                firstCondition = false;
                if ((compValue != value) ^ notEquals) {
                    lineIndex++;
                    debugOutput?.Add(outputString + ", FALSE");
                    ParseAndEvaluateIfex(lines, ref lineIndex, debugOutput);
                    return;
                }
            }

            // skip all code until matching #endex
            int depth = 0;
            int linesSkipped = 0;
            while (++lineIndex < lines.Count)
            {
                line = lines[lineIndex];
                linesSkipped++;
                if (line[0] != '#')
                    continue;
                if (line == "#endex")
                {
                    if (depth == 0)
                    {
                        lineIndex++;
                        debugOutput?.Add($"{outputString}, TRUE skipped {linesSkipped} lines");
                        ParseAndEvaluateIfex(lines, ref lineIndex, debugOutput);
                        return;
                    }
                    depth--;
                }
                else if (line.Length >= 5 && line[4] == 'x' && line[1] == 'i' && line[2] == 'f' && line[3] == 'e')
                {
                    depth++;
                }
            }
            debugOutput?.Add($"// didn't find matching #endex, skipped {linesSkipped} lines");
        }

        private void Run()
        {
            lastIfEvalResultStack = new Stack<ConditionResult>();
            alreadyIncludedFiles.Push(new HashSet<string>());
            knownDefines.Push(new Dictionary<string, (bool defined, int? value)>());
            output = new List<string>();
            var tags = new List<string>();
            var lines = parsedShader.text[".shader"];
            var propertyBlock = new List<string>();
            int propertyBlockInsertionIndex = 0;
            int propertyBlockStartParseIndex = 0; 
            int lineIndex = 0;
            while (lineIndex < lines.Count)
            {
                ParseAndEvaluateIfex(lines, ref lineIndex, output);
                string line = lines[lineIndex++];
                output.Add(line);
                if (line == "Properties")
                {
                    break;
                }
            }
            while (lineIndex < lines.Count)
            {
                ParseAndEvaluateIfex(lines, ref lineIndex, null);
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
                        optimizedShader.AddProperty(name, "Float");
                        alreadyAdded.Add(name);
                    }
                    foreach (var animatedProperty in animatedPropertyValues)
                    {
                        var prop = parsedShader.propertyTable[animatedProperty.Key];
                        string defaultValue = "0";
                        string type = "Float";
                        string tagString = tagString = prop.hasGammaTag ? "[gamma] " : "";
                        if (prop.type == ParsedShader.Property.Type.Vector) {
                            defaultValue = "(0,0,0,0)";
                            type = "Vector";
                        } else if (prop.type == ParsedShader.Property.Type.Color || prop.type == ParsedShader.Property.Type.ColorHDR) {
                            defaultValue = "(0,0,0,0)";
                            type = "Color";
                        } 
                        if (prop.type == ParsedShader.Property.Type.ColorHDR) {
                            tagString += "[hdr] ";
                        }
                        foreach (int i in mergedMeshIndices) {
                            var fullPropertyName = $"d4rkAvatarOptimizer{prop.name}_ArrayIndex{i}";
                            propertyBlock.Add($"{tagString}{fullPropertyName}(\"{prop.name} {i}\", {type}) = {defaultValue}");
                            optimizedShader.AddProperty(fullPropertyName, type);
                            alreadyAdded.Add(fullPropertyName);
                        }
                    }
                    foreach (var defaultAnimatedProperty in defaultAnimatedProperties)
                    {
                        if (alreadyAdded.Contains(defaultAnimatedProperty.name))
                            continue;
                        string defaultValue = defaultAnimatedProperty.isVector ? "(0,0,0,0)" : "0";
                        string type = defaultAnimatedProperty.isVector ? "Vector" : "Float";
                        string displayName = defaultAnimatedProperty.name;
                        string tagString = "";
                        if (displayName.StartsWithSimple("_IsActiveMesh")) {
                            int meshIndex = int.Parse(displayName.Substring("_IsActiveMesh".Length));
                            displayName = $"_IsActiveMesh{meshIndex} {mergedMeshNames[meshIndex]}";
                        }
                        if (!parsedShader.propertyTable.TryGetValue(defaultAnimatedProperty.name, out var nativeProperty)) {
                            var match = Regex.Match(defaultAnimatedProperty.name,  @"d4rkAvatarOptimizer(\w+)_ArrayIndex(\d+)");
                            if (match.Success) {
                                parsedShader.propertyTable.TryGetValue(match.Groups[1].Value, out nativeProperty);
                                displayName = $"{match.Groups[1].Value} {match.Groups[2].Value}";
                            }
                        }
                        if (nativeProperty != null) {
                            tagString = nativeProperty.hasGammaTag ? "[gamma] " : "";
                            if (nativeProperty.type == ParsedShader.Property.Type.Color || nativeProperty.type == ParsedShader.Property.Type.ColorHDR) {
                                type = "Color";
                            }
                            if (nativeProperty.type == ParsedShader.Property.Type.ColorHDR) {
                                tagString += "[hdr] ";
                            }
                        }
                        propertyBlock.Add($"{tagString}{defaultAnimatedProperty.name}(\"{displayName}\", {type}) = {defaultValue}");
                        optimizedShader.AddProperty(defaultAnimatedProperty.name, type);
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
                ParseAndEvaluateIfex(lines, ref lineIndex, output);
                string line = lines[lineIndex];
                if (line.StartsWith("CustomEditor"))
                    continue;
                output.Add(line);
                if (line == "Pass")
                {
                    alreadyIncludedFiles.Clear();
                    alreadyIncludedFiles.Push(new HashSet<string>());
                    knownDefines.Clear();
                    knownDefines.Push(new Dictionary<string, (bool defined, int? value)>());
                    knownDefines.Peek()["UNITY_COLORSPACE_GAMMA"] = (false, null);
                    knownDefines.Peek()["SHADER_TARGET_SURFACE_ANALYSIS"] = (false, null);
                }
                else if (line.IndexOf("\"LightMode\"") != -1)
                {
                    var lightMode = line.Substring(line.IndexOf("\"LightMode\"") + "\"LightMode\"".Length).Trim();
                    lightMode = lightMode.Substring(lightMode.IndexOf('"') + 1);
                    lightMode = lightMode.Substring(0, lightMode.IndexOf('"'));
                    foreach (var lightModeDefine in lightModeToDefine)
                    {
                        knownDefines.Peek()[lightModeDefine.Value] = (lightMode == lightModeDefine.Key, null);
                    }
                    if (lightMode == "Meta")
                    {
                        while (output[output.Count - 1] != "Pass")
                        {
                            output.RemoveAt(output.Count - 1);
                        }
                        output.RemoveAt(output.Count - 1);
                        int curlyBraceDepthUntilEndOfPass = 2;
                        while (curlyBraceDepthUntilEndOfPass > 0)
                        {
                            line = lines[++lineIndex];
                            curlyBraceDepthUntilEndOfPass += line == "{" ? 1 : 0;
                            curlyBraceDepthUntilEndOfPass -= line == "}" ? 1 : 0;
                            passID += line == "CGPROGRAM" || line == "HLSLPROGRAM" ? 1 : 0;
                        }
                        output.Add("// Meta pass removed");
                    }
                }
                else if (line == "CGPROGRAM" || line == "HLSLPROGRAM")
                {
                    currentPass = parsedShader.passes[++passID];
                    vertexInUv0Member = "texcoord";
                    texturesToCallSoTheSamplerDoesntDisappear.Clear();
                    pragmaOutput = output;
                    output = new List<string>();
                    InjectPropertyArrays();
                    foreach (var keyword in currentPass.shaderFeatureKeyWords)
                    {
                        knownDefines.Peek()[keyword] = (setKeywords.Contains(keyword), null);
                    }
                    foreach (var skippedShaderVariant in SkippedShaderVariants)
                    {
                        knownDefines.Peek()[skippedShaderVariant] = (false, null);
                    }
                    string endSymbol = line == "CGPROGRAM" ? "ENDCG" : "ENDHLSL";
                    lineIndex++;
                    curlyBraceDepth = 0;
                    ParseCodeLinesRecursive(lines, ref lineIndex, endSymbol);
                    if (curlyBraceDepth != 0)
                    {
                        throw new ShaderAnalyzer.ParserException($"Unbalanced curly braces in {parsedShader.name} pass {passID}");
                    }
                    var includeName = $"{sanitizedMaterialName}_{GetMD5Hash(output).Substring(0, 8)}" + (line == "CGPROGRAM" ? ".cginc" : ".hlsl");
                    outputIncludes.Add((includeName, output));
                    output = pragmaOutput;
                    output.Add($"#include \"{includeName}\"");
                    output.Add(endSymbol);
                }
            }
            tags.Clear();
            for (lineIndex = propertyBlockStartParseIndex; lineIndex < lines.Count; lineIndex++)
            {
                ParseAndEvaluateIfex(lines, ref lineIndex, propertyBlock);
                string line = lines[lineIndex];
                if (line == "}")
                    break;
                var parsedProperty = ShaderAnalyzer.ParsePropertyRaw(line, tags);
                if (parsedProperty == null)
                    continue;
                string tagString = "";
                foreach (var tag in tags)
                {
                    if (tag.Length > 5 || (tag.ToLowerInvariant() != "hdr" && tag.ToLowerInvariant() != "gamma"))
                        continue;
                    tagString += $"[{tag}] ";
                }
                tags.Clear();
                var prop = parsedProperty.Value;
                if (prop.name.StartsWithSimple("_ShaderOptimizer"))
                    continue;
                if (defaultAnimatedProperties.Contains((prop.name, false)) || defaultAnimatedProperties.Contains((prop.name, true)))
                    continue;
                if ((staticPropertyValues.ContainsKey(prop.name) || arrayPropertyValues.ContainsKey(prop.name)) 
                    && !animatedPropertyValues.ContainsKey(prop.name)
                    && parsedShader.propertyTable[prop.name].shaderLabParams.Count == 0)
                    continue;
                if (texturesToMerge.Contains(prop.name))
                {
                    int index = prop.type.LastIndexOf("2D");
                    prop.type = prop.type.Substring(0, index) + "2DArray" + prop.type.Substring(index + 2);
                    if (prop.name == "_MainTex")
                        prop.name = "_MainTexButNotQuiteSoThatUnityDoesntCry";
                }
                propertyBlock.Add($"{tagString}{prop.name}(\"{prop.name}\", {prop.type}) = {prop.defaultValue}");
                optimizedShader.AddProperty(prop.name, prop.type);
            }
            output.InsertRange(propertyBlockInsertionIndex, propertyBlock);
            var shaderHash = GetMD5Hash(output);
            optimizedShader.files = new List<(string name, List<string> lines)>();
            optimizedShader.files.Add(("Shader", output));
            optimizedShader.files.AddRange(outputIncludes);
            optimizedShader.SetName($"{sanitizedMaterialName} {shaderHash.Substring(0, 8)}");
        }
    }
}
#endif