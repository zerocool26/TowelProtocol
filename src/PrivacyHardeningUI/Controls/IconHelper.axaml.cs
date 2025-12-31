using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PrivacyHardeningUI.Controls
{
    public partial class IconHelper : UserControl
    {
        public static new readonly StyledProperty<string> NameProperty = AvaloniaProperty.Register<IconHelper, string>(nameof(Name));
        public static new readonly StyledProperty<IBrush> ForegroundProperty = AvaloniaProperty.Register<IconHelper, IBrush>(nameof(Foreground));

        public IconHelper()
        {
            InitializeComponent();
            DataContext = this;
        }

        public new string Name
        {
            get => GetValue(NameProperty);
            set => SetValue(NameProperty, value);
        }

        public new IBrush Foreground
        {
            get => GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }
    }
}
