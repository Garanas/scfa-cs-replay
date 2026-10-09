# The map editor's diagnostics, on this computer

What goes well and wrong in the map editor (user story F-17), the way a service's telemetry is
kept: logs in Loki, metrics in Prometheus, a dashboard in Grafana, with an OpenTelemetry Collector
in front. The editor writes each session to disk in OpenTelemetry's JSON (OTLP/JSON, one request
a line), and sends it to a collector when the user turns sending on (Help, Diagnostics); see
`src/FAForever.MapEditor.App/AGENTS.md`, "Diagnostics".

```sh
docker compose up -d          # from this folder; Grafana on http://localhost:3000
docker compose down           # stops it; add -v to forget what it took in
```

Data comes in two ways:

- **The files of sessions.** The collector reads every `*.logs.jsonl` and `*.metrics.jsonl` in
  `./import` (git ignores what is put there), also the ones still being written. Put the files of
  a problem report there, or point `TELEMETRY_FOLDER` at the editor's own folder to see your
  sessions as they happen:

  ```powershell
  $env:TELEMETRY_FOLDER = "$env:LOCALAPPDATA\FAF Map Editor\telemetry"; docker compose up -d
  ```

  Loki and Prometheus accept lines up to 30 days old, so earlier sessions can be read in.
- **Sending.** In the editor: Help, Diagnostics, turn on sending and set the endpoint to
  `http://localhost:4318` (automation: `diagnostics send=true endpoint=http://localhost:4318`).
  What is recorded from then on goes there every half minute. Do not use both ways for the same
  sessions: they would count twice.

| Service | Port | What |
|---|---|---|
| Collector | 4318 | OTLP over HTTP (`/v1/logs`, `/v1/metrics`); the files in `/import` |
| Loki | 3100 | the logs, a stream per session (`service_instance_id`); level in `severity_text`, the category in `scope_name`, the values of a message as structured metadata |
| Prometheus | 9090 | the metrics, through its OTLP receiver; `job` is `faf-map-editor`, `instance` the session |
| Grafana | 3000 | the dashboard "FAF map editor", without a login |

Every port listens on this computer only.

## What to look at

| Question | Where |
|---|---|
| What goes wrong? | `{service_name="faf-map-editor"} \| severity_text=~"WARN\|ERROR\|FATAL"`; `editor_log_records_total` by `level` and `category` |
| Which operations fail, and how long they take | `editor_operation_duration_milliseconds_*` by `operation` (open, import, save, save-as, export, convert) and `result` |
| What people use, and ask for before it is built | `editor_commands_total` by `command` and `result` (`ran`, `not_built`, `cannot_run`, `failed`) |
| Where the time goes | `editor_span_duration_milliseconds_bucket` by `span`; `editor_ui_stalls_total`, `editor_ui_latency_milliseconds_bucket` |
| Memory | `editor_memory_working_set_bytes`, `editor_memory_managed_bytes` |

A counter starts with a zero at the session's start, so `increase()` counts its first events too.

## A server for everyone's sessions

The same collector configuration works on a server, with the OTLP receiver behind a reverse proxy
(TLS, a limit on the size and rate of requests) and Loki and Prometheus as they are here. A
desktop program cannot keep a secret, so the endpoint is public and its limits protect it.
