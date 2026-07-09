namespace TakhtyaTaboot.Util
{
    // The scripted 1707 emperor cascade (ImperialSuccessionEventBehavior) crowns the appointed
    // heir and then removes the outgoing emperor in a single scripted act. SuccessionBehavior
    // watches every ruler death and would read that removal as an open throne, spawning a
    // generic succession crisis on the very day the scripted heir accedes. Bracketing the
    // scripted act with WithSuppressedCrisis (same pattern as ThroneWar.WithInternalPeace)
    // tells the crisis engine to stand down for that one death.
    public static class ScriptedSuccession
    {
        [System.ThreadStatic] public static bool InProgress;

        public static void WithSuppressedCrisis(System.Action act)
        {
            bool prev = InProgress;
            InProgress = true;
            try { act(); }
            finally { InProgress = prev; }
        }
    }
}
