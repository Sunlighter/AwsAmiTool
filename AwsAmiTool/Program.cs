using Amazon.EC2.Model;
using Sunlighter.OptionLib;
using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Web;

namespace AwsAmiTool
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            CommandResult commandResult = CommandResult.Empty;

            ToolState toolState = new ToolState();

            using (HttpListener h = new HttpListener())
            {
                string prefix = "http://127.0.0.1:9697/";

                Console.WriteLine(prefix);

                using System.Diagnostics.Process p = new System.Diagnostics.Process();
                p.StartInfo.UseShellExecute = true;
                p.StartInfo.FileName = prefix;
                p.Start();

                h.Prefixes.Add(prefix);
                h.Start();

                while (true)
                {
                    try
                    {
                        HttpListenerContext htc = await h.GetContextAsync();

                        string urlString = htc.Request.Url?.ToString() ?? string.Empty;
                        string postfix;
                        ImmutableSortedDictionary<string, ImmutableList<string?>> queryParams;

                        if (urlString.StartsWith(prefix))
                        {
                            postfix = urlString.Substring(prefix.Length);
                        }
                        else
                        {
                            postfix = urlString;
                        }

                        Console.WriteLine($"{htc.Request.HttpMethod} {postfix}");

                        {
                            int splitPoint = postfix.IndexOf('?');
                            if (splitPoint >= 0 && splitPoint < postfix.Length)
                            {
                                string queryString = postfix.Substring(splitPoint + 1, postfix.Length - splitPoint - 1);
                                postfix = postfix.Substring(0, splitPoint);
                                queryParams = HttpUtility.ParseQueryString(queryString).ToImmutableSortedDictionary();
                            }
                            else
                            {
                                queryParams = ImmutableSortedDictionary<string, ImmutableList<string?>>.Empty;
                            }
                        }

                        bool isRequest(string method, string path) =>
                            string.Compare(htc.Request.HttpMethod, method, StringComparison.Ordinal) == 0
                            && string.Compare(postfix, path, StringComparison.InvariantCultureIgnoreCase) == 0;

                        void writeRootPage()
                        {
                            htc.Response.StatusCode = (int)HttpStatusCode.OK;
                            using (htc.Response)
                            {
                                htc.Response.WriteHtml
                                (
                                    w =>
                                    {
                                        RenderRootPage(w, prefix, commandResult);
                                    }
                                );
                            }
                        }

                        if (isRequest("GET", ""))
                        {
                            using (htc.Response)
                            {
                                htc.Response.WriteRedirect(prefix + "root");
                            }
                        }
                        else if (isRequest("GET", "root"))
                        {
                            writeRootPage();
                        }
                        else if (isRequest("GET", "polling.js"))
                        {
                            htc.Response.StatusCode = (int)HttpStatusCode.OK;
                            using (htc.Response)
                            {
                                htc.Response.WriteEmbeddedResource
                                (
                                    "AwsAmiTool.polling.js",
                                    System.Net.Mime.MediaTypeNames.Text.JavaScript,
                                    Option<Encoding>.Some(Encoding.UTF8)
                                );
                            }
                        }
                        else if (isRequest("GET", "events"))
                        {
                            Task _ignore = Task.Run
                            (
                                async () =>
                                {
                                    // handle this in a separate task

                                    try
                                    {
                                        if (commandResult is DynamicCommandResult dcr)
                                        {
                                            htc.Response.StatusCode = (int)HttpStatusCode.OK;
                                            using (htc.Response)
                                            {
                                                await dcr.Callback(htc);
                                            }
                                        }
                                        else
                                        {
                                            htc.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                                            using (htc.Response)
                                            {
                                                htc.Response.WritePlainText
                                                (
                                                    sw =>
                                                    {
                                                        sw.WriteLine("No callback available.");
                                                    }
                                                );
                                            }
                                        }
                                    }
                                    catch (HttpListenerException)
                                    {
                                        // ignore -- probably the XHR was aborted because of a form submit
                                    }
                                }
                            );
                        }
                        else if (isRequest("POST", "root"))
                        {
                            string formInput = htc.Request.ReadFormInput();
                            ImmutableSortedDictionary<string, ImmutableList<string?>> formParams =
                                HttpUtility.ParseQueryString(formInput).ToImmutableSortedDictionary();

                            string commandText = string.Empty;
                            if (formParams.TryGetValue("command", out ImmutableList<string?>? commandList))
                            {
                                if (commandList.Count >= 1 && commandList[0] is not null)
                                {
                                    commandText = commandList[0] ?? string.Empty;
                                }
                            }

                            ToolCommand? command = null;
                            try
                            {
                                command = ParseCommand(commandText);
                            }
                            catch (InvalidOperationException)
                            {
                                htc.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                                using (htc.Response)
                                {
                                    htc.Response.WritePlainText
                                    (
                                        sw =>
                                        {
                                            sw.WriteLine("Parse Failure!");
                                            sw.WriteLine();
                                            sw.WriteLine($"Command: {commandText}");
                                        }
                                    );
                                }
                                continue;
                            }
                            commandResult = await command.Run(toolState, n => { toolState = n; });

                            using (htc.Response)
                            {
                                htc.Response.WriteRedirect(prefix + "root");
                            }
                        }
                        else if (isRequest("GET", "quit"))
                        {
                            htc.Response.StatusCode = (int)HttpStatusCode.OK;
                            using (htc.Response)
                            {
                                htc.Response.WriteHtml
                                (
                                    w =>
                                    {
                                        w.WriteLine("<html>");
                                        w.WriteLine("<head>");
                                        w.WriteLine("<title>AWS AMI tool</title>");
                                        // can't reference the stylesheet here, because we will have already quit
                                        // when the browser requests the stylesheet, and the request will time out.
                                        w.WriteLine("</head>");
                                        w.WriteLine("<body>");
                                        w.WriteLine("<h1>AWS AMI tool</h1>");
                                        w.WriteLine("<p>Has Quit!</p>");
                                        w.WriteLine("</body>");
                                        w.WriteLine("</html>");
                                    }
                                );
                            }

                            break;
                        }
                        else
                        {
                            htc.Response.StatusCode = (int)HttpStatusCode.NotFound;
                            using (htc.Response)
                            {
                                htc.Response.WritePlainText
                                (
                                    sw =>
                                    {
                                        sw.WriteLine("Not found.");
                                        sw.WriteLine();
                                        sw.WriteLine($"Method: {htc.Request.HttpMethod}");
                                        sw.WriteLine($"Url: {htc.Request.Url}");
                                    }
                                );
                            }
                        }
                    }
                    catch (Exception exc)
                    {
                        Console.WriteLine(exc);
                        break;
                    }
                }
            }
        }

        static void RenderRootPage(TextWriter w, string prefix, CommandResult commandResult)
        {
            w.WriteLine("<!DOCTYPE html>");
            w.WriteLine("<html>");
            w.WriteLine("<head>");
            w.WriteLine("<meta charset=\"utf-8\">");
            w.WriteLine("<title>AWS AMI tool</title>");
            if (commandResult is DynamicCommandResult)
            {
                w.WriteLine("<script src=\"polling.js\" defer></script>");
            }
            w.WriteLine("</head>");
            w.WriteLine("<body>");
            w.WriteLine("<h1>AWS AMI tool</h1>");
            
            string html;
            if (commandResult is StaticCommandResult scr)
            {
                html = scr.Html;
            }
            else if (commandResult is DynamicCommandResult dcr)
            {
                html = dcr.GetHtml();
            }
            else
            {
                html = string.Empty;
            }

            if (!string.IsNullOrEmpty(html))
            {
                w.WriteLine("<h2>Command Result</h2>");
                w.WriteLine("<div id=\"command-result\">");
                w.WriteLine(html);
                w.WriteLine("</div>");
            }
            w.WriteLine($"<form method=\"POST\" action=\"{prefix}root\">");
            if (commandResult is DynamicCommandResult dcr2)
            {
                w.WriteLine($"<input type=\"hidden\" id=\"currentTime\" name=\"currentTime\" value=\"{dcr2.GetCurrentTime()}\">");
            }
            w.WriteLine("<p><label for=\"command\">Command</label></p>");
            w.WriteLine("<p><textarea id=\"command\" name=\"command\" rows=\"8\" cols=\"80\"></textarea></p>");
            w.WriteLine("<p><button type=\"submit\">Submit</button></p>");
            w.WriteLine($"<p><a href=\"{prefix}quit\">Quit</a></p>");
            w.WriteLine("</form>");
            w.WriteLine("</body>");
            w.WriteLine("</html>");
        }

        private static readonly Lazy<Parser> commandParser = new Lazy<Parser>(GetCommandParser, LazyThreadSafetyMode.ExecutionAndPublication);

        private static Parser GetCommandParser()
        {
#if false
            Parser p = ParserBuilder.Empty
                .BeginAlternatives()
                .ReadLiteral("one", StringComparison.InvariantCultureIgnoreCase)
                .WhiteSpace()
                .Int32()
                .Reduce
                (
                    2,
                    (ImmutableList<object> stack) =>
                    {
                        string verb = (string)stack[0];
                        int argument = (int)stack[1];
                        return $"one {argument}";
                    }
                )
                .Or()
                .ReadLiteral("two", StringComparison.InvariantCultureIgnoreCase)
                .WhiteSpace()
                .QuotedString()
                .Reduce
                (
                    2,
                    (ImmutableList<object> stack) =>
                    {
                        string verb = (string)stack[0];
                        string argument = (string)stack[1];
                        return $"two \"{argument}\"";
                    }
                )
                .EndAlternatives()
                .Value;

            return p;
#else
            return ToolCommand.Parser;
#endif
        }

        /// <summary>
        /// Stub: turn the raw textarea into a command object.
        /// Replace this with a real parser when the command language is defined.
        /// </summary>
        static ToolCommand ParseCommand(string commandText)
        {
            commandText ??= string.Empty;

            Parser cp = commandParser.Value;

            ParseResult pr = cp.Parse(ImmutableList<object>.Empty, new ImmutableStringInputStream(commandText, 0, Location.InitialLocation));

            if (pr is ParseSuccess ps)
            {
                ToolCommand command = (ToolCommand)ps.Stack[0];
                return command;
            }
            else
            {
                throw new InvalidOperationException("Parse Failure!");
            }
        }
    }
}
