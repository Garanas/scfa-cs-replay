using FAForever.Replay;

namespace FAForever.Replay.Test
{
    [TestClass]
    public class ReplayHeaderTest
    {
        private static Replay Load(string file)
        {
            return file.EndsWith(".fafreplay")
                ? ReplayLoader.LoadFAFReplayFromDisk(file)
                : ReplayLoader.LoadSCFAReplayFromDisk(file);
        }

        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay", "Supreme Commander v1.50.3809", false, 107480310, 12, 14)]
        [DataRow("assets/faforever/23225104.fafreplay", "Supreme Commander v1.50.3812", false, 79993303, 8, 10)]
        [DataRow("assets/faforever/zstd/22338092.fafreplay", "Supreme Commander v1.50.3809", true, 64239620, 1, 3)]
        [DataRow("assets/scfa/balthazar-01.SCFAReplay", "Supreme Commander v1.60.   6", true, 101117041, 5, 10)]
        public void HeaderTest(string file, string expectedGameVersion, bool expectedCheats, int expectedSeed, int expectedClients, int expectedArmies)
        {
            ReplayHeader header = Load(file).Header;

            Assert.AreEqual(expectedGameVersion, header.GameVersion);
            Assert.AreEqual("Replay v1.9", header.ReplayVersion);
            Assert.AreEqual(expectedCheats, header.CheatsEnabled);
            Assert.AreEqual(expectedSeed, header.Seed);
            Assert.AreEqual(expectedClients, header.Clients.Length);
            Assert.AreEqual(expectedArmies, header.Armies.Length);
        }

        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay", "demoralization", 1500, "pvsi", "fixed", "locked", "No")]
        [DataRow("assets/faforever/23225104.fafreplay", "demoralization", 1500, "lvsr", "fixed", "locked", "No")]
        [DataRow("assets/faforever/zstd/22338092.fafreplay", "sandbox", 1000, "tvsb", "fixed", "locked", "No")]
        [DataRow("assets/scfa/balthazar-01.SCFAReplay", "domination", 1000, null, "fixed", "locked", null)]
        public void ScenarioOptionsTest(string file, string expectedVictory, int expectedUnitCap, string? expectedAutoTeams, string expectedTeamSpawn, string expectedTeamLock, string? expectedUnranked)
        {
            ReplayScenarioOptions options = Load(file).Header.Scenario.Options;

            Assert.AreEqual(expectedVictory, options.Victory);
            Assert.AreEqual(expectedUnitCap, options.UnitCap);
            Assert.AreEqual(expectedAutoTeams, options.AutoTeams);
            Assert.AreEqual(expectedTeamSpawn, options.TeamSpawn);
            Assert.AreEqual(expectedTeamLock, options.TeamLock);
            Assert.AreEqual(expectedUnranked, options.Unranked);
            Assert.IsNotNull(options.Raw);
        }

        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay")]
        [DataRow("assets/faforever/23225104.fafreplay")]
        public void ScenarioOptionsAreNotCheatsTest(string file)
        {
            // CheatsEnabled is stored as the string 'false'/'true', PrebuiltUnits as
            // 'Off'/'On' and Score as 'no'/'yes' (lua/ui/lobby/lobbyOptions.lua); the
            // flexible reader must normalize all of them.
            ReplayScenarioOptions options = Load(file).Header.Scenario.Options;

            Assert.IsFalse(options.CheatsEnabled!.Value);
            Assert.IsFalse(options.PrebuiltUnits!.Value);
            Assert.IsFalse(options.Score!.Value);
            Assert.IsTrue(options.RevealCivilians!.Value);
        }

        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay", 2, "Wifi_", 4, 2, 3, "ru", "Yow", 2520.87, 4645)]
        [DataRow("assets/faforever/23225104.fafreplay", 6, "Gabber", 1, 3, 7, "de", "", 1885.24, 12302)]
        [DataRow("assets/faforever/zstd/22338092.fafreplay", 0, "Jip", 1, 3, 1, "nl", "", 1971.99, 1948)]
        public void PlayerOptionsTest(string file, int sourceId, string expectedName, int expectedFaction, int expectedTeam, int expectedStartSpot, string expectedCountry, string expectedClan, double expectedMean, int expectedRatedGames)
        {
            ReplayHeader header = Load(file).Header;

            ReplayPlayerOptions army = header.Armies.Single(candidate => candidate.SourceId == sourceId);

            Assert.AreEqual(expectedName, army.PlayerName);
            // The source id is the index into the clients; both carry the player name.
            Assert.AreEqual(expectedName, header.Clients[sourceId].PlayerName);
            Assert.AreEqual(expectedFaction, army.Faction);
            Assert.AreEqual(expectedTeam, army.Team);
            Assert.AreEqual(expectedStartSpot, army.StartSpot);
            Assert.AreEqual(expectedCountry, army.Country);
            Assert.AreEqual(expectedClan, army.Clan);
            Assert.IsTrue(army.Human!.Value);
            Assert.IsFalse(army.Civilian!.Value);
            Assert.AreEqual(expectedMean, army.RatingMean!.Value, 0.01);
            Assert.AreEqual(expectedRatedGames, army.RatedGames);
        }

        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay", 2)]
        [DataRow("assets/faforever/23225104.fafreplay", 2)]
        public void CivilianArmiesTest(string file, int expectedCivilians)
        {
            ReplayHeader header = Load(file).Header;

            List<ReplayPlayerOptions> civilians = header.Armies.Where(army => army.Civilian == true).ToList();

            Assert.AreEqual(expectedCivilians, civilians.Count);
            foreach (ReplayPlayerOptions civilian in civilians)
            {
                // Civilian armies are not controlled by a client and are not human.
                Assert.IsNull(civilian.SourceId);
                Assert.IsFalse(civilian.Human!.Value);
            }
        }

        [TestMethod]
        [DataRow("assets/scfa/balthazar-01.SCFAReplay", "Zoe (AI: Easy)", "easy")]
        [DataRow("assets/scfa/balthazar-01.SCFAReplay", "Lundquist (AIx: Sorian AI)", "soriancheat")]
        public void AiArmiesTest(string file, string armyName, string expectedPersonality)
        {
            ReplayHeader header = Load(file).Header;

            ReplayPlayerOptions ai = header.Armies.Single(candidate => candidate.PlayerName == armyName);

            Assert.IsNull(ai.SourceId);
            Assert.IsFalse(ai.Human!.Value);
            Assert.AreEqual(expectedPersonality, ai.AIPersonality);
        }

        /// <summary>
        /// Cross-validation: the faction in the header must match the faction implied by the
        /// first construction order of that player. Land factories are faction specific:
        /// ueb0101 (UEF), uab0101 (Aeon), urb0101 (Cybran), xsb0101 (Seraphim).
        /// This proves both the faction numbering and the SourceId linkage.
        /// </summary>
        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay", 12)]
        [DataRow("assets/faforever/23225104.fafreplay", 8)]
        public void FactionMatchesFirstBuildOrderTest(string file, int expectedValidatedPlayers)
        {
            Replay replay = Load(file);

            int validated = 0;
            foreach (ReplayPlayerOptions army in replay.Header.Armies)
            {
                if (army.SourceId is not int sourceId || army.Human != true)
                {
                    continue;
                }

                List<ReplayAnalysis.BuildOrderEntry> buildOrder = ReplayAnalysis.GetBuildOrder(replay, sourceId);
                if (buildOrder.Count == 0)
                {
                    continue;
                }

                int? impliedFaction = buildOrder[0].BlueprintId[..2] switch
                {
                    "ue" => 1,
                    "ua" => 2,
                    "ur" => 3,
                    "xs" => 4,
                    _ => null,
                };

                if (impliedFaction is not null)
                {
                    Assert.AreEqual(impliedFaction, army.Faction, $"Faction mismatch for {army.PlayerName} ({buildOrder[0].BlueprintId})");
                    validated++;
                }
            }

            Assert.AreEqual(expectedValidatedPlayers, validated);
        }

        /// <summary>
        /// The colour indices of the header resolve against lua/GameColors.lua. The expected
        /// values are cross-checked against the colours that SpawnPing marker callbacks carry
        /// for the same players in this replay.
        /// </summary>
        [TestMethod]
        [DataRow("assets/faforever/23225104.fafreplay", 0, "#e80a0a")]
        [DataRow("assets/faforever/23225104.fafreplay", 7, "#b76518")]
        [DataRow("assets/faforever/23225104.fafreplay", 2, "#436eee")]
        [DataRow("assets/faforever/23225104.fafreplay", 5, "#2f4f4f")]
        public void PlayerColorResolvesAgainstGameColorsTest(string file, int sourceId, string expectedColor)
        {
            ReplayHeader header = Load(file).Header;

            ReplayPlayerOptions army = header.Armies.Single(candidate => candidate.SourceId == sourceId);

            Assert.AreEqual(expectedColor, army.Color);
        }

        [TestMethod]
        [DataRow("assets/faforever/TestCommands01.fafreplay", 2, 2218)]
        [DataRow("assets/faforever/23225104.fafreplay", 6, 1599)]
        [DataRow("assets/faforever/zstd/22338092.fafreplay", 0, 1676)]
        public void RatingFollowsTheDisplayConventionTest(string file, int sourceId, int expectedRating)
        {
            ReplayHeader header = Load(file).Header;

            ReplayPlayerOptions army = header.Armies.Single(candidate => candidate.SourceId == sourceId);

            // mean - 3 x deviation, rounded - the convention FAForever uses everywhere.
            Assert.AreEqual(expectedRating, army.Rating);
        }
    }
}
