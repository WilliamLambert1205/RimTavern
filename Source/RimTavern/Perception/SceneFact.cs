namespace RimTavern.Perception
{
    public enum SceneFactCategory
    {
        Actor,
        MapThing,
        Equipment,
        Container,
        World,
        Delta
    }

    /// <summary>
    /// P1-1: one unified "thing the engine sees". Sources (SceneSource) will emit these later (P2).
    /// entityKey always refers to an EntityKey (P1-2); name is the localized display name;
    /// gloss is an optional one-line explanation (def.description based), salience drives selection.
    /// </summary>
    public class SceneFact
    {
        public SceneFactCategory Category;
        public string EntityKey;
        public string Name = "";
        public string Gloss = "";
        public float Salience;
        public string Anchor = "";      // e.g. room name / "on pawn X" / map
        public int FirstSeenTick;
    }
}
