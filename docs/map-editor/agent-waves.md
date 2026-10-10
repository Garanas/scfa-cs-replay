# Building the map editor in agent waves

The map editor's user stories (`USER_STORIES.md`) were built by agents working in parallel, in
eight waves, between the brushes work and 2026-10-10. This page describes how the waves were run,
what each one built, what the two QA walkthroughs found, and what is still open. The commits are on
`feature/map-editor`; each wave's branches were merged with `--no-ff`, so
`git log --merges --first-parent` lists them.

At the start, the editor had commands and undo, the workspace and asset shelf, the sculpt and
strata brushes with picture shapes, a top view and a 3D view. Most stories were open: nothing tracked
a selection, the 3D view drew no props, units or markers, the Problems tab was a placeholder and
every inspector section was planned. At the end, all 66 stories are done.

| Wave | Agents | What it was for | Tests after the merge (App / Project) |
|---|---|---|---|
| 0 | 4 | Foundations: selection, 3D objects and picking, the picker, presets/symmetry/generation/events | |
| 1 | 8 | Features on the foundations, one area each, plus tooling | |
| 2 | 8 | Stories that waited on wave 1: height layers, rules, symmetry applied, handles, map size | |
| 3 | 4 | Lua plugins, vault upload and wrecks, gaps, and a QA walkthrough | 334 / 341 |
| 4 | 3 | Fixing the 35 issues the QA walkthrough found | 381 / 355 |
| 5 | 3 | The stories' remaining parts, and the agents' tooling wishes | 416 / 386 |
| 6 | 2 | The last story (V-4 layouts) and a second QA walkthrough | 428 / 394 |
| 7 | 2 | Fixing the 32 issues of the second walkthrough | 465 / 397 |

## How a wave ran

**The plan.** Before wave 0 the stories were analysed for what depends on what (the plan file
`bubbly-sprouting-heron.md`). The dependencies decided the order:

```
F-11 selection ─────┬─> markers, props, decals, units, areas   (wave 1)
3D objects, picking ┘
F-9 picker ─────────> strata, props, decals, units
F-10 presets ───────> brush, light, unit and prop palettes
F-12 symmetry maths ─> guides in wave 0; mirrored edits in wave 2
F-7 generate when asked ─> walkability, normal map, preview; then rules (wave 2)
T-5 height layers ──> T-4 curves, rules from layers               (wave 2, alone)
K-1 mask layers ────> props on masks, masks and props from rules
F-13 events ────────> problem checks from plugins, Lua plugins    (wave 3)
```

The plan also listed the small features that make editing easier (select similar, nudge with the
arrows, hold X or Z for one axis, align props to the terrain, drop to ground, replace a blueprint, and
so on); each agent built the ones in its area.

**Isolation.** Every agent had its own:

- git worktree at a short path (`D:\wt\<id>`, with `core.longpaths true`: a longer path failed
  with "Filename too long") on its own branch (`wave4/editing`, ...);
- build output (`--artifacts-path D:/wt/art-<id>`), so builds never collided;
- automation port (5373 and up; never 5371, the user's editor);
- settings file and scratch workspace (`faf-map workspace scratch`), whose `assets` folder is a
  junction to converted assets that it must never write into. A `Sandbox` project in each
  workspace holds one of every kind of thing.

**The brief.** Every wave had a brief (`D:\wt\WAVE<n>-BRIEF.md`) that every agent read first: what
earlier waves built (reuse, do not rebuild), the rules for shared files, how to build, start and
stop a test editor, the shell traps, the standing rules and the report to write. The rules for shared
files came from merge conflicts:

- **Unique names.** Two wave-1 branches defined the same style class (`TextBox.number`), token
  (`SwatchSize`) and extension method (`RenameArmy`) with different meanings. New classes and
  tokens take a feature prefix.
- **Append in shared files**, each agent its own block: `EditorModes.cs`, `Menus.cs`, the
  automation state (one key per feature), `USER_STORIES.md`, the guides.
- **New record parameters at the end, with defaults.** Two wave-4 branches each appended a
  parameter to `ObjectFrame`.
- **Snapshots** are accepted by the main session at the merge, after looking at them.
- **Project format:** agents add optional properties only; the main session raises
  `MapProjectDto.CurrentFormatVersion` once per wave that changed it.

**Done** meant the editor-ui checklist: a command with its story, labels on every control,
headless tests and library tests, performance spans and logging, automation state, a check in a
running test editor with screenshots the agent looked at, the guide and stories updated, and
stepwise commits.

**Merging.** The main session merged each branch, resolved conflicts (usually by keeping both
sides; scripts kept each file's CRLF and UTF-8), ran the App, Project, Cli and Map tests, looked at
and accepted changed snapshots, and wrote a report before the next wave.

**Tooling.** Every agent ended its report with a Tooling section: did it have enough to test its
work through tests and by running the editor, and what was missing. Those wishes became a running
list (52 items by wave 7), and later waves built most of them (see "The agents' tooling" below).

## Wave 0: foundations

Four agents, each owning separate files.

- **0A Selection** (F-11 core): `ObjectRef` (kind and id), the selection on `MainViewModel`,
  hit testing in the top view, click, Shift to add, Ctrl to toggle, a box, Escape; dragging moves
  and turning about the up axis, one undo step per drag; delete, duplicate, copy and paste, select
  all and none; `DocumentEdits` for every kind of thing; the inspector host with a template per kind
  that edits many things at once.
- **0B Objects in 3D and picking**: props and units drawn instanced from the workspace's `.glb`
  meshes with their textures, markers as symbols, the selection highlighted, picking by a ray
  against each thing's bounds, overlays per kind.
- **0C Picker** (F-9, U-1): a searchable popup with thumbnails and filters
  (`IEditorDialogs.PickAssetAsync`), unit filters by faction, tech, kind and size, and dropping a
  shelf tile onto the map.
- **0D Small foundations**: presets in the workspace (F-10), the symmetry maths and guides
  (F-12), generated results that know when they are out of date (F-7) and editor events (F-13).

## Wave 1: features

Seven feature agents and one for tooling.

- **1A Markers** (R-1 to R-6): a placing tool per kind, deposits on the half-ogrid grid,
  numbered spawns, path links by dragging from node to node, chains, adaptive deposits.
- **1B Props** (P-1, P-2, P-3, P-5): placing from the picker, aligning to the terrain, painting
  from a palette with an eraser, painting only where a stratum covers, reclaim totals.
- **1C Decals** (D-1, D-2): placing, scaling and turning on the terrain, the distances at which they
  show, their order, groups.
- **1D Units and armies** (U-2, U-3, M-4, U-4): placing units, the group tree, armies and teams
  (a new field in the format), unit presets.
- **1E Terrain painting** (T-1, T-6, W-2, K-1, S-1): the strata tool became "paint any grid":
  terrain types, water masks and named mask layers; the sculpt brush gained set height, noise,
  erosion, falloffs and height limits.
- **1F Generation** (T-7, S-4, F-14): walkability from the game's own terrain types, its overlay,
  the normal map and preview generated when asked, the Problems list with jump-to and its first
  checks.
- **1G Map and atmosphere**: map information, the new-map page, areas, the script, water,
  lighting, strata textures, heightmap files, the ruler.
- **1T Tooling**: automation input (`mouse`, `key`, `type`, `drag`), reads (`get`, `objects`,
  `view-point`), test editors and scratch workspaces, a small fixture workspace with a prop and a
  unit, GPU timing, the sandbox project.

## Wave 2: what waited on wave 1

Eight agents.

- **2A Height layers** (T-5, T-4): the heightmap as a stack of layers (sculpted, from a picture or
  a curve) that add up or replace, and curves along which the terrain is shaped. Alone in its
  wave's area, because it changes what every brush writes into.
- **2B Symmetry applied** (F-12, S-1): mirrored strokes, placing and moving.
- **2C Handles** (F-11, V-3, L-1): move, turn and scale handles in both views, 3D overlays, the
  thing under the mouse lit up, the sun's handle.
- **2D Rules** (K-2, P-4): masks and props generated from rules, when asked.
- **2E AI markers** (R-3, R-5): path networks generated from walkability, marker presets, weather
  markers, optional civilian groups.
- **2F Map size** (M-3, M-8): growing and cropping the map, the playable area as a field of its
  own, raising the version on export.
- **2G Shell** (V-4, F-1, F-18, F-17, F-8): side by side, the panel layout, the toolbar, git
  push, pull and history, the problem report, where each asset comes from and which are missing.
- **2H Polish and tooling**: the options row's More (what does not fit at narrow widths goes
  behind it), stale sections, prop dots, automation `set` (by label), `focus`, `grid`,
  `screenshot part`, `faf-map dev replace`, a flat test map with a slope.

The org's spending limit stopped the wave-2 agents partway; they were resumed where they stopped,
and later waves were kept smaller (four agents, then three, then two).

## Wave 3: plugins, the vault, gaps and QA

- **3A Lua plugins** (F-15, F-1, F-13, F-14): plugins with commands, problem checks, fields and
  event handlers, in a sandbox with time limits; everything one run changes is one undo step.
- **3B Vault upload and wrecks** (M-8, U-5): uploading a map to the FAF vault (built, never run
  against the real vault: the first real upload waits for the owner, see `TODO.md`), and units as
  wrecks.
- **3C Gaps**: symmetry for scaling, turning, paste and duplicate; the brush outline at every
  mirrored place; the box selection, curves and path links in 3D; generating in dependency order;
  number fields that read the same in every culture; automation `wait` and held keys.
- **3D QA walkthrough**: an agent used the whole editor as a map author would, through the mouse
  and keys, and wrote a report of 35 issues: 1 blocker, 4 major, about 30 minor and polish.
  - Blocker: the unsaved-changes question opened as an OS window that automation could not reach.
  - Major: moving a selection holding both mirror partners broke the symmetry; the mask rules'
    preview showed in other modes; the Map size and Curve options were cut off at 1280 px; tool keys
    did nothing after clicking a tool button.

## Wave 4: fixing the first QA walkthrough

Three agents, each with a list of the report's issues. Each reproduced its issues first; three
turned out to be fixed already.

- **4A Dialogs and input**: questions shown inside the window (Tab, Enter, Escape; automation
  `dialog {button}` and `state.dialog`); tool keys after a tool button; brush radius with `[` and
  `]`, strength with Shift, Ctrl+wheel over a view; undo and redo restore the selection a step had
  and say what was undone; corrected texts; Ctrl+Shift+Z redoes. Tooling: `click {label}`, a `ui`
  that returns the labelled control, `state.overlays`.
- **4B Symmetry and editing**: a selection holding a thing and its partners stays symmetric when
  moved, turned, scaled or deleted (the thing selected first leads, its images follow); deposits
  land on ogrid centres; a new map's spawns are exactly symmetric; the symmetry is saved with each
  map; mirrored units go to the partner spawn's army; curves are mirrored. New selection commands:
  select similar (Ctrl+Shift+G), invert (Ctrl+Shift+I), select inside an area, isolate
  (Ctrl+Shift+H), lock or hide a kind, a pick filter, R and a drag to turn, a "Select anything"
  tool in the Map mode.
- **4C Views and performance**: one owner for the overlay on the terrain; every options row a
  `ToolRow`, with a test that measures every tool's row at 1280 px; effect props as diamonds; path
  links hidden behind hills; the 3D box selection leaves out what the terrain hides; a one-row views
  bar (the top view 464 px high at 1280x800, was 383); distinct overlay colours. On a 4096 map:

  | Span | Before | After |
  |---|---|---|
  | Camera ground after a stroke | 161 ms | 4 ms (only the changed tiles) |
  | Mask update on the first frame | 585 ms | 14 ms |
  | Longest first frame | 702 ms | 94 ms |

  It also found the camera's ground reading the heightmap one sample short per row.

Project format 4: the map's symmetry and a curve's symmetry.

## Wave 5: the stories' remaining parts

After wave 4, 17 stories each had a "Missing" part. Three agents took them, with the tooling wishes.

- **5A Tooling and Lua tools**:
  - Automation answers carry a short state by default (`quiet`, `paths`, `full` to choose);
    `perf save` and `perf before=` compare timings; `ui {type, text}` filters; a command that
    refuses answers `ran: false` with why; `run interactive=true` returns the question it opened;
    `focus` frames a thing at 50 degrees.
  - `faf-map editor start` runs the editor from a shadow copy of its bin folder, so building while a
    test editor runs works. This removed the stop, build, start cycle that slowed every agent.
  - Lua plugins can add mouse tools to a mode, with their own options, one undo step per stroke
    (F-15 done; example `plateau-brush`).
  - The props that seemed missing in 3D were drawn but tiny; cut-out foliage now reads its alpha
    from the top mip levels, so distant trees keep their leaves.
- **5B Content**: presets of a mixed selection (F-10); height stamps and capturing a part of the
  map as one (T-2); new-map templates and Save as template (M-2); dragging in the units tree (U-3);
  starred and recent assets (F-9); walkability marked by hand, where blocked marks become a
  blocking terrain type in the export (T-7); the adaptive `_options.lua`, `_tables.lua` and script
  written in the Archsimkats Valley convention, never over a map maker's own script (R-5).
- **5C Looks**: choosing the terrain shader, with the 3D view blending as it says (S-4); a preview
  drawn with the strata's textures, close to HardFFA's own; the sky's cube map drawn behind the map
  and the water reflecting its cube map (L-1); grid spacing and a decals overlay (V-3); the game's
  unit icons (U-1); reading one asset again (F-8). The asset conversion now also reads
  `textures.scd` (sky textures and unit icons): asset format 4, so every workspace converts again.

## Wave 6: the last story and a second QA walkthrough

- **6B Layouts and gaps**:
  - V-4: the mode panel, inspector, dock and each dock tab go left, right or bottom, or float in a
    window of their own, owned by the editor's window; the layout is remembered; Reset layout.
    Automation reaches floating windows.
  - W-1: a Waves tool places wave generators with a click and aims them with a drag.
  - Plugin tools act at every mirrored place; a plugin command's added props are mirrored; a
    plugin tool's fields sit in the options row and take the brush keys.
  - Decal outlines: outside the Decals mode only large decals are outlined, clipped to the map
    (on Archsimkats Valley 4 outlines in the Terrain mode, was about 3600).
  - The settings file is read part by part: one bad value used to make the whole file read as
    empty, and the next save overwrote it.
  - Tooling: `faf-map editor restart`, `editor expand`, `workspace scratch --convert-into`,
    `workspace init --settings`.
  - The not-built-yet tests use a command registered only in the test, so they depend on no real
    unbuilt feature.
- **6A QA walkthrough**: 32 issues: 1 blocker, 3 major, 17 minor, 11 polish. Nearly all of wave 4's
  fixes held.
  - Blocker: a map made from a settings template could not be exported (6 layers where version 56
    needs 10).
  - Major: shortcuts reached the map from text fields and from behind pages (Ctrl+A selected every
    thing instead of the text); a newly opened large map was not fitted; Save as a template could
    not be named.
  - Performance on Archsimkats Valley: the 3D frame had gone from 2.2 ms (wave 2) to 4.1 ms;
    dragging all 8304 props cost 30 ms per mouse move; the assets were read twice at startup.

With asset format 4, the test workspaces' junctions were pointed at a copy converted by 5C, so the
owner's own assets stayed untouched.

## Wave 7: fixing the second QA walkthrough

- **7A Shell, input and files**: every new map is a version 56 map with 10 layers, and a test
  exports every biome and a template of each; one handler for the window's shortcuts, so a focused
  text field keeps its keys and pages block shortcuts and close on Escape; large maps fitted on
  open; Save as a template asks in the window; Enter applies a field; commands run without a person
  refuse to discard unsaved changes unless given `discard: true`; walkability kept with the project;
  clearer status lines and history labels; a compact symmetry combo. Tooling: `menu {path}`,
  `window {width, height, close}`, key names checked, `click` measures after scrolling settles,
  section headings for `under`.
- **7B Editing, looks and performance**: a placing tool pressed on a deposit selects it; presets
  holding a spawn move the map's spawns and keep its armies, and every preset shows a preview under
  the mouse; civilians keep their army; uncompressed cube maps (`skycube_desert01`) decoded right;
  green foliage at a distance; below the horizon the sky fades into a haze; the inspector shows only
  the active tool's parts. Performance on Archsimkats Valley: the 3D frame back to 2.3 ms, dragging
  all props about 13 ms per move, select all 115 ms, the assets read once.

Project format 5: the walkability marked by hand (`walkability-marks.png`).

## The agents' tooling

What the agents asked for, and where it came from. Most of it is built; the App guide and the
`editor-ui` skill describe the commands, and [agent-tooling.md](agent-tooling.md) lists every
wish with its status.

- **Input as a person gives it** (wave 0): `mouse`, `key`, `type`, `drag`, through Avalonia's raw
  input in the window, never the OS mouse. Built in 1T; held keys in 3C; `click {label}` in 4A.
- **Test editors that do not touch the owner's** (wave 0): scratch workspaces, settings per agent,
  `editor start` detached with a log, `stop` that waits; the shadow copy (5A) so builds work while
  one runs; `restart` (6B).
- **Reading what the window shows**: `ui` with `under` (4A labels, 7A headings), filters (5A),
  `state` keys per feature, `screenshot settle/gpu/part`, `perf` with GPU timing and comparisons.
- **Shell traps**: Bash heredocs with an apostrophe, `sed -i` turning CRLF into LF and Python's
  cp1252 default broke edits in almost every wave. `faf-map dev replace` (2H) keeps a file's line
  endings and UTF-8, and the root `AGENTS.md` explains the traps.
- **Shorter answers**: every answer carried the whole state until 5A made it short by default.
- **Still open** (wave 7's list): a `camera {x, z, distance, yaw, pitch}` command; a way to pull
  real files out of the game's archives; `mouse moves:[...]` at a set rate to measure per-move work;
  `wait` by an id rather than a list index; a wait for "the window has the keyboard"; seeing a
  floating window as the desktop shows it.

## What is still open

- The water on Archsimkats Valley looks milky; it follows that map's own settings, and without
  the game to compare against the formula was left alone.
- About 1.8 s of startup stall (window creation, plugins and the import on the UI thread).
- Panels cannot be moved by dragging their header, and panels sharing a side have no splitter.
- Moving a wave generator does not move its mirrored copies; a plugin command's height and mask
  edits are not mirrored automatically (the plugin repeats them at `map.images`).
- The first real upload to the FAF vault waits for the owner (`TODO.md`).
- A workspace converted before asset format 4 converts again on its next start (about 30 s).

## Lessons

- **Order by dependencies, then fix by QA.** Building waves 0 to 3 by the dependency graph, then
  alternating a QA walkthrough with a fixing wave, found what tests did not: questions automation
  could not reach, shortcuts firing from text fields, symmetry broken by selections holding
  partners, rows cut off at 1280 px.
- **QA as a person.** The walkthroughs used the mouse and keys and looked at every screenshot;
  most of their issues were invisible through `run` alone.
- **Ask for the tooling gaps.** The Tooling section of every report made the agents' friction
  visible, and building it (the shadow copy above all) made the later waves faster.
- **Conflicts come from shared files and shared assumptions.** Most conflicts were in the same few
  files (`EditorModes.cs`, `Menus.cs`, the automation state, `USER_STORIES.md`, the guides). The
  harder clashes compiled cleanly and only failed in the tests: one branch built the command another
  branch's tests used as their "not built yet" example, and a QA fix changed status texts that
  another branch's tests compared.
- **Smaller waves.** Eight agents in parallel ran into the spending limit; two or three agents per
  wave kept the merges and the reports manageable.
