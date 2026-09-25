using System.Collections.Immutable;
using System.Text;

namespace AwsAmiTool
{
    public sealed class ImmutableStringBuilder
    {
        private static readonly ImmutableStringBuilder empty = new ImmutableStringBuilder(ImmutableList<Part>.Empty, 0);

        private readonly ImmutableList<Part> parts;
        private readonly int len;

        private string? cachedValue;

        private ImmutableStringBuilder(ImmutableList<Part> parts, int len)
        {
            this.parts = parts;
            this.len = len;

            this.cachedValue = null;
        }

        public static ImmutableStringBuilder Empty => empty;

        private abstract class Part
        {

        }

        private sealed class StringPart : Part
        {
            private readonly string str;

            public StringPart(string str)
            {
                this.str = str;
            }

            public string Value => str;
        }

        private sealed class CharPart : Part
        {
            private readonly char ch;

            public CharPart(char ch)
            {
                this.ch = ch;
            }

            public char Value => ch;
        }

        public ImmutableStringBuilder Append(string str)
        {
            ArgumentNullException.ThrowIfNull(str, nameof(str));

            if (str.Length == 0)
            {
                return this;
            }

            var newParts = parts.Add(new StringPart(str));
            var newLen = len + str.Length;
            return new ImmutableStringBuilder(newParts, newLen);
        }

        public ImmutableStringBuilder Append(char ch)
        {
            var newParts = parts.Add(new CharPart(ch));
            var newLen = len + 1;
            return new ImmutableStringBuilder(newParts, newLen);
        }

        public string Value
        {
            get
            {
                if (cachedValue is null)
                {
                    var sb = new StringBuilder(len);
                    foreach (var part in parts)
                    {
                        switch (part)
                        {
                            case StringPart stringPart:
                                sb.Append(stringPart.Value);
                                break;
                            case CharPart charPart:
                                sb.Append(charPart.Value);
                                break;
                        }
                    }
                    cachedValue = sb.ToString();
                }

                return cachedValue;
            }
        }
    }
}
