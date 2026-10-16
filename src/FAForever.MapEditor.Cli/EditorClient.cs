using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FAForever.MapEditor.Cli
{
    /// <summary>
    /// Sends a command to an editor started with <c>--automation port</c> and prints its answer:
    /// <c>faf-map editor stroke points=[[10,10],[50,50]]</c>.
    /// </summary>
    public static class EditorClient
    {
        /// <returns>0 when the editor did the command, 1 when it refused, 3 when no editor answered.</returns>
        public static int Send(int port, string command, IEnumerable<string> arguments)
        {
            JsonObject body = [];
            foreach (string argument in arguments)
            {
                int split = argument.IndexOf('=');
                if (split <= 0)
                {
                    Console.Error.WriteLine($"'{argument}' is not name=value");
                    return 2;
                }
                body[argument[..split]] = Value(argument[(split + 1)..]);
            }

            using HttpClient client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            try
            {
                // always a body: Windows refuses a POST without one
                using StringContent content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                using HttpResponseMessage response = client.PostAsync($"http://localhost:{port}/{command}", content).GetAwaiter().GetResult();
                Console.WriteLine(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                return response.IsSuccessStatusCode ? 0 : 1;
            }
            catch (HttpRequestException exception)
            {
                Console.Error.WriteLine($"No editor answered on port {port}; start one with faf-map-editor --automation {port} ({exception.Message})");
                return 3;
            }
        }

        /// <summary>
        /// A value that reads as JSON (a number, an array, true) as such, any other as text: a path
        /// stays a path.
        /// </summary>
        private static JsonNode? Value(string text)
        {
            try
            {
                return JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                return JsonValue.Create(text);
            }
        }
    }
}
