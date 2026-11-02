using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace HackerSimulator;

public partial class EagleLogo : UserControl
{
    private readonly DropShadowEffect _glow = new()
    {
        Color = Color.FromRgb(0x9C, 0xCB, 0xFF),
        BlurRadius = 16,
        ShadowDepth = 0,
        Opacity = 0.7
    };

    public EagleLogo() => InitializeComponent();

    public bool Glow
    {
        get => Pic.Effect != null;
        set => Pic.Effect = value ? _glow : null;
    }
}