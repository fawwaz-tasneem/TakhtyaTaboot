using System;
using System.Linq;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement.Categories;
using TakhtyaTaboot.Util;

namespace TakhtyaTaboot.UI
{
    // The clan-screen Zamindari fix (roadmap A.2). The engine cannot give a village an owner
    // separate from its bound town, so a village the player holds ONLY through our feudal
    // zamindari layer (beneath an AI town-lord) never appears in the vanilla clan "Fiefs" tab —
    // it isn't in Clan.Settlements. This UIExtenderEx mixin injects those zamindari villages into
    // the fiefs list after the tab rebuilds, so a player-zamindar can finally SEE his villages
    // where he looks for his holdings.
    //
    // Villages that DID take real engine ownership (the player's own zamindari can) already show,
    // and are skipped here to avoid duplicates. Everything is defensive: any failure degrades to
    // "the zamindari simply aren't listed", never a broken clan screen. ClanSettlementItemVM is
    // village-capable in v1.3 (it branches on IsVillage / Town == null), so the fief card renders.
    [ViewModelMixin("RefreshAllLists")]
    internal sealed class ClanFiefsZamindariMixin : BaseViewModelMixin<ClanFiefsVM>
    {
        public ClanFiefsZamindariMixin(ClanFiefsVM vm) : base(vm) { }

        public override void OnRefresh()
        {
            try
            {
                ClanFiefsVM vm = ViewModel;
                var ft = FeudalTitlesBehavior.Instance;
                if (vm == null || ft == null || Hero.MainHero == null) return;

                var villages = ft.GetVillagesLordedBy(Hero.MainHero);
                if (villages == null || villages.Count == 0) return;

                foreach (Settlement v in villages)
                {
                    if (v == null || !v.IsVillage) continue;
                    if (v.OwnerClan == Clan.PlayerClan) continue; // already an engine-owned fief; shown by vanilla
                    if (AlreadyListed(vm, v)) continue;

                    try { vm.Castles.Add(new ClanSettlementItemVM(v, _ => { }, () => { }, null)); }
                    catch (Exception e) { TYTLog.Error($"ClanFiefsZamindari: could not add {v.StringId}", e); }
                }
            }
            catch (Exception e) { TYTLog.Error("ClanFiefsZamindariMixin.OnRefresh failed", e); }
        }

        private static bool AlreadyListed(ClanFiefsVM vm, Settlement v)
            => (vm.Settlements != null && vm.Settlements.Any(x => x != null && x.Settlement == v))
            || (vm.Castles != null && vm.Castles.Any(x => x != null && x.Settlement == v));
    }
}
