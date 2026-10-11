# The agents' tooling wish list

Every agent of the waves ([agent-waves.md](agent-waves.md)) ended its report with a Tooling
section: did it have enough to test its work through tests and by running the editor, and what was
missing, awkward or slow. This is that list, in the order the wishes came in (73 items by wave 16),
with what came of each.

**Status**: **built** (and by which agent), **partly** (what is left), **open**, or **not checked**
(an early wish whose outcome was not recorded; it may have been built in passing).

The App guide (`src/FAForever.MapEditor.App/AGENTS.md`) and the `editor-ui` skill describe the
commands that exist.

## Wave 0

| # | From | Wish | Status |
|---|---|---|---|
| 1 | 0C | Input for automation through Avalonia's raw input in the window, never the OS mouse: `key`, `type`, `mouse`; `run {id, interactive: true}` that starts a command as a person would, without waiting | Built: input by 1T, interactive `run` by 5A |
| 2 | 0C | `drag {from, to}` raising DragOver/Drop like headless drag and drop; an injectable drag starter so a headless test sees a drag start | Built (1T) |
| 3 | 0C | Restart a test editor on a port with a map and settings and wait until it is ready with its assets loaded; `assetsLoaded` in `state` | Built: `editor start` (1T, detached in 2H), `restart` (6B) |
| 4 | 0C | `screenshot {settle: true}`, or the number of thumbnails still rendering in `state` | Built (1T) |
| 5 | 0C | Bash heredocs with apostrophes break; mixed LF/CRLF files need care in every edit script | Built: `faf-map dev replace` (2H), the traps in the root `AGENTS.md` |
| 6 | 0C | `ui {under: "<label or name>"}` | Built (1T); labelled controls (4A), section headings (7A) |
| 7 | 0C | A snapshot helper for overlays such as the picker, with the test assets | Not checked |
| 8 | 0D | A scratch workspace with assets for test editors, so writing features (presets) can be tried without the real workspace | Built: `faf-map workspace scratch` (1T) |
| 9 | 0D | A headless helper to capture one control instead of the whole window | Not checked |
| 10 | 0D | `sed -i` in Git Bash turns CRLF into LF; the Edit tool needs a Read first | Built: see 5 |
| 11 | 0D | A test hook on the project watcher (quiet time, flush) instead of polling with `Task.Delay` | Not checked |
| 12 | 0D | `ui` for one part of the window (as 6) | Built (1T) |
| 13 | 0B | GPU timing: spans measure only the CPU's submission | Built: `perf gpu=true` (1T) |
| 14 | 0B | `state` reports the 3D view's pixel size and scaling (aiming a pick was guesswork) | Built (1T) |
| 15 | 0B | Hover and highlight in 3D, to check them | Built (2C) |
| 16 | 0B | A command listing some things with their places, to aim the camera at | Built: `objects` (1T) |
| 17 | 0B | A small converted workspace with a prop and a unit, so model loading is testable headless | Built (1T) |

## Wave 1

| # | From | Wish | Status |
|---|---|---|---|
| 18 | wave 1 | Set a control's value by its label (sliders, fields, combo boxes, tree items) as a person would; it would have caught a slider bug | Built: `set` (2H); by index (4A) |
| 19 | wave 1 | `stroke` and `mouse` naming a view (top or 3D) | Partly (1T's `mouse`) |
| 20 | wave 1 | A general `grid {target, x, z}` read: terrain types, water masks, masks | Built (2H) |
| 21 | wave 1 | `POST /undo` with an empty body does nothing silently | Built: refusals say why (2H) |
| 22 | wave 1 | `view {zoom}` does not centre the top view; `camera.look` may not change the frame | Not checked |
| 23 | wave 1 | A screenshot of one part (the options row); `ui` reporting what overflows | Built: `screenshot part` (2H) |
| 24 | wave 1 | A shared edit helper that keeps line endings and UTF-8 | Built: see 5 |
| 25 | wave 1 | `faf-map editor start` keeps the CLI's output pipe open, so the call never returns | Built: start detached (2H) |
| 26 | wave 1 | A small flat test map with a clean slope; a typed automation client (MCP or C#) | Partly: the slope map (2H); no typed client |
| 27 | wave 1 | `focus {ref}`: frame a thing or the selection | Built (2H); framed at 50 degrees (5A) |

## Wave 2

| # | From | Wish | Status |
|---|---|---|---|
| 28 | 2F | The options row's scroll bar covers its controls (`ui` could give a free point to click); combo box popups missing from screenshots; `wait-for {path, value}`; the history readable in `state` | Partly: `wait` (3C), More replaced the scroll bar (2H) |
| 29 | 2B | `view-point` hung with the top tab shown; a bigger top-view screenshot; hiding panels for screenshots; `stroke` with a turn per dab | Partly: the hang fixed (2H) |
| 30 | 2D | `ui under=X scroll=true`; a `generate` that waits; which grid the top view's overlay shows; `perf`'s JSON documented; tests with a fixed culture (Dutch number formats hid a bug) | Partly: `click` scrolls (4A), numbers read the same in every culture (3C) |
| 31 | 2E | Automation answers with the error instead of timing out; `objects {name}`, `get {name}`; debug pictures of library results; path links drawn in 3D | Built: `get`/`objects {name}` and path links (3C) |
| 32 | 2C | `key {action: down/up}` to hold a key across mouse actions; tests rebuild into the bin the test editor runs from; `get` returns the whole state; a first `tab` once hung 60 s; a GPU screenshot of a region; `state.handles.points` | Partly: held keys (3C), the shadow copy (5A), short answers (5A) |
| 33 | 2A | `state` held bytes that are not UTF-8; `view-point` for a list of places; `perf` cannot time library code | Not checked |
| 34 | 2G | Window screenshots leave the 3D half blank in side by side; `mouse action=drag` does not move splitters; a build stamp in `state`; waiting for test runs | Partly: `state.build` (3C), splitter drags (4A) |
| 35 | 2H | Light dismiss for More; `perf` per command; `wait {until}`; number fields the same in every culture; in-process automation with whole numbers breaks `stroke`; long test runs in `timeout` | Partly: light dismiss, `wait` and culture (3C); no `perf` per command |

## Wave 3

| # | From | Wish | Status |
|---|---|---|---|
| 36 | 3B | A `dialog` command to see and answer the dialog windows; refusals answering `ran: false` instead of throwing; a fake HTTP server for a running editor | Partly: questions in the window and `dialog` (4A), `ran: false` (5A); no fake server |

## Wave 4

| # | From | Wish | Status |
|---|---|---|---|
| 37 | 4A | Building while a test editor runs fails (bin locked) | Built: the shadow copy (5A) |
| 38 | 4A | `screenshot part=dialog` | Built (5A) |
| 39 | 4A | `run` returns the question a command opened | Built: `run interactive=true` answers `asked` (5A) |
| 40 | 4A | `ui` filtered by type or text (its output is very long) | Built (5A) |
| 41 | 4B, 4C | Every answer carries the whole state: `quiet`, or a `path` filter for `state` and `perf` | Built: short answers, `quiet`, `paths`, `full` (5A) |
| 42 | 4B | `focus` leaves the 3D camera straight down and too close | Built (5A) |
| 43 | 4B | In-process `RunAsync` throws when a command refuses | Built: `TryRunAsync` and `ran: false` (5A) |
| 44 | 4C | `perf compare` of a before and an after; a sample 4096 map with path nodes in the test assets | Partly: `perf save` and `perf before=` (5A); no 4096 test map |
| 45 | 4B | The sandbox's birches did not show in 3D screenshots | Built: they were tiny; foliage alpha from the top mips (5A) |

## Wave 5

| # | From | Wish | Status |
|---|---|---|---|
| 46 | 5A | `faf-map editor restart`; easier JSON arrays as CLI arguments; a screenshot that frames a thing in 3D in one step; a `grid` difference between two points of the history; `ui --diff` | Partly: `restart` (6B) |
| 47 | 5C | `workspace scratch --convert-into` for asset format changes; `workspace init --settings` (it wrote the user's own settings); `MSYS_NO_PATHCONV=1` in the skill; a settings file without its install id read as empty; GPU screenshots compared with a tolerance | Partly: all but the screenshot comparison (6B) |
| 48 | 5B | `editor expand label=...` (`ui` lists nothing inside a closed tree item); a warning that a running editor locks bin; port 5432 is Docker's postgres | Built: `expand` (6B), the shadow copy (5A) |

## Wave 6

| # | From | Wish | Status |
|---|---|---|---|
| 49 | 6A | Menu popups out of reach (`menu {path}`); `window {width, height, close}`; `key Ctrl+2` silently doing nothing; `click` measuring before a scroll settles; section headings for `under`; key answers naming the focused control; failed automation calls raising the Console's count | Built (7A) |
| 50 | 6B | Seeing a floating window as the desktop shows it: `screenshot part=floating:<panel>`, a `window` argument on `mouse`, closing a floating window as a person does | Partly: `ui`, `click`, `set` and `screenshot part` reach floating windows (6B) |

## Wave 7

| # | From | Wish | Status |
|---|---|---|---|
| 51 | 7B | A `camera {x, z, distance, yaw, pitch}` command (the wheel in 3D did nothing through automation); `faf-map dev extract <game path>` for real game files; `mouse moves:[...]` at a set rate, to measure per-move work as a real mouse gives it; finding an empty point of ground for drags | Open |
| 52 | 7A | The editor reopens maximized at 2560 px after a restart (checks at 1280 need `window` first); `wait` by an id rather than a list index (`generated.0.status`); "Choose ... in the picker" labels where no picker opens; a wait for "the window has the keyboard"; the full App suite takes 1.5 min | Open |

## Waves 8 to 12

| # | From | Wish | Status |
|---|---|---|---|
| 53 | 8C | Mouse places are view pixels: a drag off the map silently does nothing; scroll a panel's section into view for a screenshot; command arguments that take enum names | Open |
| 54 | 8A, 8B | `perf` against a baseline build; `perf before=` without a running editor; `quiet` for `perf`; a tool tip in automation; a test mode for "Show the folder" (it opens real Explorer windows) | Open |
| 55 | 9A | `faf-map editor` without `--port` uses 5372 silently (make it required); `screenshot gpu` in side by side shows only the 3D half; a ready 4096 test project with layers; stroke a list of points | Open |
| 56 | 9D | A read of the preview's path nodes; a screenshot framing a region; timing library code without a temporary test | Open |
| 57 | 9B | Refuse to start an editor older than its sources; read one layer of a stack (`grid layer=`) | Open |
| 58 | 9C | `get` shows a decal's set; reach an asset tile by name; a DDS info command; an overlay folder for test assets in a scratch workspace | Partly: the map's own decals on the shelf (12B) |
| 59 | 10E | Add a file to the map's own files; read an exported folder's files and formats | Partly: `map.addFiles` (12B) |
| 60 | 10D, 10B | `state.inspector.title`; `click under=`; `objects` rows with a unit's army and group; a preset as a picture | Open |
| 61 | 10A | A cube map as a strip in a screenshot; log messages in automation | Open |
| 62 | 10F | Labels with a middle dot garble in Git Bash; `assets.browse tileSize=`; `screenshot part=picker` | Open |
| 63 | 11A | `click` waits for a scroll and refuses hidden controls; answer an OS file dialog; a tool-tip screenshot; `objects kind=markers` counts path nodes | Partly: `click` and `set` aim and refuse (12A) |
| 64 | 12A | `ui` describes what lies in a button (pictures); `screenshot part=menu` | Open |
| 65 | 12B | Warn when a command's argument has a name automation keeps for itself (`paths`) | Open |
| 66 | 12C | `perf` says whether the window has focus (Windows slows an editor without focus 8 to 15 times); time a library operation in-process; `perf trace=` for nested spans, or `dotnet-trace` installed | Open |

## Waves 13 to 16

| # | From | Wish | Status |
|---|---|---|---|
| 67 | 13A, 14B | `click` that refuses or reports an ambiguous label; a `tree` command listing a tree's items | Partly: `click type= index=`, `matches` (16A) |
| 68 | 13B, 16A | `stroke tool=` and strokes in map coordinates; a `history` read; a fresh-sandbox start | Open |
| 69 | 13C | `dev scan-units --positions` or `dev unit-clusters`; a contact sheet of presets (place, capture, undo) | Open |
| 70 | 14A, 16B | A shared `HeadlessApp.Settle()` and `UiTree.Unlabelled(window)` for headless tests; usage hints in argument errors | Open |
| 71 | 14C | `screenshot part=` for one panel entry; a style rule against `TextBlock` classes on `SelectableTextBlock` | Open |
| 72 | 15 | Popups in screenshots; combo items in `ui`; `checked` after the click; a flag per result made for the shown version; brush and rules fixtures in the sandbox | Partly: `checked`, `state.popups`, `madeForShown`, `auto.idle` (16A) |
| 73 | 16C | `workspace scratch --assets-as-current` for containers; Avalonia's warnings in the editor's log | Open |

## Waves 19 to 22

| # | From | Wish | Status |
|---|---|---|---|
| 74 | 19C, 20A | A double click from automation | Built: `mouse clicks=1..3` (20A) |
| 75 | 19C, 21 | A command that chooses a tree node by army and group, rather than its pixel | Open |
| 76 | 19B, 21, 22C | `drag` carrying a units preset; `drag` without `data` hung the editor in the OS drag loop | Built: refused with a hint, `data=preset:<name>` (22C) |
| 77 | 19B, 20B, 21 | Context menus and flyouts in `ui`, `state` and screenshots, and a click on their items | Built (22C); combo lists' items still missing |
| 78 | 20C | The editor's unhandled exceptions in its log (a crash left an empty `editor-<port>.log`; the stack was only in Windows' event log); a headless test that renders through the real view (setting the status during render crashed only the real editor) | Open |
| 79 | 20C, 22A | `dotnet build A B` builds nothing; `--convert-into` leaves its folder empty and the workspace's `assets` a real folder | Open |
| 80 | 20C, 22A | Zoom the top view to a scale; a hint that `mouse` takes the view's coordinates | Open |
| 81 | 20B | `picker choose=<key>`; a helper that puts a few units into a fresh sandbox in one call; the turn and scale handles in `handles.points` | Open |
| 82 | 20A | Digits as key names (`D1`) in the guide; a crop or zoom of a screenshot; strict case in `UiTree.Find` for tests; a headless catalogue with real footprints | Partly: the guide names `D1` (22) |
| 83 | 21 | Switch the theme at run time to look at the light theme | Built: `view.theme` (22C) |
| 84 | 21 | A click on a Problems line selects its things | Built (22C) |
| 85 | 22A | A command that runs the problem checks; a `mouse up` that opens a dialog blocks until it is answered | Open |
| 86 | 22B | Reset the click count between `mouse` calls; a check that a control lies inside its scroll viewer's view | Open |
| 87 | 22C | `click` on combo list items; the shelf's virtualised tiles by label; `state.units.place.armyChosen` | Open |

## Still open, by theme

- **Camera and views**: a `camera` command (51), a screenshot framing a thing in 3D (46), the 3D
  half of side by side in window screenshots (34), a combo box's popup in screenshots (28).
- **Measuring**: mouse moves at a set rate (51), `perf` per command (35), library code in `perf`
  (33), GPU screenshots compared with a tolerance (47), a 4096 test map with path nodes (44).
- **Waiting and reading**: `wait` by id (52), waiting for the keyboard (52), a `grid` difference
  between history points and `ui --diff` (46).
- **Game files and services**: extracting real files from the game's archives (51), a fake HTTP
  server for the vault upload (36).
- **The CLI**: JSON arrays as arguments (46), a typed automation client (26).
- **Small**: the window size after a restart, picker labels (52).
