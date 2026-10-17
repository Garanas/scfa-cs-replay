---
name: editor-tours
description: How to write, change and check a guided tour of the FAF map editor (a *.tour.json file that dims the window, rings a control and explains it) and the static guide made from the tours with faf-map guide. Use when adding a tour or a step, when a change to the window breaks a tour (TourTest fails), when a tour's text must follow a changed control, and when making or checking the guide in docs/guide.
---

# Tours of the map editor

A tour is a JSON file. The editor plays it over the window (user story H-1); `faf-map guide` plays
the same file in a test editor and writes a static guide with pictures (H-2). The full rules and
their reasons are in `src/FAForever.MapEditor.App/AGENTS.md`, section "Tours and the guide"; this is
the short form to work from. Read `src/FAForever.MapEditor.App/Tours/terrain.tour.json` as the
example: it has every kind of step.

## Where a tour lives

- **The editor's own tours**: `src/FAForever.MapEditor.App/Tours/<id>.tour.json`, embedded by a
  wildcard (nothing to register). When a workspace opens they are written into its `tours/` folder,
  as the example plugins are into `plugins/`.
- **A map maker's tour**: any `*.tour.json` in the workspace's `tours/`. The editor reads the folder
  again when a file changes; a file it cannot use shows with its error on Help, Tours and in
  `state.tour.errors`.
- **Changing a shipped tour**: the workspace keeps the copy written before. Before you change a
  shipped file, add the SHA-256 of its current text (lines ending in `\n`) to `EditorTours.Earlier`
  in `Tours/TourCatalog.cs` as `"<file>:<hash>"`, so an untouched old copy is brought up to date.
  A copy the person changed stays theirs.
- **The order on the Tours page**: `order` (window 10, sketch 15, terrain 20, masks 30, paint 40, markers 50,
  units 60, procedural 70). A new mode's tour takes a free number in that order.

## The file

```jsonc
{
  "id": "terrain",                       // letters, digits, - and _; the file is <id>.tour.json
  "title": "Shaping the terrain",
  "summary": "One line for the Tours page.",
  "order": 20,
  "needs": ["Map"],                      // an open map
  "start": [{ "run": "terrain.mode" }, { "run": "view.top" }],   // run when the tour starts
  "steps": [
    { "id": "welcome", "title": "...", "text": "..." },          // no target: the card in the middle
    { "id": "tools", "target": { "label": "Tools" }, "title": "...", "text": "..." },
    {
      "id": "top-view",
      "target": { "label": "Top view" },
      "before": [{ "run": "view.top" }, { "run": "tool.terrain.sculpt" }],  // run on entering, going on or back
      "title": "Try it: make one stroke",
      "text": "Press the left mouse in the top view and drag ...",
      "until": { "path": "edits", "value": 1 },                  // a step that asks the person to act
      "doItForMe": { "run": "brush.stroke", "points": [[90, 112], [140, 112]] },  // required with until
      "done": "The ground rose where you dragged.",              // the card once until holds
      "guide": { "more": "Longer text for the guide only.", "margin": 60, "skip": false, "after": true }
    }
  ]
}
```

Comments and trailing commas are allowed; a misspelt field is an error. Every step needs a unique
`id`, a `title` and a `text`.

### Targets: what the ring goes round

| Target | Finds | Use for |
|---|---|---|
| `{ "label": "Sculpt", "type": "Button", "under": "Tools", "index": 0 }` | a control as automation's `click` finds it: its `AutomationProperties.Name`, `x:Name` or text; `type` and `under` narrow it, `index` picks among several (from 0) | buttons, chips, fields, tabs, a list by its label (`Tools`, `Palette slots`) |
| `{ "part": "inspector" }` | a region: `menu`, `rail`, `panel`, `options`, `views`, `top`, `3d`, `dock`, `inspector`, `status` | explaining a whole area |
| `{ "ref": "marker:3" }` or `{ "x": 96, "z": 96 }` | a place of the map in the view that shows | a spawn, a deposit, a spot to click |
| none | the card in the middle of the window | welcome and closing steps |

Find the label in a running editor, never by guessing: `faf-map editor ui type=Button text=sculpt
--port <p>`, `ui under=Tools`. A control without a good label gets one
(`AutomationProperties.Name`, `HelpText`; the editor-ui skill). A setting behind a tool row's More,
a panel the person hid or floated, and a control scrolled out of sight are brought into view by the
tour itself; the layout is put back when the tour ends.

**Not possible**: an item inside an open menu (a popup). Ring the menu's header (`{ "label": "File" }`)
and name the item in the text.

### Steps that ask the person to act

`until` is `{ "path", "value", "prefix"? }` on the editor's state; the step waits, the ring's hole lets
the mouse through, and Next becomes available once it holds (Skip before). `doItForMe` is required.
The paths a tour may wait on (`Tours/TourState.cs`; a new one is added there with a line in
`TourTest`'s paths test, never read from the whole state):

| Path | Holds when |
|---|---|
| `tab` | the view: `top`, `3d`, `side` |
| `mode`, `tool` | a mode's id (`terrain`), a tool's id (`terrain.sculpt`) |
| `dock`, `palette` | the dock's tab; the command palette open (`true`) |
| `selection.count` | that many things selected |
| `show.<overlay>` | an overlay's chip on (`show.walkability: true`) |
| `generated.<result>` | a generated result's status (`generated.walkability: Current`) |
| `units.place.unit` | the Place tool's unit (`uab1103`) |
| `layout.modePanel`, `layout.inspector`, `layout.dock` | a panel shown |
| `edits` | that many edits made **since the step began** (a stroke is one) |
| `undo` | the last undo step's name, and an edit made since the step began; `"prefix": true` for names that get a number (`Add the height layer Sculpt2`) |
| `coverageStacks.shown` | the Masks mode's mask or the Paint mode's stratum (`stratum-2`) |

The condition is first read after the step's `before` commands ran, so put the state the person
should change into `before` (the Ruler chosen before "choose Sculpt"). Keep acts cheap and safe: one
step of undo, nothing the next tour trips over. Try a condition by hand: `faf-map editor wait
path=tool value=terrain.sculpt --port <p>`.

### Actions

`{ "run": "<command id>", ...arguments }`; a command whose own argument is called `id` or `run`
takes its arguments in `"args": { ... }`. They run without a person, so they ask nothing (give the
arguments a picker would ask for). Never allowed (`Tours/TourRefusals.cs`; the file is refused when
read): `file.*` (open, import, new, save, export, upload, push, commit), `workspace.*`,
`diagnostics.*`, `help.reportProblem`, `help.tour*`, the Help folders, `assets.brushesFolder`,
`plugin.*` and the plugins' reload, enable, fields, checks and folder. A tour about export or upload
rings the control and explains it; the person presses it. `faf-map editor commands` lists the ids and
arguments.

## Writing the text

Tour text is UI text (the repository's writing rules apply):

- English, plain, short sentences, for a newcomer who never saw the editor.
- Name things as the window labels them (the row reads "Mass", so not "Mass deposits").
- Say what the ringed thing is for, then what to do with it; one idea per step, 8 to 14 steps a
  tour. A welcome step says how many steps and that Escape ends the tour; the last step points to
  the next tour.
- An act step's title starts "Try it: ..." and its text says exactly what to click or press; `done`
  says what happened and how to undo it.
- State what is true now; never compare with an earlier version of the editor.
- Never mention a control the step does not ring and the person cannot see.
- No text belongs in the guide's pictures: what the guide needs beyond the card goes in `guide.more`.

## Check it

```sh
# a scratch workspace and editor of your own (never port 5371, never the user's workspace)
faf-map workspace scratch D:/wt/ws-x --from D:/wt/ws-22a --settings D:/wt/ws-x-settings.json
faf-map editor start --port 5395 --settings D:/wt/ws-x-settings.json --refresh-tours   # writes the edited tours over the old copies
faf-map editor run id=help.tourOffer answer=never --port 5395      # the first-start card out of the way
faf-map editor window width=1280 height=800 --port 5395
faf-map editor tour id=terrain step=3 --port 5395                  # by number from 1 or by step id
faf-map editor state path=tour --port 5395                         # target.found, target.bounds, target.why, card.bounds, waiting, done, problem
faf-map editor screenshot path=D:/wt/x/step3.png settle=true --port 5395   # then look at it
faf-map editor click label=Sculpt under=Tools --port 5395          # do the act step as a person would
faf-map editor run id=help.tourDo --port 5395                      # or Do it for me; help.tourNext, help.tourBack, help.tourEnd
faf-map editor stop --port 5395
```

For every step: the target is found and `why` is null (the mouse reaches it), the card does not
cover what its text talks about, and the screenshot reads right. Do each act step by hand and with
Do it for me, go Back across it, end the tour half way (view, mode, tool, layout and More are put
back), and run it again on the same map (the step must not be done at once). Check the window tour
and your tour at 1024 by 700 too.

Tests (`--artifacts-path D:\wt\art-<x>`, a short path):

```sh
dotnet test tests/FAForever.MapEditor.App.Tests --artifacts-path D:/wt/art-x --filter "FullyQualifiedName~Tour"
```

`TourTest` plays every step of every shipped tour at 1280 by 800 and 1024 by 700 (target found and
reachable, card inside the window) and runs Do it for me on every act step; `TourWindowTest` checks
the overlay. A new tour needs no test of its own. A failure names the tour and step: fix the label,
the target or the `before`, never the test. A test window that lacks what an act step needs (a palette
slot, a unit in the catalogue) gets it in `TourTest.Open`.

## The guide

Made on the owner's computer (the game's textures), never in CI, never in the user's workspace:

```sh
faf-map guide build --out docs/guide --port 5399 --settings D:/wt/guide-settings.json [--tours terrain,masks]
faf-map guide check --out docs/guide --port 5399 --settings D:/wt/guide-settings.json   # after a change to the window
faf-map guide render --out docs/guide                                                   # pages again after a text change, no editor
```

`build` starts its own editor with `--refresh-tours`, plays each step without the card (the ring and
dimming only), crops round the target (`guide.margin`, 40 px by default), takes an after picture of a
step with `doItForMe` (`guide.after: false` leaves it out), undoes the map between tours, and writes
`guide.json`, the pictures and a Markdown and an HTML page per tour. A step without a target has no
picture; `guide.skip` leaves a step out of the guide. `check` fails on a target not found and notes
moves of more than 8 pixels. `docs/guide/README.md` has the steps.

## Before you finish

- `TourTest` and `TourWindowTest` green; the App suite green.
- Every step looked at in a running editor; act steps done both ways.
- A shipped tour changed: its old hash in `EditorTours.Earlier`.
- A new path, target kind or refused command: in `TourState`/`TourLocator`/`TourRefusals` with a test,
  and in the App guide's "Tours and the guide".
- `docs/map-editor/user-stories.md` H-1 says which tours exist.
