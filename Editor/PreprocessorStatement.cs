#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;

namespace d4rkpl4y3r.AvatarOptimizer
{
    /*
        Parses and evaluates preprocessor condition expressions (what comes after #if / #elif; #ifdef /
        #ifndef are synthesized into defined() / !defined() expressions by the caller). Supports: int
        literals, identifiers (their value is the int tracked in the defines map, truthy if non 0,
        value-less defines count as 1), defined(NAME), comparison operators (== != < > <= >=), unary !
        and binary && / || with C precedence and free () nesting. Anything the evaluator can't decide
        (e.g. an untracked macro) resolves to Unknown so the caller can keep the branch verbatim.
        Mirrors IfexStatement's abstract AST + Parse/Evaluate structure, but the value here is an int
        and evaluation is done against a define name -> (isDefined, value) map.
    */
    public abstract class PreprocessorStatement
    {
        public enum IfConditionResult
        {
            False,
            True,
            Unknown
        }

        public readonly struct Evaluation
        {
            public IfConditionResult Result { get; }
            public string DebugText { get; }

            public Evaluation(IfConditionResult result, string debugText)
            {
                Result = result;
                DebugText = debugText;
            }
        }

        private enum BoolOperator
        {
            And,
            Or
        }

        private enum ComparisonOperator
        {
            Equal,
            NotEqual,
            LessThan,
            GreaterThan,
            LessThanOrEqual,
            GreaterThanOrEqual
        }

        private sealed class BoolBinaryNode : PreprocessorStatement
        {
            private readonly BoolOperator op;
            private readonly PreprocessorStatement left;
            private readonly PreprocessorStatement right;

            public BoolBinaryNode(BoolOperator op, PreprocessorStatement left, PreprocessorStatement right)
            {
                this.op = op;
                this.left = left;
                this.right = right;
            }

            protected override bool TryGetValue(Dictionary<string, (bool isDefined, int? value)> defines, out int value)
            {
                bool isAnd = op == BoolOperator.And;
                if (left.TryGetValue(defines, out var leftValue))
                {
                    if (isAnd)
                    {
                        if (leftValue == 0)
                        {
                            value = 0;
                            return true;
                        }
                    }
                    else if (leftValue != 0)
                    {
                        value = 1;
                        return true;
                    }
                    return right.TryGetValue(defines, out value);
                }
                if (right.TryGetValue(defines, out var rightValue))
                {
                    if (isAnd && rightValue == 0)
                    {
                        value = 0;
                        return true;
                    }
                    if (!isAnd && rightValue != 0)
                    {
                        value = 1;
                        return true;
                    }
                }
                value = 0;
                return false;
            }

            public override string GetDebugText(Dictionary<string, (bool isDefined, int? value)> defines)
            {
                string opText = op == BoolOperator.And ? "&&" : "||";
                return $"({left.GetDebugText(defines)} {opText} {right.GetDebugText(defines)})";
            }
        }

        private sealed class NotNode : PreprocessorStatement
        {
            private readonly PreprocessorStatement operand;

            public NotNode(PreprocessorStatement operand)
            {
                this.operand = operand;
            }

            protected override bool TryGetValue(Dictionary<string, (bool isDefined, int? value)> defines, out int value)
            {
                if (!operand.TryGetValue(defines, out var operandValue))
                {
                    value = 0;
                    return false;
                }
                value = operandValue == 0 ? 1 : 0;
                return true;
            }

            public override string GetDebugText(Dictionary<string, (bool isDefined, int? value)> defines)
            {
                return $"!({operand.GetDebugText(defines)})";
            }
        }

        private sealed class ComparisonNode : PreprocessorStatement
        {
            private readonly ComparisonOperator op;
            private readonly PreprocessorStatement left;
            private readonly PreprocessorStatement right;

            public ComparisonNode(ComparisonOperator op, PreprocessorStatement left, PreprocessorStatement right)
            {
                this.op = op;
                this.left = left;
                this.right = right;
            }

            private static string GetOpText(ComparisonOperator op)
            {
                return op switch
                {
                    ComparisonOperator.Equal => "==",
                    ComparisonOperator.NotEqual => "!=",
                    ComparisonOperator.LessThan => "<",
                    ComparisonOperator.GreaterThan => ">",
                    ComparisonOperator.LessThanOrEqual => "<=",
                    _ => ">=",
                };
            }

            protected override bool TryGetValue(Dictionary<string, (bool isDefined, int? value)> defines, out int value)
            {
                if (!left.TryGetValue(defines, out var leftValue) || !right.TryGetValue(defines, out var rightValue))
                {
                    value = 0;
                    return false;
                }
                bool met = op switch
                {
                    ComparisonOperator.Equal => leftValue == rightValue,
                    ComparisonOperator.NotEqual => leftValue != rightValue,
                    ComparisonOperator.LessThan => leftValue < rightValue,
                    ComparisonOperator.GreaterThan => leftValue > rightValue,
                    ComparisonOperator.LessThanOrEqual => leftValue <= rightValue,
                    _ => leftValue >= rightValue,
                };
                value = met ? 1 : 0;
                return true;
            }

            public override string GetDebugText(Dictionary<string, (bool isDefined, int? value)> defines)
            {
                return $"({left.GetDebugText(defines)} {GetOpText(op)} {right.GetDebugText(defines)})";
            }
        }

        private sealed class DefinedNode : PreprocessorStatement
        {
            private readonly string name;

            public DefinedNode(string name)
            {
                this.name = name;
            }

            protected override bool TryGetValue(Dictionary<string, (bool isDefined, int? value)> defines, out int value)
            {
                if (!defines.TryGetValue(name, out var define))
                {
                    value = 0;
                    return false;
                }
                value = define.isDefined ? 1 : 0;
                return true;
            }

            public override string GetDebugText(Dictionary<string, (bool isDefined, int? value)> defines)
            {
                return defines.ContainsKey(name) ? $"defined({name})" : $"defined({name}?)";
            }
        }

        private sealed class IdentifierNode : PreprocessorStatement
        {
            private readonly string name;

            public IdentifierNode(string name)
            {
                this.name = name;
            }

            protected override bool TryGetValue(Dictionary<string, (bool isDefined, int? value)> defines, out int value)
            {
                if (!defines.TryGetValue(name, out var define))
                {
                    value = 0;
                    return false;
                }
                value = define.isDefined ? define.value ?? 1 : 0;
                return true;
            }

            public override string GetDebugText(Dictionary<string, (bool isDefined, int? value)> defines)
            {
                if (!defines.TryGetValue(name, out var define))
                    return $"{name}?";
                if (!define.isDefined)
                    return $"{name}(0)";
                return $"{name}({define.value ?? 1})";
            }
        }

        private sealed class IntLiteralNode : PreprocessorStatement
        {
            private readonly int value;

            public IntLiteralNode(int value)
            {
                this.value = value;
            }

            protected override bool TryGetValue(Dictionary<string, (bool isDefined, int? value)> defines, out int value)
            {
                value = this.value;
                return true;
            }

            public override string GetDebugText(Dictionary<string, (bool isDefined, int? value)> defines)
            {
                return this.value.ToString(CultureInfo.InvariantCulture);
            }
        }

        public Evaluation Evaluate(Dictionary<string, (bool isDefined, int? value)> defines)
        {
            if (TryGetValue(defines, out var resultValue))
                return new Evaluation(resultValue != 0 ? IfConditionResult.True : IfConditionResult.False, GetDebugText(defines));
            return new Evaluation(IfConditionResult.Unknown, GetDebugText(defines));
        }

        protected abstract bool TryGetValue(Dictionary<string, (bool isDefined, int? value)> defines, out int value);

        public abstract string GetDebugText(Dictionary<string, (bool isDefined, int? value)> defines);

        public static PreprocessorStatement Parse(string expression)
        {
            if (string.IsNullOrEmpty(expression))
                return null;
            var index = 0;
            var statement = ParseOr(expression, ref index);
            if (statement == null)
                return null;
            SkipWhitespace(expression, ref index);
            return index == expression.Length ? statement : null;
        }

        private static PreprocessorStatement ParseOr(string expression, ref int index)
        {
            var statement = ParseAnd(expression, ref index);
            if (statement == null)
                return null;
            while (true)
            {
                SkipWhitespace(expression, ref index);
                if (!TryConsume(expression, ref index, "||"))
                    return statement;
                var right = ParseAnd(expression, ref index);
                if (right == null)
                    return null;
                statement = new BoolBinaryNode(BoolOperator.Or, statement, right);
            }
        }

        private static PreprocessorStatement ParseAnd(string expression, ref int index)
        {
            var statement = ParseComparison(expression, ref index);
            if (statement == null)
                return null;
            while (true)
            {
                SkipWhitespace(expression, ref index);
                if (!TryConsume(expression, ref index, "&&"))
                    return statement;
                var right = ParseComparison(expression, ref index);
                if (right == null)
                    return null;
                statement = new BoolBinaryNode(BoolOperator.And, statement, right);
            }
        }

        private static PreprocessorStatement ParseComparison(string expression, ref int index)
        {
            var left = ParseUnary(expression, ref index);
            if (left == null)
                return null;
            SkipWhitespace(expression, ref index);
            if (index >= expression.Length)
                return left;
            ComparisonOperator? comparisonOp = null;
            int opLength = 0;
            char firstChar = expression[index];
            char secondChar = index + 1 < expression.Length ? expression[index + 1] : '\0';
            if (firstChar == '=' && secondChar == '=')
            {
                comparisonOp = ComparisonOperator.Equal;
                opLength = 2;
            }
            else if (firstChar == '!' && secondChar == '=')
            {
                comparisonOp = ComparisonOperator.NotEqual;
                opLength = 2;
            }
            else if (firstChar == '<' && secondChar == '=')
            {
                comparisonOp = ComparisonOperator.LessThanOrEqual;
                opLength = 2;
            }
            else if (firstChar == '<')
            {
                comparisonOp = ComparisonOperator.LessThan;
                opLength = 1;
            }
            else if (firstChar == '>' && secondChar == '=')
            {
                comparisonOp = ComparisonOperator.GreaterThanOrEqual;
                opLength = 2;
            }
            else if (firstChar == '>')
            {
                comparisonOp = ComparisonOperator.GreaterThan;
                opLength = 1;
            }
            if (comparisonOp == null)
                return left;
            index += opLength;
            var right = ParseUnary(expression, ref index);
            if (right == null)
                return null;
            return new ComparisonNode(comparisonOp.Value, left, right);
        }

        private static PreprocessorStatement ParseUnary(string expression, ref int index)
        {
            SkipWhitespace(expression, ref index);
            if (index < expression.Length && expression[index] == '!')
            {
                index++;
                var operand = ParseUnary(expression, ref index);
                return operand == null ? null : new NotNode(operand);
            }
            return ParsePrimary(expression, ref index);
        }

        private static PreprocessorStatement ParsePrimary(string expression, ref int index)
        {
            SkipWhitespace(expression, ref index);
            if (index >= expression.Length)
                return null;
            if (expression[index] == '(')
            {
                index++;
                var statement = ParseOr(expression, ref index);
                if (statement == null)
                    return null;
                SkipWhitespace(expression, ref index);
                if (index >= expression.Length || expression[index] != ')')
                    return null;
                index++;
                return statement;
            }
            if (expression[index] == '-' || char.IsDigit(expression[index]))
            {
                if (!TryParseInt(expression, index, out var value, out int endIndex))
                    return null;
                index = endIndex;
                return new IntLiteralNode(value);
            }
            if (ShaderAnalyzer.IsIdentifierFirstLetter(expression[index]))
            {
                var name = ShaderAnalyzer.ParseIdentifierAndTrailingWhitespace(expression, ref index);
                if (name == "defined")
                {
                    if (index >= expression.Length || expression[index] != '(')
                        return null;
                    index++;
                    SkipWhitespace(expression, ref index);
                    var definedName = ShaderAnalyzer.ParseIdentifierAndTrailingWhitespace(expression, ref index);
                    if (definedName == null)
                        return null;
                    if (index >= expression.Length || expression[index] != ')')
                        return null;
                    index++;
                    return new DefinedNode(definedName);
                }
                return new IdentifierNode(name);
            }
            return null;
        }

        private static void SkipWhitespace(string expression, ref int index)
        {
            while (index < expression.Length && char.IsWhiteSpace(expression[index]))
                index++;
        }

        private static bool TryConsume(string expression, ref int index, string token)
        {
            if (index + token.Length > expression.Length || string.CompareOrdinal(expression, index, token, 0, token.Length) != 0)
                return false;
            index += token.Length;
            return true;
        }

        public static bool TryParseInt(string str, int startIndex, out int value, out int endIndex)
        {
            int index = startIndex;
            if (index < str.Length && str[index] == '-')
                index++;
            bool sawDigit = false;
            while (index < str.Length && char.IsDigit(str[index]))
            {
                sawDigit = true;
                index++;
            }
            if (!sawDigit)
            {
                value = 0;
                endIndex = startIndex;
                return false;
            }
            endIndex = index;
            return int.TryParse(str[startIndex..index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
#endif