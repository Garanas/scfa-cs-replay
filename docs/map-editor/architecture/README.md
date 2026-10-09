# The editor window's architecture: analysis and directions

An analysis of `src/FAForever.MapEditor.App` (branch `feature/map-editor`, 2026-10-09), split by
topic. Each file says what the code does today, what that costs a reader or a change, the
direction proposed, what to avoid, and when it is done. Paths without a project are relative to
`src/FAForever.MapEditor.App`.

The aim: a window that stays easy to reason about while the user stories fill in (56 commands
and some 20 tools are still marked as not built), with model, view and controller apart, and
Lua plugins later (user story F-15).

## The yardstick: less to hold in one's head

A change is worth making when a reader needs to know less afterwards to understand or change a
part. An abstraction is not a goal; it earns its place only when it takes knowledge out of the
code that uses it.

- **Concrete classes first.** An interface only where a second implementation exists (tests
  count: `IEditorDialogs`), or where code must work with things it does not know (the views
  with "the active tool").
- **One place per fact.** A list copied by hand into a second place becomes one list, and the
  other place is generated from it. Duplicate code is fine when the copies may diverge; it is
  not when they must stay equal.
- **Behaviour next to its data.** What a tool does is in one file, not in five.
- **One framework, in one place.** Commands go through a mediator with a pipeline (file 01); it
  brings a dependency injection container, which stays at the start of the app. Beyond that: no
  messenger or event bus, no reactive library, no generic property editor before Lua needs one.
  Who talks to whom stays visible in constructors and method calls.
- **Leave what works.** Several findings end in "no change"; they are listed so nobody spends
  time on them.

## What is good and stays

- The model is in the library (`FAForever.MapEditor.Project`): an immutable `MapDocument`,
  `EditSession` with transactions, undo as versions of the document. Saving in the background,
  painting during a save and plugins computing on a snapshot all follow from it.
- One `CommandRegistry` that the menus, shortcuts, palette and automation run commands through,
  with `Register` for commands made at run time.
- Modes and tools as data (`EditorModes`), and what is not built yet visible with its story.
- `IEditorDialogs`: the view model uses no Avalonia types and is tested without a window.
- Built to be checked: automation, `UiTree`, snapshots, style rules, performance spans,
  telemetry.

## The findings

| File | Topic | Priority | Effort |
|---|---|---|---|
| [01-commands.md](01-commands.md) | One route for every operation: requests with handlers, through a mediator and its pipeline | high | medium to large |
| [02-tools.md](02-tools.md) | What a tool does, in one class instead of the views and the view model | high | medium |
| [03-main-view-model.md](03-main-view-model.md) | `MainViewModel` holds everything | medium | medium |
| [04-duplication.md](04-duplication.md) | Lists kept by hand in two or three places | medium | small |
| [05-change-notifications.md](05-change-notifications.md) | Coarse events and refreshes | low (measure first) | small |
| [06-lua-plugins.md](06-lua-plugins.md) | What Lua plugins need on top of 01 to 03 | later | large |
| [07-small-points.md](07-small-points.md) | Smaller points, several of them "leave it" | low | small |

## Order

Each step can be merged on its own and leaves the editor working.

1. **Built**: commands as requests with handlers through MediatR, the undo step in the pipeline,
   automation through the registry, the duplicate `RelayCommand`s gone (01). The modes', tools'
   and overlays' commands are made from their data, as file 04 proposed.
2. **Built**: tools as classes (02), each with its own settings and radius; the views send the
   left mouse to the active tool.
3. **Built**: the lists generated from one source: overlays, modes, the tabs as enums, the
   Markers panel's check boxes from the overlays (04).
4. **Built**: the loose parts out of `MainViewModel`: diagnostics and console, palette, workspace
   setup, version control, the camera's ground (03).
5. Measure, then make the notifications finer where it shows (05).
6. Lua (06), once 1 and 2 have shown which hooks built-in code needs; plugins use the same ones.

Do steps 1 and 2 before the next user stories: every operation and tool added before them is
written in the places these steps take away.
