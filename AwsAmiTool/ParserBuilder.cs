using Sunlighter.LrParserGenLib;
using Sunlighter.OptionLib;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace AwsAmiTool
{
    public sealed class PushParserPayload
    {
        private readonly object value;

        public PushParserPayload(object value)
        {
            this.value = value;
        }

        public object Value => value;
    }

    public sealed class ProtoSequenceParser
    {
        private readonly ImmutableList<Parser> parsers;

        public ProtoSequenceParser(ImmutableList<Parser> parsers)
        {
            this.parsers = parsers;
        }
        public ImmutableList<Parser> Parsers => parsers;

        public ProtoSequenceParser Add(Parser p) => new ProtoSequenceParser(parsers.Add(p));

        private static readonly ProtoSequenceParser empty = new ProtoSequenceParser(ImmutableList<Parser>.Empty);

        public static ProtoSequenceParser Empty => empty;

        public Parser ToParser()
        {
            ImmutableList<Parser> toConvert = parsers;
            ImmutableList<Parser> converted = ImmutableList<Parser>.Empty;

            while(!toConvert.IsEmpty)
            {
                Parser px = toConvert[0];
                toConvert = toConvert.RemoveAt(0);

                if (px is NopParser)
                {
                    // skip it
                }
                else if (px is SequenceParser sp)
                {
                    toConvert = sp.Parsers.AddRange(toConvert);
                }
                else
                {
                    converted = converted.Add(px);
                }
            }


            if (converted.IsEmpty)
            {
                return NopParser.Value;
            }
            else if (converted.Count == 1)
            {
                return converted[0];
            }
            else
            {
                return new SequenceParser(converted);
            }
        }
    }

    public static class ParserBuilder_Grammar
    {
        [GrammarRule]
        public static ProtoSequenceParser Rule_Init() => ProtoSequenceParser.Empty;

        [GrammarRule]
        public static ProtoSequenceParser Rule_Append(ProtoSequenceParser psp, Parser p) => psp.Add(p);

        [GrammarRule]
        public static Parser Rule_ReadLiteral([TokenTypeName("read-literal")] string _readLiteral, string literal, StringComparison comparison) =>
            new ReadLiteralParser(literal, comparison);

        [GrammarRule]
        public static Parser Rule_ReadRegex([TokenTypeName("read-regex")] string _readRegex, string pattern, bool ignoreCase) =>
            new ReadRegexParser(pattern, ignoreCase);

        [GrammarRule]
        public static Parser Rule_AssertEof([TokenTypeName("assert-eof")] string _assertEof) =>
            AssertEofParser.Value;

        [GrammarRule]
        public static Parser Rule_NopParser([TokenTypeName("nop")] string _nopParser) =>
            NopParser.Value;

        [GrammarRule]
        public static Parser Rule_FailParser([TokenTypeName("fail")] string _failParser) =>
            FailParser.Value;

        [GrammarRule]
        public static Parser Rule_AlternativesParser
        (
            [TokenTypeName("begin-alternatives")] string _beginAlternatives,
            [TokenTypeName("empty-or-non-empty")] ImmutableList<ProtoSequenceParser> parsers,
            [TokenTypeName("end-alternatives")] string _endAlternatives
        )
        {
            ImmutableList<Parser> toConvert = parsers.Select(psp => psp.ToParser()).ToImmutableList();
            ImmutableList<Parser> converted = ImmutableList<Parser>.Empty;

            while(!toConvert.IsEmpty)
            {
                Parser px = toConvert[0];
                toConvert = toConvert.RemoveAt(0);

                if (px is FailParser)
                {
                    // skip it
                }
                else if (px is AlternativesParser ap)
                {
                    toConvert = ap.Parsers.AddRange(toConvert);
                }
                else
                {
                    converted = converted.Add(px);
                }
            }

            if (converted.IsEmpty)
            {
                return FailParser.Value;
            }
            else if (converted.Count == 1)
            {
                return converted[0];
            }
            else
            {
                return new AlternativesParser(converted);
            }
        }

        [GrammarRule]
        [return: TokenTypeName("empty-or-non-empty")]
        public static ImmutableList<ProtoSequenceParser> Rule_Empty() => ImmutableList<ProtoSequenceParser>.Empty;

        [GrammarRule]
        [return: TokenTypeName("empty-or-non-empty")]
        public static ImmutableList<ProtoSequenceParser> Rule_NonEmpty(ImmutableList<ProtoSequenceParser> list) => list;

        [GrammarRule]
        public static ImmutableList<ProtoSequenceParser> Rule_SingleAlternative(ProtoSequenceParser a) => ImmutableList<ProtoSequenceParser>.Empty.Add(a);

        [GrammarRule]
        public static ImmutableList<ProtoSequenceParser> Rule_TwoOrMoreAlternatives
        (
            ImmutableList<ProtoSequenceParser> list,
            [TokenTypeName("or")] string _or,
            ProtoSequenceParser a
        )
            => list.Add(a);

        [GrammarRule]
        public static Parser Rule_RepeatingParser
        (
            [TokenTypeName("begin-repeating")] string _repeatingParser,
            RepetitionType repetitionType,
            ProtoSequenceParser body,
            [TokenTypeName("end-repeating")] string _endRepeatingParser
        )
            => new RepeatingParser(repetitionType, body.ToParser());

        [GrammarRule]
        public static Parser Rule_LookaheadParser
        (
            [TokenTypeName("begin-lookahead")] string _lookaheadParser,
            LookaheadType lookaheadType,
            ProtoSequenceParser body,
            [TokenTypeName("end-lookahead")] string _endLookaheadParser
        )
            => new LookaheadParser(lookaheadType, body.ToParser());

        [GrammarRule]
        public static Parser Rule_PushLocationParser([TokenTypeName("push-location")] string _pushLocationParser)
            => PushLocation.Value;

        [GrammarRule]
        public static Parser Rule_PushParser([TokenTypeName("push")] string _pushParser, PushParserPayload value)
            => new PushParser(value.Value);

        [GrammarRule]
        public static Parser Rule_PushNewParser([TokenTypeName("push-new")] string _pushNewParser, Func<object> createValue)
            => new PushNewParser(createValue);

        [GrammarRule]
        public static Parser Rule_DropParser([TokenTypeName("drop")] string _dropParser)
            => DropParser.Value;

        [GrammarRule]
        public static Parser Rule_ReduceParser([TokenTypeName("reduce")] string _reduceParser, int count, Func<ImmutableList<object>, object> reduction)
            => new ReduceParser(count, reduction);

        [GrammarRule]
        public static Parser Rule_OptionalParser
        (
            [TokenTypeName("begin-optional")] string _optional,
            ProtoSequenceParser body,
            [TokenTypeName("end-optional")] string _endOptional
        )
            => ParserFactory.Optional(body.ToParser());

        [GrammarRule]
        public static Parser Rule_WhiteSpaceParser([TokenTypeName("white-space")] string _whiteSpace) => ParserFactory.WhiteSpace;

        [GrammarRule]
        public static Parser Rule_OptionalWhiteSpaceParser([TokenTypeName("optional-white-space")] string _optionalWhiteSpace) => ParserFactory.OptionalWhiteSpace;

        [GrammarRule]
        public static Parser Rule_QuotedStringParser([TokenTypeName("quoted-string")] string _quotedString) => ParserFactory.QuotedString;

        [GrammarRule]
        public static Parser Rule_Int32Parser([TokenTypeName("int32")] string _int32) => ParserFactory.Int32;
    }

    public sealed class ResourceCacheStorage : ICacheStorage
    {
        private static readonly ResourceCacheStorage instance = new ResourceCacheStorage();

        private ResourceCacheStorage() { }

        public static ResourceCacheStorage Instance => instance;

        public void Set(byte[] value)
        {
            string path = Path.Combine
            (
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "parseTables.bin"
            );

            File.WriteAllBytes(path, Compress(value));
        }

        public Option<byte[]> TryGet()
        {
            var asm = typeof(ResourceCacheStorage).Assembly;
            using Stream? s = asm.GetManifestResourceStream("AwsAmiTool.parseTables.bin");
            if (s == null) return Option<byte[]>.None;

            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                byte[] data = ms.ToArray();
                return Option<byte[]>.Some(Uncompress(data));
            }
        }

        private static byte[] Compress(byte[] data)
        {
            using var output = new MemoryStream();
            using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
                brotli.Write(data, 0, data.Length);
            return output.ToArray();
        }

        private static byte[] Uncompress(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            brotli.CopyTo(output);
            return output.ToArray();
        }
    }

    public sealed class ParserBuilder
    {
        private static readonly Lazy<ImmutableParserState<object?>> initialState =
            new Lazy<ImmutableParserState<object?>>(GetInitialState, LazyThreadSafetyMode.ExecutionAndPublication);

        private static ImmutableParserState<object?> GetInitialState()
        {
            ReflectionResults rr = ReflectionUtility.BuildParser(typeof(ParserBuilder_Grammar), new TypeSymbol(typeof(ProtoSequenceParser)), ResourceCacheStorage.Instance);
            StaticReflectionResults srr = (StaticReflectionResults)rr;

            return srr.InitialState;
        }

        private readonly ImmutableParserState<object?> state;

        private ParserBuilder(ImmutableParserState<object?> state)
        {
            this.state = state;
        }

        private static readonly ParserBuilder empty = new ParserBuilder(initialState.Value);

        public static ParserBuilder Empty => empty;

        private static void Shift(StrongBox<ImmutableParserState<object?>> state, string tokenType, object tokenValue, string exc)
        {
            var opState = state.Value!.Shift(new NamedSymbol(tokenType), tokenValue);
            if (!opState.HasValue) throw new InvalidOperationException(exc);
            state.Value = opState.Value;
        }

        private static void Shift(StrongBox<ImmutableParserState<object?>> state, Type tokenType, object tokenValue, string exc)
        {
            var opState = state.Value!.Shift(new TypeSymbol(tokenType), tokenValue);
            if (!opState.HasValue) throw new InvalidOperationException(exc);
            state.Value = opState.Value;
        }

        public ParserBuilder ReadLiteral(string literal, StringComparison comparison)
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "read-literal", string.Empty, "Failed to shift read-literal");
            Shift(stateBox, typeof(string), literal, "Failed to shift string argument of read-literal");
            Shift(stateBox, typeof(StringComparison), comparison, "Failed to shift StringComparison argument of read-literal");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder ReadRegex(string pattern, bool ignoreCase)
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "read-regex", string.Empty, "Failed to shift read-regex");
            Shift(stateBox, typeof(string), pattern, "Failed to shift string argument of read-regex");
            Shift(stateBox, typeof(bool), ignoreCase, "Failed to shift bool argument of read-regex");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder AssertEof()
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "assert-eof", string.Empty, "Failed to shift assert-eof");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder Nop()
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "nop", string.Empty, "Failed to shift nop");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder Fail()
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "fail", string.Empty, "Failed to shift fail");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder BeginAlternatives()
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "begin-alternatives", string.Empty, "Failed to shift begin-alternatives");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder Or()
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "or", string.Empty, "Failed to shift or");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder EndAlternatives()
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "end-alternatives", string.Empty, "Failed to shift end-alternatives");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder BeginRepeating(RepetitionType repetitionType)
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "begin-repeating", string.Empty, "Failed to shift repeating");
            Shift(stateBox, typeof(RepetitionType), repetitionType, "Failed to shift RepetitionType argument of repeating");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder EndRepeating()
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "end-repeating", string.Empty, "Failed to shift end-repeating");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder BeginLookahead(LookaheadType lookaheadType)
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "begin-lookahead", string.Empty, "Failed to shift lookahead");
            Shift(stateBox, typeof(LookaheadType), lookaheadType, "Failed to shift LookaheadType argument of lookahead");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder EndLookahead()
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "end-lookahead", string.Empty, "Failed to shift end-lookahead");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder PushLocation()
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "push-location", string.Empty, "Failed to shift push-location");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder Push(object value)
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "push", string.Empty, "Failed to shift push");
            Shift(stateBox, typeof(PushParserPayload), new PushParserPayload(value), "Failed to shift PushParserPayload argument of push");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder PushNew(Func<object> createValue)
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "push-new", string.Empty, "Failed to shift push-new");
            Shift(stateBox, typeof(Func<object>), createValue, "Failed to shift Func<object> argument of push-new");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder Drop()
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "drop", string.Empty, "Failed to shift drop");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder Reduce(int count, Func<ImmutableList<object>, object> reduction)
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "reduce", string.Empty, "Failed to shift reduce");
            Shift(stateBox, typeof(int), count, "Failed to shift int argument of reduce");
            Shift(stateBox, typeof(Func<ImmutableList<object>, object>), reduction, "Failed to shift reduction function");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder BeginOptional()
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "begin-optional", string.Empty, "Failed to shift begin-optional");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder EndOptional()
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "end-optional", string.Empty, "Failed to shift end-optional");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder WhiteSpace()
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "white-space", string.Empty, "Failed to shift white-space");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder OptionalWhiteSpace()
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "optional-white-space", string.Empty, "Failed to shift optional-white-space");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder QuotedString()
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "quoted-string", string.Empty, "Failed to shift quoted-string");
            return new ParserBuilder(stateBox.Value!);
        }

        public ParserBuilder Int32()
        {
            var stateBox = new StrongBox<ImmutableParserState<object?>>(state);
            Shift(stateBox, "int32", string.Empty, "Failed to shift int32");
            return new ParserBuilder(stateBox.Value!);
        }

        public Parser Value
        {
            get
            {
                var opState = state.Shift(EofSymbol.Value, null);
                if (!opState.HasValue) throw new InvalidOperationException("Failed to accept end-of-input");
                var finalState = opState.Value;
                if (finalState.IsAcceptState)
                {
                    ProtoSequenceParser psp = (ProtoSequenceParser)finalState.AcceptedValue!;
                    return psp.ToParser();
                }
                else
                {
                    throw new InvalidOperationException("Parser is not in an accepting state");
                }
            }
        }
    }
}