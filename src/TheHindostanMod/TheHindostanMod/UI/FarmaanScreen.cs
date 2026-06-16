using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace TakhtyaTaboot.UI
{
    // Issues royal farmaans as a focus layer over whatever screen is current
    // (usually the map). Adding a layer to the top screen — rather than pushing a
    // new ScreenBase — is safe to call from campaign ticks. Farmaans are queued so
    // only one shows at a time.
    public static class RoyalFarmaan
    {
        private class Pending
        {
            public string Title, Sender, Body, Seal, Primary, Secondary;
            public Action OnPrimary, OnSecondary;
        }

        private static readonly Queue<Pending> _queue = new Queue<Pending>();
        private static bool _active;
        private static GauntletLayer _layer;
        private static ScreenBase _host;
        private static FarmaanVM _vm;

        // sender: e.g. "By order of Shahenshah Muhammad Shah". seal: short attribution.
        public static void Issue(string title, string sender, string body, string seal = null,
            string primary = "Receive the decree", Action onPrimary = null,
            string secondary = null, Action onSecondary = null)
        {
            if (Campaign.Current == null) return;
            _queue.Enqueue(new Pending
            {
                Title = title, Sender = sender, Body = body, Seal = seal,
                Primary = primary, Secondary = secondary,
                OnPrimary = onPrimary, OnSecondary = onSecondary,
            });
            if (!_active) ShowNext();
        }

        // A decree from a kingdom's sovereign.
        public static void FromRuler(Kingdom kingdom, string title, string body,
            string primary = "As you command", Action onPrimary = null,
            string secondary = null, Action onSecondary = null)
        {
            Hero r = kingdom?.Leader;
            string sender = r != null
                ? $"By order of {kingdom.EncyclopediaRulerTitle} {r.Name}, sovereign of {kingdom.Name}"
                : "By order of the Imperial Court";
            Issue(title, sender, body, $"Sealed at the court of {kingdom?.Name}, {CurrentDate()}", primary, onPrimary, secondary, onSecondary);
        }

        // A summons or message from a hero's immediate liege.
        public static void FromLiege(Hero liege, string title, string body,
            string primary = "I obey", Action onPrimary = null,
            string secondary = null, Action onSecondary = null)
        {
            string honorific = liege != null ? Honorific(liege.Clan?.Kingdom) : "";
            string named = liege != null ? (string.IsNullOrEmpty(honorific) ? liege.Name.ToString() : $"{honorific} {liege.Name}") : "your liege lord";
            string sender = $"By the hand of {named}, your liege lord";
            Issue(title, sender, body, $"By the seal of your liege, {CurrentDate()}", primary, onPrimary, secondary, onSecondary);
        }

        // The in-game date, themed for 1719 Hindostan, so decrees feel dated and real.
        public static string CurrentDate()
        {
            try
            {
                if (Campaign.Current == null) return "";
                int year = 1719 + Math.Max(0, CampaignTime.Now.GetYear - 1084);
                return $"the {CampaignTime.Now.GetDayOfSeason + 1}th day of {CampaignTime.Now.GetSeasonOfYear}, {year} AD";
            }
            catch { return ""; }
        }

        // A kingdom's ruler-title honorific (Padshah, Maharajadhiraja, …), or "".
        public static string Honorific(Kingdom kingdom) => kingdom?.EncyclopediaRulerTitle?.ToString() ?? "";

        // A hero named with their realm's honorific, e.g. "Padshah Muhammad Shah".
        public static string NameWithHonorific(Hero hero)
        {
            if (hero == null) return "";
            string h = Honorific(hero.Clan?.Kingdom);
            return string.IsNullOrEmpty(h) || hero.Clan?.Kingdom?.Leader != hero ? hero.Name.ToString() : $"{h} {hero.Name}";
        }

        private static void ShowNext()
        {
            if (_queue.Count == 0) { _active = false; return; }
            ScreenBase top = ScreenManager.TopScreen;
            if (top == null) { _active = false; return; }

            _active = true;
            Pending p = _queue.Dequeue();
            try
            {
                _vm = new FarmaanVM(p.Title, p.Sender, p.Body, p.Seal,
                    p.Primary, p.OnPrimary, p.Secondary, p.OnSecondary, Close);
                _layer = new GauntletLayer("HindostanFarmaan", 1010, false);
                _layer.LoadMovie("HindostanFarmaan", _vm);
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                top.AddLayer(_layer);
                _layer.IsFocusLayer = true;
                ScreenManager.TrySetFocus(_layer);
                _host = top;
            }
            catch
            {
                // If the layer cannot be shown, don't stall the queue.
                CleanupLayer();
                _active = false;
            }
        }

        private static void Close()
        {
            CleanupLayer();
            ShowNext();
        }

        private static void CleanupLayer()
        {
            if (_layer != null && _host != null)
            {
                _layer.IsFocusLayer = false;
                _host.RemoveLayer(_layer);
            }
            _layer = null;
            _host = null;
            FarmaanVM vm = _vm;
            _vm = null;
            vm?.OnFinalize();
        }
    }
}
