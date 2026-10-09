using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// What goes well and wrong, kept per session on disk in OpenTelemetry's JSON and sent only when
    /// the user turns it on (user story F-17).
    /// </summary>
    [TestClass]
    public class TelemetryTest
    {
        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "telemetry");

        private static readonly TelemetrySettings Recording = new TelemetrySettings(true, false, null, "install");

        [ClassCleanup]
        public static void Cleanup()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static string NewFolder() => Path.Combine(Root, Guid.NewGuid().ToString("N"));

        /// <summary>
        /// A collector that answers as a test says, and keeps what it was sent.
        /// </summary>
        private sealed class Collector : HttpMessageHandler
        {
            public List<(string Path, string Body)> Received { get; } = [];

            public HttpStatusCode Answer { get; set; } = HttpStatusCode.OK;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Assert.AreEqual("application/json", request.Content!.Headers.ContentType!.MediaType);
                if (Answer == HttpStatusCode.OK)
                {
                    Received.Add((request.RequestUri!.AbsolutePath, await request.Content.ReadAsStringAsync(cancellationToken)));
                }
                return new HttpResponseMessage(Answer);
            }
        }

        private static JsonObject LastLine(string file) => (JsonObject)JsonNode.Parse(File.ReadAllLines(file).Last())!;

        private static string? Attribute(JsonNode? owner, string key) => owner!["attributes"]!.AsArray()
            .FirstOrDefault(attribute => (string?)attribute!["key"] == key)?["value"]?.AsObject().First().Value?.ToString();

        [TestMethod]
        public void WritesTheLogsAsOpenTelemetryJsonWithTheirAttributes()
        {
            using Telemetry telemetry = new Telemetry(NewFolder(), Recording, background: false);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            telemetry.CreateLogger("Rendering").LogWarning("A texture could not be read: {Key}", Path.Combine(home, "env", "rock.dds"));
            telemetry.CreateLogger("Files").LogInformation("Saved in {Milliseconds} ms", 12);
            telemetry.Flush();

            JsonObject request = LastLine(telemetry.LogsFile!);
            JsonNode resource = request["resourceLogs"]![0]!["resource"]!;
            Assert.AreEqual("faf-map-editor", Attribute(resource, "service.name"));
            Assert.AreEqual(telemetry.SessionId, Attribute(resource, "service.instance.id"));
            Assert.AreEqual("install", Attribute(resource, "editor.install.id"));

            JsonArray scopes = request["resourceLogs"]![0]!["scopeLogs"]!.AsArray();
            Assert.AreEqual("FAForever.MapEditor.Rendering", (string?)scopes[0]!["scope"]!["name"]);
            JsonNode warning = scopes[0]!["logRecords"]![0]!;
            Assert.AreEqual("WARN", (string?)warning["severityText"]);
            Assert.AreEqual(13, (int)warning["severityNumber"]!);
            Assert.AreEqual($"A texture could not be read: {Path.Combine("~", "env", "rock.dds")}", (string?)warning["body"]!["stringValue"], "the home folder is written as ~");
            Assert.AreEqual(Path.Combine("~", "env", "rock.dds"), Attribute(warning, "Key"));
            Assert.AreEqual("A texture could not be read: {Key}", Attribute(warning, "message.template"));

            JsonNode information = scopes[1]!["logRecords"]![0]!;
            Assert.AreEqual("INFO", (string?)information["severityText"]);
            Assert.AreEqual("12", Attribute(information, "Milliseconds"), "a number stays a number (intValue)");
            Assert.AreEqual(1, telemetry.Warnings);
        }

        [TestMethod]
        public void WritesTheMetricsCumulativeSinceTheSessionStarted()
        {
            using Telemetry telemetry = new Telemetry(NewFolder(), Recording, background: false);

            EditorMetrics.Commands.Add(1, EditorMetrics.Tag("command", "test.metrics"), EditorMetrics.Tag("result", "ran"));
            EditorMetrics.Commands.Add(1, EditorMetrics.Tag("command", "test.metrics"), EditorMetrics.Tag("result", "ran"));
            EditorMetrics.SpanDuration.Record(3, EditorMetrics.Tag("span", "test.metrics"));
            telemetry.Flush();

            JsonArray metrics = LastLine(telemetry.MetricsFile!)["resourceMetrics"]![0]!["scopeMetrics"]![0]!["metrics"]!.AsArray();
            JsonNode Metric(string name) => metrics.Single(metric => (string?)metric!["name"] == name)!;

            JsonNode commands = Metric("editor.commands")["sum"]!;
            Assert.AreEqual(2, (int)commands["aggregationTemporality"]!, "cumulative");
            Assert.IsTrue((bool)commands["isMonotonic"]!);
            JsonNode[] points = [.. commands["dataPoints"]!.AsArray().Where(point => Attribute(point, "command") == "test.metrics").Select(point => point!)];
            Assert.AreEqual("0", (string?)points[0]["asInt"], "a new series starts with a zero at the session's start, for increase()");
            Assert.AreEqual((string?)points[0]["startTimeUnixNano"], (string?)points[0]["timeUnixNano"]);
            JsonNode ran = points[1];
            Assert.AreEqual("2", (string?)ran["asInt"]);
            Assert.AreEqual("ran", Attribute(ran, "result"));

            JsonNode span = Metric("editor.span.duration")["histogram"]!["dataPoints"]!.AsArray().Last(point => Attribute(point, "span") == "test.metrics")!;
            Assert.AreEqual("ms", (string?)Metric("editor.span.duration")["unit"]);
            Assert.AreEqual("1", (string?)span["count"]);
            Assert.AreEqual(3.0, (double)span["sum"]!);
            string[] buckets = [.. span["bucketCounts"]!.AsArray().Select(count => (string)count!)];
            Assert.AreEqual(MetricCollector.Bounds.Length + 1, buckets.Length);
            Assert.AreEqual("1", buckets[Array.IndexOf(MetricCollector.Bounds, 4.0)], "3 ms falls in the bucket up to 4 ms");

            Assert.IsNotNull(Metric("editor.memory.working_set")["gauge"], "the gauges are read when the metrics are written");

            telemetry.Flush();
            JsonArray again = LastLine(telemetry.MetricsFile!)["resourceMetrics"]![0]!["scopeMetrics"]![0]!["metrics"]!.AsArray();
            Assert.AreEqual(1, again.Single(metric => (string?)metric!["name"] == "editor.commands")!["sum"]!["dataPoints"]!.AsArray()
                .Count(point => Attribute(point, "command") == "test.metrics"), "the zero only once");
        }

        [TestMethod]
        public void WritesNothingWhenRecordingIsOff()
        {
            string folder = NewFolder();
            using Telemetry telemetry = new Telemetry(folder, Recording with { Record = false }, background: false);
            List<LogEntry> shown = [];
            telemetry.Logged += shown.Add;

            telemetry.CreateLogger("Files").LogError("Saving failed");
            telemetry.Flush();

            Assert.IsFalse(Directory.Exists(folder) && Directory.EnumerateFiles(folder).Any());
            Assert.AreEqual(1, shown.Count, "the Console still shows it");
            Assert.AreEqual(1, telemetry.Errors);
        }

        [TestMethod]
        public async Task SendsOnlyWhatIsRecordedAfterSendingIsTurnedOn()
        {
            Collector collector = new Collector();
            using Telemetry telemetry = new Telemetry(NewFolder(), Recording, collector, background: false);
            ILogger log = telemetry.CreateLogger("Files");

            log.LogInformation("Before sending was on");
            telemetry.Flush();
            Assert.AreEqual(0, await telemetry.UploadAsync(), "nothing is sent unless the user turns it on");

            telemetry.Apply(Recording with { Send = true, Endpoint = "http://collector.test/" });
            log.LogWarning("After sending was on");
            telemetry.Flush();
            collector.Answer = HttpStatusCode.ServiceUnavailable;
            Assert.AreEqual(0, await telemetry.UploadAsync());
            StringAssert.Contains(telemetry.LastUploadError, "503");

            collector.Answer = HttpStatusCode.OK;
            int sent = await telemetry.UploadAsync();

            Assert.AreEqual(2, sent, "a line of logs and one of metrics");
            CollectionAssert.AreEquivalent(new[] { "/v1/logs", "/v1/metrics" }, collector.Received.Select(request => request.Path).ToArray());
            string logs = collector.Received.Single(request => request.Path == "/v1/logs").Body;
            StringAssert.Contains(logs, "After sending was on");
            Assert.IsFalse(logs.Contains("Before sending was on", StringComparison.Ordinal));
            Assert.IsNull(telemetry.LastUploadError);
            Assert.AreEqual(0, await telemetry.UploadAsync(), "what is sent is not sent again");
        }

        [TestMethod]
        public void KeepsTheNewestSessions()
        {
            string folder = NewFolder();
            Directory.CreateDirectory(folder);
            for (int i = 0; i < Telemetry.KeptSessions + 5; i++)
            {
                File.WriteAllText(Path.Combine(folder, $"20200101-0000{i:00}-abcdef{i:00}.logs.jsonl"), "{}\n");
                File.WriteAllText(Path.Combine(folder, $"20200101-0000{i:00}-abcdef{i:00}.metrics.jsonl"), "{}\n");
            }

            using Telemetry telemetry = new Telemetry(folder, Recording, background: false);

            string[] sessions = [.. Directory.EnumerateFiles(folder).Select(file => Path.GetFileName(file).Split('.')[0]).Distinct().Order()];
            Assert.AreEqual(Telemetry.KeptSessions - 1, sessions.Length, "room for this session");
            Assert.AreEqual($"20200101-000034-abcdef34", sessions[^1]);
            Assert.AreEqual($"20200101-000006-abcdef06", sessions[0]);
        }

        [TestMethod]
        public void TheSettingsFileOfTestsKeepsTheirTelemetryApart()
        {
            string? before = Environment.GetEnvironmentVariable(UserSettings.FileVariable);
            try
            {
                Environment.SetEnvironmentVariable(UserSettings.FileVariable, Path.Combine(Root, "editor", "settings.json"));
                Assert.AreEqual(Path.Combine(Root, "editor", "telemetry"), Telemetry.DefaultFolder);
            }
            finally
            {
                Environment.SetEnvironmentVariable(UserSettings.FileVariable, before);
            }
        }
    }
}
