using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia;

namespace PrivacyHardeningUI.Controls
{
    // Legacy/TextBlock-based icon helper renamed to avoid type conflict with the UserControl-based IconHelper
    public class IconHelperTextBlock : TextBlock
    {
        private sealed class ActionObserver<T> : System.IObserver<T>
        {
            private readonly System.Action<T> _action;
            public ActionObserver(System.Action<T> action) => _action = action;
            public void OnCompleted() { }
            public void OnError(System.Exception error) { }
            public void OnNext(T value) => _action(value);
        }

        public static readonly StyledProperty<string?> IconProperty =
            AvaloniaProperty.Register<IconHelperTextBlock, string?>(nameof(Icon));

        private static readonly Dictionary<string, string> _glyphMap = new()
        {
            { "settings", "\uE713" }, // Gear (Segoe MDL2)
            { "check", "\uE73E" },
            { "close", "\uE711" },
            { "menu", "\uE700" },
            { "search", "\uE721" },
            { "info", "\uE946" },
            { "warning", "\uE7BA" },
            { "privacy", "\uE72E" }
        };

        public string? Icon
        {
            get => GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public IconHelperTextBlock()
        {
            this.GetObservable(IconProperty).Subscribe(new ActionObserver<string?>(value => OnIconChanged(value)));
            this.FontFamily = (FontFamily)Application.Current?.Resources!["AppIconFontFamily"]!;
            this.FontSize = 16;
            this.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        }

        private void OnIconChanged(string? iconKey)
        {
            if (iconKey is null)
            {
                this.Text = string.Empty;
                return;
            }

            var key = iconKey.ToLowerInvariant();
            if (_glyphMap.TryGetValue(key, out var glyph))
            {
                this.Text = glyph;
            }
            else
            {
                // If not found, show the key as fallback
                this.Text = iconKey;
            }
        }
    }
}
