namespace FAForever.Replay
{

    /// <summary>
    /// Represents the invariant of the loop of processing the input of a replay. This allows the process to exit periodically, make room for other code to run, and then continue where it left off. This is useful for single threaded environments such as WebAssembly that is used by Blazor.
    /// </summary>
    /// <param name="input"></param>
    /// <param name="tick"></param>
    /// <param name="source"></param>
    /// <param name="inSync"></param>
    /// <param name="hashTick"></param>
    /// <param name="hashValue"></param>
    /// <param name="endOfStream"></param>
    /// <param name="startingPointOfStream"></param>
    /// <param name="percentageProcessed"></param>
    public record ReplayBodyInvariant(List<ReplayInput> input, int tick, int source, bool inSync, int hashTick, long hashValue, bool endOfStream, long startingPointOfStream, int percentageProcessed)
    {
        public List<ReplayInput> Input { get; init; } = input;

        public int Tick { get; init; } = tick;

        public int Source { get; init; } = source;

        public bool InSync { get; init; } = inSync;

        public int HashTick { get; init; } = hashTick;

        public long HashValue { get; init; } = hashValue;

        public bool EndOfStream { get; init; } = endOfStream;

        public long StartingPointOfStream { get; init; } = startingPointOfStream;

        public int PercentageProcessed { get; init; } = percentageProcessed;
    }
}
