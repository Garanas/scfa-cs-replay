using System.Numerics;
using System.Text;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using Lua;
using Lua.Standard;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The script the export writes for an adaptive map (user story R-5), run in Lua with the
    /// game's functions it calls stood in for: which groups it creates by the lobby's options and the
    /// number of players, and which deposits by who is in the game.
    /// </summary>
    [TestClass]
    public class AdaptiveScriptRunTest
    {
        /// <summary>
        /// The game as the script sees it: <c>import</c>, <c>ScenarioUtils</c>, <c>ScenarioInfo</c>,
        /// <c>ListArmies</c> and the functions that spawn deposits, each noting what it was asked.
        /// </summary>
        private const string Game = """
            created = {}
            spawned = {}
            modules = {}
            function import(path) return modules[path] end
            ScenarioUtils = {
                InitializeArmies = function() end,
                CreateArmyGroup = function(army, group, wreck)
                    table.insert(created, army .. ':' .. group .. (wreck and ':wreck' or ''))
                end,
                GetMarkers = function() return markers end,
            }
            modules['/lua/sim/ScenarioUtilities.lua'] = ScenarioUtils
            modules['/lua/ScenarioFramework.lua'] = { SetPlayableArea = function() end }
            function ListArmies() return armies end
            function CreateResourceDeposit(kind, x, y, z, size) table.insert(spawned, kind .. '@' .. x) end
            function CreatePropHPR() end
            function CreateSplat() end
            function Random(a, b) return a end
            markers = {
                ['Mass 01'] = { resource = true, type = 'Mass', position = { 40.5, 30, 40.5 } },
                ['Mass 02'] = { resource = true, type = 'Mass', position = { 200.5, 30, 40.5 } },
                ['Mass 03'] = { resource = true, type = 'Mass', position = { 128.5, 30, 128.5 } },
                ['Hydrocarbon 01'] = { resource = true, type = 'Hydrocarbon', position = { 60.5, 30, 60.5 } },
                ['ARMY_1'] = { type = 'Blank Marker', position = { 1, 1, 1 } },
            }
            """;

        private static async Task<LuaState> Start(string[] armies, string options)
        {
            IReadOnlyDictionary<string, byte[]> files = MapExporter.ToBytes(Adaptive());
            LuaState state = LuaState.Create();
            state.OpenBasicLibrary();
            state.OpenStringLibrary();
            state.OpenTableLibrary();
            state.OpenMathLibrary();
            await state.DoStringAsync(Game);
            await state.DoStringAsync($"armies = {{ {string.Join(", ", armies.Select(army => $"'{army}'"))} }}\nScenarioInfo = {{ Options = {{ {options} }} }}");
            // the tables as a module of their own
            await state.DoStringAsync("function readTables(text) local env = {} local chunk = load(text, 'tables', 't', env) chunk() return env end");
            LuaValue[] read = await state.DoStringAsync($"return readTables({Literal(Encoding.UTF8.GetString(files["adapt_tables.lua"]))})");
            state.Environment["tablesModule"] = read[0];
            await state.DoStringAsync("modules['/maps/adapt.v0001/adapt_tables.lua'] = tablesModule");
            await state.DoStringAsync(Encoding.UTF8.GetString(files["adapt_script.lua"]), "adapt_script.lua");
            return state;
        }

        /// <summary>
        /// A new map of four players with a deposit for each of the first two armies, a shared one,
        /// and civilians by option and by the number of players (as the library's test).
        /// </summary>
        private static MapDocument Adaptive()
        {
            MapDocument document = NewMap.Create(new NewMapSettings { Name = "Adapt", Size = 256, Players = 4 });
            Vector3 At(float x, float z) => new Vector3(x, document.Terrain.Heightmap.HeightAt(x, z), z);
            document = document
                .AddMarker(new ResourceMarker { Id = 0, Name = "Mass 01", Kind = ResourceKind.Mass, Position = At(40.5f, 40.5f), SpawnWithArmy = "ARMY_1" })
                .AddMarker(new ResourceMarker { Id = 0, Name = "Mass 02", Kind = ResourceKind.Mass, Position = At(200.5f, 40.5f), SpawnWithArmy = "ARMY_2" })
                .AddMarker(new ResourceMarker { Id = 0, Name = "Mass 03", Kind = ResourceKind.Mass, Position = At(128.5f, 128.5f) })
                .AddMarker(new ResourceMarker { Id = 0, Name = "Hydrocarbon 01", Kind = ResourceKind.Hydrocarbon, Position = At(60.5f, 60.5f), SpawnWithArmy = "ARMY_1" });
            document = document.AddOptionalGroup("ARMY_17", "Civilian_Base", 2, out string base2)
                .AddOptionalGroup("ARMY_17", "Civilian_Base", 3, out string base3)
                .AddOptionalGroup("ARMY_17", "Wreckage", 2, out string wrecks)
                .AddOptionalGroup("ARMY_17", AdaptiveScript.PlayersOption, 4, out string crowd);
            Unit Unit(string name, float x) => new Unit { Name = name, Blueprint = "uec1101", Position = At(x, 100) };
            return document.AddUnit("ARMY_17", [base2], Unit("UNIT_1", 100))
                .AddUnit("ARMY_17", [base3], Unit("UNIT_2", 110))
                .AddUnit("ARMY_17", [wrecks], Unit("UNIT_3", 120))
                .AddUnit("ARMY_17", [crowd], Unit("UNIT_4", 130));
        }

        private static string Literal(string text) => "'" + text.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "").Replace("\n", "\\n") + "'";

        private static async Task<string> Joined(LuaState state, string list) =>
            (await state.DoStringAsync($"table.sort({list}) return table.concat({list}, ',')"))[0].Read<string>();

        [TestMethod]
        public async Task TheGroupsOfTheChosenOptionsAndOfTheNumberOfPlayersAreCreated()
        {
            LuaState two = await Start(["ARMY_1", "ARMY_2", "ARMY_17"], "optional_civilian_base = 3");
            await two.DoStringAsync("OnPopulate()");
            Assert.AreEqual("ARMY_17:Optional_Civilian_Base_2,ARMY_17:Optional_Civilian_Base_3", await Joined(two, "created"),
                "the groups up to the value chosen; no wreckage by default; two players are too few for the crowd");

            LuaState four = await Start(["ARMY_1", "ARMY_2", "ARMY_3", "ARMY_4", "ARMY_17"], "optional_wreckage = 2");
            await four.DoStringAsync("OnPopulate()");
            Assert.AreEqual("ARMY_17:Optional_Players_4,ARMY_17:Optional_Wreckage_2:wreck", await Joined(four, "created"),
                "wreckage as wrecks, and the group of four players");
        }

        [TestMethod]
        public async Task TheDepositsOfArmiesNotInTheGameDoNotSpawn()
        {
            LuaState used = await Start(["ARMY_2", "ARMY_17"], "dynamic_spawn = 2");
            await used.DoStringAsync("ScenarioUtils.CreateResources()");
            Assert.AreEqual("Mass@128.5,Mass@200.5", await Joined(used, "spawned"), "used slots: ARMY_2's and the shared one");

            LuaState mirror = await Start(["ARMY_2", "ARMY_17"], "");
            await mirror.DoStringAsync("ScenarioUtils.CreateResources()");
            Assert.AreEqual("Hydrocarbon@60.5,Mass@128.5,Mass@200.5,Mass@40.5", await Joined(mirror, "spawned"), "mirror slots, the default: ARMY_1 is ARMY_2's mirror");

            LuaState pairs = await Start(["ARMY_2", "ARMY_17"], "dynamic_spawn = 3");
            await pairs.DoStringAsync("ScenarioUtils.CreateResources()");
            Assert.AreEqual("Mass@128.5", await Joined(pairs, "spawned"), "no mirror, no resources");
        }
    }
}
