using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PrivacyHardeningUI.Controls
{
    public partial class IconHelper : UserControl
    {
        private sealed class ActionObserver<T> : System.IObserver<T>
        {
            private readonly System.Action<T> _action;
            public ActionObserver(System.Action<T> action) => _action = action;
            public void OnCompleted() { }
            public void OnError(System.Exception error) { }
            public void OnNext(T value) => _action(value);
        }

        public static new readonly StyledProperty<string> NameProperty = AvaloniaProperty.Register<IconHelper, string>(nameof(Name));
        public static new readonly StyledProperty<IBrush> ForegroundProperty = AvaloniaProperty.Register<IconHelper, IBrush>(nameof(Foreground));

        private static readonly System.Collections.Generic.Dictionary<string, string> _glyphMap = new()
        {
            { "home", "\uE80F" },
            { "settings", "\uE713" },
            { "check", "\uE73E" },
            { "check_circle", "\uE73E" },
            { "close", "\uE711" },
            { "menu", "\uE700" },
            { "search", "\uE721" },
            { "info", "\uE946" },
            { "warning", "\uE7BA" },
            { "privacy", "\uE72E" },
            { "theme", "\uE790" },
            { "folder", "\uE8B7" },
            { "bar_chart", "\uE9D2" },
            { "diff", "\uE8D2" },
            { "shield", "\uE730" },
            { "history", "\uE81C" },
            { "report", "\uE9D5" },

            // Workflow / security tool icons (best-effort glyphs; can be replaced with SVG assets later)
            { "undo", "\uE7A7" },
            { "lock", "\uE72E" },
            { "export", "\uE9D5" },
            { "pin", "\uE718" },

            // Mechanism icons
            { "registry", "\uE713" },
            { "service", "\uE7FC" },
            { "task", "\uE823" },
            { "firewall", "\uE7BA" },
            { "powershell", "\uE756" },
            { "gpo", "\uE8B7" },
            { "mdm", "\uE946" },
            { "hosts", "\uE8B7" },
            { "wfp", "\uE7BA" }
        };

        public IconHelper()
        {
            InitializeComponent();
            DataContext = this;

            // Ensure icon font and visual are applied
            try
            {
                if (Application.Current?.Resources?.ContainsKey("AppIconFontFamily") == true)
                {
                    PART_Icon.FontFamily = (FontFamily)Application.Current.Resources["AppIconFontFamily"]!;
                }
            }
            catch
            {
                // ignore and let default font render fallback text
            }

            // React to Name changes and update the displayed glyph or SVG
            this.GetObservable(NameProperty).Subscribe(new ActionObserver<string>(value => OnNameChanged(value)));
        }

        public new string Name
        {
            get => GetValue(NameProperty) ?? string.Empty;
            set => SetValue(NameProperty, value);
        }

        public new IBrush Foreground
        {
            get => GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        private void OnNameChanged(string? name)
        {
            if (PART_Icon == null)
                return;

            if (string.IsNullOrWhiteSpace(name))
            {
                PART_Icon.Text = string.Empty;
                if (PART_Svg != null)
                {
                    PART_Svg.Source = null;
                    PART_Svg.IsVisible = false;
                }
                return;
            }

            var key = name.ToLowerInvariant();
            if (_glyphMap.TryGetValue(key, out var glyph))
            {
                PART_Icon.Text = glyph;
                if (PART_Svg != null)
                {
                    PART_Svg.Source = null;
                    PART_Svg.IsVisible = false;
                }
            }
            else
            {
                // try to load an SVG asset from the running folder first (Assets/Icons/{name}.svg)
                try
                {
                    var exeDir = AppContext.BaseDirectory ?? string.Empty;
                    var filePath = System.IO.Path.Combine(exeDir, "Assets", "Icons", $"{key}.svg");
                    if (System.IO.File.Exists(filePath) && PART_Svg != null)
                    {
                        using var fs = System.IO.File.OpenRead(filePath);
                        var bmp = new Avalonia.Media.Imaging.Bitmap(fs);
                        PART_Svg.Source = bmp;
                        PART_Svg.IsVisible = true;
                        PART_Icon.Text = string.Empty;
                        return;
                    }
                }
                catch
                {
                    // ignore and fallback to glyph/text
                }

                // fallback to the provided name if no glyph or svg is found
                PART_Icon.Text = name;
                if (PART_Svg != null)
                {
                    PART_Svg.Source = null;
                    PART_Svg.IsVisible = false;
                }
            }
        }
    }
}
