namespace FAForever.MapEditor.Cli.Guide
{
    public enum GuideFindingKind
    {
        /// <summary>The step's target is not found: the check fails.</summary>
        Missing,
        /// <summary>The target lies elsewhere than in guide.json by more than the tolerance.</summary>
        Moved,
        /// <summary>The editor's tour has a step guide.json lacks.</summary>
        Added,
        /// <summary>guide.json has a step the editor's tour lacks.</summary>
        Removed,
        /// <summary>The editor has a tour guide.json lacks.</summary>
        AddedTour,
        /// <summary>guide.json has a tour the editor lacks.</summary>
        RemovedTour,
    }

    public sealed record GuideFinding(GuideFindingKind Kind, string Tour, string? Step, string Message)
    {
        /// <summary>Whether the check fails for it.</summary>
        public bool IsError => Kind is GuideFindingKind.Missing;
    }

    /// <summary>
    /// What the editor's tours do now, per step: the answer of <c>state.tour.target</c>.
    /// </summary>
    public sealed record LiveStep(string Id, bool Found, int[]? Bounds, string? Why);

    /// <summary>
    /// <c>faf-map guide check</c>'s comparison of the editor's tours with <c>guide.json</c>, apart from
    /// the editor.
    /// </summary>
    public static class GuideCheck
    {
        /// <summary>
        /// A target that moved or changed size by more than this many pixels is reported.
        /// </summary>
        public const int Tolerance = 8;

        public static List<GuideFinding> Compare(GuideManifest manifest, IReadOnlyDictionary<string, List<LiveStep>> live)
        {
            List<GuideFinding> findings = [];
            foreach (GuideTour tour in manifest.Tours)
            {
                if (!live.TryGetValue(tour.Id, out List<LiveStep>? steps))
                {
                    findings.Add(new GuideFinding(GuideFindingKind.RemovedTour, tour.Id, null, $"The tour '{tour.Id}' is in guide.json and not in the editor"));
                    continue;
                }
                foreach (GuideStep step in tour.Steps)
                {
                    LiveStep? now = steps.FirstOrDefault(candidate => candidate.Id == step.Id);
                    if (now is null)
                    {
                        findings.Add(new GuideFinding(GuideFindingKind.Removed, tour.Id, step.Id, $"{tour.Id}/{step.Id}: the step is in guide.json and not in the tour"));
                        continue;
                    }
                    Compare(tour.Id, step, now, findings);
                }
                foreach (LiveStep now in steps.Where(now => tour.Steps.All(step => step.Id != now.Id)))
                {
                    findings.Add(new GuideFinding(GuideFindingKind.Added, tour.Id, now.Id, $"{tour.Id}/{now.Id}: the step is in the tour and not in guide.json"));
                    if (!now.Found)
                    {
                        findings.Add(new GuideFinding(GuideFindingKind.Missing, tour.Id, now.Id, $"{tour.Id}/{now.Id}: target not found ({now.Why ?? "no reason given"})"));
                    }
                }
            }
            foreach (string id in live.Keys.Where(id => manifest.Tours.All(tour => tour.Id != id)))
            {
                findings.Add(new GuideFinding(GuideFindingKind.AddedTour, id, null, $"The tour '{id}' is in the editor and not in guide.json"));
            }
            return findings;
        }

        private static void Compare(string tour, GuideStep step, LiveStep now, List<GuideFinding> findings)
        {
            string name = $"{tour}/{step.Id}";
            if (!now.Found)
            {
                findings.Add(new GuideFinding(GuideFindingKind.Missing, tour, step.Id, $"{name}: target not found ({now.Why ?? "no reason given"})"));
                return;
            }
            int[]? before = step.Target?.Bounds;
            if (before is not null && now.Bounds is not null && Moved(before, now.Bounds))
            {
                findings.Add(new GuideFinding(GuideFindingKind.Moved, tour, step.Id,
                    $"{name}: the target moved from [{string.Join(", ", before)}] to [{string.Join(", ", now.Bounds)}]"));
            }
        }

        /// <summary>
        /// Whether any of the four numbers differs by more than <see cref="Tolerance"/>.
        /// </summary>
        public static bool Moved(int[] before, int[] now)
        {
            for (int index = 0; index < 4; index++)
            {
                if (Math.Abs(before[index] - now[index]) > Tolerance)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
