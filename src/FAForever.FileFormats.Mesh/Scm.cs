using System.Numerics;

namespace FAForever.FileFormats.Mesh
{
    /// <summary>
    /// A mesh of the game (<c>.scm</c>, version 5): the model of a unit or a prop at one level of
    /// detail (<c>&lt;name&gt;_lod0.scm</c>, <c>_lod1.scm</c>, ...). Read it with <see cref="ScmParser"/>,
    /// write it with <see cref="ScmWriter"/>.
    /// </summary>
    /// <remarks>
    /// Every vertex is attached to one bone, so a mesh is the vertices in the rest pose and the
    /// skeleton the game animates them with. The game draws triangles from <see cref="Indices"/>,
    /// three per triangle. Coordinates are in the game's space: y is up, one unit is one ogrid.
    /// </remarks>
    public sealed record Scm
    {
        /// <summary>
        /// The skeleton. A bone's <see cref="ScmBone.Parent"/> is an index into this list.
        /// </summary>
        public required IReadOnlyList<ScmBone> Bones { get; init; }

        public required IReadOnlyList<ScmVertex> Vertices { get; init; }

        /// <summary>
        /// The triangles: three indices into <see cref="Vertices"/> each.
        /// </summary>
        public required ushort[] Indices { get; init; }

        /// <summary>
        /// Notes of the exporter, such as the machine, the folder and the time it was exported on
        /// (<c>Machine:JALBERT-203</c>) or the name of the exporter.
        /// </summary>
        public required IReadOnlyList<string> Information { get; init; }

        /// <summary>
        /// The number of bones the game skins the vertices with, as the header stores it (at byte
        /// 0x0C): the bones of the GPU skinning palette, the first ones of <see cref="Bones"/>; the
        /// bones after them are attachment points that no vertex moves with. Null leaves it to
        /// <see cref="ScmWriter"/>, which writes <see cref="ScmWriter.WeightedBoneCount"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="ScmParser"/> keeps what the file says, so a mesh writes back to the same bytes:
        /// the game's exporter stores one more than the highest bone a vertex uses, some of FAF's
        /// meshes store the number of all bones (<c>UES0304_LOD0</c>: 26 where its vertices use 9).
        /// The engine gives a draw 80 palette slots (<see cref="ScmWriter.MaxSkinnedBones"/>); see
        /// the library's guide (<c>AGENTS.md</c>).
        /// </remarks>
        public int? SkinnedBoneCount { get; init; }

        /// <summary>
        /// The number of triangles.
        /// </summary>
        public int TriangleCount => Indices.Length / 3;
    }

    /// <summary>
    /// A bone of a mesh.
    /// </summary>
    /// <param name="Name">The name the game knows the bone by, e.g. <c>Turret_Barrel</c>.</param>
    /// <param name="RestPoseInverse">From the mesh's space to the bone's space in the rest pose.</param>
    /// <param name="Position">The position relative to the parent bone.</param>
    /// <param name="Rotation">The rotation relative to the parent bone.</param>
    /// <param name="Parent">The index of the parent bone, -1 for the root. A parent comes before its
    /// children: the engine updates the pose in one pass from the first bone to the last.</param>
    /// <param name="FirstVertex">The index of the first vertex that moves with the bone. The game's
    /// exporter sorts the vertices and triangles by bone and fills this in; other exporters mostly
    /// leave it 0. The engine never reads it; it is kept so that a mesh writes back to the same bytes.</param>
    /// <param name="TriangleCount">The number of triangles of the bone, filled in by the game's
    /// exporter as <paramref name="FirstVertex"/> is, and never read by the engine.</param>
    public sealed record ScmBone(
        string Name,
        Matrix4x4 RestPoseInverse,
        Vector3 Position,
        Quaternion Rotation,
        int Parent,
        int FirstVertex = 0,
        int TriangleCount = 0);

    /// <summary>
    /// A vertex of a mesh in the rest pose.
    /// </summary>
    /// <param name="Position">The position in the mesh's space.</param>
    /// <param name="Normal">The normal of the surface.</param>
    /// <param name="Tangent">The tangent of the normal map's space; the normal map's alpha (its y)
    /// tilts the normal along it. In the game's meshes it points against the texture's u
    /// (<see cref="GlbWriter.Tangent"/> turns it round for glTF).</param>
    /// <param name="Binormal">The binormal of the normal map's space; the normal map's green (its x)
    /// tilts the normal along it. In the game's meshes it points against the texture's v.</param>
    /// <param name="Uv0">The texture coordinates of the albedo and the specular (team colour) map.</param>
    /// <param name="Uv1">The second set of texture coordinates, which the game's shaders read the normal
    /// map with (<c>texcoord0.zw</c> in <c>effects/mesh.fx</c>).</param>
    /// <param name="Bone0">The bone the vertex moves with, with weight 1: the only bone the game's
    /// shaders read.</param>
    /// <param name="Bone1">A second bone, for skinning. The game's meshes leave it 0, as the next two,
    /// and the shaders never read them.</param>
    /// <param name="Bone2">A third bone.</param>
    /// <param name="Bone3">A fourth bone.</param>
    public readonly record struct ScmVertex(
        Vector3 Position,
        Vector3 Normal,
        Vector3 Tangent,
        Vector3 Binormal,
        Vector2 Uv0,
        Vector2 Uv1,
        byte Bone0,
        byte Bone1 = 0,
        byte Bone2 = 0,
        byte Bone3 = 0);
}
