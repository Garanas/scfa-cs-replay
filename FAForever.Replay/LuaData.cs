
namespace FAForever.Replay
{
    public interface LuaData
    {

        public record Nil() : LuaData;

        public record Bool(bool Value) : LuaData;

        public record Number(double Value) : LuaData;

        public record String(string Value) : LuaData;

        public record Table(Dictionary<string, LuaData> Value) : LuaData;

    }
}
