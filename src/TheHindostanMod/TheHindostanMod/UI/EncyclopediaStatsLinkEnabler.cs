using System.Collections.Generic;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace TakhtyaTaboot.UI
{
    // The vanilla encyclopedia hero page renders the Info-section value widgets as rich text
    // (so they show link markup) but does NOT wire them for clicks — only the History line
    // carries Command.LinkClick="ExecuteLink". We add court-standing rows (liege, fiefs) as
    // encyclopedia links in that section (EncyclopediaHeroStatsPatch); this UIExtenderEx
    // prefab patch enables click handling on the value widget so those links actually
    // navigate, reusing the page's own ExecuteLink command.
    [PrefabExtension("EncyclopediaHeroPage", "//GridWidget[@Id='StatsGrid']//AutoHideRichTextWidget[@Text='@Value']")]
    internal sealed class EncyclopediaStatsLinkEnabler : PrefabExtensionSetAttributePatch
    {
        public override List<PrefabExtensionSetAttributePatch.Attribute> Attributes => new List<PrefabExtensionSetAttributePatch.Attribute>
        {
            new PrefabExtensionSetAttributePatch.Attribute("Command.LinkClick", "ExecuteLink"),
        };
    }
}
