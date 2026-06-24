using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

namespace TakhtyaTaboot.Config
{
    // In-game tuning for Takht ya Taboot, served through MCM (Mod Configuration Menu).
    // Behaviours never touch this class directly — they read values through Tune, which
    // falls back to compiled defaults if MCM is unavailable. Open with:
    //   Main menu / Esc -> Options -> Mod Options -> Takht ya Taboot
    public class TYTSettings : AttributeGlobalSettings<TYTSettings>
    {
        public override string Id => "TakhtyaTaboot_v1";
        public override string DisplayName => "Takht ya Taboot";
        public override string FolderName => "TakhtyaTaboot";
        public override string FormatType => "json2";

        // ── Career: valour & elevation ───────────────────────────────────────────────
        [SettingPropertyGroup("Career & Mansab")]
        [SettingPropertyFloatingInteger("Valour per battle won", 0f, 30f, "0.0", RequireRestart = false,
            HintText = "Valour earned for winning a field battle against a real army. Higher = faster rise.", Order = 0)]
        public float ValourPerWin { get; set; } = 4f;

        [SettingPropertyGroup("Career & Mansab")]
        [SettingPropertyFloatingInteger("Siege valour multiplier", 1f, 5f, "0.0", RequireRestart = false,
            HintText = "Storming or defending a settlement is worth this many times a field battle.", Order = 1)]
        public float ValourSiegeMultiplier { get; set; } = 2f;

        [SettingPropertyGroup("Career & Mansab")]
        [SettingPropertyFloatingInteger("Valour for capturing/routing an enemy king", 0f, 300f, "0", RequireRestart = false,
            HintText = "Huge one-time valour bonus when the enemy sovereign is captured or routed in a battle you win.", Order = 2)]
        public float ValourKingCapture { get; set; } = 80f;

        [SettingPropertyGroup("Career & Mansab")]
        [SettingPropertyFloatingInteger("Valour per personal kill", 0f, 5f, "0.0", RequireRestart = false,
            HintText = "Valour gained per enemy you personally cut down in battle. Deserters (bhagode) count; common bandits give nothing.", Order = 3)]
        public float ValourPerKill { get; set; } = 0.5f;

        [SettingPropertyGroup("Career & Mansab")]
        [SettingPropertyFloatingInteger("Valour needed per rank step", 5f, 200f, "0", RequireRestart = false,
            HintText = "Valour required to be eligible for the next mansab = this x the next rank index. Higher = harder.", Order = 3)]
        public float ValourPerRankStep { get; set; } = 30f;

        [SettingPropertyGroup("Career & Mansab")]
        [SettingPropertyFloatingInteger("Renown needed per rank step", 0f, 500f, "0", RequireRestart = false,
            HintText = "Clan renown required for the next mansab = this x the next rank index. Higher = harder.", Order = 4)]
        public float RenownPerRankStep { get; set; } = 60f;

        [SettingPropertyGroup("Career & Mansab")]
        [SettingPropertyInteger("Minimum relation with king to be elevated", -50, 50, "0", RequireRestart = false,
            HintText = "Your relation with your sovereign must be at least this to be raised in rank.", Order = 5)]
        public int MinRelationForElevation { get; set; } = 0;

        // ── Career: muster, demotion, stipend ────────────────────────────────────────
        [SettingPropertyGroup("Career & Mansab")]
        [SettingPropertyFloatingInteger("Troop capacity multiplier", 0.25f, 3f, "0.00", RequireRestart = false,
            HintText = "Scales the troop target of every mansab. Your party cap is set to this target so you can always field it.", Order = 6)]
        public float TroopCapacityMultiplier { get; set; } = 1f;

        [SettingPropertyGroup("Career & Mansab")]
        [SettingPropertyInteger("Assumed vanilla base party size", 0, 200, "0", RequireRestart = false,
            HintText = "The party-size the game gives before mansab. The mansab bonus is (rank target minus this) so the cap lands on the target.", Order = 7)]
        public int BaseTroopCapacity { get; set; } = 30;

        [SettingPropertyGroup("Career & Mansab")]
        [SettingPropertyFloatingInteger("Muster retention fraction", 0.4f, 1f, "0.00", RequireRestart = false,
            HintText = "You must keep at least this fraction of your rank's troop target. Fall below it and a demotion clock starts.", Order = 8)]
        public float RetentionFraction { get; set; } = 0.8f;

        [SettingPropertyGroup("Career & Mansab")]
        [SettingPropertyInteger("Days below muster before demotion", 5, 120, "0", RequireRestart = false,
            HintText = "Consecutive days below the retention fraction before you are reduced one mansab.", Order = 9)]
        public int DemoteGraceDays { get; set; } = 30;

        [SettingPropertyGroup("Career & Mansab")]
        [SettingPropertyFloatingInteger("Stipend per required troop (per 30 days)", 0f, 20f, "0.0", RequireRestart = false,
            HintText = "Every 30 days the treasury pays you this x your rank's troop target. 0 disables the stipend.", Order = 10)]
        public float StipendPerTroop { get; set; } = 2f;

        // ── Tenure edict (Feudal <-> Mansabdari) ─────────────────────────────────────
        [SettingPropertyGroup("Tenure & Rotation")]
        [SettingPropertyFloatingInteger("Legitimacy needed to impose Mansabdari", 0f, 100f, "0", RequireRestart = false,
            HintText = "A sovereign's legitimacy must clear this to rewrite tenure law. Only a secure throne may convert the realm to rotational, non-hereditary mansabs.", Order = 0)]
        public float TenureLegitimacyFloor { get; set; } = 50f;

        [SettingPropertyGroup("Tenure & Rotation")]
        [SettingPropertyFloatingInteger("Base influence to impose Mansabdari", 0f, 1000f, "0", RequireRestart = false,
            HintText = "Flat influence the crown spends to enact the tenure edict, on top of a little per affected noble.", Order = 1)]
        public float TenureEdictBaseInfluence { get; set; } = 150f;

        [SettingPropertyGroup("Tenure & Rotation")]
        [SettingPropertyFloatingInteger("Base gold to impose Mansabdari", 0f, 100000f, "0", RequireRestart = false,
            HintText = "Flat gold the crown pays to enact the edict; entrenched, resentful magnates add to this proportionally to their Rusukh and holdings.", Order = 2)]
        public float TenureEdictBaseGold { get; set; } = 5000f;

        [SettingPropertyGroup("Tenure & Rotation")]
        [SettingPropertyFloatingInteger("Defiance threshold that resists reform", 0f, 1f, "0.00", RequireRestart = false,
            HintText = "A noble whose defiance chance (deep Rusukh vs a weak crown) meets this will refuse the reform outright rather than be bought off.", Order = 3)]
        public float TenureResistThreshold { get; set; } = 0.5f;

        [SettingPropertyGroup("Tenure & Rotation")]
        [SettingPropertyInteger("Mansab rotation interval (days)", 180, 3600, "0", RequireRestart = false,
            HintText = "How long a mansabdar may hold a fief under Mansabdari tenure before the crown rotates him on. 360 days = 1 year.", Order = 4)]
        public int TenureRotationIntervalDays { get; set; } = 1080;

        // ── Succession law (per-kingdom constitution) ─────────────────────────────────
        [SettingPropertyGroup("Succession Law")]
        [SettingPropertyFloatingInteger("Legitimacy needed to rewrite the succession law", 0f, 100f, "0", RequireRestart = false,
            HintText = "A sovereign's legitimacy must clear this to proclaim a new law of succession (primogeniture, election, or appointed Wali Ahd).", Order = 0)]
        public float SuccLawLegitimacyFloor { get; set; } = 50f;

        [SettingPropertyGroup("Succession Law")]
        [SettingPropertyFloatingInteger("Base influence to change the succession law", 0f, 1000f, "0", RequireRestart = false,
            HintText = "Flat influence the crown spends to enact a succession-law edict, on top of a little per house whose expectations it overturns.", Order = 1)]
        public float SuccLawBaseInfluence { get; set; } = 150f;

        [SettingPropertyGroup("Succession Law")]
        [SettingPropertyFloatingInteger("Heir's claim-support boost", 0f, 100f, "0", RequireRestart = false,
            HintText = "Starting support a named Wali Ahd or a primogeniture eldest son gains in a succession crisis (a Naib gets half).", Order = 2)]
        public float HeirSupportBoost { get; set; } = 25f;

        [SettingPropertyGroup("Succession Law")]
        [SettingPropertyFloatingInteger("Election decisive margin", 1f, 3f, "0.00", RequireRestart = false,
            HintText = "How far the winner of an election vote must clear the runner-up to settle the throne; a closer result throws the realm into civil war.", Order = 3)]
        public float MagnateElectionDecisiveMargin { get; set; } = 1.25f;

        [SettingPropertyGroup("Succession Law")]
        [SettingPropertyInteger("AI law-review interval (days)", 90, 1800, "0", RequireRestart = false,
            HintText = "How often an AI sovereign reconsiders his realm's succession law and names an heir. 360 days = 1 year.", Order = 4)]
        public int AiLawReviewIntervalDays { get; set; } = 360;

        [SettingPropertyGroup("Succession Law")]
        [SettingPropertyFloatingInteger("Contest chance floor on a ruler's death", 0f, 1f, "0.00", RequireRestart = false,
            HintText = "Minimum chance that a ruler's death sparks a contested succession even under a secure law with a valid heir. 0 = clean accessions allowed; higher = the war-of-princes is always a real risk.", Order = 5)]
        public float SuccessionContestFloor { get; set; } = 0.20f;

        // ── Council & capital ────────────────────────────────────────────────────────
        [SettingPropertyGroup("Council & Capital")]
        [SettingPropertyInteger("Councils a king must hold per year", 1, 12, "0", RequireRestart = false,
            HintText = "A sovereign who convenes fewer imperial councils than this in a year loses authority.", Order = 0)]
        public int KingCouncilsPerYear { get; set; } = 4;

        [SettingPropertyGroup("Council & Capital")]
        [SettingPropertyInteger("Councils a lord must hold per year", 0, 6, "0", RequireRestart = false,
            HintText = "A landed lord who convenes fewer councils than this in a year loses influence.", Order = 1)]
        public int LordCouncilsPerYear { get; set; } = 1;

        [SettingPropertyGroup("Council & Capital")]
        [SettingPropertyInteger("Cost to move the capital", 0, 1000000, "0", RequireRestart = false,
            HintText = "Gold a sovereign must pay to relocate the imperial capital to another of his towns.", Order = 2)]
        public int MoveCapitalCost { get; set; } = 200000;

        // ── Villages & patrol ────────────────────────────────────────────────────────
        [SettingPropertyGroup("Villages & Patrol")]
        [SettingPropertyFloatingInteger("Daily chance bandits overwhelm a village", 0f, 0.3f, "0.000", RequireRestart = false,
            HintText = "Per-day chance a high-threat village is overrun and its zamindar sends for help.", Order = 0)]
        public float PatrolOverwhelmChance { get; set; } = 0.05f;

        [SettingPropertyGroup("Villages & Patrol")]
        [SettingPropertyFloatingInteger("Militia & zamindar defence weight", 0f, 4f, "0.00", RequireRestart = false,
            HintText = "How strongly a capable zamindar and standing militia suppress a village's bandit threat.", Order = 1)]
        public float MilitiaDefenceWeight { get; set; } = 1f;

        [SettingPropertyGroup("Villages & Patrol")]
        [SettingPropertyInteger("Troops a dispatched commander takes", 10, 150, "0", RequireRestart = false,
            HintText = "When you send a companion to relieve a village, this many men leave your party for the relief.", Order = 2)]
        public int ReliefDetachmentSize { get; set; } = 40;
    }
}
