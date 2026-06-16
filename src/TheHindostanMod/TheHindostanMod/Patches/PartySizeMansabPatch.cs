using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;

namespace TakhtyaTaboot
{
    // Military benefit of rank: your personal contingent grows to the mansab's troop
    // target. Rather than a flat per-step bonus (which could demand more men than it
    // granted, causing a promote->over-cap->demote spiral), we add exactly the gap
    // between the rank's target and the assumed vanilla base, so a clan leader's party
    // cap lands ON the target (plus any genuine perk bonuses). The retention floor the
    // career system enforces is a fraction of this same target, so it is always fieldable.
    [HarmonyPatch(typeof(DefaultPartySizeLimitModel), "GetPartyMemberSizeLimit")]
    public static class PartySizeMansabPatch
    {
        static void Postfix(PartyBase party, ref ExplainedNumber __result)
        {
            var clan = party?.MobileParty?.LeaderHero?.Clan;
            if (clan == null) return;
            int idx = MansabdariBehavior.Instance?.GetRankIndex(clan) ?? 0;
            if (idx <= 0) return;

            int target = MansabdariBehavior.RequiredTroopsForIndex(idx);
            float bonus = target - Config.Tune.BaseTroopCapacity;
            if (bonus > 0f)
                __result.Add(bonus, new TextObject("{=!}Mansabdari rank"));
        }
    }
}
