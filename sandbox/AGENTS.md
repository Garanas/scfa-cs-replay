# Agent guide: sandbox/

`FAForever.FileFormats.Replay.Sandbox` is a command-line scratch pad for the replay parser: load a
replay from disk or a URL and look at what comes out. The repository-wide guide is
[`../AGENTS.md`](../AGENTS.md).

## What it does today

```sh
cd sandbox/FAForever.FileFormats.Replay.Sandbox
dotnet run -- -o ../../TestResults/sandbox -f path/to/replay.fafreplay   # print input counts per type
dotnet run -- -o ../../TestResults/sandbox -u https://api.faforever.com/game/25717491/replay
dotnet run -- -o ../../TestResults/sandbox -i                            # prompt for files/URLs, write XML
```

- `-f` (file) and `-u` (URL) print the number of inputs and a count per input type
  (`ReplaySemantics.CountInputTypes`).
- `-i` (interactive) asks for paths or URLs in a loop and writes each replay as XML to the output
  directory (`-o`, required).
- `-j`, `-x` and `-c` (JSON, XML, CSV output) are declared in `ProgramArguments.cs` but not
  implemented yet; do not rely on them.
- Replays from the vault: `https://api.faforever.com/game/{id}/replay` is anonymous and redirects
  to the file; the sandbox follows the redirect. The `.fafreplay` extension decides how a file
  from disk is decoded.

## Rules

- **Nothing depends on it.** No other project references the sandbox, and it has no tests. It is
  free to change, but it is part of the solution, so it must keep compiling (CI builds everything).
- **It is not where code lives.** Anything worth keeping moves into a library under `src/` with a
  test under `tests/`; the sandbox only calls public APIs, like any other consumer would.
- **Output stays out of git.** Write to a gitignored directory such as `TestResults/` (as above)
  or outside the repository, never into the project folder.
- **Desktop only.** Unlike the libraries, the sandbox may read files and use the network; that is
  the point of it. Do not copy those patterns into `src/`, which must also run in the browser.
- **No secrets.** Vault downloads are anonymous; the sandbox needs no token and must not ask for one.

## Typical uses

- Checking how a specific replay parses before writing a test for it: load it here, then add the
  file to `tests/FAForever.FileFormats.Replay.Tests/assets/` with a test.
- Reproducing a bug report with a vault replay id, without starting the web app.
- Dumping a replay to XML to search through it by hand.
- Trying out a new public API from a consumer's point of view before the Viewer uses it.
