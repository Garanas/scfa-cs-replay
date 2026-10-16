using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// A stand-in for FAForever's API and sign-in on the loopback address, for tests of the upload
    /// (user story M-8): it records each request and answers what the test set. Nothing goes to the
    /// real vault.
    /// </summary>
    public sealed class FakeVaultServer : IDisposable
    {
        /// <summary>
        /// A request as it came in.
        /// </summary>
        public sealed record Received(string Method, string Path, string? Authorization, string? ContentType, byte[] Body)
        {
            public string Text => Encoding.UTF8.GetString(Body);
        }

        private readonly HttpListener _listener = new HttpListener();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly Task _loop;

        public FakeVaultServer()
        {
            int port;
            using (TcpListener probe = new TcpListener(IPAddress.Loopback, 0))
            {
                probe.Start();
                port = ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            Url = new Uri($"http://127.0.0.1:{port}/");
            _listener.Prefixes.Add(Url.ToString());
            _listener.Start();
            _loop = Task.Run(LoopAsync);
        }

        public Uri Url { get; }

        public ConcurrentQueue<Received> Requests { get; } = new ConcurrentQueue<Received>();

        /// <summary>
        /// The status and body it answers an upload with; 200 and nothing by default, as the API.
        /// </summary>
        public (int Status, string Body) UploadAnswer { get; set; } = (200, "");

        /// <summary>
        /// The body it answers the token endpoint (<c>/oauth2/token</c>) with.
        /// </summary>
        public string TokenAnswer { get; set; } = """{"access_token":"fake-access","refresh_token":"fake-refresh","expires_in":3600,"token_type":"bearer"}""";

        private async Task LoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception) when (_stop.IsCancellationRequested || !_listener.IsListening)
                {
                    return;
                }
                catch (HttpListenerException)
                {
                    return;
                }
                using MemoryStream body = new MemoryStream();
                await context.Request.InputStream.CopyToAsync(body);
                string path = context.Request.Url!.AbsolutePath;
                Requests.Enqueue(new Received(context.Request.HttpMethod, path, context.Request.Headers["Authorization"], context.Request.ContentType, body.ToArray()));
                (int status, string text, string type) = path switch
                {
                    "/maps/upload" => (UploadAnswer.Status, UploadAnswer.Body, "application/json"),
                    "/oauth2/token" => (200, TokenAnswer, "application/json"),
                    _ => (404, "", "text/plain"),
                };
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                context.Response.StatusCode = status;
                context.Response.ContentType = type;
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        /// <summary>
        /// The parts of a multipart body by their names: the headers and the content of each.
        /// </summary>
        public static Dictionary<string, (string Headers, byte[] Content)> Parts(Received request)
        {
            string boundary = request.ContentType!.Split("boundary=")[1].Trim('"');
            byte[] body = request.Body;
            byte[] separator = Encoding.ASCII.GetBytes("--" + boundary);
            Dictionary<string, (string, byte[])> parts = new Dictionary<string, (string, byte[])>();
            int start = IndexOf(body, separator, 0);
            while (start >= 0)
            {
                int headersStart = start + separator.Length + 2;
                int next = IndexOf(body, separator, headersStart);
                if (next < 0)
                {
                    break;
                }
                int headersEnd = IndexOf(body, "\r\n\r\n"u8.ToArray(), headersStart);
                string headers = Encoding.UTF8.GetString(body, headersStart, headersEnd - headersStart);
                byte[] content = body[(headersEnd + 4)..(next - 2)];
                string name = headers.Split("name=")[1].Split(';')[0].Split("\r\n")[0].Trim('"');
                parts[name] = (headers, content);
                start = next;
            }
            return parts;
        }

        private static int IndexOf(byte[] haystack, byte[] needle, int from)
        {
            for (int i = from; i <= haystack.Length - needle.Length; i++)
            {
                if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                {
                    return i;
                }
            }
            return -1;
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _listener.Close();
            try
            {
                _loop.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }
        }
    }
}
