using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;

namespace TakhtyaTaboot
{
    // Military benefit of rank: the mansab's troop target acts as a FLOOR on the clan
    // leader's party cap. We raise the live, fully-computed vanilla limit up to the target
    // when it falls short, and leave it untouched when vanilla already grants more (high
    // clan tier, Leadership/Steward perks, kingdom policies). This lands the cap exactly ON
    // the target without the old promote->over-cap->demote spiral.
    //
    // The earlier version added (target - BaseTroopCapacity) on the ASSUMPTION that the
    // incoming vanilla limit equalled the BaseTroopCapacity constant. It never does — the
    // real limit varies per clan (tier/perks/policies) — so the cap drifted off target
    // (e.g. a target of 100 landed at 90 for a low-tier clan whose real base was ~20).
    // Keying off the live __result.ResultNumber instead makes the floor exact for everyone.
    // The retention floor the career system enforces is a fraction of this same target, so
    // it is always fieldable.
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
            float gap = target - __result.ResultNumber;
            if (gap > 0f)
                __result.Add(gap, new TextObject("{=!}Mansabdari rank"));
        }
    }
}
