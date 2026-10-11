using System.Text;

namespace FAForever.FileFormats.Replay.Tests;

/// <summary>
/// Writes a replay by hand, in the engine's layout (faf-re 2ba55b2: VCR_CreateReplay in
/// src/sdk/moho/misc/SessionStartup.cpp for the header, CMarshaller in
/// src/sdk/moho/net/CClientManagerImpl.cpp for the messages): for messages that no replay asset
/// holds, and for broken messages. Lua values are written as their bytes (tag 4 opens a table,
/// 5 closes it, 1 is a string, 0 a number, 2 nil).
/// </summary>
internal sealed class ReplayStreamBuilder
{
    private readonly MemoryStream stream = new MemoryStream();
    private readonly BinaryWriter writer;

    public ReplayStreamBuilder()
    {
        writer = new BinaryWriter(stream);
    }

    /// <summary>A header with one client ("Jip", pause allowance 3) and the given source lists per army.</summary>
    public ReplayStreamBuilder Header(params byte[][] armySources)
    {
        String("Supreme Commander v1.50.3830");
        String("\r\n");
        writer.Write("Replay v1.9\r\n"u8);
        String("/maps/SCMP_009/SCMP_009.scmap");
        String("\r\n\x1A");
        LengthPrefixed([4, 5]);
        LengthPrefixed([4, 5]);
        writer.Write((byte)1);
        String("Jip");
        writer.Write(3);
        writer.Write((byte)1);
        writer.Write((byte)armySources.Length);
        for (int i = 0; i < armySources.Length; i++)
        {
            LengthPrefixed([4, 1, .. Encoding.UTF8.GetBytes("PlayerName"), 0, 1, .. Encoding.UTF8.GetBytes($"Army {i + 1}"), 0, 5]);
            writer.Write(armySources[i]);
            writer.Write((byte)0xFF);
        }

        writer.Write(12345);
        return this;
    }

    /// <summary>A message: its type, its length (these three bytes included) and its content.</summary>
    public ReplayStreamBuilder Message(byte type, byte[] content, int? length = null)
    {
        writer.Write(type);
        writer.Write((ushort)(length ?? content.Length + 3));
        writer.Write(content);
        return this;
    }

    public ReplayStreamBuilder Advance(int beats = 1) => Message((byte)ReplayInputType.Advance, BitConverter.GetBytes(beats));

    public ReplayStreamBuilder EndGame() => Message((byte)ReplayInputType.EndGame, []);

    /// <summary>Raw bytes, e.g. a message cut off by the end of the stream.</summary>
    public ReplayStreamBuilder Bytes(byte[] bytes)
    {
        writer.Write(bytes);
        return this;
    }

    /// <summary>The bytes written so far: an uncompressed replay (.scfareplay).</summary>
    public byte[] ToArray()
    {
        writer.Flush();
        return stream.ToArray();
    }

    public Replay Load() => ReplayLoader.LoadSCFAReplayFromStream(new MemoryStream(ToArray()));

    /// <summary>The content of a message from pieces: numbers, strings (written with their zero) and byte arrays.</summary>
    public static byte[] Content(params object[] parts)
    {
        MemoryStream content = new MemoryStream();
        BinaryWriter partWriter = new BinaryWriter(content);
        foreach (object part in parts)
        {
            switch (part)
            {
                case byte value: partWriter.Write(value); break;
                case short value: partWriter.Write(value); break;
                case int value: partWriter.Write(value); break;
                case float value: partWriter.Write(value); break;
                case string value: partWriter.Write(Encoding.UTF8.GetBytes(value)); partWriter.Write((byte)0); break;
                case byte[] value: partWriter.Write(value); break;
                default: throw new ArgumentException($"Cannot write a {part.GetType().Name}.");
            }
        }

        partWriter.Flush();
        return content.ToArray();
    }

    private void String(string value)
    {
        writer.Write(Encoding.UTF8.GetBytes(value));
        writer.Write((byte)0);
    }

    private void LengthPrefixed(byte[] bytes)
    {
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
