
using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Replay
{
    /// <summary>
    /// One message of the replay's input stream, with the moment and the source it came from.
    /// </summary>
    /// <param name="Tick">The beat the message arrived in: the number of Advance messages before it.
    /// Ten beats make a second. Beats continue while the game is paused, when the simulation's own
    /// tick stands still, so after a pause this runs ahead of game time.</param>
    /// <param name="SourceId">The command source (index into <see cref="ReplayHeader.Clients"/>)
    /// that sent the message.</param>
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
        /// Created by the console command `CreateUnit`; the simulation runs it only with cheats.
        /// </summary>
        /// <param name="ArmyId">The army the unit is created for.</param>
        /// <param name="BlueprintId">The blueprint id of the unit.</param>
        /// <param name="X">In world coordinates</param>
        /// <param name="Z">In world coordinates</param>
        /// <param name="Heading">The heading of the unit.</param>
        public record CreateUnit(int Tick, int SourceId, int ArmyId, string BlueprintId, float X, float Z, float Heading) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the console command `CreateProp`; the simulation runs it only with cheats.
        /// </summary>
        /// <param name="BlueprintId">The blueprint path of the prop.</param>
        /// <param name="X">In world coordinates</param>
        /// <param name="Y">In world coordinates</param>
        /// <param name="Z">In world coordinates</param>
        public record CreateProp(int Tick, int SourceId, string BlueprintId, float X, float Y, float Z) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the console commands `DestroySelectedUnits` and `DestroySelectedUnits`
        /// </summary>
        /// <param name="EntityId"></param>
        public record DestroyEntity(int Tick, int SourceId, int EntityId) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the console command `TeleportSelectedUnits`: the entity moves to a new place
        /// and orientation (a <c>VTransform</c> in the engine). The simulation runs it only with cheats.
        /// </summary>
        /// <param name="EntityId">The entity that moves.</param>
        /// <param name="Orientation">Its new orientation.</param>
        /// <param name="X">In world coordinates</param>
        /// <param name="Y">In world coordinates</param>
        /// <param name="Z">In world coordinates</param>
        public record WarpEntity(int Tick, int SourceId, int EntityId, ReplayOrientation Orientation, float X, float Y, float Z) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// Created by the UserUnit function `ProcessInfo`: one message per unit, so a selection
        /// of several units sends several. Keys the simulation knows: SetFireState, SetAutoMode,
        /// SetAutoSurfaceMode, SetRepeatQueue, SetPaused, SiloBuildTactical and SiloBuildNuke (value
        /// "add"), CustomName, ToggleScriptBit, PlayNoStagingPlatformsVO and PlayBusyStagingPlatformsVO.
        /// </summary>
        /// <param name="EntityId">The unit the pair applies to.</param>
        /// <param name="Name">The key.</param>
        /// <param name="Value">The value, as text.</param>
        public record ProcessInfoPair(int Tick, int SourceId, int EntityId, String Name, String Value) : ReplayInput(Tick, SourceId);

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

        /// <summary>
        /// Replaces the map cells of a queued command and gives it a ground target
        /// (<c>SetCommandCells</c> in the engine). No user interface or Lua code of the game is known
        /// to send it.
        /// </summary>
        /// <param name="CommandId">The command that changes.</param>
        /// <param name="Cells">Its new cells.</param>
        /// <param name="X">The new target, in world coordinates</param>
        /// <param name="Y">The new target, in world coordinates</param>
        /// <param name="Z">The new target, in world coordinates</param>
        public record SetCommandCells(int Tick, int SourceId, int CommandId, IReadOnlyList<CommandCell> Cells, float X, float Y, float Z) : ReplayInput(Tick, SourceId);

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
        /// Created by the User global function `ExecLuaInSim(name, args)`, which debug windows such as
        /// lua/debug/ArmyStats.lua use. The message is recorded whatever the cheat setting; the
        /// simulation calls the function only with cheats.
        /// </summary>
        /// <param name="FunctionName">The name of the sim function to call.</param>
        /// <param name="Arguments">The argument it is called with.</param>
        public record ExecuteLuaInSim(int Tick, int SourceId, String FunctionName, LuaData Arguments) : ReplayInput(Tick, SourceId);

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

        /// <summary>
        /// A message of a type the parser does not know (the engine knows types 0 to 23). The
        /// loader steps over it by its length.
        /// </summary>
        /// <param name="Type">The type byte of the message.</param>
        /// <param name="Data">The content of the message, without its type and length.</param>
        public record Unknown(int Tick, int SourceId, ReplayInputType Type, byte[] Data) : ReplayInput(Tick, SourceId);

        /// <summary>
        /// A message the parser could not read: it does not decode to exactly its length, its
        /// content is invalid, or the stream ends inside it. The loader steps over it by its length
        /// and reads on; the message has no other effect (an Advance in error does not advance
        /// <see cref="ReplayInput.Tick"/>).
        /// </summary>
        /// <param name="Type">The type byte of the message.</param>
        /// <param name="Exception">What went wrong.</param>
        /// <param name="Data">The content of the message, without its type and length.</param>
        public record Error(int Tick, int SourceId, ReplayInputType Type, Exception Exception, byte[] Data) : ReplayInput(Tick, SourceId);
    }

}
