using System.Globalization;
using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Checks
{
    /// <summary>
    /// Whether a map is as symmetric as the symmetry chosen for it (user stories F-12, F-14): the
    /// check <c>not-symmetric</c>.
    /// </summary>
    public static class SymmetryChecks
    {
        public const string Id = "not-symmetric";

        /// <summary>
        /// The check, with the symmetry it checks against: a function of the document giving its
        /// parts (<see cref="MapSymmetry.Transforms"/>), empty while there is none.
        /// </summary>
        public static IProblemCheck Check(Func<MapDocument, IReadOnlyList<MapSymmetryTransform>> transforms) =>
            new ProblemCheck(Id, "Things without a mirror partner", context => NotSymmetric(context.Document, transforms(context.Document)));

        /// <summary>
        /// The things without a mirror partner (<see cref="SymmetryEdits.PartnerOf"/>) in a part of the
        /// map: a warning for each spawn and deposit, naming where its counterpart is missing; a
        /// warning per blueprint for units; a note per blueprint, texture or layer for props, decals
        /// and path nodes, which maps rarely mirror exactly. A thing whose image lies on itself or
        /// outside the map needs none.
        /// </summary>
        public static IEnumerable<Problem> NotSymmetric(MapDocument document, IReadOnlyList<MapSymmetryTransform> transforms, float tolerance = SymmetryEdits.DefaultTolerance)
        {
            if (transforms.Count < 2)
            {
                yield break;
            }
            List<(ObjectRef Thing, string Group, string Kind)> grouped = [];
            foreach (ObjectRef thing in document.All(ObjectKinds.All & ~ObjectKinds.Areas))
            {
                object? found = document.Find(thing);
                if (found is null || SymmetryEdits.LikenessOf(found) is null || document.PlaceOf(thing) is not Vector3 place)
                {
                    continue;
                }
                Vector2 at = new Vector2(place.X, place.Z);
                MapSymmetryTransform? missing = null;
                foreach (MapSymmetryTransform transform in transforms)
                {
                    Vector2 image = transform.Apply(at);
                    if (transform == MapSymmetryTransform.Identity
                        || Vector2.Distance(image, at) <= tolerance
                        || image.X < 0 || image.Y < 0 || image.X > document.Width || image.Y > document.Height
                        || document.PartnerOf(thing, transform, tolerance) is not null)
                    {
                        continue;
                    }
                    missing = transform;
                    break;
                }
                if (missing is not MapSymmetryTransform lacking)
                {
                    continue;
                }
                switch (found)
                {
                    case Spawn or ResourceMarker:
                        Marker marker = (Marker)found;
                        Vector2 where = lacking.Apply(at);
                        yield return new Problem(Id, ProblemSeverity.Warning, string.Create(CultureInfo.InvariantCulture,
                            $"{marker.Name} has no mirror partner: nothing of its kind at {where.X:0.#}, {where.Y:0.#}"), thing);
                        break;
                    case PlacedUnit unit:
                        grouped.Add((thing, unit.Unit.Blueprint, "unit"));
                        break;
                    case Prop prop:
                        grouped.Add((thing, prop.Blueprint, "prop"));
                        break;
                    case Decal decal:
                        grouped.Add((thing, decal.Textures.IsEmpty ? "" : decal.Textures[0], "decal"));
                        break;
                    case PathNode node:
                        grouped.Add((thing, node.Layer.ToString(), "path node"));
                        break;
                    case AiMarker ai:
                        grouped.Add((thing, ai.Type, "AI marker"));
                        break;
                }
            }
            foreach (IGrouping<(string Kind, string Group), (ObjectRef Thing, string Group, string Kind)> uses in grouped.GroupBy(item => (item.Kind, item.Group)))
            {
                int count = uses.Count();
                (string kind, string group) = uses.Key;
                string them = kind == "path node"
                    ? count == 1 ? $"A {group} path node" : $"{count} {group} path nodes"
                    : count == 1 ? $"A {kind} of {group}" : $"{count} {kind}s of {group}";
                ProblemSeverity severity = kind is "unit" or "AI marker" ? ProblemSeverity.Warning : ProblemSeverity.Info;
                yield return new Problem(Id, severity, $"{them} without a mirror partner", uses.First().Thing);
            }
        }
    }
}
