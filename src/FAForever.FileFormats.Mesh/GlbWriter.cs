using System.Numerics;
using System.Text;
using System.Text.Json;

namespace FAForever.FileFormats.Mesh
{
    /// <summary>
    /// Writes a mesh as glTF 2.0 in its binary form (<c>.glb</c>), which 3D tools and engines read:
    /// the triangles, the skeleton as a skin, and every vertex bound to its bone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The game's space carries over as it is: glTF is right-handed with y up, as the game is, a
    /// triangle's front winds counter-clockwise in both, and the texture coordinates of both start
    /// at the top left of the image. One game unit (ogrid) is one metre.
    /// </para>
    /// <para>
    /// Each bone becomes a node with its position and rotation relative to its parent, and the
    /// rest-pose inverses become the skin's inverse bind matrices: the game stores them row by row
    /// for row vectors, which is the column-major order glTF asks for. A vertex follows its first
    /// bone with weight 1, as the game draws it. The notes of the exporter are kept in
    /// <c>asset.extras.information</c>. Textures are not part of the mesh (a blueprint names them),
    /// so the mesh has no material.
    /// </para>
    /// <para>
    /// glTF wants normals and tangents of length 1: they are normalised, and a tangent of length 0
    /// or not a number (which some exporters write) is replaced by a direction across the normal. The binormal
    /// becomes the sign in the tangent's fourth component.
    /// </para>
    /// </remarks>
    public static class GlbWriter
    {
        private const uint Magic = 0x46546c67; // "glTF"
        private const uint JsonChunk = 0x4e4f534a; // "JSON"
        private const uint BinaryChunk = 0x004e4942; // "BIN\0"

        private const int Float = 5126;
        private const int UnsignedByte = 5121;
        private const int UnsignedShort = 5123;
        private const int ArrayBuffer = 34962;
        private const int ElementArrayBuffer = 34963;
        private const int Triangles = 4;

        /// <summary>
        /// Writes a mesh to a new array.
        /// </summary>
        /// <exception cref="ArgumentException">The mesh cannot be written: it has no triangles, a vertex
        /// or index points at nothing, or the bones' parents form a loop.</exception>
        public static byte[] Write(Scm scm)
        {
            Validate(scm);
            bool skinned = scm.Bones.Count > 0;
            int n = scm.Vertices.Count;

            using MemoryStream binary = new MemoryStream();
            List<(int Offset, int Length, int? Target)> views = [];
            int View(int? target, Action<BinaryWriter> write)
            {
                while (binary.Length % 4 != 0)
                {
                    binary.WriteByte(0);
                }
                int offset = (int)binary.Length;
                using (BinaryWriter writer = new BinaryWriter(binary, Encoding.UTF8, leaveOpen: true))
                {
                    write(writer);
                }
                views.Add((offset, (int)binary.Length - offset, target));
                return views.Count - 1;
            }

            Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);
            foreach (ScmVertex vertex in scm.Vertices)
            {
                min = Vector3.Min(min, vertex.Position);
                max = Vector3.Max(max, vertex.Position);
            }

            int positions = View(ArrayBuffer, w => { foreach (ScmVertex v in scm.Vertices) { Write(w, v.Position); } });
            int normals = View(ArrayBuffer, w => { foreach (ScmVertex v in scm.Vertices) { Write(w, Normal(v)); } });
            int tangents = View(ArrayBuffer, w => { foreach (ScmVertex v in scm.Vertices) { Write(w, Tangent(v)); } });
            int uv0 = View(ArrayBuffer, w => { foreach (ScmVertex v in scm.Vertices) { w.Write(v.Uv0.X); w.Write(v.Uv0.Y); } });
            int uv1 = View(ArrayBuffer, w => { foreach (ScmVertex v in scm.Vertices) { w.Write(v.Uv1.X); w.Write(v.Uv1.Y); } });
            int joints = -1, weights = -1, inverseBinds = -1;
            if (skinned)
            {
                joints = View(ArrayBuffer, w => { foreach (ScmVertex v in scm.Vertices) { w.Write(v.Bone0); w.Write(v.Bone1); w.Write(v.Bone2); w.Write(v.Bone3); } });
                weights = View(ArrayBuffer, w => { foreach (ScmVertex _ in scm.Vertices) { Write(w, Vector4.UnitX); } });
                inverseBinds = View(null, w => { foreach (ScmBone bone in scm.Bones) { Write(w, bone.RestPoseInverse); } });
            }
            int indices = View(ElementArrayBuffer, w => { foreach (ushort index in scm.Indices) { w.Write(index); } });
            while (binary.Length % 4 != 0)
            {
                binary.WriteByte(0);
            }

            using MemoryStream json = new MemoryStream();
            using (Utf8JsonWriter j = new Utf8JsonWriter(json))
            {
                j.WriteStartObject();

                j.WriteStartObject("asset");
                j.WriteString("version", "2.0");
                j.WriteString("generator", "FAForever.FileFormats.Mesh");
                if (scm.Information.Count > 0)
                {
                    j.WriteStartObject("extras");
                    j.WriteStartArray("information");
                    foreach (string text in scm.Information)
                    {
                        j.WriteStringValue(text);
                    }
                    j.WriteEndArray();
                    j.WriteEndObject();
                }
                j.WriteEndObject();

                // the bones are nodes 0 to n - 1, the mesh the node after them
                int meshNode = scm.Bones.Count;
                j.WriteNumber("scene", 0);
                j.WriteStartArray("scenes");
                j.WriteStartObject();
                j.WriteStartArray("nodes");
                for (int i = 0; i < scm.Bones.Count; i++)
                {
                    if (scm.Bones[i].Parent < 0)
                    {
                        j.WriteNumberValue(i);
                    }
                }
                j.WriteNumberValue(meshNode);
                j.WriteEndArray();
                j.WriteEndObject();
                j.WriteEndArray();

                j.WriteStartArray("nodes");
                for (int i = 0; i < scm.Bones.Count; i++)
                {
                    ScmBone bone = scm.Bones[i];
                    Quaternion rotation = Quaternion.Normalize(bone.Rotation);
                    j.WriteStartObject();
                    j.WriteString("name", bone.Name);
                    WriteArray(j, "translation", bone.Position.X, bone.Position.Y, bone.Position.Z);
                    WriteArray(j, "rotation", rotation.X, rotation.Y, rotation.Z, rotation.W);
                    int[] children = [.. Enumerable.Range(0, scm.Bones.Count).Where(c => scm.Bones[c].Parent == i)];
                    if (children.Length > 0)
                    {
                        j.WriteStartArray("children");
                        foreach (int child in children)
                        {
                            j.WriteNumberValue(child);
                        }
                        j.WriteEndArray();
                    }
                    j.WriteEndObject();
                }
                j.WriteStartObject();
                j.WriteString("name", scm.Bones.Count > 0 ? scm.Bones[0].Name : "mesh");
                j.WriteNumber("mesh", 0);
                if (skinned)
                {
                    j.WriteNumber("skin", 0);
                }
                j.WriteEndObject();
                j.WriteEndArray();

                j.WriteStartArray("meshes");
                j.WriteStartObject();
                j.WriteStartArray("primitives");
                j.WriteStartObject();
                j.WriteStartObject("attributes");
                j.WriteNumber("POSITION", 0);
                j.WriteNumber("NORMAL", 1);
                j.WriteNumber("TANGENT", 2);
                j.WriteNumber("TEXCOORD_0", 3);
                j.WriteNumber("TEXCOORD_1", 4);
                if (skinned)
                {
                    j.WriteNumber("JOINTS_0", 5);
                    j.WriteNumber("WEIGHTS_0", 6);
                }
                j.WriteEndObject();
                j.WriteNumber("indices", skinned ? 8 : 5);
                j.WriteNumber("mode", Triangles);
                j.WriteEndObject();
                j.WriteEndArray();
                j.WriteEndObject();
                j.WriteEndArray();

                if (skinned)
                {
                    j.WriteStartArray("skins");
                    j.WriteStartObject();
                    j.WriteNumber("inverseBindMatrices", 7);
                    j.WriteStartArray("joints");
                    for (int i = 0; i < scm.Bones.Count; i++)
                    {
                        j.WriteNumberValue(i);
                    }
                    j.WriteEndArray();
                    j.WriteEndObject();
                    j.WriteEndArray();
                }

                // accessors 0 to 4 are the vertices, then the joints, weights and bind matrices, then the indices
                j.WriteStartArray("accessors");
                WriteAccessor(j, positions, Float, n, "VEC3", min, max);
                WriteAccessor(j, normals, Float, n, "VEC3");
                WriteAccessor(j, tangents, Float, n, "VEC4");
                WriteAccessor(j, uv0, Float, n, "VEC2");
                WriteAccessor(j, uv1, Float, n, "VEC2");
                if (skinned)
                {
                    WriteAccessor(j, joints, UnsignedByte, n, "VEC4");
                    WriteAccessor(j, weights, Float, n, "VEC4");
                    WriteAccessor(j, inverseBinds, Float, scm.Bones.Count, "MAT4");
                }
                WriteAccessor(j, indices, UnsignedShort, scm.Indices.Length, "SCALAR");
                j.WriteEndArray();

                j.WriteStartArray("bufferViews");
                foreach ((int offset, int length, int? target) in views)
                {
                    j.WriteStartObject();
                    j.WriteNumber("buffer", 0);
                    j.WriteNumber("byteOffset", offset);
                    j.WriteNumber("byteLength", length);
                    if (target is int t)
                    {
                        j.WriteNumber("target", t);
                    }
                    j.WriteEndObject();
                }
                j.WriteEndArray();

                j.WriteStartArray("buffers");
                j.WriteStartObject();
                j.WriteNumber("byteLength", binary.Length);
                j.WriteEndObject();
                j.WriteEndArray();

                j.WriteEndObject();
            }
            while (json.Length % 4 != 0)
            {
                json.WriteByte((byte)' ');
            }

            using MemoryStream glb = new MemoryStream();
            using (BinaryWriter w = new BinaryWriter(glb, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(Magic);
                w.Write(2u);
                w.Write((uint)(12 + 8 + json.Length + 8 + binary.Length));
                w.Write((uint)json.Length);
                w.Write(JsonChunk);
                w.Write(json.GetBuffer(), 0, (int)json.Length);
                w.Write((uint)binary.Length);
                w.Write(BinaryChunk);
                w.Write(binary.GetBuffer(), 0, (int)binary.Length);
            }
            return glb.ToArray();
        }

        /// <summary>
        /// The normal at length 1; up when the mesh has none (length 0, or not a number).
        /// </summary>
        public static Vector3 Normal(ScmVertex vertex) =>
            Usable(vertex.Normal) ? Vector3.Normalize(vertex.Normal) : Vector3.UnitY;

        /// <summary>
        /// The tangent at length 1 and across the normal, with the binormal's side as its fourth
        /// component (1 or -1): glTF's binormal is <c>cross(normal, tangent) * w</c>.
        /// </summary>
        public static Vector4 Tangent(ScmVertex vertex)
        {
            Vector3 normal = Normal(vertex);
            Vector3 tangent = vertex.Tangent - Vector3.Dot(vertex.Tangent, normal) * normal;
            if (!Usable(tangent))
            {
                tangent = Vector3.Cross(normal, MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX);
            }
            tangent = Vector3.Normalize(tangent);
            float side = Vector3.Dot(Vector3.Cross(normal, tangent), vertex.Binormal) < 0 ? -1 : 1;
            return new Vector4(tangent, side);
        }

        /// <summary>
        /// Whether a direction can be normalised: not of length 0, and no component not a number.
        /// </summary>
        private static bool Usable(Vector3 direction)
        {
            float length = direction.LengthSquared();
            return float.IsFinite(length) && length > 1e-12f;
        }

        private static void Validate(Scm scm)
        {
            if (scm.Indices.Length == 0 || scm.Indices.Length % 3 != 0)
            {
                throw new ArgumentException($"A mesh needs whole triangles, this one has {scm.Indices.Length} indices", nameof(scm));
            }
            if (scm.Indices.Any(index => index >= scm.Vertices.Count))
            {
                throw new ArgumentException("An index is past the last vertex", nameof(scm));
            }
            if (scm.Bones.Count > 0 && ScmWriter.WeightedBoneCount(scm) > scm.Bones.Count)
            {
                throw new ArgumentException("A vertex moves with a bone the mesh does not have", nameof(scm));
            }
            for (int i = 0; i < scm.Bones.Count; i++)
            {
                int parent = scm.Bones[i].Parent;
                for (int steps = 0; parent >= 0; steps++)
                {
                    if (parent >= scm.Bones.Count || steps >= scm.Bones.Count)
                    {
                        throw new ArgumentException($"The parents of bone {i} do not lead to a root", nameof(scm));
                    }
                    parent = scm.Bones[parent].Parent;
                }
            }
        }

        private static void WriteAccessor(Utf8JsonWriter j, int view, int componentType, int count, string type, Vector3? min = null, Vector3? max = null)
        {
            j.WriteStartObject();
            j.WriteNumber("bufferView", view);
            j.WriteNumber("componentType", componentType);
            j.WriteNumber("count", count);
            j.WriteString("type", type);
            if (min is Vector3 low && max is Vector3 high)
            {
                WriteArray(j, "min", low.X, low.Y, low.Z);
                WriteArray(j, "max", high.X, high.Y, high.Z);
            }
            j.WriteEndObject();
        }

        private static void WriteArray(Utf8JsonWriter j, string name, params float[] values)
        {
            j.WriteStartArray(name);
            foreach (float value in values)
            {
                j.WriteNumberValue(value);
            }
            j.WriteEndArray();
        }

        private static void Write(BinaryWriter w, Vector3 v)
        {
            w.Write(v.X);
            w.Write(v.Y);
            w.Write(v.Z);
        }

        private static void Write(BinaryWriter w, Vector4 v)
        {
            w.Write(v.X);
            w.Write(v.Y);
            w.Write(v.Z);
            w.Write(v.W);
        }

        private static void Write(BinaryWriter w, Matrix4x4 m)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    w.Write(m[row, column]);
                }
            }
        }
    }
}
