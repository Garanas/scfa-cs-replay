# 06. Lua plugins

Priority later, effort large. User story F-15: "Plugins with commands, tools and panels, through
the controller and the events; a run is one step of undo."

## What is ready

- `CommandRegistry.Register` adds commands made at run time.
- The document is immutable: a plugin can read a snapshot without locks, and an edit replaces it.
- `EditSession.Begin` gives a transaction: everything a run does becomes one undo step, and
  disposing it without commit undoes it all.
- `RunAsync` already counts a failed command and lets the caller handle the exception.
- Modes and tools are data that a plugin could add to.

## What has to come first

- **Commands as requests, with the pipeline** (file 01): a plugin command without arguments is of
  little use, and the Lua side calls built-in commands with arguments too: a Lua table becomes the
  request record the way automation's JSON does. The pipeline gives a plugin's run the same
  measuring, logging and undo step as a built-in command.
- **Tools as objects** (file 02): a Lua tool is one more `IMapTool`, made from the plugin's
  functions. Without file 02 a plugin tool would need branches in the views.

After these two, a plugin hooks in at the same points as built-in code. That is the test of the
design: what a built-in tool or command cannot do through those points, a plugin cannot either.

## What is missing

- **A Lua runtime.** `FAForever.FileFormats.Lua` evaluates the data-only Lua of game files; it
  does not run code. The candidates are MoonSharp (a Lua interpreter in C#, no native files) and
  NLua or KeraLua (the real Lua, with a native library per platform). Check how each is
  maintained and how each limits a script (instructions or time) before choosing; this is the
  owner's decision (`docs/TODO.md`).
- **Plugin commands without handlers of their own.** The container is built once at start, and a
  plugin is turned on later. Every plugin command is an entry of the registry that sends one
  request, `RunPluginCommand(Plugin, Id, Arguments)`, marked `[UndoStep]`; its one handler calls the
  plugin's Lua function. So a run is one undo step, and a failing run leaves nothing, through the
  same behaviour as built-in commands.
- **Removing what a plugin registered**: the registry has `Unregister` for commands; ids in the
  plugin's namespace (`plugin.<name>.<id>`), and the same for its tools and panels, so turning a
  plugin off takes all of it away.
- **Queries for what plugins read.** A plugin reads the map (a height, a mask, the markers). These
  reads are worth making queries (requests that answer) when the first plugin needs one, so Lua and
  automation read the same way.
- **Edits in bulk.** A call from Lua into C# per height sample is too slow on an 81 km map
  (a heightmap of 1025 by 1025 samples and more). The Lua side gets operations on regions and
  brushes (a dab, a rectangle, a mask from a rule) rather than a sample at a time.
- **Panels without XAML.** A plugin cannot ship Avalonia views. It describes its settings: a
  number with a range, a choice, a check box, a text, a button that runs a command; one generic
  view shows them. This is the one place where a generic options editor is worth building. Built-in
  tools move to it only if it turns out plainer than their `DataTemplate`s.
- **Errors stay in the plugin.** An error becomes a line in the Console and `ran: false`; the
  editor goes on. A script runs on the window's thread, so a run needs a limit, or an endless loop
  freezes the editor.
- **Where plugins live**: the workspace (as `plugins.manage` says), turned on or off per plugin.

## Avoid

- Designing an "editor host" interface for plugins up front. Write two or three real plugins
  first (a command that raises the terrain in a rectangle, a tool that places mass in a pattern,
  a check that reports spawns too close together), and give the Lua side what they need.
- Plugins on other threads before a plugin needs it.

## A first step

A spike: one Lua plugin in a test workspace with one command, `plugin.example.raise {x, z, size,
height}`, that raises a square as one undo step, run through automation's `run`. It shows the
runtime, the registration, the transaction and the errors, with the least to build.
