using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // Scripted history (round 8): the real events of the age, fired once each when the
    // campaign reaches their year (Util/ScriptedHistory, tested; HistoricalCalendar maps
    // AD -> game years). Each event is announced as an AKHBAR farmaan and applies its
    // engine effect only if the world still allows it — a dead kingdom cannot declare war —
    // but is marked done regardless, so nothing refires. Late-loading old saves skip events
    // whose moment has clearly passed (a 1707 war is not declared into a 1719 world).
    //
    // Also owns the two Mysore threads the timeline anchors:
    //   • the house restructure (Tipu's line folded into Hyder Ali's house — heals old saves);
    //   • the TIPU ACCESSION WATCHER: the day Tipu leads Mysore, the kingdom raises the
    //     lion standard — yellow and black, with the lion device (console: mysore_banner).
    public class ScriptedHistoryBehavior : CampaignBehaviorBase
    {
        public static ScriptedHistoryBehavior Instance { get; private set; }

        // Yellow/black striped field with the lion device (palette 84 = bright yellow,
        // 116 = near-black; icon 160 = the lion; background 7 = the striped field).
        private const string TipuBannerCode = "7.84.116.1536.1536.764.764.1.0.0.160.116.116.512.512.764.764.1.0.0";
        private const uint TipuYellow = 0xFFFDE217;
        private const uint TipuBlack = 0xFF0B0C11;

        private List<string> _done = new List<string>();
        private bool _tipuBannerRaised;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("History.Daily", OnDailyTick));
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("hind_hist_done", ref _done);
            dataStore.SyncData("hind_hist_tipu_banner", ref _tipuBannerRaised);
            if (!dataStore.IsSaving && _done == null) _done = new List<string>();
        }

        private void OnDailyTick()
        {
            if (!WorldGen.Ready) return;
            int gameYear = CampaignTime.Now.GetYear;
            foreach (var (year, id, _) in ScriptedHistory.Events)
            {
                if (_done.Contains(id) || !HistoricalCalendar.HasReached(gameYear, year)) continue;
                _done.Add(id);
                TYTLog.Guard("History." + id, () => Fire(id, year, gameYear));
            }
            if (!_tipuBannerRaised) WatchForTipu();
        }

        private static Kingdom K(string id) => Kingdom.All.FirstOrDefault(k => k.StringId == id && !k.IsEliminated);
        private static Hero H(string id) => Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id);

        private void Fire(string id, int adYear, int gameYear)
        {
            // An event loaded long after its year (an old save) gets its state skipped but
            // its place in the register kept — except pure restructures, which always run.
            bool stale = HistoricalCalendar.ToADYear(gameYear) > adYear + 4;

            switch (id)
            {
                case "mysore_house": MergeMysoreHouses(); break;

                case "deccan_war":
                {
                    Kingdom empire = K("empire"); Kingdom marathas = K("battania");
                    if (stale || empire == null || marathas == null || empire.IsAtWarWith(marathas)) break;
                    DeclareWarAction.ApplyByDefault(empire, marathas);
                    Akhbar("The Deccan War Does Not Die With Alamgir",
                        "The old Padishah is gone, but the war he fed for a quarter-century feeds itself now: the imperial " +
                        "columns and the Maratha bands still tear at each other across the ghats. The chauth is collected " +
                        "at lance-point; the granaries of the south count for both sides.");
                    break;
                }

                case "banda_rising":
                {
                    Kingdom empire = K("empire"); Kingdom misls = K("khuzait");
                    if (stale || empire == null || misls == null || empire.IsAtWarWith(misls)) break;
                    DeclareWarAction.ApplyByDefault(empire, misls);
                    Akhbar("The Panth Rises",
                        "Word rides from the north: a warrior they call Banda Singh Bahadur has raised the Sikh misls in " +
                        "arms, broken the faujdar of Sirhind, and struck coin in the name of the Gurus. The imperial court " +
                        "declares the Panth in rebellion. The five rivers will run red before this is settled.");
                    break;
                }

                case "deccan_treaty":
                {
                    Kingdom empire = K("empire"); Kingdom marathas = K("battania");
                    if (empire == null || marathas == null || !empire.IsAtWarWith(marathas)) break;
                    MakePeaceAction.Apply(empire, marathas);
                    Akhbar("The Treaty With The Marathas",
                        "The court's soldiers have made the peace the court's pride would not: by treaty, the Maratha " +
                        "collectors are conceded their fourth of the Deccan's revenue, and the imperial columns march home. " +
                        "Delhi calls it statecraft. The Deccan calls it what it is.");
                    break;
                }

                case "hyder_coup":
                {
                    Kingdom mysore = K("aserai");
                    Hero hyder = H("lord_3_5");
                    if (mysore == null || hyder == null || hyder.Clan == null || hyder.Clan.Kingdom != mysore
                        || mysore.RulingClan == hyder.Clan) break;
                    Clan wodeyar = mysore.RulingClan;
                    ChangeRulingClanAction.Apply(mysore, hyder.Clan);
                    if (wodeyar?.Leader != null)
                    {
                        CharacterRelationManager.SetHeroRelation(wodeyar.Leader, hyder, -60);
                        OpinionBehavior.Instance?.AddOpinion(wodeyar.Leader, hyder, OpinionMath.OpinionType.Grudge);
                    }
                    Akhbar("The Dalvai Takes Mysore",
                        $"From Srirangapatna: {RoyalFarmaan.NameWithHonorific(hyder)} has seized the government of Mysore. " +
                        "The Wodeyar Maharaja keeps his palace, his Dasara, and his dignities — and not one soldier, one " +
                        "rupee, or one word of the state's business. The Gaddi remains; the power has moved.");
                    break;
                }

                case "bajirao_delhi":
                {
                    Kingdom empire = K("empire"); Kingdom marathas = K("battania");
                    if (stale || empire == null || marathas == null || empire.IsAtWarWith(marathas)) break;
                    DeclareWarAction.ApplyByDefault(marathas, empire);
                    Akhbar("Maratha Horse At The Gates Of Delhi",
                        "The Peshwa has done what Delhi swore no enemy could: ridden to the suburbs of the imperial capital " +
                        "itself, burned the outposts within sight of the walls, and ridden away before the armies sent " +
                        "against him had finished forming their line. The court is shaken to its marrow.");
                    break;
                }

                case "nadir_sack":
                {
                    Kingdom empire = K("empire");
                    if (empire == null) break;
                    ImperialAuthorityBehavior.Instance?.ModifyAuthority(empire, -15f, "the sack of Delhi");
                    if (empire.Leader != null && empire.Leader.Gold > 100000)
                        empire.Leader.ChangeHeroGold(-(int)(empire.Leader.Gold * 0.4f));
                    Akhbar("NADIR SHAH TAKES DELHI",
                        "The worst has happened. The Shah of Persia has broken the imperial army at Karnal, entered " +
                        "Shahjahanabad, and — after the qatl-e-aam, the general slaughter — carried away the treasure of " +
                        "nine reigns: the Peacock Throne itself, the great diamonds, the accumulated silver of Hindostan. " +
                        "The empire's name will never again mean what it meant.");
                    break;
                }

                case "durrani_rise":
                {
                    Kingdom afghans = K("sturgia"); Kingdom misls = K("khuzait");
                    Akhbar("The Durrani Proclaimed At Kandahar",
                        "The Afghan jirga has raised Ahmad Shah of the Sadozai upon the throne at Kandahar, and the tribes " +
                        "have taken his salt. The passes are his; the plunder of Hindostan is preached to the lashkars as " +
                        "both destiny and wages. The Panjab will feel it first.");
                    if (!stale && afghans != null && misls != null && !afghans.IsAtWarWith(misls))
                        DeclareWarAction.ApplyByDefault(afghans, misls);
                    break;
                }
            }
        }

        // ── The Mysore restructure (heals old saves; new campaigns get the XML wiring) ──
        private void MergeMysoreHouses()
        {
            Hero tipu = H("lord_3_3");
            Hero hyder = H("lord_3_5");
            if (tipu == null || hyder == null || hyder.Clan == null || tipu.Clan == hyder.Clan) return;

            Clan kalale = tipu.Clan;
            // A clan must not lose its leader without a successor: seat the Kalale line first.
            if (kalale != null && kalale.Leader == tipu)
            {
                Hero successor = H("lord_3_20") ?? kalale.Heroes.FirstOrDefault(h =>
                    h != tipu && h.IsAlive && !h.IsChild && !h.IsFemale && h.Clan == kalale);
                if (successor != null && successor.Clan == kalale)
                    ChangeClanLeaderAction.ApplyWithSelectedNewLeader(kalale, successor);
            }

            foreach (string id in new[] { "lord_3_3", "lord_3_4", "lord_3_8", "lord_3_11", "lord_3_3_1" })
            {
                Hero h = H(id);
                if (h != null && h.Clan != hyder.Clan) h.Clan = hyder.Clan;
            }
            TYTLog.Info("History: Tipu's line folded into Hyder Ali's house (mysore_house).");
        }

        // ── The lion standard ────────────────────────────────────────────────────────
        private void WatchForTipu()
        {
            Kingdom mysore = K("aserai");
            Hero tipu = H("lord_3_3");
            if (mysore == null || tipu == null || mysore.Leader != tipu) return;
            _tipuBannerRaised = true;
            TYTLog.Guard("History.TipuBanner", () => RaiseTipuBanner(mysore));
        }

        private void RaiseTipuBanner(Kingdom mysore)
        {
            ApplyBanner(mysore, TipuBannerCode, TipuYellow, TipuBlack);
            Akhbar("The Lion Standard Over Mysore",
                $"{RoyalFarmaan.NameWithHonorific(mysore.Leader)} ascends the musnud of Mysore, and over Srirangapatna a " +
                "new standard breaks: yellow and black, bearing the lion. The old dispensation of the south is ended; " +
                "what replaces it intends to be feared.");
            TYTLog.Info("History: Tipu's lion standard raised over Mysore.");
        }

        private static void ApplyBanner(Kingdom k, string bannerCode, uint color1, uint color2)
        {
            try
            {
                k.Banner = new Banner(bannerCode);
                // Color / Color2 / PrimaryBannerColor / SecondaryBannerColor have private
                // setters; the map and UI read them for nameplates and shields.
                SetProp(k, "Color", color1);
                SetProp(k, "Color2", color2);
                SetProp(k, "PrimaryBannerColor", color1);
                SetProp(k, "SecondaryBannerColor", color2);
                foreach (Clan c in k.Clans.Where(c => c == k.RulingClan))
                { c.Color = color1; c.Color2 = color2; }
            }
            catch (Exception e) { TYTLog.Error("History: banner change failed", e); }
        }

        private static void SetProp(object target, string name, uint value)
        {
            PropertyInfo p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite) { p.SetValue(target, value); return; }
            MethodInfo setter = p?.GetSetMethod(true);
            if (setter != null) setter.Invoke(target, new object[] { value });
        }

        private static void Akhbar(string title, string body)
            => RoyalFarmaan.Issue(title, "AKHBAR — the newsletter of the age", body,
                seal: "Set down " + RoyalFarmaan.CurrentDate(),
                primary: "So the age turns", priority: FarmaanPriority.Ceremonial);

        // ── Console (testing) ────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("history_status", "hindostan")]
        public static string HistoryStatus(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            int gy = CampaignTime.Now.GetYear;
            return $"Year: {HistoricalCalendar.ToADYear(gy)} AD ({HistoricalCalendar.HijriYear(HistoricalCalendar.ToADYear(gy))} AH)\n"
                 + string.Join("\n", ScriptedHistory.Events.Select(e =>
                     $"{e.Year}  {e.Id,-14} {(Instance._done.Contains(e.Id) ? "DONE" : HistoricalCalendar.HasReached(gy, e.Year) ? "DUE" : "pending")}"))
                 + $"\nTipu banner: {(Instance._tipuBannerRaised ? "raised" : "watching")}";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("history_fire", "hindostan")]
        public static string HistoryFire(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (args == null || args.Count < 1) return "Usage: hindostan.history_fire <event id>";
            var ev = ScriptedHistory.Events.FirstOrDefault(e => e.Id == args[0]);
            if (ev.Id == null) return $"No event '{args[0]}'. Ids: " + string.Join(", ", ScriptedHistory.Events.Select(e => e.Id));
            Instance._done.Remove(ev.Id);
            Instance._done.Add(ev.Id);
            Instance.Fire(ev.Id, ev.Year, HistoricalCalendar.ToGameYear(ev.Year)); // fire as if on time
            return $"Fired {ev.Id}.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("mysore_banner", "hindostan")]
        public static string MysoreBanner(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            Kingdom mysore = K("aserai");
            if (mysore == null) return "Mysore is not standing.";
            string code = args != null && args.Count > 0 ? args[0] : TipuBannerCode;
            ApplyBanner(mysore, code, TipuYellow, TipuBlack);
            return "Mysore banner applied: " + code + " (pass a banner code to experiment).";
        }
    }
}
