namespace TakhtyaTaboot.Util
{
    // The timeline of scripted history, PURE and unit-tested: real events of the age, authored
    // in AD years (HistoricalCalendar converts) and fired once each by ScriptedHistoryBehavior.
    // The table carries the WHEN and the WHAT-KIND; the engine-side effects and the akhbar
    // prose live in the behavior's switch, keyed by id. Events fire when the campaign reaches
    // the year — but only if the world still allows them (a dead kingdom cannot declare war),
    // and each is marked done regardless, so nothing refires or dangles.
    public static class ScriptedHistory
    {
        public static readonly string[] ValidKinds = { "restructure", "war", "peace", "rising", "coup", "sack", "proclamation" };

        // (adYear, id, kind). Chronological; ids unique.
        public static readonly (int Year, string Id, string Kind)[] Events =
        {
            (1707, "mysore_house",   "restructure"), // Tipu's line folded into Hyder's house (also heals old saves)
            (1707, "deccan_war",     "war"),         // Alamgir's folly does not die with him: empire vs the Marathas
            (1709, "banda_rising",   "rising"),      // Banda Singh Bahadur raises the Panth: empire vs the misls
            (1714, "deccan_treaty",  "peace"),       // Hussain Ali's treaty with the Marathas
            (1724, "hyder_coup",     "coup"),        // the Dalvai's seizure of Mysore (compressed from 1761)
            (1737, "bajirao_delhi",  "war"),         // Bajirao's dash on Delhi
            (1739, "nadir_sack",     "sack"),        // Nadir Shah takes Delhi and the Peacock Throne
            (1747, "durrani_rise",   "proclamation"),// Ahmad Shah proclaimed at Kandahar: the Durrani push begins
        };
    }
}
