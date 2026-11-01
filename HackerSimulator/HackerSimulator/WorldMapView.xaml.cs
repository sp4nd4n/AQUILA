using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace HackerSimulator;

public partial class WorldMapView : UserControl
{
    private const double Cell = 9;                       // pixels per 3-degree map cell
    private const double MapW = GeoData.Cols * Cell;     // 1080
    private const double MapH = GeoData.Rows * Cell;     // 432

    private readonly Random _rng = new();
    private readonly Path _dots;
    private Func<bool> _stopped = () => false;

    private Brush Cyan => (Brush)FindResource("CyanBrush");
    private Brush Amber => (Brush)FindResource("AmberBrush");
    private Brush Red => (Brush)FindResource("RedBrush");
    private Brush White => (Brush)FindResource("WhiteBrush");
    private Brush Gray => (Brush)FindResource("GrayBrush");

    public WorldMapView()
    {
        InitializeComponent();

        var group = new GeometryGroup();
        for (int r = 0; r < GeoData.Rows; r++)
            for (int c = 0; c < GeoData.Cols; c++)
                if (GeoData.MapRows[r][c] == '#')
                    group.Children.Add(new EllipseGeometry(new Point((c + 0.5) * Cell, (r + 0.5) * Cell), 2.7, 2.7));
        group.Freeze();

        _dots = new Path
        {
            Data = group,
            Fill = new SolidColorBrush(Color.FromRgb(0x4F, 0x73, 0x87)),
            Opacity = 0.2
        };
        MapCanvas.Children.Add(_dots);
    }

    private static Point Pos(double lat, double lon) => new(
        Math.Clamp((lon + 180) / 3.0 * Cell, 6, MapW - 6),
        Math.Clamp((84 - lat) / 3.0 * Cell, 6, MapH - 6));

    private static Color ColorOf(Brush b) => ((SolidColorBrush)b).Color;

    public async Task PlayAsync(IReadOnlyList<City> hops, City target, Func<bool> stopped)
    {
        _stopped = stopped;

        StatusText.Text = "ESTABLISHING SATELLITE LINK...";
        Sfx.Beep();
        await SweepAsync();

        City? prev = null;
        for (int i = 0; i < hops.Count; i++)
        {
            if (_stopped()) return;
            var h = hops[i];
            StatusText.Text = $"ROUTING  //  HOP {i + 1} OF {hops.Count + 1}  //  {h.Name.ToUpperInvariant()}";

            var pt = Pos(h.Lat, h.Lon);
            if (prev != null) await DrawArcAsync(Pos(prev.Lat, prev.Lon), pt, Cyan);
            AddPing(pt, Cyan, h.Code, 8);
            Sfx.Beep();
            await Task.Delay(380);
            prev = h;
        }

        if (_stopped()) return;
        StatusText.Text = $"ROUTING  //  HOP {hops.Count + 1} OF {hops.Count + 1}  //  LOCKING TARGET";
        var tp = Pos(target.Lat, target.Lon);
        if (prev != null) await DrawArcAsync(Pos(prev.Lat, prev.Lon), tp, Amber);

        await LockOnAsync(tp, target);
    }

    private async Task SweepAsync()
    {
        var bar = new Rectangle
        {
            Width = 90,
            Height = MapH,
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0x00, 0x00, 0xD8, 0xFF), 0.0),
                new GradientStop(Color.FromArgb(0x66, 0x00, 0xD8, 0xFF), 0.9),
                new GradientStop(Color.FromArgb(0xCC, 0xB8, 0xF4, 0xFF), 1.0)
            }, 0)
        };
        Canvas.SetTop(bar, 0);
        MapCanvas.Children.Add(bar);

        bar.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(-90, MapW, TimeSpan.FromMilliseconds(1100)));
        _dots.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(1100)));
        await Task.Delay(1150);
        MapCanvas.Children.Remove(bar);
    }

    private void AddPing(Point p, Brush color, string label, double size)
    {
        var core = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = color,
            Effect = new DropShadowEffect { Color = ColorOf(color), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.95 }
        };
        Canvas.SetLeft(core, p.X - size / 2);
        Canvas.SetTop(core, p.Y - size / 2);

        var scale = new ScaleTransform(1, 1);
        var ring = new Ellipse
        {
            Width = size,
            Height = size,
            Stroke = color,
            StrokeThickness = 1.5,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale
        };
        Canvas.SetLeft(ring, p.X - size / 2);
        Canvas.SetTop(ring, p.Y - size / 2);

        var grow = new DoubleAnimation(1, 7, TimeSpan.FromMilliseconds(1500)) { RepeatBehavior = RepeatBehavior.Forever };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        ring.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.9, 0, TimeSpan.FromMilliseconds(1500)) { RepeatBehavior = RepeatBehavior.Forever });

        var text = new TextBlock
        {
            Text = label,
            Foreground = color,
            FontFamily = (FontFamily)FindResource("MonoFont"),
            FontSize = 12,
            Opacity = 0
        };
        Canvas.SetLeft(text, p.X + 9);
        Canvas.SetTop(text, p.Y - 20);
        text.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));

        MapCanvas.Children.Add(ring);
        MapCanvas.Children.Add(core);
        MapCanvas.Children.Add(text);
    }

    private async Task DrawArcAsync(Point a, Point b, Brush color)
    {
        double dist = (b - a).Length;
        var ctrl = new Point((a.X + b.X) / 2, Math.Max(0, Math.Min(a.Y, b.Y) - dist * 0.28));

        var line = new Polyline { Stroke = color, StrokeThickness = 1.6, Opacity = 0.9 };
        line.Points.Add(a);
        MapCanvas.Children.Add(line);

        const int steps = 34;
        for (int i = 1; i <= steps; i++)
        {
            if (_stopped()) return;
            double t = (double)i / steps;
            double u = 1 - t;
            line.Points.Add(new Point(
                u * u * a.X + 2 * u * t * ctrl.X + t * t * b.X,
                u * u * a.Y + 2 * u * t * ctrl.Y + t * t * b.Y));
            await Task.Delay(13);
        }
    }

    private async Task LockOnAsync(Point tp, City target)
    {
        var fade = new DoubleAnimation(0, 0.7, TimeSpan.FromMilliseconds(500));
        var h = new Line { X1 = 0, X2 = MapW, Y1 = tp.Y, Y2 = tp.Y, Stroke = Red, StrokeThickness = 1 };
        var v = new Line { X1 = tp.X, X2 = tp.X, Y1 = 0, Y2 = MapH, Stroke = Red, StrokeThickness = 1 };
        h.BeginAnimation(OpacityProperty, fade);
        v.BeginAnimation(OpacityProperty, fade);
        MapCanvas.Children.Add(h);
        MapCanvas.Children.Add(v);

        var scale = new ScaleTransform(1, 1);
        var ring = new Ellipse
        {
            Width = 260,
            Height = 260,
            Stroke = Red,
            StrokeThickness = 2,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale
        };
        Canvas.SetLeft(ring, tp.X - 130);
        Canvas.SetTop(ring, tp.Y - 130);
        MapCanvas.Children.Add(ring);

        var shrink = new DoubleAnimation(1, 0.12, TimeSpan.FromMilliseconds(900))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);

        Sfx.Beep();
        await Task.Delay(950);
        if (_stopped()) return;

        AddPing(tp, Red, "TARGET", 12);
        StatusText.Text = "TARGET LOCATED";
        StatusText.Foreground = Red;
        Sfx.Glitch();

        bool exact = target.Exact;
        double lat = target.Lat + (exact ? 0 : (_rng.NextDouble() - 0.5) * 0.06);
        double lon = target.Lon + (exact ? 0 : (_rng.NextDouble() - 0.5) * 0.06);
        string coords = $"{Math.Abs(lat):0.0000}° {(lat >= 0 ? "N" : "S")}   {Math.Abs(lon):0.0000}° {(lon >= 0 ? "E" : "W")}";

        InfoText.Inlines.Clear();
        if (target.Name.Length > 0)
        {
            InfoText.Inlines.Add(new Run(target.Name.ToUpperInvariant()) { Foreground = Amber });
            InfoText.Inlines.Add(new Run("   |   ") { Foreground = Gray });
        }
        InfoText.Inlines.Add(new Run(coords) { Foreground = White });
        InfoText.Inlines.Add(new Run("   |   ACCURACY ") { Foreground = Gray });
        InfoText.Inlines.Add(new Run($"±{_rng.Next(3, 12)} m") { Foreground = Cyan });

        await Task.Delay(2200);
    }
}