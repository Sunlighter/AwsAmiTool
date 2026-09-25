using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace AwsAmiTool
{
    public abstract class Parser
    {
        public abstract ParseResult Parse
        (
            ImmutableList<object> stack,
            ImmutableStringInputStream input
        );
    }

    public sealed class ReadLiteralParser : Parser
    {
        private readonly string literal;
        private readonly StringComparison comparison;

        public ReadLiteralParser
        (
            string literal,
            StringComparison comparison
        )
        {
            this.literal = literal;
            this.comparison = comparison;
        }

        public string Literal => literal;
        public StringComparison Comparison => comparison;

        public override ParseResult Parse(ImmutableList<object> stack, ImmutableStringInputStream input)
        {
            int length = literal.Length;
            if (input.Input.Length - input.Pos >= length)
            {
                string candidate = input.Input.Substring(input.Pos, length);
                if (string.Compare(literal, candidate, comparison) == 0)
                {
                    return new ParseSuccess
                    (
                        stack.PushLast(candidate),
                        new ImmutableStringInputStream(input.Input, input.Pos + length, input.Location.Add(candidate))
                    );
                }
            }

            return ParseFailure.Value;
        }
    }

    public sealed class ReadRegexParser : Parser
    {
        private readonly Regex regex;
        private readonly string regexOfRecord;

        public ReadRegexParser
        (
            string regexString,
            bool ignoreCase
        )
        {
            string regexAnchored;
            string regexOfRecord;

            if (regexString.StartsWith("\\G"))
            {
                regexAnchored = regexString;
                regexOfRecord = regexString[2..];
            }
            else
            {
                regexAnchored = "\\G" + regexString;
                regexOfRecord = regexString;
            }

            this.regexOfRecord = regexOfRecord;
            regex = new Regex(regexAnchored, ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        }

        public string RegexOfRecord => regexOfRecord;

        public override ParseResult Parse(ImmutableList<object> stack, ImmutableStringInputStream input)
        {
            Match m = regex.Match(input.Input, input.Pos);
            if (m.Success && m.Index == input.Pos)
            {
                return new ParseSuccess
                (
                    stack.PushLast(m),
                    new ImmutableStringInputStream(input.Input, input.Pos + m.Length, input.Location.Add(m.Value))
                );
            }
            else
            {
                return ParseFailure.Value;
            }
        }
    }

    public sealed class AssertEofParser : Parser
    {
        private static readonly AssertEofParser value = new AssertEofParser();

        private AssertEofParser() { }

        public static AssertEofParser Value => value;

        public override ParseResult Parse(ImmutableList<object> stack, ImmutableStringInputStream input)
        {
            if (input.Pos == input.Input.Length)
            {
                return new ParseSuccess(stack, input);
            }
            else
            {
                return ParseFailure.Value;
            }
        }
    }

    public sealed class NopParser : Parser
    {
        private static readonly NopParser value = new NopParser();

        private NopParser() { }

        public static NopParser Value => value;

        public override ParseResult Parse(ImmutableList<object> stack, ImmutableStringInputStream input)
        {
            return new ParseSuccess(stack, input);
        }
    }

    public sealed class SequenceParser : Parser
    {
        private readonly ImmutableList<Parser> parsers;

        public SequenceParser
        (
            params ImmutableList<Parser> parsers
        )
        {
            this.parsers = parsers;
        }

        public ImmutableList<Parser> Parsers => parsers;

        public override ParseResult Parse(ImmutableList<object> stack, ImmutableStringInputStream input)
        {
            foreach (Parser p in parsers)
            {
                ParseResult result = p.Parse(stack, input);
                if (result is ParseFailure)
                {
                    return result;
                }
                else if (result is ParseSuccess success)
                {
                    stack = success.Stack;
                    input = success.Input;
                }
            }
            return new ParseSuccess(stack, input);
        }
    }

    public sealed class FailParser : Parser
    {
        private static readonly FailParser value = new FailParser();

        private FailParser() { }

        public static FailParser Value => value;

        public override ParseResult Parse(ImmutableList<object> stack, ImmutableStringInputStream input)
        {
            return ParseFailure.Value;
        }
    }

    public sealed class AlternativesParser : Parser
    {
        private readonly ImmutableList<Parser> parsers;

        public AlternativesParser
        (
            params ImmutableList<Parser> parsers
        )
        {
            this.parsers = parsers;
        }

        public ImmutableList<Parser> Parsers => parsers;

        public override ParseResult Parse(ImmutableList<object> stack, ImmutableStringInputStream input)
        {
            foreach (Parser p in parsers)
            {
                ParseResult result = p.Parse(stack, input);
                if (result is ParseSuccess)
                {
                    return result;
                }
            }
            return ParseFailure.Value;
        }
    }

    public enum RepetitionType
    {
        ZeroOrMore,
        OneOrMore
    }

    public sealed class RepeatingParser : Parser
    {
        private readonly RepetitionType repetitionType;
        private readonly Parser body;

        public RepeatingParser(RepetitionType repetitionType, Parser body)
        {
            this.repetitionType = repetitionType;
            this.body = body;
        }

        public RepetitionType RepetitionType => repetitionType;

        public Parser Body => body;

        public override ParseResult Parse(ImmutableList<object> stack, ImmutableStringInputStream input)
        {
            bool atLeastOne = false;
            while (true)
            {
                ParseResult result = body.Parse(stack, input);
                if (result is ParseFailure)
                {
                    if (repetitionType == RepetitionType.ZeroOrMore || atLeastOne)
                    {
                        return new ParseSuccess(stack, input);
                    }
                    else
                    {
                        return ParseFailure.Value;
                    }
                }
                else if (result is ParseSuccess success)
                {
                    atLeastOne = true;
                    stack = success.Stack;
                    input = success.Input;
                }
                else
                {
                    throw new InvalidOperationException("Unexpected ParseResult type");
                }
            }
        }
    }

    public enum LookaheadType
    {
        OnlyIfFollowedBy,
        OnlyIfNotFollowedBy
    }

    public sealed class LookaheadParser : Parser
    {
        private readonly LookaheadType lookaheadType;
        private readonly Parser body;

        public LookaheadParser(LookaheadType lookaheadType, Parser body)
        {
            this.lookaheadType = lookaheadType;
            this.body = body;
        }

        public LookaheadType LookaheadType => lookaheadType;
        public Parser Body => body;

        public override ParseResult Parse(ImmutableList<object> stack, ImmutableStringInputStream input)
        {
            ParseResult result = body.Parse(stack, input);
            bool gotSuccess = result is ParseSuccess;
            bool wantSuccess = lookaheadType == LookaheadType.OnlyIfFollowedBy;

            return (wantSuccess == gotSuccess)
                ? new ParseSuccess(stack, input)
                : ParseFailure.Value;
        }
    }

    public sealed class PushLocation : Parser
    {
        private static readonly PushLocation value = new PushLocation();
        private PushLocation() { }

        public static PushLocation Value => value;

        public override ParseResult Parse(ImmutableList<object> stack, ImmutableStringInputStream input)
        {
            return new ParseSuccess
            (
                stack.PushLast(input.Location),
                input
            );
        }
    }

    public sealed class PushParser : Parser
    {
        private readonly object value;

        public PushParser(object value)
        {
            this.value = value;
        }

        public object Value => value;

        public override ParseResult Parse(ImmutableList<object> stack, ImmutableStringInputStream input)
        {
            return new ParseSuccess
            (
                stack.PushLast(value),
                input
            );
        }
    }

    public sealed class PushNewParser : Parser
    {
        private readonly Func<object> createValue;

        public PushNewParser(Func<object> createValue)
        {
            this.createValue = createValue;
        }

        public Func<object> CreateValueFunc => createValue;

        public override ParseResult Parse(ImmutableList<object> stack, ImmutableStringInputStream input)
        {
            return new ParseSuccess
            (
                stack.PushLast(createValue()),
                input
            );
        }
    }

    public sealed class DropParser : Parser
    {
        private static readonly DropParser value = new DropParser();

        private DropParser() { }

        public static DropParser Value => value;

        public override ParseResult Parse(ImmutableList<object> stack, ImmutableStringInputStream input)
        {
            if (stack.Count > 0)
            {
                return new ParseSuccess
                (
                    stack.ExceptLast(1),
                    input
                );
            }
            else throw new InvalidOperationException("Stack underflow when trying to drop value");
        }
    }

    public sealed class ReduceParser : Parser
    {
        private readonly int count;
        private readonly Func<ImmutableList<object>, object> reduction;

        public ReduceParser(int count, Func<ImmutableList<object>, object> reduction)
        {
            this.count = count;
            this.reduction = reduction;
        }

        public int Count => count;

        public Func<ImmutableList<object>, object> Reduction => reduction;

        public override ParseResult Parse(ImmutableList<object> stack, ImmutableStringInputStream input)
        {
            if (stack.Count >= count)
            {
                ImmutableList<object> toReduce = stack.Last(count);
                object result = reduction(toReduce);
                ImmutableList<object> newStack = stack.ExceptLast(count).PushLast(result);
                return new ParseSuccess(newStack, input);
            }
            else
            {
                throw new InvalidOperationException($"Stack underflow when trying to reduce: expected {count} values");
            }
        }
    }

    public sealed class ForwardingParser : Parser
    {
        private Parser? target;

        public ForwardingParser()
        {
            this.target = null;
        }

        public void SetTarget(Parser p)
        {
            target = p;
        }

        public override ParseResult Parse(ImmutableList<object> stack, ImmutableStringInputStream input)
        {
            if (target is not null)
            {
                return target.Parse(stack, input);
            }
            else
            {
                throw new InvalidOperationException("ForwardingParser target is not set");
            }
        }
    }

    public static partial class ParserFactory
    {
        public static Parser Optional(Parser p)
        {
            return new AlternativesParser(p, NopParser.Value);
        }

        private static readonly Lazy<Parser> whiteSpace = new Lazy<Parser>
        (
            () => new SequenceParser
            (
                new ReadRegexParser("\\s+", false),
                DropParser.Value
            ),
            LazyThreadSafetyMode.ExecutionAndPublication
        );

        public static Parser WhiteSpace => whiteSpace.Value;

        private static readonly Lazy<Parser> optionalWhiteSpace = new Lazy<Parser>
        (
            () => new SequenceParser
            (
                new ReadRegexParser("\\s*", false),
                DropParser.Value
            ),
            LazyThreadSafetyMode.ExecutionAndPublication
        );

        public static Parser OptionalWhiteSpace => optionalWhiteSpace.Value;

        private static readonly Lazy<Parser> quotedString = new Lazy<Parser>
        (
            GetQuotedStringParser,
            LazyThreadSafetyMode.ExecutionAndPublication
        );

        private static Parser GetQuotedStringParser()
        {
            return new SequenceParser
            (
                new ReadLiteralParser("\"", StringComparison.Ordinal),
                DropParser.Value,
                new PushParser(ImmutableStringBuilder.Empty),
                new RepeatingParser
                (
                    RepetitionType.ZeroOrMore,
                    new AlternativesParser
                    (
                        new SequenceParser
                        (
                            new ReadRegexParser("\\\\[\\\\\\\"]", false),
                            new ReduceParser
                            (
                                2,
                                args =>
                                {
                                    ImmutableStringBuilder sb = (ImmutableStringBuilder)args[0];
                                    Match m = (Match)args[1];
                                    return sb.Append(m.Value[1]);
                                }
                            )
                        ),
                        new SequenceParser
                        (
                            new ReadRegexParser("[^\\\\\\\"]+", false),
                            new ReduceParser
                            (
                                2,
                                args =>
                                {
                                    ImmutableStringBuilder sb = (ImmutableStringBuilder)args[0];
                                    Match m = (Match)args[1];
                                    return sb.Append(m.Value);
                                }
                            )
                        )
                    )
                ),
                new ReadLiteralParser("\"", StringComparison.Ordinal),
                DropParser.Value,
                new ReduceParser
                (
                    1,
                    args =>
                    {
                        ImmutableStringBuilder sb = (ImmutableStringBuilder)args[0];
                        return sb.Value;
                    }
                )
            );
        }

        public static Parser QuotedString => quotedString.Value;

        private static readonly Lazy<Parser> int32 = new Lazy<Parser>
        (
            GetInt32Parser,
            LazyThreadSafetyMode.ExecutionAndPublication
        );

        private static Parser GetInt32Parser()
        {
            return new SequenceParser
            (
                new ReadRegexParser("[+-]?[0-9]+", false),
                new ReduceParser
                (
                    1,
                    args =>
                    {
                        Match m = (Match)args[0];
                        return int.Parse(m.Value);
                    }
                )
            );
        }

        public static Parser Int32 => int32.Value;
    }

    public static partial class Utility
    {
        public static ImmutableList<T> PushLast<T>(this ImmutableList<T> list, T value)
        {
            return list.Add(value);
        }

        public static ImmutableList<T> Last<T>(this ImmutableList<T> list, int count)
        {
            if (list.Count <= count) return list;
            return list.GetRange(list.Count - count, count);
        }

        public static ImmutableList<T> ExceptLast<T>(this ImmutableList<T> list, int count)
        {
            if (list.Count <= count) return list.Clear();
            return list.GetRange(0, list.Count - count);
        }
    }
}
