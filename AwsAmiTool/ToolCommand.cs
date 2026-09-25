using Amazon;
using Amazon.EC2;
using Amazon.EC2.Model;
using Sunlighter.OptionLib;
using System.Collections.Immutable;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;

namespace AwsAmiTool
{
    public abstract class CommandResult
    {
        public static CommandResult FromHtml(string html) => new StaticCommandResult(html);

        private static readonly CommandResult empty = new StaticCommandResult(string.Empty);
        public static CommandResult Empty => empty;
    }

    public sealed class StaticCommandResult : CommandResult
    {
        private string html;

        public StaticCommandResult(string html)
        {
            this.html = html;
        }

        public string Html => html;
    }

    public sealed class DynamicCommandResult : CommandResult
    {
        private Func<int> getCurrentTime;
        private Func<string> getHtml;
        private Func<HttpListenerContext, Task> callback;

        public DynamicCommandResult
        (
            Func<int> getCurrentTime,
            Func<string> getHtml,
            Func<HttpListenerContext, Task> callback
        )
        {
            this.getCurrentTime = getCurrentTime;
            this.getHtml = getHtml;
            this.callback = callback;
        }

        public Func<int> GetCurrentTime => getCurrentTime;

        public Func<string> GetHtml => getHtml;

        public Func<HttpListenerContext, Task> Callback => callback;
    }

    public sealed class ToolState
    {
        private static readonly Lazy<ImmutableSortedDictionary<string, RegionEndpoint>> allRegions =
            new Lazy<ImmutableSortedDictionary<string, RegionEndpoint>>
            (
                GetAllRegions,
                LazyThreadSafetyMode.ExecutionAndPublication
            );

        private static ImmutableSortedDictionary<string, RegionEndpoint> GetAllRegions()
        {
            return Amazon.RegionEndpoint.EnumerableAllRegions.Where(x => x.SystemName is not null).ToImmutableSortedDictionary(x => x.SystemName, x => x);
        }

        public static ImmutableSortedDictionary<string, RegionEndpoint> AllRegions => allRegions.Value;

        public Option<RegionEndpoint> RegionEndpoint { get; set; } = Option<RegionEndpoint>.None;

        public ImmutableList<Image> Images { get; set; } = ImmutableList<Image>.Empty;

        public ImmutableSortedDictionary<string, int> IdToIndex { get; set; } = ImmutableSortedDictionary<string, int>.Empty;

        public ImmutableSortedDictionary<int, int> QuickToIndex { get; set; } = ImmutableSortedDictionary<int, int>.Empty;

        public ImmutableSortedDictionary<int, int> IndexToQuick { get; set; } = ImmutableSortedDictionary<int, int>.Empty;

        public ImmutableSortedDictionary<string, ImmutableList<int>> Groups { get; set; } =
            ImmutableSortedDictionary<string, ImmutableList<int>>.Empty;

        public ImmutableSortedSet<string> SelectedIds { get; set; } = ImmutableSortedSet<string>.Empty;

        public string GetImageGroups()
        {
            ImmutableSortedSet<string> deletedIds = DeletedIds.GetSynchronous();
            ImmutableSortedSet<string> faultedIds = FaultedIds.GetSynchronous().Keys.ToImmutableSortedSet();

            if (Groups.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("<ul>");
                foreach (KeyValuePair<string, ImmutableList<int>> kvp in Groups)
                {
                    string prefix = kvp.Key;
                    sb.AppendLine($"<li><b>{HttpUtility.HtmlEncode(prefix)}</b>");
                    sb.AppendLine($"<ul>");
                    foreach (int index in kvp.Value)
                    {
                        Image image = Images[index];
                        int quickRef = IndexToQuick[index];

                        string span = string.Empty;
                        string endSpan = "</span>";

                        string getColor(string imageId)
                        {
                            if (deletedIds.Contains(imageId))
                            {
                                return "#FFC0C0";
                            }
                            else if (faultedIds.Contains(imageId))
                            {
                                return "#008000";
                            }
                            else if (SelectedIds.Contains(imageId))
                            {
                                return "red";
                            }
                            else
                            {
                                return string.Empty;
                            }
                        }

                        string color = getColor(image.ImageId);
                        if (!string.IsNullOrEmpty(color))
                        {
                            span = $"<span id=\"{HttpUtility.HtmlAttributeEncode(image.ImageId)}\" style=\"color: {HttpUtility.HtmlAttributeEncode(color)};\">";
                        }
                        else
                        {
                            span = $"<span id=\"{HttpUtility.HtmlAttributeEncode(image.ImageId)}\">";
                        }

                        sb.AppendLine($"<li>[{quickRef}] <b>{span}{HttpUtility.HtmlEncode(image.Name)}{endSpan}</b> &mdash; {HttpUtility.HtmlEncode(image.ImageId)} &mdash; {HttpUtility.HtmlEncode(image.CreationDate)}");

                        ImmutableList<BlockDeviceMapping> bdmList = image.BlockDeviceMappings.Where(b => b.Ebs != null && !string.IsNullOrEmpty(b.Ebs.SnapshotId)).ToImmutableList();
                        if (bdmList.Count > 0)
                        {
                            sb.Append(" [");
                            bool bdmNeedDelim = false;
                            foreach(BlockDeviceMapping bdm in bdmList)
                            {
                                if (bdmNeedDelim)
                                {
                                    sb.Append(" &mdash; ");
                                }
                                bdmNeedDelim = true;
                                sb.Append("<span id=\"");
                                sb.Append(HttpUtility.HtmlAttributeEncode(bdm.Ebs.SnapshotId));
                                sb.Append('"');
                                string bdmColor = getColor(bdm.Ebs.SnapshotId);
                                if (!string.IsNullOrEmpty(bdmColor))
                                {
                                    sb.Append(" style=\"color: ");
                                    sb.Append(HttpUtility.HtmlAttributeEncode(bdmColor));
                                    sb.Append(";\"");
                                }
                                sb.Append(">");
                                sb.Append(HttpUtility.HtmlEncode(bdm.DeviceName ?? string.Empty));
                                sb.AppendLine("</span>");
                            }
                            sb.Append(']');
                        }

                        sb.AppendLine($"</li>");
                    }
                    sb.AppendLine($"</ul></li>");
                }

                sb.AppendLine("</ul>");
                return sb.ToString();
            }
            else
            {
                return "<p>Image list is empty</p>";
            }
        }

        public Option<Task> DeletionTask { get; set; } = Option<Task>.None;

        public AsyncLockedBox<ImmutableSortedSet<string>> DeletedIds { get; set; } =
            new AsyncLockedBox<ImmutableSortedSet<string>>(ImmutableSortedSet<string>.Empty);

        public AsyncLockedBox<ImmutableSortedDictionary<string, Exception>> FaultedIds { get; set; } =
            new AsyncLockedBox<ImmutableSortedDictionary<string, Exception>>(ImmutableSortedDictionary<string, Exception>.Empty);

        public JsonCallbackQueue<PollMessage> PollQueue { get; set; } = new JsonCallbackQueue<PollMessage>(pm => pm.ToJsonNode(), PM_EOF.Value);
    }

    public abstract class ToolCommand
    {
        /// <summary>
        /// Returns an HTML string; may modify program state
        /// </summary>
        public abstract Task<CommandResult> Run(ToolState state, Action<ToolState> setNewState);

        private static readonly Lazy<Parser> parser =
            new Lazy<Parser>(GetParser, LazyThreadSafetyMode.ExecutionAndPublication);

        private static Parser GetParser()
        {
            var descendants = typeof(ToolCommand).Assembly.GetTypes().Where(x => x.IsAssignableTo(typeof(ToolCommand)) && !x.IsAbstract);

            ImmutableList<Action<StrongBox<ParserBuilder>>> addParserMethods =
                ImmutableList<Action<StrongBox<ParserBuilder>>>.Empty;

            foreach (Type desc in descendants)
            {
                MethodInfo? mAddParser = desc.GetMethod
                (
                    "AddParser",
                    BindingFlags.Public | BindingFlags.Static,
                    [ typeof(StrongBox<ParserBuilder>) ]
                );

                if (mAddParser != null)
                {
                    Action<StrongBox<ParserBuilder>> action = (StrongBox<ParserBuilder> pb) =>
                    {
                        mAddParser.Invoke(null, [pb]);
                    };

                    addParserMethods = addParserMethods.Add(action);
                }
            }

            if (addParserMethods.IsEmpty)
            {
                throw new InvalidOperationException("No command parsers found");
            }
            else if (addParserMethods.Count == 1)
            {
                StrongBox<ParserBuilder> pb2 = new StrongBox<ParserBuilder>(ParserBuilder.Empty);
                addParserMethods[0](pb2);
                return pb2.Value!.Value;
            }
            else
            {
                StrongBox<ParserBuilder> pb3 = new StrongBox<ParserBuilder>
                (
                    ParserBuilder.Empty
                    .BeginAlternatives()
                );

                bool needDelim = false;

                foreach (Action<StrongBox<ParserBuilder>> action in addParserMethods)
                {
                    if (needDelim)
                    {
                        pb3.Value = pb3.Value!.Or();
                    }
                    needDelim = true;
                    action(pb3);
                }

                return pb3.Value!.EndAlternatives().Value;
            }
        }

        public static Parser Parser
        {
            get
            {
                return parser.Value;
            }
        }
    }

    public sealed class TC_ListRegions : ToolCommand
    {
        private static readonly TC_ListRegions value = new TC_ListRegions();

        private TC_ListRegions() { }

        public static TC_ListRegions Value => value;

        public override async Task<CommandResult> Run(ToolState state, Action<ToolState> setNewState)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<ul>");
            foreach(KeyValuePair<string, RegionEndpoint> kvp in ToolState.AllRegions)
            {
                sb.AppendLine($"<li><b>{HttpUtility.HtmlEncode(kvp.Key)}</b> &mdash; {HttpUtility.HtmlEncode(kvp.Value.DisplayName)}</li>");
            }
            sb.AppendLine("</ul>");

            string currentRegion = state.RegionEndpoint.HasValue ? state.RegionEndpoint.Value.DisplayName : "(None)";
            sb.AppendLine($"<p>Current Region: <b>{HttpUtility.HtmlEncode(currentRegion)}</b></p>");
            return CommandResult.FromHtml(sb.ToString());
        }

        public static void AddParser(StrongBox<ParserBuilder> pbBox)
        {
            ParserBuilder pb = pbBox.Value!;

            pb = pb.ReadLiteral("list-regions", StringComparison.OrdinalIgnoreCase)
                .Drop()
                .Push(TC_ListRegions.Value);

            pbBox.Value = pb;
        }
    }

    public sealed class TC_SetRegion : ToolCommand
    {
        private readonly string newRegion;

        public TC_SetRegion(string newRegion)
        {
            this.newRegion = newRegion;
        }

        public override async Task<CommandResult> Run(ToolState state, Action<ToolState> setNewState)
        {
            if (ToolState.AllRegions.TryGetValue(newRegion, out RegionEndpoint? regionEndpoint))
            {
                state.RegionEndpoint = Option<RegionEndpoint>.Some(regionEndpoint);
                string html = $"<p>Region set to <b>{HttpUtility.HtmlEncode(regionEndpoint.DisplayName)}</b></p>";
                return CommandResult.FromHtml(html);
            }
            else
            {
                string html = $"<p>Error: Region <b>{HttpUtility.HtmlEncode(newRegion)}</b> not found.</p>";
                return CommandResult.FromHtml(html);
            }
        }

        public static void AddParser(StrongBox<ParserBuilder> pbBox)
        {
            ParserBuilder pb = pbBox.Value!;

            pb = pb.ReadLiteral("set-region", StringComparison.OrdinalIgnoreCase)
                .Drop()
                .WhiteSpace()
                .QuotedString()
                .Reduce
                (
                    1,
                    stack =>
                    {
                        return new TC_SetRegion((string)stack[0]);
                    }
                );

            pbBox.Value = pb;
        }
    }

    public sealed class TC_GetImages : ToolCommand
    {
        private static readonly TC_GetImages value = new TC_GetImages();

        private TC_GetImages() { }

        public static TC_GetImages Value => value;

        public override async Task<CommandResult> Run(ToolState state, Action<ToolState> setNewState)
        {
            if (state.RegionEndpoint.HasValue)
            {
                using AmazonEC2Client ec2 = new AmazonEC2Client(state.RegionEndpoint.Value);

                DescribeImagesRequest q = new DescribeImagesRequest()
                {
                    Owners = ["self"],
                };
                DescribeImagesResponse a = await ec2.DescribeImagesAsync(q);

                state.Images = a.Images.Where(i => string.Compare(i.GetTag("group"), "linuxdev", StringComparison.InvariantCultureIgnoreCase) == 0).ToImmutableList();

                state.IdToIndex = Enumerable.Range(0, state.Images.Count)
                    .Select(idx => (state.Images[idx].ImageId, idx))
                    .ToImmutableSortedDictionary(x => x.ImageId, x => x.idx);

                ImmutableSortedSet<string> prefixes = state.Images.Select(ii => ii.Name.UpToUnderscore()).Distinct().OrderBy(x => x).ToImmutableSortedSet();

                state.QuickToIndex = state.QuickToIndex.Clear();
                state.IndexToQuick = state.IndexToQuick.Clear();

                int pos = 0;
                void addQuickRef(Image image)
                {
                    int index = state.IdToIndex[image.ImageId];
                    state.QuickToIndex = state.QuickToIndex.Add(pos, index);
                    state.IndexToQuick = state.IndexToQuick.Add(index, pos);
                    ++pos;
                }

                ImmutableSortedDictionary<int, string> longestMatchingPrefix =
                    ImmutableSortedDictionary<int, string>.Empty;

                foreach(int index in Enumerable.Range(0, state.Images.Count))
                {
                    Image image = state.Images[index];
                    foreach(string prefix in prefixes)
                    {
                        if (image.Name.StartsWith(prefix, StringComparison.InvariantCultureIgnoreCase))
                        {
                            if (!longestMatchingPrefix.TryGetValue(index, out string? existingPrefix) || prefix.Length > existingPrefix.Length)
                            {
                                longestMatchingPrefix = longestMatchingPrefix.SetItem(index, prefix);
                            }
                        }
                    }
                }

                if (prefixes.Count > 0)
                {
                    state.Groups = state.Groups.Clear();

                    foreach (string prefix in prefixes)
                    {
                        IEnumerable<Image> imagesWithPrefix =
                            Enumerable.Range(0, state.Images.Count)
                            .Where(idx => string.Compare(longestMatchingPrefix[idx], prefix, StringComparison.OrdinalIgnoreCase) == 0)
                            .Select(idx => state.Images[idx])
                            .OrderByDescending(i => i.CreationDate);

                        foreach (Image image in imagesWithPrefix)
                        {
                            addQuickRef(image);
                            state.Groups = state.Groups.SetItem(prefix, state.Groups.GetValueOrDefault(prefix, ImmutableList<int>.Empty).Add(state.IdToIndex[image.ImageId]));
                        }
                    }
                }

                state.SelectedIds = state.SelectedIds.Clear();

                foreach(KeyValuePair<string, ImmutableList<int>> kvp in state.Groups)
                {
                    foreach(int index in kvp.Value.Skip(1))
                    {
                        state.SelectedIds = state.SelectedIds.Add(state.Images[index].ImageId);
                    }
                }

                string html = state.GetImageGroups();
                return CommandResult.FromHtml(html);
            }
            else
            {
                string html = "<p>Error: region not set.</p>";
                return CommandResult.FromHtml(html);
            }
        }

        public static void AddParser(StrongBox<ParserBuilder> pbBox)
        {
            ParserBuilder pb = pbBox.Value!;

            pb = pb.ReadLiteral("get-images", StringComparison.OrdinalIgnoreCase)
                .Drop()
                .Push(TC_GetImages.Value);

            pbBox.Value = pb;
        }
    }

    public sealed class TC_Select : ToolCommand
    {
        private readonly bool select;
        private readonly ImmutableSortedSet<int> quickRefs;

        public TC_Select(bool select, ImmutableSortedSet<int> quickRefs)
        {
            this.select = select;
            this.quickRefs = quickRefs;
        }

        public bool Select => select;
        public ImmutableSortedSet<int> QuickRefs => quickRefs;

        public override Task<CommandResult> Run(ToolState state, Action<ToolState> setNewState)
        {
            if (select)
            {
                foreach(int qr in quickRefs)
                {
                    state.SelectedIds = state.SelectedIds.Add(state.Images[state.QuickToIndex[qr]].ImageId);
                }
            }
            else
            {
                foreach (int qr in quickRefs)
                {
                    state.SelectedIds = state.SelectedIds.Remove(state.Images[state.QuickToIndex[qr]].ImageId);
                }
            }

            string html = state.GetImageGroups();
            return Task.FromResult(CommandResult.FromHtml(html));
        }

        public static void AddParser(StrongBox<ParserBuilder> pbBox)
        {
            ParserBuilder pb = pbBox.Value!;

            pb = pb.BeginAlternatives()
                .ReadLiteral("select", StringComparison.OrdinalIgnoreCase)
                .Drop()
                .Push(true)
                .Or()
                .ReadLiteral("deselect", StringComparison.OrdinalIgnoreCase)
                .Drop()
                .Push(false)
                .EndAlternatives()
                .WhiteSpace()
                .Int32()
                .Reduce
                (
                    1,
                    stack =>
                    {
                        return ImmutableSortedSet<int>.Empty.Add((int)stack[0]);
                    }
                )
                .BeginRepeating(RepetitionType.ZeroOrMore)
                .OptionalWhiteSpace()
                .ReadLiteral(",", StringComparison.Ordinal)
                .Drop()
                .OptionalWhiteSpace()
                .Int32()
                .Reduce
                (
                    2,
                    stack =>
                    {
                        ImmutableSortedSet<int> set = (ImmutableSortedSet<int>)stack[0];
                        int newValue = (int)stack[1];
                        return set.Add(newValue);
                    }
                )
                .EndRepeating()
                .Reduce
                (
                    2,
                    stack =>
                    {
                        bool select = (bool)stack[0];
                        ImmutableSortedSet<int> quickRefs = (ImmutableSortedSet<int>)stack[1];
                        return new TC_Select(select, quickRefs);
                    }
                );

            pbBox.Value = pb;
        }
    }

    public sealed class JsonCallbackQueue<T>
    {
        private readonly object syncRoot;
        private ImmutableList<T> items;
        private bool eofAfterItems;
        private bool isWaiting;
        private readonly Func<T, JsonNode> format;
        private readonly T eofValue;

        public JsonCallbackQueue(Func<T, JsonNode> format, T eofValue)
        {
            this.format = format;
            syncRoot = new object();
            items = ImmutableList<T>.Empty;
            eofAfterItems = false;
            isWaiting = false;
            this.eofValue = eofValue;
        }

        public void Post(T item)
        {
            lock(syncRoot)
            {
                items = items.Add(item);
                if (isWaiting)
                {
                    isWaiting = false;
                    Monitor.PulseAll(syncRoot);
                }
            }
        }

        public void PostEof()
        {
            lock(syncRoot)
            {
                eofAfterItems = true;
                if (isWaiting)
                {
                    isWaiting = false;
                    Monitor.PulseAll(syncRoot);
                }

            }
        }

        public int GetCurrentTime()
        {
            lock(syncRoot)
            {
                return items.Count;
            }
        }

        public async Task HandleCallback(HttpListenerContext context)
        {
            ImmutableSortedDictionary<string, ImmutableList<string?>> queryParams = context.Request.QueryString.ToImmutableSortedDictionary();

            int after = 0; queryParams.TryGetInt32("after", i => { after = i; });

            ImmutableList<T> localItems = ImmutableList<T>.Empty;
            bool localEofAfterItems = false;

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            ThreadPool.QueueUserWorkItem
            (
                _ =>
                {
                    lock (syncRoot)
                    {
                        long desiredWakeup = 30000 + Environment.TickCount64;

                        while (items.Count <= after && !eofAfterItems && Environment.TickCount64 < desiredWakeup)
                        {
                            isWaiting = true;
                            
                            Monitor.Wait(syncRoot, Math.Max(0, (int)(desiredWakeup - Environment.TickCount64)));
                        }

                        localItems = items;
                        localEofAfterItems = eofAfterItems;
                    }
                    tcs.PostResult(true);
                }
            );

            await tcs.Task;

            ImmutableList<JsonNode> formattedItems = localItems.Skip(after).Select(format).ToImmutableList();
            if (formattedItems.IsEmpty && localEofAfterItems) formattedItems = [ format(eofValue) ];

            JsonArray a = new JsonArray(formattedItems.ToArray());

            context.Response.ContentType = System.Net.Mime.MediaTypeNames.Application.Json;
            context.Response.ContentEncoding = Encoding.UTF8;

            context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Expires"] = "0";

            using Utf8JsonWriter sw = new Utf8JsonWriter(context.Response.OutputStream, new JsonWriterOptions { Indented = true });
            a.WriteTo(sw);
            await sw.FlushAsync();
        }
    }

    public abstract class PollMessage
    {
        public abstract JsonNode ToJsonNode();
    }

    public sealed class PM_CompleteItem : PollMessage
    {
        public readonly string itemId;

        public PM_CompleteItem(string itemId)
        {
            this.itemId = itemId;
        }

        public string ItemID => itemId;

        public override JsonNode ToJsonNode()
        {
            JsonObject obj = new JsonObject();
            obj["complete"] = itemId;
            return obj;
        }
    }

    public sealed class PM_FaultItem : PollMessage
    {
        public readonly string itemId;

        public PM_FaultItem(string itemId)
        {
            this.itemId = itemId;
        }

        public override JsonNode ToJsonNode()
        {
            JsonObject obj = new JsonObject();
            obj["fault"] = itemId;
            return obj;
        }
    }

    public sealed class PM_EOF : PollMessage
    {
        private static readonly PM_EOF value = new PM_EOF();
        private PM_EOF() { }
        public static PM_EOF Value => value;

        public override JsonNode ToJsonNode()
        {
            JsonObject obj = new JsonObject();
            obj["eof"] = true;
            return obj;
        }
    }

    /// <summary>
    /// I am not sure I even need this class... atomics would do the job? ...
    /// </summary>
    public sealed class AsyncLockedBox<T>
    {
        private readonly Lock syncRoot;
        private T value;

        public AsyncLockedBox(T initialValue)
        {
            this.syncRoot = new Lock();
            this.value = initialValue;
        }

        public Task<T> Get()
        {
            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();
            ThreadPool.QueueUserWorkItem
            (
                _ =>
                {
                    lock (syncRoot)
                    {
                        tcs.PostResult(value);
                    }
                }
            );
            return tcs.Task;
        }

        public T GetSynchronous()
        {
            lock (syncRoot)
            {
                return value;
            }
        }

        public Task Set(T newValue)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            ThreadPool.QueueUserWorkItem
            (
                _ =>
                {
                    lock (syncRoot)
                    {
                        value = newValue;
                        tcs.PostResult(true);
                    }
                }
            );
            return tcs.Task;
        }
    }

    public sealed class TC_ShowTaskStatus : ToolCommand
    {
        private static readonly TC_ShowTaskStatus value = new TC_ShowTaskStatus();
        private TC_ShowTaskStatus() { }
        public static TC_ShowTaskStatus Value => value;

        public override async Task<CommandResult> Run(ToolState state, Action<ToolState> setNewState)
        {
            if (state.DeletionTask.HasValue && state.DeletionTask.Value.Status.IsNotDone())
            {
                return new DynamicCommandResult
                (
                    () => state.PollQueue.GetCurrentTime(),
                    () => state.GetImageGroups(),
                    state.PollQueue.HandleCallback
                );
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("<p>Deletion task: ");
                if (state.DeletionTask.HasValue)
                {
                    sb.Append($"<b>{state.DeletionTask.Value.Status}</b>");
                }
                else
                {
                    sb.Append("<b>does not exist</b>");
                }
                sb.Append($"&mdash; {(await state.DeletedIds.Get()).Count} deleted, {(await state.FaultedIds.Get()).Count} faulted");
                sb.AppendLine("</p>");
                return CommandResult.FromHtml(sb.ToString());
            }
        }

        public static void AddParser(StrongBox<ParserBuilder> pbBox)
        {
            ParserBuilder pb = pbBox.Value!;
            pb = pb.ReadLiteral("show-task-status", StringComparison.OrdinalIgnoreCase)
                .Drop()
                .Push(TC_ShowTaskStatus.Value);
            pbBox.Value = pb;
        }
    }

    public sealed class TC_StartDeletion : ToolCommand
    {
        private static readonly TC_StartDeletion value = new TC_StartDeletion();
        private TC_StartDeletion() { }
        public static TC_StartDeletion Value => value;

        public override async Task<CommandResult> Run(ToolState state, Action<ToolState> setNewState)
        {
            if (state.DeletionTask.HasValue && state.DeletionTask.Value.Status.IsNotDone())
            {
                string html = "<p>Deletion task already exists</p>";
                return CommandResult.FromHtml(html);
            }
            else if (!state.RegionEndpoint.HasValue)
            {
                return CommandResult.FromHtml("<p>Region not set</p>");
            }
            else
            {
                state.DeletedIds = new AsyncLockedBox<ImmutableSortedSet<string>>(ImmutableSortedSet<string>.Empty);
                state.PollQueue = new JsonCallbackQueue<PollMessage>(pm => pm.ToJsonNode(), PM_EOF.Value);

                Task deletionTask = Task.Run
                (
                    () => DeleteSelected
                    (
                        () => new AmazonEC2Client(state.RegionEndpoint.Value),
                        state.Images, state.IdToIndex, state.SelectedIds, state.DeletedIds, state.FaultedIds, state.PollQueue
                    )
                );

                state.DeletionTask = Option<Task>.Some(deletionTask);

                return new DynamicCommandResult
                (
                    () => state.PollQueue.GetCurrentTime(),
                    () => state.GetImageGroups(),
                    state.PollQueue.HandleCallback
                );
            }
        }

        public static void AddParser(StrongBox<ParserBuilder> pbBox)
        {
            ParserBuilder pb = pbBox.Value!;
            pb = pb.ReadLiteral("start-deletion", StringComparison.OrdinalIgnoreCase)
                .Drop()
                .Push(TC_StartDeletion.Value);
            pbBox.Value = pb;
        }

        public static async Task DeleteSelected
        (
            Func<AmazonEC2Client> makeEc2Client,
            ImmutableList<Image> images,
            ImmutableSortedDictionary<string, int> idToIndex,
            ImmutableSortedSet<string> selectedIds,
            AsyncLockedBox<ImmutableSortedSet<string>> deletedIds,
            AsyncLockedBox<ImmutableSortedDictionary<string, Exception>> faultedIds,
            JsonCallbackQueue<PollMessage> pollQueue
        )
        {
            using AmazonEC2Client ec2 = makeEc2Client();

            ImmutableSortedDictionary<long, ImmutableList<Func<Task>>> actionQueue = ImmutableSortedDictionary<long, ImmutableList<Func<Task>>>.Empty;

            void schedule(long time, Func<Task> task)
            {
                actionQueue = actionQueue.SetItem(time, actionQueue.GetValueOrDefault(time, ImmutableList<Func<Task>>.Empty).Add(task));
            }

            Lock randomSyncRoot = new Lock();
            Random r = new Random();

#if false
            int nextRandom(int maxValue)
            {
                lock(randomSyncRoot)
                {
                    return r.Next(maxValue);
                }
            }
#endif

            async Task doDelete(string id, ImmutableList<string> continuation)
            {
                if (idToIndex.TryGetValue(id, out int index))
                {
                    // it's an AMI

                    Image image = images[index];

                    ImmutableList<string> bdmList = image.BlockDeviceMappings
                        .Where(b => b.Ebs != null && !string.IsNullOrEmpty(b.Ebs.SnapshotId))
                        .Select(b => b.Ebs.SnapshotId).ToImmutableList();

                    try
                    {
                        //await Task.Delay(250 + nextRandom(250));
                        //if (nextRandom(10) == 1) throw new InvalidOperationException();

                        await ec2.DeregisterImage(id);

                        pollQueue.Post(new PM_CompleteItem(id));
                        await faultedIds.Set((await faultedIds.Get()).Remove(id));
                        await deletedIds.Set((await deletedIds.Get()).Add(id));

                        if (bdmList.Count > 0)
                        {
                            schedule(Environment.TickCount64 + 30000, () => doDelete(bdmList[0], bdmList.RemoveAt(0)));
                        }
                    }
                    catch(Exception exc)
                    {
                        pollQueue.Post(new PM_FaultItem(id));
                        await faultedIds.Set((await faultedIds.Get()).SetItem(id, exc));
                    }
                }
                else
                {
                    try
                    {
                        //await Task.Delay(250 + nextRandom(250));
                        //if (nextRandom(10) == 1) throw new InvalidOperationException();

                        await ec2.DeleteSnapshot(id);

                        pollQueue.Post(new PM_CompleteItem(id));
                        await faultedIds.Set((await faultedIds.Get()).Remove(id));
                        await deletedIds.Set((await deletedIds.Get()).Add(id));
                    }
                    catch(Exception exc)
                    {
                        pollQueue.Post(new PM_FaultItem(id));
                        await faultedIds.Set((await faultedIds.Get()).SetItem(id, exc));
                    }
                }

                if (continuation.Count > 0)
                {
                    schedule(Environment.TickCount64, () => doDelete(continuation[0], continuation.RemoveAt(0)));
                }
            }

            async Task scheduleDeletes(ImmutableSortedSet<string> selectedIds)
            {
                if (selectedIds.Count > 0)
                {
                    string id = selectedIds.First();
                    schedule(Environment.TickCount64, () => doDelete(id, selectedIds.Remove(id).ToImmutableList()));
                }
            }

            actionQueue = actionQueue.Add(Environment.TickCount64, [ () => scheduleDeletes(selectedIds) ]);

            while(actionQueue.Count > 0)
            {
                KeyValuePair<long, ImmutableList<Func<Task>>> first = actionQueue.First();
                if (first.Key > Environment.TickCount64)
                {
                    await Task.Delay(Math.Max(0, (int)(first.Key - Environment.TickCount64)));
                }
                else
                {
                    actionQueue = actionQueue.Remove(first.Key);
                    foreach (Func<Task> action in first.Value)
                    {
                        await action();
                    }
                }
            }

            pollQueue.PostEof();
        }
    }

    public sealed class TC_RetryFaulted : ToolCommand
    {
        private static readonly TC_RetryFaulted value = new TC_RetryFaulted();
        private TC_RetryFaulted() { }
        public static TC_RetryFaulted Value => value;

        public override async Task<CommandResult> Run(ToolState state, Action<ToolState> setNewState)
        {
            if (state.DeletionTask.HasValue && state.DeletionTask.Value.Status.IsNotDone())
            {
                string html = "<p>Deletion task already exists</p>";
                return CommandResult.FromHtml(html);
            }
            else if (!state.RegionEndpoint.HasValue)
            {
                return CommandResult.FromHtml("<p>Region not set</p>");
            }
            else
            {
                state.PollQueue = new JsonCallbackQueue<PollMessage>(pm => pm.ToJsonNode(), PM_EOF.Value);

                Task deletionTask = Task.Run
                (
                    () => TC_StartDeletion.DeleteSelected
                    (
                        () => new AmazonEC2Client(state.RegionEndpoint.Value),
                        state.Images, state.IdToIndex,
                        state.FaultedIds.GetSynchronous().Keys.ToImmutableSortedSet(),
                        state.DeletedIds, state.FaultedIds, state.PollQueue
                    )
                );

                state.DeletionTask = Option<Task>.Some(deletionTask);

                return new DynamicCommandResult
                (
                    () => state.PollQueue.GetCurrentTime(),
                    () => state.GetImageGroups(),
                    state.PollQueue.HandleCallback
                );
            }
        }

        public static void AddParser(StrongBox<ParserBuilder> pbBox)
        {
            ParserBuilder pb = pbBox.Value!;
            pb = pb.ReadLiteral("retry-faulted", StringComparison.OrdinalIgnoreCase)
                .Drop()
                .Push(TC_RetryFaulted.Value);
            pbBox.Value = pb;
        }
    }

    public static partial class Utility
    {
        public static string GetTag(this Image i, string tagName)
        {
            Tag? t1 = i.Tags.FirstOrDefault(t => string.Compare(t.Key, tagName, StringComparison.InvariantCultureIgnoreCase) == 0);

            if (t1 == null) return "";
            return t1.Value;
        }

        public static string UpToUnderscore(this string str)
        {
            int i = str.IndexOf('_');
            if (i > 0) return str.Substring(0, i);
            else return str;
        }

        public static void PostResult(this TaskCompletionSource tcs)
        {
            ThreadPool.QueueUserWorkItem
            (
                _ =>
                {
                    tcs.SetResult();
                }
            );
        }

        public static void PostResult<T>(this TaskCompletionSource<T> tcs, T result)
        {
            ThreadPool.QueueUserWorkItem
            (
                _ =>
                {
                    tcs.SetResult(result);
                }
            );
        }

        public static bool IsNotDone(this TaskStatus ts)
        {
            return ts == TaskStatus.Created || ts == TaskStatus.WaitingForActivation || ts == TaskStatus.WaitingToRun || ts == TaskStatus.Running || ts == TaskStatus.WaitingForChildrenToComplete;
        }


        public static async Task<DeleteSnapshotResponse> DeleteSnapshot(this AmazonEC2Client ec2, string snapshotId)
        {
            DeleteSnapshotRequest q3 = new DeleteSnapshotRequest()
            {
                SnapshotId = snapshotId,
            };

            return await ec2.DeleteSnapshotAsync(q3);
        }

        public static async Task<DeregisterImageResponse> DeregisterImage(this AmazonEC2Client ec2, string imageId)
        {
            DeregisterImageRequest q2 = new DeregisterImageRequest()
            {
                ImageId = imageId,
            };

            return await ec2.DeregisterImageAsync(q2);
        }
    }
}
