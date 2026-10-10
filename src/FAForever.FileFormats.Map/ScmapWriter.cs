using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace FAForever.FileFormats.Map
{
    /// <summary>
    /// Writes the binary map format (<c>.scmap</c>), versions 53, 56 and 60: the reverse of
    /// <see cref="ScmapParser"/>. A parsed map writes back to the same bytes.
    /// </summary>
    /// <remarks>
    /// The layout follows the saver of the FAF map editor (<c>Assets/Scripts/HazardX SCMAP Code/Map.cs</c>
    /// in FAForever/FAForeverMapEditor). The fields the parser does not keep are constants there and
    /// in every map of the vault: they are written as such.
    /// </remarks>
    public static class ScmapWriter
    {
        /// <summary>
        /// Writes a map to a new array.
        /// </summary>
        public static byte[] Write(Scmap scmap)
        {
            using MemoryStream memory = new MemoryStream();
            Write(scmap, memory);
            return memory.ToArray();
        }

        /// <summary>
        /// Writes a map to a stream, from its current position.
        /// </summary>
        /// <exception cref="ArgumentException">The map does not fit its version, e.g. a version 56 map
        /// without minimap colours, or arrays whose size does not follow the map's size.</exception>
        public static void Write(Scmap scmap, Stream stream)
        {
            Validate(scmap);
            Writer w = new Writer(stream);

            w.Int32(ScmapParser.Magic);
            w.Int32(2);
            w.UInt32(0xbeeffeed);
            w.Int32(2);
            w.Single(scmap.Width);
            w.Single(scmap.Height);
            w.Int32(0);
            w.Int16(0);

            w.Blob(scmap.Preview);

            int version = scmap.Version;
            w.Int32(version);

            ScmapHeightmap heightmap = scmap.Heightmap;
            w.Int32(heightmap.Width);
            w.Int32(heightmap.Height);
            w.Single(heightmap.Scale);
            w.UInt16s(heightmap.Samples);

            if (version >= 56)
            {
                w.Byte(0);
            }

            w.String(scmap.TerrainShader);
            w.String(scmap.BackgroundTexture);
            w.String(scmap.SkyCubemap);

            if (version >= 56)
            {
                w.Int32(scmap.EnvironmentCubemaps.Count);
                foreach (ScmapCubemap cubemap in scmap.EnvironmentCubemaps)
                {
                    w.String(cubemap.Name);
                    w.String(cubemap.Texture);
                }
            }
            else
            {
                w.String(scmap.EnvironmentCubemaps[0].Texture);
            }

            ScmapLighting lighting = scmap.Lighting;
            w.Single(lighting.LightingMultiplier);
            w.Vector3(lighting.SunDirection);
            w.Vector3(lighting.SunAmbience);
            w.Vector3(lighting.SunColor);
            w.Vector3(lighting.ShadowFillColor);
            w.Vector4(lighting.SpecularColor);
            w.Single(lighting.Bloom);
            w.Vector3(lighting.FogColor);
            w.Single(lighting.FogStart);
            w.Single(lighting.FogEnd);

            WriteWater(ref w, scmap.Water);

            w.Int32(scmap.WaveGenerators.Count);
            foreach (ScmapWaveGenerator generator in scmap.WaveGenerators)
            {
                w.String(generator.Texture);
                w.String(generator.Ramp);
                w.Vector3(generator.Position);
                w.Single(generator.Rotation);
                w.Vector3(generator.Velocity);
                w.Single(generator.LifetimeFirst);
                w.Single(generator.LifetimeSecond);
                w.Single(generator.PeriodFirst);
                w.Single(generator.PeriodSecond);
                w.Single(generator.ScaleFirst);
                w.Single(generator.ScaleSecond);
                w.Single(generator.FrameCount);
                w.Single(generator.FrameRateFirst);
                w.Single(generator.FrameRateSecond);
                w.Single(generator.StripCount);
            }

            if (version >= 56)
            {
                ScmapMinimap minimap = scmap.Minimap!;
                w.Int32(minimap.ContourInterval);
                w.UInt32(minimap.DeepWaterColor);
                w.UInt32(minimap.ContourColor);
                w.UInt32(minimap.ShoreColor);
                w.UInt32(minimap.LandStartColor);
                w.UInt32(minimap.LandEndColor);
                if (version > 56)
                {
                    w.Single(minimap.Unknown ?? 0f);
                }

                // ten albedo textures, then nine normal maps: the top layer has none
                foreach (ScmapTerrainLayer layer in scmap.Layers)
                {
                    w.String(layer.Albedo);
                    w.Single(layer.AlbedoScale);
                }
                foreach (ScmapTerrainLayer layer in scmap.Layers.Take(scmap.Layers.Count - 1))
                {
                    w.String(layer.Normal);
                    w.Single(layer.NormalScale);
                }
            }
            else
            {
                w.String(scmap.Tileset ?? "No Tileset");
                w.Int32(scmap.Layers.Count);
                foreach (ScmapTerrainLayer layer in scmap.Layers)
                {
                    w.String(layer.Albedo);
                    w.String(layer.Normal);
                    w.Single(layer.AlbedoScale);
                    w.Single(layer.NormalScale);
                }
            }

            w.Int32(scmap.DecalHeader.First);
            w.Int32(scmap.DecalHeader.Second);

            w.Int32(scmap.Decals.Count);
            foreach (ScmapDecal decal in scmap.Decals)
            {
                w.Int32(decal.Id);
                w.Int32(decal.Type);
                w.Int32(decal.Textures.Count);
                foreach (string texture in decal.Textures)
                {
                    w.Blob(Encoding.UTF8.GetBytes(texture));
                }
                w.Vector3(decal.Scale);
                w.Vector3(decal.Position);
                w.Vector3(decal.Rotation);
                w.Single(decal.CutOffLod);
                w.Single(decal.NearCutOffLod);
                w.Int32(decal.OwnerArmy);
            }

            w.Int32(scmap.DecalGroups.Count);
            foreach (ScmapDecalGroup group in scmap.DecalGroups)
            {
                w.Int32(group.Id);
                w.String(group.Name);
                w.Int32(group.DecalIds.Count);
                foreach (int decalId in group.DecalIds)
                {
                    w.Int32(decalId);
                }
            }

            w.Int32(scmap.Width);
            w.Int32(scmap.Height);

            w.Int32(scmap.NormalMaps.Count);
            foreach (byte[] normalMap in scmap.NormalMaps)
            {
                w.Blob(normalMap);
            }

            if (version < 56)
            {
                w.Int32(1);
            }
            foreach (byte[] mask in scmap.StratumMasks)
            {
                w.Blob(mask);
            }

            w.Int32(1);
            w.Blob(scmap.WaterMap);

            w.Bytes(scmap.WaterFoamMask);
            w.Bytes(scmap.WaterFlatnessMask);
            w.Bytes(scmap.WaterDepthBiasMask);
            w.Bytes(scmap.TerrainTypes);

            if (version >= 60)
            {
                WriteSkybox(ref w, scmap.Skybox!);
            }

            w.Int32(scmap.Props.Count);
            foreach (ScmapProp prop in scmap.Props)
            {
                w.String(prop.Blueprint);
                w.Vector3(prop.Position);
                w.Vector3(prop.RotationX);
                w.Vector3(prop.RotationY);
                w.Vector3(prop.RotationZ);
                w.Vector3(prop.Scale);
            }
        }

        /// <summary>
        /// Checks what the format cannot store, before anything is written.
        /// </summary>
        private static void Validate(Scmap scmap)
        {
            int version = scmap.Version;
            if (!ScmapParser.SupportedVersions.Contains(version))
            {
                throw new ArgumentException($"Unsupported map version {version}", nameof(scmap));
            }

            ScmapHeightmap heightmap = scmap.Heightmap;
            Require(heightmap.Samples.Length == (heightmap.Width + 1) * (heightmap.Height + 1),
                $"The heightmap has {heightmap.Samples.Length} samples, expected (width + 1) * (height + 1)");

            int halfSize = scmap.Width / 2 * (scmap.Height / 2);
            Require(scmap.WaterFoamMask.Length == halfSize, "The water foam mask must have one byte per two by two ogrids");
            Require(scmap.WaterFlatnessMask.Length == halfSize, "The water flatness mask must have one byte per two by two ogrids");
            Require(scmap.WaterDepthBiasMask.Length == halfSize, "The water depth bias mask must have one byte per two by two ogrids");
            Require(scmap.TerrainTypes.Length == scmap.Width * scmap.Height, "The terrain types must have one byte per ogrid");
            Require(scmap.Water.WaveTextures.Count == 4, "The water has exactly four wave textures");

            if (version >= 56)
            {
                Require(scmap.Minimap is not null, $"A version {version} map has minimap colours");
                Require(scmap.Layers.Count == 10, $"A version {version} map has ten terrain layers");
                Require(scmap.StratumMasks.Count == 2, $"A version {version} map has two stratum masks");
            }
            else
            {
                Require(scmap.EnvironmentCubemaps.Count == 1, "A version 53 map has one environment cubemap");
                Require(scmap.StratumMasks.Count == 1, "A version 53 map has one stratum mask");
            }

            if (version >= 60)
            {
                Require(scmap.Skybox is not null, $"A version {version} map has a skybox");
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new ArgumentException(message);
            }
        }

        private static void WriteWater(ref Writer w, ScmapWater water)
        {
            w.Byte(water.HasWater ? (byte)1 : (byte)0);
            w.Single(water.Elevation);
            w.Single(water.ElevationDeep);
            w.Single(water.ElevationAbyss);
            w.Vector3(water.SurfaceColor);
            w.Vector2(water.ColorLerp);
            w.Single(water.RefractionScale);
            w.Single(water.FresnelBias);
            w.Single(water.FresnelPower);
            w.Single(water.UnitReflection);
            w.Single(water.SkyReflection);
            w.Single(water.SunShininess);
            w.Single(water.SunStrength);
            w.Vector3(water.SunDirection);
            w.Vector3(water.SunColor);
            w.Single(water.SunReflection);
            w.Single(water.SunGlow);
            w.String(water.Cubemap);
            w.String(water.WaterRamp);

            // the four repeats come first, then the movement and texture of each layer
            foreach (ScmapWaveTexture wave in water.WaveTextures)
            {
                w.Single(wave.NormalRepeat);
            }
            foreach (ScmapWaveTexture wave in water.WaveTextures)
            {
                w.Vector2(wave.NormalMovement);
                w.String(wave.Texture);
            }
        }

        private static void WriteSkybox(ref Writer w, ScmapSkybox skybox)
        {
            w.Vector3(skybox.Position);
            w.Single(skybox.HorizonHeight);
            w.Single(skybox.Scale);
            w.Single(skybox.SubtractHeight);
            w.Int32(skybox.SubdivisionsAxis);
            w.Int32(skybox.SubdivisionsHeight);
            w.Single(skybox.ZenithHeight);
            w.Vector3(skybox.HorizonColor);
            w.Vector3(skybox.ZenithColor);
            w.Single(skybox.DecalGlowMultiplier);
            w.String(skybox.Albedo);
            w.String(skybox.Glow);

            w.Int32(skybox.Planets.Count);
            foreach (ScmapPlanet planet in skybox.Planets)
            {
                w.Vector3(planet.Position);
                w.Single(planet.Rotation);
                w.Vector2(planet.Scale);
                w.Vector4(planet.Uv);
            }

            w.Byte(skybox.MidColor.Red);
            w.Byte(skybox.MidColor.Green);
            w.Byte(skybox.MidColor.Blue);
            w.Single(skybox.CirrusMultiplier);
            w.Vector3(skybox.CirrusColor);
            w.String(skybox.CirrusTexture);

            w.Int32(skybox.CirrusLayers.Count);
            foreach (ScmapCirrusLayer layer in skybox.CirrusLayers)
            {
                w.Vector2(layer.Frequency);
                w.Single(layer.Speed);
                w.Vector2(layer.Direction);
            }

            w.Single(skybox.Clouds7);
        }

        /// <summary>
        /// Little-endian writes to a stream.
        /// </summary>
        private ref struct Writer(Stream stream)
        {
            private readonly Stream _stream = stream;

            public readonly void Byte(byte value) => _stream.WriteByte(value);

            public readonly void Int16(short value)
            {
                Span<byte> buffer = stackalloc byte[2];
                BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
                _stream.Write(buffer);
            }

            public readonly void Int32(int value)
            {
                Span<byte> buffer = stackalloc byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
                _stream.Write(buffer);
            }

            public readonly void UInt32(uint value)
            {
                Span<byte> buffer = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
                _stream.Write(buffer);
            }

            public readonly void Single(float value)
            {
                Span<byte> buffer = stackalloc byte[4];
                BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
                _stream.Write(buffer);
            }

            public readonly void Vector2(Vector2 value)
            {
                Single(value.X);
                Single(value.Y);
            }

            public readonly void Vector3(Vector3 value)
            {
                Single(value.X);
                Single(value.Y);
                Single(value.Z);
            }

            public readonly void Vector4(Vector4 value)
            {
                Single(value.X);
                Single(value.Y);
                Single(value.Z);
                Single(value.W);
            }

            public readonly void Bytes(ReadOnlySpan<byte> bytes) => _stream.Write(bytes);

            /// <summary>
            /// Bytes after their length.
            /// </summary>
            public readonly void Blob(ReadOnlySpan<byte> bytes)
            {
                Int32(bytes.Length);
                _stream.Write(bytes);
            }

            public readonly void UInt16s(ushort[] values)
            {
                byte[] raw = new byte[checked(values.Length * 2)];
                for (int i = 0; i < values.Length; i++)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(i * 2, 2), values[i]);
                }
                _stream.Write(raw);
            }

            /// <summary>
            /// A string that ends with a zero byte.
            /// </summary>
            public readonly void String(string text)
            {
                if (text.Contains('\0'))
                {
                    throw new ArgumentException($"A string in a map cannot contain a zero byte: {text}", "scmap");
                }
                _stream.Write(Encoding.UTF8.GetBytes(text));
                _stream.WriteByte(0);
            }
        }
    }
}
