using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Changes of any layer stack (<see cref="LayerStack"/>), whatever its target: add, remove,
    /// rename and order layers, and set how each combines, how strongly, where (a clip mask),
    /// whether it shows and whether it is locked. Each takes a stack and gives the changed stack; a
    /// target's own edits (<see cref="HeightLayerEdits"/> for the heightmap) put it in its place in
    /// the document and add the target's rules.
    /// </summary>
    /// <remarks>
    /// A stack's base (<see cref="Layer.IsBase"/>) is always its lowest layer: it cannot be moved,
    /// removed, clipped or weakened, and it replaces. A name is a file name too: letters, digits,
    /// spaces, dashes and underscores, unique in the stack without regard to case. The messages of
    /// refusals name a layer as <c>what</c> says (<c>height layer</c>, <c>mask layer</c>).
    /// </remarks>
    public static class LayerStackEdits
    {
        /// <summary>
        /// The longest name a layer may have.
        /// </summary>
        public const int MaxNameLength = 64;

        /// <summary>
        /// Why a name cannot be a new layer's name in a stack, or null when it can.
        /// </summary>
        /// <param name="except">A layer the name may equal (one being renamed).</param>
        public static string? NameProblem(LayerStack stack, string name, string? except = null, string what = "layer")
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return $"A {what} needs a name";
            }
            if (name.Length > MaxNameLength)
            {
                return $"A {what}'s name has at most {MaxNameLength} characters";
            }
            if (name != name.Trim())
            {
                return $"A {what}'s name does not start or end with a space";
            }
            if (!name.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_'))
            {
                return $"'{name}' is no name for a {what}: use letters, digits, spaces, dashes and underscores";
            }
            if (stack.Layers.FirstOrDefault(layer => layer.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && layer.Name != except) is Layer taken)
            {
                return $"The map has a {what} '{taken.Name}' already";
            }
            return null;
        }

        /// <summary>
        /// A name no layer of the stack has: <paramref name="stem"/>, else with the first free number after it.
        /// </summary>
        public static string FreeName(LayerStack stack, string stem)
        {
            if (NameProblem(stack, stem) is null)
            {
                return stem;
            }
            for (int number = 2; ; number++)
            {
                string name = $"{stem} {number}";
                if (NameProblem(stack, name) is null)
                {
                    return name;
                }
            }
        }

        /// <summary>
        /// The place of a layer in the stack, by name (exactly, else without regard to case).
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer.</exception>
        public static int IndexOf(LayerStack stack, string name, string what = "layer")
        {
            int index = stack.IndexOf(name);
            if (index < 0)
            {
                throw new ArgumentException(stack.IsEmpty
                    ? $"The map has no {what}s"
                    : $"The map has no {what} '{name}': {string.Join(", ", stack.Layers.Select(each => each.Name))}", nameof(name));
            }
            return index;
        }

        /// <summary>
        /// The stack with a layer added above the layer <paramref name="above"/>, or on top.
        /// </summary>
        /// <exception cref="ArgumentException">The name is taken or no name, the source does not fit
        /// the stack, or the layer is a second base or a base not at the bottom.</exception>
        public static LayerStack Add(LayerStack stack, Layer layer, string? above = null, string what = "layer")
        {
            if (NameProblem(stack, layer.Name, what: what) is string problem)
            {
                throw new ArgumentException(problem, nameof(layer));
            }
            if (!LayerSources.Fits(layer.Source, stack.Target.IsHeights))
            {
                throw new ArgumentException($"A {LayerSources.NameOf(layer.Source)} layer does not fit the stack of {stack.Target}", nameof(layer));
            }
            int index = above is null ? stack.Layers.Length : IndexOf(stack, above, what) + 1;
            if (layer.IsBase && (index != 0 || stack.Layers.Any(each => each.IsBase)))
            {
                throw new ArgumentException($"A stack has one base {what}, the lowest", nameof(layer));
            }
            return stack with { Layers = stack.Layers.Insert(index, layer) };
        }

        /// <summary>
        /// The stack without a layer. The base stays.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, or it is the base.</exception>
        public static LayerStack Remove(LayerStack stack, string name, string what = "layer")
        {
            int index = IndexOf(stack, name, what);
            if (stack.Layers[index].IsBase)
            {
                throw new ArgumentException($"The base {what} stays", nameof(name));
            }
            return stack with { Layers = stack.Layers.RemoveAt(index) };
        }

        /// <summary>
        /// The stack with a layer at another place: 0 is the bottom, 1 just above a base.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, it is the base, or the place is
        /// outside the stack or below its base.</exception>
        public static LayerStack Move(LayerStack stack, string name, int index, string what = "layer")
        {
            int from = IndexOf(stack, name, what);
            if (stack.Layers[from].IsBase)
            {
                throw new ArgumentException($"The base {what} stays the lowest", nameof(name));
            }
            int lowest = stack.Layers.Any(layer => layer.IsBase) ? 1 : 0;
            if (index < lowest || index >= stack.Layers.Length)
            {
                throw new ArgumentException(lowest == 1
                    ? $"A layer goes to place 1 (just above the base) to {stack.Layers.Length - 1}, not {index}"
                    : $"A layer goes to place 0 (the bottom) to {stack.Layers.Length - 1}, not {index}", nameof(index));
            }
            Layer layer = stack.Layers[from];
            return stack with { Layers = stack.Layers.RemoveAt(from).Insert(index, layer) };
        }

        /// <summary>
        /// The stack with a layer under another name.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, or the new name cannot be a layer's.</exception>
        public static LayerStack Rename(LayerStack stack, string name, string newName, string what = "layer")
        {
            int index = IndexOf(stack, name, what);
            Layer layer = stack.Layers[index];
            if (NameProblem(stack, newName, except: layer.Name, what: what) is string problem)
            {
                throw new ArgumentException(problem, nameof(newName));
            }
            return stack.With(index, layer with { Name = newName });
        }

        /// <summary>
        /// The stack with a layer combining another way (its values as they are).
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, or it is the base.</exception>
        public static LayerStack SetCombine(LayerStack stack, string name, LayerCombine combine, string what = "layer")
        {
            int index = IndexOf(stack, name, what);
            Layer layer = stack.Layers[index];
            if (layer.IsBase && combine != LayerCombine.Replace)
            {
                throw new ArgumentException($"The base {what} replaces: it is the lowest", nameof(name));
            }
            return stack.With(index, layer with { Combine = combine });
        }

        /// <summary>
        /// The stack with a layer working more or less strongly, 0 to 1.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, or it is the base.</exception>
        public static LayerStack SetStrength(LayerStack stack, string name, float strength, string what = "layer")
        {
            int index = IndexOf(stack, name, what);
            Layer layer = stack.Layers[index];
            if (layer.IsBase)
            {
                throw new ArgumentException($"The base {what} works at full strength", nameof(name));
            }
            return stack.With(index, layer with { Strength = Math.Clamp(strength, 0, 1) });
        }

        /// <summary>
        /// The stack with a layer working only where a mask is (a key of
        /// <see cref="LayerStacks.MaskKey"/> or <see cref="LayerStacks.StratumKey"/>), or everywhere
        /// (null). Whether the document has the mask is the caller's to check.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, or it is the base.</exception>
        public static LayerStack SetClip(LayerStack stack, string name, string? clip, string what = "layer")
        {
            int index = IndexOf(stack, name, what);
            Layer layer = stack.Layers[index];
            if (layer.IsBase && clip is not null)
            {
                throw new ArgumentException($"The base {what} works everywhere", nameof(name));
            }
            return stack.With(index, layer with { Clip = clip });
        }

        public static LayerStack SetVisible(LayerStack stack, string name, bool visible, string what = "layer")
        {
            int index = IndexOf(stack, name, what);
            return stack.With(index, stack.Layers[index] with { Visible = visible });
        }

        public static LayerStack SetLocked(LayerStack stack, string name, bool locked, string what = "layer")
        {
            int index = IndexOf(stack, name, what);
            return stack.With(index, stack.Layers[index] with { Locked = locked });
        }
    }
}
