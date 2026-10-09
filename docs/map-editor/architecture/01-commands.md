# 01. Commands as requests with handlers, through a mediator

Priority high, effort medium to large. User story F-1 asks for it: "Every operation is a command
with a name, arguments and a description, in one registry".

**Built** (branch `feature/editor-commands`, 2026-10-09): every command is in
`src/FAForever.MapEditor.App/Commands/`, and the App's guide describes it. "Before" says what it
replaced; the direction below is what was built, with "As built" where it differs from the plan.

## Before

An operation reaches the view model along three routes:

1. **The registry.** Methods with `[EditorCommand]` on the controllers (86 of them, plus one
   `tool.*` command per tool). Menus, shortcuts, the palette and automation's `run` use it.
   A command method may not take arguments: `CommandRegistry.Add` refuses it
   (`Commands/CommandRegistry.cs:64`).
2. **`[RelayCommand]`s on `MainViewModel`.** `OpenProjectCommand`, `ImportMapCommand`,
   `SaveProjectCommand`, `SaveProjectAsCommand`, `ExportMapCommand`, `UndoEditCommand`,
   `RedoEditCommand` (`ViewModels/MainViewModel.cs:433-485`). No view binds to them: only
   `FileController` (`Controllers/FileController.cs:22-34`) and the tests call them. They exist
   because asking for a folder has to happen somewhere and a command could not take one.
3. **Automation's own `switch`** (`Automation/AutomationServer.cs`, from line 120). `open`,
   `import`, `save`, `save-as`, `export`, `undo`, `redo`, `goto`, `mode`, `tool`, `brush`,
   `fit`, `show` and others call the view model directly, because they need arguments.

What every command shares is done in two places: `CommandRegistry.RunAsync` counts and logs a
run, and `MainViewModel.BusyAsync` marks the editor busy, times and logs opening, saving and
exporting. An operation that makes several edits makes several undo steps, and one that fails
halfway leaves the first ones in the history.

The list of automation commands is written three times: the `help` array, the XML comment of
`AutomationServer` and the App's `AGENTS.md`. They already differ (the XML comment lacks
`mode`, `tool`, `ui`, `perf`, `logs`, ...).

`UndoHeader` and `RedoHeader` (`MainViewModel.cs:132-134`) are left from a menu made by hand:
only `MainViewModelTest` reads them.

## What it costs

A new operation is written in up to six places: a method on the view model, maybe a
`RelayCommand`, a controller method, a case in automation, a line in `help`, a line in the
guide. A reader who wants to know "what does Save do" follows `FileController.Save` to
`SaveProjectCommand` to `SaveProjectAsync` to `SaveAsync` to `BusyAsync`; automation's `save`
skips the first three, so the two can behave differently (automation never asks for a folder
when the map has none).

## Direction

Every operation is a **request** (a record: its properties are the arguments) with one
**handler**, sent through a mediator whose **pipeline** does what every command shares. The
registry stays as the catalogue of commands: what they are called, where they show, when they can
run.

### A request and its handler, in one file

One file per command, named after it, in a folder per menu (`Commands/File/OpenProject.cs`). The
request and its handler sit together, so "go to definition" on the request finds the code that
runs one screen down.

```csharp
[EditorCommand("file.open", "Open project...", Gesture = "Ctrl+O", Story = "M-7", Needs = Needs.Idle)]
public sealed record OpenProject(string? Folder = null) : IRequest;

public sealed class OpenProjectHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<OpenProject>
{
    public async Task Handle(OpenProject request, CancellationToken cancellation)
    {
        if (!await model.ConfirmCloseAsync())
        {
            return;
        }
        string? folder = request.Folder ?? await dialogs.PickFolderAsync("Open a project", model.Workspace?.ProjectsFolder);
        if (folder is not null)
        {
            await model.LoadProjectAsync(folder);
        }
    }
}
```

- **The arguments are the record.** Automation's JSON deserializes into it with
  `System.Text.Json`, and later a Lua table the same way. No conversion per argument type.
- **A missing argument is asked for.** The menu sends `new OpenProject()` and gets the dialog;
  automation, the CLI and Lua give the folder. The seven `RelayCommand`s and their
  `NotifyCanExecuteChanged` calls go. Where the menu and automation should differ (automation's
  `close` does not ask about unsaved changes), that is an argument too (`Force`).
- **Handlers call the view model, never the mediator.** An operation that needs another one calls
  the view model's method; that keeps runs from nesting (see "One undo step" below).

### Not built yet: a request without a handler

```csharp
[Incomplete("F-11", "Copy the selected markers, props, decals or units")]
[EditorCommand("edit.copy", "Copy", Gesture = "Ctrl+C", Story = "F-11")]
public sealed record Copy : IRequest;
```

The planned commands of a menu share one file (`Commands/Edit/Planned.cs`): a line each, no empty
handler. Building one means moving its record into its own file and adding the handler. A test
checks that every request has a handler unless it is marked `[Incomplete]`, and that none marked
has one.

### The registry: the catalogue

The mediator sends a request; it does not list commands, know their titles or shortcuts, or say
whether one can run now. `CommandRegistry` keeps doing that. At start it finds the request types
with `[EditorCommand]` and keeps an entry per command: id, title, gesture, story, `Needs`,
whether it is built, and how to make its request. `RunAsync(id, arguments)` makes the request from
the arguments, checks `Needs`, and sends it.

Commands made while the editor runs are entries whose request has a parameter: `tool.terrain.sculpt`
sends `new ChooseTool("terrain.sculpt")`, `terrain.mode` sends `new ChooseMode("terrain")`, an
overlay's command `new ToggleOverlay("markers")` (file 04), a plugin's command
`new RunPluginCommand(...)` (file 06). The container is built once at start; these need no
handler of their own.

**Everything sends through the registry**: menus, shortcuts, palette, automation, Lua. No view or
handler calls the mediator itself, so every route gets the same checks.

### Where a command shows: the menu layout in one file

With the commands in separate files, the order of methods on a controller no longer gives the
order of a menu. `Views/Menus.cs` lists the menu bar: each menu, its command ids in order, and its
separators. The request says *what* a command is, the layout *where* it shows. A test checks that
every id in the layout exists; a command in no menu is still in the palette and automation.

### Run without a person

**As built**: `RunAsync(id, arguments, interactive)`. Automation (and later Lua) runs with
`interactive: false`, and the dialogs the handlers and the view model ask through (`DialogGuard`)
refuse a question while it does, saying which argument to give. So "a request with its arguments
asks nothing" holds for every command without each handler checking it.

### When a command can run: `Needs`

Before, `CanRun` named a property of the controller. With handlers in their own classes it becomes a
flags enum the registry checks itself: `Needs.Map`, `Needs.Idle` (not busy), `Needs.Undo`,
`Needs.Redo`. The four cover what the controllers' `CanRun` properties say today. The menus ask
the registry, and the registry refuses to send a request whose needs are not met (`ran: false`).
A command with a condition the flags cannot say gets a flag for it, not a property name.

### The pipeline

Three behaviours were planned: measuring, busy, and one undo step.

**As built**, the pipeline has one behaviour, the undo step (below):

- **Measuring stays in the registry.** Every run passes `CommandRegistry.RunAsync`, which knows
  the command's id; a behaviour knows only the request's type, and the commands made from data
  (`ChooseTool` for every `tool.*`) share one type. The registry counts each run by result and
  the view model logs it.
- **Busy stays in the view model** (`BusyAsync`). The operations (`LoadProjectAsync`,
  `SaveAsync`, ...) are also called by tests, by the start and by the close dialog, outside any
  command, and must still mark the editor busy and reload what changed meanwhile.

A behaviour is worth adding for what belongs to the request's type and holds for every route to
it; the next is likely the plugins' (file 06).

### One undo step, with the attribute

The editor keeps every version of the document already (`MapDocument` is immutable, `EditSession`
keeps the versions), so undo needs nothing saved. What the behaviour adds is that a command is one
step, and that a failing command leaves nothing:

- It opens `session.Begin(<the command's title>)` before the handler runs.
- It commits when the handler returns. `Commit` keeps nothing when the document did not change.
- It disposes the transaction when the handler throws: the half-made edit is gone.

No command of the editor edits the map yet (the edits are brush strokes); `CommandRegistryTest`
checks the behaviour with a command of its own. The handler edits through the transaction it is
given. The behaviour puts it in `CurrentEdit`, a
small class the handler takes in its constructor; asking it for the transaction in a command
without `[UndoStep]` throws, and the message says to add the attribute.

```csharp
[UndoStep]
[EditorCommand("map.resize", "Size and playable area...", Story = "M-3", Needs = Needs.Map)]
public sealed record Resize(int Width, int Height) : IRequest;

public sealed class ResizeHandler(CurrentEdit edit) : IRequestHandler<Resize>
{
    public Task Handle(Resize request, CancellationToken cancellation)
    {
        edit.Transaction.Apply(document => document.Resize(request.Width, request.Height));
        return Task.CompletedTask;
    }
}
```

Transactions do not nest: `EditSession.Begin`, `Edit`, `Undo`, `Redo` and `GoTo` throw while one
is open. So:

- Undo, redo, `goto`, open, import, save, export and the view's commands (tabs, overlays, the
  camera) have no `[UndoStep]`.
- A brush stroke holds its own transaction while the mouse is down (file 02). The registry
  answers `ran: false` for an `[UndoStep]` command during a stroke, rather than letting `Begin`
  throw.
- Handlers do not send requests (above), so a run never opens a second transaction.

### Automation through the registry

`run id=file.open folder=...` works for every command. Automation keeps only what reads the
window or the document, or simulates input: `state`, `commands`, `ui`, `hit`, `screenshot`,
`height`, `mask`, `perf`, `logs`, `stroke` (a mouse stroke is input, not an operation), `view`,
`camera`, `gpu-debug`. The old names (`open`, `save`, `undo`, ...) map to command ids in one small
table, so scripts and the CLI keep working; drop the table when nothing uses it.

`help` is generated from the registry and the short list of reads. `AGENTS.md` lists the reads and
says that `commands` lists the operations, so it cannot fall behind.

Delete `UndoHeader` and `RedoHeader`, or let the menu show "Undo Raise terrain" (a title the
window reads again when the commands refresh). Not both.

## The mediator: MediatR

The editor uses MediatR (`IRequest`, `IRequestHandler`, `IPipelineBehavior`). Since version 13
(2025) it comes under a commercial licence with a licence key; the owner has accepted that. Its
version goes in `Directory.Packages.props`, as every package's does.

MediatR resolves handlers from `Microsoft.Extensions.DependencyInjection`. The
`ServiceCollection` is built in one place, `CommandServices.Create`: MediatR with the handlers of
the assembly, the behaviour, and the instances they need (`MainViewModel`, `IEditorDialogs`,
`CurrentEdit`). **As built**, `MainViewModel`'s constructor calls it, so every test that makes a
view model gets the same commands, and a test can make a registry of its own commands. Nothing
else asks the container for anything; the view models stay made by hand.

## What it costs

- More files: the built commands are about 20 files (a file may hold a few small ones, such as
  the dock's tabs); the planned ones are a line each in their menu's `Planned.cs`.
- A container, kept to the start of the app.
- From a call site, "go to definition" lands on the request, not the handler: hence one file for
  both.

## Avoid

- Sending requests from anywhere but the registry.
- The mediator's notifications for the model's events (`DocumentChanged`, `CameraMoved`): C#
  events say who listens.
- A query for every read. Automation's reads stay in automation; a query is worth adding when Lua
  needs the same read (file 06).
- Arrays and objects as arguments, until a command needs one. `stroke` stays in automation.

## Done when

- Adding an operation is one file (request and handler) plus its line in `Views/Menus.cs` and its
  test; `help`, `commands`, the palette and the shortcuts show it without other changes.
- `MainViewModel` has no `RelayCommand` for an operation of the menu. (The version control
  page's buttons keep theirs: they show what fails on the page, and call the same view model
  methods as `file.commit` and `file.enableVersionControl`.)
- `AutomationServer` has no case that calls an operation of the view model directly.
- A command with `[UndoStep]` that fails leaves the history as it was (a test).
