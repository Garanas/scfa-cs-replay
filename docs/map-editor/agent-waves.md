# Building the map editor in agent waves

The map editor's user stories (`USER_STORIES.md`) were built by agents working in parallel, in
eight waves, between the brushes work and 2026-10-10; then the owner looked the editor over and
asked for a rework, built in five more waves (8 to 12) up to 2026-10-11. This page describes how the
waves were run, what each one built, what the three QA walkthroughs found, and what is still open. The commits are on
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
| 8 | 3 | The rework's foundations: layer stacks, panels and shell, the Procedural mode and export steps | 495 / 425 |
| 9 | 4 | Draft and final heightmaps, masks and stratum stacks, decals, path nodes | 521 / 489 |
| 10 | 5 + QA | Sky cube map, unit templates, the Procedural list, DDS export, mass and energy icons; a third QA walkthrough | 545 / 519 |
| 12 | 3 | Fixing the 31 issues of the third walkthrough (wave 11 was the walkthrough) | 583 / 542 |
| 13 | 3 | The refinements: who owns the inspector and Units, Procedural derived and automatic, civilian presets and smaller changes | 608 / 556 |
| 14 | 3 | The Map, Markers and Plugins modes as lists, Clear, blank markers | 636 / 559 |
| 16 | 3 | Fixing the fourth walkthrough (wave 15) and Linux | 666 / 579 |

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

## Waves 8 to 12: the rework

After wave 7 the owner used the editor and sent feedback: masks serve another purpose than stratum
masks and need their own tab; stratum masks, masks and the water's maps should be stacks of layers
like the heightmap; the heightmap should have a draft (for World Machine and Gaea) and a final; water
and lighting had settings on both sides; map-wide decals, decal sets with `_mask` files, unit
templates from real maps, a keyboard shortcuts page, workspace settings, mass and energy on tiles,
hover previews, a Procedural tab, the navigation mesh with path nodes generated at export, the editor
named in `_scenario.lua`, presets in one folder. The plan, with the owner's decisions, was an
artifact ("Map Editor Rework"); its decisions are in the memory note `editor-rework-plan`.

- **Wave 8, foundations**: 8A one layer-stack model and compositor for heights and coverage (the
  height layers moved onto it, a reusable stack control, `ImagePreview`, project format 6); 8B "the
  panel lists, the inspector edits" for Water and Lighting, the Masks and Procedural modes and a
  Plugins entry on the rail, Help's shortcuts and folders, File › Workspace settings, one `presets/`
  folder, clearing the palette, mass and energy on tiles; 8C the Procedural mode, walkability
  regenerating after a second, export as steps (the preview always rendered), the editor named in
  `_scenario.lua` (checked against the game, the FAF client and the vault's upload), the navigation
  mesh with regions and routes. The merge broke 147 tests at once: two branches defined the same
  icon key.
- **Wave 9, on the foundations**: 9A draft and final heightmaps (shared layers at a place of their
  own in each, the switch, files for World Machine and Gaea in `procedural/temp`, a watched final
  base); 9B every stratum mask, mask and water map a stack, rules as layers, the Masks mode filled
  in; 9C map-wide decals written as Kaali (`kaali.v0004`) has them (the normal decal's red channel
  found in the game's `terrain.fx` to be its blend weight), decal sets, decal-mask layers, DDS with
  mipmaps; 9D path networks from the navigation mesh (skeleton, smoothing, links), previewed only,
  edited by hand on top, written at export.
- **Wave 10**: 10A a sky cube map generated from the map and its version 60 sky; 10B a scan of
  15,374 unit groups of 266 campaign, coop and vault maps and 36 example unit presets placed by army
  role; 10D the Procedural mode as a clickable list (the owner's request during the wave); 10E every
  exported texture as DDS (the owner's request); 10F mass and energy icons (the owner's request).
  The hover previews were cut to a strip by Fluent's tool-tip width: fixed by the main session.
- **Wave 11**: a QA walkthrough of the rework, point by point against the feedback: 31 issues, none
  a blocker; 21 of 32 points worked, 11 partly.
- **Wave 12, the fixes**: 12A the inspector shows what was chosen last, layer fields filled on open,
  `click` and `set` aim and refuse rather than miss; 12B the map's own decals and sets on the shelf,
  adding files to the map, decal-mask fields, masks never exported, Kaali's layout imported into the
  slots; 12C enemy presets get an army, Clear the palette can be taken back, props from rules and path
  node edits mirror, exports leave the history alone, kept results current on reopen, and a strata
  stroke on Archsimkats Valley from 13.8 to 1.8 ms per move.

## Waves 13 to 16: the refinements

After the rework the owner sent thirteen refinements; the analysis with the owner's decisions is the
artifact "Map Editor Refinements" (memory note `editor-refinements-plan`). The decisions that shaped
the work: Map, Markers, Units and Plugins became "pick on the left, edit in the inspector"; choosing
an entry keeps the map's selection; generated results are derived and never a step of undo;
results regenerate on their own only while the views show them; unit presets are civilian bases.

- **Wave 13**: 13A who owns the inspector (the last choice of a tile, the selection or a part) and the
  Units mode; 13B routes removed, connected regions, results derived into the current version without
  steps, every generator able to regenerate on its own while visible, the Auto switch, procedural
  defaults in `workspace.json`; 13C 38 civilian presets from a new scan of 266 maps (26 settlements, 12
  small wreck fields), reclaim computed at export, presets at the bottom, Q, E and alt with the wheel
  turning a brush. Wave 14 started as soon as 13A was merged, alongside 13B and 13C.
- **Wave 14**: 14A the Map mode (an entry per army and area), 14B the Markers mode with `markers.clear`
  for every kind and blank markers as a kind of their own (the owner's request during the wave), 14C
  the Plugins mode with a category and a kind.
- **Wave 15**: a QA walkthrough of the fourteen points: 0 blockers, 1 major (kept results not per
  version), 10 minor, 16 polish.
- **Wave 16**: 16A results kept per version of their inputs (undo shows them at once), "Connected
  regions" as the result's name, the Auto chip in the status bar, Q and E from anywhere; 16B one look
  for entries in every list mode; 16C Linux: the test suites pass on Ubuntu, file names and paths
  found whatever their case where the game's files are, Linux folders and Steam's game folder, software
  OpenGL on request, a 3D start check in CI. The asset lookups proved case-proof already
  (`faf-map dev resolve`: 311 assets of four maps, 85 in mixed case, all found on Linux before and after).
- Beside the waves: a workflow publishing standalone editors for Windows, Linux and macOS on every push
  to `main`, with a start check of the Linux build on a virtual display.

## Waves 17 and 18: richer height curves

The owner evaluated nineteen proposals for the curve layers on the artifact "Height curve upgrades"
(memory note `curve-upgrades-plan`): everything built but surface roughness (dropped: ground stays
flat where units walk) and curves as masks (not decided). Decisions: a node overrides width, both
falloffs, height, strength and a sharp corner; noise alike in every part of a symmetric map; closed
curves fill (older ones load as line only, project format 9); smooth heights for every curve;
variation along a curve as ranges. Height per node mattered most, for ramps.

- **Wave 17**: 17A the library and commands (per-node values, monotone heights, fade shapes and
  terraces, cliff face, edge noise and variation ranges matching across symmetric parts, closed fills,
  river, road modes and camber, reverse, set a point, even grade, snap to the ground, grades; a ridge
  of 520 ogrids on the 81 km map now 8 to 12 ms an edit, 40 to 70 before); 17B decals along a curve.
- **Wave 18** (agents on Sonnet from here, the owner's choice): 18A the window (the inspector's groups
  by profile, the node table with grades, Reverse, Even the grade, Snap, a point's height by shift and
  a drag or + and -, the new point's height from the ground, width and fade lines in both views);
  18B cliff faces block walkability unless marked open (checked after the merge: a gentle cliff's face
  walkable with it off, 49,083 passable ogrids, blocked with it on, 46,685).
- Both wave 18 agents were cut off by the usage limit and restarted; the restarted agents found and
  kept their predecessors' work in the worktrees.
- The shared converted assets were still format 4 after another session raised
  `AssetConverter.FormatVersion` to 5: no agent's test editor got past the update page until the main
  session converted them (`faf-map workspace convert D:/wt/scratch-5c/wsconv`, 32 s). Check the
  assets' format when setting up a wave.

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
- **Since wave 8**: `restart` and `expand` (6B), `menu` and `window` (7A), `click` and `set` that
  aim, wait for the control to settle and refuse rather than land elsewhere (12A).
- **Still open** (wave 7's list): a `camera {x, z, distance, yaw, pitch}` command; a way to pull
  real files out of the game's archives; `mouse moves:[...]` at a set rate to measure per-move work;
  `wait` by an id rather than a list index; a wait for "the window has the keyboard"; seeing a
  floating window as the desktop shows it.

## What is still open

Written at the end of wave 16 (2026-10-11), in the order it should be done. Everything below is
`feature/map-editor` at a3c98f8 or later; nothing of it is pushed, and the branch is not merged into
`main`.

### 1. Proof in the game (needs the owner, with the game)
No map made with the editor has been loaded in Forged Alliance or FAF yet. The tests prove that
maps round-trip through the editor's own reader and writer (98 vault maps), not that the game draws
and plays them the same. To compare in the game, with two or three exported maps (Kaali, one with
decals and a generated sky, one adaptive map):
- the terrain, strata and the map-wide decals in Kaali's layout (albedo, normals, shadow);
- the generated sky cube and the skybox (also in `TODO.md`), the water (Archsimkats Valley looks
  milky in the editor), the textures converted to DDS;
- the reclaim totals in the lobby, the adaptive scripts and optional groups, wrecks;
- the markers: spawns, deposits, blank markers, chains, and the generated path networks the AI uses.
- The first real upload to the vault with a real account (`TODO.md`).

### 2. CI and review
- Neither `test.yml` nor `map-editor.yml` has run on this work: both run on `main` (the tests also
  on pull requests to `main`). Push the branch and open a pull request so the tests run on GitHub's
  Linux runner; run `map-editor.yml` by hand on the branch (or give it a `pull_request` trigger) to see
  the three builds, the Linux start check and the 3D check.
- `feature/map-editor` holds 16 waves; it has never had a review. Merging it into `main` is the
  owner's call.

### 3. Shipping it to people
- Builds are workflow artifacts that expire after 90 days: no GitHub Releases, version numbers or
  changelog yet.
- The builds are unsigned: SmartScreen warns on Windows, Gatekeeper blocks the macOS executable, and
  macOS expects a `.app` bundle. The macOS build has never been started.
- No getting-started guide for map makers beyond the keyboard shortcuts page.
- Crash reports and telemetry stay on the user's computer: sending works, but no server collects them,
  and "Report a problem" writes a zip nothing uploads.

### 4. The editor
- The Props mode is the last mode whose settings (Placing) are edited in the panel rather than the
  inspector.
- The Lighting panel still has a "not built yet" entry for a darkening shadow decal
  (`LightingSettingsViewModel`, `PlannedPart` of L-1), while wave 9 built the map-wide shadow decal:
  check and remove it.
- Symmetry: importing a map does not restore its symmetry; moving a wave generator does not move its
  mirrored copies; scaling and turning about the selection's centre are not mirrored; a plugin
  command's height and mask edits are not mirrored by themselves (the plugin repeats them at
  `map.images`).
- Not built yet (the App guide's lists): team colours in the views; hydraulic erosion; limits for the
  water and mask brushes; the map size's rectangle and handles in 3D; dragging a panel by its header
  (and a splitter between panels sharing a side); choosing toolbar commands in the window; the vault
  upload's licence, its versions page and cancelling an upload; a height per curve point in the views
  and merging layers; a mark of the drop place; dashed box outlines and a 3D ruler label; a line while
  a path link is dragged; placing a units preset as wrecks; `generate.stale` making props from masks
  generated in the same run.
- A plugin's `kind` is not checked against what it registers.
- Speed: about 1.8 s of stall at start-up (window, plugins and the import on the UI thread); a
  mask-reference layer added on a 4096 map stalls the window for a few hundred milliseconds. The editor
  runs 8 to 15 times slower while its window has no focus (Windows' power throttling), so timings from
  a test editor compare only with each other.
- A workspace converted before asset format 4 converts again on its next start (about 30 s).
- Seen once in wave 16 and not chased: the Connected regions chip was off after a mode switch; the
  sandbox starts with nothing set to regenerate on its own.

### 5. Platforms
- Linux works (tests, start, 3D under Xvfb); its file dialogs and opening folders in a file manager
  were not tried. Without a graphics card the 3D view needs `FAF_MAP_EDITOR_SOFTWARE_GL=1`.
- macOS is built by the workflow and untested: its OpenGL 4.1, Avalonia's OpenGL control there and
  its file dialogs are unchecked.

### 6. Tooling
73 wishes of the agents, about half open: `agent-tooling.md`.

### Housekeeping
The worktrees of waves 13 to 16 (`D:\wt\13a` ... `D:\wt\16c`, `D:\wt\15qa`) and their branches
`wave13/*` to `wave16/*` are merged and can be removed; their scratch workspaces `D:\wt\ws-*` hold
nothing that is not in the repository (their `assets` are links to shared converted assets: remove the
link, never the target).

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
