using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HackerSimulator;

/// <summary>Small vector icons (folder, file, trash, terminal) used on the desktop and in the file manager.</summary>
public static class Icons
{
    private static Brush Br(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    private static Viewbox Wrap(Canvas canvas, double size) => new()
    {
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        Child = canvas
    };

    private static Path Shape(string data, string fill) => new() { Data = Geometry.Parse(data), Fill = Br(fill) };

    public static UIElement Folder(double size)
    {
        var c = new Canvas { Width = 56, Height = 56 };
        c.Children.Add(Shape("M4,12 Q4,8 8,8 L22,8 L27,14 L48,14 Q52,14 52,18 L52,44 Q52,48 48,48 L8,48 Q4,48 4,44 Z", "#3B82C4"));
        c.Children.Add(Shape("M4,22 Q4,18 8,18 L48,18 Q52,18 52,22 L52,44 Q52,48 48,48 L8,48 Q4,48 4,44 Z", "#63B3F0"));
        return Wrap(c, size);
    }

    public static UIElement File(double size)
    {
        var c = new Canvas { Width = 56, Height = 56 };
        c.Children.Add(Shape("M12,6 L34,6 L44,16 L44,50 L12,50 Z", "#E5E9F0"));
        c.Children.Add(Shape("M34,6 L34,16 L44,16 Z", "#AEB7C4"));
        c.Children.Add(new Path
        {
            Data = Geometry.Parse("M18,26 L38,26 M18,32 L38,32 M18,38 L32,38"),
            Stroke = Br("#8B96A6"),
            StrokeThickness = 2
        });
        return Wrap(c, size);
    }

    public static UIElement Trash(double size)
    {
        var c = new Canvas { Width = 56, Height = 56 };
        c.Children.Add(new Path
        {
            Data = Geometry.Parse("M22,10 L22,6 L34,6 L34,10"),
            Stroke = Br("#9AA4B2"),
            StrokeThickness = 3
        });

        var lid = new Rectangle { Width = 38, Height = 6, RadiusX = 2, RadiusY = 2, Fill = Br("#9AA4B2") };
        Canvas.SetLeft(lid, 9);
        Canvas.SetTop(lid, 10);
        c.Children.Add(lid);

        c.Children.Add(Shape("M13,19 L43,19 L40,51 L16,51 Z", "#C3CAD4"));
        c.Children.Add(new Path
        {
            Data = Geometry.Parse("M23,25 L24,45 M28,25 L28,45 M33,25 L32,45"),
            Stroke = Br("#6B7683"),
            StrokeThickness = 2
        });
        return Wrap(c, size);
    }

    public static UIElement Terminal(double size)
    {
        var c = new Canvas { Width = 56, Height = 56 };

        var body = new Rectangle
        {
            Width = 48,
            Height = 44,
            RadiusX = 6,
            RadiusY = 6,
            Fill = Br("#11151C"),
            Stroke = Br("#5B6574"),
            StrokeThickness = 2
        };
        Canvas.SetLeft(body, 4);
        Canvas.SetTop(body, 6);
        c.Children.Add(body);

        var bar = new Rectangle { Width = 44, Height = 9, Fill = Br("#242C38") };
        Canvas.SetLeft(bar, 6);
        Canvas.SetTop(bar, 8);
        c.Children.Add(bar);

        var prompt = new TextBlock
        {
            Text = ">_",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = Br("#3FB950")
        };
        Canvas.SetLeft(prompt, 10);
        Canvas.SetTop(prompt, 19);
        c.Children.Add(prompt);

        return Wrap(c, size);
    }
}