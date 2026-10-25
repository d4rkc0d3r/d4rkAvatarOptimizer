#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;
using System.Text;
using System.Linq;
using d4rkpl4y3r.AvatarOptimizer.Extensions;
using System.Security.Cryptography;

namespace d4rkpl4y3r.AvatarOptimizer
{
    public class ShaderOptimizer
    {
        public class OptimizedShader
        {
            public string name;
            public List<(string name, List<string> lines)> files;
            public List<string> floatProperties = new();
            public List<string> colorProperties = new();
            public List<string> integerProperties = new();
            public List<string> tex2DProperties = new();
            public List<string> tex3DCubeProperties = new();
            public List<string> unknownTypeProperties = new();
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
                        if (type.ToLowerInvariant().StartsWithSimple("range"))
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
        private List<(string name, List<string> lines)> outputIncludes = new();
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
        private Dictionary<string, bool[]> animatedPropertyOnMeshID;
        private Dictionary<string, string> texturesToNullCheck;
        private HashSet<string> texturesToMerge;
        private HashSet<string> texturesToReplaceCalls;
        private Dictionary<string, bool> poiUsedPropertyDefines;
        private string vertexInUv0Member;
        private string vertexInUv0EndSwizzle = "";
        private HashSet<string> texturesToCallSoTheSamplerDoesntDisappear;
        private List<string> setKeywords;
        private int curlyBraceDepth = 0;
        private string sanitizedShaderName;
        private bool stripShadowVariants = false;
        private bool inlineReplaceConstants = false;
        private Dictionary<string, string> constantPropertyValues = new();
        private Dictionary<string, string> variableTypesThisPass = new();
        private OptimizedShader optimizedShader = new();

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
            bool stripShadowVariants = false,
            Dictionary<string, bool[]> animatedPropertyOnMeshID = null
            )
        {
            if (source == null || !source.parsedCorrectly)
                return null;
            using var invariantCulture = new InvariantCultureScope();
            mergedMeshIndices ??= new();
            if (mergedMeshIndices.Count == 0)
                mergedMeshIndices.Add(0);
            mergedMeshIndices = mergedMeshIndices.Distinct().OrderBy(i => i).ToList();
            mergedMeshNames ??= Enumerable.Range(0, mergedMeshCount).Select(i => "").ToList();
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
                sanitizedShaderName = $"s_{Path.GetFileNameWithoutExtension(source.filePath)}".Replace('\'', '_').Replace(' ', '_'),
                stripShadowVariants = stripShadowVariants,
                animatedPropertyOnMeshID = animatedPropertyOnMeshID ?? new Dictionary<string, bool[]>()
            };
            optimizer.texturesToReplaceCalls = new HashSet<string>(
                optimizer.texturesToMerge.Union(optimizer.texturesToNullCheck.Keys));
            optimizer.optimizedShader.originalShader = source;
            optimizer.inlineReplaceConstants = source.ifexParameters.Count > 0 || source.requiredConstantProperties.Count > 0;
            foreach (var staticValues in optimizer.staticPropertyValues)
            {
                if (optimizer.animatedPropertyValues.ContainsKey(staticValues.Key))
                    continue;
                if (optimizer.arrayPropertyValues.ContainsKey(staticValues.Key))
                    continue;
                if (source.noConstantFoldingProperties.Contains(staticValues.Key))
                    continue;
                if (source.propertyTable.TryGetValue(staticValues.Key, out var prop) && prop.doNotLock)
                    continue;
                optimizer.constantPropertyValues[staticValues.Key] = staticValues.Value;
            }
            try
            {
                optimizer.Run();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error optimizing shader {source.name}: {e.Message}\n{e.StackTrace}");
                throw;
            }
            return optimizer.optimizedShader;
        }

        private string ReplaceConstants(string line)
        {
            if (!inlineReplaceConstants || line.Length == 0 || line[0] == '#')
            {
                return line;
            }
            var sb = new StringBuilder();
            bool inIdentifier = ShaderAnalyzer.IsIdentifierLetter(line[0]);
            bool didReplaceSomething = false;
            bool isIfStatementWithBranchAttribute = line.StartsWithSimple("UNITY_BRANCH");
            int currentChunkStart = 0;
            int identifierId = 0;
            for (int i = 1; i <= line.Length; i++)
            {
                if (i == line.Length || ShaderAnalyzer.IsIdentifierLetter(line[i]) != inIdentifier)
                {
                    var chunk = line[currentChunkStart..i];
                    if (inIdentifier)
                    {
                        bool isNumber = char.IsDigit(chunk[0]);
                        if (!isNumber && isIfStatementWithBranchAttribute && identifierId == 1 && chunk != "if")
                        {
                            isIfStatementWithBranchAttribute = false;
                        }
                        if (constantPropertyValues.TryGetValue(chunk, out var replacement))
                        {
                            if (variableTypesThisPass.TryGetValue(chunk, out var type))
                            {
                                replacement = $"({type}){replacement}";
                            }
                            sb.Append($"({replacement})");
                            didReplaceSomething = true;
                        }
                        else
                        {
                            sb.Append(chunk);
                            if (identifierId > 1 && !isNumber)
                            {
                                isIfStatementWithBranchAttribute = false;
                            }
                        }
                        if (!isNumber)
                        {
                            identifierId++;
                        }
                    }
                    else
                    {
                        sb.Append(chunk);
                    }
                    currentChunkStart = i;
                    inIdentifier = !inIdentifier;
                }
            }
            var result = didReplaceSomething ? sb.ToString() : line;
            return isIfStatementWithBranchAttribute ? result["UNITY_BRANCH".Length..].TrimStart() + " // removed UNITY_BRANCH" : result;
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
                string isAnimatedCheck = localMeshCount > 32
                    ? $"d4rkAvatarOptimizer{name}_IsAnimated[d4rkAvatarOptimizer_MeshID]"
                    : $"((1u << d4rkAvatarOptimizer_MeshID) & d4rkAvatarOptimizer{name}_IsAnimatedMask) != 0";
                bool foundAnimatedPropertyOnMeshID = animatedPropertyOnMeshID.TryGetValue(name, out var onMeshID);
                bool skipAnimatedCheck = foundAnimatedPropertyOnMeshID && mergedMeshIndices.All(i => onMeshID[i]);
                if (localMeshCount > 1)
                {
                    output.Add($"#if !defined(SHADER_API_D3D11)");
                    output.Add("{");
                    foreach (int i in mergedMeshIndices)
                    {
                        string currentIndex = $"d4rkAvatarOptimizer{name}_ArrayIndex{i}";
                        if (!foundAnimatedPropertyOnMeshID)
                        {
                            currentIndex = $"(isnan(asfloat(asuint({currentIndex}.x) ^ asuint(d4rkAvatarOptimizer_Zero))) ? {name} : {currentIndex})";
                        }
                        output.Add($"{name} = {i - mergedMeshIndices.First()} == d4rkAvatarOptimizer_MeshID ? "
                            + (skipAnimatedCheck || !foundAnimatedPropertyOnMeshID ? $"{currentIndex}" : $"({isAnimatedCheck} ? {currentIndex} : {name})")
                            + $" : {name};");
                    }
                    output.Add("}");
                    output.Add($"#else");
                }
                string animatedValue = localMeshCount > 1
                    ? $"{CBufferAliasArray[name].name}[{CBufferAliasArray[name].offset} + d4rkAvatarOptimizer_MeshID]"
                    : $"d4rkAvatarOptimizer{name}_ArrayIndex{mergedMeshIndices.First()}";
                if (foundAnimatedPropertyOnMeshID)
                {
                    output.Add($"{name} = {(skipAnimatedCheck ? animatedValue : $"{isAnimatedCheck} ? {animatedValue} : {name}")};");
                }
                else
                {
                    output.Add($"{name} = isnan(asfloat(asuint({animatedValue}.x) ^ asuint(d4rkAvatarOptimizer_Zero))) ? {name} : {animatedValue};");
                }
                if (localMeshCount > 1)
                {
                    output.Add($"#endif");
                }
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
                if (line.StartsWithSimple("#"))
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
                if (line.StartsWithSimple("#"))
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
                if (line.StartsWithSimple("#"))
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
                if (line.StartsWithSimple("#"))
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

        private bool NeedVertexInputOutputWrappers()
        {
            return
                arrayPropertyValues.Count > 0
                || (currentPass.geometry != null && mergedMeshCount > 1)
                || animatedPropertyValues.Count > 0;
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
            bool isEmptyPass = isVoidReturn && func.parameters.Count == 1;
            List<string> funcParams = null;
            List<string> originalVertexShader = null;
            bool needToPassOnMeshOrMaterialID = NeedVertexInputOutputWrappers() && !isEmptyPass;
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
                if (mergedMeshCount > 1 && !isEmptyPass)
                    output.Add($"d4rkAvatarOptimizer_MeshID = ((uint){inParam.name}.{vertexInUv0Member}.z >> 12) - {mergedMeshIndices.First()};");
                if (arrayPropertyValues.Count > 0 && !isEmptyPass)
                    output.Add($"d4rkAvatarOptimizer_MaterialID = 0xFFF & (uint){inParam.name}.{vertexInUv0Member}.z;");
            }
            if (!isEmptyPass)
            {
                InitializeOutputParameter(funcParams, output, !needToPassOnMeshOrMaterialID);
                InjectDummyCBufferUsage(nullReturn);
                InjectIsActiveMeshCheck(nullReturn);
                InjectArrayPropertyInitialization();
                InjectAnimatedPropertyInitialization();
            }
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
                line = ReplaceConstants(line);
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
                else if (needToPassOnMeshOrMaterialID && ShaderAnalyzer.LineStartsWithReturnStatement(line))
                {
                    output.Add("{");
                    if (!isVoidReturn)
                        output.Add($"returnWrappedStruct = {line["return".Length..].TrimStart()}");
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
            var inputWrapperName = $"geometryInputWrapper_l{dummyLineIndex}";
            var outputWrapperName = $"geometryOutputWrapper_l{dummyLineIndex}";
            if (usesInputWrapper)
            {
                wrapperStructs.Add("struct " + inputWrapperName);
                wrapperStructs.Add("{");
                wrapperStructs.Add("uint d4rkAvatarOptimizer_MeshMaterialID : d4rkAvatarOptimizer_MeshMaterialID;");
                wrapperStructs.Add(inParam.type + " d4rkAvatarOptimizer_geometryInput;");
                wrapperStructs.Add("};");
            }
            if (usesOutputWrapper)
            {
                wrapperStructs.Add("struct " + outputWrapperName);
                wrapperStructs.Add("{");
                wrapperStructs.Add("uint d4rkAvatarOptimizer_MeshMaterialID : d4rkAvatarOptimizer_MeshMaterialID;");
                wrapperStructs.Add(outParamType + " d4rkAvatarOptimizer_geometryOutput;");
                wrapperStructs.Add("};");
            }
            int insertIndex = output.FindLastIndex(s => !s.StartsWithSimple("#") && !s.StartsWithSimple("[")) + 1;
            output.InsertRange(insertIndex, wrapperStructs);
            string line = source[sourceLineIndex];
            while (line != "{" && sourceLineIndex < source.Count - 1)
            {
                line = source[++sourceLineIndex];
            }
            curlyBraceDepth++;
            string geometryType = inParam.arraySize == 1 ? "point" : inParam.arraySize == 2 ? "line" : "triangle";
            output.Add($"void {func.name}({geometryType} "
                + (usesInputWrapper ? inputWrapperName + " d4rkAvatarOptimizer_inputWrapper" : inParam.type + " " + inParam.name)
                + $"[{inParam.arraySize}]"
                + (inParam.semantic == null ? "" : $" : {inParam.semantic}")
                + ", inout "
                + outParam.type.Substring(0, 7 + outParam.type.IndexOf('S'))
                + (usesOutputWrapper ? outputWrapperName : outParamType)
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
                line = ReplaceConstants(line);
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
                    output.Add(outputWrapperName + " d4rkAvatarOptimizer_geomOutput;");
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
            if (ShaderAnalyzer.LineStartsWithReturnStatement(output[^1]))
            {
                returnStatement = output[^1];
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
            } catch (ShaderAnalyzer.ParserException e) {
                Debug.LogWarning($"Failed to parse function parameters for function {func.name}. Skipping duplication.\n{e.Message}");
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
                line = ReplaceConstants(line);
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

            output.Add("#if defined(SHADER_API_D3D11)");
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
            output.Add("#endif");
        }

        private void InjectIsActiveMeshCheck(string nullReturn)
        {
            if (mergedMeshCount <= 1)
                return;
            if (localMeshCount > 1)
            {
                output.Add("#if defined(SHADER_API_D3D11)");
                output.Add($"if (0.5 > d4rkAvatarOptimizerAnimatedScalars[d4rkAvatarOptimizer_MeshID]) {nullReturn}");
                output.Add("#else");
                output.Add("{");
                output.Add($"bool isActive = true;");
                foreach (int i in mergedMeshIndices)
                {
                    output.Add($"isActive = {i - mergedMeshIndices.First()} == d4rkAvatarOptimizer_MeshID ? _IsActiveMesh{i} > 0.5 : isActive;");
                }
                output.Add($"if (!isActive) {nullReturn}");
                output.Add("}");
                output.Add("#endif");
            }
            else
                output.Add($"if (0.5 > _IsActiveMesh{mergedMeshIndices.First()}) {nullReturn}");
        }

        private bool hasVectorCBufferAliasArray = false;
        private Dictionary<string, (string name, int offset)> CBufferAliasArray = new();

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

        private static readonly List<string> SkippedShaderVariants = new() {
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
            target.Add($"#pragma warning (disable : 3554) // The attribute is unknown or invalid for the specified statement.");
        }

        private void InjectOptimizerDefines()
        {
            if (!inlineReplaceConstants)
                return;
            var currentKnownDefines = knownDefines.Peek();
            output.Add("#define OPTIMIZER_ENABLED 1");
            currentKnownDefines["OPTIMIZER_ENABLED"] = (true, 1);
            foreach (var prop in poiUsedPropertyDefines)
            {
                if (!prop.Value)
                {
                    currentKnownDefines[prop.Key] = (false, null);
                    continue;
                }
                output.Add($"#define {prop.Key} 1");
                currentKnownDefines[prop.Key] = (true, 1);
            }
        }

        private void InjectPropertyArrays()
        {
            pragmaOutput.Add($"#pragma skip_variants {string.Join(" ", SkippedShaderVariants)}");
            InjectWarningDisables(pragmaOutput);
            InjectWarningDisables(output);
            output.Add("//UNITY_SHADER_NO_UPGRADE");
            hasVectorCBufferAliasArray = false;
            CBufferAliasArray.Clear();
            if (mergedMeshCount > 1)
            {
                var usedScalarRegisters = new HashSet<int>();
                var usedVectorRegisters = new HashSet<int>();
                var scalarOutput = new List<string>();
                var glOutput = new List<string>();
                var vectorOutput = new List<string>();
                foreach (int i in mergedMeshIndices)
                {
                    scalarOutput.Add($"float _IsActiveMesh{i} : packoffset(c{i - mergedMeshIndices.First()});");
                    glOutput.Add($"float _IsActiveMesh{i};");
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
                        glOutput.Add($"{type} d4rkAvatarOptimizer{name}_ArrayIndex{i};");
                    }
                }
                output.Add("#if defined(SHADER_API_D3D11)");
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
                output.Add("#else");
                output.AddRange(glOutput);
                output.Add("#endif");
                foreach (var animatedPropertyMask in animatedPropertyOnMeshID)
                {
                    string name = animatedPropertyMask.Key;
                    bool[] data = animatedPropertyMask.Value;
                    if (localMeshCount > 32)
                    {
                        output.Add($"static const bool d4rkAvatarOptimizer{name}_IsAnimated[{localMeshCount}] = ");
                        output.Add("{");
                        for (int i = 0; i < localMeshCount; i++)
                        {
                            output.Add(data[i + mergedMeshIndices.First()] ? "true," : "false,");
                        }
                        output.Add("};");
                    }
                    else
                    {
                        uint bitField = 0;
                        for (int i = 0; i < localMeshCount; i++)
                        {
                            if (data[i + mergedMeshIndices.First()])
                                bitField |= 1u << i;
                        }
                        output.Add($"static const uint d4rkAvatarOptimizer{name}_IsAnimatedMask = {bitField};");
                    }
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
            foreach (var keyword in setKeywords.Where(k => currentPass.shaderFeatureKeyWords.Contains(k)))
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
                    output.Add($"static const {type} d4rkAvatarOptimizerArray{arrayProperty.Key}[{values.Count}] = ");
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
                if (name.StartsWithSimple("arrayIndex") && texturesToMerge.Contains(name.Substring(10)))
                {
                    output.Add($"static float {name} = {property.Value};");
                }
            }
            int textureWrapperCount = 0;
            foreach (var texName in texturesToReplaceCalls)
            {
                string nullCheck = null;
                if (texturesToNullCheck.TryGetValue(texName, out string textureDefaultValue))
                {
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

                output.Add($"class {texName}_Wrapper");
                output.Add("{");
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
        private Stack<Dictionary<string, (bool defined, int? value)>> knownDefines = new();
        private Stack<HashSet<string>> alreadyIncludedFiles = new();
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
                // hardcoded parse of POI_PIPE == POI_XXX guard
                if (index == 0 && expr.StartsWithSimple("POI_PIPE == POI_"))
                {
                    return expr["POI_PIPE == ".Length..].Trim() switch
                    {
                        "POI_BIRP" => ConditionResult.True,
                        "POI_URP" => ConditionResult.False,
                        _ => ConditionResult.Unknown,
                    };
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
                    if (expr[index] == '1' || expr[index] == '0')
                    {
                        bool value = expr[index] == '1';
                        index++;
                        if (index < expr.Length && char.IsDigit(expr[index]))
                        {
                            output.Add($"// Expected 1 or 0 at {index}, got {expr.Substring(index, System.Math.Min(10, expr.Length - index))}");
                            return ConditionResult.Unknown;
                        }
                        values.Add(value ^ isNegated ? ConditionResult.True : ConditionResult.False);
                        continue;
                    }
                    if (!expr.StartsWithSimple("defined", index))
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
            else if (line.StartsWithSimple("else", 1) || line.StartsWithSimple("elif", 1))
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
                    string expr = line[6..].TrimStart();
                    var exprIndex = 0;
                    var evalResult = EvalPreprocessorCondition(expr, ref exprIndex);
                    if (evalResult == ConditionResult.Unknown)
                    {
                        lastIfEvalResultStack.Push(ConditionResult.Unknown);
                        if (lastEval == ConditionResult.False)
                        {
                            PushPreprocessorScope();
                            return $"#{line[3..]}";
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
            else if (line.StartsWithSimple("endif", 1))
            {
                if (lastIfEvalResultStack.Pop() == ConditionResult.Unknown)
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
                if (identifier == null) {
                    return null;
                }
                switch (identifier) {
                    case "vertex":
                        if (NeedVertexInputOutputWrappers()) {
                            output.Add("#pragma vertex d4rkAvatarOptimizer_vertexWithWrapper");
                        } else {
                            output.Add(line);
                        }
                        break;
                    case "skip_optimizations":
                    case "enable_d3d11_debug_symbols":
                        break;
                    case "multi_compile_fwdbase":
                        output.Add("#pragma multi_compile DIRECTIONAL");
                        output.Add("#pragma multi_compile LIGHTPROBE_SH");
                        if (!stripShadowVariants)
                            output.Add("#pragma multi_compile _ SHADOWS_SCREEN");
                        break;
                    case "multi_compile_fwdadd_fullshadows":
                        output.Add(stripShadowVariants ? "#pragma multi_compile_fwdadd" : "#pragma multi_compile_fwdadd_fullshadows");
                        break;
                    default:
                        if (!identifier.StartsWithSimple("shader_feature")) {
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
                    if (line.StartsWithSimple("#include "))
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
                            variableTypesThisPass[name] = type;
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
                                && !arrayPropertyValues.ContainsKey(name)
                                && !parsedShader.noConstantFoldingProperties.Contains(name))
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
                        output.Add(ReplaceConstants(line));
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
            var ifexStatement = IfexStatement.Parse(line);
            if (ifexStatement == null) {
                lineIndex++;
                debugOutput?.Add($"// {line} failed to parse conditions, just skip statement");
                ParseAndEvaluateIfex(lines, ref lineIndex, debugOutput);
                return;
            }
            var evaluation = ifexStatement.Evaluate(new IfexStatement.EvaluationContext(
                staticPropertyValues,
                animatedPropertyValues,
                arrayPropertyValues));
            var outputString = $"// #ifex {evaluation.DebugText}";
            if (evaluation.Result != IfexStatement.EvaluationResult.True)
            {
                lineIndex++;
                debugOutput?.Add(outputString + $", {evaluation.Result.ToString().ToUpperInvariant()}");
                ParseAndEvaluateIfex(lines, ref lineIndex, debugOutput);
                return;
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
        
        static readonly HashSet<string> shaderPropertiesToKeep = new()
        {
            // copied by vrchat shader fallback system
            "_MainTex",
            "_MetallicGlossMap",
            "_SpecGlossMap",
            "_BumpMap",
            "_ParallaxMap",
            "_OcclusionMap",
            "_EmissionMap",
            "_DetailMask",
            "_DetailAlbedoMap",
            "_DetailNormalMap",
            "_Color",
            "_EmissionColor",
            "_SpecColor",
            "_Cutoff",
            "_Glossiness",
            "_GlossMapScale",
            "_SpecularHighlights",
            "_GlossyReflections",
            "_SmoothnessTextureChannel",
            "_Metallic",
            "_SpecularHighlights",
            "_GlossyReflections",
            "_BumpScale",
            "_Parallax",
            "_OcclusionStrength",
            "_DetailNormalMapScale",
            "_UVSec",
            "_Mode",
            "_SrcBlend",
            "_DstBlend",
            "_ZWrite",

            // particle systems with skinned mesh renderer as source might inherit these
            "_TintColor",
        };

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
                if (line.ToLowerInvariant() == "properties")
                {
                    break;
                }
            }
            output[^1] = output[^1] + " //UNITY_SHADER_NO_UPGRADE";
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
                        if (prop.type == ParsedShader.Property.Type.Vector)
                        {
                            defaultValue = "(0,0,0,0)";
                            type = "Vector";
                        }
                        else if (prop.type == ParsedShader.Property.Type.Color || prop.type == ParsedShader.Property.Type.ColorHDR)
                        {
                            defaultValue = "(0,0,0,0)";
                            type = "Color";
                        }
                        if (prop.type == ParsedShader.Property.Type.ColorHDR)
                        {
                            tagString += "[hdr] ";
                        }
                        foreach (int i in mergedMeshIndices)
                        {
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
                        if (displayName.StartsWithSimple("_IsActiveMesh"))
                        {
                            int meshIndex = int.Parse(displayName.Substring("_IsActiveMesh".Length));
                            displayName = $"_IsActiveMesh{meshIndex} {mergedMeshNames[meshIndex]}";
                        }
                        if (!parsedShader.propertyTable.TryGetValue(defaultAnimatedProperty.name, out var nativeProperty))
                        {
                            var match = Regex.Match(defaultAnimatedProperty.name, @"d4rkAvatarOptimizer(\w+)_ArrayIndex(\d+)");
                            if (match.Success)
                            {
                                parsedShader.propertyTable.TryGetValue(match.Groups[1].Value, out nativeProperty);
                                displayName = $"{match.Groups[1].Value} {match.Groups[2].Value}";
                            }
                        }
                        if (nativeProperty != null)
                        {
                            tagString = nativeProperty.hasGammaTag ? "[gamma] " : "";
                            if (nativeProperty.type == ParsedShader.Property.Type.Color || nativeProperty.type == ParsedShader.Property.Type.ColorHDR)
                            {
                                type = "Color";
                            }
                            if (nativeProperty.type == ParsedShader.Property.Type.ColorHDR)
                            {
                                tagString += "[hdr] ";
                            }
                        }
                        propertyBlock.Add($"{tagString}{defaultAnimatedProperty.name}(\"{displayName}\", {type}) = {defaultValue}");
                        optimizedShader.AddProperty(defaultAnimatedProperty.name, type);
                    }
                }
            }
            int passID = -1;
            var lightModeToDefine = new Dictionary<string, string>()
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
                string lowerLine = line.ToLowerInvariant();
                if (lowerLine.StartsWithSimple("customeditor"))
                    continue;
                if (lowerLine.StartsWithSimple("pass//"))
                {
                    passID = int.Parse(line.Substring(6));
                    currentPass = parsedShader.passes[passID];
                    variableTypesThisPass.Clear();
                    alreadyIncludedFiles.Clear();
                    alreadyIncludedFiles.Push(new HashSet<string>());
                    knownDefines.Clear();
                    knownDefines.Push(new Dictionary<string, (bool defined, int? value)>());
                    knownDefines.Peek()["UNITY_COLORSPACE_GAMMA"] = (false, null);
                    knownDefines.Peek()["SHADER_TARGET_SURFACE_ANALYSIS"] = (false, null);
                    if (currentPass.tags.TryGetValue("LightMode", out var lightMode))
                    {
                        foreach (var lightModeDefine in lightModeToDefine)
                        {
                            knownDefines.Peek()[lightModeDefine.Value] = (lightMode == lightModeDefine.Key, null);
                        }
                        if (lightMode == "Meta" && parsedShader.passes.Count > 1)
                        {
                            output.Add($"// {lightMode} pass removed, skipped {currentPass.lineCount} lines");
                            lineIndex = currentPass.startLineIndex + currentPass.lineCount - 1;
                            continue;
                        }
                    }
                }
                static string GetColorMaskString(int colorMask)
                {
                    if (colorMask == 0) return "0";
                    string mask = "";
                    if ((colorMask & 8) != 0) mask += "R";
                    if ((colorMask & 4) != 0) mask += "G";
                    if ((colorMask & 2) != 0) mask += "B";
                    if ((colorMask & 1) != 0) mask += "A";
                    return mask;
                }
                if (lowerLine.StartsWithSimple("colormask"))
                {
                    // Having ColorMask [_ColorMask] will break post processing SAO
                    // The hardcoded mask doesn't break it so we have to replace it with the value of the property
                    var propertyMatch = Regex.Match(line, @"\s*\[\s*(\w+)\s*\]");
                    if (propertyMatch.Success
                        && staticPropertyValues.TryGetValue(propertyMatch.Groups[1].Value, out var colorMaskValue)
                        && float.TryParse(colorMaskValue, out var colorMaskFloat))
                    {
                        line = line[..propertyMatch.Index]
                            + $" {GetColorMaskString(Mathf.Clamp((int)colorMaskFloat, 0, 15))}"
                            + line[(propertyMatch.Index + propertyMatch.Length)..];
                    }
                }
                if (lowerLine.StartsWithSimple("cull"))
                {
                    var propertyMatch = Regex.Match(line, @"\s*\[\s*(\w+)\s*\]");
                    if (propertyMatch.Success
                        && staticPropertyValues.TryGetValue(propertyMatch.Groups[1].Value, out var cullValue)
                        && float.TryParse(cullValue, out var cullFloat))
                    {
                        line = line[..propertyMatch.Index]
                            + $" {(UnityEngine.Rendering.CullMode)(int)cullFloat}"
                            + line[(propertyMatch.Index + propertyMatch.Length)..];
                    }
                }
                output.Add(line);
                if (line == "CGPROGRAM" || line == "HLSLPROGRAM")
                {
                    vertexInUv0Member = "texcoord";
                    texturesToCallSoTheSamplerDoesntDisappear.Clear();
                    pragmaOutput = output;
                    output = new List<string>();
                    InjectOptimizerDefines();
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
                    var includeName = $"{sanitizedShaderName}-{GetMD5Hash(output)[..12]}" + (line == "CGPROGRAM" ? ".cginc" : ".hlsl");
                    outputIncludes.Add((includeName, output));
                    output = pragmaOutput;
                    output.Add($"#include_with_pragmas \"{includeName}\"");
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
                    && !parsedShader.noConstantFoldingProperties.Contains(prop.name)
                    && parsedShader.propertyTable[prop.name].shaderLabParams.Count == 0
                    && !shaderPropertiesToKeep.Contains(prop.name))
                    continue;
                if (texturesToMerge.Contains(prop.name))
                {
                    int index = prop.type.LastIndexOf("2D");
                    prop.type = prop.type.Insert(index + 2, "Array");
                    if (prop.name == "_MainTex")
                        prop.name = "_MainTexButNotQuiteSoThatUnityDoesntCry";
                }
                propertyBlock.Add($"{tagString}{prop.name}(\"{prop.name}\", {prop.type}) = {prop.defaultValue}");
                optimizedShader.AddProperty(prop.name, prop.type);
            }
            output.InsertRange(propertyBlockInsertionIndex, propertyBlock);
            var shaderHash = GetMD5Hash(output);
            // add the first 4 characters of the overall shader hash to the include filenames so that the includes get sorted right below their respective shaders
            outputIncludes = outputIncludes.Select(oi => {
                var newName = oi.name.Insert(oi.name.LastIndexOf('-'), $"_{shaderHash[..4]}");
                output = output.Select(line => line.Replace(oi.name, newName)).ToList();
                return (newName, oi.lines);
            }).ToList();
            optimizedShader.files = new() { ("Shader", output) };
            optimizedShader.files.AddRange(outputIncludes);
            optimizedShader.SetName($"{sanitizedShaderName}_{shaderHash[..4]}_{shaderHash[4..12]}");
            static List<string> IndentContent(List<string> content)
            {
                static bool StartsPreprocessorBlock(string line)
                {
                    return line.StartsWithSimple("#if")
                        || line.StartsWithSimple("#ifdef")
                        || line.StartsWithSimple("#ifndef");
                }
                static bool ContinuesPreprocessorBlock(string line)
                {
                    return line.StartsWithSimple("#elif")
                        || line.StartsWithSimple("#else");
                }
                int indentLevel = 0;
                for (int i = 0; i < content.Count; i++)
                {
                    var line = content[i];
                    if (line.StartsWithSimple("}") || line.StartsWithSimple("#endif") || ContinuesPreprocessorBlock(line))
                        indentLevel--;
                    indentLevel = System.Math.Max(indentLevel, 0);
                    content[i] = new string(' ', indentLevel * 4) + line;
                    if (line.StartsWithSimple("{") || StartsPreprocessorBlock(line) || ContinuesPreprocessorBlock(line))
                        indentLevel++;
                }
                return content;
            }
            optimizedShader.files = optimizedShader.files.Select(f => (f.name, IndentContent(f.lines))).ToList();
        }
    }
}
#endif