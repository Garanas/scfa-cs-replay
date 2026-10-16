using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The path networks of the Procedural mode (R-3): generated from the navigation mesh as a
    /// preview, edited by hand on top, written as markers by the export only.
    /// </summary>
    [TestClass]
    public class PathNodesTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "path-nodes");

        private static async Task<MainViewModel> Open()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        private static PathLayerNetwork Land(MainViewModel model) => model.PathNodes.Preview!.Network[PathLayer.Land]!;

        [TestMethod]
        public async Task GeneratesAPreviewAndNeverTouchesTheMarkers()
        {
            MainViewModel model = await Open();
            MapDocument before = model.Map!.Document;

            CommandRun run = await Run(model, "markers.connectPaths", new JsonObject { ["layers"] = new JsonArray("Land", "Air"), ["spacing"] = 24 });

            Assert.IsTrue(run.Ran);
            Assert.AreEqual(2, ((JsonObject)run.Result!)["layers"]!.AsArray().Count);
            Assert.AreSame(before.Markers, model.Map.Document.Markers, "the markers stay as they are");
            Assert.IsTrue(Land(model).Nodes.Length > 10);
            Assert.AreEqual(256, model.PathNodes.Preview!.Network[PathLayer.Air]!.Nodes.Length, "16 by 16 air nodes");
            Assert.IsTrue(model.ShowGeneratedPathNodes, "the preview shows");
            Assert.IsNotNull(model.PreviewMarkers);
            Assert.AreEqual("Generate the path networks", model.Map.Session.UndoDescription, "the settings are kept with the map");
            PathNodeSettings settings = model.Map.Document.Procedural.PathNodes;
            Assert.AreEqual(24f, settings.Spacing);
            Assert.IsTrue(settings.WritesOnExport, "the first Generate turns writing on export on");
            Assert.AreEqual(GeneratedStatus.Current, model.Generation.Rows.Single(row => row.Id == PathNetworkGenerator.GeneratorId).State);
            ProceduralRow row = model.Procedural.Rows.Single(row => row.Id == PathNetworkGenerator.GeneratorId);
            Assert.AreEqual("generatedPathNodes", row.Overlay);
            Assert.AreEqual(ExportUse.Written, row.Export);
            Assert.IsTrue(row.OnExport);

            // out of date when walkability's inputs or the settings change; generate.stale makes it again
            model.Map.Session.Edit("Raise the water", document => document with { Water = document.Water with { Elevation = document.Water.Elevation + 3 } });
            model.Generation.Refresh(model.Map.Document);
            Assert.IsTrue(model.PathNodes.IsStale);
            StringAssert.Contains(model.PathNodes.Status, "water level");
            await Run(model, "generate.stale");
            Assert.IsFalse(model.PathNodes.IsStale);
            Assert.AreSame(before.Markers, model.Map.Document.Markers, "still no markers");

            await Run(model, "generate.setPathNodes", new JsonObject { ["smoothing"] = 5, ["fill"] = false });
            Assert.AreEqual(5, model.Map.Document.Procedural.PathNodes.Smoothing);
            Assert.IsTrue(model.PathNodes.IsStale, "the settings changed");
            Assert.AreEqual("Change the path network settings", model.Map.Session.UndoDescription);

            // the row's check box: written on export or not, a step of undo
            await Run(model, "generate.onExport", new JsonObject { ["id"] = PathNetworkGenerator.GeneratorId });
            Assert.IsFalse(model.Map.Document.Procedural.PathNodes.WritesOnExport);
            model.Undo();
            Assert.IsTrue(model.Map.Document.Procedural.PathNodes.WritesOnExport);
        }

        [TestMethod]
        public async Task RefusesNoLayerAndASpacingOutOfRange()
        {
            MainViewModel model = await Open();
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "markers.connectPaths", new JsonObject { ["layers"] = new JsonArray() }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "markers.connectPaths", new JsonObject { ["spacing"] = 2 }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "markers.connectPaths", new JsonObject { ["layers"] = new JsonArray("Hover") }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "generate.setPathNodes", new JsonObject { ["smoothing"] = 11 }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "generate.addPathNode", new JsonObject { ["x"] = 10, ["z"] = 10 }), "nothing generated yet");
        }

        [TestMethod]
        public async Task EditsAreStepsOfUndoAndSurviveRegenerating()
        {
            MainViewModel model = await Open();
            await Run(model, "markers.connectPaths", new JsonObject { ["layers"] = new JsonArray("Land"), ["spacing"] = 16 });
            PathLayerNetwork generated = Land(model);
            GeneratedPathNode moved = generated.Nodes[0];
            GeneratedPathNode removed = generated.Nodes[^1];
            (int a, int b) = generated.Links[generated.Links.Length / 2];
            Vector2 empty = (generated.Nodes[a].Place + generated.Nodes[b].Place) / 2 + new Vector2(0.25f, 0.25f);

            await Run(model, "generate.movePathNode", new JsonObject { ["x"] = moved.X, ["z"] = moved.Z, ["toX"] = moved.X + 2, ["toZ"] = moved.Z, ["within"] = 0.01 });
            Assert.AreEqual("Move a path node", model.Map!.Session.UndoDescription);
            await Run(model, "generate.removePathNode", new JsonObject { ["x"] = removed.X, ["z"] = removed.Z, ["within"] = 0.01 });
            await Run(model, "generate.linkPathNodes", new JsonObject { ["x"] = generated.Nodes[a].X, ["z"] = generated.Nodes[a].Z, ["toX"] = generated.Nodes[b].X, ["toZ"] = generated.Nodes[b].Z, ["cut"] = true, ["within"] = 0.01 });
            await Run(model, "generate.addPathNode", new JsonObject { ["x"] = empty.X, ["z"] = empty.Y, ["layer"] = "Land" });
            Assert.AreEqual("Add a land path node", model.Map.Session.UndoDescription);

            PathLayerNetwork edited = Land(model);
            Assert.AreEqual(generated.Nodes.Length, edited.Nodes.Length, "one removed, one added");
            Assert.IsTrue(edited.Nodes.Any(node => node.Ref == moved.Ref && node.X == moved.X + 2));
            Assert.IsFalse(edited.Nodes.Any(node => node.Ref == removed.Ref));
            Assert.AreEqual(4, model.Map.Document.Procedural.PathNodes.Edits.Length);
            Assert.AreEqual(PathNodeRole.Added, model.PathNodes.Chosen?.Node.Role, "the added node is chosen");
            Assert.AreEqual("4 edits by hand", model.PathNodes.EditsText);

            // the terrain changes far away: generated again, every edit finds its node
            model.Map.Session.Edit("Raise the water", document => document with { Water = document.Water with { Elevation = document.Water.Elevation + 0.5f } });
            await Run(model, "generate.stale");
            Assert.AreEqual(0, model.PathNodes.Preview!.Unmatched.Count);
            Assert.IsTrue(Land(model).Nodes.Any(node => node.Ref == moved.Ref && node.X == moved.X + 2), "the move is applied again");

            // each edit a step of undo
            for (int i = 0; i < 5 && model.Map.Document.Procedural.PathNodes.Edits.Length == 4; i++)
            {
                model.Undo();
            }
            Assert.AreEqual(3, model.Map.Document.Procedural.PathNodes.Edits.Length, "the last edit undone alone");
            await Run(model, "generate.clearPathEdits");
            Assert.AreEqual(0, model.Map.Document.Procedural.PathNodes.Edits.Length);
            await Run(model, "generate.stale");
            Assert.AreEqual(generated.Nodes.Length, Land(model).Nodes.Length);
        }

        [TestMethod]
        public async Task EditsByHandMirrorWithTheSymmetry()
        {
            MainViewModel model = await Open();
            await Run(model, "select.symmetry", new JsonObject { ["kind"] = "rotational", ["count"] = 2 });
            await Run(model, "select.mirrorEdits", new JsonObject { ["on"] = true });
            await Run(model, "markers.connectPaths", new JsonObject { ["layers"] = new JsonArray("Land"), ["spacing"] = 16 });
            MapDocument document = model.Map!.Document;
            Vector2 Image(Vector2 place) => new Vector2(document.Width - place.X, document.Height - place.Y);

            // a node added comes with its image, in one step
            Vector2 at = new Vector2(100.25f, 60.25f);
            await Run(model, "generate.addPathNode", new JsonObject { ["x"] = at.X, ["z"] = at.Y });
            Assert.AreEqual("Add 2 land path nodes, mirrored", model.Map.Session.UndoDescription);
            StringAssert.EndsWith(model.Status, "and 1 mirrored");
            PathLayerNetwork network = Land(model);
            Assert.IsTrue(network.Nodes.Any(node => node.Role == PathNodeRole.Added && Vector2.Distance(node.Place, at) < 0.01f));
            Assert.IsTrue(network.Nodes.Any(node => node.Role == PathNodeRole.Added && Vector2.Distance(node.Place, Image(at)) < 0.01f), "its image in the other half");

            // removing a node takes its partner along, when the network has one near the image
            GeneratedPathNode partnered = network.Nodes.First(node => node.Role != PathNodeRole.Added
                && model.PathNodes.NodeAt(Image(node.Place).X, Image(node.Place).Y, MainViewModel.PathNodePartnerReach, PathLayer.Land) is PreviewPathNode partner
                && Vector2.Distance(partner.Node.Place, node.Place) > 8);
            int before = network.Nodes.Length;
            await Run(model, "generate.removePathNode", new JsonObject { ["x"] = partnered.X, ["z"] = partnered.Z, ["within"] = 0.01 });
            Assert.AreEqual(before - 2, Land(model).Nodes.Length, "the node and its partner");
            Assert.AreEqual("Remove 2 path nodes, mirrored", model.Map.Session.UndoDescription);

            // with mirroring off, an edit stays where it is made
            await Run(model, "select.mirrorEdits", new JsonObject { ["on"] = false });
            await Run(model, "generate.addPathNode", new JsonObject { ["x"] = 80.25f, ["z"] = 40.25f });
            Assert.AreEqual("Add a land path node", model.Map.Session.UndoDescription);
        }

        [TestMethod]
        public async Task TheExportWritesTheNetworkAndTheProjectKeepsNone()
        {
            MainViewModel model = await Open();
            // a node placed by hand in the Markers mode: it stays a marker and is joined to the network
            await Run(model, "markers.place", new JsonObject { ["kind"] = "path", ["x"] = 100.5, ["z"] = 100.5, ["layer"] = "Land" });
            await Run(model, "markers.connectPaths", new JsonObject { ["layers"] = new JsonArray("Land"), ["spacing"] = 24 });
            await Run(model, "generate.addPathNode", new JsonObject { ["x"] = 60.5, ["z"] = 60.5 });
            int nodes = Land(model).Nodes.Length;
            MapDocument before = model.Map!.Document;

            string folder = await model.ExportAsync(Path.Combine(Root, Guid.NewGuid().ToString("N")));

            ExportStepState step = model.Export.Lines.Single(line => line.Id == MainViewModel.PathNodesExportStep);
            Assert.AreEqual(ExportStepStatus.Done, step.Status);
            StringAssert.Contains(step.StatusText, $"{nodes} nodes");
            Assert.AreSame(before.Markers, model.Map.Document.Markers, "the project keeps no generated node");
            Assert.AreEqual(1, model.Map.Document.Markers.PathNodes.Length);
            MapDocument exported = MapImporter.ImportFolder(folder);
            PathNode[] land = [.. exported.Markers.PathNodes.Where(node => node.Layer == PathLayer.Land)];
            Assert.AreEqual(nodes + 1, land.Length, "the network and the node placed by hand");
            Assert.IsTrue(land.Single(node => node.Id == before.Markers.PathNodes[0].Id).AdjacentTo.Length >= 1, "the hand-placed node is joined to the network");
            Assert.IsTrue(land.Any(node => Math.Abs(node.Position.X - 60.5f) < 0.01f && Math.Abs(node.Position.Z - 60.5f) < 0.01f), "the edit by hand is written");

            // with replace, the map's own node of the layer is left out
            await Run(model, "generate.setPathNodes", new JsonObject { ["replace"] = true });
            MapDocument replaced = MapImporter.ImportFolder(await model.ExportAsync(Path.Combine(Root, Guid.NewGuid().ToString("N"))));
            Assert.AreEqual(nodes, replaced.Markers.PathNodes.Count(node => node.Layer == PathLayer.Land));

            // off, nothing generated is written
            await Run(model, "generate.setPathNodes", new JsonObject { ["onExport"] = false });
            MapDocument off = MapImporter.ImportFolder(await model.ExportAsync(Path.Combine(Root, Guid.NewGuid().ToString("N"))));
            Assert.AreEqual(1, off.Markers.PathNodes.Length);
            Assert.AreEqual(ExportStepStatus.Skipped, model.Export.Lines.Single(line => line.Id == MainViewModel.PathNodesExportStep).Status);
        }

        [TestMethod]
        public async Task TheExportGeneratesWhatIsOutOfDate()
        {
            MainViewModel model = await Open();
            await Run(model, "generate.setPathNodes", new JsonObject { ["layers"] = new JsonArray("Air"), ["onExport"] = true });

            string folder = await model.ExportAsync(Path.Combine(Root, Guid.NewGuid().ToString("N")));

            Assert.AreEqual(256, MapImporter.ImportFolder(folder).Markers.PathNodes.Count(node => node.Layer == PathLayer.Air), "generated during the export");
            Assert.IsNotNull(model.PathNodes.Preview);
        }

        [TestMethod]
        public async Task ThePathNodesToolAddsMovesLinksAndRemoves()
        {
            MainViewModel model = await Open();
            await Run(model, "markers.connectPaths", new JsonObject { ["layers"] = new JsonArray("Land"), ["spacing"] = 16 });
            model.SelectTool("procedural.pathNodes");
            PathNodeEditTool tool = (PathNodeEditTool)model.ActiveTool!.Tool!;
            PathLayerNetwork generated = Land(model);
            ToolInput input = new ToolInput(ToolModifiers.None, 0.1f);

            // a click on a node chooses it, a drag onto empty ground moves it
            GeneratedPathNode node = generated.Nodes[2];
            tool.Press(node.X, node.Z, input);
            Assert.AreEqual(node.Ref, model.PathNodes.Chosen?.Node.Ref);
            tool.Drag(node.X + 1.5f, node.Z, input);
            tool.Release();
            await Task.Yield();
            Assert.IsTrue(Land(model).Nodes.Any(n => n.Ref == node.Ref && Math.Abs(n.X - (node.X + 1.5f)) < 1e-3f), "moved");

            // a drag from a node onto another with control cuts their link
            (int a, int b) = generated.Links.First(link => link.A != 2 && link.B != 2);
            tool.Press(generated.Nodes[a].X, generated.Nodes[a].Z, input with { Modifiers = ToolModifiers.Control });
            tool.Drag(generated.Nodes[b].X, generated.Nodes[b].Z, input with { Modifiers = ToolModifiers.Control });
            tool.Release();
            await Task.Yield();
            Assert.AreEqual(PathEditKind.Cut, model.Map!.Document.Procedural.PathNodes.Edits[^1].Kind);

            // a click on empty ground adds; Delete removes the chosen node
            int count = Land(model).Nodes.Length;
            Vector2 empty = (generated.Nodes[a].Place + generated.Nodes[b].Place) / 2 + new Vector2(0.3f, 0.3f);
            tool.Press(empty.X, empty.Y, input);
            tool.Release();
            await Task.Yield();
            Assert.AreEqual(count + 1, Land(model).Nodes.Length);
            Assert.IsTrue(tool.Key("Delete", ToolModifiers.None));
            await Task.Yield();
            Assert.AreEqual(count, Land(model).Nodes.Length);
            Assert.AreEqual(0, model.Map.Document.Markers.PathNodes.Length, "the markers never change");
        }
    }
}
