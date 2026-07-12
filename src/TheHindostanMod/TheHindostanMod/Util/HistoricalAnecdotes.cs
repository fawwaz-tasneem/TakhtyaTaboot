namespace TakhtyaTaboot.Util
{
    // The news of the roads, PURE and unit-tested: what men actually talked about in the
    // serais and camps of early-18th-century Hindostan — real events, retold as hearsay in
    // the Persianate voice of the age. Surfaced by the dialogue pack ("What word do the
    // roads carry?"); the teller rotates through the pool by his identity and the week, so
    // the same lord tells the same tale for a while and then moves on.
    public static class HistoricalAnecdotes
    {
        public static readonly string[] Pool =
        {
            "They say the old Padishah Alamgir sewed prayer-caps with his own hands to pay for his shroud, and died owning nothing — he who owned Hindostan. Twenty-five years in the Deccan, and for what? The bands he hunted collect their fourth from the villages he taxed.",
            "A sarraf in the bazaar swears the Jagat Seth's house in Murshidabad clears more silver in a season than the Padishah's mint at Shahjahanabad. When the banker sneezes, he says, the exchange rate catches cold from Kabul to the sea.",
            "You have heard of Mir Wais the Ghilzai? Freed Kandahar from the Persians with a jirga and a knife, and his son — they whisper — dreams of Isfahan itself. The Afghans have remembered they once took thrones instead of tolls.",
            "In the Panth they still tell of Banda Singh Bahadur — how he broke Sirhind and minted coin in the Gurus' names, and how they brought him to Delhi in an iron cage and he never once asked for mercy. The misls have not forgotten. Neither has Delhi.",
            "A pilgrim from the south told me the Maratha horse cross the Narmada now as other men cross a market square. Sixty miles a day, no baggage, and the chauth collected before the governor has finished his morning prayers.",
            "They say the young Peshwa's men can bridle their horses in the dark and be ten kos away by first light. The Nizam's officers laugh at the ragged riders — those of them, at least, who have not yet had to chase them.",
            "The talk in the caravanserai is that the English at their factories weigh out silver like grain merchants but drill their peons like soldiers. My cousin has seen it at the coast: clerks with muster-rolls. Clerks, huzoor, with muster-rolls.",
            "An old trooper of the Deccan war told me the imperial camp was a city of half a million souls — bazaars, qazis, dancing-girls, all of it dragged over the ghats for a quarter of a century. Wherever it halted, the province starved; wherever it marched, the roads broke.",
            "In Rajputana they say Ajit Singh of Marwar was smuggled out of Delhi in a basket of sweets when he was an infant, and won back his father's kingdom by outliving the emperor who seized it. Patience, they say, is also a weapon.",
            "The kotwal's men were saying the Sayyid Brothers make and unmake Padishahs the way a tailor makes and unmakes a coat — and that every coat they have cut so far has been a shroud.",
            "A darwesh at the shrine was preaching that the empire is a great tent whose pole is broken, and every amir now holds up his own corner and calls it the sky. The kotwal moved him along, but no one in the crowd disagreed.",
            "They tell of Sawai Jai Singh of Amber that he has built instruments of stone the size of palaces, to read the heavens as a clerk reads a ledger — and that his star-tables put the Firangi almanacs to shame. A king who measures the sky! What will the Kachwaha think of next?",
            "Word from the passes is that the Afridi have raised the Khyber toll again. The caravan masters curse them from Kabul to Lahore — and pay. Kings hire the Khyber, my friend; nobody has ever owned it.",
            "A boatman on the great river told me Bengal's muslin is woven so fine a whole bolt passes through a signet ring — and that the ladies of the Firangi courts pay for it in silver bullion, chest upon chest, straight into the Nawab's harbours.",
        };

        // Stable pick: the same teller keeps his tale for a week, then the pool turns.
        public static string Tale(int tellerSeed, int weekOfCampaign)
        {
            int n = Pool.Length;
            int i = ((tellerSeed + weekOfCampaign) % n + n) % n;
            return Pool[i];
        }
    }
}
