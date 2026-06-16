using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TheHindostanMod.UI;

namespace TheHindostanMod
{
    // The War of Accession. When an Amir-ul-Umara challenges the throne, he raises the
    // standard of revolt: he and every house that backs him secede into a temporary
    // rebel kingdom and go to war with the throne. Break or capture the Emperor — or
    // outlast and outweigh his loyalists — and you seize the throne, your coalition
    // folding back into the realm under your crown. Lose, and your bid is crushed.
    public class AccessionWarBehavior : CampaignBehaviorBase
    {
        public static AccessionWarBehavior Instance { get; private set; }

        private bool _active;
        private string _kingdomId = "";        // the throne being contested (the old realm)
        private string _rebelKingdomId = "";   // the temporary rebel kingdom we raise
        private string _emperorId = "";
        private int _deadlineDay = -1;
        private List<string> _rebelClanIds = new List<string>();
        private List<string> _loyalistClanIds = new List<string>();

        public bool IsActive => _active;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, OnPrisonerTaken);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("hind_acc_active", ref _active);
            dataStore.SyncData("hind_acc_kingdom", ref _kingdomId);
            dataStore.SyncData("hind_acc_rebelkingdom", ref _rebelKingdomId);
            dataStore.SyncData("hind_acc_emperor", ref _emperorId);
            dataStore.SyncData("hind_acc_deadline", ref _deadlineDay);
            dataStore.SyncData("hind_acc_rebels", ref _rebelClanIds);
            dataStore.SyncData("hind_acc_loyal", ref _loyalistClanIds);
        }

        // ── Begin the challenge (called from the Mansabdari court) ──────────────────
        public void StartChallenge()
        {
            if (_active) { Notify("The realm is already aflame with your challenge.", true); return; }

            Kingdom kingdom = Hero.MainHero?.Clan?.Kingdom;
            Hero emperor = kingdom?.Leader;
            if (kingdom == null || emperor == null || emperor == Hero.MainHero) return;

            TallySides(kingdom, emperor);

            // Found the temporary rebel kingdom: the player's house secedes (keeping its
            // fiefs and declaring war), then every backing house joins the cause.
            Settlement seat = Clan.PlayerClan.Settlements.FirstOrDefault(s => s.IsTown)
                ?? Clan.PlayerClan.Settlements.FirstOrDefault(s => s.IsCastle)
                ?? Clan.PlayerClan.Settlements.FirstOrDefault()
                ?? Hero.MainHero.HomeSettlement
                ?? kingdom.Settlements.FirstOrDefault();
            if (seat == null) { Notify("You hold no seat from which to raise your claim.", true); return; }

            string causeName = $"{Hero.MainHero.Name}'s Claim";
            Kingdom rebel = RevoltCascadeBehavior.Instance?.CreateRebelKingdom(Clan.PlayerClan, seat, causeName);
            if (rebel == null) { Notify("The court is in disarray; the challenge cannot be raised.", true); return; }

            foreach (string id in _rebelClanIds.ToList())
            {
                if (id == Clan.PlayerClan.StringId) continue;
                Clan c = Clan.All.FirstOrDefault(x => x.StringId == id);
                if (c != null && c.Kingdom != rebel)
                    try { ChangeKingdomAction.ApplyByJoinToKingdom(c, rebel, default(CampaignTime), false); } catch { }
            }
            RevoltCascadeBehavior.Instance?.EnsureAtWar(rebel, kingdom);

            _kingdomId = kingdom.StringId;
            _rebelKingdomId = rebel.StringId;
            _emperorId = emperor.StringId;
            _deadlineDay = (int)CampaignTime.Now.ToDays + 45;
            _active = true;

            int rebelStr = (int)CoalitionStrength(_rebelClanIds);
            int loyalStr = (int)CoalitionStrength(_loyalistClanIds);

            RoyalFarmaan.Issue("The Standard of Accession Is Raised", $"Proclaimed by {Hero.MainHero.Name}",
                $"You cast down the standard of {emperor.Name} and lay claim to the throne of {kingdom.Name}. " +
                $"You and {RebelCount - 1} other house(s) secede as {causeName} and march to war; {LoyalCount} houses stand with the Emperor.\n\n" +
                $"Break or capture the Emperor — or outlast and outweigh his loyalists — and the throne is yours.\n\n" +
                $"(Your coalition: ~{rebelStr} men.  The Emperor's: ~{loyalStr} men.)",
                "For the throne!");

            Notify("The War of Accession has begun. Defeat the Emperor to claim the throne.", false);
        }

        private void TallySides(Kingdom kingdom, Hero emperor)
        {
            _rebelClanIds = new List<string> { Clan.PlayerClan.StringId };
            _loyalistClanIds = new List<string>();
            if (emperor.Clan != null) _loyalistClanIds.Add(emperor.Clan.StringId);

            foreach (Clan c in kingdom.Clans)
            {
                if (c == null || c.IsEliminated || c.Leader == null) continue;
                if (c == Clan.PlayerClan || c == emperor.Clan) continue;

                // Council members and the Emperor's kin hold to the throne. Otherwise a
                // noble rides with whoever he favours more.
                bool establishment = CouncilBehavior.Instance?.GetPostOf(c.Leader) != null;
                int relPlayer = CharacterRelationManager.GetHeroRelation(c.Leader, Hero.MainHero);
                int relEmperor = CharacterRelationManager.GetHeroRelation(c.Leader, emperor);

                if (!establishment && relPlayer > relEmperor) _rebelClanIds.Add(c.StringId);
                else _loyalistClanIds.Add(c.StringId);
            }
        }

        // ── Resolution ──────────────────────────────────────────────────────────────
        private void OnPrisonerTaken(PartyBase captorParty, Hero prisoner)
        {
            if (!_active) return;
            try
            {
                Hero emperor = Emperor;
                if (prisoner == emperor && IsRebelSide(captorParty)) { ResolveWin("the Emperor is taken captive on the field"); return; }
                if (prisoner == Hero.MainHero && IsLoyalSide(captorParty)) { ResolveLoss("you are taken captive by the imperial host"); return; }
            }
            catch { }
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (!_active) return;
            if (victim == Emperor) ResolveWin("the Emperor has fallen");
            else if (victim == Hero.MainHero) EndWar(); // dead challengers make no kings
        }

        private void OnDailyTick()
        {
            if (!_active) return;
            Kingdom rebel = RebelKingdom;
            Kingdom orig = TheKingdom;
            if (rebel == null || rebel.IsEliminated || !rebel.Settlements.Any()) { ResolveLoss("your rebel state was broken and scattered"); return; }
            if (orig == null || orig.IsEliminated) { ResolveWin("the old realm has collapsed before you"); return; }
            if (Emperor == null) { ResolveWin("the Emperor is no more"); return; }
            if ((int)CampaignTime.Now.ToDays >= _deadlineDay) ResolveBySimulation();
        }

        private void ResolveBySimulation()
        {
            float rebel = CoalitionStrength(_rebelClanIds) * MBRandom.RandomFloatRanged(0.8f, 1.2f);
            float loyal = CoalitionStrength(_loyalistClanIds) * MBRandom.RandomFloatRanged(0.8f, 1.2f);
            if (rebel >= loyal) ResolveWin("a season of war breaks the imperial host");
            else ResolveLoss("the imperial host grinds your coalition down");
        }

        private void ResolveWin(string how)
        {
            Kingdom orig = TheKingdom;
            Kingdom rebel = RebelKingdom;
            EndWar();
            if (orig == null) return;
            try
            {
                if (rebel != null && rebel.IsAtWarWith(orig)) MakePeaceAction.Apply(rebel, orig);
                if (rebel != null)
                    foreach (Clan c in rebel.Clans.ToList())
                        try { ChangeKingdomAction.ApplyByJoinToKingdom(c, orig, default(CampaignTime), false); } catch { }
                ChangeRulingClanAction.Apply(orig, Clan.PlayerClan);
                if (rebel != null && rebel != orig && !rebel.Settlements.Any()) DestroyKingdomAction.Apply(rebel);
            }
            catch { }
            RoyalFarmaan.Issue("The Throne is Won", $"Acclaimed at {orig.Name}",
                $"By force of arms, {how}. The amirs bow before you — you are acclaimed sovereign of {orig.Name}, and your " +
                "coalition folds back into the realm beneath your crown.",
                seal: "Long live the sovereign", primary: "I take up the burden of empire");
            LegitimacyBehavior.Instance?.SetLegitimacy(Hero.MainHero, 55f);
            ImperialAuthorityBehavior.Instance?.ModifyAuthority(orig, 10f, "victory in the War of Accession");
        }

        private void ResolveLoss(string how)
        {
            Hero emperor = Emperor;
            Kingdom orig = TheKingdom;
            Kingdom rebel = RebelKingdom;
            EndWar();
            try
            {
                if (rebel != null && orig != null && rebel.IsAtWarWith(orig)) MakePeaceAction.Apply(rebel, orig);
                if (rebel != null && orig != null)
                    foreach (Clan c in rebel.Clans.ToList())
                        try { ChangeKingdomAction.ApplyByJoinToKingdom(c, orig, default(CampaignTime), false); } catch { }
                if (rebel != null && rebel != orig && !rebel.Settlements.Any()) DestroyKingdomAction.Apply(rebel);
            }
            catch { }
            if (emperor != null)
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, emperor, -40);
            if (orig != null)
                ImperialAuthorityBehavior.Instance?.ModifyAuthority(orig, 8f, "the pretender was crushed");
            MansabdariBehavior.Instance?.DebugSetRank(Clan.PlayerClan, 1); // stripped to the lowest rank
            RoyalFarmaan.FromRuler(orig, "The Pretender Humbled",
                $"Your bid for the throne is broken — {how}. The Emperor strips your mansab and your standing at " +
                "court lies in ruins. Be grateful you keep your head.",
                "I submit");
        }

        private void EndWar()
        {
            _active = false;
            _kingdomId = ""; _rebelKingdomId = ""; _emperorId = ""; _deadlineDay = -1;
            _rebelClanIds = new List<string>();
            _loyalistClanIds = new List<string>();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────
        private Hero Emperor => string.IsNullOrEmpty(_emperorId) ? null
            : Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _emperorId);
        private Kingdom TheKingdom => string.IsNullOrEmpty(_kingdomId) ? null
            : Kingdom.All.FirstOrDefault(k => k.StringId == _kingdomId);
        private Kingdom RebelKingdom => string.IsNullOrEmpty(_rebelKingdomId) ? null
            : Kingdom.All.FirstOrDefault(k => k.StringId == _rebelKingdomId);

        private float CoalitionStrength(List<string> clanIds)
        {
            float total = 0f;
            foreach (string id in clanIds)
            {
                Clan c = Clan.All.FirstOrDefault(x => x.StringId == id);
                if (c != null && !c.IsEliminated) total += c.CurrentTotalStrength;
            }
            return total;
        }

        private bool IsRebelSide(PartyBase party) => SideContains(party, _rebelClanIds);
        private bool IsLoyalSide(PartyBase party) => SideContains(party, _loyalistClanIds);
        private static bool SideContains(PartyBase party, List<string> clanIds)
        {
            Clan c = party?.LeaderHero?.Clan ?? party?.Owner?.Clan;
            return c != null && clanIds.Contains(c.StringId);
        }

        private int RebelCount => _rebelClanIds.Count;
        private int LoyalCount => _loyalistClanIds.Count;

        private static void Notify(string text, bool bad)
            => InformationManager.DisplayMessage(new InformationMessage(text,
                bad ? Color.FromUint(0xFFCC4400) : Color.FromUint(0xFFD4AF37)));

        // ── Console (testing) ────────────────────────────────────────────────────────
        [CommandLineFunctionality.CommandLineArgumentFunction("accession_status", "hindostan")]
        public static string Status(List<string> args)
        {
            if (Campaign.Current == null || Instance == null) return "Load a campaign first.";
            if (!Instance._active) return "No War of Accession is active.";
            return $"War of Accession active. Rebels: {Instance.RebelCount} houses (~{Instance.CoalitionStrength(Instance._rebelClanIds):0}), " +
                   $"Loyalists: {Instance.LoyalCount} houses (~{Instance.CoalitionStrength(Instance._loyalistClanIds):0}). " +
                   $"Resolves by day {Instance._deadlineDay} if undecided.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("force_accession_win", "hindostan")]
        public static string ForceWin(List<string> args)
        {
            if (Campaign.Current == null || Instance == null || !Instance._active) return "No active War of Accession.";
            Instance.ResolveWin("by your command");
            return "You are made sovereign.";
        }
    }
}
