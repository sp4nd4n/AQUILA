using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace HackerSimulator;

/// <summary>Boot animation modelled on the reference video: a blue light chases along the logo,
/// the logo turns white and glows, dims, fades; a console text flash; black; login.</summary>
public partial class BootSplashView : UserControl
{
    private static readonly double[] BandShape = { -0.22, -0.07, 0.0, 0.07, 0.22 };

    private readonly Action _next;
    private readonly LinearGradientBrush _band = new() { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
    private readonly DropShadowEffect _glow = new()
    {
        Color = Color.FromRgb(0xA8, 0xD2, 0xFF),
        BlurRadius = 0,
        ShadowDepth = 0,
        Opacity = 0.95
    };

    public BootSplashView(Action next)
    {
        InitializeComponent();
        _next = next;

        var logo = new BitmapImage(new Uri("pack://application:,,,/Assets/aquila.png"));
        Chase.OpacityMask = new ImageBrush(logo) { Stretch = Stretch.Uniform };

        Color clear = Color.FromArgb(0, 0x1B, 0x8C, 0xFF);
        Color blue = Color.FromRgb(0x1B, 0x8C, 0xFF);
        Color white = Color.FromRgb(0xE8, 0xF4, 0xFF);
        Color[] colors = { clear, blue, white, blue, clear };
        for (int i = 0; i < colors.Length; i++)
            _band.GradientStops.Add(new GradientStop(colors[i], 1.5 + BandShape[i]));
        Chase.Fill = _band;

        LogoHost.Effect = _glow;
        Loaded += async (s, e) => await RunAsync();
    }

    private void AnimateBand(double from, double to, double seconds)
    {
        for (int i = 0; i < _band.GradientStops.Count; i++)
        {
            _band.GradientStops[i].BeginAnimation(GradientStop.OffsetProperty,
                new DoubleAnimation(from + BandShape[i], to + BandShape[i], TimeSpan.FromSeconds(seconds)));
        }
    }

    private static void Fade(UIElement element, double to, double seconds) =>
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromSeconds(seconds)));

    private void SetGlow(double blur, double seconds) =>
        _glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(blur, TimeSpan.FromSeconds(seconds)));

    private async Task Pass(bool full)
    {
        Fade(Chase, 1, 0.3);
        AnimateBand(1.3, 1.02, 0.7);                 // small blue glint at the bottom of the logo
        await Task.Delay(800);

        if (!full)
        {
            Fade(Chase, 0, 0.5);
            await Task.Delay(1200);
            return;
        }

        Fade(Base, 0.38, 1.6);                       // dim grey logo appears
        AnimateBand(1.02, -0.3, 3.6);                // the blue light runs up the strokes
        await Task.Delay(3700);
        Fade(Chase, 0, 0.5);

        Fade(Base, 1, 1.5);                          // logo turns bright white and glows
        SetGlow(38, 1.5);
        await Task.Delay(2600);

        Fade(Base, 0.42, 1.2);                       // back to grey
        SetGlow(0, 1.2);
        await Task.Delay(1300);

        Fade(Base, 0, 1.3);                          // and out
        await Task.Delay(1500);
        AnimateBand(1.3, 1.3, 0.01);                 // park the light below the logo again
    }

    private async Task TypeLine(string text)
    {
        foreach (char c in text)
        {
            ConsoleText.Text += c;
            await Task.Delay(14);
        }
    }

    private async Task RunAsync()
    {
        await Task.Delay(900);

        await Pass(full: true);
        await Pass(full: false);

        // console text flash, like the real boot
        ConsoleText.Visibility = Visibility.Visible;
        await TypeLine("AQUILA GNU/Linux Rolling aquila tty1");
        await Task.Delay(350);
        ConsoleText.Text += "\n\n";
        await TypeLine("aquila login: _");
        await Task.Delay(1300);
        ConsoleText.Visibility = Visibility.Collapsed;

        await Task.Delay(2200);                      // black screen while the "system" starts

        BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(600)));
        await Task.Delay(650);
        _next();
    }
}