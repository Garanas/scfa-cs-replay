
namespace FAForever.FileFormats.Replay
{
    /// <summary>
    /// The family of an entity, the top four bits of its id (bits 31 to 28). The engine reserves ids
    /// per family and army (see AGENTS.md, "What the engine does").
    /// </summary>
    public enum ReplayEntityFamily
    {
        /// <summary>A unit: <c>(army &lt;&lt; 20) | serial</c>. Selections of orders hold only units.</summary>
        Unit = 0,

        /// <summary>A projectile, reserved per army.</summary>
        Projectile = 1,

        /// <summary>A prop (trees, rocks, wreckage): what reclaim orders target. The middle byte is 0xFF.</summary>
        Prop = 2,

        /// <summary>
        /// A recon blip: what a player sees of an enemy unit on radar or in vision. Attack, capture and
        /// overcharge orders on enemies target blips. A blip has an id of its own, reserved per army of
        /// the unit it shows; the replay cannot map it to that unit's id.
        /// </summary>
        ReconBlip = 3,

        /// <summary>A shield.</summary>
        Shield = 4,

        /// <summary>An entity a script created (<c>Entity</c> in Lua).</summary>
        ScriptEntity = 5,
    }

    /// <summary>
    /// Reads the parts of an entity id: <c>[31..28] family, [27..20] index, [19..0] serial</c>. For a
    /// unit the index is its army. Serials are reused: a serial that is released when its entity is
    /// destroyed returns, lowest first, about 100 beats (10 seconds) later, so one id can name several
    /// units over a game.
    /// </summary>
    public static class ReplayEntityId
    {
        /// <summary>The family of the entity (bits 31 to 28).</summary>
        public static ReplayEntityFamily GetFamily(int entityId) => (ReplayEntityFamily)((uint)entityId >> 28);

        /// <summary>The index within the family (bits 27 to 20): the army for units, projectiles and recon blips.</summary>
        public static int GetIndex(int entityId) => (entityId >> 20) & 0xFF;

        /// <summary>The serial within the family and index (bits 19 to 0).</summary>
        public static int GetSerial(int entityId) => entityId & 0xFFFFF;
    }
}
