using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace HackerSimulator;

/// <summary>Anything that can live inside a desktop window (terminal, file manager...).</summary>
public interface IWindowContent
{
    string WindowTitle { get; }
    event Action<string>? TitleChanged;
    event Action? CloseRequested;
    void FocusContent();
    void Shutdown();
}

public partial class AppWindow : UserControl
{
    private const double MinW = 460;
    private const double MinH = 280;

    private bool _dragging;
    private bool _maxed;
    private Point _dragStart;
    private double _left0, _top0;
    private Rect _restore;

    public event Action<AppWindow>? Activated;
    public event Action<AppWindow>? CloseClicked;
    public event Action<AppWindow>? MinimizeClicked;

    public IWindowContent? Inner { get; private set; }

    public bool IsMinimized => Visibility == Visibility.Collapsed;

    public string WindowTitle
    {
        get => TitleText.Text;
        set => TitleText.Text = value;
    }

    public AppWindow()
    {
        InitializeComponent();
        PreviewMouseDown += (s, e) => Activated?.Invoke(this);
    }

    public void SetContent(IWindowContent content)
    {
        Inner = content;
        Body.Content = content;
        WindowTitle = content.WindowTitle;
    }

    public void SetActive(bool active)
    {
        TitleBar.Background = new SolidColorBrush(active
            ? Color.FromRgb(0x2A, 0x31, 0x3F)
            : Color.FromRgb(0x1B, 0x1F, 0x27));
        Frame.BorderBrush = new SolidColorBrush(active
            ? Color.FromRgb(0x4B, 0x55, 0x66)
            : Color.FromRgb(0x3A, 0x3F, 0x4B));
    }

    // ---------- buttons ----------

    private void Min_Click(object sender, RoutedEventArgs e) => MinimizeClicked?.Invoke(this);

    private void Max_Click(object sender, RoutedEventArgs e) => ToggleMax();

    private void Close_Click(object sender, RoutedEventArgs e) => CloseClicked?.Invoke(this);

    private void ToggleMax()
    {
        if (Parent is not Canvas canvas) return;

        if (!_maxed)
        {
            _restore = new Rect(Canvas.GetLeft(this), Canvas.GetTop(this), Width, Height);
            Canvas.SetLeft(this, 0);
            Canvas.SetTop(this, 0);
            Width = canvas.ActualWidth;
            Height = canvas.ActualHeight;
            _maxed = true;
        }
        else
        {
            Canvas.SetLeft(this, _restore.X);
            Canvas.SetTop(this, _restore.Y);
            Width = _restore.Width;
            Height = _restore.Height;
            _maxed = false;
        }
    }

    // ---------- dragging ----------

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMax();
            return;
        }
        if (_maxed || Parent is not Canvas canvas) return;

        _dragging = true;
        _dragStart = e.GetPosition(canvas);
        _left0 = Canvas.GetLeft(this);
        _top0 = Canvas.GetTop(this);
        TitleBar.CaptureMouse();
    }

    private void TitleBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || Parent is not Canvas canvas) return;
        Point p = e.GetPosition(canvas);
        Canvas.SetLeft(this, _left0 + p.X - _dragStart.X);
        Canvas.SetTop(this, Math.Max(0, _top0 + p.Y - _dragStart.Y));
    }

    private void TitleBar_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        TitleBar.ReleaseMouseCapture();
    }

    // ---------- resizing ----------

    private void Resize(double dx, double dy, bool left, bool top, bool right, bool bottom)
    {
        if (_maxed) return;

        double l = Canvas.GetLeft(this);
        double t = Canvas.GetTop(this);
        double w = Width;
        double h = Height;

        if (right) w = Math.Max(MinW, w + dx);
        if (bottom) h = Math.Max(MinH, h + dy);
        if (left)
        {
            double nw = Math.Max(MinW, w - dx);
            l += w - nw;
            w = nw;
        }
        if (top)
        {
            double nh = Math.Max(MinH, h - dy);
            t += h - nh;
            h = nh;
        }

        Canvas.SetLeft(this, l);
        Canvas.SetTop(this, Math.Max(0, t));
        Width = w;
        Height = h;
    }

    private void ResizeL(object s, DragDeltaEventArgs e) => Resize(e.HorizontalChange, e.VerticalChange, true, false, false, false);
    private void ResizeR(object s, DragDeltaEventArgs e) => Resize(e.HorizontalChange, e.VerticalChange, false, false, true, false);
    private void ResizeT(object s, DragDeltaEventArgs e) => Resize(e.HorizontalChange, e.VerticalChange, false, true, false, false);
    private void ResizeB(object s, DragDeltaEventArgs e) => Resize(e.HorizontalChange, e.VerticalChange, false, false, false, true);
    private void ResizeTL(object s, DragDeltaEventArgs e) => Resize(e.HorizontalChange, e.VerticalChange, true, true, false, false);
    private void ResizeTR(object s, DragDeltaEventArgs e) => Resize(e.HorizontalChange, e.VerticalChange, false, true, true, false);
    private void ResizeBL(object s, DragDeltaEventArgs e) => Resize(e.HorizontalChange, e.VerticalChange, true, false, false, true);
    private void ResizeBR(object s, DragDeltaEventArgs e) => Resize(e.HorizontalChange, e.VerticalChange, false, false, true, true);
}