using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TakhtyaTaboot.UI;
using TakhtyaTaboot.Util;
using Kind = TakhtyaTaboot.Util.WarProgressMath.Kind;

namespace TakhtyaTaboot
{
    // WAR, AND WHY IT IS FOUGHT (wiki ch.30). Rewritten from the old player-only scorer.
    //
    // Every war on the map — the player's and the AI's alike — is a RECORD: an aggressor, a defender, an
    // AIM, and (for a war of conquest) the named fiefs it was declared FOR. The aim is fixed at declaration
    // and it IS the win condition:
    //
    //   • ProvincialConquest — take the named fiefs. Nothing else will do, and no score, however crushing,
    //     substitutes for them. On victory each fief passes to the HOUSE whose claim was acted upon
    //     (under Feudal law directly; under Mansabdari through the crown's channel).
    //   • Tribute / Revenge  — beat them decisively, then dictate an indemnity, a tributary yoke, or the
    //     surrender of the lord who wronged you.
    //   • TotalSubjugation   — swallow the realm entire. Either their throne collapses (the gate), or every
    //     last fief falls. The defeated houses are ABSORBED, not scattered — carrying a bitter, decaying
    //     grudge (OpinionType.Subjugated).
    //   • The defender chooses NOTHING: his aim is to deny the aggressor's.
    //
    // Score is no longer an anonymous float. It is an ITEMIZED LEDGER of named contributions, because the
    // player can hover the war's progress bar and be told exactly what moved it (WarProgressMath).
    //
    // THRONE WARS ARE NOT THIS SYSTEM'S BUSINESS. A hind_rebel_* war is binary and settles by its own
    // deadline; the aim of a war for the crown is the crown. The old code offered "choose your war aim:
    // conquest/tribute/chastisement" in the middle of a civil war. It no longer does.
    public class WarfareBehavior : CampaignBehaviorBase
    {
        private enum Term { WhitePeace, DemandNazrana, DemandTargets, MakeTributary, OfferNazrana, Subjugate, SurrenderCulprit }

        public static WarfareBehavior Instance { get; private set; }

        private const int BannerCooldownDays = 14;
        private const int ForcedTruceDays    = 365 * 3;  // a dictated peace binds for three years
        private const int MaxConquestTargets = 3;        // a realm marches for a handful of fiefs, not a list

        // ── The wars ─────────────────────────────────────────────────────────────────
        private List<string> _wAgg       = new List<string>();
        private List<string> _wDef       = new List<string>();
        private List<int>    _wAim       = new List<int>();
        private List<string> _wTargets   = new List<string>();   // ';'-joined settlement ids
        private List<string> _wClaimants = new List<string>();   // ';'-joined clan ids, parallel to targets
        private List<int>    _wStart     = new List<int>();

        // ── The contribution ledger (what moved each war's score, and why) ───────────
        private List<string> _cWar   = new List<string>();   // "aggId|defId"
        private List<int>    _cDay   = new List<int>();
        private List<int>    _cKind  = new List<int>();
        private List<float>  _cDelta = new List<float>();
        private List<string> _cSubj  = new List<string>();

        // ── Truces (read by the WarDeclarationGate patch) ────────────────────────────
        private List<string> _trA     = new List<string>();
        private List<string> _trB     = new List<string>();
        private List<int>    _trUntil = new List<int>();

        // Tributaries the player's realm has imposed: tributaryKingdomId -> day the yoke lifts.
        private Dictionary<string, int> _tributaryUntil = new Dictionary<string, int>();

        private int _lastBannerDay = -100;
        private bool _ready;
        private bool _applyingTerms;
        private readonly HashSet<string> _peaceUrged = new HashSet<string>();
        private readonly HashSet<string> _completionOffered = new HashSet<string>();

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => TYTLog.Guard("Warfare.DailyTick", OnDailyTick));
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnMakePeace);
            CampaignEvents.OnPlayerBattleEndEvent.AddNonSerializedListener(this, OnPlayerBattleEnd);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, e => TYTLog.GuardQuiet("Warfare.MapEvent", () => OnAnyBattleEnd(e)));
            CampaignEvents.TournamentFinished.AddNonSerializedListener(this, OnTournamentFinished);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnOwnerChanged);
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, OnPrisonerTaken);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            // A realm being swallowed must not be scattered by vanilla the moment its last fief falls —
            // we need it alive to absorb its houses (ch.30 §4).
            CampaignEvents.CanKingdomBeDiscontinuedEvent.AddNonSerializedListener(this, OnCanKingdomBeDiscontinued);
        }

        private static Kingdom PK => Hero.MainHero?.Clan?.Kingdom;
        private static Kingdom Find(string id) => Kingdom.All.FirstOrDefault(k => k.StringId == id);
        private static bool IsRuler => PK != null && PK.Leader == Hero.MainHero;
        private static int Today => (int)CampaignTime.Now.ToDays;

        // A war this layer owns: two real realms. A throne war is binary and settles by its own rules.
        private static bool Tracked(Kingdom a, Kingdom b)
            => a != null && b != null && a != b && !a.IsEliminated && !b.IsEliminated
               && !ThroneWar.IsRebelKingdom(a) && !ThroneWar.IsRebelKingdom(b);

        // ── The war record ───────────────────────────────────────────────────────────
        private int FindWar(Kingdom a, Kingdom b)
        {
            if (a == null || b == null) return -1;
            for (int i = 0; i < _wAgg.Count; i++)
            {
                if (_wAgg[i] == a.StringId && _wDef[i] == b.StringId) return i;
                if (_wAgg[i] == b.StringId && _wDef[i] == a.StringId) return i;
            }
            return -1;
        }

        private string WarKey(int i) => _wAgg[i] + "|" + _wDef[i];
        private WarAim AimAt(int i) => (WarAim)_wAim[i];
        public WarAim AimOf(Kingdom a, Kingdom b) { int i = FindWar(a, b); return i < 0 ? WarAim.Tribute : AimAt(i); }

        private List<string> TargetIds(int i)
            => string.IsNullOrEmpty(_wTargets[i]) ? new List<string>()
             : _wTargets[i].Split(';').Where(s => !string.IsNullOrEmpty(s)).ToList();

        private List<string> ClaimantIds(int i)
            => string.IsNullOrEmpty(_wClaimants[i]) ? new List<string>()
             : _wClaimants[i].Split(';').Where(s => !string.IsNullOrEmpty(s)).ToList();

        private static Settlement Settle(string id) => Settlement.All.FirstOrDefault(s => s.StringId == id);
        private static Clan ClanOf(string id) => Clan.All.FirstOrDefault(c => c.StringId == id);

        // ── Declaration: the aim is fixed here, and it is the win condition ──────────
        private void OnWarDeclared(IFaction f1, IFaction f2, DeclareWarAction.DeclareWarDetail detail)
            => TYTLog.Guard("Warfare.WarDeclared", () =>
            {
                // THE BUG THIS REWRITE KILLS: a hind_rebel_* breakaway IS a Kingdom, so the old code
                // cheerfully asked the player to pick "conquest / tribute / chastisement" in the middle
                // of his own civil war. The aim of a war for the throne is the throne.
                if (!(f1 is Kingdom agg) || !(f2 is Kingdom def)) return;
                if (!Tracked(agg, def)) return;
                if (FindWar(agg, def) >= 0) return;

                // War with a tributary breaks the pact.
                if (_tributaryUntil.Remove(def.StringId) || _tributaryUntil.Remove(agg.StringId))
                    if (PK == agg || PK == def)
                        RoyalFarmaan.FromRuler(PK, "The Tributary Yoke Is Cast Off",
                            "The pact of tribute is broken: the realms are at war once more, and no further nazrana will flow.",
                            "So be it");

                OpenWar(agg, def, WarAim.Tribute, new List<Settlement>(), new List<Clan>());

                // The player's own realm, and he wears the crown: HE names the aim.
                if (_ready && agg == PK && IsRuler) { OfferAimChoice(def); return; }

                // Everyone else — the AI, and the player as a mere vassal — gets the aim assigned from
                // the standing claims (ch.30 §7.10, the cheap path: vanilla decides WHETHER to fight,
                // we decide WHAT FOR).
                AssignAimFromClaims(agg, def);
            });

        private void OpenWar(Kingdom agg, Kingdom def, WarAim aim, List<Settlement> targets, List<Clan> claimants)
        {
            _wAgg.Add(agg.StringId); _wDef.Add(def.StringId); _wAim.Add((int)aim);
            _wTargets.Add(string.Join(";", targets.Select(s => s.StringId)));
            _wClaimants.Add(string.Join(";", claimants.Select(c => c.StringId)));
            _wStart.Add(Today);
            _peaceUrged.Remove(def.StringId);
            _completionOffered.Remove(agg.StringId + "|" + def.StringId);
        }

        private void SetAim(int i, WarAim aim, List<Settlement> targets, List<Clan> claimants)
        {
            _wAim[i] = (int)aim;
            _wTargets[i] = string.Join(";", targets.Select(s => s.StringId));
            _wClaimants[i] = string.Join(";", claimants.Select(c => c.StringId));
        }

        // What a realm actually has cause to demand of another. This is the AI's aim, and it is also
        // the honest menu the player is offered.
        private void AssignAimFromClaims(Kingdom agg, Kingdom def)
        {
            int i = FindWar(agg, def);
            if (i < 0) return;

            var claims = ClaimsBehavior.Instance?.ClaimTargets(agg, def) ?? new List<(Settlement, Clan, float)>();
            if (claims.Count > 0)
            {
                var take = claims.Take(MaxConquestTargets).ToList();
                SetAim(i, WarAim.ProvincialConquest,
                       take.Select(t => t.Item1).ToList(), take.Select(t => t.Item2).ToList());
                TYTLog.Info($"Warfare: {agg.Name} wars on {def.Name} for {string.Join(", ", take.Select(t => t.Item1.Name))} (claims).");
                return;
            }

            // No claim, but a grievance: a war of chastisement.
            if (Util.WarAimsBehavior.Instance != null && Util.WarAimsBehavior.Instance.HasCasusBelli(agg, def, out _))
            { SetAim(i, WarAim.Revenge, new List<Settlement>(), new List<Clan>()); return; }

            // Naked ambition: gold, then.
            SetAim(i, WarAim.Tribute, new List<Settlement>(), new List<Clan>());
        }

        private void OfferAimChoice(Kingdom ok)
        {
            Kingdom pk = PK;
            var claims = ClaimsBehavior.Instance?.ClaimTargets(pk, ok) ?? new List<(Settlement, Clan, float)>();
            bool hasAffront = Util.WarAimsBehavior.Instance != null
                              && Util.WarAimsBehavior.Instance.HasCasusBelli(pk, ok, out _);

            var elements = new List<InquiryElement>();

            if (claims.Count > 0)
            {
                var take = claims.Take(MaxConquestTargets).ToList();
                string names = string.Join(", ", take.Select(t => $"{t.Item1.Name} (for {t.Item2.Name}, {t.Item3:0.0} yrs)"));
                elements.Add(new InquiryElement(WarAim.ProvincialConquest,
                    $"Conquest — press our claims: {string.Join(", ", take.Select(t => t.Item1.Name.ToString()))}", null, true,
                    $"Our houses hold claims upon {names}. The war is won when every one of them is taken — " +
                    "and each passes to the house whose claim was pressed."));
            }

            if (hasAffront)
                elements.Add(new InquiryElement(WarAim.Revenge, "Chastisement — answer the affront", null, true,
                    "A punitive war. Beat them decisively, then take reparation — or the surrender of the lord who wronged us."));

            elements.Add(new InquiryElement(WarAim.Tribute, "Tribute — bleed them for gold", null, true,
                claims.Count > 0 || hasAffront
                    ? "Beat them decisively, then demand an indemnity or a tributary yoke."
                    : "Beat them decisively, then demand an indemnity. We march without a claim or a grievance — " +
                      "naked ambition, and the court will mark it."));

            elements.Add(new InquiryElement(WarAim.TotalSubjugation, "Subjugation — swallow the realm entire", null, true,
                "The hardest war there is. It ends only when their throne collapses — their king fallen, their " +
                "legitimacy broken, their lords looking to you — or when every last fief of theirs has fallen."));

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"The Aim of the War with {ok.Name}",
                "Why do we march? The aim is fixed here, and it is what victory will mean. It cannot be changed once the banners are out.",
                elements, false, 1, 1, "Fix the aim", "",
                sel =>
                {
                    WarAim aim = sel != null && sel.Count > 0 && sel[0].Identifier is WarAim a ? a : WarAim.Tribute;
                    ApplyChosenAim(ok, aim, claims);
                }, null, ""), true);
        }

        private void ApplyChosenAim(Kingdom ok, WarAim aim, List<(Settlement, Clan, float)> claims)
            => TYTLog.Guard("Warfare.ApplyAim", () =>
            {
                Kingdom pk = PK;
                int i = FindWar(pk, ok);
                if (i < 0) return;

                var targets = new List<Settlement>();
                var claimants = new List<Clan>();
                if (aim == WarAim.ProvincialConquest)
                {
                    var take = claims.Take(MaxConquestTargets).ToList();
                    targets = take.Select(t => t.Item1).ToList();
                    claimants = take.Select(t => t.Item2).ToList();
                }
                SetAim(i, aim, targets, claimants);

                // A war fought for nothing at all costs the throne some standing.
                if (aim == WarAim.Tribute && claims.Count == 0
                    && !(Util.WarAimsBehavior.Instance?.HasCasusBelli(pk, ok, out _) ?? false))
                {
                    ImperialAuthorityBehavior.Instance?.ModifyAuthority(pk, -4f, "a war declared without cause");
                    if (pk.Leader != null)
                        LegitimacyBehavior.Instance?.ModifyLegitimacy(pk.Leader, -3f, "a war of naked ambition");
                }

                string body = aim == WarAim.ProvincialConquest
                    ? $"The war with {ok.Name} is fought for {string.Join(", ", targets.Select(t => t.Name.ToString()))}. " +
                      "It is won when they are ours — and each shall pass to the house whose claim we press."
                    : aim == WarAim.TotalSubjugation
                    ? $"The war with {ok.Name} is fought to end them as a realm. Break their throne, or take every stone they hold."
                    : aim == WarAim.Revenge
                    ? $"The war with {ok.Name} is fought to answer their affront. Beat them, and they will answer for it."
                    : $"The war with {ok.Name} is fought for gold. Beat them, and they will pay.";

                RoyalFarmaan.FromRuler(pk, "The Aim of the War", body, "It shall be done");
            });

        // ── The ledger: nothing moves the bar unless it can be named ─────────────────
        private void Add(Kingdom scorer, Kingdom against, Kind kind, string subject = null, float? delta = null)
        {
            if (!Tracked(scorer, against)) return;
            int i = FindWar(scorer, against);
            if (i < 0) { EnsureWar(scorer, against); i = FindWar(scorer, against); if (i < 0) return; }

            // The ledger is kept from the AGGRESSOR's point of view: his gains are positive.
            bool scorerIsAggressor = _wAgg[i] == scorer.StringId;
            float d = delta ?? WarProgressMath.DefaultDelta(kind);
            if (!scorerIsAggressor) d = -d;

            _cWar.Add(WarKey(i)); _cDay.Add(Today); _cKind.Add((int)kind);
            _cDelta.Add(d); _cSubj.Add(subject ?? "");
        }

        private List<WarProgressMath.Contribution> LedgerOf(int i)
        {
            string key = WarKey(i);
            var list = new List<WarProgressMath.Contribution>();
            for (int j = 0; j < _cWar.Count; j++)
                if (_cWar[j] == key)
                    list.Add(new WarProgressMath.Contribution(_cDay[j], (Kind)_cKind[j], _cDelta[j], _cSubj[j]));
            return list;
        }

        // The war as the given realm sees it — the aggressor's aim, or the defender's denial of it.
        public WarProgressMath.Snapshot SnapshotFor(Kingdom mine, Kingdom theirs)
        {
            var snap = new WarProgressMath.Snapshot();
            int i = FindWar(mine, theirs);
            if (i < 0) return snap;

            Kingdom agg = Find(_wAgg[i]);
            Kingdom def = Find(_wDef[i]);
            snap.Aim = AimAt(i);
            snap.IsDefender = mine != agg;
            snap.Score = WarProgressMath.Score(LedgerOf(i));
            if (snap.IsDefender) snap.Score = -snap.Score;   // the ledger is kept aggressor-positive

            var targets = TargetIds(i).Select(Settle).Where(s => s != null).ToList();
            snap.TargetsTotal = targets.Count;
            snap.TargetsHeld = agg == null ? 0 : targets.Count(s => s.OwnerClan?.Kingdom == agg);

            if (snap.Aim == WarAim.TotalSubjugation && agg != null && def != null)
            {
                // Fiefs the defender still holds, against what he began with — plus his throne's health.
                var defFiefs = def.Settlements.Where(s => s.IsTown || s.IsCastle).ToList();
                int takenByUs = Settlement.All.Count(s => (s.IsTown || s.IsCastle)
                    && s.OwnerClan?.Kingdom == agg && WasTakenFrom(s, def, i));
                snap.EnemyFiefsTotal = defFiefs.Count + takenByUs;
                snap.EnemyFiefsTaken = takenByUs;

                Hero king = def.Leader;
                snap.EnemyKingFallen = king == null || !king.IsAlive || king.IsPrisoner;
                snap.EnemyKingLegitimacy = LegitimacyBehavior.Instance?.GetLegitimacy(king) ?? 60f;
                snap.LoyalLordFraction = WarAimMath.FractionWithRelationAtLeast(
                    def.Clans.Where(c => !c.IsEliminated && c.Leader != null)
                             .Select(c => CharacterRelationManager.GetHeroRelation(agg.Leader ?? Hero.MainHero, c.Leader)),
                    WarAimMath.SubjugationLoyalRelation);
            }
            return snap;
        }

        // Did this fief change hands from the defender to us during THIS war? The ledger knows: every
        // FiefTaken names its settlement.
        private bool WasTakenFrom(Settlement s, Kingdom def, int warIndex)
        {
            string key = WarKey(warIndex);
            for (int j = 0; j < _cWar.Count; j++)
                if (_cWar[j] == key && (Kind)_cKind[j] == Kind.FiefTaken && _cSubj[j] == s.StringId)
                    return true;
            return false;
        }

        public float ProgressPercent(Kingdom mine, Kingdom theirs)
            => WarProgressMath.Percent(SnapshotFor(mine, theirs));

        public List<WarProgressMath.Line> ProgressBreakdown(Kingdom mine, Kingdom theirs)
        {
            int i = FindWar(mine, theirs);
            if (i < 0) return new List<WarProgressMath.Line>();
            var snap = SnapshotFor(mine, theirs);
            var targets = TargetIds(i).Select(Settle).Where(s => s != null)
                .Select(s => (s.Name.ToString(), s.OwnerClan?.Kingdom == Find(_wAgg[i])));
            return WarProgressMath.Breakdown(snap, LedgerOf(i), targets);
        }

        // Every war on the map is tracked, including those that predate this behavior (old saves) and
        // those the player never saw declared. Legacy saves land here with a reconstructed aim.
        private void EnsureWar(Kingdom a, Kingdom b)
        {
            if (!Tracked(a, b) || FindWar(a, b) >= 0) return;
            OpenWar(a, b, WarAim.Tribute, new List<Settlement>(), new List<Clan>());
            AssignAimFromClaims(a, b);   // sensible default: whatever cause a actually has against b
        }

        // ── Score events ─────────────────────────────────────────────────────────────
        // Every battle on the map, not just the player's — the AI's wars must progress too.
        private void OnAnyBattleEnd(MapEvent e)
        {
            if (e == null || !WorldGen.Ready || !e.HasWinner) return;
            Kingdom att = e.AttackerSide?.MapFaction as Kingdom;
            Kingdom def = e.DefenderSide?.MapFaction as Kingdom;
            if (!Tracked(att, def)) return;

            if (e.IsRaid) { Add(att, def, Kind.VillageRaided, e.MapEventSettlement?.Name?.ToString()); return; }

            bool siege = e.MapEventSettlement != null;
            Kingdom winner = e.WinningSide == BattleSideEnum.Attacker ? att : def;
            Kingdom loser  = winner == att ? def : att;

            Add(winner, loser, siege ? Kind.SiegeTaken : Kind.BattleWon, e.MapEventSettlement?.Name?.ToString());
            Add(loser, winner, siege ? Kind.SiegeLost : Kind.BattleLost, e.MapEventSettlement?.Name?.ToString());

            if (KingBrokenOn(e, loser))
                Add(winner, loser, Kind.KingCaptured, loser.Leader?.Name?.ToString());
        }

        private static bool KingBrokenOn(MapEvent e, Kingdom loser)
        {
            if (e == null || loser?.Leader == null) return false;
            BattleSideEnum losing = e.WinningSide == BattleSideEnum.Attacker ? BattleSideEnum.Defender : BattleSideEnum.Attacker;
            MapEventSide side = e.GetMapEventSide(losing);
            if (side?.Parties == null) return false;
            foreach (MapEventParty p in side.Parties)
                if (p?.Party?.LeaderHero == loser.Leader) return true;
            return false;
        }

        // The player's own battles: valour toward the next mansab (unchanged), on top of the war ledger
        // that OnAnyBattleEnd has already written.
        private void OnPlayerBattleEnd(MapEvent mapEvent)
            => TYTLog.Guard("Warfare.OnPlayerBattleEnd", () => PlayerBattleEnd(mapEvent));

        private void PlayerBattleEnd(MapEvent mapEvent)
        {
            if (mapEvent == null) return;
            Kingdom pk = PK;
            IFaction self = (IFaction)pk ?? Clan.PlayerClan;
            IFaction att = mapEvent.AttackerSide?.MapFaction;
            IFaction def = mapEvent.DefenderSide?.MapFaction;
            IFaction opp = att == self ? def : (def == self ? att : null);

            bool win = mapEvent.HasWinner && mapEvent.WinningSide == mapEvent.PlayerSide;
            bool siege = mapEvent.MapEventSettlement != null;

            if (win && pk != null && opp is Kingdom enemy)
            {
                float gain = Config.Tune.ValourPerWin * (siege ? Config.Tune.ValourSiegeMultiplier : 1f);
                try
                {
                    int ours = mapEvent.GetNumberOfInvolvedMen(mapEvent.PlayerSide);
                    int theirs = mapEvent.GetNumberOfInvolvedMen(
                        mapEvent.PlayerSide == BattleSideEnum.Attacker ? BattleSideEnum.Defender : BattleSideEnum.Attacker);
                    if (ours > 0 && theirs >= ours * 1.5f)
                    {
                        gain *= Config.Tune.ValourOutnumberedMultiplier;
                        Notify($"Victory against the odds — {theirs} of the foe against your {ours}. The court will hear of it.", false);
                    }
                }
                catch (Exception e) { TYTLog.Error("Outnumbered-valour check failed", e); }

                if (KingBrokenOn(mapEvent, enemy))
                {
                    gain += Config.Tune.ValourKingCapture;
                    Notify($"You have broken {enemy.Leader?.Name} on the field — a deed that will be sung of. Your valour soars.", false);
                }
                MansabdariBehavior.Instance?.AddValour(Clan.PlayerClan, gain);
            }

            int kills = PlayerKillValourLogic.Take();
            if (kills > 0)
            {
                float killGain = kills * Config.Tune.ValourPerKill;
                if (killGain > 0f)
                {
                    MansabdariBehavior.Instance?.AddValour(Clan.PlayerClan, killGain);
                    Notify($"Your own blade accounted for {kills} of the foe — {killGain:0.#} valour earned.", false);
                }
            }
        }

        private void OnTournamentFinished(CharacterObject winner, MBReadOnlyList<CharacterObject> participants, Town town, ItemObject prize)
            => TYTLog.Guard("Warfare.TournamentFinished", () =>
            {
                if (winner == null || winner != Hero.MainHero?.CharacterObject) return;
                float gain = Config.Tune.ValourTournamentWin;
                if (gain <= 0f) return;
                MansabdariBehavior.Instance?.AddValour(Clan.PlayerClan, gain);
                Notify($"Your triumph in the tournament at {town?.Name} rings through the court — {gain:0.#} valour earned.", false);
            });

        private void OnOwnerChanged(Settlement s, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturer,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (!WorldGen.Ready) return;
            if (s == null || !(s.IsTown || s.IsCastle)) return;
            Kingdom newK = newOwner?.Clan?.Kingdom;
            Kingdom oldK = oldOwner?.Clan?.Kingdom;

            TYTLog.GuardQuiet("Warfare.OwnerChanged", () =>
            {
                if (Tracked(newK, oldK) && newK.IsAtWarWith(oldK))
                {
                    int i = FindWar(newK, oldK);
                    if (i < 0) { EnsureWar(newK, oldK); i = FindWar(newK, oldK); }
                    if (i >= 0)
                    {
                        // A fief the war was DECLARED FOR is worth far more than any other.
                        bool isTarget = TargetIds(i).Contains(s.StringId);
                        bool takerIsAggressor = _wAgg[i] == newK.StringId;

                        Add(newK, oldK, Kind.FiefTaken, s.StringId);
                        Add(oldK, newK, Kind.FiefLost, s.StringId);

                        if (isTarget)
                        {
                            if (takerIsAggressor) Add(newK, oldK, Kind.TargetTaken, s.Name.ToString());
                            else                  Add(newK, oldK, Kind.TargetRetaken, s.Name.ToString());
                        }
                    }
                }
            });

            // Spoils of war — the player takes a city by storm.
            if (newOwner == Hero.MainHero && detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege)
                OfferSpoils(s);
        }

        // ── The daily pulse ──────────────────────────────────────────────────────────
        private void OnDailyTick()
        {
            _ready = true;
            if (!WorldGen.Ready) return;

            // Track every war actually being fought, including AI-vs-AI and those from before this
            // behavior existed.
            foreach (Kingdom a in Kingdom.All.Where(k => !k.IsEliminated && !ThroneWar.IsRebelKingdom(k)))
                foreach (Kingdom b in Kingdom.All.Where(k => k != a && !k.IsEliminated && !ThroneWar.IsRebelKingdom(k) && a.IsAtWarWith(k)))
                    EnsureWar(a, b);

            ExpireTruces();

            for (int i = _wAgg.Count - 1; i >= 0; i--)
            {
                Kingdom agg = Find(_wAgg[i]);
                Kingdom def = Find(_wDef[i]);
                if (agg == null || def == null || agg.IsEliminated || def.IsEliminated) { CloseWar(i); continue; }
                if (!agg.IsAtWarWith(def)) { CloseWar(i); continue; }

                GrindOn(i);
                CheckCompletion(i, agg, def);
            }

            CouncilUrging();
            PayTribute();
            CompactLedger();
        }

        // A war that merely drags favours nobody: one running row per war, incremented, rather than a
        // new ledger line every single day (which would bloat the save by 365 rows a year).
        private void GrindOn(int i)
        {
            string key = WarKey(i);
            for (int j = 0; j < _cWar.Count; j++)
                if (_cWar[j] == key && (Kind)_cKind[j] == Kind.TimeGrind)
                { _cDelta[j] += WarProgressMath.DefaultDelta(Kind.TimeGrind); _cDay[j] = Today; return; }

            _cWar.Add(key); _cDay.Add(Today); _cKind.Add((int)Kind.TimeGrind);
            _cDelta.Add(WarProgressMath.DefaultDelta(Kind.TimeGrind)); _cSubj.Add("");
        }

        // THE AIM IS THE WIN CONDITION. When it is met, the war can be concluded — on the victor's terms,
        // with a forced truce on the loser.
        private void CheckCompletion(int i, Kingdom agg, Kingdom def)
        {
            var snap = SnapshotFor(agg, def);
            if (!WarProgressMath.Complete(snap)) return;

            string key = WarKey(i);
            if (_completionOffered.Contains(key)) return;
            _completionOffered.Add(key);

            // The player's realm, and he rules: HE decides whether to take the win.
            if (agg == PK && IsRuler) { OfferConclusion(i, agg, def, snap); return; }

            // An AI aggressor takes what he came for.
            ConcludeWar(i, agg, def);
        }

        private void OfferConclusion(int i, Kingdom agg, Kingdom def, WarProgressMath.Snapshot snap)
        {
            string what = snap.Aim == WarAim.ProvincialConquest
                ? $"Every fief we marched for is ours: {string.Join(", ", TargetIds(i).Select(Settle).Where(s => s != null).Select(s => s.Name.ToString()))}."
                : snap.Aim == WarAim.TotalSubjugation
                ? $"{def.Name} is broken. Their realm may be swallowed entire — their houses will bend the knee, and hate you for it."
                : $"{def.Name} is beaten decisively. They will accept whatever terms you dictate.";

            RoyalFarmaan.FromRuler(agg, "The War Is Won",
                $"{what}\n\nConclude it now — the peace will be dictated on your terms, and {def.Name} will be bound to a " +
                "truce for three years. Or press on, and take more than you came for.",
                primary: "Conclude the war on our terms", onPrimary: () => TYTLog.Guard("Warfare.Conclude", () =>
                {
                    int j = FindWar(agg, def);
                    if (j >= 0) ConcludeWar(j, agg, def);
                }),
                secondary: "Press on — I want more", onSecondary: () =>
                    Notify($"The war with {def.Name} goes on. Direct it from any town or castle when you wish to dictate terms.", false));
        }

        // ── Concluding a war: the aim is paid out, then the truce is imposed ─────────
        private void ConcludeWar(int i, Kingdom agg, Kingdom def)
        {
            if (i < 0 || i >= _wAgg.Count) return;
            WarAim aim = AimAt(i);
            _applyingTerms = true;
            try
            {
                if (aim == WarAim.TotalSubjugation) { Subjugate(agg, def); return; }   // absorbs and closes

                if (aim == WarAim.ProvincialConquest) AwardTargets(i, agg);

                if (agg.IsAtWarWith(def)) MakePeaceAction.Apply(agg, def);
                ApplyTruce(agg, def, ForcedTruceDays);

                if (agg == PK || def == PK)
                    RoyalFarmaan.FromRuler(PK, "The War Is Ended",
                        $"Peace is sealed between {agg.Name} and {def.Name} on the victor's terms. " +
                        $"The realms are bound to a truce for three years.\n\nWhat was taken is not forgotten: the " +
                        "dispossessed houses keep their claims, and those claims will fade only slowly.", "It is done");
                TYTLog.Info($"Warfare: {agg.Name}'s war ({aim}) on {def.Name} concluded; truce {ForcedTruceDays}d.");
            }
            catch (Exception e) { TYTLog.Error("ConcludeWar failed", e); }
            finally { _applyingTerms = false; CloseWar(FindWar(agg, def)); }
        }

        // The fiefs the war was fought for pass to the HOUSES whose claims were pressed — that is the whole
        // bargain. Under Mansabdari the crown is not bound by a house's pretensions: the fief stays with the
        // crown's own channel to re-grant by rank (ch.30 §2).
        private void AwardTargets(int i, Kingdom agg)
        {
            bool mansabdari = MansabdariTenureBehavior.Instance?.IsMansabdari(agg) ?? false;
            var targets = TargetIds(i);
            var claimants = ClaimantIds(i);

            for (int t = 0; t < targets.Count; t++)
            {
                Settlement s = Settle(targets[t]);
                if (s == null || s.OwnerClan?.Kingdom != agg) continue;   // we never actually took it
                if (mansabdari)
                {
                    if (agg == PK)
                        Notify($"{s.Name} is taken, but under Mansabdari law it is the crown's to grant, not the claimant's.", false);
                    continue;
                }
                Clan claimant = t < claimants.Count ? ClanOf(claimants[t]) : null;
                if (claimant == null || claimant.IsEliminated || claimant.Leader == null) continue;
                if (s.OwnerClan == claimant) continue;

                try
                {
                    ChangeOwnerOfSettlementAction.ApplyByGift(s, claimant.Leader);
                    if (agg == PK)
                        Notify($"{s.Name} passes to {claimant.Name}, whose claim we pressed.", false);
                }
                catch (Exception e) { TYTLog.Error($"AwardTargets: could not seat {claimant.Name} at {s.Name}", e); }
            }
        }

        // ── Total subjugation: the realm is SWALLOWED, not scattered ─────────────────
        // Vanilla would destroy the kingdom and its houses would be flung to the winds. Instead every clan
        // is absorbed — and carries a bitter, DECAYING grudge (OpinionType.Subjugated, -50, half-life 270d,
        // so it fades over ~3-4 years). Their lords do not leave. They stay, and they seethe: the civil-war
        // and conspiracy systems read exactly this opinion, so a swallowed realm is a slate of live
        // challengers for a few years. That is the price of an empire, and it is intended (ch.30 §4).
        private void Subjugate(Kingdom victor, Kingdom loser)
        {
            if (victor == null || loser == null) return;
            Hero conqueror = victor.Leader;
            var houses = loser.Clans.Where(c => c != null && !c.IsEliminated && c.Leader != null).ToList();

            // Peace FIRST, while both realms still live: annexing every clan empties the loser, and making
            // peace with a dead kingdom is incoherent.
            try { if (victor.IsAtWarWith(loser)) MakePeaceAction.Apply(victor, loser); }
            catch (Exception e) { TYTLog.Error("Subjugate: peace failed", e); }

            int absorbed = 0;
            foreach (Clan c in houses)
            {
                try
                {
                    ChangeKingdomAction.ApplyByJoinToKingdom(c, victor, default(CampaignTime), false);
                    absorbed++;
                    if (conqueror != null && c.Leader != conqueror)
                        OpinionBehavior.Instance?.AddOpinion(c.Leader, conqueror, OpinionMath.OpinionType.Subjugated);
                }
                catch (Exception e) { TYTLog.Error($"Subjugate: could not absorb {c.Name}", e); }
            }

            CloseWar(FindWar(victor, loser));   // release the discontinuation veto before we retire the husk

            try
            {
                if (!loser.IsEliminated && !loser.Settlements.Any() && !loser.Clans.Any(c => !c.IsEliminated))
                { loser.RulingClan = null; DestroyKingdomAction.Apply(loser); }
            }
            catch (Exception e) { TYTLog.Error("Subjugate: could not retire the husk", e); }

            if (victor.Leader != null)
                ImperialAuthorityBehavior.Instance?.ModifyAuthority(victor, 10f, "a realm swallowed whole");

            TYTLog.Info($"Warfare: {victor.Name} SUBJUGATED {loser.Name}; {absorbed} houses absorbed, each bearing a -50 grudge.");

            if (victor == PK)
                RoyalFarmaan.FromRuler(victor, "A Realm Is Swallowed Whole",
                    $"{loser.Name} is no more. Its {absorbed} houses bend the knee to you and enter your realm — " +
                    "but they are not friends. They have been conquered, and they know it. Watch them: a swallowed " +
                    "realm seethes, and it will be years before the bitterness cools.", "Let them kneel");
            else if (Clan.PlayerClan?.Kingdom == loser)
                Notify($"{loser.Name} is no more — {victor.Name} has swallowed it whole. You bend the knee to a conqueror.", true);
        }

        // A realm being swallowed must NOT be discontinued by vanilla the instant its last fief falls —
        // we need it alive long enough to absorb its houses rather than scatter them.
        private void OnCanKingdomBeDiscontinued(Kingdom k, ref bool result)
        {
            if (k == null) return;
            for (int i = 0; i < _wAgg.Count; i++)
                if (_wDef[i] == k.StringId && AimAt(i) == WarAim.TotalSubjugation) { result = false; return; }
        }

        // ── Truces ───────────────────────────────────────────────────────────────────
        public void ApplyTruce(Kingdom a, Kingdom b, int days)
        {
            if (a == null || b == null || days <= 0) return;
            int until = Today + days;
            for (int i = 0; i < _trA.Count; i++)
                if (IsPair(i, a, b)) { _trUntil[i] = Math.Max(_trUntil[i], until); return; }
            _trA.Add(a.StringId); _trB.Add(b.StringId); _trUntil.Add(until);
        }

        // Read by the WarDeclarationGate patch: no war may be declared across a live truce.
        public bool IsTruced(IFaction a, IFaction b)
        {
            if (!(a is Kingdom ka) || !(b is Kingdom kb)) return false;
            int today = Today;
            for (int i = 0; i < _trA.Count; i++)
                if (IsPair(i, ka, kb) && _trUntil[i] > today) return true;
            return false;
        }

        public int TruceDaysLeft(Kingdom a, Kingdom b)
        {
            int today = Today;
            for (int i = 0; i < _trA.Count; i++)
                if (IsPair(i, a, b) && _trUntil[i] > today) return _trUntil[i] - today;
            return 0;
        }

        private bool IsPair(int i, Kingdom a, Kingdom b)
            => (_trA[i] == a.StringId && _trB[i] == b.StringId)
            || (_trA[i] == b.StringId && _trB[i] == a.StringId);

        private void ExpireTruces()
        {
            int today = Today;
            for (int i = _trUntil.Count - 1; i >= 0; i--)
                if (_trUntil[i] <= today)
                { _trA.RemoveAt(i); _trB.RemoveAt(i); _trUntil.RemoveAt(i); }
        }

        // ── Closing a war ────────────────────────────────────────────────────────────
        private void CloseWar(int i)
        {
            if (i < 0 || i >= _wAgg.Count) return;
            string key = WarKey(i);
            for (int j = _cWar.Count - 1; j >= 0; j--)
                if (_cWar[j] == key) RemoveContribution(j);

            _completionOffered.Remove(key);
            _wAgg.RemoveAt(i); _wDef.RemoveAt(i); _wAim.RemoveAt(i);
            _wTargets.RemoveAt(i); _wClaimants.RemoveAt(i); _wStart.RemoveAt(i);
        }

        private void RemoveContribution(int j)
        {
            _cWar.RemoveAt(j); _cDay.RemoveAt(j); _cKind.RemoveAt(j);
            _cDelta.RemoveAt(j); _cSubj.RemoveAt(j);
        }

        // A decade-long war would otherwise carry thousands of rows into the save.
        private void CompactLedger()
        {
            if (_cWar.Count <= WarProgressMath.MaxLedgerRows * 2) return;
            for (int i = 0; i < _wAgg.Count; i++)
            {
                string key = WarKey(i);
                var rows = LedgerOf(i);
                if (rows.Count <= WarProgressMath.MaxLedgerRows) continue;
                var compact = WarProgressMath.Compact(rows);

                for (int j = _cWar.Count - 1; j >= 0; j--)
                    if (_cWar[j] == key) RemoveContribution(j);
                foreach (var c in compact)
                {
                    _cWar.Add(key); _cDay.Add(c.Day); _cKind.Add((int)c.Kind);
                    _cDelta.Add(c.Delta); _cSubj.Add(c.Subject ?? "");
                }
            }
        }

        // ── Peace concluded elsewhere (the AI, exhaustion, a barter) ─────────────────
        private void OnMakePeace(IFaction f1, IFaction f2, MakePeaceAction.MakePeaceDetail detail)
            => TYTLog.GuardQuiet("Warfare.MakePeace", () =>
            {
                if (!(f1 is Kingdom a) || !(f2 is Kingdom b)) return;
                int i = FindWar(a, b);
                if (i < 0) return;

                // If WE dictated it, ConcludeWar has already paid the aim out and closed the war.
                if (_applyingTerms) return;

                // A war that simply petered out: no aim is paid, but a decisive victor still settles
                // accounts, and a short truce keeps the realms from re-declaring the same afternoon.
                var snap = SnapshotFor(a, b);
                if (Math.Abs(snap.Score) >= WarProgressMath.DecisiveScore && a.Leader != null && b.Leader != null)
                {
                    Kingdom agg = Find(_wAgg[i]);
                    Hero winner = snap.Score > 0 ? agg?.Leader : Find(_wDef[i])?.Leader;
                    Hero loser  = winner == a.Leader ? b.Leader : a.Leader;
                    if (winner != null && loser != null)
                    {
                        int amount = Math.Min((int)Math.Abs(snap.Score) * 80, loser.Gold);
                        if (amount > 0) GiveGoldAction.ApplyBetweenCharacters(loser, winner, amount, true);
                    }
                }
                ApplyTruce(a, b, 365);
                CloseWar(i);
            });

        private void CouncilUrging()
        {
            Kingdom pk = PK;
            if (pk == null || !IsRuler) return;
            foreach (Kingdom ok in Kingdom.All.Where(k => k != pk && !k.IsEliminated && pk.IsAtWarWith(k)).ToList())
            {
                if (ThroneWar.IsRebelKingdom(ok)) continue;
                float w = WarExhaustionBehavior.Instance?.Exhaustion(pk, ok) ?? 0f;
                if (!WarExhaustionMath.CouncilUrgesPeace(w) || _peaceUrged.Contains(ok.StringId)) continue;
                _peaceUrged.Add(ok.StringId);
                RoyalFarmaan.Issue("The Realm Wearies of War", $"From the Imperial Council of {pk.Name}",
                    $"The war with {ok.Name} drags on and the realm grows weary. The council urges you to seek terms — " +
                    "press for what your war aim allows, or grant peace. Direct the war effort from any town or castle.",
                    seal: null, primary: "I shall consider it",
                    dedupeKey: "weary:" + ok.StringId, cooldownDays: 30);
            }
        }

        // The tributary yoke, and the grievance of throwing it off. A tributary that cannot (or will not)
        // pay its nazrana hands its overlord a JUST CAUSE for a war of chastisement — one of the standing
        // casus belli of the whole layer (ch.30 §3).
        private void PayTribute()
        {
            Kingdom pk = PK;
            if (pk == null) return;
            int today = Today;
            if (today % 7 != 0) return;

            foreach (string id in _tributaryUntil.Keys.ToList())
            {
                Kingdom trib = Find(id);
                if (trib == null || today >= _tributaryUntil[id]) { _tributaryUntil.Remove(id); continue; }
                if (pk.IsAtWarWith(trib)) continue;
                if (trib.Leader == null || pk.Leader == null) continue;

                if (trib.Leader.Gold > 500)
                {
                    GiveGoldAction.ApplyBetweenCharacters(trib.Leader, pk.Leader, 500, true);
                    continue;
                }

                // The nazrana does not come. Whether from an empty treasury or plain defiance, the insult
                // is the same — and it is a cause for war.
                Util.WarAimsBehavior.Instance?.RegisterRevengeCasusBelli(pk, trib, trib.Leader);
                RoyalFarmaan.Issue("The Nazrana Does Not Come", $"Concerning our tributary, {trib.Name}",
                    $"{trib.Name} has withheld its nazrana. Whether their treasury is empty or their spine has " +
                    "stiffened, the insult stands — and the realm now holds just cause to chastise them.",
                    seal: "A tribute withheld is a defiance offered", primary: "They will answer for it",
                    dedupeKey: "tribwithheld:" + trib.StringId, cooldownDays: 60);
            }
        }

        // ── Directing the war (the ruler's menu) ─────────────────────────────────────
        private void OpenDirectWar()
        {
            Kingdom pk = PK;
            if (pk == null || !IsRuler) { Notify("Only the sovereign may direct the realm's wars.", true); return; }
            var wars = Kingdom.All.Where(k => k != null && k != pk && !k.IsEliminated
                                              && pk.IsAtWarWith(k) && !ThroneWar.IsRebelKingdom(k)).ToList();
            foreach (var k in wars) EnsureWar(pk, k);
            if (wars.Count == 0) { Notify("The realm is at peace.", false); return; }

            var elements = wars.Select(k =>
            {
                var snap = SnapshotFor(pk, k);
                float ours = WarExhaustionBehavior.Instance?.Exhaustion(pk, k) ?? 0f;
                float theirs = WarExhaustionBehavior.Instance?.Exhaustion(k, pk) ?? 0f;
                string hint = string.Join("\n", ProgressBreakdown(pk, k)
                    .Select(l => $"{l.Label}: {l.Value}" + (string.IsNullOrEmpty(l.Detail) ? "" : $"  ({l.Detail})")));
                return new InquiryElement(k,
                    $"{k.Name} — {WarProgressMath.AimName(snap.Aim)}{(snap.IsDefender ? " (we defend)" : "")}, " +
                    $"{WarProgressMath.Percent(snap):0}% — {WarProgressMath.Headline(snap)}",
                    null, true,
                    hint + $"\n\nOur realm is {WarExhaustionMath.Tier(ours)}; theirs is {WarExhaustionMath.Tier(theirs)}.");
            }).ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Direct the War Effort", "Choose a war to bring to terms. Hover a war to see exactly what has moved it.",
                elements, true, 1, 1, "Negotiate", "Cancel",
                sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is Kingdom k) OfferTerms(k); },
                _ => { }, "", false), false, false);
        }

        // THE TERMS ARE GATED BY THE AIM, not by the score. A war for Bijapur can take Bijapur — not a
        // nazrana, not a random province, and never the whole realm.
        private void OfferTerms(Kingdom ok)
        {
            Kingdom pk = PK;
            int i = FindWar(pk, ok);
            if (i < 0) { Notify("That war is not tracked.", true); return; }

            var snap = SnapshotFor(pk, ok);
            WarAim aim = snap.Aim;
            bool complete = WarProgressMath.Complete(snap);
            bool decisive = snap.Score >= WarProgressMath.DecisiveScore;

            var elements = new List<InquiryElement>
            {
                new InquiryElement(Term.WhitePeace, "White peace (no terms)", null, true,
                    "End the war as it stands. Nothing is taken, nothing is paid — and a year's truce follows.")
            };

            if (WarAimMath.AllowsAnnexProvince(aim) && complete && !snap.IsDefender)
                elements.Add(new InquiryElement(Term.DemandTargets, "Take what we came for", null, true,
                    $"Annex {string.Join(", ", TargetIds(i).Select(Settle).Where(s => s != null).Select(s => s.Name.ToString()))} — " +
                    "each passing to the house whose claim we pressed. Then a three-year truce."));

            if (WarAimMath.AllowsTribute(aim) && decisive)
            {
                elements.Add(new InquiryElement(Term.DemandNazrana, "Demand a nazrana indemnity", null, true, "Take gold for peace."));
                elements.Add(new InquiryElement(Term.MakeTributary, "Make them a tributary", null, true,
                    "They pay weekly nazrana and keep the peace for three years."));
            }

            if (WarAimMath.AllowsJudgement(aim) && decisive
                && Util.WarAimsBehavior.Instance != null
                && Util.WarAimsBehavior.Instance.HasCasusBelli(pk, ok, out Hero culprit) && culprit != null)
                elements.Add(new InquiryElement(Term.SurrenderCulprit, $"Demand they surrender {culprit.Name}", null, true,
                    "Make them hand over the lord who wronged us — to pardon, fine, imprison, or execute."));

            if (WarAimMath.AllowsAnnexAll(aim) && complete)
                elements.Add(new InquiryElement(Term.Subjugate, "Demand total submission (annex the realm)", null, true,
                    "Absorb their ENTIRE realm. Their houses bend the knee and enter our realm — bearing a bitter grudge " +
                    "that will take years to cool."));

            if (snap.Score <= -8)
                elements.Add(new InquiryElement(Term.OfferNazrana, "Offer a nazrana for peace", null, true,
                    "Buy your way out of a losing war."));

            // The honest reason an option is missing.
            string why = complete
                ? "Our aim is achieved — we may take what we came for."
                : $"Our aim ({WarProgressMath.AimName(aim)}) is {WarProgressMath.Percent(snap):0}% achieved. " +
                  "Until it is met, we cannot demand what the war was declared to win.";

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"Terms with {ok.Name}",
                $"{why}\n\n" + string.Join("\n", ProgressBreakdown(pk, ok).Select(l => $"{l.Label}: {l.Value}")),
                elements, true, 1, 1, "Seal it", "Cancel",
                sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is Term t) ApplyTerms(ok, t); },
                _ => { }, "", false), false, false);
        }

        private void ApplyTerms(Kingdom ok, Term t)
        {
            Kingdom pk = PK;
            if (pk == null) return;
            int i = FindWar(pk, ok);
            if (i < 0) return;
            var snap = SnapshotFor(pk, ok);

            _applyingTerms = true;
            try
            {
                switch (t)
                {
                    case Term.DemandTargets:
                        AwardTargets(i, pk);
                        break;
                    case Term.DemandNazrana:
                        TransferGold(ok.Leader, pk.Leader, Math.Min((int)snap.Score * 100, ok.Leader?.Gold ?? 0));
                        break;
                    case Term.OfferNazrana:
                        TransferGold(pk.Leader, ok.Leader, Math.Min((int)Math.Abs(snap.Score) * 100, pk.Leader?.Gold ?? 0));
                        break;
                    case Term.MakeTributary:
                        _tributaryUntil[ok.StringId] = Today + 365 * 3;
                        break;
                    case Term.SurrenderCulprit:
                        Util.WarAimsBehavior.Instance?.JudgeCulprit(ok);
                        break;
                    case Term.Subjugate:
                        Subjugate(pk, ok);
                        return;   // Subjugate concludes and closes the war itself
                }

                if (!ok.IsEliminated && pk.IsAtWarWith(ok)) MakePeaceAction.Apply(pk, ok);
                ApplyTruce(pk, ok, t == Term.WhitePeace ? 365 : ForcedTruceDays);

                RoyalFarmaan.FromRuler(pk, "Peace Is Dictated",
                    $"Peace is sealed with {ok.Name} ({Describe(t)}). " +
                    (t == Term.WhitePeace ? "A year's truce holds." : "They are bound to a three-year truce."),
                    "It is done");
            }
            catch (Exception e) { TYTLog.Error("ApplyTerms failed", e); Notify("The terms could not be enforced.", true); }
            finally { _applyingTerms = false; CloseWar(FindWar(pk, ok)); }
        }

        private static void TransferGold(Hero from, Hero to, int amount)
        {
            if (from != null && to != null && amount > 0) GiveGoldAction.ApplyBetweenCharacters(from, to, amount, true);
        }

        private static string Describe(Term t) => t == Term.DemandNazrana ? "a nazrana indemnity"
            : t == Term.DemandTargets ? "the fiefs we marched for, ceded" : t == Term.MakeTributary ? "their tributary submission"
            : t == Term.OfferNazrana ? "a nazrana paid for peace" : t == Term.Subjugate ? "their realm annexed entire"
            : t == Term.SurrenderCulprit ? "the surrender of the guilty lord" : "white peace";

        // ── Regicide ─────────────────────────────────────────────────────────────────
        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
            => TYTLog.GuardQuiet("Warfare.HeroKilled", () =>
            {
                if (victim == null || detail != KillCharacterAction.KillCharacterActionDetail.Executed) return;
                Kingdom theirs = Kingdom.All.FirstOrDefault(k => !k.IsEliminated && k.Leader == victim);
                if (theirs == null) return;
                Kingdom pk = PK;
                if (pk == null || theirs == pk) return;
                bool byUs = killer != null && (killer == Hero.MainHero || killer.Clan?.Kingdom == pk);
                if (!byUs) return;

                if (pk.IsAtWarWith(theirs))
                    try { ThroneWar.WithInternalPeace(() => MakePeaceAction.Apply(pk, theirs)); } catch { }

                Hero butcher = killer == Hero.MainHero ? Hero.MainHero : (pk.Leader ?? killer);
                foreach (Clan c in theirs.Clans.Where(c => !c.IsEliminated && c.Leader != null))
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(butcher, c.Leader, -30);
                Util.WarAimsBehavior.Instance?.RegisterRevengeCasusBelli(theirs, pk, butcher);
                if (pk.Leader != null) LegitimacyBehavior.Instance?.ModifyLegitimacy(pk.Leader, -3f, "the killing of a king");

                Notify($"You have put {victim.Name}, king of {theirs.Name}, to death. His realm sues for peace — but every " +
                       $"lord of {theirs.Name} now thirsts for vengeance against you.", true);
            });

        // ── Spoils, notables, prisoners, banners (unchanged behaviour) ───────────────
        private enum Fate { Pardon, Penalize, Banish, Execute }

        private void OfferSpoils(Settlement s)
        {
            RoyalFarmaan.Issue("The City Is Taken", $"{s.Name} falls to your arms",
                $"{s.Name} is yours by storm. Your soldiers look to you: do you give the city over to plunder, " +
                "or show the mercy that wins a people's hearts?",
                seal: "By right of conquest",
                primary: "Sack the city", onPrimary: () => { Sack(s); JudgeNotables(s); },
                secondary: "Show mercy", onSecondary: () => { Mercy(s); JudgeNotables(s); });
        }

        private void JudgeNotables(Settlement s)
        {
            if (s?.Notables == null) return;
            var queue = new Queue<Hero>(s.Notables.Where(n => n != null && n.IsAlive)
                .OrderByDescending(n => n.Power).Take(6));
            PromptNextNotable(s, queue);
        }

        private void PromptNextNotable(Settlement s, Queue<Hero> queue)
        {
            while (queue.Count > 0)
            {
                Hero n = queue.Dequeue();
                if (n == null || !n.IsAlive) continue;
                int rel = CharacterRelationManager.GetHeroRelation(Hero.MainHero, n);
                string role = FeudalTitlesBehavior.NotableRole(n);
                var elements = new List<InquiryElement>
                {
                    new InquiryElement(Fate.Pardon, "Pardon him", null, true, "Win his goodwill; the city settles. Relation rises."),
                    new InquiryElement(Fate.Penalize, "Fine him", null, true, "Seize his gold; he resents it. Relation falls, slight unrest."),
                    new InquiryElement(Fate.Banish, "Banish him", null, true, "Strip his standing and cast him out. Real unrest and lasting enmity."),
                    new InquiryElement(Fate.Execute, "Execute him", null, true, "Put him to death. Severe unrest; his kin and peers will not forget."),
                };
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    $"The Fate of {n.Name}",
                    $"{n.Name}, {role} of {s.Name} (your relation {rel}). As conqueror, decree his fate.",
                    elements, true, 1, 1, "Decree it", "Pardon the rest",
                    sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is Fate f) ApplyFate(s, n, f);
                             PromptNextNotable(s, queue); },
                    _ => { while (queue.Count > 0) { Hero r = queue.Dequeue(); if (r != null && r.IsAlive) ApplyFate(s, r, Fate.Pardon); }
                           ApplyFate(s, n, Fate.Pardon); },
                    "", false), false, false);
                return;
            }
        }

        private void ApplyFate(Settlement s, Hero n, Fate f)
        {
            Town town = s.Town;
            switch (f)
            {
                case Fate.Pardon:
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, n, 10);
                    if (town != null) town.Loyalty = MathF.Min(100f, town.Loyalty + 3f);
                    break;

                case Fate.Penalize:
                    int fine = 500 + (int)n.Power * 2;
                    int take = Math.Min(fine, Math.Max(0, n.Gold));
                    if (take > 0) GiveGoldAction.ApplyBetweenCharacters(n, Hero.MainHero, take, true);
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, n, -10);
                    if (town != null) town.Loyalty = MathF.Max(0f, town.Loyalty - 3f);
                    AddPressure(s, 5f);
                    break;

                case Fate.Banish:
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, n, -20);
                    try { n.AddPower(-n.Power * 0.85f); } catch { }
                    if (town != null) { town.Loyalty = MathF.Max(0f, town.Loyalty - 8f); town.Prosperity = MathF.Max(0f, town.Prosperity - 300f); }
                    AddPressure(s, 12f);
                    AffectKinAndPeers(s, n, -12, -4);
                    Notify($"{n.Name} is stripped of standing and cast out of {s.Name}.", false);
                    break;

                case Fate.Execute:
                    if (town != null) { town.Loyalty = MathF.Max(0f, town.Loyalty - 15f); town.Prosperity = MathF.Max(0f, town.Prosperity - 600f); }
                    AddPressure(s, 25f);
                    AffectKinAndPeers(s, n, -30, -8);
                    if (IsRuler) LegitimacyBehavior.Instance?.ModifyLegitimacy(Hero.MainHero, -4f, "an execution after conquest");
                    try { KillCharacterAction.ApplyByMurder(n, Hero.MainHero); } catch { }
                    Notify($"{n.Name} is put to death. {s.Name} seethes.", false);
                    break;
            }
        }

        private static void AddPressure(Settlement s, float delta)
        {
            var rc = RevoltCascadeBehavior.Instance;
            if (rc != null) rc.SetPressure(s, MathF.Min(100f, rc.GetPressure(s) + delta));
        }

        private static void AffectKinAndPeers(Settlement s, Hero n, int kinDelta, int peerDelta)
        {
            if (n.Clan?.Leader != null && n.Clan.Leader != n)
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, n.Clan.Leader, kinDelta);
            if (s.Notables != null)
                foreach (Hero other in s.Notables.Where(o => o != null && o.IsAlive && o != n))
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, other, peerDelta);
        }

        private void Sack(Settlement s)
        {
            int loot = 2000;
            if (s.Town != null) loot += (int)s.Town.Prosperity;
            Hero.MainHero.ChangeHeroGold(loot);
            if (s.Town != null) s.Town.Prosperity = MathF.Max(0f, s.Town.Prosperity - 1500f);
            RevoltCascadeBehavior.Instance?.SetPressure(s, Math.Min(100f, (RevoltCascadeBehavior.Instance.GetPressure(s)) + 30f));
            if (IsRuler) LegitimacyBehavior.Instance?.ModifyLegitimacy(Hero.MainHero, -5f, "the sack of a city");
            Notify($"Your men sack {s.Name}. You take {loot} rupees, but the city seethes with hatred.", false);
        }

        private void Mercy(Settlement s)
        {
            ChangeClanInfluenceAction.Apply(Clan.PlayerClan, 15f);
            RevoltCascadeBehavior.Instance?.SetPressure(s, MathF.Max(0f, (RevoltCascadeBehavior.Instance?.GetPressure(s) ?? 0f) - 15f));
            if (IsRuler) LegitimacyBehavior.Instance?.ModifyLegitimacy(Hero.MainHero, 6f, "clemency to a conquered city");
            if (s.Notables != null)
                foreach (Hero n in s.Notables.Where(n => n != null && n.IsAlive))
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, n, 5);
            Notify($"You spare {s.Name}. The people bless your name, and your standing grows.", false);
        }

        private void OnPrisonerTaken(PartyBase captor, Hero prisoner)
        {
            if (prisoner == null || !prisoner.IsLord || prisoner.Clan == Clan.PlayerClan) return;
            bool mine = captor != null && (captor.LeaderHero == Hero.MainHero || captor == MobileParty.MainParty?.Party);
            if (!mine) return;
            Kingdom pk = PK;
            Kingdom pkOf = prisoner.Clan?.Kingdom;
            if (pk == null || pkOf == null || !pk.IsAtWarWith(pkOf)) return;

            int ransom = 1000 + (prisoner.Clan != null ? (int)prisoner.Clan.Tier * 1500 : 1500);
            RoyalFarmaan.Issue("A Noble Captive", $"{prisoner.Name} is your prisoner",
                $"{prisoner.Name} of {prisoner.Clan?.Name} has fallen into your hands. Will you ransom him for gold, " +
                "or hold him hostage as leverage over his house?",
                seal: "The fortunes of war",
                primary: $"Ransom for {ransom} rupees", onPrimary: () => Ransom(prisoner, ransom),
                secondary: "Hold as hostage", onSecondary: () => Notify($"You hold {prisoner.Name} hostage. His house will not soon forget it.", false));
        }

        private void Ransom(Hero prisoner, int ransom)
        {
            try
            {
                Hero.MainHero.ChangeHeroGold(ransom);
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, prisoner, 5);
                EndCaptivityAction.ApplyByRansom(prisoner, Hero.MainHero);
                Notify($"You ransom {prisoner.Name} for {ransom} rupees. His gratitude is noted.", false);
            }
            catch { Notify("The ransom could not be arranged.", true); }
        }

        private void CallBanners()
        {
            Kingdom pk = PK;
            if (pk == null || !IsRuler) { Notify("Only the sovereign calls the realm's banners.", true); return; }
            int today = Today;
            if (today - _lastBannerDay < BannerCooldownDays) { Notify("The banners were lately called; the lords need time to gather.", true); return; }
            if (MobileParty.MainParty == null) return;
            _lastBannerDay = today;

            float auth = ImperialAuthorityBehavior.Instance?.GetAuthority(pk) ?? 60f;
            int capacity = MobileParty.MainParty.Party.PartySizeLimit - MobileParty.MainParty.MemberRoster.TotalManCount;
            int mustered = 0; int loyal = 0, defiant = 0;

            foreach (Clan c in pk.Clans.Where(c => !c.IsEliminated && c.Leader != null && c.Leader != Hero.MainHero))
            {
                int rel = CharacterRelationManager.GetHeroRelation(Hero.MainHero, c.Leader);
                bool answers = (rel + auth / 2f + MBRandom.RandomInt(0, 30)) >= 40f;
                if (answers)
                {
                    loyal++;
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, c.Leader, 2);
                    int levy = Math.Min(15 + (int)c.Tier * 5, Math.Max(0, capacity - mustered));
                    CharacterObject troop = (c.Culture ?? pk.Culture)?.BasicTroop;
                    if (levy > 0 && troop != null) { MobileParty.MainParty.MemberRoster.AddToCounts(troop, levy); mustered += levy; }
                }
                else
                {
                    defiant++;
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, c.Leader, -4);
                }
            }

            if (defiant > 0) ImperialAuthorityBehavior.Instance?.ModifyAuthority(pk, -2f, "lords defied the muster");
            RoyalFarmaan.FromRuler(pk, "The Banners Are Called",
                $"You summon the realm to war. {loyal} houses answer and bring {mustered} men to your host; " +
                $"{defiant} hold back, and their defiance is noted against them.", "Let us march");
        }

        // ── The vassal's petition: ask the crown to press YOUR house's claim ─────────
        // A landed house that holds a claim it cannot act on alone — because only the sovereign declares
        // war — may lay it before the throne. This is the other half of the wakil's bargain (ch.30 §1):
        // march for it yourself, or petition the crown to take it up as the REALM's cause. Without this,
        // a vassal who spends two years buying a bazaar has bought nothing he can use.
        private void OpenClaimPetition()
        {
            Kingdom pk = PK;
            Hero ruler = pk?.Leader;
            if (pk == null || ruler == null || ruler == Hero.MainHero)
            { Notify("You are the sovereign — you need petition no one. Declare the war yourself.", true); return; }

            var claims = (ClaimsBehavior.Instance?.ClaimsOf(Clan.PlayerClan)
                          ?? new List<(Settlement, float, ClaimsBehavior.ClaimKind)>())
                .Where(c => c.Item1?.OwnerClan?.Kingdom != null
                            && c.Item1.OwnerClan.Kingdom != pk
                            && !ThroneWar.IsRebelKingdom(c.Item1.OwnerClan.Kingdom)
                            && ClaimMath.WorthAWar(c.Item2))
                .ToList();

            if (claims.Count == 0)
            { Notify("Your house holds no claim abroad worth laying before the throne.", true); return; }

            var elements = claims.Select(c =>
            {
                Kingdom holder = c.Item1.OwnerClan.Kingdom;
                bool truced = IsTruced(pk, holder);
                bool atWar = pk.IsAtWarWith(holder);
                string state = truced ? "a truce stands — the crown cannot move"
                             : atWar ? "the realm already wars with them"
                             : "the realm is at peace with them";
                return new InquiryElement(c.Item1,
                    $"{c.Item1.Name} — {c.Item2:0.0} yrs ({ClaimMath.Describe(c.Item2)}), held by {holder.Name}",
                    null, !truced,
                    $"Ask {ruler.Name} to press our house's claim on {c.Item1.Name} as the realm's cause — {state}. " +
                    $"The petition costs {ClaimPetitionInfluence} influence, whatever the answer.");
            }).ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Lay a Claim Before the Throne",
                $"Only the sovereign declares war. Lay your house's claim before {ruler.Name} and ask him to make it " +
                "the realm's own — if he consents, the war will be fought for it, and the fief will be yours when it falls.",
                elements, true, 1, 1, "Petition the throne", "Withdraw",
                sel => { if (sel != null && sel.Count > 0 && sel[0].Identifier is Settlement s) PressClaim(s); },
                _ => { }, "", false), false, false);
        }

        private const int ClaimPetitionInfluence = 100;

        private void PressClaim(Settlement s)
            => TYTLog.Guard("Warfare.PressClaim", () =>
            {
                Kingdom pk = PK;
                Hero ruler = pk?.Leader;
                Kingdom holder = s?.OwnerClan?.Kingdom;
                if (pk == null || ruler == null || holder == null) return;

                if (IsTruced(pk, holder))
                { Notify($"A truce stands with {holder.Name}. The crown will not break it.", true); return; }

                if (Clan.PlayerClan.Influence < ClaimPetitionInfluence)
                { Notify($"The petition needs {ClaimPetitionInfluence} influence at court; you have {Clan.PlayerClan.Influence:0}.", true); return; }
                ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -ClaimPetitionInfluence);

                float claim = ClaimsBehavior.Instance?.GetClaim(Clan.PlayerClan, s) ?? 0f;
                float opinion = OpinionBehavior.Instance?.EffectiveOpinion(ruler, Hero.MainHero)
                                ?? CharacterRelationManager.GetHeroRelation(ruler, Hero.MainHero);
                float weary = WarExhaustionBehavior.Instance?.Exhaustion(pk, holder) ?? 0f;

                // The crown weighs the man, the claim, and whether the realm has the stomach for it.
                float verdict = opinion + claim * 3f - weary * 0.5f + MBRandom.RandomFloatRanged(-15f, 15f);
                bool consents = verdict >= 20f;

                if (!consents)
                {
                    RoyalFarmaan.FromRuler(pk, "The Throne Declines",
                        $"{ruler.Name} hears your claim upon {s.Name} and sets it aside. " +
                        (weary >= WarExhaustionMath.AdvisoryThreshold
                            ? "The realm is too weary for another war."
                            : "Your house does not yet carry weight enough at court to move the realm to war."),
                        "I withdraw, for now");
                    return;
                }

                // Already at war with them: the claim is folded into the war being fought.
                if (pk.IsAtWarWith(holder))
                {
                    int i = FindWar(pk, holder);
                    if (i < 0) { EnsureWar(pk, holder); i = FindWar(pk, holder); }
                    if (i < 0) return;

                    if (AimAt(i) != WarAim.ProvincialConquest)
                    {
                        RoyalFarmaan.FromRuler(pk, "The War Is Already Fought For Another Purpose",
                            $"The realm's war with {holder.Name} is a war of {WarProgressMath.AimName(AimAt(i)).ToLowerInvariant()}. " +
                            $"{ruler.Name} will not recast its aim now the banners are out. Your claim upon {s.Name} must wait for the next war.",
                            "So be it");
                        return;
                    }
                    AddTarget(i, s, Clan.PlayerClan);
                    RoyalFarmaan.FromRuler(pk, "Your Claim Is Taken Up",
                        $"{ruler.Name} adds {s.Name} to the aims of the realm's war with {holder.Name}. " +
                        "When it falls, it falls to your house.", "It shall be taken");
                    return;
                }

                // Peace: the crown declares the war, and it is declared FOR your claim.
                try
                {
                    DeclareWarAction.ApplyByDefault(pk, holder);
                    int i = FindWar(pk, holder);
                    if (i < 0) { EnsureWar(pk, holder); i = FindWar(pk, holder); }
                    if (i >= 0)
                        SetAim(i, WarAim.ProvincialConquest, new List<Settlement> { s }, new List<Clan> { Clan.PlayerClan });

                    RoyalFarmaan.FromRuler(pk, "The Realm Takes Up Your Claim",
                        $"{ruler.Name} declares war upon {holder.Name} to press your house's claim on {s.Name}. " +
                        "The realm marches for it — and when it falls, it is yours.\n\nSee that you are worth the blood.",
                        "We march");
                    TYTLog.Info($"Warfare: {pk.Name} declared war on {holder.Name} on the player's claim to {s.Name}.");
                }
                catch (Exception e)
                {
                    TYTLog.Error("PressClaim: the war could not be declared", e);
                    Notify("The crown consents, but the war could not be declared.", true);
                }
            });

        private void AddTarget(int i, Settlement s, Clan claimant)
        {
            var targets = TargetIds(i);
            if (targets.Contains(s.StringId)) return;
            var claimants = ClaimantIds(i);
            targets.Add(s.StringId); claimants.Add(claimant.StringId);
            _wTargets[i] = string.Join(";", targets);
            _wClaimants[i] = string.Join(";", claimants);
            _completionOffered.Remove(WarKey(i));   // the aim just grew; it may no longer be complete
        }

        // ── Menus ────────────────────────────────────────────────────────────────────
        private void OnSessionLaunched(CampaignGameStarter starter) => AddMenus(starter);

        private void AddMenus(CampaignGameStarter starter)
        {
            foreach (string root in new[] { "town", "castle" })
            {
                starter.AddGameMenuOption(root, "hindostan_directwar_" + root, "{=!}Direct the war effort",
                    args => { args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                              return IsRuler && AtWar(); },
                    args => OpenDirectWar(), false, 7);
                starter.AddGameMenuOption(root, "hindostan_callbanners_" + root, "{=!}Call the realm's banners",
                    args => { args.optionLeaveType = GameMenuOption.LeaveType.Recruit;
                              return IsRuler && AtWar(); },
                    args => CallBanners(), false, 8);

                // A vassal's road to acting on a claim: only the sovereign declares war.
                starter.AddGameMenuOption(root, "hindostan_pressclaim_" + root, "{=!}Lay a claim before the throne",
                    args => { args.optionLeaveType = GameMenuOption.LeaveType.Mission;
                              return PK != null && !IsRuler && HasForeignClaim(); },
                    args => OpenClaimPetition(), false, 9);
            }
        }

        private static bool HasForeignClaim()
        {
            Kingdom pk = PK;
            if (pk == null || ClaimsBehavior.Instance == null) return false;
            return ClaimsBehavior.Instance.ClaimsOf(Clan.PlayerClan).Any(c =>
                c.Item1?.OwnerClan?.Kingdom != null && c.Item1.OwnerClan.Kingdom != pk
                && !ThroneWar.IsRebelKingdom(c.Item1.OwnerClan.Kingdom)
                && ClaimMath.WorthAWar(c.Item2));
        }

        private static bool AtWar()
        {
            Kingdom pk = PK;
            return pk != null && Kingdom.All.Any(o => o != pk && !o.IsEliminated && pk.IsAtWarWith(o) && !ThroneWar.IsRebelKingdom(o));
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        // ── Save / load ──────────────────────────────────────────────────────────────
        // The state model changed shape in ch.30 (from a player-implicit dictionary to real war records).
        // Legacy saves simply arrive with no records: the daily tick's EnsureWar reconstructs one for every
        // war actually being fought, with its aim inferred from the aggressor's standing claims.
        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("hind_war2_agg", ref _wAgg);
            dataStore.SyncData("hind_war2_def", ref _wDef);
            dataStore.SyncData("hind_war2_aim", ref _wAim);
            dataStore.SyncData("hind_war2_targets", ref _wTargets);
            dataStore.SyncData("hind_war2_claimants", ref _wClaimants);
            dataStore.SyncData("hind_war2_start", ref _wStart);

            dataStore.SyncData("hind_war2_cwar", ref _cWar);
            dataStore.SyncData("hind_war2_cday", ref _cDay);
            dataStore.SyncData("hind_war2_ckind", ref _cKind);
            dataStore.SyncData("hind_war2_cdelta", ref _cDelta);
            dataStore.SyncData("hind_war2_csubj", ref _cSubj);

            dataStore.SyncData("hind_war2_trA", ref _trA);
            dataStore.SyncData("hind_war2_trB", ref _trB);
            dataStore.SyncData("hind_war2_trUntil", ref _trUntil);

            var tIds = _tributaryUntil.Keys.ToList(); var tVals = _tributaryUntil.Values.ToList();
            dataStore.SyncData("hind_war_tIds", ref tIds); dataStore.SyncData("hind_war_tVals", ref tVals);
            dataStore.SyncData("hind_war_lastbanner", ref _lastBannerDay);

            if (!dataStore.IsSaving)
            {
                _wAgg ??= new List<string>(); _wDef ??= new List<string>(); _wAim ??= new List<int>();
                _wTargets ??= new List<string>(); _wClaimants ??= new List<string>(); _wStart ??= new List<int>();
                _cWar ??= new List<string>(); _cDay ??= new List<int>(); _cKind ??= new List<int>();
                _cDelta ??= new List<float>(); _cSubj ??= new List<string>();
                _trA ??= new List<string>(); _trB ??= new List<string>(); _trUntil ??= new List<int>();

                _tributaryUntil = new Dictionary<string, int>();
                if (tIds != null && tVals != null)
                    for (int i = 0; i < tIds.Count && i < tVals.Count; i++) _tributaryUntil[tIds[i]] = tVals[i];
            }
        }

        // ── Console ──────────────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("war_status", "hindostan")]
        public static string WarStatus(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (Instance._wAgg.Count == 0) return "No wars are being fought.";

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Instance._wAgg.Count; i++)
            {
                Kingdom agg = Find(Instance._wAgg[i]);
                Kingdom def = Find(Instance._wDef[i]);
                if (agg == null || def == null) continue;
                var snap = Instance.SnapshotFor(agg, def);
                sb.AppendLine($"{agg.Name} -> {def.Name}: {WarProgressMath.AimName(snap.Aim)}, " +
                              $"{WarProgressMath.Percent(snap):0}% ({WarProgressMath.Headline(snap)}), score {snap.Score:0}");
                foreach (var l in Instance.ProgressBreakdown(agg, def))
                    sb.AppendLine($"    {l.Label}: {l.Value}");
            }
            return sb.ToString();
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("truces", "hindostan")]
        public static string Truces(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (Instance._trA.Count == 0) return "No truces stand.";
            var sb = new System.Text.StringBuilder("Standing truces:\n");
            for (int i = 0; i < Instance._trA.Count; i++)
                sb.AppendLine($"  {Find(Instance._trA[i])?.Name} / {Find(Instance._trB[i])?.Name} — " +
                              $"{Instance._trUntil[i] - Today} days left");
            return sb.ToString();
        }
    }
}
