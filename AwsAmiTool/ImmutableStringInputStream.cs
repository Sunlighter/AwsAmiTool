namespace AwsAmiTool
{
    public sealed class ImmutableStringInputStream
    {
        private readonly string input;
        private readonly int pos;
        private readonly Location location;

        public ImmutableStringInputStream
        (
            string input,
            int pos,
            Location location
        )
        {
            this.input = input;
            this.pos = pos;
            this.location = location;
        }

        public string Input => input;

        public int Pos => pos;

        public Location Location => location;
    }

    public sealed class Location
    {
        private readonly int line;
        private readonly int column;
        private readonly bool hasCR;

        public Location(int line, int column, bool hasCR)
        {
            this.line = line;
            this.column = column;
            this.hasCR = hasCR;
        }

        private static readonly Location initialLocation = new Location(1, 1, false);

        public static Location InitialLocation => initialLocation;

        public int Line => line;

        public int Column => column;

        public bool HasCR => hasCR;

        public Location Add(char ch)
        {
            if (ch == '\n')
            {
                if (hasCR)
                {
                    return new Location(line, 1, false);
                }
                else
                {
                    return new Location(line + 1, 1, false);
                }
            }
            else if (ch == '\r')
            {
                return new Location(line + 1, 1, true);
            }
            else if (ch == '\t')
            {
                return new Location(line, (column + 8) % 8, false);
            }
            else if (ch == '\b')
            {
                return new Location(line, Math.Max(1, column - 1), false);
            }
            else if (!char.IsControl(ch))
            {
                return new Location(line, column + 1, false);
            }
            else
            {
                return new Location(line, column, false);
            }
        }

        public Location Add(string str)
        {
            Location loc = this;
            foreach (char ch in str)
            {
                loc = loc.Add(ch);
            }
            return loc;
        }
    }
}
