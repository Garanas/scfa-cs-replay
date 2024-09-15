
namespace FAForever.Replay
{
    public abstract record ReplayInput(int Tick, int SourceId)
    {
        /// <summary>
        /// Created by the engine when a player leaves the game.
        /// </summary>
        public record CommandSourceTerminated(int Tick, int SourceId) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the User global `SessionRequestPause` to request a pause
        /// </summary>
        public record RequestPause(int Tick, int SourceId) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the User global `SessionResume` to request a resume
        /// </summary>
        public record RequestResume(int Tick, int SourceId) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the console command `wld_SingleStep` while the game is paused
        /// </summary>
        public record SingleStep(int Tick, int SourceId) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the console command `CreateUnit`
        /// </summary>
        /// <param name="PlayerId"></param>
        /// <param name="BlueprintId"></param>
        /// <param name="X"></param>
        /// <param name="Z"></param>
        /// <param name="Heading"></param>
        public record CreateUnit(int Tick, int SourceId, int ArmyId, string BlueprintId, float X, float Z, float Heading) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the console command `CreateProp`
        /// </summary>
        /// <param name="BlueprintId"></param>
        /// <param name="X"></param>
        /// <param name="Z"></param>
        /// <param name="Heading"></param>
        public record CreateProp(int Tick, int SourceId, string BlueprintId, float X, float Z, float Heading) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the console commands `DestroySelectedUnits` and `DestroySelectedUnits`
        /// </summary>
        /// <param name="EntityId"></param>
        public record DestroyEntity(int Tick, int SourceId, int EntityId) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the console command `TeleportSelectedUnits`
        /// </summary>
        /// <param name="EntityId"></param>
        /// <param name="X"></param>
        /// <param name="Y"></param>
        /// <param name="Z"></param>
        public record WarpEntity(int Tick, int SourceId, int EntityId, float X, float Y, float Z) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the UserUnit function `ProcessInfo`.
        /// 
        /// When you have multiple units in your selection this event is fired for each unit. 
        /// A common example is to (un)pause a unit. In the library we choose to combine these 
        /// events and instead of listing the entities we list the number of units that are 
        /// affected. This significantly reduces the number of events in memory.
        /// </summary>
        /// <param name="Arg1"></param>
        /// <param name="Arg2"></param>
        public record ProcessInfoPair(int Tick, int SourceId, int EntityCount, String Name, String Value) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the engine when the user creates a command by clicking
        /// </summary>
        /// <param name="Units"></param>
        /// <param name="Data"></param>
        public record IssueCommand(int Tick, int SourceId, CommandUnits Units, CommandData Data) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the User global function `IssueBlueprintCommand`
        /// </summary>
        /// <param name="Factories"></param>
        /// <param name="Data"></param>
        public record IssueFactoryCommand(int Tick, int SourceId, CommandUnits Factories, CommandData Data) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the User global function `IncreaseBuildCountInQueue`
        /// </summary>
        /// <param name="CommandId"></param>
        /// <param name="Delta"></param>
        public record IncreaseCommandCount(int Tick, int SourceId, int CommandId, int Delta) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the user global function `DecreaseBuildCountInQueue`
        /// </summary>
        /// <param name="CommandId"></param>
        /// <param name="Delta"></param>
        public record DecreaseCommandCount(int Tick, int SourceId, int CommandId, int Delta) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the engine when updating the target (entity or position) of a command
        /// </summary>
        /// <param name="CommandId"></param>
        /// <param name="Target"></param>
        public record UpdateCommandTarget(int Tick, int SourceId, int CommandId, CommandTarget Target) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the engine when transforming the command (move to patrol)
        /// </summary>
        /// <param name="CommandId"></param>
        /// <param name="Type"></param>
        public record UpdateCommandType(int Tick, int SourceId, int CommandId, CommandType Type) : ReplayInput(Tick, SourceId);

        public record UpdateCommandLuaParameters(int Tick, int SourceId, int CommandId, LuaData LuaParameters, float X, float Y, float Z) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the User global function `DeleteCommand`
        /// </summary>
        /// <param name="CommandId"></param>
        /// <param name="EntityId"></param>
        public record RemoveCommandFromQueue(int Tick, int SourceId, int CommandId, int EntityId) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by debug related console commands such as `SallyShears`
        /// </summary>
        /// <param name="Command"></param>
        /// <param name="X"></param>
        /// <param name="Y"></param>
        /// <param name="Z"></param>
        /// <param name="FocusArmy"></param>
        /// <param name="Units"></param>
        public record DebugCommand(int Tick, int SourceId, String Command, float X, float Y, float Z, byte FocusArmy, CommandUnits Units) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the User global function `ExecLuaInSim`
        /// </summary>
        /// <param name="LuaCode"></param>
        public record ExecuteLuaInSim(int Tick, int SourceId, String LuaCode) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the user global function `SimCallback`
        /// </summary>
        /// <param name="func"></param>
        /// <param name="LuaParameters"></param>
        /// <param name="Units"></param>
        public record SimCallback(int Tick, int SourceId, String Endpoint, LuaData LuaParameters, CommandUnits Units) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the User global function `SessionEndGame`
        /// </summary>
        public record EndGame(int Tick, int SourceId) : ReplayInput(Tick, SourceId);

        public record Unknown(int Tick, int SourceId, ReplayInputType Type, byte[] Data) : ReplayInput(Tick, SourceId);

        public record Error(int Tick, int SourceId, Exception Exception, byte[] Data) : ReplayInput(Tick, SourceId);
    }

}
