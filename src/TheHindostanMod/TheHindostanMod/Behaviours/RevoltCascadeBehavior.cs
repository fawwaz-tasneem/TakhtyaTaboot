using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TakhtyaTaboot.UI;

namespace TakhtyaTaboot
{
    // Revolt Cascade — design: wiki/RevoltCascade-Design.md
    //
    // Provincial unrest (pressure 0-100 per settlement) fed by collapsing authority,
    // disloyal/poor-standing holders, bandit threat, religious mismatch and weak
    // legitimacy. At high pressure a province ignites: a clan secedes into a PROVISIONAL
    // rebel Kingdom — a real kingdom, so vanilla AND the Diplomacy mod make the war fully
    // playable (declare/negotiate peace, war exhaustion, treaties). Survive the
    // consolidation window -> permanent state; be crushed -> lords imprisoned, the most
    // hostile executed.
    public class RevoltCascadeBehavior : CampaignBehaviorBase
    {
        public static RevoltCascadeBehavior Instance { get; private set; }

        private const float WarnAt = 50f;
        private const float IgniteAt = 80f;
        private const int ConsolidationDays = 60;
        private const int DisloyalRelation = -20;
        private const int ExecutionRelation = -30;

        public enum RevoltType { NobleSecession, RegionalBreakaway, ReligiousUprising, PeasantRevolt }

        // ── State (parallel lists) ──
        private List<string> _pSettleIds = new List<string>();
        private List<float>  _pValues    = new List<float>();
        private List<string> _warned     = new List<string>();
        private List<string> _provKIds   = new List<string>();   // provisional rebel kingdom ids
        private List<int>    _provUntil   = new List<int>();       // day it consolidates
        private List<string> _provParent  = new List<string>();   // parent kingdom id

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, () => Util.TYTLog.Guard("RevoltCascade.WeeklyTick", OnWeeklyTick));
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => Util.TYTLog.Guard("RevoltCascade.DailyTick", OnDailyTick));
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("hind_rev_pIds", ref _pSettleIds);
            dataStore.SyncData("hind_rev_pVals", ref _pValues);
            dataStore.SyncData("hind_rev_warned", ref _warned);
            dataStore.SyncData("hind_rev_provKIds", ref _provKIds);
            dataStore.SyncData("hind_rev_provUntil", ref _provUntil);
            dataStore.SyncData("hind_rev_provParent", ref _provParent);
        }

        // ── Public API ──
        public float GetPressure(Settlement s)
        {
            int i = _pSettleIds.IndexOf(s?.StringId ?? "");
            return i < 0 ? 0f : _pValues[i];
        }

        public void SetPressure(Settlement s, float v)
        {
            if (s == null) return;
            v = Math.Max(0f, Math.Min(100f, v));
            int i = _pSettleIds.IndexOf(s.StringId);
            if (i < 0) { _pSettleIds.Add(s.StringId); _pValues.Add(v); }
            else _pValues[i] = v;
        }

        public void AddPressure(Settlement s, float amount) => SetPressure(s, GetPressure(s) + amount);

        public bool IsProvisional(Kingdom k) => k != null && _provKIds.Contains(k.StringId);

        // ── Weekly: pressure + ignition ──
        private void OnWeeklyTick()
        {
            foreach (Settlement s in Settlement.All)
            {
                if (!(s.IsTown || s.IsCastle || s.IsVillage)) continue;
                Kingdom k = s.OwnerClan?.Kingdom;
                if (k == null || IsProvisional(k)) continue;

                float before = GetPressure(s);
                float after = Math.Max(0f, Math.Min(100f, before + ComputePressureDelta(s, k)));
                SetPressure(s, after);

                if (after >= WarnAt && before < WarnAt && !_warned.Contains(s.StringId))
                {
                    _warned.Add(s.StringId);
                    if (k == Hero.MainHero?.Clan?.Kingdom)
                        RoyalFarmaan.FromRuler(k, "Unrest in the Provinces",
                            $"Word reaches the court that the country around {s.Name} seethes with discontent. " +
                            "Left unchecked, it will boil over into open revolt.", "We are warned",
                            dedupeKey: "unrest:" + s.StringId, priority: Util.FarmaanPriority.Routine, cooldownDays: 15);
                }
                if (after < WarnAt) _warned.Remove(s.StringId);

                if (after >= IgniteAt)
                {
                    bool disloyalOwner = s.OwnerClan?.Leader != null && k.Leader != null
                        && CharacterRelationManager.GetHeroRelation(s.OwnerClan.Leader, k.Leader) <= DisloyalRelation;
                    float chance = disloyalOwner ? 0.20f : 0.10f;
                    if (MBRandom.RandomFloat < chance)
                        TryIgnite(s, k);
                }
            }
        }

        private float ComputePressureDelta(Settlement s, Kingdom k)
        {
            float d = 0f;
            float authority = ImperialAuthorityBehavior.Instance?.GetAuthority(k) ?? 75f;
            if (authority < 25f) d += 5f; else if (authority < 50f) d += 2f;

            Hero owner = s.OwnerClan?.Leader;
            if (owner != null && k.Leader != null && owner != k.Leader)
            {
                int rel = CharacterRelationManager.GetHeroRelation(owner, k.Leader);
                if (rel <= DisloyalRelation) d += 4f;
            }
            if (FiefHierarchyBehavior.Instance != null && s.OwnerClan == Clan.PlayerClan
                && FiefHierarchyBehavior.Instance.GetDaysInPoorStanding() > 0)
                d += 3f;

            if (s.IsVillage && (VillageDevelopmentBehavior.Instance?.GetThreat(s) ?? 0f) > 70f) d += 2f;

            if (ReligiousMismatch(s, k)) d += 3f;

            float legit = k.Leader != null ? (LegitimacyBehavior.Instance?.GetLegitimacy(k.Leader) ?? 60f) : 60f;
            if (legit < 40f) d += 2f; else if (legit >= 70f) d -= 2f;

            int wars = Kingdom.All.Count(o => o != k && !o.IsEliminated && k.IsAtWarWith(o));
            if (wars >= 2) d += 2f;

            // Mitigators
            if ((s.Town?.GarrisonParty?.MemberRoster?.TotalManCount ?? 0) > 50) d -= 4f;
            if (MobileParty.MainParty?.CurrentSettlement == s || s.OwnerClan?.Leader?.CurrentSettlement == s) d -= 5f;

            return d;
        }

        private bool ReligiousMismatch(Settlement s, Kingdom k)
        {
            if (ReligionBehavior.Instance == null || k.Leader == null) return false;
            Religion populace = ReligionBehavior.Instance.GetCultureReligion(s.Culture);
            Religion ruler = ReligionBehavior.Instance.GetReligion(k.Leader);
            return populace != Religion.None && ruler != Religion.None && populace != ruler;
        }

        // ── Ignition ──
        private void TryIgnite(Settlement origin, Kingdom parent)
        {
            try { Ignite(origin, parent); }
            catch (Exception e)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "Revolt ignition failed: " + e.Message, Color.FromUint(0xFFAA3333)));
            }
        }

        private void Ignite(Settlement origin, Kingdom parent)
        {
            Hero ruler = parent.Leader;
            Hero ownerLeader = origin.OwnerClan?.Leader;
            bool ownerDisloyal = ownerLeader != null && ruler != null && ownerLeader != ruler
                && CharacterRelationManager.GetHeroRelation(ownerLeader, ruler) <= DisloyalRelation;

            RevoltType type = DetermineType(origin, parent, ownerDisloyal);

            // A rebellion is always led by an existing house that takes its own clan and
            // fiefs out of the empire — never a fabricated clan. The seething settlement's
            // own lord secedes; if that settlement belongs to the throne itself, the most
            // disloyal vassal house raises the standard instead.
            Clan rebelClan = (origin.OwnerClan != null && origin.OwnerClan != parent.RulingClan)
                ? origin.OwnerClan
                : parent.Clans.Where(c => !c.IsEliminated && c.Leader != null && c != parent.RulingClan)
                        .OrderBy(c => parent.Leader != null ? CharacterRelationManager.GetHeroRelation(c.Leader, parent.Leader) : 0)
                        .FirstOrDefault();
            if (rebelClan?.Leader == null) return; // no house willing to lead — no revolt

            string name = RebelName(type, origin, rebelClan);
            Kingdom rebel = CreateRebelKingdom(rebelClan, origin, name);
            if (rebel == null) return;

            EnsureAtWar(rebel, parent);
            RallyDisloyalLords(parent, rebel, rebelClan.Leader, type);

            _provKIds.Add(rebel.StringId);
            _provUntil.Add((int)CampaignTime.Now.ToDays + ConsolidationDays);
            _provParent.Add(parent.StringId);

            ImperialAuthorityBehavior.Instance?.ModifyAuthority(parent, -5f, "a province has risen in revolt");
            SetPressure(origin, 0f);
            _warned.Remove(origin.StringId);

            if (parent == Hero.MainHero?.Clan?.Kingdom && ruler == Hero.MainHero)
                EmperorResponse(parent, rebel, origin, type);
            else
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{rebelClan.Leader.Name} of {rebelClan.Name} raises {name} in revolt against {parent.Name}!",
                    Color.FromUint(0xFFCC2200)));
        }

        private string RebelName(RevoltType type, Settlement origin, Clan rebelClan)
        {
            string place = origin?.Name?.ToString() ?? rebelClan?.Name?.ToString() ?? "the Provinces";
            switch (type)
            {
                case RevoltType.ReligiousUprising:
                    Religion r = ReligionBehavior.Instance?.GetCultureReligion(origin?.Culture) ?? Religion.None;
                    return r == Religion.Sikh ? "The Khalsa Raj"
                         : r == Religion.Hindu ? $"The Dharma Sena of {place}"
                         : $"The Faithful of {place}";
                case RevoltType.RegionalBreakaway: return $"Free {place}";
                case RevoltType.NobleSecession: return $"Dominion of {rebelClan?.Name}";
                default: return $"The {place} Rebellion";
            }
        }

        private RevoltType DetermineType(Settlement origin, Kingdom parent, bool ownerDisloyal)
        {
            if (ReligiousMismatch(origin, parent)) return RevoltType.ReligiousUprising;
            if (ownerDisloyal) return origin.IsTown ? RevoltType.RegionalBreakaway : RevoltType.NobleSecession;
            return RevoltType.PeasantRevolt;
        }

        public Kingdom CreateRebelKingdom(Clan rebelClan, Settlement origin, string name)
        {
            try
            {
                // A vassal must throw off its allegiance before it can found a kingdom.
                // Leaving by rebellion keeps the clan's fiefs and declares war on the parent.
                if (rebelClan.Kingdom != null)
                    ChangeKingdomAction.ApplyByLeaveWithRebellionAgainstKingdom(rebelClan, false);

                Kingdom k = Kingdom.CreateKingdom("hind_rebel_" + origin.StringId + "_" + (int)CampaignTime.Now.ToDays);
                TextObject n = new TextObject(name);
                Banner banner = rebelClan.Banner ?? Banner.CreateRandomClanBanner(MBRandom.RandomInt());
                k.InitializeKingdom(n, n, rebelClan.Culture, banner, rebelClan.Color, rebelClan.Color2, origin,
                    new TextObject($"{name} — a state forged in rebellion against the empire."), n,
                    new TextObject("Rebel Lord"));
                ChangeKingdomAction.ApplyByCreateKingdom(rebelClan, k, true);
                return k;
            }
            catch (Exception e)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "Rebel kingdom creation failed: " + e.Message, Color.FromUint(0xFFAA3333)));
                return null;
            }
        }

        public void EnsureAtWar(Kingdom rebel, Kingdom parent)
        {
            if (rebel == null || parent == null || rebel == parent) return;
            if (!rebel.IsAtWarWith(parent))
            {
                try { DeclareWarAction.ApplyByDefault(rebel, parent); } catch { }
            }
        }

        // Other disloyal lords may flock to the rebel banner (max 3).
        private void RallyDisloyalLords(Kingdom parent, Kingdom rebel, Hero rebelLeader, RevoltType type)
        {
            int joined = 0;
            Religion rebelFaith = ReligionBehavior.Instance?.GetReligion(rebelLeader) ?? Religion.None;
            foreach (Clan c in parent.Clans.Where(c => !c.IsEliminated && c.Leader != null
                        && c != rebel.RulingClan && c != parent.RulingClan).ToList())
            {
                if (joined >= 3) break;
                int rel = parent.Leader != null ? CharacterRelationManager.GetHeroRelation(c.Leader, parent.Leader) : 0;
                bool faithBond = rebelFaith != Religion.None
                    && ReligionBehavior.Instance?.GetReligion(c.Leader) == rebelFaith;
                if (rel <= -25 || (faithBond && rel <= 0 && MBRandom.RandomFloat < 0.5f))
                {
                    try { ChangeKingdomAction.ApplyByJoinToKingdom(c, rebel, default(CampaignTime), false); joined++; } catch { }
                }
            }
            if (joined > 0)
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{joined} disloyal house{(joined == 1 ? "" : "s")} flock to the rebel banner.",
                    Color.FromUint(0xFFCC4400)));
        }

        // ── Emperor's response (player is the sovereign) ──
        private void EmperorResponse(Kingdom parent, Kingdom rebel, Settlement origin, RevoltType type)
        {
            string rebelLord = rebel.Leader != null ? $"{rebel.Leader.Name} of {rebel.RulingClan?.Name}" : "a rebel lord";
            string body = $"{rebelLord} has cast off your authority, seized {origin.Name}, and proclaimed {rebel.Name}. " +
                          "The court awaits your command, Majesty.";
            RoyalFarmaan.Issue("Rebellion!", $"A rising in {origin.Name}", body, "By the Imperial Seal",
                "Crush them by force of arms", () =>
                {
                    EnsureAtWar(rebel, parent);
                    Notify($"You vow to crush {rebel.Name}. The imperial host marches.", false);
                },
                "Grant them autonomy", () => GrantAutonomy(parent, rebel));
        }

        private void GrantAutonomy(Kingdom parent, Kingdom rebel)
        {
            if (rebel == null || parent == null) return;
            try { if (rebel.IsAtWarWith(parent)) Util.ThroneWar.WithInternalPeace(() => MakePeaceAction.Apply(rebel, parent)); } catch { }
            Establish(rebel, "granted autonomy by the throne");
            ImperialAuthorityBehavior.Instance?.ModifyAuthority(parent, -10f, "autonomy conceded to a breakaway");
            Notify($"You recognise {rebel.Name} as autonomous. The empire bends rather than breaks.", true);
        }

        // ── Daily: consolidation + crush ──
        private void OnDailyTick()
        {
            int today = (int)CampaignTime.Now.ToDays;
            for (int i = _provKIds.Count - 1; i >= 0; i--)
            {
                Util.TYTLog.Crumb("provisional rebel " + _provKIds[i]);
                Kingdom rebel = Kingdom.All.FirstOrDefault(x => x.StringId == _provKIds[i]);
                if (rebel == null || rebel.IsEliminated || !rebel.Settlements.Any())
                {
                    Crush(rebel, i);
                    continue;
                }
                if (today >= _provUntil[i])
                    Establish(rebel, "endured and won its independence");
            }
        }

        private void Establish(Kingdom rebel, string how)
        {
            int i = _provKIds.IndexOf(rebel?.StringId ?? "");
            if (i < 0) return;
            Kingdom parent = Kingdom.All.FirstOrDefault(x => x.StringId == _provParent[i]);
            RemoveProvisional(i);
            if (rebel == null) return;

            if (parent?.Leader != null)
                LegitimacyBehavior.Instance?.ModifyLegitimacy(parent.Leader, -3f, "a province broke free for good");

            string msg = $"The breakaway state of {rebel.Name} has {how}. It now stands among the powers of Hindostan.";
            if (parent == Hero.MainHero?.Clan?.Kingdom)
                RoyalFarmaan.Issue("A Province Lost", "From the Imperial Chronicle", msg, "So it is recorded");
            else
                InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(0xFFCC4400)));
        }

        private void Crush(Kingdom rebel, int provIndex)
        {
            Kingdom parent = Kingdom.All.FirstOrDefault(x => x.StringId == _provParent[provIndex]);
            RemoveProvisional(provIndex);
            if (rebel == null) { return; }

            Hero sovereign = parent?.Leader;
            var rebelLords = rebel.Clans.Where(c => !c.IsEliminated && c.Leader != null && c.Leader.IsAlive)
                                        .Select(c => c.Leader).ToList();

            if (sovereign == Hero.MainHero && rebelLords.Count > 0)
                JudgeCaptivesFarmaan(parent, rebel, rebelLords);
            else
                ResolveCaptives(parent, rebelLords, autoExecuteRingleaders: sovereign != null);

            if (parent != null) ImperialAuthorityBehavior.Instance?.ModifyAuthority(parent, 8f, "a rebellion crushed");
            try { DestroyKingdomAction.Apply(rebel); } catch { }

            string msg = $"The revolt of {rebel.Name} is crushed and its kingdom dissolved.";
            if (parent == Hero.MainHero?.Clan?.Kingdom) { /* handled by judge farmaan */ }
            else InformationManager.DisplayMessage(new InformationMessage(msg, Color.FromUint(0xFFD4AF37)));
        }

        private void JudgeCaptivesFarmaan(Kingdom parent, Kingdom rebel, List<Hero> rebelLords)
        {
            var ringleaders = rebelLords.Where(h => parent?.Leader != null
                && CharacterRelationManager.GetHeroRelation(h, parent.Leader) <= ExecutionRelation).ToList();

            var elements = new List<InquiryElement>
            {
                new InquiryElement("imprison", "Imprison them all", null, true,
                    "Every rebel lord is taken captive. Merciful by the standards of kings."),
                new InquiryElement("execute", $"Execute the {ringleaders.Count} ringleader(s), imprison the rest", null,
                    ringleaders.Count > 0, "The bitterest enemies are put to death; the others jailed. Fear settles the realm."),
                new InquiryElement("pardon", "Pardon them", null, true,
                    "Clemency to bind wounds — but rebels spared once may rise again."),
            };

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"The Fate of the Rebels of {rebel.Name}",
                $"{rebel.Name} is crushed. {rebelLords.Count} rebel lord(s) kneel before you. How shall they be judged?",
                elements, false, 1, 1, "Pronounce the decree", "",
                sel =>
                {
                    string choice = sel != null && sel.Count > 0 ? (string)sel[0].Identifier : "imprison";
                    ApplyJudgement(parent, rebelLords, ringleaders, choice);
                }, null, ""), true);
        }

        private void ApplyJudgement(Kingdom parent, List<Hero> rebelLords, List<Hero> ringleaders, string choice)
        {
            Hero sovereign = parent?.Leader;
            PartyBase captor = FindCaptorParty(parent);
            foreach (Hero lord in rebelLords)
            {
                if (lord == null || !lord.IsAlive) continue;
                if (choice == "execute" && ringleaders.Contains(lord) && sovereign != null)
                    KillCharacterAction.ApplyByExecution(lord, sovereign, true, true);
                else if (choice == "pardon")
                    { if (sovereign != null) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(lord, sovereign, 10); }
                else
                    Imprison(lord, captor);
            }
            if (choice == "execute" && parent != null)
                ImperialAuthorityBehavior.Instance?.ModifyAuthority(parent, 4f, "rebel ringleaders executed");
        }

        // AI sovereign auto-resolves captives.
        private void ResolveCaptives(Kingdom parent, List<Hero> rebelLords, bool autoExecuteRingleaders)
        {
            Hero sovereign = parent?.Leader;
            PartyBase captor = FindCaptorParty(parent);
            foreach (Hero lord in rebelLords)
            {
                if (lord == null || !lord.IsAlive) continue;
                bool ringleader = sovereign != null
                    && CharacterRelationManager.GetHeroRelation(lord, sovereign) <= ExecutionRelation;
                // Never let RNG execute the player — capture is the loss state.
                if (autoExecuteRingleaders && ringleader && lord != Hero.MainHero && MBRandom.RandomFloat < 0.4f)
                    KillCharacterAction.ApplyByExecution(lord, sovereign, false, true);
                else
                    Imprison(lord, captor);
            }
        }

        private void Imprison(Hero lord, PartyBase captor)
        {
            try
            {
                if (captor != null) TakePrisonerAction.Apply(captor, lord);
                else KillCharacterAction.ApplyByRemove(lord, false, true);
            }
            catch { }
        }

        private PartyBase FindCaptorParty(Kingdom parent)
        {
            if (parent == null) return null;
            PartyBase p = parent.Leader?.PartyBelongedTo?.Party;
            if (p != null) return p;
            Settlement holder = parent.Settlements.FirstOrDefault(s => s.IsTown || s.IsCastle);
            return holder?.Party;
        }

        // ── Player-led revolt ──
        public string PlayerSecede()
        {
            Kingdom k = Hero.MainHero?.Clan?.Kingdom;
            if (k == null) return "You serve no kingdom to rebel against.";
            if (k.Leader == Hero.MainHero) return "You are the sovereign; you cannot revolt against yourself.";
            var fiefs = Clan.PlayerClan.Settlements.Where(s => s.IsTown || s.IsCastle || s.IsVillage).ToList();
            if (fiefs.Count == 0) return "You hold no fief to carry into rebellion. Acquire one first.";

            Settlement seat = fiefs.FirstOrDefault(s => s.IsTown) ?? fiefs.FirstOrDefault(s => s.IsCastle) ?? fiefs[0];
            string name = $"{Hero.MainHero.Name}'s Dominion";
            Kingdom rebel = CreateRebelKingdom(Clan.PlayerClan, seat, name);
            if (rebel == null) return "The revolt could not be raised.";

            EnsureAtWar(rebel, k);
            RallyDisloyalLords(k, rebel, Hero.MainHero, RevoltType.NobleSecession);
            _provKIds.Add(rebel.StringId);
            _provUntil.Add((int)CampaignTime.Now.ToDays + ConsolidationDays);
            _provParent.Add(k.StringId);
            ImperialAuthorityBehavior.Instance?.ModifyAuthority(k, -8f, "the player raised the standard of revolt");

            RoyalFarmaan.Issue("The Standard of Revolt is Raised", $"Proclaimed by {Hero.MainHero.Name}",
                $"You cast off the yoke of {k.Name} and proclaim {name}. Hold your lands for {ConsolidationDays} days " +
                "and your kingdom will be recognised among the powers of Hindostan. Fail, and face the emperor's justice.",
                "So begins the war");
            return $"You have seceded as {name}. War with {k.Name} is declared.";
        }

        // ── Reactions ──
        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (!Util.WorldGen.Ready) return; // skip the parallel world-gen distribution (see Util/WorldGen.cs)
            Kingdom newK = settlement.OwnerClan?.Kingdom;
            // A rebel kingdom that just lost its last settlement will be crushed next daily tick.
            // A rebel seizing a settlement from the parent erodes imperial authority.
            for (int i = 0; i < _provParent.Count; i++)
            {
                Kingdom parent = Kingdom.All.FirstOrDefault(x => x.StringId == _provParent[i]);
                if (parent != null && newK == Kingdom.All.FirstOrDefault(x => x.StringId == _provKIds[i]))
                    ImperialAuthorityBehavior.Instance?.ModifyAuthority(parent, -5f, "a rebel seized a province");
            }
        }

        private void RemoveProvisional(int i)
        {
            if (i < 0 || i >= _provKIds.Count) return;
            _provKIds.RemoveAt(i);
            _provUntil.RemoveAt(i);
            _provParent.RemoveAt(i);
        }

        // ── Cheat support ──
        public string DebugTriggerRevolt()
        {
            Settlement s = Settlement.CurrentSettlement
                ?? Clan.PlayerClan?.Kingdom?.Settlements.FirstOrDefault(x => x.IsTown);
            if (s == null) return "Enter or own a settlement first.";
            Kingdom k = s.OwnerClan?.Kingdom;
            if (k == null) return $"{s.Name} belongs to no kingdom.";
            SetPressure(s, 100f);
            TryIgnite(s, k);
            return $"Forced a revolt at {s.Name}.";
        }

        public string DebugCrushAll()
        {
            int n = _provKIds.Count;
            for (int i = _provKIds.Count - 1; i >= 0; i--)
            {
                Kingdom rk = Kingdom.All.FirstOrDefault(x => x.StringId == _provKIds[i]);
                Crush(rk, i);
            }
            return $"Crushed {n} provisional rebel kingdom(s).";
        }

        public string DescribeUnrest()
        {
            var hot = Settlement.All.Where(s => GetPressure(s) >= 20f)
                .OrderByDescending(GetPressure).Take(15).ToList();
            string unrest = hot.Count == 0 ? "No notable unrest." :
                string.Join("\n", hot.Select(s => $"{s.Name}: {GetPressure(s):0} ({s.OwnerClan?.Kingdom?.Name})"));
            string rebels = _provKIds.Count == 0 ? "No provisional rebel kingdoms." :
                "Provisional rebel kingdoms:\n" + string.Join("\n", _provKIds.Select((id, i) =>
                {
                    var rk = Kingdom.All.FirstOrDefault(x => x.StringId == id);
                    return $"  {(rk?.Name?.ToString() ?? id)} — consolidates day {_provUntil[i]} (now {(int)CampaignTime.Now.ToDays})";
                }));
            return unrest + "\n\n" + rebels;
        }

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));
    }
}
