using System;
using TaleWorlds.Library;

namespace TheHindostanMod.UI
{
    // A royal decree (farmaan) presented as a parchment popup. Supports a single
    // acknowledge button, or two buttons for an actionable summons (heed / defy).
    public class FarmaanVM : ViewModel
    {
        private readonly Action _onClose;
        private readonly Action _onPrimary;
        private readonly Action _onSecondary;

        private string _titleText;
        private string _senderText;
        private string _bodyText;
        private string _sealText;
        private string _primaryText;
        private string _secondaryText;
        private bool _hasSecondary;

        public FarmaanVM(string title, string sender, string body, string seal,
            string primaryText, Action onPrimary,
            string secondaryText, Action onSecondary, Action onClose)
        {
            _titleText = title ?? "Royal Farmaan";
            _senderText = sender ?? "";
            _bodyText = body ?? "";
            _sealText = seal ?? "By the Imperial Seal";
            _primaryText = primaryText ?? "Receive the decree";
            _secondaryText = secondaryText ?? "";
            _hasSecondary = !string.IsNullOrEmpty(secondaryText);
            _onPrimary = onPrimary;
            _onSecondary = onSecondary;
            _onClose = onClose;
        }

        [DataSourceProperty]
        public string TitleText { get => _titleText; set { if (_titleText != value) { _titleText = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string SenderText { get => _senderText; set { if (_senderText != value) { _senderText = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string BodyText { get => _bodyText; set { if (_bodyText != value) { _bodyText = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string SealText { get => _sealText; set { if (_sealText != value) { _sealText = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string PrimaryText { get => _primaryText; set { if (_primaryText != value) { _primaryText = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public string SecondaryText { get => _secondaryText; set { if (_secondaryText != value) { _secondaryText = value; OnPropertyChangedWithValue(value); } } }

        [DataSourceProperty]
        public bool HasSecondary { get => _hasSecondary; set { if (_hasSecondary != value) { _hasSecondary = value; OnPropertyChangedWithValue(value); } } }

        public void ExecutePrimary()
        {
            _onClose?.Invoke();
            _onPrimary?.Invoke();
        }

        public void ExecuteSecondary()
        {
            _onClose?.Invoke();
            _onSecondary?.Invoke();
        }
    }
}
