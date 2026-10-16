using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// A layer stack shows the chosen layer's fields filled as soon as it shows (issue 2 of the wave
    /// 10 walkthrough: a stack opened after a mask was added had its row chosen and its fields empty
    /// and greyed until the row was chosen again).
    /// </summary>
    [TestClass]
    public sealed class LayerStackFieldsTest
    {
        [TestMethod]
        public void TheChosenLayersFieldsAreFilledAfterTheClipChoicesChanged()
        {
            MapDocument document = MaskLayers.Add(Project.NewMap.Create(new Project.NewMapSettings { Size = 256 }), "forest");
            LayerStack stack = LayerStackEdits.Add(LayerStack.Empty(LayerTarget.Mask("forest")),
                new Layer { Name = "paint", Source = LayerSource.Painted, Coverage = TiledBuffer<byte>.Create(256, 256) });
            LayerStackViewModel model = new LayerStackViewModel((_, _) => Task.CompletedTask, _ => stack,
                new LayerStackCommands("masks.setLayer", "masks.moveLayer", "masks.removeLayer", "mask layer", "clip", "Clip"), []);
            model.Refresh(document);
            Assert.AreEqual("paint", ((TextSetting)model.Layer.Settings[0]).Text);

            // another mask: the clip offers it, so the fields are made again; the stack itself stayed
            model.Refresh(MaskLayers.Add(document, "rocks"));

            Assert.AreEqual("paint", model.Chosen);
            Assert.IsTrue(model.Layer.Settings.All(setting => setting.IsEnabled), "the fields can be used");
            Assert.AreEqual("paint", ((TextSetting)model.Layer.Settings[0]).Text, "the name shows");
        }

        [TestMethod]
        public void AMasksClipOffersEveryMaskButItself()
        {
            MapDocument document = MaskLayers.Add(MaskLayers.Add(Project.NewMap.Create(new Project.NewMapSettings { Size = 256 }), "forest"), "rocks");
            LayerTarget forest = LayerTarget.Mask("forest");
            CoverageStackViewModel model = new CoverageStackViewModel((_, _) => Task.CompletedTask, () => forest, "mask layer");
            model.Refresh(document);

            ChoiceSetting clip = (ChoiceSetting)model.Layer.Settings[3];
            CollectionAssert.Contains(clip.Choices.ToList(), "mask rocks");
            CollectionAssert.DoesNotContain(clip.Choices.ToList(), "mask forest", "a mask is not clipped by itself (issue 28)");
            Assert.AreEqual(LayerStackViewModel.Everywhere, clip.Choices[0]);

            // another mask shown: its own clip leaves out that one instead
            forest = LayerTarget.Mask("rocks");
            model.Refresh(document);
            clip = (ChoiceSetting)model.Layer.Settings[3];
            CollectionAssert.Contains(clip.Choices.ToList(), "mask forest");
            CollectionAssert.DoesNotContain(clip.Choices.ToList(), "mask rocks");
        }
    }
}
