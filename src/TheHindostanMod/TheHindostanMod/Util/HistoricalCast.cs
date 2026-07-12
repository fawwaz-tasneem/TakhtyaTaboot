namespace TakhtyaTaboot.Util
{
    // The historical cast's web of friendship and enmity, PURE and unit-tested (no TaleWorlds
    // types). One table drives two things, applied once per campaign by HistoricalCastBehavior:
    //   • Relations — the friendships and rivalries the round-7 biographies (heroes.xml)
    //     describe, within and across kingdoms, so the encyclopedia's prose and the game's
    //     numbers tell the same history;
    //   • Traits — the five personality traits assigned to match each lord's historical
    //     character (the source the AI uses for mercy, oath-keeping, and war counsel).
    // Values are starting relations (-100..100) and trait levels (-2..+2). Hero ids are the
    // mod's cast (spnpccharacters/tyt_spclans); a missing id is skipped harmlessly at apply
    // time (cast lists shift), but the TESTS pin the table's internal consistency.
    public static class HistoricalCast
    {
        public static readonly string[] ValidTraits = { "Honor", "Valor", "Mercy", "Generosity", "Calculating" };

        // ── The web of relations ─────────────────────────────────────────────────────
        public static readonly (string A, string B, int Relation)[] Relations =
        {
            // Delhi: the Sayyid kingmakers against the court
            ("lord_1_58", "lord_NE7_u", 80),   // the Sayyid Brothers stand together
            ("lord_1_1",  "lord_1_58", -60),   // Muhammad Shah despises his kingmakers
            ("lord_1_51", "lord_1_58", -60),   // Qamar ud Din's Turani party vs Abdullah Khan
            ("lord_1_51", "lord_NE7_u", -50),
            ("lord_1_3",  "lord_1_58", -40),   // Najaf Khan blocked twice by the wazir
            ("lord_1_50", "lord_1_51", 40),    // Amir Khan attached to the Turani party
            ("lord_1_5",  "lord_1_20", -50),   // Rohilla vs Bangash: two self-made Afghans, one prize
            ("lord_NE8_l", "lord_1_3", 30),    // the two Persians of the court
            ("lord_1_1",  "lord_1_20", 35),    // the emperor's favourite hammer
            ("lord_1_1",  "lord_4_1",  45),    // Jai Singh, Delhi's loyal grandee (cross-kingdom)
            ("lord_1_51", "lord_1_47", 50),    // Qamar ud Din, cousin to Asaf Jah (cross-kingdom)

            // Bengal: the diwan's machine and its enemies
            ("lord_1_7",  "lord_1_9",  50),    // Murshid Quli + the Jagat Seth: ledger and law
            ("lord_1_7",  "lord_1_71", -50),   // the Dacca princes call the diwan a usurper
            ("lord_1_7",  "lord_1_45", 25),    // Reza Khan, the diwan's exact servant
            ("lord_1_52", "lord_1_53", -25),   // Burdwan vs Rajshahi: acreage and precedence
            ("lord_5_17", "lord_1_7",  -30),   // Raghuji eyes Bengal's treasury (cross-kingdom)

            // The Deccan: the Nizamat
            ("lord_1_47", "lord_1_15", 50),    // Asaf Jah + the Paigah sword-arm
            ("lord_1_47", "lord_1_17", 40),    // + the Salar Jung ministry
            ("lord_1_47", "lord_1_63", -20),   // Arcot's perfect, disobedient courtesy
            ("lord_1_54", "lord_B8_l", -70),   // Janjira vs the Angre: the coast's oldest feud (cross-kingdom)
            ("lord_1_72", "lord_5_17", -45),   // Berar pays Raghuji and hates it (cross-kingdom)
            ("lord_1_30", "lord_1_72", 30),    // the two wardens of the western wall
            ("lord_SE9_l", "lord_NE9_l", 30),  // Bilgram and Badakhshan: the scholars' letters (cross-kingdom)
            ("lord_5_1",  "lord_1_47", -55),   // Bajirao vs Asaf Jah (cross-kingdom)

            // Mysore and the far south
            ("lord_3_5",  "lord_3_17", 40),    // Hyder + Basappaji: the two self-made soldiers
            ("lord_3_5",  "lord_3_19", -40),   // Coorg watches the roads from Srirangapatna
            ("lord_3_18", "lord_A9_l", -60),   // the Zamorin vs Palakkad: older than most kingdoms
            ("lord_A9_l", "lord_3_5",  25),    // Palakkad's letters go to Hyder
            ("lord_3_22", "lord_3_3",  -40),   // the Sira house vs the Kalale: the slain hero
            ("lord_3_1",  "lord_3_16", 40),    // the Gaddi and its loyal cadets
            ("lord_3_16", "lord_3_5",  -20),   // the old court's reservations about the Dalvai

            // Rajputana: honour and precedence
            ("lord_4_1",  "lord_4_3",  20),    // Amber and Marwar: allied, rivalrous
            ("lord_4_1",  "lord_4_16", 40),    // Budh Singh, the loyal brother-in-law
            ("lord_4_1",  "lord_4_22", 45),    // the Shekhawat kin
            ("lord_4_6",  "lord_4_1",  -20),   // Mewar's precedence is not negotiable
            ("lord_4_6",  "lord_4_3",  -20),
            ("lord_4_6",  "lord_4_24", 60),    // Hadi Rani trusts the Jhala entirely

            // The Maratha confederacy
            ("lord_5_1",  "lord_5_3",  60),    // the Peshwa and his Chhatrapati
            ("lord_5_1",  "lord_5_5",  55),    // Scindia, Bajirao's creature and proud of it
            ("lord_5_1",  "lord_5_14", 50),    // Holkar, spotted and raised
            ("lord_5_5",  "lord_5_14", 30),    // the twin engine: comrades in rivalry
            ("lord_5_1",  "lord_5_16", -45),   // the Senapati faction: Gujarat's tribute
            ("lord_5_1",  "lord_5_17", -40),   // Raghuji: 'the east needs no permission from Pune'
            ("lord_5_3",  "lord_5_17", 30),    // but Shahu's kinsman he remains
            ("lord_5_1",  "lord_5_15", 45),    // the Patwardhans bet the house on the Peshwa
            ("lord_B8_l", "lord_5_1",  -15),   // the admiralty's polite silence

            // The Sikh misls
            ("lord_6_4",  "lord_6_5",  -50),   // Ahluwalia vs Ramgarhia: the never-forgiven insult
            ("lord_6_1",  "lord_6_19", -45),   // Sukerchakia vs Kanhaiya: same rivers, same future
            ("lord_6_16", "lord_6_4",  30),    // the Singhpuria voice mediates
            ("lord_6_16", "lord_6_5",  20),
            ("lord_6_20", "lord_6_19", -25),   // 'the Padishah's Sikh'
            ("lord_6_17", "lord_6_1",  20),    // the scouts ride with the young Sukerchakia
            ("lord_2_1",  "lord_6_1",  -50),   // the Afghan invasions meet the misls (cross-kingdom)

            // The Afghans
            ("lord_2_1",  "lord_2_5",  60),    // the Sadozai throne's Popalzai inner keep
            ("lord_2_1",  "lord_2_18", -60),   // Ghilzai vs Abdali: the lost kingship
            ("lord_2_1",  "lord_2_16", 35),    // the Alakozai kingmakers' debt
            ("lord_2_1",  "lord_2_3",  20),    // the Barakzai serve loyally, and keep the ledger
            ("lord_2_5",  "lord_2_3",  -15),   // heirs apparent watch each other
            ("lord_2_3",  "lord_2_17", -20),   // kin at weddings, rivals everywhere else
            ("lord_2_1",  "lord_2_19", 25),    // the Khyber is paid promptly
        };

        // ── The traits of the cast ───────────────────────────────────────────────────
        public static readonly (string Hero, string Trait, int Level)[] Traits =
        {
            // Delhi
            ("lord_1_1", "Calculating", 1), ("lord_1_1", "Valor", -1), ("lord_1_1", "Generosity", 1),
            ("lord_1_58", "Calculating", 2), ("lord_1_58", "Honor", -1),
            ("lord_NE7_u", "Valor", 2), ("lord_NE7_u", "Mercy", -1),
            ("lord_1_3", "Honor", 2), ("lord_1_3", "Valor", 1), ("lord_1_3", "Calculating", 1),
            ("lord_1_51", "Calculating", 2), ("lord_1_51", "Honor", 1),
            ("lord_1_5", "Calculating", 1), ("lord_1_5", "Valor", 1), ("lord_1_5", "Honor", -1),
            ("lord_1_20", "Valor", 2), ("lord_1_20", "Honor", 1), ("lord_1_20", "Mercy", -1),
            ("lord_1_50", "Calculating", 1), ("lord_1_50", "Valor", -1),
            ("lord_NE8_l", "Honor", 1), ("lord_NE8_l", "Valor", 1),
            ("lord_NE9_l", "Honor", 1), ("lord_NE9_l", "Calculating", 1),
            // Bengal
            ("lord_1_7", "Calculating", 2), ("lord_1_7", "Generosity", -1), ("lord_1_7", "Honor", 1),
            ("lord_1_9", "Calculating", 2), ("lord_1_9", "Generosity", 1), ("lord_1_9", "Valor", -1),
            ("lord_1_11", "Calculating", 1),
            ("lord_1_40", "Valor", 1),
            ("lord_1_45", "Calculating", 1), ("lord_1_45", "Honor", -1),
            ("lord_1_52", "Calculating", 1), ("lord_1_52", "Generosity", 1),
            ("lord_1_53", "Valor", 1), ("lord_1_53", "Calculating", 1),
            ("lord_1_71", "Honor", 1), ("lord_1_71", "Generosity", 1), ("lord_1_71", "Calculating", -1),
            ("lord_WE9_l", "Calculating", 1), ("lord_WE9_l", "Valor", -1), ("lord_WE9_l", "Generosity", 1),
            // The Deccan
            ("lord_1_47", "Calculating", 2), ("lord_1_47", "Mercy", -1), ("lord_1_47", "Honor", 1),
            ("lord_1_15", "Valor", 2), ("lord_1_15", "Honor", 1),
            ("lord_1_17", "Calculating", 1), ("lord_1_17", "Honor", 1),
            ("lord_1_30", "Valor", 1), ("lord_1_30", "Mercy", -1),
            ("lord_1_63", "Calculating", 2), ("lord_1_63", "Generosity", 1),
            ("lord_1_54", "Valor", 2), ("lord_1_54", "Mercy", -1),
            ("lord_1_55", "Calculating", 1), ("lord_1_55", "Valor", 1),
            ("lord_1_72", "Calculating", 1),
            ("lord_SE9_l", "Honor", 2), ("lord_SE9_l", "Mercy", 1), ("lord_SE9_l", "Valor", -1),
            // Mysore
            ("lord_3_1", "Mercy", 1), ("lord_3_1", "Generosity", 1), ("lord_3_1", "Valor", -1),
            ("lord_3_5", "Calculating", 2), ("lord_3_5", "Valor", 1), ("lord_3_5", "Honor", -1),
            ("lord_3_3", "Valor", 2), ("lord_3_3", "Honor", 1), ("lord_3_3", "Calculating", 1),
            ("lord_3_16", "Honor", 1), ("lord_3_16", "Generosity", 1),
            ("lord_3_17", "Valor", 1), ("lord_3_17", "Honor", 1),
            ("lord_3_18", "Calculating", 1), ("lord_3_18", "Generosity", 1),
            ("lord_3_19", "Valor", 2), ("lord_3_19", "Honor", 1), ("lord_3_19", "Mercy", -1),
            ("lord_3_22", "Honor", 1),
            ("lord_A9_l", "Calculating", 2), ("lord_A9_l", "Honor", -1),
            // Rajputana
            ("lord_4_1", "Calculating", 2), ("lord_4_1", "Honor", 1), ("lord_4_1", "Valor", 1),
            ("lord_4_3", "Honor", 2), ("lord_4_3", "Valor", 1), ("lord_4_3", "Mercy", -1), ("lord_4_3", "Calculating", -1),
            ("lord_4_6", "Honor", 2), ("lord_4_6", "Valor", 1), ("lord_4_6", "Mercy", -1),
            ("lord_4_16", "Honor", 2), ("lord_4_16", "Valor", 2), ("lord_4_16", "Generosity", 1), ("lord_4_16", "Calculating", -2),
            ("lord_4_21", "Generosity", 1), ("lord_4_21", "Calculating", 1),
            ("lord_4_22", "Valor", 1), ("lord_4_22", "Honor", 1),
            ("lord_4_23", "Honor", 1), ("lord_4_23", "Valor", 1),
            ("lord_4_24", "Honor", 2), ("lord_4_24", "Valor", 1),
            ("lord_4_27", "Calculating", 1), ("lord_4_27", "Honor", 1),
            ("lord_4_28", "Calculating", 1), ("lord_4_28", "Valor", 1),
            ("lord_V11_l", "Valor", 1), ("lord_V11_l", "Calculating", 1),
            // The Marathas
            ("lord_5_1", "Valor", 2), ("lord_5_1", "Calculating", 1), ("lord_5_1", "Honor", 1),
            ("lord_5_3", "Mercy", 2), ("lord_5_3", "Generosity", 1), ("lord_5_3", "Honor", 1), ("lord_5_3", "Valor", -1),
            ("lord_5_5", "Valor", 2), ("lord_5_5", "Generosity", 1),
            ("lord_5_14", "Valor", 2), ("lord_5_14", "Generosity", 1), ("lord_5_14", "Calculating", -1),
            ("lord_5_16", "Valor", 1), ("lord_5_16", "Honor", -1), ("lord_5_16", "Calculating", 1),
            ("lord_5_17", "Valor", 1), ("lord_5_17", "Calculating", 1), ("lord_5_17", "Honor", -1),
            ("lord_5_15", "Calculating", 1), ("lord_5_15", "Honor", 1),
            ("lord_B8_l", "Valor", 2), ("lord_B8_l", "Calculating", 1), ("lord_B8_l", "Mercy", -1),
            // The Sikh misls
            ("lord_6_1", "Valor", 2), ("lord_6_1", "Calculating", 1),
            ("lord_6_4", "Calculating", 2), ("lord_6_4", "Honor", 1),
            ("lord_6_5", "Honor", 1), ("lord_6_5", "Calculating", 1), ("lord_6_5", "Valor", 1),
            ("lord_6_16", "Honor", 2), ("lord_6_16", "Mercy", 1),
            ("lord_6_17", "Valor", 1), ("lord_6_17", "Honor", 1),
            ("lord_6_18", "Honor", 1), ("lord_6_18", "Generosity", 1),
            ("lord_6_19", "Valor", 1), ("lord_6_19", "Calculating", 1), ("lord_6_19", "Honor", -1),
            ("lord_6_20", "Calculating", 2), ("lord_6_20", "Honor", -1), ("lord_6_20", "Generosity", 1),
            ("lord_K9_l", "Valor", 2), ("lord_K9_l", "Mercy", -1),
            // The Afghans
            ("lord_2_1", "Valor", 2), ("lord_2_1", "Calculating", 1), ("lord_2_1", "Honor", 1),
            ("lord_2_3", "Calculating", 2), ("lord_2_3", "Valor", 1),
            ("lord_2_5", "Honor", 1), ("lord_2_5", "Valor", 1),
            ("lord_2_16", "Valor", 1), ("lord_2_16", "Calculating", 1),
            ("lord_2_17", "Valor", 2),
            ("lord_2_18", "Valor", 1), ("lord_2_18", "Honor", -1), ("lord_2_18", "Calculating", 1), ("lord_2_18", "Mercy", -1),
            ("lord_2_19", "Calculating", 1), ("lord_2_19", "Generosity", 1), ("lord_2_19", "Valor", 1),
            ("lord_2_20", "Honor", 1), ("lord_2_20", "Valor", 1),
            ("lord_S9_l", "Calculating", 1), ("lord_S9_l", "Generosity", 1),
        };
    }
}
