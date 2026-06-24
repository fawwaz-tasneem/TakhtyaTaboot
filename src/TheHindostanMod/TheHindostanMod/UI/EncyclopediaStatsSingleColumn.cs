using System.Collections.Generic;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace TakhtyaTaboot.UI
{
    // The vanilla Info-section grid is two columns of 275px cells. Our added rows (feudal standing,
    // mansab, fiefs, liege) carry longer values than the short vanilla ones (Culture, Age), so a long
    // value overflowed its cell and rendered ON TOP of the neighbouring column's label — e.g.
    // "Sovereign (Padshah of Hindostan)" colliding with the "Mansab" label.
    //
    // Forcing the grid to a SINGLE column gives every stat its own full-width row, so nothing can
    // overlap another cell. The section is taller but clean.
    [PrefabExtension("EncyclopediaHeroPage", "//GridWidget[@Id='StatsGrid']")]
    internal sealed class EncyclopediaStatsSingleColumn : PrefabExtensionSetAttributePatch
    {
        public override List<PrefabExtensionSetAttributePatch.Attribute> Attributes => new List<PrefabExtensionSetAttributePatch.Attribute>
        {
            new PrefabExtensionSetAttributePatch.Attribute("ColumnCount", "1"),
        };
    }
}
