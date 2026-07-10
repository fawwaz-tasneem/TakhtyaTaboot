using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot
{
    // Parley at the walls (playtest round 2). Vanilla gives the BESIEGED a parley option but the
    // besieger has none — walls fall only by assault or starvation. This adds the attacker's
    // envoy: treat with the qiladar (garrison commander) of a fort you besiege.
    //   • BRIBE — coin opens the gates while the defence still stands; expensive, no stain.
    //   • TERMS — the garrison marches out free and the town is spared; costs nothing, but only
    //     a desperate commander accepts. Once the gates open the player chooses: HONOUR the
    //     terms (legitimacy +, the old owner respects the conduct) or DEFY them and seize the
    //     garrison as prisoners (legitimacy and authority bleed, grudges are written, the city
    //     remembers).
    // Whether the qiladar treats at all is deterministic and explained (SiegeParleyMath, tested):
    // the player is told his resolve and what would break it, not diced against.
    public class SiegeParleyBehavior : CampaignBehaviorBase
    {
        private const string ParleyMenuId = "hindostan_siege_parley";

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore ds) { }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption("menu_siege_strategies", "hindostan_parley_envoy",
                "{=!}Send an envoy to the qiladar",
                EnvoyCondition, args => GameMenu.SwitchToMenu(ParleyMenuId), false, 3);

            starter.AddGameMenu(ParleyMenuId, "{=!}{HINDOSTAN_PARLEY_TEXT}", ParleyInit);

            starter.AddGameMenuOption(ParleyMenuId, "hindostan_parley_bribe",
                "{=!}Offer a bribe of {PARLEY_BRIBE} dinars to open the gates",
                BribeCondition, args => TYTLog.Guard("SiegeParley.Bribe", AcceptBribe));

            starter.AddGameMenuOption(ParleyMenuId, "hindostan_parley_terms",
                "{=!}Offer terms: the garrison marches free and the city is spared",
                TermsCondition, args => TYTLog.Guard("SiegeParley.Terms", AcceptTerms));

            starter.AddGameMenuOption(ParleyMenuId, "hindostan_parley_back", "{=!}Return to the siege lines",
                args => { args.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                args => GameMenu.SwitchToMenu("menu_siege_strategies"), true);
        }

        // ── State reads ──────────────────────────────────────────────────────────────
        private static Settlement Besieged
            => PlayerSiege.PlayerSiegeEvent?.BesiegedSettlement;

        private static bool PlayerLeadsTheSiege
            => MobileParty.MainParty.Army == null || MobileParty.MainParty.Army.LeaderParty == MobileParty.MainParty;

        private static float AttackerStrength(SiegeEvent se)
            => se?.BesiegerCamp?.GetInvolvedPartiesForEventType().Sum(p => p.EstimatedStrength) ?? 1f;

        private static float DefenderStrength(Settlement s)
        {
            float str = s.Party?.EstimatedStrength ?? 0f;
            if (s.Town?.GarrisonParty?.Party != null) str += s.Town.GarrisonParty.Party.EstimatedStrength;
            foreach (MobileParty p in s.Parties)
                if (p != null && p.IsLordParty && p.Party != s.Town?.GarrisonParty?.Party && p.MapFaction == s.MapFaction)
                    str += p.Party.EstimatedStrength;
            return str;
        }

        private static float FoodDays(Settlement s)
        {
            Town t = s.Town;
            if (t == null) return 60f;
            float change = t.FoodChange;
            return change < 0f ? t.FoodStocks / -change : 60f;
        }

        private static float CurrentResolve(Settlement s)
            => SiegeParleyMath.Resolve(DefenderStrength(s), AttackerStrength(s.SiegeEvent), FoodDays(s));

        private static int CurrentBribe(Settlement s)
            => SiegeParleyMath.BribeCost(s.Town?.GarrisonParty?.MemberRoster.TotalManCount ?? 0,
                s.Town?.Prosperity ?? 0f, CurrentResolve(s));

        // ── Menu plumbing ────────────────────────────────────────────────────────────
        private static bool EnvoyCondition(MenuCallbackArgs args)
        {
            if (PlayerSiege.PlayerSiegeEvent == null || PlayerSiege.PlayerSide != BattleSideEnum.Attacker) return false;
            Settlement s = Besieged;
            if (s == null || !s.IsFortification) return false;
            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
            if (!PlayerLeadsTheSiege)
            { args.IsEnabled = false; args.Tooltip = new TextObject("{=!}Only the commander of the siege may treat with the garrison."); }
            else if (PlayerSiege.PlayerSiegeEvent.BesiegerCamp?.LeaderParty?.MapEvent != null)
            { args.IsEnabled = false; args.Tooltip = new TextObject("{=!}You cannot parley during an ongoing battle."); }
            return true;
        }

        private void ParleyInit(MenuCallbackArgs args)
        {
            Settlement s = Besieged;
            if (s == null) { GameMenu.SwitchToMenu("menu_siege_strategies"); return; }
            float resolve = CurrentResolve(s);
            int garrison = s.Town?.GarrisonParty?.MemberRoster.TotalManCount ?? 0;
            int food = (int)FoodDays(s);
            MBTextManager.SetTextVariable("PARLEY_BRIBE", CurrentBribe(s).ToString(), false);
            MBTextManager.SetTextVariable("HINDOSTAN_PARLEY_TEXT",
                $"Your envoy rides to the walls of {s.Name} under a white flag.\n \n" +
                $"The qiladar commands some {garrison} men behind the walls, with perhaps {food} days of bread. " +
                $"Against him you muster {(int)AttackerStrength(s.SiegeEvent)} strength to his {(int)DefenderStrength(s)}.\n \n" +
                $"His resolve is {SiegeParleyMath.ResolveTier(resolve)}. " +
                (SiegeParleyMath.AcceptsTerms(resolve)
                    ? "He knows the walls cannot hold; he will treat for his men's lives."
                    : SiegeParleyMath.AcceptsBribe(resolve)
                        ? "He will not yield for honour's sake — but every man has a price."
                        : "He sees no reason to yield. Starve the granaries or mass more strength, and his arithmetic will change."),
                false);
        }

        private static bool BribeCondition(MenuCallbackArgs args)
        {
            Settlement s = Besieged;
            if (s == null) return false;
            args.optionLeaveType = GameMenuOption.LeaveType.BribeAndEscape;
            float resolve = CurrentResolve(s);
            int cost = CurrentBribe(s);
            MBTextManager.SetTextVariable("PARLEY_BRIBE", cost.ToString(), false);
            if (!SiegeParleyMath.AcceptsBribe(resolve))
            { args.IsEnabled = false; args.Tooltip = new TextObject("{=!}The qiladar's resolve is too firm for coin. Weaken the defence and try again."); }
            else if (Hero.MainHero.Gold < cost)
            { args.IsEnabled = false; args.Tooltip = new TextObject($"{{=!}}He demands {cost} dinars; you carry {Hero.MainHero.Gold}."); }
            return true;
        }

        private static bool TermsCondition(MenuCallbackArgs args)
        {
            Settlement s = Besieged;
            if (s == null) return false;
            args.optionLeaveType = GameMenuOption.LeaveType.Surrender;
            if (!SiegeParleyMath.AcceptsTerms(CurrentResolve(s)))
            { args.IsEnabled = false; args.Tooltip = new TextObject("{=!}Only a broken commander accepts terms. His men still believe the walls will hold."); }
            return true;
        }

        // ── The three endings ────────────────────────────────────────────────────────
        private static void AcceptBribe()
        {
            Settlement s = Besieged;
            if (s == null) return;
            int cost = CurrentBribe(s);
            if (!SiegeParleyMath.AcceptsBribe(CurrentResolve(s)) || Hero.MainHero.Gold < cost) return;

            Hero.MainHero.ChangeHeroGold(-cost);
            TakeBySurrender(s, seizeGarrison: false);
            InformationManager.DisplayMessage(new InformationMessage(
                $"Gold changed hands in the night; at dawn the gates of {s.Name} stand open. The garrison marches out with its shame.",
                Color.FromUint(0xFFD4AF37)));
            TYTLog.Info($"SiegeParley: {s.StringId} taken by bribe ({cost} dinars).");
        }

        private static void AcceptTerms()
        {
            Settlement s = Besieged;
            if (s == null || !SiegeParleyMath.AcceptsTerms(CurrentResolve(s))) return;

            Hero formerOwner = s.OwnerClan?.Leader;
            Hero formerKing = (s.MapFaction as Kingdom)?.Leader;

            // The choice is put in the player's hands the moment the gates open.
            UI.RoyalFarmaan.Issue("The Gates Open Upon Your Word",
                $"Before the walls of {s.Name}",
                $"The qiladar accepts your terms: the garrison marches out under arms, the city is spared the sack, " +
                "and your banner rises over the gate. His men file past your lines, trusting the word you gave.",
                seal: "Your word, given before both hosts",
                primary: "Honour the terms — let them march",
                onPrimary: () => TYTLog.Guard("SiegeParley.Honour", () => ResolveTerms(s, formerOwner, formerKing, honoured: true)),
                secondary: "Defy them — seize the garrison",
                onSecondary: () => TYTLog.Guard("SiegeParley.Defy", () => ResolveTerms(s, formerOwner, formerKing, honoured: false)));
        }

        private static void ResolveTerms(Settlement s, Hero formerOwner, Hero formerKing, bool honoured)
        {
            TakeBySurrender(s, seizeGarrison: !honoured);
            Kingdom pk = Hero.MainHero.Clan?.Kingdom;

            if (honoured)
            {
                LegitimacyBehavior.Instance?.ModifyLegitimacy(Hero.MainHero, +4f, $"honoured the terms at {s.Name}");
                if (pk != null) ImperialAuthorityBehavior.Instance?.ModifyAuthority(pk, +2f, "a city taken on honoured terms");
                if (formerOwner != null)
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, formerOwner, +3, false);
                    OpinionBehavior.Instance?.AddOpinion(formerOwner, Hero.MainHero, OpinionMath.OpinionType.Favor);
                }
                InformationManager.DisplayMessage(new InformationMessage(
                    $"The garrison of {s.Name} marches free. Word of your good faith travels farther than any army.",
                    Color.FromUint(0xFFD4AF37)));
            }
            else
            {
                LegitimacyBehavior.Instance?.ModifyLegitimacy(Hero.MainHero, -8f, $"broke sworn terms at {s.Name}");
                if (pk != null) ImperialAuthorityBehavior.Instance?.ModifyAuthority(pk, -3f, "an oath broken before both hosts");
                if (formerOwner != null)
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, formerOwner, -15, false);
                    OpinionBehavior.Instance?.AddOpinion(formerOwner, Hero.MainHero, OpinionMath.OpinionType.Grudge, -20f);
                }
                if (formerKing != null && formerKing != formerOwner)
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, formerKing, -10, false);
                    OpinionBehavior.Instance?.AddOpinion(formerKing, Hero.MainHero, OpinionMath.OpinionType.Grudge);
                }
                if (s.Town != null) s.Town.Loyalty = MBMath.ClampFloat(s.Town.Loyalty - 10f, 0f, 100f);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"The garrison of {s.Name} is seized as it files out. The city — and all Hindostan — will remember whose word this was.",
                    Color.FromUint(0xFFCC4400)));
            }
            TYTLog.Info($"SiegeParley: {s.StringId} taken by terms, honoured={honoured}.");
        }

        // The bloodless capture: garrison handled per the deal, siege wound down the way the
        // engine's own leave-siege path does, then ownership passes by siege (openToClaim, so
        // the realm's usual fief politics apply to the new conquest).
        private static void TakeBySurrender(Settlement s, bool seizeGarrison)
        {
            SiegeEvent se = s.SiegeEvent;
            MobileParty garrison = s.Town?.GarrisonParty;

            if (garrison != null)
            {
                if (seizeGarrison)
                {
                    var roster = garrison.MemberRoster;
                    for (int i = 0; i < roster.Count; i++)
                    {
                        var e = roster.GetElementCopyAtIndex(i);
                        if (e.Character != null && !e.Character.IsHero && e.Number > 0)
                            MobileParty.MainParty.PrisonRoster.AddToCounts(e.Character, e.Number);
                    }
                }
                try { DestroyPartyAction.Apply(null, garrison); } catch (System.Exception e) { TYTLog.Error("SiegeParley: garrison removal failed", e); }
            }

            try { se?.FinalizeSiegeEvent(); } catch (System.Exception e) { TYTLog.Error("SiegeParley: siege finalize failed", e); }
            ChangeOwnerOfSettlementAction.ApplyBySiege(Hero.MainHero, Hero.MainHero, s);

            if (PlayerEncounter.Current != null) PlayerEncounter.Finish();
            else GameMenu.ExitToLast();
        }
    }
}
