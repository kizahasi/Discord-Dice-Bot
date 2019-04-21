using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DiscordDice
{
    // "-1d100+1-2d5" のような式を解析する機能を提供する。
    // 式ツリーの Expression と被って不便にならないように Expr という名前にしている(Expression は使いそうにないから杞憂な気もするけど)。
    internal static class Expr
    {
        private static class Tokens
        {
            internal interface IToken
            {

            }

            internal class ConstantToken : IToken
            {
                public ConstantToken(BigInteger value)
                {
                    Value = value;
                }

                public BigInteger Value { get; }
            }

            internal class PlusToken : IToken
            {

            }

            internal class MinusToken : IToken
            {

            }

            internal class DieToken : IToken
            {
                public DieToken(BigInteger count, BigInteger max)
                {
                    Count = count;
                    Max = max;
                }

                public BigInteger Count { get; }
                public BigInteger Max { get; }
            }
        }

        private static class Interpreter
        {
            static readonly IReadOnlyList<(int, char)> numbers = 
                new (int number, char character)[]
                    {
                        (0, '0'), (0, '０'),
                        (1, '1'), (1, '１'),
                        (2, '2'), (2, '２'),
                        (3, '3'), (3, '３'),
                        (4, '4'), (4, '４'),
                        (5, '5'), (5, '５'),
                        (6, '6'), (6, '６'),
                        (7, '7'), (7, '７'),
                        (8, '8'), (8, '８'),
                        (9, '9'), (9, '９'),
                    };

            private sealed class TokensBuilder
            {
                LastType _lastType = LastType.None;

                BigInteger _lastNumber; // _lastType == LastType.Number || _lastType == LastType.D || _lastType == LastType.DRight のときのみ使われる
                BigInteger _dCountNumber; // _lastType == LastType.DRight のときのみ使われる

                // 例1: ['6', ' '] の場合、_lastType == LastType.Number かつ _hasSpace == true かつ _hasLineBreak == false
                // 例2: ['6', '\n'] の場合、_lastType == LastType.Number かつ _hasSpace == false かつ _hasLineBreak == true
                // 例3: ['6', '\n', ' '] の場合、_lastType == LastType.Number かつ _hasSpace == true かつ _hasLineBreak == true
                // 例3: ['6', ' ', '\n'] の場合、_lastType == LastType.Number かつ _hasSpace == true かつ _hasLineBreak == true
                bool _hasSpace;
                bool _hasLineBreak;

                readonly List<Tokens.IToken> _tokens = new List<Tokens.IToken>();

                static int? TryToInt32(BigInteger i)
                {
                    if (int.TryParse(i.ToString(), out var result))
                    {
                        return result;
                    }
                    return null;
                }

                public bool TryAddSpace()
                {
                    _hasSpace = true;
                    return true;
                }

                public bool TryAddLineBreak()
                {
                    _hasLineBreak = true;
                    return true;
                }

                // 0 ≦ n ≦ 9
                public bool TryAddNumber(int n)
                {
                    switch (_lastType)
                    {
                        case LastType.None:
                            _lastType = LastType.Number;
                            _lastNumber = n;
                            _hasSpace = false;
                            _hasLineBreak = false;
                            return true;
                        case LastType.Number:
                            if (_hasSpace || _hasLineBreak)
                            {
                                return false;
                            }
                            _lastNumber = (_lastNumber * 10) + n;
                            _hasSpace = false;
                            _hasLineBreak = false;
                            return true;
                        case LastType.Plus:
                            _tokens.Add(new Tokens.PlusToken());
                            _lastType = LastType.Number;
                            _lastNumber = n;
                            _hasSpace = false;
                            _hasLineBreak = false;
                            return true;
                        case LastType.Minus:
                            _tokens.Add(new Tokens.MinusToken());
                            _lastType = LastType.Number;
                            _lastNumber = n;
                            _hasSpace = false;
                            _hasLineBreak = false;
                            return true;
                        case LastType.D:
                            if (_hasSpace || _hasLineBreak)
                            {
                                return false;
                            }
                            _dCountNumber = _lastNumber;
                            _lastNumber = n;
                            _lastType = LastType.DRightNumber;
                            _hasSpace = false;
                            _hasLineBreak = false;
                            return true;
                        case LastType.DRightNumber:
                            if (_hasSpace || _hasLineBreak)
                            {
                                return false;
                            }
                            _lastNumber = (_lastNumber * 10) + n;
                            _hasSpace = false;
                            _hasLineBreak = false;
                            return true;
                        default:
                            throw new Exception();
                    }
                }

                public bool TryAddD()
                {
                    switch (_lastType)
                    {
                        case LastType.Number:
                            if (_hasSpace || _hasLineBreak)
                            {
                                return false;
                            }
                            _lastType = LastType.D;
                            _hasSpace = false;
                            _hasLineBreak = false;
                            return true;
                        default:
                            return false;
                    }
                }

                public bool TryAddPlus()
                {
                    switch (_lastType)
                    {
                        case LastType.None:
                            _lastType = LastType.Plus;
                            _hasSpace = false;
                            _hasLineBreak = false;
                            return true;
                        case LastType.Number:
                            _tokens.Add(new Tokens.ConstantToken(_lastNumber));
                            _lastType = LastType.Plus;
                            _hasSpace = false;
                            _hasLineBreak = false;
                            return true;
                        case LastType.Plus:
                            _tokens.Add(new Tokens.PlusToken());
                            _hasSpace = false;
                            _hasLineBreak = false;
                            return true;
                        case LastType.Minus:
                            _tokens.Add(new Tokens.MinusToken());
                            _lastType = LastType.Plus;
                            _hasSpace = false;
                            _hasLineBreak = false;
                            return true;
                        case LastType.D:
                            return false;
                        case LastType.DRightNumber:
                            _tokens.Add(new Tokens.DieToken(_dCountNumber, _lastNumber));
                            _lastType = LastType.Plus;
                            _hasSpace = false;
                            _hasLineBreak = false;
                            return true;
                        default:
                            throw new Exception();
                    }
                }

                public bool TryAddMinus()
                {
                    switch (_lastType)
                    {
                        case LastType.None:
                            _lastType = LastType.Minus;
                            _hasSpace = false;
                            _hasLineBreak = false;
                            return true;
                        case LastType.Number:
                            _tokens.Add(new Tokens.ConstantToken(_lastNumber));
                            _lastType = LastType.Minus;
                            _hasSpace = false;
                            _hasLineBreak = false;
                            return true;
                        case LastType.Plus:
                            _tokens.Add(new Tokens.PlusToken());
                            _hasSpace = false;
                            _hasLineBreak = false;
                            return true;
                        case LastType.Minus:
                            _tokens.Add(new Tokens.MinusToken());
                            _lastType = LastType.Minus;
                            _hasSpace = false;
                            _hasLineBreak = false;
                            return true;
                        case LastType.D:
                            return false;
                        case LastType.DRightNumber:
                            _tokens.Add(new Tokens.DieToken(_dCountNumber, _lastNumber));
                            _lastType = LastType.Minus;
                            _hasSpace = false;
                            _hasLineBreak = false;
                            return true;
                        default:
                            throw new Exception();
                    }
                }

                public IReadOnlyList<Tokens.IToken> FinishOrDefault()
                {
                    switch (_lastType)
                    {
                        case LastType.None:
                            return _tokens.ToReadOnly();
                        case LastType.Number:
                            _tokens.Add(new Tokens.ConstantToken(_lastNumber));
                            return _tokens.ToReadOnly();
                        case LastType.Plus:
                            return null;
                        case LastType.Minus:
                            return null;
                        case LastType.D:
                            return null;
                        case LastType.DRightNumber:
                            _tokens.Add(new Tokens.DieToken(_dCountNumber, _lastNumber));
                            return _tokens.ToReadOnly();
                        default:
                            throw new Exception();
                    }
                }

                private enum LastType
                {
                    None,
                    Number,
                    Plus,
                    Minus,
                    D, // dの文字そのもの
                    DRightNumber, // dの文字の右側にある数字
                }
            }

            // 解析エラーがある場合は null を返す
            // 「null 以外を返す AND 戻り値の Count != 0 ⇔ 正常な Expr である」が成り立っている
            public static IReadOnlyList<Tokens.IToken> ToTokens(string text)
            {
                var tokensBuilder = new TokensBuilder();
                foreach (var c in text)
                {
                    if (char.IsWhiteSpace(c))
                    {
                        if (!tokensBuilder.TryAddSpace())
                        {
                            return null;
                        }
                        continue;
                    }

                    if (c == '\r' || c == '\n')
                    {
                        if (!tokensBuilder.TryAddLineBreak())
                        {
                            return null;
                        }
                        continue;
                    }

                    if (c == '+' || c == '＋')
                    {
                        if (!tokensBuilder.TryAddPlus())
                        {
                            return null;
                        }
                        continue;
                    }

                    if (c == '-' || c == '－')
                    {
                        if (!tokensBuilder.TryAddMinus())
                        {
                            return null;
                        }
                        continue;
                    }

                    if (c == 'd' || c == 'D' || c == 'ｄ' || c == 'Ｄ')
                    {
                        if (!tokensBuilder.TryAddD())
                        {
                            return null;
                        }
                        continue;
                    }

                    foreach (var (number, character) in numbers)
                    {
                        if (c.ToString() == character.ToString())
                        {
                            if (!tokensBuilder.TryAddNumber(number))
                            {
                                return null;
                            }
                            goto Continue;
                        }
                    }

                    return null;

                    Continue:;
                }

                return tokensBuilder.FinishOrDefault();
            }
        }

        public static class NumberFunctions
        {
            internal interface INumberFunction
            {
                // 例1: (3, "+1d6(3)") ただし1個目の場合は (3, "1d6(3)")
                // 例2: (-4, "-1d15(4)")
                (BigInteger, string) Execute();

                // 例1: "+1d6" ただし1個目の場合は "1d6"
                // 例2: "-4"
                string NonExecutedString { get; }
            }

            internal sealed class Die : IEquatable<Die>, INumberFunction
            {
                // countは負の値でもいい
                // maxが負のときの変換規則: "xD-y" == "-xDy"
                public Die(BigInteger count, BigInteger max, bool omitPlusString)
                {
                    if (max <= 0) throw new ArgumentOutOfRangeException($"Expected not {nameof(max)} <= 0 but {nameof(max)} == {max}");

                    Count = count;
                    Max = max;
                    OmitPlusString = omitPlusString;
                    if(int.TryParse(Count.ToString(), out var countAsInt32))
                    {
                        CountAsInt32 = countAsInt32;
                    }
                    if (int.TryParse(Max.ToString(), out var maxAsInt32))
                    {
                        MaxAsInt32 = maxAsInt32;
                    }
                }

                public BigInteger Count { get; }
                public int? CountAsInt32 { get; }
                public BigInteger Max { get; }
                public int? MaxAsInt32 { get; }
                public bool OmitPlusString { get; }

                public (BigInteger, string) Roll()
                {
                    var tooBigErrorMessage = $"{ToString()}(エラー: 大きすぎます)";

                    if(CountAsInt32 == null || MaxAsInt32 == null)
                    {
                        return (0, tooBigErrorMessage);
                    }

                    var isMinus = CountAsInt32.Value <= -1;
                    var count = Math.Abs(CountAsInt32.Value);
                    var max = MaxAsInt32.Value;

                    if (count >= 101 || max >= 1000000001)
                    {
                        return (0, tooBigErrorMessage);
                    }
                    if (count == 0)
                    {
                        return (0, $"{ToString()}(0)");
                    }

                    var result = new StringBuilder($"{ToString()}(");
                    var sum = 0;
                    var isFirst = true;
                    foreach (var _ in Enumerable.Repeat(System.Reactive.Unit.Default, count))
                    {
                        var rolled = max == 0 ? 0 : Random.Next(1, max + 1);
                        sum += rolled;
                        if (!isFirst)
                        {
                            result.Append("+");
                        }
                        result.Append(rolled);
                        isFirst = false;
                    }
                    result.Append(')');
                    if (isMinus)
                    {
                        return (-sum, result.ToString());
                    }
                    else
                    {
                        return (sum, result.ToString());
                    }
                }

                (BigInteger, string) INumberFunction.Execute()
                {
                    return Roll();
                }

                public bool Equals(Die other)
                {
                    if (other == null)
                    {
                        return false;
                    }

                    return Max == other.Max && Count == other.Count && OmitPlusString == other.OmitPlusString;
                }

                public override bool Equals(object obj) => Equals(obj as Die);

                public override int GetHashCode() => Max.GetHashCode() ^ Count.GetHashCode() ^ OmitPlusString.GetHashCode();

                public static bool operator ==(Die x, Die y)
                {
                    if ((object)x == null)
                    {
                        return (object)y == null;
                    }
                    return x.Equals(y);
                }

                public static bool operator !=(Die x, Die y)
                {
                    return !(x == y);
                }

                public override string ToString()
                {
                    if (Count >= 1)
                    {
                        return $"{(OmitPlusString ? "" : "+")}{Count}d{Max}";
                    }
                    return $"{Count}d{Max}";
                }

                public string NonExecutedString => ToString();
            }

            internal sealed class Constant : IEquatable<Constant>, INumberFunction
            {
                public Constant(BigInteger value, bool omitPlusString)
                {
                    Value = value;
                    OmitPlusString = omitPlusString;
                }

                public BigInteger Value { get; }
                public bool OmitPlusString { get; }

                public (BigInteger, string) Execute()
                {
                    var plusString = Value >= 0 && !OmitPlusString ? "+" : "";
                    return (Value, plusString + Value.ToString());
                }

                public bool Equals(Constant other)
                {
                    if (other == null)
                    {
                        return false;
                    }

                    return Value == other.Value && OmitPlusString == other.OmitPlusString;
                }

                public override bool Equals(object obj) => Equals(obj as Constant);

                public override int GetHashCode() => Value.GetHashCode() ^ OmitPlusString.GetHashCode();

                public static bool operator ==(Constant x, Constant y)
                {
                    if ((object)x == null)
                    {
                        return (object)y == null;
                    }
                    return x.Equals(y);
                }

                public static bool operator !=(Constant x, Constant y)
                {
                    return !(x == y);
                }

                public override string ToString()
                {
                    var plusString = OmitPlusString ? "" : "+";
                    return $"{plusString}{Value}";
                }

                public string NonExecutedString => ToString();
            }
        }

        public sealed class Main
        {
            public IReadOnlyList<NumberFunctions.INumberFunction> Functions { get; }

            static Main()
            {
                Invalid = new Main(null);
            }

            private Main(IReadOnlyList<NumberFunctions.INumberFunction> functions)
            {
                Functions = functions;
            }
            
            public static Main Invalid { get; }

            public static Main Create(IReadOnlyList<NumberFunctions.INumberFunction> functions) => new Main(functions);

            private static Regex CreateRegex(ulong botCurrentUserId)
            {
                return new Regex($@"^\s*<@\!?(?<id>{botCurrentUserId})>(?<body>.*)$");
            }

            public static async Task<Main> InterpretFromLazySocketMessageAsync(ILazySocketMessage message, ulong botCurrentUserId)
            {
                var content = await message.GetContentAsync();
                var nonMentionedCode = Interpret(content);
                if (nonMentionedCode.IsValid)
                {
                    return nonMentionedCode;
                }

                var regex = CreateRegex(botCurrentUserId);
                var m = regex.Match(content);
                if (!m.Success)
                {
                    return Invalid;
                }
                var body = m.Groups["body"].Value;
                return Interpret(body);
            }

            public static Main Interpret(string text)
            {
                if (text == null)
                {
                    return Invalid;
                }

                var tokens = Interpreter.ToTokens(text);
                if (tokens == null)
                {
                    return Invalid;
                }

                var result = new List<NumberFunctions.INumberFunction>();
                var omitPlusString = true;
                var isMinus = false;
                foreach (var token in tokens)
                {
                    switch (token)
                    {
                        case Tokens.ConstantToken t:
                            result.Add(new NumberFunctions.Constant(isMinus ? -t.Value : t.Value, omitPlusString));
                            omitPlusString = false;
                            isMinus = false;
                            continue;
                        case Tokens.PlusToken t:
                            continue;
                        case Tokens.MinusToken t:
                            isMinus = !isMinus;
                            continue;
                        case Tokens.DieToken t:
                            result.Add(new NumberFunctions.Die(isMinus ? -t.Count : t.Count, t.Max, omitPlusString));
                            omitPlusString = false;
                            isMinus = false;
                            continue;
                    }
                }
                return Create(result.ToReadOnly());
            }

            public bool IsValid { get => Functions != null && Functions.Count != 0; }

            public sealed class Executed
            {
                public Executed(Main expr, BigInteger value, string message)
                {
                    Expr = expr ?? throw new ArgumentNullException(nameof(expr));
                    Value = value;
                    Message = message;
                }

                public Main Expr { get; }
                public BigInteger Value { get; }
                public string Message { get; }
            }

            public Executed ExecuteOrDefault()
            {
                if (!IsValid)
                {
                    return default;
                }

                var resultValue = BigInteger.Zero;
                var resultMessage = new StringBuilder();
                foreach(var token in Functions)
                {
                    var (val, msg) = token.Execute();
                    resultValue = resultValue + val;
                    resultMessage.Append(msg);
                }
                resultMessage.Append('=');
                resultMessage.Append($"**{resultValue}**");
                return new Executed(this, resultValue, resultMessage.ToString());
            }

            // 「3+4」や「-1」のような、BOT が反応すべきでない単純な数かどうかを示す
            public bool IsConstant
            {
                get
                {
                    if (!IsValid)
                    {
                        return false;
                    }

                    if (Functions.Count != 1)
                    {
                        return false;
                    }
                    var function = Functions[0];
                    if (function == null)
                    {
                        return false;
                    }
                    return function is NumberFunctions.Constant;
                }
            }

            // 1d100+1d10+1d100+876 => [ (100, 2), (10, 1) ]
            // 2d100-1d100+1d6 => [ (100, 2), (-100, 1), (6, 1) ]
            // 後者のようなパターンは注意!
            private IDictionary<BigInteger, BigInteger> MergeDice()
            {
                return
                    (Functions ?? new NumberFunctions.INumberFunction[] { })
                    .OfType<NumberFunctions.Die>()
                    .GroupBy(die => new { Max = die.Max, IsMinus = die.Count < 0 })
                    .Select(group => 
                        new
                        {
                            Key = (group.Key.IsMinus ? -1 : 1) * group.Key.Max,
                            Value = group.Aggregate(BigInteger.Zero, (seed, die) => seed + die.Count)
                        })
                    .Where(a => a.Value >= 1)
                    .ToDictionary(a => a.Key, a => a.Value);
            }

            // 100+1001-1+200d2000 => 1100
            private BigInteger AggregateConstants()
            {
                return
                    (Functions ?? new NumberFunctions.INumberFunction[] { })
                    .OfType<NumberFunctions.Constant>()
                    .Aggregate(BigInteger.Zero, (seed, constant) => seed + constant.Value);
            }

            // キーと値が全て一致していたら true を返す。
            // IDictionary には副作用のある操作がなされるので注意。
            private static bool AreEquivalent<TKey, TValue>(IDictionary<TKey, TValue> x, IDictionary<TKey, TValue> y)
            {
                if (x == null)
                {
                    return y == null;
                }

                foreach (var xPair in x)
                {
                    if (y.Remove(xPair.Key, out var yValue))
                    {
                        if (Equals(xPair.Value, yValue))
                        {
                            continue;
                        }
                    }

                    return false;
                }

                return y.Count == 0;
            }

            ///<summary>等価なら true を返します。</summary>
            // AreEquivalent(1d100+1d100, 2d100) => true
            // AreEquivalent(1d10+1d100+2, -1+1d100+3+1d10) => true
            // Equals をオーバーライドしなかったのは、例えば 1d10+1d100 と 1d100+1d10 はメッセージを投稿するときに文字列の順序が異なるから。
            public static bool AreEquivalent(Main x, Main y)
            {
                if (x == null)
                {
                    return y == null;
                }

                if (!x.IsValid)
                {
                    return !y.IsValid;
                }

                var mergedXDice = x.MergeDice();
                var mergedYDice = y.MergeDice();
                var xConstantSum = x.AggregateConstants();
                var yConstantSum = y.AggregateConstants();

                return AreEquivalent(mergedXDice, mergedYDice) && xConstantSum == yConstantSum;
            }

            public override string ToString()
            {
                return
                    (Functions ?? new NumberFunctions.INumberFunction[] { })
                    .Aggregate(new StringBuilder(), (sb, f) => sb.Append(f.NonExecutedString))
                    .ToString();
            }
        }
    }

    
}
