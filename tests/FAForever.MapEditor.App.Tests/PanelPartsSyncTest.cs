using System.ComponentModel;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Lists whose entries come and go with the map (<see cref="PanelParts.SyncGroup"/>): the armies,
    /// areas and chains a mode keeps in step on every change of the map, under a fixed first entry.
    /// </summary>
    [TestClass]
    public sealed class PanelPartsSyncTest
    {
        private static PanelParts List() => new PanelParts(
        [
            new PanelPart("map.information", "Information", "Map", new PlannedPart("M-1", "about")),
            new PanelPart("map.script", "Script and files", "Map", new PlannedPart("M-6", "files")),
            new PanelPart("map.areas.note", "How areas work", "Areas", new PlannedPart("M-5", "areas")),
        ]);

        private static PanelPartEntry Army(string name, string? detail = null) =>
            new PanelPartEntry($"map.army.{name}", name, () => new PlannedPart("M-4", name), detail);

        private static string[] Ids(PanelParts list) => [.. list.Items.Select(part => part.Id)];

        [TestMethod]
        public void EntriesAreAddedUnderTheirHeadingWhereTheListSays()
        {
            PanelParts list = List();

            Assert.IsTrue(list.SyncGroup("Armies", [Army("ARMY_1", "team 1"), Army("ARMY_2")], afterGroup: "Map"));

            CollectionAssert.AreEqual(new[] { "map.information", "map.script", "map.army.ARMY_1", "map.army.ARMY_2", "map.areas.note" }, Ids(list), "after the Map heading");
            CollectionAssert.AreEqual(new[] { "Map", "Armies", "Areas" }, list.Groups.Select(group => group.Title).ToArray());
            Assert.AreEqual("team 1", list.Find("map.army.ARMY_1")!.Detail);
            Assert.AreEqual("", list.Find("map.army.ARMY_2")!.Detail);
            Assert.AreEqual("map.information", list.Chosen.Id, "the chosen entry stays");

            // without a heading to follow, a new heading goes at the end
            list.SyncGroup("Chains", [new PanelPartEntry("markers.chain.A", "A", () => new object())]);
            Assert.AreEqual("markers.chain.A", list.Items[^1].Id);
        }

        [TestMethod]
        public void NothingHappensWhenNothingChanged()
        {
            PanelParts list = List();
            list.SyncGroup("Armies", [Army("ARMY_1"), Army("ARMY_2")], afterGroup: "Map");
            PanelPart first = list.Find("map.army.ARMY_1")!;
            List<string?> raised = [];
            list.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            int made = 0;
            PanelPartEntry Counted(string name) => new PanelPartEntry($"map.army.{name}", name, () => { made++; return new object(); });

            Assert.IsFalse(list.SyncGroup("Armies", [Counted("ARMY_1"), Counted("ARMY_2")], afterGroup: "Map"));

            Assert.AreEqual(0, raised.Count, "nothing raised");
            Assert.AreEqual(0, made, "no content made");
            Assert.AreSame(first, list.Find("map.army.ARMY_1"), "the same entry");
            Assert.IsFalse(list.SyncGroup("Nothing", []), "a heading that has nothing and gets nothing");
        }

        [TestMethod]
        public void AnEntryThatStaysIsRetitledAndKeepsItsContent()
        {
            PanelParts list = List();
            list.SyncGroup("Armies", [Army("ARMY_1", "team 1")], afterGroup: "Map");
            PanelPart army = list.Find("map.army.ARMY_1")!;
            object content = army.Content;
            List<string?> raised = [];
            army.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            Assert.IsTrue(list.SyncGroup("Armies", [new PanelPartEntry("map.army.ARMY_1", "North", () => new object(), "team 2")]));

            Assert.AreSame(army, list.Find("map.army.ARMY_1"));
            Assert.AreEqual("North", army.Title);
            Assert.AreEqual("team 2", army.Detail);
            Assert.AreEqual("North: team 2", army.Label);
            Assert.AreSame(content, army.Content, "the content made once stays");
            CollectionAssert.Contains(raised, nameof(PanelPart.Title));
            CollectionAssert.Contains(raised, nameof(PanelPart.Label));

            // a detail left out leaves the entry's
            list.SyncGroup("Armies", [new PanelPartEntry("map.army.ARMY_1", "North", () => new object())]);
            Assert.AreEqual("team 2", army.Detail);
        }

        [TestMethod]
        public void TheChosenEntryStaysWhenItStaysElseTheHeadingsFirstElseTheListsFirst()
        {
            PanelParts list = List();
            list.SyncGroup("Armies", [Army("ARMY_1"), Army("ARMY_2"), Army("ARMY_3")], afterGroup: "Map");
            list.Choose("map.army.ARMY_2");

            list.SyncGroup("Armies", [Army("ARMY_2"), Army("ARMY_3"), Army("ARMY_4")]);
            Assert.AreEqual("map.army.ARMY_2", list.Chosen.Id, "it stays, and stays chosen");
            Assert.IsTrue(list.Chosen.IsChosen);
            CollectionAssert.AreEqual(new[] { "map.information", "map.script", "map.army.ARMY_2", "map.army.ARMY_3", "map.army.ARMY_4", "map.areas.note" }, Ids(list),
                "in the order given, where the heading was");

            list.SyncGroup("Armies", [Army("ARMY_3"), Army("ARMY_4")]);
            Assert.AreEqual("map.army.ARMY_3", list.Chosen.Id, "the heading's first");
            Assert.IsTrue(list.Chosen.IsChosen);

            list.SyncGroup("Armies", []);
            Assert.AreEqual("map.information", list.Chosen.Id, "the list's fixed first entry");
            CollectionAssert.DoesNotContain(list.Groups.Select(group => group.Title).ToArray(), "Armies", "a heading without entries is gone");

            // an entry chosen elsewhere is left alone
            list.Choose("map.areas.note");
            list.SyncGroup("Armies", [Army("ARMY_9")], afterGroup: "Map");
            Assert.AreEqual("map.areas.note", list.Chosen.Id);
            Assert.AreEqual(2, list.Items.ToList().FindIndex(part => part.Id == "map.army.ARMY_9"), "back after the Map heading");
        }

        [TestMethod]
        public void TheListRaisesItsEntriesWhenTheyChange()
        {
            PanelParts list = List();
            List<string?> raised = [];
            ((INotifyPropertyChanged)list).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            list.SyncGroup("Armies", [Army("ARMY_1")]);

            CollectionAssert.Contains(raised, nameof(PanelParts.Items));
            CollectionAssert.Contains(raised, nameof(PanelParts.Groups));
        }

        [TestMethod]
        public void WrongEntriesAreRefused()
        {
            PanelParts list = List();
            Assert.ThrowsException<ArgumentException>(() => list.SyncGroup("Armies", [Army("ARMY_1"), Army("ARMY_1")]), "one id twice");
            Assert.ThrowsException<ArgumentException>(() => list.SyncGroup("Armies", [new PanelPartEntry("map.script", "Script", () => new object())]), "another heading's id");
            CollectionAssert.AreEqual(new[] { "map.information", "map.script", "map.areas.note" }, Ids(list), "nothing changed");

            PanelParts only = new PanelParts([new PanelPart("a", "A", "Things", new object())]);
            Assert.ThrowsException<ArgumentException>(() => only.SyncGroup("Things", []), "a list of parts holds at least one");
        }
    }
}
