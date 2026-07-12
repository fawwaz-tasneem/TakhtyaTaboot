using System.Collections.Generic;

namespace TakhtyaTaboot.Util
{
    // The book of oaths, PURE and unit-tested (no TaleWorlds types). Each culture of Hindostan
    // swears fealty at a coronation darbar in its own voice — seven variations per culture
    // (playtest round 6), chosen deterministically per lord so the same man always swears the
    // same oath, and coloured by his regard for the new sovereign (CoronationMath.OathRegister):
    // gladly, dutifully, or through his teeth. Culture ids are the engine ids the mod reskins
    // (CultureNamePatch): empire=Mughals, empire_w=Bengalis, empire_s=Hyderabadis,
    // sturgia=Afghans, aserai=Mysoreans, vlandia=Rajputs, battania=Marathas, khuzait=Sikhs.
    public static class CoronationOaths
    {
        public const int VariantsPerCulture = 7;

        private static readonly Dictionary<string, string[]> Book = new Dictionary<string, string[]>
        {
            ["empire"] = new[] // Mughals — the Persianate high court
            {
                "Padishah, shadow of God upon the earth: my sword, my salt, and the service of my house are laid at the foot of the takht.",
                "May the khutba be read and the sikka struck in your name, padishah. My house swears its fealty before the assembled court.",
                "As my fathers served the house of Timur, so I serve you, padishah. The oath is given; let the registers set it down.",
                "Padishah, I make the kornish before the throne as the old custom bids, and with it I give my word: my banner rides at your farmaan.",
                "The court has its sun again, padishah. My house turns toward it — sword, salt, and revenue, all sworn to the takht.",
                "By the canopy and the peacock throne, padishah, I bind my house to yours. Call, and my sowars answer.",
                "Padishah, the pen writes and the sword confirms: my fealty is entered this day, and my house stands surety for it.",
            },
            ["empire_w"] = new[] // Bengalis — the delta's wealth
            {
                "Padishah, from the thousand rivers of Bengal I bring my oath: my boats, my rice lands, and my levy are sworn to the takht.",
                "The delta remembers who guards it, padishah. My house swears — and Bengal's treasuries stand behind the word.",
                "Padishah, as the monsoon feeds the paddy, so the throne feeds the realm. I swear my fealty, and my house with me.",
                "From Murshidabad's counting-houses to the sea, padishah, my word is given: fealty, revenue, and the levy of the east.",
                "Padishah, Bengal weaves muslin fine enough for kings — and oaths strong enough to hold them. Mine is sworn this day.",
                "The rivers change their beds, padishah, but this oath will not move: my house is yours, in flood and in drought.",
                "Padishah, I swear for the east: while the tide runs up the Hooghly, my banner answers the summons of the takht.",
            },
            ["empire_s"] = new[] // Hyderabadis — the Deccani court
            {
                "Padishah, the Deccan bends its knee with grace: my house, my horse, and the southern marches are sworn to your throne.",
                "As the pearl is set in the crown, padishah, so my oath is set in the registers this day. The south keeps faith.",
                "Padishah, the courts of the Deccan know courtesy and they know steel. Both are yours: my fealty is given before the hall.",
                "From the walls of the southern forts, padishah, I bring the old salute and the old bond: my house serves the takht.",
                "Padishah, my fathers held the marches for your fathers. The watch continues — my oath stands sworn.",
                "Let the south be counted loyal, padishah: my banner, my jagirs, and my word, all laid before the throne.",
                "Padishah, the Deccan's roads are long, but its memory is longer. It remembers its oaths — and mine is sworn to you.",
            },
            ["sturgia"] = new[] // Afghans — the plain speech of the passes
            {
                "Padishah, I have eaten your salt, and an Afghan does not forget whose salt he has eaten. My oath is given.",
                "The mountains do not bend, padishah — but a man may give his word, and mine is given: my rifles and my riders are yours.",
                "Padishah, I speak as the passes speak: plainly. My house is sworn to yours. Break faith with us and the hills will remember; keep it, and so will we.",
                "By the hearth and the jirga's witness, padishah, I swear: my banner comes down from the passes when the takht calls.",
                "Padishah, an oath in the hills is worth more than gold in the plains. Mine is sworn — hold it as we hold ours.",
                "I bend the knee once, padishah, and mean it once and for all. The men of my house are yours to summon.",
                "Padishah, the road through the passes is open to your word. My oath stands at the mouth of it, sworn before this court.",
            },
            ["aserai"] = new[] // Mysoreans — the southern uplands
            {
                "Padishah, the tiger of the south lowers its head to no one lightly. It lowers it to you: my oath is sworn.",
                "From the sandal groves and the Kaveri's banks, padishah, I bring the fealty of Mysore: my house and my levy are yours.",
                "Padishah, the southern uplands breed hard soldiers and harder oaths. Mine is given this day, before the assembled hall.",
                "As the Kaveri keeps to its course, padishah, my house keeps to its word: sworn to the takht, in war and in peace.",
                "Padishah, Mysore's rockets fly far, and its word carries farther. My fealty stands entered in the registers.",
                "The south watches its kings closely, padishah. It has watched you, and I swear gladly what it has seen: my banner is yours.",
                "Padishah, by the hill forts of the south I swear it: call, and the uplands answer under my banner.",
            },
            ["vlandia"] = new[] // Rajputs — honor of the blood
            {
                "Padishah, a Rajput's oath is not given twice, for it never breaks once. By my sword and the honour of my line: I am yours.",
                "By the blood of the sun-born houses, padishah, I swear before the court: my sword rides at the takht's word.",
                "Padishah, my ancestors watch from their cenotaphs as I bend the knee. I do not shame them: the oath is sworn, and it will hold.",
                "The saffron robe is worn but once, padishah, and the oath sworn but once. Hear mine now: my house keeps faith with the throne.",
                "Padishah, iron may rust and stone may crack — a Rajput's word does neither. My fealty is given before this hall.",
                "By sword, by fire, by the honour of the clan, padishah: my house is sworn to yours. Let any man in this hall witness it.",
                "Padishah, we count our lineage in oaths kept. Add this one to the count: my banner answers the takht.",
            },
            ["battania"] = new[] // Marathas — the ghats and the swift horse
            {
                "Padishah, the ghats breed swift horse and careful men. This careful man gives his word: my banner rides at your call.",
                "From the hill forts, padishah, I bring the Maratha's oath: hard-bargained, and harder to break. My house is sworn.",
                "Padishah, the Deccan soil is thin, but the loyalty it grows runs deep. Mine is planted this day before the throne.",
                "As the monsoon horse-columns ride, padishah — fast, and where they are pledged — so rides my house: sworn to the takht.",
                "Padishah, a Maratha weighs an oath like grain in the hand. I have weighed this one, and I swear it in full.",
                "By the forts my fathers held and the passes they watched, padishah: my fealty is given, and my sowars with it.",
                "Padishah, the hills taught us to promise little and keep all of it. I promise you my banner — and I will keep it.",
            },
            ["khuzait"] = new[] // Sikhs — plain truth and steel
            {
                "Padishah, steel does not flatter and neither do I: my oath is sworn, and my house stands behind it.",
                "By the steel I carry and the truth I keep, padishah: my banner is sworn to the takht this day.",
                "Padishah, the Panth teaches that a word given is a debt owed. I give mine before this court: my house serves the throne.",
                "The five rivers have watered many oaths, padishah. This one will not run dry: my fealty is given.",
                "Padishah, I speak once and plainly, as my faith bids: my sword, my levy, and my word are yours.",
                "As the steel bracelet has no end, padishah, so this oath has none: my house keeps faith with the takht.",
                "Padishah, truth is the highest of all things — and higher still is true living. I swear truly: my banner answers your summons.",
            },
        };

        private static readonly string[] Generic =
        {
            "Padishah, before the assembled court I swear my oath: my sword and my salt are yours while you keep the faith of the throne.",
            "My house bends the knee, padishah. The oath is given before these witnesses, and the registers may set it down.",
            "Padishah, my banner answers the takht's summons from this day: the fealty of my house is sworn.",
            "As my fathers kept faith with the throne, padishah, so do I: my oath stands entered before the hall.",
            "Padishah, sword, salt, and service — all sworn to your throne before this court.",
            "The realm has its sovereign, padishah, and my house has its oath: I swear it here, before the assembled darbar.",
            "Padishah, I give my word before the throne and the hall together: my house keeps faith with the takht.",
        };

        // A stable, engine-free seed from a lord's id, so the same man always speaks the same oath.
        public static int SeedOf(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return 0;
            int s = 0;
            foreach (char c in heroId) s = (s * 31 + c) & 0x7FFFFFFF;
            return s;
        }

        public static string Oath(string cultureId, int seed, CoronationMath.OathRegister register)
        {
            string[] set = cultureId != null && Book.TryGetValue(cultureId, out string[] found) ? found : Generic;
            string body = set[((seed % VariantsPerCulture) + VariantsPerCulture) % VariantsPerCulture];
            switch (register)
            {
                case CoronationMath.OathRegister.Warm:
                    return body + " And I swear it gladly — let the whole hall hear it.";
                case CoronationMath.OathRegister.Cold:
                    return "..." + body + " So it is sworn. Let that suffice.";
                default:
                    return body;
            }
        }

        public static bool HasCulture(string cultureId) => cultureId != null && Book.ContainsKey(cultureId);
        public static IEnumerable<string> KnownCultures => Book.Keys;
    }
}
