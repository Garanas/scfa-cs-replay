using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FAForever.MapEditor.Cli.Guide
{
    /// <summary>
    /// A typed client of a running test editor's automation: each call is a command with arguments
    /// and answers the editor's JSON, or throws when the editor says it failed.
    /// </summary>
    public sealed class EditorSession : IDisposable
    {
        private readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        private readonly int port;

        public EditorSession(int port) => this.port = port;

        public JsonObject Post(string command, JsonObject? arguments = null)
        {
            // always a body: Windows refuses a POST without one
            using StringContent content = new StringContent((arguments ?? []).ToJsonString(), Encoding.UTF8, "application/json");
            try
            {
                using HttpResponseMessage response = client.PostAsync($"http://localhost:{port}/{command}", content).GetAwaiter().GetResult();
                string text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                JsonObject answer = JsonNode.Parse(text) as JsonObject ?? throw new GuideException($"The editor's answer to {command} is not JSON: {text}");
                if (answer["ok"]?.GetValue<bool>() == false)
                {
                    throw new GuideException($"The editor refused {command}: {answer["error"]?.GetValue<string>()}");
                }
                return answer;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
            {
                throw new GuideException($"The editor on port {port} did not answer {command}: {exception.Message}");
            }
        }

        /// <summary>
        /// Runs a command of the editor's registry and says whether it ran.
        /// </summary>
        public bool Run(string id, JsonObject? arguments = null)
        {
            JsonObject body = arguments ?? [];
            body["id"] = id;
            return Post("run", body)["ran"]?.GetValue<bool>() == true;
        }

        public void Dispose() => client.Dispose();
    }
}
