using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// The draft and final heightmaps (user story T-9) for automation's <c>state.draftFinal</c>: which
/// one the views show, both stacks in their order (each layer's use and on/off there), the final's
/// base, whether the draft has heights of its own, and what is written for the procedural tools.
/// </summary>
public static class DraftFinalState
{
    public static JsonObject Describe(MainViewModel model)
    {
        MapDocument? document = model.ShownDocument;
        JsonArray StackOf(HeightStackKind kind) => new JsonArray([.. (document is null ? [] : HeightStacks.Of(document, kind).Layers).Select(layer => (JsonNode)new JsonObject
        {
            ["name"] = layer.Name,
            ["use"] = HeightStacks.UseOf(document!, layer.Name)?.ToString(),
            ["visible"] = layer.Visible,
            ["base"] = layer.IsBase,
        })]);
        string? folder = model.Map?.ProjectFolder;
        Layer? own = document is null ? null : HeightStacks.FinalBase(document);
        return new JsonObject
        {
            ["shown"] = model.ShownHeights == HeightStackKind.Draft ? "draft" : "final",
            ["sessionHeights"] = model.Map?.Session.Heights.ToString(),
            ["final"] = StackOf(HeightStackKind.Final),
            ["draft"] = StackOf(HeightStackKind.Draft),
            ["sharesBase"] = document is null || HeightStacks.SharesBase(document),
            ["finalBase"] = own is null ? null : new JsonObject { ["name"] = own.Name, ["file"] = own.Path },
            ["draftOwnHeights"] = document?.DraftSamples is not null,
            ["shownHeightAtCentre"] = model.ShownHeightmap is Heightmap shown ? Math.Round(shown.HeightAt(shown.Width / 2f, shown.Height / 2f), 3) : null,
            ["writeOnChange"] = document?.Procedural.WriteDraftOnChange,
            ["draftMasks"] = new JsonArray([.. (document?.Procedural.DraftMasks ?? []).Select(name => (JsonNode)JsonValue.Create(name))]),
            ["writes"] = model.DraftWrites,
            ["outputs"] = new JsonArray([.. (folder is null ? [] : ProceduralFiles.Outputs(folder)).Select(path => (JsonNode)JsonValue.Create(path))]),
        };
    }
}
