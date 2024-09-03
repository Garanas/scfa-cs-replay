
namespace FAForever.Replay
{
    public interface ReplayInput
    {
        /// <summary>
        /// Created by the engine to advance the tick.
        /// </summary>
        /// <param name="TicksToAdvance"></param>
        public record struct Advance(int TicksToAdvance) : ReplayInput;

        /// <summary>
        /// Created by the engine to set the command source for the following events.
        /// </summary>
        /// <param name="SourceId"></param>
        public record struct SetCommandSource(int SourceId) : ReplayInput;

        /// <summary>
        /// Created by the engine when a player leaves the game.
        /// </summary>
        public record struct CommandSourceTerminated(): ReplayInput;

        /// <summary>
        /// Created by the engine to check the state of the game
        /// </summary>
        /// <param name="Hash"></param>
        /// <param name="Tick"></param>
        public record struct VerifyChecksum(byte[] Hash, int Tick) : ReplayInput;

        /// <summary>
        /// Created by the User global `SessionRequestPause` to request a pause
        /// </summary>
        public record struct RequestPause() : ReplayInput;

        /// <summary>
        /// Created by the User global `SessionResume` to request a resume
        /// </summary>
        public record struct RequestResume() : ReplayInput;

        /// <summary>
        /// Created by the console command `wld_SingleStep` while the game is paused
        /// </summary>
        public record struct SingleStep() : ReplayInput;

        /// <summary>
        /// Created by the console command `CreateUnit`
        /// </summary>
        /// <param name="PlayerId"></param>
        /// <param name="BlueprintId"></param>
        /// <param name="X"></param>
        /// <param name="Z"></param>
        /// <param name="Heading"></param>
        public record struct CreateUnit(int ArmyId, string BlueprintId, float X, float Z, float Heading) : ReplayInput;

        /// <summary>
        /// Created by the console command `CreateProp`
        /// </summary>
        /// <param name="BlueprintId"></param>
        /// <param name="X"></param>
        /// <param name="Z"></param>
        /// <param name="Heading"></param>
        public record struct CreateProp(string BlueprintId, float X, float Z, float Heading) : ReplayInput;

        /// <summary>
        /// Created by the console commands `DestroySelectedUnits` and `DestroySelectedUnits`
        /// </summary>
        /// <param name="EntityId"></param>
        public record struct DestroyEntity(int EntityId) : ReplayInput;

        /// <summary>
        /// Created by the console command `TeleportSelectedUnits`
        /// </summary>
        /// <param name="EntityId"></param>
        /// <param name="X"></param>
        /// <param name="Y"></param>
        /// <param name="Z"></param>
        public record struct WarpEntity(int EntityId, float X, float Y, float Z) : ReplayInput;

        /// <summary>
        /// Created by the UserUnit function `ProcessInfo`
        /// </summary>
        /// <param name="EntityId"></param>
        /// <param name="Arg1"></param>
        /// <param name="Arg2"></param>
        public record struct ProcessInfoPair(int EntityId, String Arg1, String Arg2) : ReplayInput;

        /// <summary>
        /// Created by the engine when the user creates a command by clicking
        /// </summary>
        /// <param name="Units"></param>
        /// <param name="Data"></param>
        public record struct IssueCommand(CommandUnits Units, CommandData Data) : ReplayInput;

        /// <summary>
        /// Created by the User global function `IssueBlueprintCommand`
        /// </summary>
        /// <param name="Factories"></param>
        /// <param name="Data"></param>
        public record struct IssueFactoryCommand(CommandUnits Factories, CommandData Data) : ReplayInput;

        /// <summary>
        /// Created by the User global function `IncreaseBuildCountInQueue`
        /// </summary>
        /// <param name="CommandId"></param>
        /// <param name="Delta"></param>
        public record struct IncreaseCommandCount(int CommandId, int Delta) : ReplayInput;

        /// <summary>
        /// Created by the user global function `DecreaseBuildCountInQueue`
        /// </summary>
        /// <param name="CommandId"></param>
        /// <param name="Delta"></param>
        public record DecreaseCommandCount(int CommandId, int Delta): ReplayInput;

        /// <summary>
        /// Created by the engine when updating the target (entity or position) of a command
        /// </summary>
        /// <param name="CommandId"></param>
        /// <param name="Target"></param>
        public record struct UpdateCommandTarget(int CommandId, CommandTarget Target) : ReplayInput;

        /// <summary>
        /// Created by the engine when transforming the command (move to patrol)
        /// </summary>
        /// <param name="CommandId"></param>
        /// <param name="Type"></param>
        public record struct UpdateCommandType(int CommandId, CommandType Type) : ReplayInput;

        public record struct UpdateCommandLuaParameters(int CommandId, LuaData LuaParameters, float X, float Y, float Z) : ReplayInput;

        /// <summary>
        /// Created by the User global function `DeleteCommand`
        /// </summary>
        /// <param name="CommandId"></param>
        /// <param name="EntityId"></param>
        public record struct RemoveCommandFromQueue(int CommandId, int EntityId) : ReplayInput;

        /// <summary>
        /// Created by debug related console commands such as `SallyShears`
        /// </summary>
        /// <param name="Command"></param>
        /// <param name="X"></param>
        /// <param name="Y"></param>
        /// <param name="Z"></param>
        /// <param name="FocusArmy"></param>
        /// <param name="Units"></param>
        public record struct DebugCommand(String Command, float X, float Y, float Z, byte FocusArmy, CommandUnits Units) : ReplayInput;

        /// <summary>
        /// Created by the User global function `ExecLuaInSim`
        /// </summary>
        /// <param name="LuaCode"></param>
        public record struct ExecuteLuaInSim(String LuaCode) : ReplayInput;

        /// <summary>
        /// Created by the user global function `SimCallback`
        /// </summary>
        /// <param name="func"></param>
        /// <param name="LuaParameters"></param>
        /// <param name="Units"></param>
        public record struct SimCallback(String Endpoint, LuaData LuaParameters, CommandUnits Units, byte[] Unknown1, byte[] Unknown2) : ReplayInput;

        /// <summary>
        /// Created by the User global function `SessionEndGame`
        /// </summary>
        public record struct EndGame() : ReplayInput;

        public record struct Unknown(ReplayInputType Type, byte[] Data) : ReplayInput;

        public record struct Error(Exception Exception, byte[] Data) : ReplayInput;
    }

}
