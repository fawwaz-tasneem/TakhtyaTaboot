using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;
using TakhtyaTaboot.UI;

namespace TakhtyaTaboot
{
    // Conveys the great acts of state through Royal Farmaans, so that war, peace and
    // every court vote reaches the player as a sealed proclamation rather than a silent
    // log line. Covers the engine's and Diplomacy's decisions; the mod's own calls
    // (musters, tribute, accession, council, career) already issue their own farmaans.
    public class RoyalDecisionsBehavior : CampaignBehaviorBase
    {
        private bool _ready;   // suppress the flurry of war/peace set at campaign start

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, () => _ready = true);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnMakePeace);
            CampaignEvents.KingdomDecisionAdded.AddNonSerializedListener(this, OnDecisionAdded);
            CampaignEvents.KingdomDecisionConcluded.AddNonSerializedListener(this, OnDecisionConcluded);
        }

        public override void SyncData(IDataStore dataStore) { /* no persistent state */ }

        private static Kingdom PlayerKingdom => Hero.MainHero?.Clan?.Kingdom;

        private void OnWarDeclared(IFaction f1, IFaction f2, DeclareWarAction.DeclareWarDetail detail)
        {
            Kingdom pk = PlayerKingdom;
            if (!_ready || pk == null) return;
            if (f1 != pk && f2 != pk) return;
            IFaction other = f1 == pk ? f2 : f1;
            if (other == null || !other.IsKingdomFaction) return;

            RoyalFarmaan.FromRuler(pk, "War Is Declared",
                $"Let it be known across {pk.Name}: the realm is now at war with {other.Name}. " +
                "Summon your banners, ready your fortresses, and steel the provinces for the trial of arms.",
                "For the realm!");
        }

        private void OnMakePeace(IFaction f1, IFaction f2, MakePeaceAction.MakePeaceDetail detail)
        {
            Kingdom pk = PlayerKingdom;
            if (!_ready || pk == null) return;
            if (f1 != pk && f2 != pk) return;
            IFaction other = f1 == pk ? f2 : f1;
            if (other == null || !other.IsKingdomFaction) return;

            RoyalFarmaan.FromRuler(pk, "Peace Is Concluded",
                $"A peace is sealed between {pk.Name} and {other.Name}. The swords are sheathed, the roads reopen, " +
                "and the provinces may breathe once more.",
                "A welcome respite");
        }

        private void OnDecisionAdded(KingdomDecision decision, bool isPlayerInvolved)
        {
            Kingdom pk = PlayerKingdom;
            if (!_ready || pk == null || decision == null || decision.Kingdom != pk) return;

            string title = decision.GetGeneralTitle()?.ToString() ?? "a matter of state";
            RoyalFarmaan.Issue("A Vote Is Called at Court",
                $"From the Imperial Council of {pk.Name}",
                $"The court calls a vote: {title}. The amirs will weigh the matter and cast their voices. " +
                "Make your will known at the kingdom council before the decision is sealed.",
                "I shall be heard");
        }

        private void OnDecisionConcluded(KingdomDecision decision, DecisionOutcome outcome, bool isPlayerInvolved)
        {
            Kingdom pk = PlayerKingdom;
            if (!_ready || pk == null || decision == null || decision.Kingdom != pk) return;

            string title = decision.GetGeneralTitle()?.ToString() ?? "the matter before the court";
            // A lighter notice for the result, so the court does not bury the player in seals.
            InformationManager.DisplayMessage(new InformationMessage(
                $"The court of {pk.Name} has decided: {title}.", Color.FromUint(0xFFD4AF37)));
        }
    }
}
