using System;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace TheHindostanMod.UI
{
    // Opens the REAL vanilla trade screen for a settlement (village or town), using
    // the game's own InventoryManager.OpenScreenAsTrade. We removed nothing from the
    // village economy — this simply re-exposes the buy/sell screen through a clearly
    // labelled menu option so the player can always reach a village's goods.
    //
    // The trade screen opener lives in the game's UI layer and its exact assembly /
    // signature shifts between versions, so we locate and invoke it by reflection and
    // fail softly. The goods, prices and rosters are entirely vanilla.
    public static class TradeScreenHelper
    {
        private static MethodInfo _open;          // OpenScreenAsTrade(...)
        private static bool _resolved;

        public static bool OpenTradeWith(Settlement settlement)
        {
            try
            {
                if (settlement == null) return false;
                object roster = settlement.ItemRoster;
                object component = settlement.IsVillage
                    ? (object)settlement.Village
                    : (object)settlement.Town;
                if (roster == null || component == null) return false;

                MethodInfo m = ResolveOpenMethod();
                if (m == null) return false;

                object[] args = BuildArgs(m, roster, component);
                if (args == null) return false;

                if (m.IsStatic) m.Invoke(null, args);
                else
                {
                    object inst = ResolveManagerInstance(m.DeclaringType);
                    if (inst == null) return false;
                    m.Invoke(inst, args);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static MethodInfo ResolveOpenMethod()
        {
            if (_resolved) return _open;
            _resolved = true;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (Type t in types)
                {
                    if (t == null || t.Name != "InventoryManager") continue;
                    MethodInfo cand = t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                        .FirstOrDefault(mi => mi.Name == "OpenScreenAsTrade");
                    if (cand != null) { _open = cand; return _open; }
                }
            }
            return _open;
        }

        // Fill the call: the first param the roster fits gets the roster, the first the
        // settlement component fits gets the component, everything else gets its default.
        private static object[] BuildArgs(MethodInfo m, object roster, object component)
        {
            ParameterInfo[] ps = m.GetParameters();
            object[] args = new object[ps.Length];
            bool rosterUsed = false, compUsed = false;
            for (int i = 0; i < ps.Length; i++)
            {
                Type pt = ps[i].ParameterType;
                if (!rosterUsed && pt.IsInstanceOfType(roster)) { args[i] = roster; rosterUsed = true; }
                else if (!compUsed && pt.IsInstanceOfType(component)) { args[i] = component; compUsed = true; }
                else if (ps[i].HasDefaultValue) args[i] = ps[i].DefaultValue;
                else if (pt.IsValueType) args[i] = Activator.CreateInstance(pt);
                else args[i] = null;
            }
            return rosterUsed ? args : null;
        }

        private static object ResolveManagerInstance(Type managerType)
        {
            PropertyInfo inst = managerType.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Static);
            return inst?.GetValue(null);
        }
    }
}
