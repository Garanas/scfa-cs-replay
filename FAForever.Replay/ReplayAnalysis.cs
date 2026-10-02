namespace FAForever.Replay
{
    /// <summary>
    /// Pure functions that derive statistics from a parsed replay, such as game duration,
    /// actions per minute and build orders. Complements <see cref="ReplaySemantics"/>.
    /// </summary>
    public static class ReplayAnalysis
    {
        /// <summary>
        /// The simulation runs at 10 ticks per second.
        /// </summary>
        public const int TicksPerSecond = 10;

        /// <summary>
        /// An entry of a build order: at <paramref name="Timestamp"/> the player ordered
        /// <paramref name="UnitCount"/> of their units/factories to construct the blueprint.
        /// </summary>
        public record BuildOrderEntry(TimeSpan Timestamp, CommandType Type, string BlueprintId, int UnitCount);

        /// <summary>
        /// A position on the map in world units. The top-left corner of the map is (0, 0);
        /// X runs to the right and Z runs down. A 20x20 km map is 1024 by 1024 world units.
        /// </summary>
        public record MapPosition(float X, float Z);

        /// <summary>
        /// Converts the tick of an input to in-game time.
        /// </summary>
        public static TimeSpan GetTimestamp(ReplayInput input)
        {
            return TimeSpan.FromSeconds(input.Tick / (double)TicksPerSecond);
        }

        /// <summary>
        /// Computes the in-game duration of the replay, based on the tick of the last input.
        /// </summary>
        public static TimeSpan GetDuration(Replay replay)
        {
            int lastTick = 0;
            foreach (ReplayInput input in replay.Body.UserInput)
            {
                if (input.Tick > lastTick)
                {
                    lastTick = input.Tick;
                }
            }

            return TimeSpan.FromSeconds(lastTick / (double)TicksPerSecond);
        }

        /// <summary>
        /// Counts the actions of each input source. The key of the dictionary is the source id,
        /// which maps to <see cref="ReplayHeader.Clients"/>.
        /// </summary>
        public static Dictionary<int, int> CountPlayerActions(Replay replay)
        {
            Dictionary<int, int> actions = new();
            foreach (ReplayInput input in replay.Body.UserInput)
            {
                if (!IsPlayerAction(input))
                {
                    continue;
                }

                actions[input.SourceId] = actions.GetValueOrDefault(input.SourceId) + 1;
            }

            return actions;
        }

        /// <summary>
        /// Buckets the actions of each input source over time. The key of the dictionary is the
        /// source id. Each value holds one count per bucket of <paramref name="bucketSeconds"/>,
        /// covering the full duration of the replay. Divide by the bucket length to get actions
        /// per minute.
        /// </summary>
        public static Dictionary<int, int[]> GetActionBuckets(Replay replay, int bucketSeconds = 60)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(bucketSeconds, 1);

            int ticksPerBucket = bucketSeconds * TicksPerSecond;
            int bucketCount = Math.Max(1, (int)(GetDuration(replay).TotalSeconds / bucketSeconds) + 1);

            Dictionary<int, int[]> buckets = new();
            foreach (ReplayInput input in replay.Body.UserInput)
            {
                if (!IsPlayerAction(input))
                {
                    continue;
                }

                if (!buckets.TryGetValue(input.SourceId, out int[]? series))
                {
                    series = new int[bucketCount];
                    buckets[input.SourceId] = series;
                }

                int bucket = Math.Min(input.Tick / ticksPerBucket, bucketCount - 1);
                series[bucket]++;
            }

            return buckets;
        }

        /// <summary>
        /// Extracts the construction orders of a single input source, in chronological order.
        /// </summary>
        public static List<BuildOrderEntry> GetBuildOrder(Replay replay, int sourceId)
        {
            List<BuildOrderEntry> buildOrder = new();
            foreach (ReplayInput input in replay.Body.UserInput)
            {
                if (input.SourceId != sourceId)
                {
                    continue;
                }

                (CommandUnits Units, CommandData Data)? command = input switch
                {
                    ReplayInput.IssueCommand issueCommand => (issueCommand.Units, issueCommand.Data),
                    ReplayInput.IssueFactoryCommand factoryCommand => (factoryCommand.Factories, factoryCommand.Data),
                    _ => null,
                };

                if (command is not { Data: { } data } || !IsConstructionCommand(data.Type) || string.IsNullOrEmpty(data.BlueprintId))
                {
                    continue;
                }

                buildOrder.Add(new BuildOrderEntry(GetTimestamp(input), data.Type, data.BlueprintId, command.Value.Units.UnitCount));
            }

            return buildOrder;
        }

        /// <summary>
        /// Estimates where each input source started, by averaging the map positions of their
        /// first construction orders. Early build orders cluster around the spawn, so the
        /// centroid lands on the base within a few map units. The key of the dictionary is the
        /// source id; sources without early construction orders (e.g. observers) are absent.
        /// </summary>
        /// <param name="replay">The replay to analyse.</param>
        /// <param name="maxTick">Only orders up to this tick are considered (default: the first two minutes).</param>
        /// <param name="samplesPerSource">The number of construction orders to average per source.</param>
        public static Dictionary<int, MapPosition> GetEstimatedSpawnPositions(Replay replay, int maxTick = 1200, int samplesPerSource = 5)
        {
            Dictionary<int, (float SumX, float SumZ, int Count)> sums = new();
            foreach (ReplayInput input in replay.Body.UserInput)
            {
                if (input.Tick > maxTick)
                {
                    // Inputs are ordered by tick.
                    break;
                }

                CommandData? data = input switch
                {
                    ReplayInput.IssueCommand command => command.Data,
                    ReplayInput.IssueFactoryCommand factoryCommand => factoryCommand.Data,
                    _ => null,
                };

                if (data is not { Target: CommandTarget.Position position } || !IsConstructionCommand(data.Type))
                {
                    continue;
                }

                (float sumX, float sumZ, int count) = sums.GetValueOrDefault(input.SourceId);
                if (count < samplesPerSource)
                {
                    sums[input.SourceId] = (sumX + position.X, sumZ + position.Z, count + 1);
                }
            }

            Dictionary<int, MapPosition> spawns = new(sums.Count);
            foreach ((int sourceId, (float sumX, float sumZ, int count)) in sums)
            {
                spawns[sourceId] = new MapPosition(sumX / count, sumZ / count);
            }

            return spawns;
        }

        /// <summary>
        /// Whether the input represents a deliberate action of a player, as opposed to
        /// bookkeeping that the game generates (checksums, info pairs, sim callbacks).
        /// </summary>
        private static bool IsPlayerAction(ReplayInput input)
        {
            return input switch
            {
                ReplayInput.IssueCommand => true,
                ReplayInput.IssueFactoryCommand => true,
                ReplayInput.IncreaseCommandCount => true,
                ReplayInput.DecreaseCommandCount => true,
                ReplayInput.UpdateCommandTarget => true,
                ReplayInput.UpdateCommandType => true,
                ReplayInput.UpdateCommandLuaParameters => true,
                ReplayInput.RemoveCommandFromQueue => true,
                ReplayInput.RequestPause => true,
                ReplayInput.RequestResume => true,
                _ => false,
            };
        }

        private static bool IsConstructionCommand(CommandType type)
        {
            return type switch
            {
                CommandType.IssueBuildFactory => true,
                CommandType.IssueBuildMobile => true,
                CommandType.IssueUpgrade => true,
                CommandType.IssueSiloBuildTactical => true,
                CommandType.IssueSiloBuildNuke => true,
                _ => false,
            };
        }
    }
}
