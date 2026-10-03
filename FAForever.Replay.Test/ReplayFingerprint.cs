using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FAForever.Replay.Test;

/// <summary>
/// Hashes everything the parser reads from a replay into one fingerprint. Guards performance
/// work on the parser: the output must stay identical, so any change in what is parsed changes
/// the hash. The header is rendered from the wire data only (raw Lua tables and scalar fields),
/// not from derived display properties, so UI-oriented additions to the model do not break it.
/// </summary>
internal static class ReplayFingerprint
{
    public static string Compute(ReplayHeader header, ReplayBody body)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            StringBuilder builder = new StringBuilder();

            AppendHeader(builder, header);
            Flush(hash, builder);

            foreach (ReplayInput input in body.UserInput)
            {
                AppendInput(builder, input);
                Flush(hash, builder);
            }

            builder.Append("InSync=").Append(body.InSync);
            Flush(hash, builder);

            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static void Flush(IncrementalHash hash, StringBuilder builder)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(builder.ToString()));
        builder.Clear();
    }

    private static void AppendHeader(StringBuilder builder, ReplayHeader header)
    {
        builder.Append(header.GameVersion).Append('|')
            .Append(header.ReplayVersion).Append('|')
            .Append(header.PathToScenario).Append('|')
            .Append(header.CheatsEnabled).Append('|')
            .Append(header.Seed).Append('\n');

        ReplayScenarioMap map = header.Scenario.Map;
        builder.Append("Scenario ").Append(header.Scenario.Type).Append('|')
            .Append(map.Name).Append('|').Append(map.Description).Append('|')
            .Append(map.SCMapReference).Append('|').Append(map.PreviewReference).Append('|')
            .Append(map.Repository).Append('|').Append(map.Version).Append('|')
            .Append(map.SizeX).Append('|').Append(map.SizeZ).Append('|')
            .Append(map.MassReclaim).Append('|').Append(map.EnergyReclaim).Append('\n');

        builder.Append("Options ");
        AppendLua(builder, header.Scenario.Options.Raw);
        builder.Append('\n');

        foreach (ReplaySource client in header.Clients)
        {
            builder.Append("Client ").Append(client.PlayerName).Append('|').Append(client.PlayerId).Append('\n');
        }

        foreach (LuaData mod in header.Mods)
        {
            builder.Append("Mod ");
            AppendLua(builder, mod);
            builder.Append('\n');
        }

        foreach (ReplayPlayerOptions army in header.Armies)
        {
            builder.Append("Army ").Append(army.SourceId).Append('|');
            AppendLua(builder, army.Raw);
            builder.Append('\n');
        }
    }

    private static void AppendInput(StringBuilder builder, ReplayInput input)
    {
        // Inputs are plain data records: their generated ToString covers every field,
        // except the contents of Lua tables, which are appended separately.
        builder.Append(input);

        LuaData? lua = input switch
        {
            ReplayInput.IssueCommand command => command.Data.LuaParameters,
            ReplayInput.IssueFactoryCommand command => command.Data.LuaParameters,
            ReplayInput.UpdateCommandLuaParameters update => update.LuaParameters,
            ReplayInput.SimCallback callback => callback.LuaParameters,
            _ => null,
        };

        if (lua is not null)
        {
            builder.Append(" Lua=");
            AppendLua(builder, lua);
        }

        // The generated ToString of CommandUnits shows the memory, not the ids in it.
        CommandUnits? units = input switch
        {
            ReplayInput.IssueCommand command => command.Units,
            ReplayInput.IssueFactoryCommand command => command.Factories,
            ReplayInput.DebugCommand command => command.Units,
            ReplayInput.SimCallback callback => callback.Units,
            _ => null,
        };

        if (units is not null)
        {
            builder.Append(" Ids=");
            foreach (int id in units.EntityIds.Span)
            {
                builder.Append(id).Append(',');
            }
        }

        builder.Append('\n');
    }

    private static void AppendLua(StringBuilder builder, LuaData? data)
    {
        switch (data)
        {
            case null:
                builder.Append("null");
                break;

            case LuaData.Nil:
                builder.Append("nil");
                break;

            case LuaData.Bool boolean:
                builder.Append(boolean.Value ? "true" : "false");
                break;

            case LuaData.Number number:
                builder.Append(number.Value.ToString("R", CultureInfo.InvariantCulture));
                break;

            case LuaData.String text:
                // Length-prefixed, so the rendering is unambiguous whatever the string contains.
                builder.Append('s').Append(text.Value.Length).Append(':').Append(text.Value);
                break;

            case LuaData.Table table:
                builder.Append('{');
                foreach ((string key, LuaData value) in table.Value)
                {
                    builder.Append('k').Append(key.Length).Append(':').Append(key).Append('=');
                    AppendLua(builder, value);
                    builder.Append(',');
                }
                builder.Append('}');
                break;

            default:
                builder.Append(data.GetType().Name);
                break;
        }
    }
}
