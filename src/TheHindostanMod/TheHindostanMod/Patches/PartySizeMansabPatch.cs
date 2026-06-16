using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;

namespace TheHindostanMod
{
    // Military benefit of rank: a larger personal contingent. Each mansab step adds
    // to the party member-size limit of any clan leader who holds that rank.
    [HarmonyPatch(typeof(DefaultPartySizeLimitModel), "GetPartyMemberSizeLimit")]
    public static class PartySizeMansabPatch
    {
        static void Postfix(PartyBase party, ref ExplainedNumber __result)
        {
            var clan = party?.MobileParty?.LeaderHero?.Clan;
            if (clan == null) return;
            int idx = MansabdariBehavior.Instance?.GetRankIndex(clan) ?? 0;
            if (idx > 0)
                __result.Add(idx * 15f, new TextObject("{=!}Mansabdari rank"));
        }
    }
}
