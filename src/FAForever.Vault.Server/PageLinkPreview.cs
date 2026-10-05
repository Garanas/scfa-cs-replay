using System.Globalization;
using System.Text.Json;
using FAForever.FileFormats.Blueprints;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;

/// <summary>
/// Link previews for the pages of the app other than replays: the unit pages get a card about the
/// units in their address (<c>units/database?unit=uel0401</c>, <c>units/history?units=uel0401</c>),
/// with the unit's icon, and the About and Units pages a fixed card each.
/// <para>
/// Everything comes from the app's own files: the unit data (<c>data/units/</c>, read with
/// FAForever.FileFormats.Blueprints) and the unit icons. Nothing is fetched, so there is no rate limit;
/// cards are cached in memory. The query parameters mirror the Viewer's (UnitLinks, the guide's table).
/// </para>
/// </summary>
public sealed class PageLinkPreview(IMemoryCache cache, IWebHostEnvironment environment, ILogger<PageLinkPreview> logger)
{
    private const string DataFolder = "data/units/";

    /// <summary>The comparison and the history show at most this many units (UnitLinks.MaxCompared in the Viewer).</summary>
    private const int MaxUnits = 6;

    // The data only changes with a deploy (or a regeneration in development).
    private static readonly TimeSpan DataLifetime = TimeSpan.FromMinutes(10);

    private static readonly IReadOnlyDictionary<string, LinkPreviewCard> FixedCards = new Dictionary<string, LinkPreviewCard>(StringComparer.OrdinalIgnoreCase)
    {
        ["/about"] = new("About Vault of FAF",
            "How Vault of FAF reads your games: explainers for players, not programmers."),
        ["/about/replay-format"] = new("Inside a replay file",
            "Every game on FAF can be watched again. This page opens a replay and goes from loose bytes to build orders and APM, one step at a time."),
        ["/about/blueprints"] = new("Inside a blueprint",
            "Every unit in the game is described in a text file. This page opens the blueprint of the Fatboy and shows how a whole game is built from files like it."),
        ["/units"] = new("Every unit, in every version",
            "FAF changes the game with every release. Look up what a unit is today, and see how it got there."),
        ["/units/database"] = new("The unit database",
            "All units of one release of FAF. Filter by faction, tech level or role, sort by cost or range, and compare units side by side."),
        ["/units/history"] = new("The unit history",
            "Follow a unit through every release of FAF, and see which values went up or down, and when."),
    };

    /// <summary>The fixed icon of a page without units of its own: the Fatboy of the blueprint page.</summary>
    private static readonly IReadOnlyDictionary<string, (string BlueprintId, string Name)> FixedIcons = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
    {
        ["/about/blueprints"] = ("uel0401", "Fatboy"),
    };

    /// <summary>The paths that get a card of their own; Program.cs maps each to <see cref="RenderAsync"/>.</summary>
    public static IEnumerable<string> Paths => FixedCards.Keys;

    /// <summary>The app's entry page with the link preview of this address, or null without an entry page.</summary>
    public async Task<string?> RenderAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (await LinkPreviewHtml.ReadIndexAsync(environment, cancellationToken) is not string html)
        {
            return null;
        }

        HttpRequest request = context.Request;
        string path = request.Path.Value?.TrimEnd('/') ?? "";
        LinkPreviewCard? card = null;
        try
        {
            card = await UnitCardAsync(path, request.Query, ImageBase(request), cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            logger.LogWarning(exception, "No unit link preview for {Path}{Query}", path, request.QueryString);
        }

        card ??= FixedCards.GetValueOrDefault(path) is { } fixedCard
            ? FixedIcons.TryGetValue(path, out (string BlueprintId, string Name) icon) && IconUrl(ImageBase(request), icon.BlueprintId) is { } image
                ? fixedCard with { Image = image, ImageAlt = $"Icon of the {icon.Name}" }
                : fixedCard
            : null;

        return card is null ? html : LinkPreviewHtml.Render(html, card, request.GetEncodedUrl(), path.TrimStart('/'));
    }

    /// <summary>The card of a unit page about particular units, or null for the page's fixed card.</summary>
    private async Task<LinkPreviewCard?> UnitCardAsync(string path, IQueryCollection query, string imageBase, CancellationToken cancellationToken)
    {
        bool database = path.Equals("/units/database", StringComparison.OrdinalIgnoreCase)
            // The old address of the database (/units?unit=...), which the app sends on.
            || path.Equals("/units", StringComparison.OrdinalIgnoreCase) && query.ContainsKey("unit");
        bool history = path.Equals("/units/history", StringComparison.OrdinalIgnoreCase);
        if (!database && !history)
        {
            return null;
        }

        string[] ids = Ids(history ? query["units"] : query["compare"]);
        if (database && ids.Length < 2)
        {
            ids = Ids(query["unit"]);
        }
        if (ids.Length == 0)
        {
            return null;
        }

        string key = $"page-card:{path.ToLowerInvariant()}:{string.Join(',', ids)}:{(database ? query["version"].ToString() : "")}";
        if (cache.TryGetValue(key, out LinkPreviewCard? cached))
        {
            return cached;
        }

        UnitDataIndex index = await LoadIndexAsync(cancellationToken);
        LinkPreviewCard? card = database
            ? await DatabaseCardAsync(index, ids, query["version"], imageBase, cancellationToken)
            : await HistoryCardAsync(index, ids, imageBase, cancellationToken);
        cache.Set(key, card, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = DataLifetime, Size = 1 });
        return card;
    }

    /// <summary>One unit of a release ("Fatboy"), or several side by side ("Fatboy and Titan compared").</summary>
    private async Task<LinkPreviewCard?> DatabaseCardAsync(UnitDataIndex index, string[] ids, string? versionText, string imageBase, CancellationToken cancellationToken)
    {
        int? asked = int.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
        if (index.Resolve(asked) is not { } version)
        {
            return null;
        }

        string text = await LoadFileAsync(index.Versions[version].File, cancellationToken);
        UnitSummary[] units = ids.Select(id => UnitData.ReadUnit(text, id)).OfType<UnitSummary>().ToArray();
        if (units.Length == 0)
        {
            return null;
        }

        string? image = IconUrl(imageBase, units[0].BlueprintId);
        if (units.Length == 1)
        {
            UnitSummary unit = units[0];
            return new LinkPreviewCard(Name(unit), $"{Summary(unit)} · release {version}", image, $"Icon of the {Name(unit)}");
        }

        return new LinkPreviewCard($"{List(units.Select(Name))} compared",
            $"Side by side in release {version}: cost, health, speed, range and weapons.", image, $"Icon of the {Name(units[0])}");
    }

    /// <summary>Units across the releases: in which ones they changed, from the index alone where it can.</summary>
    private async Task<LinkPreviewCard?> HistoryCardAsync(UnitDataIndex index, string[] ids, string imageBase, CancellationToken cancellationToken)
    {
        if (index.Latest is not { } latest)
        {
            return null;
        }

        int[] versions = index.Versions.Keys.Order().ToArray();
        string latestText = await LoadFileAsync(index.Versions[latest].File, cancellationToken);
        string oldestText = await LoadFileAsync(index.Versions[versions[0]].File, cancellationToken);

        List<(string Id, string Name, string Story)> stories = [];
        foreach (string id in ids)
        {
            // A unit that is gone now has its name in the oldest data, if anywhere.
            if ((UnitData.ReadUnit(latestText, id) ?? UnitData.ReadUnit(oldestText, id)) is not { } unit)
            {
                continue;
            }

            int[] added = versions.Where(version => index.Versions[version].Changes?.Added.Contains(id) == true).ToArray();
            int[] changed = versions.Where(version => index.Versions[version].Changes?.Changed.Contains(id) == true).ToArray();
            stories.Add((id, Name(unit), Story(versions[0], added, changed)));
        }

        if (stories.Count == 0)
        {
            return null;
        }

        string? image = IconUrl(imageBase, stories[0].Id);
        string title = stories.Count == 1 ? $"The {stories[0].Name} through the years" : $"{List(stories.Select(story => story.Name))} through the years";
        string description = stories.Count == 1
            ? $"{stories[0].Story}."
            : string.Join(" · ", stories.Select(story => $"{story.Name}: {LowerFirst(story.Story)}"));
        return new LinkPreviewCard(title, description, image, $"Icon of the {stories[0].Name}");
    }

    /// <summary>
    /// What happened to a unit since the oldest release with data (<paramref name="first"/>): "Since 3700,
    /// changed in 3765, 3801 and 3830", "Added in 3765, unchanged since", "Unchanged in every release since 3700".
    /// </summary>
    internal static string Story(int first, IReadOnlyList<int> added, IReadOnlyList<int> changed)
    {
        string changes = changed.Count switch
        {
            0 => "unchanged since",
            <= 5 => $"changed in {List(changed.Select(version => version.ToString(CultureInfo.InvariantCulture)))}",
            _ => $"changed in {changed.Count} releases, most recently in {changed[^1]}",
        };

        return added.Count > 0 ? $"Added in {added[0]}, {changes}"
            : changed.Count == 0 ? $"Unchanged in every release since {first}"
            : $"Since {first}, {changes}";
    }

    /// <summary>"Experimental Mobile Factory · UEF · 28,000 mass, 350,000 energy · 12,500 health, 20,000 shield · builds 20 units".</summary>
    internal static string Summary(UnitSummary unit)
    {
        List<string> parts = [];
        if (unit.Name is not null && unit.Description is { Length: > 0 } description)
        {
            parts.Add(description);
        }

        string? tech = unit.TechLevel switch
        {
            4 when unit.Description?.Contains("Experimental", StringComparison.OrdinalIgnoreCase) != true => "experimental",
            >= 1 and <= 3 => $"tech {unit.TechLevel}",
            _ => null,
        };
        if (string.Join(' ', new[] { unit.Faction, tech }.OfType<string>()) is { Length: > 0 } kind)
        {
            parts.Add(kind);
        }

        if (unit.BuildCostMass is > 0 || unit.BuildCostEnergy is > 0)
        {
            parts.Add($"{Number(unit.BuildCostMass)} mass, {Number(unit.BuildCostEnergy)} energy");
        }
        if (unit.MaxHealth is > 0)
        {
            parts.Add(unit.ShieldMaxHealth is > 0
                ? $"{Number(unit.MaxHealth)} health, {Number(unit.ShieldMaxHealth)} shield"
                : $"{Number(unit.MaxHealth)} health");
        }
        if (unit.Builds.Count > 0)
        {
            parts.Add(unit.Builds.Count == 1 ? "builds 1 unit" : $"builds {unit.Builds.Count} units");
        }

        return string.Join(" · ", parts);
    }

    private static string Name(UnitSummary unit) => unit.Name ?? unit.Description ?? unit.BlueprintId;

    private static string Number(double? value) => (value ?? 0).ToString("#,0.##", CultureInfo.InvariantCulture);

    /// <summary>"A", "A and B", "A, B and C".</summary>
    private static string List(IEnumerable<string> items)
    {
        string[] all = items.ToArray();
        return all.Length <= 1 ? string.Concat(all) : $"{string.Join(", ", all[..^1])} and {all[^1]}";
    }

    private static string LowerFirst(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    /// <summary>The blueprint ids of a parameter: lower case, distinct, in order, at most <see cref="MaxUnits"/>.</summary>
    private static string[] Ids(string? value) => (value ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(id => id.ToLowerInvariant())
        .Where(id => id.Length <= 32 && id.All(char.IsAsciiLetterOrDigit))
        .Distinct()
        .Take(MaxUnits)
        .ToArray();

    private static string ImageBase(HttpRequest request) => $"{request.Scheme}://{request.Host}{request.PathBase}/";

    /// <summary>The unit's icon (64 px, transparent), when there is one.</summary>
    private string? IconUrl(string imageBase, string blueprintId)
    {
        string file = $"images/units/units/{blueprintId.ToLowerInvariant()}.png";
        return environment.WebRootFileProvider.GetFileInfo(file).Exists ? imageBase + file : null;
    }

    private async Task<UnitDataIndex> LoadIndexAsync(CancellationToken cancellationToken)
    {
        const string key = "unit-data-index";
        if (cache.TryGetValue(key, out UnitDataIndex? cached) && cached is not null)
        {
            return cached;
        }

        UnitDataIndex index = UnitDataIndex.Deserialize(await LoadFileAsync("index.json", cancellationToken));
        cache.Set(key, index, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = DataLifetime, Size = 1 });
        return index;
    }

    /// <summary>A file of the unit data as text, cached: a data file is read line by line (UnitData.ReadUnit).</summary>
    private async Task<string> LoadFileAsync(string name, CancellationToken cancellationToken)
    {
        string key = "unit-data-file:" + name;
        if (cache.TryGetValue(key, out string? cached) && cached is not null)
        {
            return cached;
        }

        IFileInfo file = environment.WebRootFileProvider.GetFileInfo(DataFolder + name);
        if (!file.Exists)
        {
            throw new IOException($"The unit data has no {name}.");
        }

        await using Stream stream = file.CreateReadStream();
        using StreamReader reader = new(stream);
        string text = await reader.ReadToEndAsync(cancellationToken);
        cache.Set(key, text, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = DataLifetime, Size = 1 });
        return text;
    }
}
