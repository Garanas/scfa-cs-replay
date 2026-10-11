using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Checks
{
    /// <summary>
    /// How bad a problem is.
    /// </summary>
    public enum ProblemSeverity
    {
        /// <summary>Worth knowing; nothing is wrong.</summary>
        Info,

        /// <summary>Doubtful: the map works, but likely not as meant.</summary>
        Warning,

        /// <summary>Wrong: the game will miss something or fail.</summary>
        Error,
    }

    /// <summary>
    /// Something wrong or doubtful about a map (user story F-14), and where it is.
    /// </summary>
    /// <param name="Check">The id of the check that found it, e.g. <c>spawn-without-mass</c>.</param>
    /// <param name="Thing">The thing it is about, which the editor selects when the person goes there.</param>
    /// <param name="Place">The place on the map (x, z in ogrids) to go to; else the thing's place.</param>
    public sealed record Problem(string Check, ProblemSeverity Severity, string Message, ObjectRef? Thing = null, Vector2? Place = null);

    /// <summary>
    /// What a check reads: the document, and what the editor knows besides it. Each part but the
    /// document may be missing (no workspace yet); a check that needs it then finds nothing.
    /// </summary>
    /// <param name="Catalog">The workspace's assets, read once (no disk access per check).</param>
    /// <param name="HasAsset">Whether the workspace has a converted file of a game path; the editor keeps the answers.</param>
    /// <param name="Results">The generated results, such as the last walkability.</param>
    /// <param name="Walkability">What changed in the land's walkability when it was generated last.</param>
    /// <param name="PresetFingerprint">The fingerprint of the workspace's units preset of a name
    /// (<see cref="UnitPresets.Fingerprint(UnitPresetSettings)"/>), null for a name the workspace has
    /// no preset of; the editor keeps the answers until the presets change. Absent without a workspace.</param>
    public sealed record ProblemContext(
        MapDocument Document,
        AssetCatalog? Catalog = null,
        Func<string, bool>? HasAsset = null,
        GeneratedResults? Results = null,
        WalkabilityChanges? Walkability = null,
        Func<string, string?>? PresetFingerprint = null);

    /// <summary>
    /// A check of a map: the editor's own (<see cref="MapChecks"/>) and, later, a plugin's (user
    /// story F-15). The editor runs every check after each step of undo and before a save or export,
    /// so a check must be cheap: no disk, nothing that grows faster than the map's things.
    /// </summary>
    public interface IProblemCheck
    {
        /// <summary>
        /// E.g. <c>missing-texture</c>.
        /// </summary>
        string Id { get; }

        string Title { get; }

        IEnumerable<Problem> Check(ProblemContext context);
    }

    /// <summary>
    /// A check made of a function.
    /// </summary>
    public sealed record ProblemCheck(string Id, string Title, Func<ProblemContext, IEnumerable<Problem>> Run) : IProblemCheck
    {
        public IEnumerable<Problem> Check(ProblemContext context) => Run(context);
    }

    /// <summary>
    /// A check that failed itself, and why.
    /// </summary>
    public sealed record FailedCheck(IProblemCheck Check, Exception Exception);

    /// <summary>
    /// The checks of the editor; a feature or plugin registers its own.
    /// </summary>
    public sealed class ProblemChecks
    {
        private readonly List<IProblemCheck> _checks = [];

        /// <summary>
        /// With the editor's own checks (<see cref="MapChecks.All"/>) unless <paramref name="withMapChecks"/> is false.
        /// </summary>
        public ProblemChecks(bool withMapChecks = true)
        {
            if (withMapChecks)
            {
                _checks.AddRange(MapChecks.All);
            }
        }

        public IReadOnlyList<IProblemCheck> All => _checks;

        /// <exception cref="ArgumentException">A check with its id is registered already.</exception>
        public void Register(IProblemCheck check)
        {
            if (_checks.Any(other => other.Id == check.Id))
            {
                throw new ArgumentException($"A check '{check.Id}' is registered already", nameof(check));
            }
            _checks.Add(check);
        }

        /// <returns>Whether there was such a check.</returns>
        public bool Unregister(string id) => _checks.RemoveAll(check => check.Id == id) > 0;

        /// <summary>
        /// Runs every check: the problems, the worst first and then in the order the checks found
        /// them. A check that throws finds nothing and is named in <paramref name="failed"/>.
        /// </summary>
        public IReadOnlyList<Problem> Run(ProblemContext context, List<FailedCheck>? failed = null)
        {
            List<Problem> problems = [];
            foreach (IProblemCheck check in _checks)
            {
                try
                {
                    problems.AddRange(check.Check(context));
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    failed?.Add(new FailedCheck(check, exception));
                }
            }
            return [.. problems.Select((problem, index) => (problem, index))
                .OrderByDescending(item => item.problem.Severity)
                .ThenBy(item => item.index)
                .Select(item => item.problem)];
        }
    }
}
