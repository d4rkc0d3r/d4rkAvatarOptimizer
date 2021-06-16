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
    public class ShaderAnalyzer
    {
        public Shader shader;
        public readonly List<string> rawLines = new List<string>();
        public readonly List<string> processedLines = new List<string>();
        public readonly List<string> properties = new List<string>();

        public ShaderAnalyzer()
        {

        }

        public ShaderAnalyzer(Shader shader)
        {
            this.shader = shader;
        }

        public void Parse()
        {
            rawLines.Clear();
            processedLines.Clear();
            properties.Clear();
            ReadRawLines();
            ProcessRawLines();
            ParsePropertyBlock();
        }

        private void ReadRawLines()
        {
            string filePath = AssetDatabase.GetAssetPath(shader);
            string fileContents = null;
            try
            {
                StreamReader sr = new StreamReader(filePath);
                fileContents = sr.ReadToEnd();
                sr.Close();
            }
            catch (FileNotFoundException e)
            {
                Debug.LogError("Shader file " + filePath + " not found.  " + e.ToString());
                return;
            }
            catch (IOException e)
            {
                Debug.LogError("Error reading shader file.  " + e.ToString());
                return;
            }
            rawLines.AddRange(Regex.Split(fileContents, "\r\n|\r|\n"));
        }

        private void ProcessRawLines()
        {
            for (int lineIndex = 0; lineIndex < rawLines.Count; lineIndex++)
            {
                string trimmedLine = rawLines[lineIndex].Trim();
                if (trimmedLine == "")
                    continue;
                for (int i = 0; i < trimmedLine.Length - 1; i++)
                {
                    if (trimmedLine[i] != '/')
                        continue;
                    i++;
                    if (trimmedLine[i] == '/')
                    {
                        trimmedLine = trimmedLine.Substring(0, i - 1).TrimEnd();
                        break;
                    }
                }
                if (trimmedLine == "")
                    continue;
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
        }

        private void ParsePropertyBlock()
        {
            bool isInPropertyBlock = false;
            int propertyBlockCurlyBracketDepth = -1;
            int curlyBracketDepth = 0;
            for (int i = 0; i < processedLines.Count; i++)
            {
                string line = processedLines[i];
                if (line == "{")
                {
                    curlyBracketDepth++;
                }
                else if (line == "}")
                {
                    curlyBracketDepth--;
                    if (isInPropertyBlock && curlyBracketDepth == propertyBlockCurlyBracketDepth)
                    {
                        isInPropertyBlock = false;
                        return;
                    }
                }
                else if (line == "Properties" && processedLines[i + 1] == "{")
                {
                    isInPropertyBlock = true;
                    propertyBlockCurlyBracketDepth = curlyBracketDepth;
                    curlyBracketDepth++;
                    i++;
                }
                else if (isInPropertyBlock)
                {
                    string modifiedLine = line;
                    int squareBracketOpenIndex = line.IndexOf('[');
                    while (squareBracketOpenIndex != -1)
                    {
                        int closeBracketIndex = modifiedLine.IndexOf(']') + 1;
                        if (closeBracketIndex != 0)
                        {
                            modifiedLine = modifiedLine.Substring(0, squareBracketOpenIndex)
                                + modifiedLine.Substring(closeBracketIndex, modifiedLine.Length - closeBracketIndex);
                            squareBracketOpenIndex = modifiedLine.IndexOf('[');
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
                        properties.Add(modifiedLine);
                    }
                }
            }
        }
    }
}
#endif