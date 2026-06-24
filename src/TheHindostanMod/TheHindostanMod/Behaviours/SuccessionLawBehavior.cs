using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TakhtyaTaboot.Config;
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // Succession law (design doc §C) — the per-kingdom constitution that drives SuccessionBehavior's
    // war-of-princes engine. This thin, guarded behavior owns the law + named-heir state and the engine
    // actions; every rule and curve lives in the pure SuccessionLawMath. Mirrors MansabdariTenureBehavior.
    //
    //   • Each kingdom carries a SuccessionLaw (seeded historically at new-game; Undeclared if unknown).
    //   • The sovereign may PROCLAIM a new law — a legitimacy/influence-gated darbar edict that angers the
    //     princes or the magnates depending on the transition.
    //   • Under AppointedHeir the sovereign NAMES a Wali Ahd (+ optional Naib Wali Ahd fallback); naming an
    //     heir raises his claim-support in a live crisis and steers future successions.
    //   • AI sovereigns adopt a law by dynasty size and name their strongest son.
    public class SuccessionLawBehavior : CampaignBehaviorBase
    {
        public static SuccessionLawBehavior Instance { get; private set; }

        // Per-kingdom law (parallel lists; an explicit entry — even Undeclared — marks a kingdom as seeded).
        private List<string> _lawKingdomIds = new List<string>();
        private List<int>    _laws          = new List<int>();

        // Appointed heirs (parallel lists; kingdomId -> heroId).
        private List<string> _waliKingdomIds = new List<string>();
        private List<string> _waliHeroIds    = new List<string>();
        private List<string> _naibKingdomIds = new List<string>();
        private List<string> _naibHeroIds    = new List<string>();

        // Day each AI realm last reconsidered its law (kingdomId -> day).
        private List<string> _reviewKingdomIds = new List<string>();
        private List<int>    _reviewDays        = new List<int>();

        private bool _ready;

        // Historical seed by kingdom StringId (cultures are shared, so key by realm). Anything unseeded
        // falls back by culture, then to Undeclared.
        private static readonly Dictionary<string, SuccessionLaw> SeedByKingdom = new Dictionary<string, SuccessionLaw>
        {
            { "empire",   SuccessionLaw.Undeclared },        // Mughals — takht-ya-taboot open contest
            { "empire_w", SuccessionLaw.AppointedHeir },     // Bengal — Nawabi designation
            { "empire_s", SuccessionLaw.AppointedHeir },     // Hyderabad — the Nizam's Wali Ahd
            { "sturgia",  SuccessionLaw.MagnateElection },   // Durrani Afghans — the jirga
            { "aserai",   SuccessionLaw.MalePrimogeniture }, // Mysore
            { "vlandia",  SuccessionLaw.MalePrimogeniture }, // Rajputs
            { "battania", SuccessionLaw.MalePrimogeniture }, // Marathas — Chhatrapati hereditary
            { "khuzait",  SuccessionLaw.MagnateElection },   // Sikhs — Sarbat Khalsa / misl confederacy
        };

        private static readonly Dictionary<string, SuccessionLaw> SeedByCulture = new Dictionary<string, SuccessionLaw>
        {
            { "empire",   SuccessionLaw.Undeclared },
            { "sturgia",  SuccessionLaw.MagnateElection },
            { "aserai",   SuccessionLaw.MalePrimogeniture },
            { "vlandia",  SuccessionLaw.MalePrimogeniture },
            { "battania", SuccessionLaw.MalePrimogeniture },
            { "khuzait",  SuccessionLaw.MagnateElection },
        };

        // ── Lifecycle ─────────────────────────────────────────────────────────────────
        public override void RegisterEvents()
        {
            Instance = this;
            // Seed at OnSessionLaunched only (post-world-gen, single-threaded). We deliberately do NOT hook
            // OnNewGameCreatedEvent: it fires while world-gen is still building factions on parallel threads,
            // and iterating Kingdom.All there is the same native-crash race as MansabdariBehavior hit.
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ => { _ready = true; Util.TYTLog.Guard("SuccLaw.Seed", SeedMissing); });
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, () => { if (_ready) Util.TYTLog.Guard("SuccLaw.Weekly", OnWeeklyTick); });
        }

        // Seed any kingdom that has no explicit law entry yet (new game, old save, fresh rebel realm).
        private void SeedMissing()
        {
            foreach (Kingdom k in Kingdom.All.Where(k => !k.IsEliminated))
            {
                if (_lawKingdomIds.Contains(k.StringId)) continue;
                SetLaw(k, SeedLawFor(k));
            }
        }

        private static SuccessionLaw SeedLawFor(Kingdom k)
        {
            if (k == null) return SuccessionLaw.Undeclared;
            if (SeedByKingdom.TryGetValue(k.StringId, out var byId)) return byId;
            string cul = k.Culture?.StringId;
            if (cul != null && SeedByCulture.TryGetValue(cul, out var byCul)) return byCul;
            return SuccessionLaw.Undeclared;
        }

        // ── Law queries ──────────────────────────────────────────────────────────────
        public SuccessionLaw GetLaw(Kingdom k)
        {
            int i = k == null ? -1 : _lawKingdomIds.IndexOf(k.StringId);
            return i < 0 ? SuccessionLaw.Undeclared : (SuccessionLaw)_laws[i];
        }

        private void SetLaw(Kingdom k, SuccessionLaw law)
        {
            if (k == null) return;
            int i = _lawKingdomIds.IndexOf(k.StringId);
            if (i >= 0) _laws[i] = (int)law;
            else { _lawKingdomIds.Add(k.StringId); _laws.Add((int)law); }
        }

        public Hero GetWaliAhd(Kingdom k) => ValidHeir(k, _waliKingdomIds, _waliHeroIds);
        public Hero GetNaib(Kingdom k)    => ValidHeir(k, _naibKingdomIds, _naibHeroIds);

        // A named heir counts only while alive, adult, male, and still of the realm.
        private static Hero ValidHeir(Kingdom k, List<string> kIds, List<string> hIds)
        {
            int i = k == null ? -1 : kIds.IndexOf(k.StringId);
            if (i < 0) return null;
            Hero h = FindHero(hIds[i]);
            if (h == null || !h.IsAlive || h.IsChild || h.IsFemale) return null;
            if (h.Clan?.Kingdom != k) return null;
            return h;
        }

        private void SetHeir(Kingdom k, Hero heir, List<string> kIds, List<string> hIds)
        {
            if (k == null) return;
            int i = kIds.IndexOf(k.StringId);
            if (heir == null)
            {
                if (i >= 0) { kIds.RemoveAt(i); hIds.RemoveAt(i); }
                return;
            }
            if (i >= 0) hIds[i] = heir.StringId;
            else { kIds.Add(k.StringId); hIds.Add(heir.StringId); }
        }

        // The candidate the law would crown without a contest (used by the clean-accession path).
        public Hero LawfulHeir(Kingdom k)
        {
            if (k?.Leader == null) return null;
            switch (GetLaw(k))
            {
                case SuccessionLaw.AppointedHeir:
                    return GetWaliAhd(k) ?? GetNaib(k) ?? EldestSon(k.Leader);
                case SuccessionLaw.MalePrimogeniture:
                    return EldestSon(k.Leader);
                default:
                    return null; // election / undeclared: never a "clean" heir — the court decides
            }
        }

        // ── Weekly: validate heirs + AI adoption ───────────────────────────────────────
        private void OnWeeklyTick()
        {
            // Promote a fallen Wali Ahd's Naib; drop heirs who are no longer valid.
            foreach (Kingdom k in Kingdom.All.Where(k => !k.IsEliminated && k.Leader != null))
            {
                if (GetWaliAhd(k) == null)
                {
                    Hero naib = GetNaib(k);
                    if (naib != null)
                    {
                        SetHeir(k, naib, _waliKingdomIds, _waliHeroIds);
                        SetHeir(k, null, _naibKingdomIds, _naibHeroIds);
                        if (k.Leader != Hero.MainHero && Hero.MainHero?.Clan?.Kingdom == k)
                            RoyalFarmaan.FromRuler(k, "The Deputy Heir Rises",
                                $"The Wali Ahd of {k.Name} is no more. By the standing law, the Naib Wali Ahd {naib.Name} " +
                                "is raised to heir apparent in his place.", "So the law provides");
                    }
                    else if (_waliKingdomIds.Contains(k.StringId))
                        SetHeir(k, null, _waliKingdomIds, _waliHeroIds);
                }
            }

            // AI sovereigns reconsider their law and name an heir on a slow cadence.
            int today = (int)CampaignTime.Now.ToDays;
            int interval = Math.Max(30, Tune.AiLawReviewIntervalDays);
            foreach (Kingdom k in Kingdom.All.Where(k => !k.IsEliminated && k.Leader != null && k.Leader != Hero.MainHero))
            {
                int ri = _reviewKingdomIds.IndexOf(k.StringId);
                int last = ri >= 0 ? _reviewDays[ri] : -interval; // due immediately the first time
                if (today - last < interval) continue;
                if (ri >= 0) _reviewDays[ri] = today; else { _reviewKingdomIds.Add(k.StringId); _reviewDays.Add(today); }
                Util.TYTLog.Guard("SuccLaw.AiReview", () => AiReview(k));
            }
        }

        private void AiReview(Kingdom k)
        {
            float legit = LegitimacyBehavior.Instance?.GetLegitimacy(k.Leader) ?? 60f;
            float auth = ImperialAuthorityBehavior.Instance?.GetAuthority(k) ?? 75f;
            bool fragmented = Kingdom.All.Count(o => o != k && !o.IsEliminated && k.IsAtWarWith(o)) >= 2;
            int princes = LivingPrinces(k.Leader);
            SuccessionLaw current = GetLaw(k);

            // A realm's historical law is deliberate and sticky: the AI only steps in for a realm that
            // was never seeded (a mid-game rebel) or whose dynasty-based law has become unworkable because
            // no prince of the blood survives to inherit.
            bool seeded = _lawKingdomIds.Contains(k.StringId);
            bool dynasticLaw = current == SuccessionLaw.MalePrimogeniture
                            || current == SuccessionLaw.PrincelyElection
                            || current == SuccessionLaw.AppointedHeir;
            bool unworkable = dynasticLaw && princes == 0;

            if (!seeded || unworkable)
            {
                SuccessionLaw want = SuccessionLawMath.ChooseLawForAi(princes, auth, legit, fragmented);
                if (want != current && SuccessionLawMath.MeetsLegitimacyFloor(legit, Tune.SuccLawLegitimacyFloor))
                {
                    ApplyAngeredEstate(k, SuccessionLawMath.WhoIsAngered(current, want), announce: false);
                    SetLaw(k, want);
                }
                else if (!seeded) SetLaw(k, current); // mark a rebel realm seeded even if it keeps Undeclared
            }

            // Under appointment, keep an heir named (strongest adult son, else clan heir).
            if (GetLaw(k) == SuccessionLaw.AppointedHeir && GetWaliAhd(k) == null)
            {
                Hero pick = EldestSon(k.Leader) ?? DynastyPrinces(k.Leader).FirstOrDefault();
                if (pick != null) SetHeir(k, pick, _waliKingdomIds, _waliHeroIds);
            }
        }

        // ── Proclaim a new law (player edict) ──────────────────────────────────────────
        public struct LawQuote
        {
            public bool Allowed;
            public string Reason;
            public int Influence;
            public AngeredEstate Angers;
        }

        public LawQuote QuoteLawChange(Kingdom k, SuccessionLaw newLaw)
        {
            var q = new LawQuote();
            if (k?.Leader == null) { q.Reason = "No sovereign to issue the edict."; return q; }
            if (newLaw == GetLaw(k)) { q.Reason = $"{k.Name} already holds to that law of succession."; return q; }

            float legit = LegitimacyBehavior.Instance?.GetLegitimacy(k.Leader) ?? 50f;
            float floor = Tune.SuccLawLegitimacyFloor;
            q.Angers = SuccessionLawMath.WhoIsAngered(GetLaw(k), newLaw);
            int affected = q.Angers == AngeredEstate.Magnates ? GreatHouses(k) : LivingPrinces(k.Leader);
            q.Influence = SuccessionLawMath.LawChangeInfluenceCost(Tune.SuccLawBaseInfluence, affected, legit);

            if (!SuccessionLawMath.MeetsLegitimacyFloor(legit, floor))
            { q.Reason = $"Your legitimacy ({legit:0}) falls short of the {floor:0} needed to rewrite the law of succession."; return q; }

            q.Allowed = true;
            q.Reason = q.Angers == AngeredEstate.None
                ? "The court will accept the change."
                : (q.Angers == AngeredEstate.Princes ? "The princes will resent losing their certainty." : "The magnates will resent losing their voice.");
            return q;
        }

        public bool TryEnactLawChange(Kingdom k, SuccessionLaw newLaw, out string reason)
        {
            reason = "";
            if (k?.Leader == null) { reason = "No sovereign to issue the edict."; return false; }
            LawQuote q = QuoteLawChange(k, newLaw);
            if (!q.Allowed) { reason = q.Reason; return false; }

            Clan ruling = k.RulingClan;
            if (ruling == null) { reason = "The realm has no ruling clan."; return false; }
            if (ruling.Influence < q.Influence)
            { reason = $"The court needs {q.Influence} influence for this edict (you have {ruling.Influence:0})."; return false; }

            try
            {
                ChangeClanInfluenceAction.Apply(ruling, -q.Influence);
                SuccessionLaw old = GetLaw(k);
                SetLaw(k, newLaw);
                ApplyAngeredEstate(k, q.Angers, announce: true);
                // A law that is no longer appointment voids any standing heir.
                if (newLaw != SuccessionLaw.AppointedHeir)
                {
                    SetHeir(k, null, _waliKingdomIds, _waliHeroIds);
                    SetHeir(k, null, _naibKingdomIds, _naibHeroIds);
                }
                Util.TYTLog.Info($"SuccLaw: {k.Name} {old} -> {newLaw} (inf {q.Influence}, angers {q.Angers}).");
            }
            catch (Exception e) { Util.TYTLog.Error("TryEnactLawChange failed", e); reason = "The edict faltered in the court."; return false; }

            RoyalFarmaan.FromRuler(k, "Edict of Succession", LawProclamation(k, newLaw), "As the throne commands");
            return true;
        }

        private static string LawProclamation(Kingdom k, SuccessionLaw law)
        {
            switch (law)
            {
                case SuccessionLaw.MalePrimogeniture:
                    return $"By imperial edict, the throne of {k.Name} shall pass by male primogeniture — to the eldest son of the reigning house, that the succession be never in doubt.";
                case SuccessionLaw.PrincelyElection:
                    return $"By imperial edict, on each accession the princes of {k.Name} shall stand, and the great lords of the realm shall raise one of them by their voices.";
                case SuccessionLaw.MagnateElection:
                    return $"By imperial edict, the crown of {k.Name} shall be elective: on each accession the assembled magnates shall raise the worthiest among the great houses — be he of the blood or not.";
                case SuccessionLaw.AppointedHeir:
                    return $"By imperial edict, the sovereign of {k.Name} shall name his own Wali Ahd to succeed him, and may name a Naib Wali Ahd to follow should the first heir fall. Name your heir, that the realm know its future.";
                default:
                    return $"By imperial edict, {k.Name} declares no fixed law of succession — the throne shall be contested as fate and the great lords decide.";
            }
        }

        // Princes wounded by a loosened grip lose relation with the crown; affronted magnates dent imperial
        // authority and cool toward the throne.
        private void ApplyAngeredEstate(Kingdom k, AngeredEstate who, bool announce)
        {
            if (k?.Leader == null || who == AngeredEstate.None) return;
            Hero ruler = k.Leader;
            if (who == AngeredEstate.Princes)
            {
                foreach (Hero p in DynastyPrinces(ruler))
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(ruler, p, -8);
                ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -3f, "the princes resent the new succession law");
            }
            else // Magnates
            {
                foreach (Clan c in k.Clans.Where(c => !c.IsEliminated && c.Leader != null && c.Leader != ruler && c != k.RulingClan && !c.IsMinorFaction))
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(ruler, c.Leader, -6);
                ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -5f, "the magnates resent the new succession law");
            }
        }

        // ── Name an heir (Wali Ahd / Naib) ─────────────────────────────────────────────
        public bool AppointWaliAhd(Kingdom k, Hero heir, out string reason)
        {
            reason = "";
            if (k?.Leader == null) { reason = "No sovereign."; return false; }
            if (GetLaw(k) != SuccessionLaw.AppointedHeir) { reason = "Only under the law of the appointed Wali Ahd may you name an heir."; return false; }
            if (heir == null || !heir.IsAlive || heir.IsChild) { reason = "That heir is not eligible."; return false; }
            SetHeir(k, heir, _waliKingdomIds, _waliHeroIds);
            if (GetNaib(k) == heir) SetHeir(k, null, _naibKingdomIds, _naibHeroIds);
            SuccessionBehavior.Instance?.NoteHeirNamed(k, heir);
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(k.Leader, heir, 5);
            if (k.Leader != Hero.MainHero || Hero.MainHero?.Clan?.Kingdom == k)
                RoyalFarmaan.FromRuler(k, "A Wali Ahd Is Named",
                    $"Let it be known that {heir.Name} is named Wali Ahd — heir apparent to the throne of {k.Name}. " +
                    "All lords are called to know him as the sovereign's chosen successor.", "I know the heir");
            return true;
        }

        public bool AppointNaib(Kingdom k, Hero heir, out string reason)
        {
            reason = "";
            if (k?.Leader == null) { reason = "No sovereign."; return false; }
            if (GetLaw(k) != SuccessionLaw.AppointedHeir) { reason = "Only under the law of the appointed Wali Ahd may you name a deputy heir."; return false; }
            if (heir == null || !heir.IsAlive || heir.IsChild) { reason = "That heir is not eligible."; return false; }
            if (GetWaliAhd(k) == heir) { reason = "He is already your Wali Ahd."; return false; }
            SetHeir(k, heir, _naibKingdomIds, _naibHeroIds);
            return true;
        }

        // ── Helpers ────────────────────────────────────────────────────────────────────
        private static Hero EldestSon(Hero ruler)
            => ruler?.Children?.Where(c => !c.IsFemale && c.IsAlive && !c.IsChild)
                    .OrderByDescending(c => c.Age).FirstOrDefault();

        // Adult male kin who could press a claim: sons, brothers, nephews of the reigning house.
        private static List<Hero> DynastyPrinces(Hero ruler)
        {
            var seen = new HashSet<Hero>();
            var outp = new List<Hero>();
            void Add(Hero h) { if (h != null && h.IsAlive && !h.IsChild && !h.IsFemale && h != ruler && seen.Add(h)) outp.Add(h); }
            if (ruler == null) return outp;
            foreach (Hero s in ruler.Children.Where(c => !c.IsFemale)) Add(s);
            var brothers = (ruler.Father?.Children ?? new List<Hero>()).Where(b => b != ruler && !b.IsFemale).ToList();
            foreach (Hero b in brothers) Add(b);
            foreach (Hero b in brothers) foreach (Hero n in b.Children.Where(c => !c.IsFemale)) Add(n);
            return outp.OrderByDescending(h => h.Age).ToList();
        }

        private static int LivingPrinces(Hero ruler) => DynastyPrinces(ruler).Count;

        // The adult male kin a sovereign may name as Wali Ahd / Naib.
        public List<Hero> EligibleHeirs(Kingdom k) => k?.Leader == null ? new List<Hero>() : DynastyPrinces(k.Leader);

        private static int GreatHouses(Kingdom k)
            => k?.Clans?.Count(c => !c.IsEliminated && c.Leader != null && c.Leader != k.Leader && c != k.RulingClan && !c.IsMinorFaction) ?? 0;

        private static Hero FindHero(string id)
            => string.IsNullOrEmpty(id) ? null
             : (Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id)
                ?? Hero.DeadOrDisabledHeroes.FirstOrDefault(h => h.StringId == id));

        public static string LawName(SuccessionLaw law) => law switch
        {
            SuccessionLaw.MalePrimogeniture => "Male Primogeniture",
            SuccessionLaw.PrincelyElection  => "Election Among the Princes",
            SuccessionLaw.MagnateElection   => "Open Magnate Election",
            SuccessionLaw.AppointedHeir     => "Appointed Wali Ahd",
            _                               => "Undeclared (Open Contest)",
        };

        // ── Save / load ────────────────────────────────────────────────────────────────
        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("succlaw_kingdoms",   ref _lawKingdomIds);
            dataStore.SyncData("succlaw_laws",       ref _laws);
            dataStore.SyncData("succlaw_waliK",      ref _waliKingdomIds);
            dataStore.SyncData("succlaw_waliH",      ref _waliHeroIds);
            dataStore.SyncData("succlaw_naibK",      ref _naibKingdomIds);
            dataStore.SyncData("succlaw_naibH",      ref _naibHeroIds);
            dataStore.SyncData("succlaw_reviewK",    ref _reviewKingdomIds);
            dataStore.SyncData("succlaw_reviewDays", ref _reviewDays);
        }

        // ── Console (testing) ────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("succlaw", "hindostan")]
        public static string SuccLawStatus(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            var sb = new StringBuilder();
            foreach (Kingdom k in Kingdom.All.Where(k => !k.IsEliminated && k.Leader != null).Take(12))
            {
                string heir = Instance.GetWaliAhd(k)?.Name?.ToString();
                string naib = Instance.GetNaib(k)?.Name?.ToString();
                sb.AppendLine($"{k.Name}: {LawName(Instance.GetLaw(k))}" +
                              (heir != null ? $" — Wali Ahd {heir}" : "") + (naib != null ? $", Naib {naib}" : ""));
            }
            return sb.ToString();
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("succlaw_set", "hindostan")]
        public static string SuccLawSet(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            Kingdom k = Clan.PlayerClan?.Kingdom;
            if (k == null) return "You serve no realm.";
            string name = args != null && args.Count > 0 ? args[0] : "";
            if (!Enum.TryParse(name, true, out SuccessionLaw law))
                return "Usage: hindostan.succlaw_set <Undeclared|MalePrimogeniture|PrincelyElection|MagnateElection|AppointedHeir>";
            return Instance.TryEnactLawChange(k, law, out string reason)
                ? $"{k.Name} adopts {LawName(law)}." : "Failed: " + reason;
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("name_heir", "hindostan")]
        public static string NameHeir(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            Kingdom k = Clan.PlayerClan?.Kingdom;
            if (k?.Leader == null) return "You serve no realm.";
            Hero pick = EldestSon(k.Leader) ?? DynastyPrinces(k.Leader).FirstOrDefault();
            if (pick == null) return "There is no eligible prince to name.";
            return Instance.AppointWaliAhd(k, pick, out string reason)
                ? $"{pick.Name} is named Wali Ahd of {k.Name}." : "Failed: " + reason;
        }
    }
}
