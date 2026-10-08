using System.Collections.Immutable;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Edits of a document that the user interface and plugins share, each a function from one
    /// version to the next for <see cref="EditSession.Edit"/> or <see cref="EditTransaction.Apply"/>.
    /// They name what they change (a prop by its id, a marker by its name), never a position in a list.
    /// </summary>
    public static class DocumentEdits
    {
        /// <summary>
        /// Adds a prop under a new id, whatever id it had.
        /// </summary>
        public static MapDocument AddProp(this MapDocument document, Prop prop) =>
            document with { Props = document.Props.Add(prop with { Id = document.NextPropId() }) };

        /// <exception cref="KeyNotFoundException">No prop has the id.</exception>
        public static MapDocument UpdateProp(this MapDocument document, int id, Func<Prop, Prop> update) =>
            document with { Props = document.Props.Update(prop => prop.Id == id, update, $"prop {id}") };

        /// <exception cref="KeyNotFoundException">No prop has the id.</exception>
        public static MapDocument RemoveProp(this MapDocument document, int id) =>
            document with { Props = document.Props.RemoveAt(document.Props.Find(prop => prop.Id == id, $"prop {id}")) };

        /// <exception cref="KeyNotFoundException">No deposit has the name.</exception>
        public static MapDocument UpdateResource(this MapDocument document, string name, Func<ResourceMarker, ResourceMarker> update) =>
            document with
            {
                Markers = document.Markers with { Resources = document.Markers.Resources.Update(resource => resource.Name == name, update, $"deposit {name}") },
            };

        /// <summary>
        /// The array with its one item that <paramref name="match"/> finds replaced by <paramref name="update"/>'s result.
        /// </summary>
        /// <exception cref="KeyNotFoundException">No item matches.</exception>
        public static ImmutableArray<T> Update<T>(this ImmutableArray<T> items, Func<T, bool> match, Func<T, T> update, string what)
        {
            int index = items.Find(match, what);
            return items.SetItem(index, update(items[index]));
        }

        private static int Find<T>(this ImmutableArray<T> items, Func<T, bool> match, string what)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (match(items[i]))
                {
                    return i;
                }
            }
            throw new KeyNotFoundException($"The map has no {what}");
        }
    }
}
