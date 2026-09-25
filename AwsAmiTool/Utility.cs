using Sunlighter.OptionLib;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Net;
using System.Net.Mime;
using System.Text;

namespace AwsAmiTool
{
    public static partial class Utility
    {
        public static void WriteHtml(this HttpListenerResponse response, Action<TextWriter> action)
        {
            response.ContentType = MediaTypeNames.Text.Html;
            response.ContentEncoding = Encoding.UTF8;

            response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            response.Headers["Pragma"] = "no-cache";
            response.Headers["Expires"] = "0";

            using (StreamWriter sw = new StreamWriter(response.OutputStream, Encoding.UTF8))
            {
                action(sw);
                sw.Flush();
            }

            response.OutputStream.Flush();
        }

        public static void WritePlainText(this HttpListenerResponse response, Action<TextWriter> action)
        {
            response.ContentType = MediaTypeNames.Text.Plain;
            response.ContentEncoding = Encoding.UTF8;

            response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            response.Headers["Pragma"] = "no-cache";
            response.Headers["Expires"] = "0";

            using (StreamWriter sw = new StreamWriter(response.OutputStream, Encoding.UTF8))
            {
                action(sw);
                sw.Flush();
            }

            response.OutputStream.Flush();
        }

        public static void WriteEmbeddedResource(this HttpListenerResponse response, string resourceName, string contentType, Option<Encoding> encoding)
        {
            response.ContentType = contentType;

            if (encoding.HasValue)
            {
                response.ContentEncoding = encoding.Value;
            }
            else
            {
                // leave unspecified
            }

            response.ContentEncoding = Encoding.UTF8;

            response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            response.Headers["Pragma"] = "no-cache";
            response.Headers["Expires"] = "0";

            using (var stream = typeof(Utility).Assembly.GetManifestResourceStream(resourceName))
            {
                if (stream is null) throw new InvalidOperationException($"Unknown resource: {resourceName}");
                stream.CopyTo(response.OutputStream);
            }

            response.OutputStream.Flush();
        }

        public static void WriteRedirect(this HttpListenerResponse response, string targetUrl)
        {
            response.StatusCode = (int)HttpStatusCode.Redirect;
            response.RedirectLocation = targetUrl;

            response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            response.Headers["Pragma"] = "no-cache";
            response.Headers["Expires"] = "0";

            response.OutputStream.Flush();
        }

        public static string ReadFormInput(this HttpListenerRequest request)
        {
            using (StreamReader sr = new StreamReader(request.InputStream, request.ContentEncoding, true))
            {
                return sr.ReadToEnd();
            }
        }

        public static ImmutableSortedDictionary<string, ImmutableList<string?>> ToImmutableSortedDictionary(this NameValueCollection nvc)
        {
            ImmutableSortedDictionary<string, ImmutableList<string?>> results = ImmutableSortedDictionary<string, ImmutableList<string?>>.Empty;

            void add(string key, string? value)
            {
                results = results.SetItem(key, results.GetValueOrDefault(key, ImmutableList<string?>.Empty).Add(value));
            }

            foreach (string key in nvc.Keys)
            {
                if (key != null)
                {
                    add(key, nvc[key]);
                }
                else
                {
                    string? realKey = nvc[key];
                    if (realKey != null)
                    {
                        foreach (string s in realKey.Split(','))
                        {
                            add(s, null);
                        }
                    }
                }
            }

            return results;
        }

        public static ImmutableSortedSet<K> GetKeySet<K, V>(this ImmutableSortedDictionary<K, V> dict)
            where K : notnull
        {
            return ImmutableSortedSet<K>.Empty.WithComparer(dict.KeyComparer).Union(dict.Keys);
        }

        public static bool TryGetInt32(this ImmutableSortedDictionary<string, ImmutableList<string?>> queryParams, string key, Action<int> action)
        {
            if (!queryParams.TryGetValue(key, out ImmutableList<string?>? values)) return false;
            string? s1 = values.FirstOrDefault(v => v is not null);
            if (s1 is null) return false;
            if (!int.TryParse(s1, out int i)) return false;
            action(i);
            return true;
        }

        public static bool TryGetUInt32(this ImmutableSortedDictionary<string, ImmutableList<string?>> queryParams, string key, Action<uint> action)
        {
            if (!queryParams.TryGetValue(key, out ImmutableList<string?>? values)) return false;
            string? s1 = values.FirstOrDefault(v => v is not null);
            if (s1 is null) return false;
            if (!uint.TryParse(s1, out uint u)) return false;
            action(u);
            return true;
        }
    }
}
