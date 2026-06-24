using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace TakhtyaTaboot.Util
{
    // Counts the enemies the PLAYER personally cuts down in a battle, so valour can be awarded
    // for them (WarfareBehavior reads Pending on battle end). Common bandits — looters, sea
    // raiders and the like — count for nothing; deserters (bhagode) and real soldiers count.
    //
    // Only blows struck by Agent.Main are tallied, so a captain's troops earn him no valour;
    // the deed must be his own hand.
    public sealed class PlayerKillValourLogic : MissionBehavior
    {
        // Qualifying kills accumulated in the current mission; taken (and zeroed) on battle end.
        public static int Pending;

        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        public PlayerKillValourLogic() { Pending = 0; }

        public static int Take() { int n = Pending; Pending = 0; return n; }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow blow)
        {
            try
            {
                if (affectorAgent == null || affectorAgent != Agent.Main) return;
                if (affectedAgent == null || affectedAgent == Agent.Main || !affectedAgent.IsHuman) return;
                if (!affectorAgent.IsEnemyOf(affectedAgent)) return;
                if (agentState != AgentState.Killed && agentState != AgentState.Unconscious) return;
                if (Qualifies(affectedAgent)) Pending++;
            }
            catch { /* a kill counter must never break the battle */ }
        }

        // Deserters count; other bandit/minor-faction rabble does not; soldiers of real
        // factions do.
        private static bool Qualifies(Agent victim)
        {
            PartyBase party = (victim.Origin as PartyAgentOrigin)?.Party;
            IFaction faction = party?.MapFaction;
            if (faction == null) return false;                 // tournament/practice/arena agents
            if (faction.StringId == "deserters") return true;  // bhagode
            if (faction.IsBanditFaction) return false;         // looters, raiders, …
            return true;                                       // real soldiers
        }
    }
}
