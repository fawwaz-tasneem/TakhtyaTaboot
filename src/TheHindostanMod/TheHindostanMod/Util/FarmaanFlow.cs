namespace TakhtyaTaboot.Util
{
    // How weighty a farmaan is. Decides whether the director may suppress or downgrade it.
    //   Ceremonial — coronations, successions, wars: always shown, never suppressed.
    //   Urgent     — needs the player's attention (often carries choices): deduped by key.
    //   Routine    — recurring court noise (stipends, summaries): downgraded to a log line
    //                and folded into the weekly Court Circular digest.
    public enum FarmaanPriority { Ceremonial = 0, Urgent = 1, Routine = 2 }

    public enum FarmaanDecision { Show, Downgrade, Drop }

    // Pure decision logic for the farmaan director (no TaleWorlds types — linked into
    // TheHindostanMod.Tests, FarmaanFlowTests). The invariant that matters most: a
    // farmaan carrying choice callbacks is NEVER downgraded to a plain message — the
    // choices would be silently lost — it can only be deduped (dropped) by its key.
    public static class FarmaanFlow
    {
        public static FarmaanDecision Decide(bool sameKeyQueuedOrActive, int lastShownDay, int today,
                                             int cooldownDays, FarmaanPriority priority, bool hasActions)
        {
            if (priority == FarmaanPriority.Ceremonial) return FarmaanDecision.Show;
            if (sameKeyQueuedOrActive) return FarmaanDecision.Drop;

            // A Routine notice with no cooldown window is PERMANENTLY routine: it goes to
            // the log/digest every time (e.g. the monthly stipend receipt).
            if (priority == FarmaanPriority.Routine && !hasActions && cooldownDays <= 0)
                return FarmaanDecision.Downgrade;

            bool withinCooldown = cooldownDays > 0 && lastShownDay >= 0
                                  && today - lastShownDay < cooldownDays;
            if (!withinCooldown) return FarmaanDecision.Show;

            if (priority == FarmaanPriority.Routine && !hasActions) return FarmaanDecision.Downgrade;
            return FarmaanDecision.Drop; // Urgent repeat, or Routine-with-choices repeat
        }
    }
}
