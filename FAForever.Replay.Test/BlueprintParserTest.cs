using FAForever.Replay;

namespace FAForever.Replay.Test
{
    [TestClass]
    public class BlueprintParserTest
    {
        private static Blueprint ParseAsset(string source)
        {
            string path = Path.Combine("assets", "blueprints", source.TrimStart('/'));
            IReadOnlyList<Blueprint> blueprints = BlueprintParser.Parse(File.ReadAllText(path), source);
            Assert.AreEqual(1, blueprints.Count);
            return blueprints[0];
        }

        private static T ParseAsset<T>(string source) where T : Blueprint
        {
            Blueprint blueprint = ParseAsset(source);
            Assert.IsInstanceOfType<T>(blueprint);
            return (T)blueprint;
        }

        [TestMethod]
        [DataRow("/units/UEL0201/UEL0201_unit.bp", typeof(BlueprintUnit), "uel0201")]
        [DataRow("/units/DAA0206/DAA0206_unit.bp", typeof(BlueprintUnit), "daa0206")]
        // the largest unit blueprints: the four ACUs, a SACU and a few experimentals and ships
        [DataRow("/units/UEL0001/UEL0001_unit.bp", typeof(BlueprintUnit), "uel0001")]
        [DataRow("/units/URL0001/URL0001_unit.bp", typeof(BlueprintUnit), "url0001")]
        [DataRow("/units/UAL0001/UAL0001_unit.bp", typeof(BlueprintUnit), "ual0001")]
        [DataRow("/units/XSL0001/XSL0001_unit.bp", typeof(BlueprintUnit), "xsl0001")]
        [DataRow("/units/XSL0301/XSL0301_unit.bp", typeof(BlueprintUnit), "xsl0301")]
        [DataRow("/units/UEL0401/UEL0401_unit.bp", typeof(BlueprintUnit), "uel0401")]
        [DataRow("/units/UES0302/UES0302_unit.bp", typeof(BlueprintUnit), "ues0302")]
        [DataRow("/units/UAA0310/UAA0310_unit.bp", typeof(BlueprintUnit), "uaa0310")]
        [DataRow("/units/XEA0306/XEA0306_unit.bp", typeof(BlueprintUnit), "xea0306")]
        [DataRow("/projectiles/TDFGauss01/TDFGauss01_proj.bp", typeof(BlueprintProjectile), "/projectiles/tdfgauss01/tdfgauss01_proj.bp")]
        [DataRow("/projectiles/SIFInainoStrategicMissile01/SIFInainoStrategicMissile01_proj.bp", typeof(BlueprintProjectile), "/projectiles/sifinainostrategicmissile01/sifinainostrategicmissile01_proj.bp")]
        // sets BlueprintId = 'aeon_chrono_dampener_large_02', which the game ignores for emitters
        [DataRow("/effects/emitters/aeon_chrono_dampener_large_02_emit.bp", typeof(BlueprintEmitter), "/effects/emitters/aeon_chrono_dampener_large_02_emit.bp")]
        [DataRow("/effects/emitters/air_move_trail_beam_03_emit.bp", typeof(BlueprintBeam), "/effects/emitters/air_move_trail_beam_03_emit.bp")]
        [DataRow("/effects/emitters/contrail_polytrail_01_emit.bp", typeof(BlueprintTrailEmitter), "/effects/emitters/contrail_polytrail_01_emit.bp")]
        [DataRow("/effects/Entities/SeraphimShield01/SeraphimShield01_mesh.bp", typeof(BlueprintMesh), "/effects/entities/seraphimshield01/seraphimshield01_mesh")]
        [DataRow("/env/Evergreen/Props/Trees/Oak01_s2_prop.bp", typeof(BlueprintProp), "/env/evergreen/props/trees/oak01_s2_prop.bp")]
        [DataRow("/env/Seraphim II/Props/Rock_prop.bp", typeof(BlueprintProp), "/env/seraphim ii/props/rock_prop.bp")]
        public void ParsesRealBlueprints(string source, Type expectedType, string expectedId)
        {
            Blueprint blueprint = ParseAsset(source);

            Assert.AreEqual(expectedType, blueprint.GetType());
            Assert.AreEqual(expectedId, blueprint.BlueprintId);
            Assert.AreEqual(source, blueprint.Source);
        }

        [TestMethod]
        public void ReadsAUnit()
        {
            BlueprintUnit unit = ParseAsset<BlueprintUnit>("/units/UEL0201/UEL0201_unit.bp");

            Assert.AreEqual("<LOC uel0201_desc>Medium Tank", unit.Description);
            Assert.AreEqual("<LOC uel0201_name>MA12 Striker", unit.General.UnitName);
            Assert.AreEqual("UEF", unit.General.FactionName);
            Assert.IsTrue(unit.General.CommandCaps.Contains("RULEUCC_Attack"));
            Assert.AreEqual(1, unit.TechLevel);
            Assert.IsTrue(unit.HasCategory("land"));
            Assert.AreEqual(56, unit.Economy.BuildCostMass);
            Assert.AreEqual(300, unit.Economy.BuildTime);
            Assert.AreEqual("Normal", unit.Defense.ArmorType);
            Assert.AreEqual(300, unit.Defense.MaxHealth);
            Assert.IsNull(unit.Defense.Shield);
            Assert.AreEqual(3.4, unit.Physics.MaxSpeed);
            Assert.AreEqual("RULEUMT_Land", unit.Physics.MotionType);
            Assert.IsNull(unit.Air);

            // Sound { ... } keeps its table
            Assert.AreEqual("UELDestroy", unit.Audio["Destroyed"].Bank);

            BlueprintWeapon weapon = unit.Weapons[0];
            Assert.AreEqual(24, weapon.Damage);
            Assert.AreEqual(18, weapon.MaxRadius);
            Assert.AreEqual(1, weapon.RateOfFire); // written as 10/10
            Assert.AreEqual("/projectiles/TDFGauss01/TDFGauss01_proj.bp", weapon.ProjectileId);
            Assert.AreEqual("Land|Water|Seabed", weapon.FireTargetLayerCapsTable["Land"]);
        }

        [TestMethod]
        public void ReadsTheCommander()
        {
            BlueprintUnit acu = ParseAsset<BlueprintUnit>("/units/UEL0001/UEL0001_unit.bp");

            Assert.AreEqual(2000, acu.Economy.BuildCostMass);
            Assert.AreEqual(10, acu.Economy.BuildRate);
            Assert.IsNull(acu.TechLevel);
            Assert.IsTrue(acu.HasCategory("COMMAND"));

            // enhancements by name, without the Slots entry
            Assert.IsFalse(acu.Enhancements.ContainsKey("Slots"));
            BlueprintUnitEnhancement engineering = acu.Enhancements["AdvancedEngineering"];
            Assert.AreEqual("LCH", engineering.Slot);
            Assert.AreEqual(800, engineering.BuildCostMass);
            Assert.AreEqual(42, engineering.NewBuildRate);
            Assert.AreEqual("BUILTBYTIER2COMMANDER UEF", engineering.BuildableCategoryAdds);
            CollectionAssert.Contains(acu.Enhancements["AdvancedEngineeringRemove"].RemoveEnhancements.ToList(), "AdvancedEngineering");
            Assert.AreEqual(1500000, acu.Enhancements["Teleporter"].BuildCostEnergy);

            // weapons are a list, in file order
            CollectionAssert.AreEqual(
                new[] { "RightZephyr", "OverCharge", "AutoOverCharge", "TacMissile", "TacNukeMissile", "DeathWeapon", "TeleportWeapon" },
                acu.Weapons.Select(weapon => weapon.Label).ToArray());
            BlueprintWeaponOvercharge? overcharge = acu.Weapons[1].Overcharge;
            Assert.IsNotNull(overcharge);
            Assert.AreEqual(0.9, overcharge.EnergyMult);
            Assert.AreEqual(15000, overcharge.MaxDamage);
            Assert.AreEqual("TacticalMissile", acu.Weapons[3].EnabledByEnhancement);

            CollectionAssert.AreEqual(new[] { 1000.0, 1250, 1750, 2500, 3500 }, acu.VeteranMass.ToArray());
        }

        [TestMethod]
        public void ReadsOptionalUnitSections()
        {
            // the Continental: a UEF T3 air transport with a shield
            BlueprintUnit continental = ParseAsset<BlueprintUnit>("/units/XEA0306/XEA0306_unit.bp");

            Assert.IsNotNull(continental.Defense.Shield);
            Assert.AreEqual(3000, continental.Defense.Shield.ShieldMaxHealth);
            Assert.AreEqual(8.5, continental.Defense.Shield.ShieldSize);
            Assert.IsNotNull(continental.Air);
            Assert.IsTrue(continental.Air.CanFly);
            Assert.IsNotNull(continental.Transport);
            Assert.AreEqual(28, continental.Transport.Class1Capacity);
            Assert.AreEqual(true, continental.Transport.AirClass);
            Assert.IsNotNull(continental.Veteran);
            Assert.AreEqual(12, continental.Veteran.Level1);
        }

        [TestMethod]
        public void EvaluatesArithmetic()
        {
            // RateOfFire = 10/1, --10/integer interval in ticks
            BlueprintUnit unit = ParseAsset<BlueprintUnit>("/units/DAA0206/DAA0206_unit.bp");

            Assert.AreEqual(10, unit.Weapons[0].RateOfFire);
        }

        [TestMethod]
        public void ReadsProjectiles()
        {
            BlueprintProjectile gauss = ParseAsset<BlueprintProjectile>("/projectiles/TDFGauss01/TDFGauss01_proj.bp");
            Assert.AreEqual(12, gauss.Physics.InitialSpeed);
            Assert.AreEqual(360, gauss.Physics.TurnRate);
            Assert.AreEqual(true, gauss.Physics.DestroyOnWater);
            Assert.IsNull(gauss.Physics.Lifetime);
            Assert.IsNull(gauss.Economy);

            BlueprintProjectile missile = ParseAsset<BlueprintProjectile>("/projectiles/SIFInainoStrategicMissile01/SIFInainoStrategicMissile01_proj.bp");
            Assert.IsNotNull(missile.Economy);
            Assert.AreEqual(12000, missile.Economy.BuildCostMass);
            Assert.AreEqual("Seraphim", missile.General.Faction);
        }

        [TestMethod]
        public void ReadsProps()
        {
            BlueprintProp rock = ParseAsset<BlueprintProp>("/env/Seraphim II/Props/Rock_prop.bp");

            Assert.AreEqual(50, rock.Defense.MaxHealth);
            Assert.AreEqual(1, rock.Economy.ReclaimMassMax);
            Assert.AreEqual(1, rock.Economy.ReclaimTime);
        }

        [TestMethod]
        public void ReadsEffects()
        {
            BlueprintEmitter emitter = ParseAsset<BlueprintEmitter>("/effects/emitters/aeon_chrono_dampener_large_02_emit.bp");
            Assert.AreEqual("/textures/particles/ring_white_06.dds", emitter.Texture); // a [[long string]]
            Assert.AreEqual(1100, emitter.LODCutoff);
            Assert.AreEqual(true, emitter.Flat);
            Assert.IsNotNull(emitter.XDirectionCurve);
            Assert.AreEqual(1, emitter.XDirectionCurve.XRange);
            Assert.AreEqual(new BlueprintCurveKey(0.5, 0, 0), emitter.XDirectionCurve.Keys[0]);

            BlueprintBeam beam = ParseAsset<BlueprintBeam>("/effects/emitters/air_move_trail_beam_03_emit.bp");
            Assert.AreEqual(0.1, beam.Thickness);
            Assert.AreEqual(-0.65, beam.VShift);
            Assert.IsNotNull(beam.StartColor);
            Assert.AreEqual(1, beam.StartColor.X);
            Assert.AreEqual(0, beam.StartColor.W);

            BlueprintTrailEmitter trail = ParseAsset<BlueprintTrailEmitter>("/effects/emitters/contrail_polytrail_01_emit.bp");
            Assert.AreEqual(15, trail.TrailLength);
            Assert.AreEqual(-1, trail.Lifetime);
            Assert.AreEqual("/textures/particles/air_contrail_02.dds", trail.RepeatTexture);

            BlueprintMesh mesh = ParseAsset<BlueprintMesh>("/effects/Entities/SeraphimShield01/SeraphimShield01_mesh.bp");
            Assert.AreEqual(1, mesh.LODs.Count);
        }

        [TestMethod]
        public void KeepsTheRawTables()
        {
            BlueprintUnit acu = ParseAsset<BlueprintUnit>("/units/UEL0001/UEL0001_unit.bp");

            // fields without a typed property are still in the raw tables
            Assert.IsTrue(acu.Raw.TryGetNumberValue("LifeBarHeight", out double? lifeBarHeight));
            Assert.AreEqual(0.15, lifeBarHeight);
            Assert.IsTrue(acu.Enhancements["AdvancedEngineering"].Raw.TryGetTableValue("UpgradeEffectBones", out LuaData.Table? bones));
            Assert.AreEqual(4, bones!.Value.Count);
        }

        [TestMethod]
        public void MissingSectionsReadAsEmpty()
        {
            BlueprintUnit unit = (BlueprintUnit)BlueprintParser.Parse("UnitBlueprint { }", "/units/x/x_unit.bp")[0];

            Assert.IsNull(unit.Economy.BuildCostMass);
            Assert.AreEqual(0, unit.Weapons.Count);
            Assert.AreEqual(0, unit.General.CommandCaps.Count);
            Assert.IsNull(unit.Air);
        }

        [TestMethod]
        public void DerivesIdsLikeTheGame()
        {
            static string IdOf(string text, string source) => BlueprintParser.Parse(text, source)[0].BlueprintId;

            // units: the explicit id wins, otherwise the file name without its suffix
            Assert.AreEqual("mod0001", IdOf("UnitBlueprint { BlueprintId = 'mod0001' }", "/mods/x/units/a/a_unit.bp"));
            Assert.AreEqual("a", IdOf("UnitBlueprint { }", "/mods/x/units/a/A_unit.bp"));
            Assert.AreEqual("/odd.bp", IdOf("UnitBlueprint { }", "/odd.bp"));

            // meshes: the explicit id wins, otherwise the path without .bp
            Assert.AreEqual("/meshes/a_mesh", IdOf("MeshBlueprint { }", "/Meshes/A_mesh.bp"));

            // everything else: always the full path
            Assert.AreEqual("/projectiles/a/a_proj.bp", IdOf("ProjectileBlueprint { BlueprintId = 'ignored' }", "/projectiles/a/A_proj.bp"));
        }

        [TestMethod]
        public void ReadsSeveralBlueprintsFromOneFile()
        {
            IReadOnlyList<Blueprint> blueprints = BlueprintParser.Parse("""
                EmitterBlueprint { Lifetime = 1 }
                BeamBlueprint { Length = 2 }
                """, "/effects/x.bp");

            CollectionAssert.AreEqual(new[] { typeof(BlueprintEmitter), typeof(BlueprintBeam) }, blueprints.Select(blueprint => blueprint.GetType()).ToArray());
        }

        [TestMethod]
        public void ACommentOnlyFileDefinesNothing()
        {
            Assert.AreEqual(0, BlueprintParser.Parse("-- content of this file is unused", "/projectiles/x/x_proj.bp").Count);
        }
    }
}
