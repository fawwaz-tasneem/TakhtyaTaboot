using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace TakhtyaTaboot
{
    // Legitimacy (0-100) is attached to the RULER of each kingdom: how widely their
    // right to rule is accepted. Separate from personal popularity. Low legitimacy
    // means lords obey less and challenges come more easily.
    public class LegitimacyBehavior : CampaignBehaviorBase
    {
        private Dictionary<string, float> _legitimacy = new Dictionary<string, float>(); // heroId -> 0..100

        public static LegitimacyBehavior Instance { get; private set; }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGame);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
        }

        public float GetLegitimacy(Hero ruler)
            => ruler != null && _legitimacy.TryGetValue(ruler.StringId, out float v) ? v : 60f;

        public void SetLegitimacy(Hero ruler, float value)
        {
            if (ruler != null) _legitimacy[ruler.StringId] = Math.Max(0f, Math.Min(100f, value));
        }

        public void ModifyLegitimacy(Hero ruler, float delta, string reason)
        {
            if (ruler == null) return;
            SetLegitimacy(ruler, GetLegitimacy(ruler) + delta);
            if (ruler == Hero.MainHero && Math.Abs(delta) >= 3f)
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Legitimacy {(delta > 0 ? "+" : "")}{delta:0}: {reason}",
                    delta > 0 ? Color.FromUint(0xFFD4AF37) : Color.FromUint(0xFFCC4400)));
        }

        public string GetTier(Hero ruler)
        {
            float l = GetLegitimacy(ruler);
            return l >= 80 ? "Unquestioned" : l >= 60 ? "Secure" : l >= 40 ? "Disputed"
                 : l >= 20 ? "Fragile" : "Illegitimate";
        }

        // How strongly lords answer this ruler (read by army cohesion + challenges).
        public float GetCallToArmsModifier(Hero ruler)
            => GetLegitimacy(ruler) switch
            {
                >= 80 => 1.20f,
                >= 60 => 1.00f,
                >= 40 => 0.75f,
                >= 20 => 0.45f,
                _     => 0.20f,
            };

        private void OnNewGame(CampaignGameStarter starter)
        {
            foreach (Kingdom k in Kingdom.All.Where(k => !k.IsEliminated && k.Leader != null))
                SetLegitimacy(k.Leader, 60f);
        }

        private void OnWeeklyTick()
        {
            foreach (Kingdom kingdom in Kingdom.All.Where(k => !k.IsEliminated && k.Leader != null))
            {
                float authority = ImperialAuthorityBehavior.Instance?.GetAuthority(kingdom) ?? 75f;
                if (authority < 40f)
                    ModifyLegitimacy(kingdom.Leader, -1f, "Imperial authority is collapsing");

                // Natural erosion toward 50 — every ruler faces the slow drift of doubt.
                float cur = GetLegitimacy(kingdom.Leader);
                if (cur > 50f) ModifyLegitimacy(kingdom.Leader, -0.5f, "Natural erosion of authority");
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            var ids = _legitimacy.Keys.ToList();
            var vals = _legitimacy.Values.ToList();
            dataStore.SyncData("hind_legit_ids", ref ids);
            dataStore.SyncData("hind_legit_vals", ref vals);
            if (!dataStore.IsSaving)
            {
                _legitimacy.Clear();
                for (int i = 0; i < ids.Count; i++)
                    _legitimacy[ids[i]] = i < vals.Count ? vals[i] : 60f;
            }
        }
    }
}
