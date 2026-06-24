using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Localization;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // Plays out the rapid imperial succession after Aurangzeb. At the campaign's open it seats
    // Aurangzeb on the throne of the House of Timur; then, on the schedule in the pure (tested)
    // ImperialSuccessionPlan — Aurangzeb falls at two months, then an emperor a month until
    // Muhammad Shah — it crowns each heir, kills the last, and announces both by royal farmaan.
    // The dead emperors remain as deceased encyclopedia entries.
    //
    // All TIMING lives in ImperialSuccessionPlan (unit-tested); this class owns only the engine
    // actions and is fully guarded. History bends to reality: if the dynasty has already been
    // toppled or an heir is unavailable, the step is skipped rather than forced.
    public class ImperialSuccessionEventBehavior : CampaignBehaviorBase
    {
        private const string EmpireKingdomId = "empire";
        private const string ImperialClanId  = "clan_empire_north_1"; // House of Timur

        private bool _started;
        private double _startDays;   // CampaignTime.ToDays captured at the campaign's open
        private double _lastDays;    // days-since-open already processed (idempotent window edge)

        public override void RegisterEvents()
        {
            // Only a DAILY-TICK listener — NO new-game/world-gen hook. Aurangzeb begins as emperor
            // by DATA (he is the owner of the House of Timur and of the empire kingdom in XML), so
            // nothing reseats the throne during world generation (doing so native-crashes). Every
            // crown/kill below happens at tick time, when the world is live and such actions are safe.
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("ImperialSuccession.DailyTick", Tick));
        }

        public override void SyncData(IDataStore ds)
        {
            ds.SyncData("tyt_imp_succ_started", ref _started);
            ds.SyncData("tyt_imp_succ_start", ref _startDays);
            ds.SyncData("tyt_imp_succ_last", ref _lastDays);
        }

        private static Kingdom Empire => Kingdom.All.FirstOrDefault(k => k.StringId == EmpireKingdomId);
        private static Clan ImperialClan => Clan.All.FirstOrDefault(c => c.StringId == ImperialClanId);
        private static Hero FindAlive(string id) => Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id);

        private void Tick()
        {
            // First tick of a campaign with this behavior: anchor the clock here (the live map is up).
            if (!_started)
            {
                _startDays = CampaignTime.Now.ToDays;
                _lastDays = 0.0;
                _started = true;
                SeatFirstEmperor();
                TYTLog.Info("Imperial succession armed; Aurangzeb seated on the throne of the House of Timur.");
                return;
            }

            double now = CampaignTime.Now.ToDays - _startDays;
            foreach (int idx in ImperialSuccessionPlan.AccessionsDue(_lastDays, now))
                Accede(idx);
            _lastDays = now;
        }

        // Seat Aurangzeb on the throne at the campaign's open. Done here, on the first live tick —
        // NOT during world generation — because changing a ruling clan's leader mid-worldgen
        // native-crashes (the start-up owner is a stable existing lord in XML; this hands the crown
        // to Aurangzeb once the map is live, exactly as the cascade crowns later emperors).
        private static void SeatFirstEmperor()
        {
            Clan clan = ImperialClan;
            Hero aurangzeb = FindAlive(ImperialSuccessionPlan.Reigns[ImperialSuccessionPlan.FirstEmperorIndex].HeroId);
            if (clan == null || aurangzeb == null)
            { TYTLog.Warn("Imperial succession: Aurangzeb or the House of Timur not found; cascade idle."); return; }
            if (aurangzeb != Hero.MainHero && clan.Leader != aurangzeb)
                ChangeClanLeaderAction.ApplyWithSelectedNewLeader(clan, aurangzeb);
        }

        private void Accede(int idx)
        {
            var reigns = ImperialSuccessionPlan.Reigns;
            Clan clan = ImperialClan;
            Kingdom k = Empire;
            Hero dying = FindAlive(reigns[idx - 1].HeroId);
            Hero heir  = FindAlive(reigns[idx].HeroId);
            string dyingName = reigns[idx - 1].Name, heirName = reigns[idx].Name;

            // History bends to reality: no dynasty or no heir -> skip this accession entirely.
            if (clan == null || heir == null || heir == Hero.MainHero)
            { TYTLog.Warn($"Imperial succession: cannot crown {heirName}; skipped (dynasty/heir gone)."); return; }

            // Crown the heir FIRST so the throne is never vacant, THEN the old emperor passes.
            if (clan.Leader != heir) ChangeClanLeaderAction.ApplyWithSelectedNewLeader(clan, heir);
            LegitimacyBehavior.Instance?.SetLegitimacy(heir, idx == ImperialSuccessionPlan.FinalEmperorIndex ? 60f : 30f);

            if (dying != null && dying.IsAlive && dying != Hero.MainHero)
            {
                if (idx == 1) KillCharacterAction.ApplyByRemove(dying, false, true);  // Aurangzeb — dies of illness
                else KillCharacterAction.ApplyByMurder(dying, heir);                  // intrigue: the new emperor's faction
            }

            UI.RoyalFarmaan.Issue("The Emperor Has Died",
                idx == 1 ? "From the imperial camp in the Deccan" : "From Shahjahanabad",
                $"{dyingName}, Emperor of Hindostan, is dead. The throne of the House of Timur stands open, and the realm holds its breath.",
                seal: "Sealed in mourning");

            if (k != null)
                UI.RoyalFarmaan.FromRuler(k, "Proclamation of Accession",
                    $"Let it be known throughout Hindostan that {heirName} now ascends the Peacock Throne. All mansabdars and " +
                    "governors are called to renew their oaths." +
                    (idx == ImperialSuccessionPlan.FinalEmperorIndex ? " May his reign be long." : ""),
                    "I renew my oath");

            TYTLog.Info($"Imperial succession: {dyingName} dies; {heirName} crowned (reign {idx}).");
        }
    }
}
