using System.Collections.Generic;

namespace TakhtyaTaboot.Util
{
    // The rapid imperial succession after Aurangzeb, as a PURE timeline (no engine types, unit-
    // tested in TheHindostanMod.Tests). The user's spec: Aurangzeb lingers ill for two months,
    // then a string of emperors each holds the throne about a month until Muhammad Shah, who
    // reigns on. Absolute dates don't matter — everything is months-from-campaign-open.
    //
    // The engine behavior (ImperialSuccessionEventBehavior) owns the heroes and the kill/crown
    // actions; this class owns only WHO reigns and WHEN, so the timing can be tested in isolation.
    public static class ImperialSuccessionPlan
    {
        public const int DaysPerMonth = 30;

        public sealed class Reign
        {
            public readonly string HeroId;        // the lord id that carries this emperor
            public readonly string Name;          // display / farmaan name
            public readonly int CrownedAtMonth;   // months after campaign open this emperor accedes (0 = reigns from open)
            public Reign(string heroId, string name, int crownedAtMonth)
            { HeroId = heroId; Name = name; CrownedAtMonth = crownedAtMonth; }
        }

        // Ordered oldest-first. Index 0 sits on the throne when the campaign opens. Each later entry
        // accedes at its CrownedAtMonth, at which moment the PREVIOUS entry's emperor dies.
        // HeroIds are the purpose-built emperor heroes defined in heroes.xml (new ids, correct ages,
        // male, parented into the House of Timur). The final reign uses the existing Muhammad Shah
        // (lord_1_1). ReligionNamePatch reads the Name here to fix each emperor's display name.
        public static readonly Reign[] Reigns =
        {
            new Reign("tyt_aurangzeb",    "Aurangzeb Alamgir", 0),  // ailing; reigns from the campaign's open
            new Reign("tyt_bahadur_shah", "Bahadur Shah I",    2),  // Aurangzeb dies at 2 months
            new Reign("tyt_jahandar_shah","Jahandar Shah",     3),
            new Reign("tyt_farrukhsiyar", "Farrukhsiyar",      4),
            new Reign("tyt_rafi_darajat", "Rafi ud-Darajat",   5),
            new Reign("tyt_shah_jahan_2", "Shah Jahan II",     6),
            new Reign("lord_1_1",         "Muhammad Shah",     7),  // accedes at 7 months and reigns on
        };

        public static int FirstEmperorIndex => 0;
        public static int FinalEmperorIndex => Reigns.Length - 1;
        public static int AccessionDay(int reignIndex) => Reigns[reignIndex].CrownedAtMonth * DaysPerMonth;

        // Which emperor sits the throne after 'days' have elapsed since the campaign opened.
        public static int ReigningIndexAt(double days)
        {
            int idx = 0;
            for (int i = 1; i < Reigns.Length; i++)
                if (days >= AccessionDay(i)) idx = i;
            return idx;
        }

        // The accessions (and therefore the deaths) whose moment falls in the half-open window
        // (fromDays, toDays]. Returns the index of each newly-crowned emperor (always >= 1); the
        // caller kills Reigns[index-1] and crowns Reigns[index]. Ordered ascending so a window that
        // spans several (e.g. after a long pause) is processed oldest-first.
        public static List<int> AccessionsDue(double fromDays, double toDays)
        {
            var due = new List<int>();
            for (int i = 1; i < Reigns.Length; i++)
            {
                int d = AccessionDay(i);
                if (d > fromDays && d <= toDays) due.Add(i);
            }
            return due;
        }
    }
}
