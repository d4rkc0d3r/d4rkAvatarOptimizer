#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using d4rkpl4y3r.AvatarOptimizer.Extensions;

namespace d4rkpl4y3r.AvatarOptimizer
{
    public abstract class IfexStatement
    {
        public enum EvaluationResult
        {
            False,
            True,
            Unknown
        }

        public readonly struct Evaluation
        {
            public EvaluationResult Result { get; }
            public string DebugText { get; }

            public Evaluation(EvaluationResult result, string debugText)
            {
                Result = result;
                DebugText = debugText;
            }
        }

        public sealed class EvaluationContext
        {
            private readonly IReadOnlyDictionary<string, string> staticPropertyValues;
            private readonly IReadOnlyDictionary<string, string> animatedPropertyValues;
            private readonly IReadOnlyDictionary<string, (string type, List<string> values)> arrayPropertyValues;

            public EvaluationContext(
                IReadOnlyDictionary<string, string> staticPropertyValues,
                IReadOnlyDictionary<string, string> animatedPropertyValues,
                IReadOnlyDictionary<string, (string type, List<string> values)> arrayPropertyValues)
            {
                this.staticPropertyValues = staticPropertyValues ?? new Dictionary<string, string>();
                this.animatedPropertyValues = animatedPropertyValues ?? new Dictionary<string, string>();
                this.arrayPropertyValues = arrayPropertyValues ?? new Dictionary<string, (string type, List<string> values)>();
            }

            public bool HasStaticValue(string propertyName)
            {
                return staticPropertyValues.ContainsKey(propertyName);
            }

            public bool IsAnimated(string propertyName)
            {
                return animatedPropertyValues.ContainsKey(propertyName);
            }

            public bool IsArrayProperty(string propertyName)
            {
                return arrayPropertyValues.ContainsKey(propertyName);
            }

            public bool IsConstantProperty(string propertyName)
            {
                return HasStaticValue(propertyName) && !IsAnimated(propertyName) && !IsArrayProperty(propertyName);
            }

            public bool TryGetConstantValue(string propertyName, out float value)
            {
                value = 0f;
                if (!IsConstantProperty(propertyName))
                    return false;
                return float.TryParse(staticPropertyValues[propertyName], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            public string FormatAnimationState(string propertyName)
            {
                return $"({HasStaticValue(propertyName)},{IsAnimated(propertyName)},{IsArrayProperty(propertyName)})";
            }
        }

        private enum BinaryOperator
        {
            And,
            Or
        }

        private sealed class BinaryStatement : IfexStatement
        {
            private readonly BinaryOperator op;
            private readonly IfexStatement left;
            private readonly IfexStatement right;

            public BinaryStatement(BinaryOperator op, IfexStatement left, IfexStatement right)
            {
                this.op = op;
                this.left = left;
                this.right = right;
            }

            public override Evaluation Evaluate(EvaluationContext context)
            {
                var leftEval = left.Evaluate(context);
                var rightEval = right.Evaluate(context);
                var result = op switch
                {
                    BinaryOperator.And when leftEval.Result == EvaluationResult.False || rightEval.Result == EvaluationResult.False => EvaluationResult.False,
                    BinaryOperator.And when leftEval.Result == EvaluationResult.True && rightEval.Result == EvaluationResult.True => EvaluationResult.True,
                    BinaryOperator.And => EvaluationResult.Unknown,
                    BinaryOperator.Or when leftEval.Result == EvaluationResult.True || rightEval.Result == EvaluationResult.True => EvaluationResult.True,
                    BinaryOperator.Or when leftEval.Result == EvaluationResult.False && rightEval.Result == EvaluationResult.False => EvaluationResult.False,
                    _ => EvaluationResult.Unknown,
                };
                string opText = op == BinaryOperator.And ? "&&" : "||";
                return new Evaluation(result, $"({leftEval.DebugText} {opText} {rightEval.DebugText})");
            }

            public override void CollectPropertyNames(ISet<string> propertyNames)
            {
                left.CollectPropertyNames(propertyNames);
                right.CollectPropertyNames(propertyNames);
            }
        }

        private sealed class AnimationStatement : IfexStatement
        {
            private readonly string propertyName;
            private readonly bool isAnimatedCheck;

            public AnimationStatement(string propertyName, bool isAnimatedCheck)
            {
                this.propertyName = propertyName;
                this.isAnimatedCheck = isAnimatedCheck;
            }

            public override Evaluation Evaluate(EvaluationContext context)
            {
                bool isConstantProperty = context.IsConstantProperty(propertyName);
                bool conditionMet = isAnimatedCheck ? !isConstantProperty : isConstantProperty;
                string prefix = isAnimatedCheck ? "isAnimated" : "isNotAnimated";
                return new Evaluation(
                    conditionMet ? EvaluationResult.True : EvaluationResult.False,
                    $"{prefix}({propertyName}){context.FormatAnimationState(propertyName)}");
            }

            public override void CollectPropertyNames(ISet<string> propertyNames)
            {
                propertyNames.Add(propertyName);
            }
        }

        private sealed class ComparisonStatement : IfexStatement
        {
            private readonly string propertyName;
            private readonly bool isNotEquals;
            private readonly float expectedValue;

            public ComparisonStatement(string propertyName, bool isNotEquals, float expectedValue)
            {
                this.propertyName = propertyName;
                this.isNotEquals = isNotEquals;
                this.expectedValue = expectedValue;
            }

            public override Evaluation Evaluate(EvaluationContext context)
            {
                if (!context.TryGetConstantValue(propertyName, out var actualValue))
                {
                    return new Evaluation(EvaluationResult.Unknown, $"{propertyName} not a constant value");
                }
                bool conditionMet = isNotEquals ? actualValue != expectedValue : actualValue == expectedValue;
                return new Evaluation(
                    conditionMet ? EvaluationResult.True : EvaluationResult.False,
                    $"{propertyName}({actualValue.ToString(CultureInfo.InvariantCulture)}) {(isNotEquals ? "!=" : "==")} {expectedValue.ToString(CultureInfo.InvariantCulture)}");
            }

            public override void CollectPropertyNames(ISet<string> propertyNames)
            {
                propertyNames.Add(propertyName);
            }
        }

        public abstract Evaluation Evaluate(EvaluationContext context);

        public abstract void CollectPropertyNames(ISet<string> propertyNames);

        public static IfexStatement Parse(string line)
        {
            if (line == null || !line.StartsWithSimple("#ifex"))
                return null;
            int index = 5;
            SkipWhitespace(line, ref index);
            var statement = ParseOr(line, ref index);
            if (statement == null)
                return null;
            SkipWhitespace(line, ref index);
            return index == line.Length ? statement : null;
        }

        private static IfexStatement ParseOr(string line, ref int index)
        {
            var statement = ParseAnd(line, ref index);
            if (statement == null)
                return null;
            while (true)
            {
                SkipWhitespace(line, ref index);
                if (!TryConsume(line, ref index, "||"))
                    return statement;
                var right = ParseAnd(line, ref index);
                if (right == null)
                    return null;
                statement = new BinaryStatement(BinaryOperator.Or, statement, right);
            }
        }

        private static IfexStatement ParseAnd(string line, ref int index)
        {
            var statement = ParsePrimary(line, ref index);
            if (statement == null)
                return null;
            while (true)
            {
                SkipWhitespace(line, ref index);
                if (!TryConsume(line, ref index, "&&"))
                    return statement;
                var right = ParsePrimary(line, ref index);
                if (right == null)
                    return null;
                statement = new BinaryStatement(BinaryOperator.And, statement, right);
            }
        }

        private static IfexStatement ParsePrimary(string line, ref int index)
        {
            SkipWhitespace(line, ref index);
            if (index == line.Length)
                return null;
            if (line[index] == '(')
            {
                index++;
                var statement = ParseOr(line, ref index);
                if (statement == null)
                    return null;
                SkipWhitespace(line, ref index);
                if (index == line.Length || line[index] != ')')
                    return null;
                index++;
                return statement;
            }
            return ParseCondition(line, ref index);
        }

        private static IfexStatement ParseCondition(string line, ref int index)
        {
            var name = ShaderAnalyzer.ParseIdentifierAndTrailingWhitespace(line, ref index);
            if (name == null)
                return null;
            if (name == "isNotAnimated" || name == "isAnimated")
            {
                bool isAnimatedCheck = name == "isAnimated";
                if (index == line.Length || line[index] != '(')
                    return null;
                index++;
                SkipWhitespace(line, ref index);
                var propertyName = ShaderAnalyzer.ParseIdentifierAndTrailingWhitespace(line, ref index);
                if (propertyName == null)
                    return null;
                if (index == line.Length || line[index] != ')')
                    return null;
                index++;
                return new AnimationStatement(propertyName, isAnimatedCheck);
            }

            SkipWhitespace(line, ref index);
            if (index >= line.Length - 1)
                return null;
            bool isNotEquals;
            if (TryConsume(line, ref index, "!="))
            {
                isNotEquals = true;
            }
            else if (TryConsume(line, ref index, "=="))
            {
                isNotEquals = false;
            }
            else
            {
                return null;
            }

            SkipWhitespace(line, ref index);
            if (!TryParseFloat(line, ref index, out var value))
                return null;
            return new ComparisonStatement(name, isNotEquals, value);
        }

        private static void SkipWhitespace(string line, ref int index)
        {
            while (index < line.Length && char.IsWhiteSpace(line[index]))
                index++;
        }

        private static bool TryConsume(string line, ref int index, string token)
        {
            if (index + token.Length > line.Length || string.CompareOrdinal(line, index, token, 0, token.Length) != 0)
                return false;
            index += token.Length;
            return true;
        }

        private static bool TryParseFloat(string line, ref int index, out float value)
        {
            int startIndex = index;
            if (index < line.Length && (line[index] == '+' || line[index] == '-'))
                index++;

            bool sawDigit = false;
            while (index < line.Length && char.IsDigit(line[index]))
            {
                sawDigit = true;
                index++;
            }

            if (index < line.Length && line[index] == '.')
            {
                index++;
                while (index < line.Length && char.IsDigit(line[index]))
                {
                    sawDigit = true;
                    index++;
                }
            }

            if (!sawDigit)
            {
                value = 0f;
                index = startIndex;
                return false;
            }

            if (index < line.Length && (line[index] == 'e' || line[index] == 'E'))
            {
                index++;
                if (index < line.Length && (line[index] == '+' || line[index] == '-'))
                    index++;
                int exponentDigitsStart = index;
                while (index < line.Length && char.IsDigit(line[index]))
                    index++;
                if (exponentDigitsStart == index)
                {
                    value = 0f;
                    index = startIndex;
                    return false;
                }
            }

            return float.TryParse(line.Substring(startIndex, index - startIndex), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
#endif