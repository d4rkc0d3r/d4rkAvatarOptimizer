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
        }
        public string name;
        public List<string> lines = new List<string>();
        public List<Property> properties = new List<Property>();
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
                ParsePropertyBlock(parsedShader);
                File.WriteAllLines("Assets/d4rkAvatarOptimizer/TrashBin/LastParsedShader.shader", parsedShader.lines);
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
            catch (FileNotFoundException e)
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

        private enum ReplacePropertysState
        {
            Init,
            PropertyBlock,
            ScanForCG,
            CGInclude,
            CGProgram
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

        public static ParsedShader ReplacePropertysWithConstants(ParsedShader source, Dictionary<string, string> properyValues)
        {
            var output = new ParsedShader();
            var cgInclude = new List<string>();
            int propertyBlockBraceDepth = 0;
            var state = ReplacePropertysState.Init;
            for (int lineIndex = 0; lineIndex < source.lines.Count; lineIndex++)
            {
                string line = source.lines[lineIndex];
                switch(state)
                {
                    case ReplacePropertysState.Init:
                        output.lines.Add(line);
                        if (line == "Properties")
                        {
                            state = ReplacePropertysState.PropertyBlock;
                        }
                        break;
                    case ReplacePropertysState.PropertyBlock:
                        if (line == "{")
                        {
                            propertyBlockBraceDepth++;
                            output.lines.Add(line);
                        }
                        else if (line == "}")
                        {
                            if (--propertyBlockBraceDepth == 0)
                            {
                                state = ReplacePropertysState.ScanForCG;
                            }
                            output.lines.Add(line);
                        }
                        else
                        {
                            var property = ParseProperty(line);
                            string propertyValue;
                            if (!properyValues.TryGetValue(property?.name, out propertyValue))
                            {
                                //output.lines.Add(line);
                            }
                            output.lines.Add(line);
                        }
                        break;
                    case ReplacePropertysState.ScanForCG:
                        if (line == "CGINCLUDE")
                        {
                            state = ReplacePropertysState.CGInclude;
                        }
                        else if (line == "CGPROGRAM")
                        {
                            output.lines.Add(line);
                            foreach (string includeLine in cgInclude)
                            {
                                output.lines.Add(ReplacePropertyDefinition(includeLine, properyValues));
                            }
                            state = ReplacePropertysState.CGProgram;
                        }
                        else
                        {
                            output.lines.Add(line);
                        }
                        break;
                    case ReplacePropertysState.CGInclude:
                        if (line == "ENDCG")
                        {
                            state = ReplacePropertysState.ScanForCG;
                        }
                        else
                        {
                            cgInclude.Add(line);
                        }
                        break;
                    case ReplacePropertysState.CGProgram:
                        if (line == "ENDCG")
                        {
                            state = ReplacePropertysState.ScanForCG;
                        }
                        output.lines.Add(ReplacePropertyDefinition(line, properyValues));
                        break;
                }
            }
            output.name = "d4rkpl4y3r/Optimizer/LastOptimized";
            output.lines[0] = "Shader \"" + output.name + "\"";
            File.WriteAllLines("Assets/d4rkAvatarOptimizer/TrashBin/LastOptimizedShader.shader", output.lines);
            return output;
        }

        private static void ParsePropertyBlock(ParsedShader parsedShader)
        {
            bool isInPropertyBlock = false;
            int propertyBlockBraceDepth = -1;
            int braceDepth = 0;
            for (int lineIndex = 0; lineIndex < parsedShader.lines.Count; lineIndex++)
            {
                string line = parsedShader.lines[lineIndex];
                if (line == "{")
                {
                    braceDepth++;
                }
                else if (line == "}")
                {
                    braceDepth--;
                    if (isInPropertyBlock && braceDepth == propertyBlockBraceDepth)
                    {
                        isInPropertyBlock = false;
                        return;
                    }
                }
                else if (line == "Properties" && parsedShader.lines[lineIndex + 1] == "{")
                {
                    isInPropertyBlock = true;
                    propertyBlockBraceDepth = braceDepth;
                    braceDepth++;
                    lineIndex++;
                }
                else if (isInPropertyBlock)
                {
                    var property = ParseProperty(line);
                    if (property != null)
                    {
                        parsedShader.properties.Add(property);
                    }
                }
            }
        }
    }
}
#endif