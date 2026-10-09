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
    /// <param name="Parent">The index of the parent bone, -1 for the root.</param>
    /// <param name="Reserved1">A number that the exporter of the game fills and other exporters leave
    /// 0; its meaning is unknown. Kept so that a mesh writes back to the same bytes.</param>
    /// <param name="Reserved2">As <paramref name="Reserved1"/>.</param>
    public sealed record ScmBone(
        string Name,
        Matrix4x4 RestPoseInverse,
        Vector3 Position,
        Quaternion Rotation,
        int Parent,
        int Reserved1 = 0,
        int Reserved2 = 0);

    /// <summary>
    /// A vertex of a mesh in the rest pose.
    /// </summary>
    /// <param name="Position">The position in the mesh's space.</param>
    /// <param name="Normal">The normal of the surface.</param>
    /// <param name="Tangent">The tangent of the normal map's space.</param>
    /// <param name="Binormal">The binormal of the normal map's space.</param>
    /// <param name="Uv0">The texture coordinates of the albedo and the other textures of the unit.</param>
    /// <param name="Uv1">A second set of texture coordinates, which some shaders use.</param>
    /// <param name="Bone0">The bone the vertex moves with.</param>
    /// <param name="Bone1">A second bone, for skinning. The game's meshes leave it 0, as the next two.</param>
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
