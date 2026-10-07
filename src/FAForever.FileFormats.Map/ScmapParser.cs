using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace FAForever.FileFormats.Map
{
    /// <summary>
    /// Thrown when the bytes are not a map file this parser understands.
    /// </summary>
    public sealed class ScmapFormatException(string message, long offset)
        : FormatException($"At byte {offset}: {message}")
    {
        /// <summary>
        /// The position in the file where the problem was found.
        /// </summary>
        public long Offset { get; } = offset;
    }

    /// <summary>
    /// Reads the binary map format (<c>.scmap</c>), versions 53, 56 and 60.
    /// </summary>
    public static class ScmapParser
    {
        /// <summary>
        /// The first four bytes of a map file: "Map" followed by 0x1a.
        /// </summary>
        public const int Magic = 0x1a70614d;

        /// <summary>
        /// The versions this parser reads.
        /// </summary>
        public static IReadOnlyList<int> SupportedVersions { get; } = [53, 56, 60];

        /// <summary>
        /// Reads a map from a stream, from its current position to the end of the map.
        /// </summary>
        public static Scmap Parse(Stream stream)
        {
            using MemoryStream memory = new MemoryStream();
            stream.CopyTo(memory);
            return Parse(memory.GetBuffer().AsSpan(0, (int)memory.Length));
        }

        /// <summary>
        /// Reads a map. Every byte must belong to the map: trailing bytes are an error.
        /// </summary>
        public static Scmap Parse(ReadOnlySpan<byte> bytes)
        {
            Reader r = new Reader(bytes);

            if (r.Int32() != Magic)
            {
                throw new ScmapFormatException("Not a map file (the magic number is missing)", 0);
            }

            int major = r.Int32();
            if (major != 2)
            {
                throw new ScmapFormatException($"Unknown major version {major}, expected 2", 4);
            }

            // 0xbeeffeed, 2, the width and height as floats, 0 (int) and 0 (short): the heightmap
            // repeats the size, so none of it is kept
            r.Skip(4 + 4 + 4 + 4 + 4 + 2);

            byte[] preview = r.Bytes(r.Count());

            int version = r.Int32();
            if (!SupportedVersions.Contains(version))
            {
                throw new ScmapFormatException($"Unsupported map version {version}", r.Position - 4);
            }

            int width = r.Int32();
            int height = r.Int32();
            float heightScale = r.Single();
            ushort[] samples = r.UInt16s(checked((width + 1) * (height + 1)));
            ScmapHeightmap heightmap = new ScmapHeightmap(width, height, heightScale, samples);

            if (version >= 56)
            {
                r.Skip(1); // always 0
            }

            string terrainShader = r.String();
            string background = r.String();
            string skyCubemap = r.String();

            ScmapCubemap[] cubemaps;
            if (version >= 56)
            {
                cubemaps = new ScmapCubemap[r.Count()];
                for (int i = 0; i < cubemaps.Length; i++)
                {
                    cubemaps[i] = new ScmapCubemap(r.String(), r.String());
                }
            }
            else
            {
                cubemaps = [new ScmapCubemap("<default>", r.String())];
            }

            ScmapLighting lighting = new ScmapLighting(
                LightingMultiplier: r.Single(),
                SunDirection: r.Vector3(),
                SunAmbience: r.Vector3(),
                SunColor: r.Vector3(),
                ShadowFillColor: r.Vector3(),
                SpecularColor: r.Vector4(),
                Bloom: r.Single(),
                FogColor: r.Vector3(),
                FogStart: r.Single(),
                FogEnd: r.Single());

            ScmapWater water = ReadWater(ref r);

            ScmapWaveGenerator[] waveGenerators = new ScmapWaveGenerator[r.Count()];
            for (int i = 0; i < waveGenerators.Length; i++)
            {
                waveGenerators[i] = new ScmapWaveGenerator(
                    Texture: r.String(),
                    Ramp: r.String(),
                    Position: r.Vector3(),
                    Rotation: r.Single(),
                    Velocity: r.Vector3(),
                    LifetimeFirst: r.Single(),
                    LifetimeSecond: r.Single(),
                    PeriodFirst: r.Single(),
                    PeriodSecond: r.Single(),
                    ScaleFirst: r.Single(),
                    ScaleSecond: r.Single(),
                    FrameCount: r.Single(),
                    FrameRateFirst: r.Single(),
                    FrameRateSecond: r.Single(),
                    StripCount: r.Single());
            }

            ScmapMinimap? minimap = null;
            string? tileset = null;
            ScmapTerrainLayer[] layers;
            if (version >= 56)
            {
                minimap = new ScmapMinimap(
                    ContourInterval: r.Int32(),
                    DeepWaterColor: r.UInt32(),
                    ContourColor: r.UInt32(),
                    ShoreColor: r.UInt32(),
                    LandStartColor: r.UInt32(),
                    LandEndColor: r.UInt32(),
                    Unknown: version > 56 ? r.Single() : null);

                // ten albedo textures, then nine normal maps: the top layer has none
                (string Path, float Scale)[] albedos = new (string, float)[10];
                for (int i = 0; i < albedos.Length; i++)
                {
                    albedos[i] = (r.String(), r.Single());
                }

                layers = new ScmapTerrainLayer[albedos.Length];
                for (int i = 0; i < layers.Length; i++)
                {
                    (string normal, float normalScale) = i < layers.Length - 1 ? (r.String(), r.Single()) : ("", 0f);
                    layers[i] = new ScmapTerrainLayer(albedos[i].Path, albedos[i].Scale, normal, normalScale);
                }
            }
            else
            {
                tileset = r.String();
                layers = new ScmapTerrainLayer[r.Count()];
                for (int i = 0; i < layers.Length; i++)
                {
                    string albedo = r.String();
                    string normal = r.String();
                    layers[i] = new ScmapTerrainLayer(albedo, r.Single(), normal, r.Single());
                }
            }

            (int, int) decalHeader = (r.Int32(), r.Int32());

            ScmapDecal[] decals = new ScmapDecal[r.Count()];
            for (int i = 0; i < decals.Length; i++)
            {
                int id = r.Int32();
                int type = r.Int32();
                string[] textures = new string[r.Count()];
                for (int t = 0; t < textures.Length; t++)
                {
                    textures[t] = r.String(r.Count());
                }
                decals[i] = new ScmapDecal(
                    Id: id,
                    Type: type,
                    Textures: textures,
                    Scale: r.Vector3(),
                    Position: r.Vector3(),
                    Rotation: r.Vector3(),
                    CutOffLod: r.Single(),
                    NearCutOffLod: r.Single(),
                    OwnerArmy: r.Int32());
            }

            ScmapDecalGroup[] decalGroups = new ScmapDecalGroup[r.Count()];
            for (int i = 0; i < decalGroups.Length; i++)
            {
                int id = r.Int32();
                string name = r.String();
                int[] decalIds = new int[r.Count()];
                for (int d = 0; d < decalIds.Length; d++)
                {
                    decalIds[d] = r.Int32();
                }
                decalGroups[i] = new ScmapDecalGroup(id, name, decalIds);
            }

            // the size again, which the masks below use
            int textureWidth = r.Int32();
            int textureHeight = r.Int32();

            byte[][] normalMaps = new byte[r.Count()][];
            for (int i = 0; i < normalMaps.Length; i++)
            {
                normalMaps[i] = r.Bytes(r.Count());
            }

            byte[][] stratumMasks;
            if (version >= 56)
            {
                stratumMasks = [r.Bytes(r.Count()), r.Bytes(r.Count())];
            }
            else
            {
                r.Skip(4); // always 1
                stratumMasks = [r.Bytes(r.Count())];
            }

            r.Skip(4); // always 1
            byte[] waterMap = r.Bytes(r.Count());

            int halfSize = checked(textureWidth / 2 * (textureHeight / 2));
            byte[] foam = r.Bytes(halfSize);
            byte[] flatness = r.Bytes(halfSize);
            byte[] depthBias = r.Bytes(halfSize);
            byte[] terrainTypes = r.Bytes(checked(textureWidth * textureHeight));

            ScmapSkybox? skybox = version >= 60 ? ReadSkybox(ref r) : null;

            ScmapProp[] props = new ScmapProp[r.Count()];
            for (int i = 0; i < props.Length; i++)
            {
                props[i] = new ScmapProp(
                    Blueprint: r.String(),
                    Position: r.Vector3(),
                    RotationX: r.Vector3(),
                    RotationY: r.Vector3(),
                    RotationZ: r.Vector3(),
                    Scale: r.Vector3());
            }

            if (r.Remaining != 0)
            {
                throw new ScmapFormatException($"{r.Remaining} bytes after the props", r.Position);
            }

            return new Scmap
            {
                Version = version,
                Preview = preview,
                Heightmap = heightmap,
                TerrainShader = terrainShader,
                BackgroundTexture = background,
                SkyCubemap = skyCubemap,
                EnvironmentCubemaps = cubemaps,
                Lighting = lighting,
                Water = water,
                WaveGenerators = waveGenerators,
                Minimap = minimap,
                Tileset = tileset,
                Layers = layers,
                DecalHeader = decalHeader,
                Decals = decals,
                DecalGroups = decalGroups,
                NormalMaps = normalMaps,
                StratumMasks = stratumMasks,
                WaterMap = waterMap,
                WaterFoamMask = foam,
                WaterFlatnessMask = flatness,
                WaterDepthBiasMask = depthBias,
                TerrainTypes = terrainTypes,
                Skybox = skybox,
                Props = props,
            };
        }

        private static ScmapWater ReadWater(ref Reader r)
        {
            bool hasWater = r.Byte() == 1;
            float elevation = r.Single();
            float elevationDeep = r.Single();
            float elevationAbyss = r.Single();
            Vector3 surfaceColor = r.Vector3();
            Vector2 colorLerp = r.Vector2();
            float refractionScale = r.Single();
            float fresnelBias = r.Single();
            float fresnelPower = r.Single();
            float unitReflection = r.Single();
            float skyReflection = r.Single();
            float sunShininess = r.Single();
            float sunStrength = r.Single();
            Vector3 sunDirection = r.Vector3();
            Vector3 sunColor = r.Vector3();
            float sunReflection = r.Single();
            float sunGlow = r.Single();
            string cubemap = r.String();
            string waterRamp = r.String();

            // the four repeats come first, then the movement and texture of each layer
            float[] repeats = [r.Single(), r.Single(), r.Single(), r.Single()];
            ScmapWaveTexture[] waveTextures = new ScmapWaveTexture[repeats.Length];
            for (int i = 0; i < waveTextures.Length; i++)
            {
                waveTextures[i] = new ScmapWaveTexture(repeats[i], r.Vector2(), r.String());
            }

            return new ScmapWater
            {
                HasWater = hasWater,
                Elevation = elevation,
                ElevationDeep = elevationDeep,
                ElevationAbyss = elevationAbyss,
                SurfaceColor = surfaceColor,
                ColorLerp = colorLerp,
                RefractionScale = refractionScale,
                FresnelBias = fresnelBias,
                FresnelPower = fresnelPower,
                UnitReflection = unitReflection,
                SkyReflection = skyReflection,
                SunShininess = sunShininess,
                SunStrength = sunStrength,
                SunDirection = sunDirection,
                SunColor = sunColor,
                SunReflection = sunReflection,
                SunGlow = sunGlow,
                Cubemap = cubemap,
                WaterRamp = waterRamp,
                WaveTextures = waveTextures,
            };
        }

        private static ScmapSkybox ReadSkybox(ref Reader r)
        {
            Vector3 position = r.Vector3();
            float horizonHeight = r.Single();
            float scale = r.Single();
            float subtractHeight = r.Single();
            int subdivisionsAxis = r.Int32();
            int subdivisionsHeight = r.Int32();
            float zenithHeight = r.Single();
            Vector3 horizonColor = r.Vector3();
            Vector3 zenithColor = r.Vector3();
            float decalGlowMultiplier = r.Single();
            string albedo = r.String();
            string glow = r.String();

            ScmapPlanet[] planets = new ScmapPlanet[r.Count()];
            for (int i = 0; i < planets.Length; i++)
            {
                planets[i] = new ScmapPlanet(r.Vector3(), r.Single(), r.Vector2(), r.Vector4());
            }

            (byte, byte, byte) midColor = (r.Byte(), r.Byte(), r.Byte());
            float cirrusMultiplier = r.Single();
            Vector3 cirrusColor = r.Vector3();
            string cirrusTexture = r.String();

            ScmapCirrusLayer[] cirrusLayers = new ScmapCirrusLayer[r.Count()];
            for (int i = 0; i < cirrusLayers.Length; i++)
            {
                cirrusLayers[i] = new ScmapCirrusLayer(r.Vector2(), r.Single(), r.Vector2());
            }

            return new ScmapSkybox
            {
                Position = position,
                HorizonHeight = horizonHeight,
                Scale = scale,
                SubtractHeight = subtractHeight,
                SubdivisionsAxis = subdivisionsAxis,
                SubdivisionsHeight = subdivisionsHeight,
                ZenithHeight = zenithHeight,
                HorizonColor = horizonColor,
                ZenithColor = zenithColor,
                DecalGlowMultiplier = decalGlowMultiplier,
                Albedo = albedo,
                Glow = glow,
                Planets = planets,
                MidColor = midColor,
                CirrusMultiplier = cirrusMultiplier,
                CirrusColor = cirrusColor,
                CirrusTexture = cirrusTexture,
                CirrusLayers = cirrusLayers,
                Clouds7 = r.Single(),
            };
        }

        /// <summary>
        /// Little-endian reads over the map's bytes that report where a truncated file ends.
        /// </summary>
        private ref struct Reader(ReadOnlySpan<byte> bytes)
        {
            private readonly ReadOnlySpan<byte> _bytes = bytes;

            public int Position { get; private set; }

            public readonly int Remaining => _bytes.Length - Position;

            private ReadOnlySpan<byte> Take(int length)
            {
                if (length < 0 || length > Remaining)
                {
                    throw new ScmapFormatException($"Unexpected end of the file (needed {length} bytes, {Remaining} left)", Position);
                }
                ReadOnlySpan<byte> slice = _bytes.Slice(Position, length);
                Position += length;
                return slice;
            }

            public void Skip(int length) => Take(length);

            public byte Byte() => Take(1)[0];

            public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

            public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

            public float Single() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));

            /// <summary>
            /// A count or length, which cannot be negative.
            /// </summary>
            public int Count()
            {
                int start = Position;
                int count = Int32();
                if (count < 0)
                {
                    throw new ScmapFormatException($"Negative count {count}", start);
                }
                return count;
            }

            public Vector2 Vector2() => new Vector2(Single(), Single());

            public Vector3 Vector3() => new Vector3(Single(), Single(), Single());

            public Vector4 Vector4() => new Vector4(Single(), Single(), Single(), Single());

            public byte[] Bytes(int length) => Take(length).ToArray();

            public ushort[] UInt16s(int count)
            {
                ReadOnlySpan<byte> raw = Take(checked(count * 2));
                ushort[] values = new ushort[count];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(i * 2, 2));
                }
                return values;
            }

            /// <summary>
            /// A string that ends with a zero byte.
            /// </summary>
            public string String()
            {
                int end = _bytes[Position..].IndexOf((byte)0);
                if (end < 0)
                {
                    throw new ScmapFormatException("Unexpected end of the file in a string", Position);
                }
                string text = Encoding.UTF8.GetString(Take(end));
                Position++;
                return text;
            }

            /// <summary>
            /// A string of a known length, without a zero byte.
            /// </summary>
            public string String(int length) => Encoding.UTF8.GetString(Take(length));
        }
    }
}
