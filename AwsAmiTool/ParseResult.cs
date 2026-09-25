using System.Collections.Immutable;

namespace AwsAmiTool
{
    public abstract class ParseResult
    {
    }

    public sealed class ParseSuccess : ParseResult
    {
        private readonly ImmutableList<object> stack;
        private readonly ImmutableStringInputStream input;

        public ParseSuccess
        (
            ImmutableList<object> stack,
            ImmutableStringInputStream input
        )
        {
            this.stack = stack;
            this.input = input;
        }

        public ImmutableList<object> Stack => stack;
        public ImmutableStringInputStream Input => input;
    }

    public sealed class ParseFailure : ParseResult
    {
        private static readonly ParseFailure value = new ParseFailure();

        private ParseFailure() { }

        public static ParseFailure Value => value;
    }
}
