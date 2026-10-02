using System.Text.Json;

namespace FAForever.Replay.Viewer.Services.Api;

/// <summary>
/// A minimal reader for the JSON:API (Elide) documents that api.faforever.com returns.
/// Only the parts we consume are modelled: resources with attributes, to-one/to-many
/// relationship ids, the included resources and the page totals.
/// </summary>
public sealed class JsonApiDocument
{
    public List<JsonApiResource> Data { get; } = [];

    private readonly Dictionary<(string Type, string Id), JsonApiResource> included = [];

    public int? TotalRecords { get; private set; }

    public int? TotalPages { get; private set; }

    public JsonApiResource? FindIncluded(string type, string id)
        => included.TryGetValue((type, id), out JsonApiResource? resource) ? resource : null;

    public JsonApiResource? FindIncluded((string Type, string Id)? reference)
        => reference is { } key ? FindIncluded(key.Type, key.Id) : null;

    public static JsonApiDocument Parse(string json)
    {
        JsonApiDocument document = new();
        using JsonDocument parsed = JsonDocument.Parse(json);
        JsonElement root = parsed.RootElement;

        if (root.TryGetProperty("data", out JsonElement data))
        {
            if (data.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in data.EnumerateArray())
                {
                    document.Data.Add(JsonApiResource.Parse(element));
                }
            }
            else if (data.ValueKind == JsonValueKind.Object)
            {
                document.Data.Add(JsonApiResource.Parse(data));
            }
        }

        if (root.TryGetProperty("included", out JsonElement includedElement) && includedElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in includedElement.EnumerateArray())
            {
                JsonApiResource resource = JsonApiResource.Parse(element);
                document.included[(resource.Type, resource.Id)] = resource;
            }
        }

        if (root.TryGetProperty("meta", out JsonElement meta)
            && meta.TryGetProperty("page", out JsonElement page))
        {
            if (page.TryGetProperty("totalRecords", out JsonElement totalRecords) && totalRecords.TryGetInt32(out int records))
            {
                document.TotalRecords = records;
            }
            if (page.TryGetProperty("totalPages", out JsonElement totalPages) && totalPages.TryGetInt32(out int pages))
            {
                document.TotalPages = pages;
            }
        }

        return document;
    }
}

public sealed class JsonApiResource
{
    public required string Type { get; init; }

    public required string Id { get; init; }

    private JsonElement attributes;
    private readonly Dictionary<string, List<(string Type, string Id)>> relationships = [];

    public static JsonApiResource Parse(JsonElement element)
    {
        JsonApiResource resource = new()
        {
            Type = element.GetProperty("type").GetString() ?? string.Empty,
            Id = element.GetProperty("id").GetString() ?? string.Empty,
        };

        if (element.TryGetProperty("attributes", out JsonElement attributesElement))
        {
            // Clone: the backing JsonDocument is disposed after parsing.
            resource.attributes = attributesElement.Clone();
        }

        if (element.TryGetProperty("relationships", out JsonElement relationshipsElement) && relationshipsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty relationship in relationshipsElement.EnumerateObject())
            {
                if (!relationship.Value.TryGetProperty("data", out JsonElement related))
                {
                    continue;
                }

                List<(string, string)> references = [];
                if (related.ValueKind == JsonValueKind.Object)
                {
                    references.Add(ParseReference(related));
                }
                else if (related.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in related.EnumerateArray())
                    {
                        references.Add(ParseReference(item));
                    }
                }

                resource.relationships[relationship.Name] = references;
            }
        }

        return resource;
    }

    private static (string Type, string Id) ParseReference(JsonElement element)
        => (element.GetProperty("type").GetString() ?? string.Empty, element.GetProperty("id").GetString() ?? string.Empty);

    public (string Type, string Id)? Relationship(string name)
        => relationships.TryGetValue(name, out List<(string Type, string Id)>? references) && references.Count > 0 ? references[0] : null;

    public IReadOnlyList<(string Type, string Id)> Relationships(string name)
        => relationships.TryGetValue(name, out List<(string Type, string Id)>? references) ? references : [];

    public string? GetString(string attribute)
        => attributes.ValueKind == JsonValueKind.Object && attributes.TryGetProperty(attribute, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public double? GetNumber(string attribute)
        => attributes.ValueKind == JsonValueKind.Object && attributes.TryGetProperty(attribute, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    public bool? GetBoolean(string attribute)
        => attributes.ValueKind == JsonValueKind.Object && attributes.TryGetProperty(attribute, out JsonElement value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    public DateTimeOffset? GetDateTimeOffset(string attribute)
        => GetString(attribute) is { } text && DateTimeOffset.TryParse(text, out DateTimeOffset value) ? value : null;
}
