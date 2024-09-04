
namespace FAForever.Replay
{
    public interface ReplayInput
    {
        /// <summary>
        /// Created by the engine when a player leaves the game.
        /// </summary>
        public record CommandSourceTerminated() : ReplayInput;

        /// <summary>
        /// Created by the engine to check the state of the game
        /// </summary>
        /// <param name="Hash"></param>
        /// <param name="Tick"></param>
        public record VerifyChecksum(byte[] Hash, int Tick) : ReplayInput;

        /// <summary>
        /// Created by the User global `SessionRequestPause` to request a pause
        /// </summary>
        public record RequestPause() : ReplayInput;

        /// <summary>
        /// Created by the User global `SessionResume` to request a resume
        /// </summary>
        public record RequestResume() : ReplayInput;

        /// <summary>
        /// Created by the console command `wld_SingleStep` while the game is paused
        /// </summary>
        public record SingleStep() : ReplayInput;

        /// <summary>
        /// Created by the console command `CreateUnit`
        /// </summary>
        /// <param name="PlayerId"></param>
        /// <param name="BlueprintId"></param>
        /// <param name="X"></param>
        /// <param name="Z"></param>
        /// <param name="Heading"></param>
        public record CreateUnit(int ArmyId, string BlueprintId, float X, float Z, float Heading) : ReplayInput;

        /// <summary>
        /// Created by the console command `CreateProp`
        /// </summary>
        /// <param name="BlueprintId"></param>
        /// <param name="X"></param>
        /// <param name="Z"></param>
        /// <param name="Heading"></param>
        public record CreateProp(string BlueprintId, float X, float Z, float Heading) : ReplayInput;

        /// <summary>
        /// Created by the console commands `DestroySelectedUnits` and `DestroySelectedUnits`
        /// </summary>
        /// <param name="EntityId"></param>
        public record DestroyEntity(int EntityId) : ReplayInput;

        /// <summary>
        /// Created by the console command `TeleportSelectedUnits`
        /// </summary>
        /// <param name="EntityId"></param>
        /// <param name="X"></param>
        /// <param name="Y"></param>
        /// <param name="Z"></param>
        public record WarpEntity(int EntityId, float X, float Y, float Z) : ReplayInput;

        /// <summary>
        /// Created by the UserUnit function `ProcessInfo`
        /// </summary>
        /// <param name="EntityId"></param>
        /// <param name="Arg1"></param>
        /// <param name="Arg2"></param>
        public record ProcessInfoPair(int EntityId, String Name, String Value) : ReplayInput;

        /// <summary>
        /// Created by the engine when the user creates a command by clicking
        /// </summary>
        /// <param name="Units"></param>
        /// <param name="Data"></param>
        public record IssueCommand(CommandUnits Units, CommandData Data) : ReplayInput;

        /// <summary>
        /// Created by the User global function `IssueBlueprintCommand`
        /// </summary>
        /// <param name="Factories"></param>
        /// <param name="Data"></param>
        public record IssueFactoryCommand(CommandUnits Factories, CommandData Data) : ReplayInput;

        /// <summary>
        /// Created by the User global function `IncreaseBuildCountInQueue`
        /// </summary>
        /// <param name="CommandId"></param>
        /// <param name="Delta"></param>
        public record IncreaseCommandCount(int CommandId, int Delta) : ReplayInput;

        /// <summary>
        /// Created by the user global function `DecreaseBuildCountInQueue`
        /// </summary>
        /// <param name="CommandId"></param>
        /// <param name="Delta"></param>
        public record DecreaseCommandCount(int CommandId, int Delta) : ReplayInput;

        /// <summary>
        /// Created by the engine when updating the target (entity or position) of a command
        /// </summary>
        /// <param name="CommandId"></param>
        /// <param name="Target"></param>
        public record UpdateCommandTarget(int CommandId, CommandTarget Target) : ReplayInput;

        /// <summary>
        /// Created by the engine when transforming the command (move to patrol)
        /// </summary>
        /// <param name="CommandId"></param>
        /// <param name="Type"></param>
        public record UpdateCommandType(int CommandId, CommandType Type) : ReplayInput;

        public record UpdateCommandLuaParameters(int CommandId, LuaData LuaParameters, float X, float Y, float Z) : ReplayInput;

        /// <summary>
        /// Created by the User global function `DeleteCommand`
        /// </summary>
        /// <param name="CommandId"></param>
        /// <param name="EntityId"></param>
        public record RemoveCommandFromQueue(int CommandId, int EntityId) : ReplayInput;

        /// <summary>
        /// Created by debug related console commands such as `SallyShears`
        /// </summary>
        /// <param name="Command"></param>
        /// <param name="X"></param>
        /// <param name="Y"></param>
        /// <param name="Z"></param>
        /// <param name="FocusArmy"></param>
        /// <param name="Units"></param>
        public record DebugCommand(String Command, float X, float Y, float Z, byte FocusArmy, CommandUnits Units) : ReplayInput;

        /// <summary>
        /// Created by the User global function `ExecLuaInSim`
        /// </summary>
        /// <param name="LuaCode"></param>
        public record ExecuteLuaInSim(String LuaCode) : ReplayInput;

        /// <summary>
        /// Created by the user global function `SimCallback`
        /// </summary>
        /// <param name="func"></param>
        /// <param name="LuaParameters"></param>
        /// <param name="Units"></param>
        public record SimCallback(String Endpoint, LuaData LuaParameters, CommandUnits Units) : ReplayInput;

        /// <summary>
        /// Created by the User global function `SessionEndGame`
        /// </summary>
        public record EndGame() : ReplayInput;

        public record Unknown(ReplayInputType Type, byte[] Data) : ReplayInput;

        public record Error(Exception Exception, byte[] Data) : ReplayInput;
    }

}
