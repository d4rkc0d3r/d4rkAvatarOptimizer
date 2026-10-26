#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using d4rkpl4y3r.AvatarOptimizer.Extensions;

namespace d4rkpl4y3r.AvatarOptimizer
{
    public class PartialPreprocessor
    {
        /*
            Pulls out all the #ifex & partial preprocessor eval (including the inlining of #include)
            that is currently intertwined inside of ShaderOptimizer.Run(). Runs once over the
            parsed shader's .shader line list and produces a flat list of lines in which:
            - #ifex blocks are resolved (true -> block removed, false/unknown -> block kept
              in place, with a `// #ifex ...` debug comment left where the #ifex line was)
            - inside CGPROGRAM / HLSLPROGRAM regions only: #includes are inlined
              (deduplicated per pass), #if/#ifdef/#ifndef/#elif/#else/#endif are partially
              evaluated (unknown branches stay verbatim), #define/#undef are tracked, and
              #pragma lines are rewritten. Everything outside those regions passes through
              verbatim.
            ShaderOptimizer consumes the resulting line list and does the actual optimization
            work (constant inlining, pass function wrapper injection, property arrays, ...)
            on top of it. The ParsedShader's text buffers are never modified, so the shared
            parsed shader cache stays untouched.
        */

        public class Options
        {
            public IfexStatement.EvaluationContext ifexContext;
            public bool stripShadowVariants;
            public List<string> setKeywords = new();
            public Dictionary<string, bool> poiUsedPropertyDefines = new();
            public bool inlineReplaceConstants;
            // whether the vertex shader of each pass needs the input/output wrapper, per pass id
            public bool[] needVertexWrappers;
        }

        public class Result
        {
            public List<string> lines = new();
            // flat index of the `}` closing each pass, indexed by pass id (0 if not found)
            public List<int> passEndLines = new();
        }

        public static readonly List<string> SkippedShaderVariants = new()
        {
            "DYNAMICLIGHTMAP_ON",
            "LIGHTMAP_ON",
            "LIGHTMAP_SHADOW_MIXING",
            "DIRLIGHTMAP_COMBINED",
            "SHADOWS_SHADOWMASK"
        };

        private static readonly Dictionary<string, string> LightModeToDefine = new()
        {
            {"ForwardBase", "UNITY_PASS_FORWARDBASE"},
            {"ForwardAdd", "UNITY_PASS_FORWARDADD"},
            {"ShadowCaster", "UNITY_PASS_SHADOWCASTER"},
            {"Meta", "UNITY_PASS_META"},
        };

        private enum IfConditionResult
        {
            True,
            False,
            Unknown
        }

        private readonly ParsedShader parsedShader;
        private readonly Options options;
        private readonly Result result;
        private readonly List<string> output;
        private Stack<IfConditionResult> lastIfEvalResultStack;
        private Stack<Dictionary<string, (bool defined, int? value)>> knownDefines = new();
        private Stack<HashSet<string>> alreadyIncludedFiles = new();
        private int currentPassID = -1;
        private int passCurlyBraceDepth = 0;

        public PartialPreprocessor(ParsedShader parsedShader, Options options)
        {
            this.parsedShader = parsedShader;
            this.options = options;
            this.result = new Result
            {
                passEndLines = new List<int>(parsedShader.passes.Count),
            };
            for (int i = 0; i < parsedShader.passes.Count; i++)
                result.passEndLines.Add(0);
            this.output = result.lines;
        }

        public Result Preprocess()
        {
            lastIfEvalResultStack = new Stack<IfConditionResult>();
            knownDefines.Push(new Dictionary<string, (bool defined, int? value)>());
            alreadyIncludedFiles.Push(new HashSet<string>());
            int topLevelLineIndex = 0;
            WalkTopLevel(parsedShader.text[".shader"], ref topLevelLineIndex);
            return result;
        }

        private void WalkTopLevel(List<string> source, ref int sourceLineIndex)
        {
            while (sourceLineIndex < source.Count)
            {
                if (HandleIfex(source, ref sourceLineIndex))
                    continue;
                var line = source[sourceLineIndex];
                if (line == "CGPROGRAM" || line == "HLSLPROGRAM")
                {
                    output.Add(line);
                    sourceLineIndex++;
                    SeedCodeBlockDefines();
                    WalkCodeBlock(source, ref sourceLineIndex, line == "CGPROGRAM" ? "ENDCG" : "ENDHLSL");
                    continue;
                }
                var lowerLine = line.ToLowerInvariant();
                if (lowerLine.StartsWithSimple("pass//"))
                {
                    ResetForPass(int.Parse(line.Substring(6)));
                    passCurlyBraceDepth = 0;
                }
                else if (currentPassID >= 0)
                {
                    if (line == "{")
                        passCurlyBraceDepth++;
                    else if (line == "}")
                    {
                        passCurlyBraceDepth--;
                        if (passCurlyBraceDepth <= 0)
                        {
                            result.passEndLines[currentPassID] = output.Count;
                            currentPassID = -1;
                            passCurlyBraceDepth = 0;
                        }
                    }
                }
                output.Add(line);
                sourceLineIndex++;
            }
        }

        private void WalkCodeBlock(List<string> source, ref int sourceLineIndex, string endSymbol)
        {
            while (sourceLineIndex < source.Count)
            {
                if (HandleIfex(source, ref sourceLineIndex))
                    continue;
                var line = source[sourceLineIndex];
                if (line == endSymbol)
                {
                    output.Add(line);
                    sourceLineIndex++;
                    return;
                }
                if (line[0] == '#')
                {
                    if (line.StartsWithSimple("#include "))
                    {
                        var includeName = ShaderAnalyzer.ParseIncludeDirective(line);
                        if (parsedShader.text.TryGetValue(includeName, out var includeSource))
                        {
                            if (alreadyIncludedFiles.Peek().Contains(includeName))
                            {
                                output.Add($"// Already included {includeName}");
                                sourceLineIndex++;
                                continue;
                            }
                            int innerLineIndex = 0;
                            output.Add($"// Include {includeName}");
                            alreadyIncludedFiles.Peek().Add(includeName);
                            WalkCodeBlock(includeSource, ref innerLineIndex, endSymbol);
                            sourceLineIndex++;
                            continue;
                        }
                        output.Add(line);
                        sourceLineIndex++;
                        continue;
                    }
                    var processedLine = PartialEvalPreprocessorLine(source, ref sourceLineIndex);
                    if (processedLine != null)
                        output.Add(processedLine);
                    sourceLineIndex++;
                    continue;
                }
                output.Add(line);
                sourceLineIndex++;
            }
        }

        private bool HandleIfex(List<string> source, ref int sourceLineIndex)
        {
            var line = source[sourceLineIndex];
            if (line[0] != '#')
                return false;
            if (line == "#endex")
            {
                sourceLineIndex++;
                return true;
            }
            if (!line.StartsWithSimple("#ifex"))
                return false;
            var ifexStatement = IfexStatement.Parse(line);
            if (ifexStatement == null)
            {
                sourceLineIndex++;
                output.Add($"// {line} failed to parse conditions, just skip statement");
                return true;
            }
            var evaluation = ifexStatement.Evaluate(options.ifexContext);
            var outputString = $"// #ifex {evaluation.DebugText}";
            if (evaluation.Result != IfexStatement.EvaluationResult.True)
            {
                sourceLineIndex++;
                output.Add($"{outputString}, {evaluation.Result.ToString().ToUpperInvariant()}");
                return true;
            }
            int depth = 0;
            int linesSkipped = 0;
            while (++sourceLineIndex < source.Count)
            {
                line = source[sourceLineIndex];
                linesSkipped++;
                if (line[0] != '#')
                    continue;
                if (line == "#endex")
                {
                    if (depth == 0)
                    {
                        sourceLineIndex++;
                        output.Add($"{outputString}, TRUE skipped {linesSkipped} lines");
                        return true;
                    }
                    depth--;
                }
                else if (line.StartsWithSimple("ifex", 1))
                {
                    depth++;
                }
            }
            output.Add($"// didn't find matching #endex, skipped {linesSkipped} lines");
            return true;
        }

        private void ResetForPass(int passID)
        {
            currentPassID = passID;
            knownDefines.Clear();
            knownDefines.Push(new Dictionary<string, (bool defined, int? value)>());
            knownDefines.Peek()["UNITY_COLORSPACE_GAMMA"] = (false, null);
            knownDefines.Peek()["SHADER_TARGET_SURFACE_ANALYSIS"] = (false, null);
            if (parsedShader.passes[passID].tags.TryGetValue("LightMode", out var lightMode))
            {
                foreach (var lightModeDefine in LightModeToDefine)
                {
                    knownDefines.Peek()[lightModeDefine.Value] = (lightMode == lightModeDefine.Key, null);
                }
            }
            alreadyIncludedFiles.Clear();
            alreadyIncludedFiles.Push(new HashSet<string>());
        }

        private void SeedCodeBlockDefines()
        {
            if (currentPassID < 0 || currentPassID >= parsedShader.passes.Count)
                return;
            var currentKnownDefines = knownDefines.Peek();
            if (options.inlineReplaceConstants)
            {
                currentKnownDefines["OPTIMIZER_ENABLED"] = (true, 1);
                foreach (var prop in options.poiUsedPropertyDefines)
                {
                    currentKnownDefines[prop.Key] = (prop.Value, prop.Value ? (int?)1 : null);
                }
            }
            foreach (var keyword in parsedShader.passes[currentPassID].shaderFeatureKeyWords)
            {
                currentKnownDefines[keyword] = (options.setKeywords.Contains(keyword), null);
            }
            foreach (var skippedShaderVariant in SkippedShaderVariants)
            {
                currentKnownDefines[skippedShaderVariant] = (false, null);
            }
        }

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

        private string PartialEvalPreprocessorLine(List<string> source, ref int sourceLineIndex)
        {
            var line = source[sourceLineIndex];
            if (line[0] != '#')
                return line;
            void SkipWhitespace(string s, ref int index) { while (index < s.Length && char.IsWhiteSpace(s[index])) index++; }
            IfConditionResult EvalPreprocessorCondition(string expr, ref int index)
            {
                // hardcoded parse of POI_PIPE == POI_XXX guard
                if (index == 0 && expr.StartsWithSimple("POI_PIPE == POI_"))
                {
                    return expr["POI_PIPE == ".Length..].Trim() switch
                    {
                        "POI_BIRP" => IfConditionResult.True,
                        "POI_URP" => IfConditionResult.False,
                        _ => IfConditionResult.Unknown,
                    };
                }
                // parse flat lists of defined() and !defined() calls that are either all || or all && connected. no nesting.
                var values = new List<IfConditionResult>();
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
                            return IfConditionResult.Unknown;
                        }
                        allOr = true;
                        index += 2;
                    }
                    else if (expr[index] == '&' && expr[index + 1] == '&')
                    {
                        if (allOr)
                        {
                            output.Add($"// Mixed && and || at {index}");
                            return IfConditionResult.Unknown;
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
                        return IfConditionResult.Unknown;
                    }
                    if (expr[index] == '(')
                    {
                        index++;
                        var res = EvalPreprocessorCondition(expr, ref index);
                        if (isNegated && res != IfConditionResult.Unknown)
                            res = res == IfConditionResult.True ? IfConditionResult.False : IfConditionResult.True;
                        values.Add(res);
                        continue;
                    }
                    if (expr[index] == '1' || expr[index] == '0')
                    {
                        bool value = expr[index] == '1';
                        index++;
                        if (index < expr.Length && char.IsDigit(expr[index]))
                        {
                            output.Add($"// Expected 1 or 0 at {index}, got {expr.Substring(index, System.Math.Min(10, expr.Length - index))}");
                            return IfConditionResult.Unknown;
                        }
                        values.Add(value ^ isNegated ? IfConditionResult.True : IfConditionResult.False);
                        continue;
                    }
                    if (!expr.StartsWithSimple("defined", index))
                    {
                        output.Add($"// Expected defined at {index}, got {expr.Substring(index, System.Math.Min(10, expr.Length - index))}");
                        return IfConditionResult.Unknown;
                    }
                    index += 7;
                    SkipWhitespace(expr, ref index);
                    if (expr[index] != '(')
                    {
                        output.Add($"// Expected ( at {index}, got {expr.Substring(index, System.Math.Min(10, expr.Length - index))}");
                        return IfConditionResult.Unknown;
                    }
                    index++;
                    SkipWhitespace(expr, ref index);
                    string constantName = ShaderAnalyzer.ParseIdentifierAndTrailingWhitespace(expr, ref index);
                    if (constantName == null)
                    {
                        output.Add($"// Expected identifier at {index}, got {expr.Substring(index, System.Math.Min(10, expr.Length - index))}");
                        return IfConditionResult.Unknown;
                    }
                    if (expr[index] != ')')
                    {
                        output.Add($"// Expected ) at {index}, got {expr.Substring(index, System.Math.Min(10, expr.Length - index))}");
                        return IfConditionResult.Unknown;
                    }
                    index++;
                    if (knownDefines.Peek().TryGetValue(constantName, out var constant))
                    {
                        values.Add(constant.defined ^ isNegated ? IfConditionResult.True : IfConditionResult.False);
                    }
                    else
                    {
                        values.Add(IfConditionResult.Unknown);
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
                    if (values.Contains(IfConditionResult.False))
                        return IfConditionResult.False;
                    if (values.Contains(IfConditionResult.Unknown))
                        return IfConditionResult.Unknown;
                    return IfConditionResult.True;
                }
                if (allOr)
                {
                    if (values.Contains(IfConditionResult.True))
                        return IfConditionResult.True;
                    if (values.Contains(IfConditionResult.Unknown))
                        return IfConditionResult.Unknown;
                    return IfConditionResult.False;
                }
                return IfConditionResult.Unknown;
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
                    else if (innerLine.StartsWithSimple("endif", 1))
                    {
                        if (depth == 0)
                        {
                            return --lineIndex - startLineIndex;
                        }
                        depth--;
                    }
                    else if (depth == 0 && (innerLine.StartsWithSimple("else", 1) || innerLine.StartsWithSimple("elif", 1)))
                    {
                        return --lineIndex - startLineIndex;
                    }
                }
                return lineIndex - startLineIndex;
            }
            if (line.StartsWithSimple("if", 1))
            {
                string expr = "";
                if (line.StartsWithSimple("def", 3))
                    expr = $"defined({line[6..].TrimStart()})";
                else if (line.StartsWithSimple("ndef", 3))
                    expr = $"!defined({line[7..].TrimStart()})";
                else
                    expr = line[4..].TrimStart();
                var exprIndex = 0;
                var evalResult = EvalPreprocessorCondition(expr, ref exprIndex);
                lastIfEvalResultStack.Push(evalResult);
                if (evalResult == IfConditionResult.True)
                {
                    return null;
                }
                if (evalResult == IfConditionResult.False)
                {
                    int skipped = SkipUntilElseOrEndif(ref sourceLineIndex);
                    return $"// Skipped {skipped} lines | {line}";
                }
                PushPreprocessorScope();
                return line;
            }
            else if (line.StartsWithSimple("else", 1) || line.StartsWithSimple("elif", 1))
            {
                var lastEval = lastIfEvalResultStack.Pop();
                if (lastEval == IfConditionResult.True)
                {
                    int skipped = SkipUntilElseOrEndif(ref sourceLineIndex);
                    lastIfEvalResultStack.Push(IfConditionResult.True);
                    return $"// Skipped {skipped} lines";
                }
                if (lastEval == IfConditionResult.Unknown)
                {
                    PopPreprocessorScope();
                    PushPreprocessorScope();
                }
                if (line[3] == 'i')
                {
                    string expr = line[6..].TrimStart();
                    var exprIndex = 0;
                    var evalResult = EvalPreprocessorCondition(expr, ref exprIndex);
                    if (evalResult == IfConditionResult.Unknown)
                    {
                        lastIfEvalResultStack.Push(IfConditionResult.Unknown);
                        if (lastEval == IfConditionResult.False)
                        {
                            PushPreprocessorScope();
                            return $"#{line[3..]}";
                        }
                        return line;
                    }
                    if (evalResult == IfConditionResult.True)
                    {
                        if (lastEval == IfConditionResult.Unknown)
                        {
                            lastIfEvalResultStack.Push(IfConditionResult.Unknown);
                            return line;
                        }
                        lastIfEvalResultStack.Push(IfConditionResult.True);
                        return null;
                    }
                    int skipped = SkipUntilElseOrEndif(ref sourceLineIndex);
                    if (lastEval == IfConditionResult.Unknown)
                    {
                        lastIfEvalResultStack.Push(IfConditionResult.Unknown);
                        return line;
                    }
                    lastIfEvalResultStack.Push(IfConditionResult.False);
                    return $"// Skipped {skipped} lines | {line}";
                }
                else
                {
                    if (lastEval == IfConditionResult.Unknown)
                    {
                        lastIfEvalResultStack.Push(IfConditionResult.Unknown);
                        return line;
                    }
                    lastIfEvalResultStack.Push(lastEval == IfConditionResult.False ? IfConditionResult.True : IfConditionResult.False);
                    return null;
                }
            }
            else if (line.StartsWithSimple("endif", 1))
            {
                if (lastIfEvalResultStack.Pop() == IfConditionResult.Unknown)
                {
                    PopPreprocessorScope();
                    return line;
                }
                return null;
            }
            else if (line.StartsWithSimple("define", 1))
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
            else if (line.StartsWithSimple("undef", 1))
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
            else if (line.StartsWithSimple("pragma", 1))
            {
                var index = 7;
                SkipWhitespace(line, ref index);
                var identifier = ShaderAnalyzer.ParseIdentifierAndTrailingWhitespace(line, ref index);
                if (identifier == null)
                {
                    return null;
                }
                switch (identifier)
                {
                    case "vertex":
                        if (options.needVertexWrappers != null && currentPassID >= 0 && options.needVertexWrappers[currentPassID])
                            output.Add("#pragma vertex d4rkAvatarOptimizer_vertexWithWrapper");
                        else
                            output.Add(line);
                        break;
                    case "skip_optimizations":
                    case "enable_d3d11_debug_symbols":
                        break;
                    case "multi_compile_fwdbase":
                        output.Add("#pragma multi_compile DIRECTIONAL");
                        output.Add("#pragma multi_compile LIGHTPROBE_SH");
                        if (!options.stripShadowVariants)
                            output.Add("#pragma multi_compile _ SHADOWS_SCREEN");
                        break;
                    case "multi_compile_fwdadd_fullshadows":
                        output.Add(options.stripShadowVariants ? "#pragma multi_compile_fwdadd" : "#pragma multi_compile_fwdadd_fullshadows");
                        break;
                    default:
                        if (!identifier.StartsWithSimple("shader_feature"))
                        {
                            output.Add(line);
                        }
                        break;
                }
                return null;
            }
            else
            {
                return line;
            }
        }
    }
}
#endif