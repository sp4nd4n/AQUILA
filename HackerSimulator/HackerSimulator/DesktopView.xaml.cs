using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace HackerSimulator;

public partial class DesktopView : UserControl
{
    public static DesktopView? Current { get; private set; }

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<AppWindow> _windows = new();
    private readonly Dictionary<AppWindow, Button> _taskButtons = new();
    private AppWindow? _active;
    private Border? _selectedIcon;
    private Func<bool>? _canDismiss;
    private int _z = 10;
    private int _cascade;

    private static MainWindow Win => (MainWindow)Application.Current.MainWindow;

    public DesktopView()
    {
        InitializeComponent();
        Current = this;

        TermBtn.Content = Icons.Terminal(20);
        FolderBtn.Content = Icons.Folder(20);
        IconColumn.Children.Add(MakeIcon("Home", Icons.Folder(52), OpenHome));
        IconColumn.Children.Add(MakeIcon("Trash", Icons.Trash(52), OpenTrash));

        UpdateClock();
        _clock.Tick += (s, e) => UpdateClock();
        Loaded += (s, e) =>
        {
            _clock.Start();
            FocusActive();
        };
        Unloaded += (s, e) => _clock.Stop();
    }

    private void UpdateClock() =>
        ClockText.Text = DateTime.Now.ToString("ddd d MMM   HH:mm", CultureInfo.InvariantCulture);

    // ---------- opening things ----------

    public void OpenTerminal()
    {
        double w = WindowLayer.ActualWidth > 0 ? Math.Min(1180, WindowLayer.ActualWidth - 140) : 1000;
        double h = WindowLayer.ActualHeight > 0 ? Math.Min(680, WindowLayer.ActualHeight - 50) : 600;
        OpenWindow(new ConsoleView(), Math.Max(w, 560), Math.Max(h, 340));
    }

    public void OpenHome() => OpenWindow(new FileManagerView(), 780, 480);

    public void OpenTrash() => OpenWindow(new FileManagerView("trash:"), 780, 480);

    // ---------- window management ----------

    public AppWindow OpenWindow(IWindowContent content, double width, double height)
    {
        var win = new AppWindow { Width = width, Height = height };
        win.SetContent(content);

        Canvas.SetLeft(win, 90 + _cascade * 30);
        Canvas.SetTop(win, 20 + _cascade * 30);
        _cascade = (_cascade + 1) % 8;

        var label = new TextBlock
        {
            Text = Short(content.WindowTitle),
            Foreground = Brushes.White,
            MaxWidth = 190,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var btn = new Button
        {
            Style = (Style)FindResource("PanelBtn"),
            Margin = new Thickness(2, 3, 0, 3),
            Padding = new Thickness(10, 0, 10, 0),
            Content = label
        };
        btn.Click += (s, e) => TaskClick(win);

        WindowLayer.Children.Add(win);
        Taskbar.Children.Add(btn);
        _windows.Add(win);
        _taskButtons[win] = btn;

        content.TitleChanged += title =>
        {
            win.WindowTitle = title;
            label.Text = Short(title);
        };
        content.CloseRequested += () => CloseWindow(win);
        win.CloseClicked += CloseWindow;
        win.MinimizeClicked += Minimize;
        win.Activated += Activate;

        Activate(win);
        return win;
    }

    private static string Short(string s) => s.Length > 28 ? s[..27] + "…" : s;

    private void Activate(AppWindow w)
    {
        if (!_windows.Contains(w)) return;
        if (w.IsMinimized) w.Visibility = Visibility.Visible;

        _active = w;
        Panel.SetZIndex(w, ++_z);
        foreach (var x in _windows) x.SetActive(x == w);
        UpdateTaskbar();
        w.Inner?.FocusContent();
    }

    private void Minimize(AppWindow w)
    {
        w.Visibility = Visibility.Collapsed;
        if (_active == w)
        {
            _active = null;
            var next = _windows.Where(x => !x.IsMinimized)
                               .OrderByDescending(x => Panel.GetZIndex(x))
                               .FirstOrDefault();
            if (next != null) Activate(next);
        }
        UpdateTaskbar();
    }

    private void TaskClick(AppWindow w)
    {
        if (w.IsMinimized) Activate(w);
        else if (_active == w) Minimize(w);
        else Activate(w);
    }

    private void CloseWindow(AppWindow w)
    {
        if (!_windows.Remove(w)) return;

        w.Inner?.Shutdown();
        WindowLayer.Children.Remove(w);
        if (_taskButtons.TryGetValue(w, out var btn))
        {
            Taskbar.Children.Remove(btn);
            _taskButtons.Remove(w);
        }

        if (_active == w)
        {
            _active = null;
            var next = _windows.Where(x => !x.IsMinimized)
                               .OrderByDescending(x => Panel.GetZIndex(x))
                               .FirstOrDefault();
            if (next != null) Activate(next);
        }
        UpdateTaskbar();
    }

    private void UpdateTaskbar()
    {
        foreach (var (w, btn) in _taskButtons)
        {
            btn.Background = w == _active && !w.IsMinimized
                ? new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF))
                : Brushes.Transparent;
        }
    }

    private void FocusActive() =>
        Dispatcher.BeginInvoke(new Action(() => _active?.Inner?.FocusContent()), DispatcherPriority.Input);

    /// <summary>Closes every window (log out / restart).</summary>
    public void Shutdown()
    {
        _clock.Stop();
        foreach (var w in _windows.ToList()) w.Inner?.Shutdown();
        _windows.Clear();
        WindowLayer.Children.Clear();
        Taskbar.Children.Clear();
        _taskButtons.Clear();
        _active = null;
        HideOverlay();
        if (Current == this) Current = null;
    }

    // ---------- desktop icons ----------

    private UIElement MakeIcon(string label, UIElement icon, Action open)
    {
        if (icon is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Center;

        var text = new TextBlock
        {
            Text = label,
            Foreground = Brushes.White,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 5, 0, 0),
            Effect = new DropShadowEffect { Color = Colors.Black, ShadowDepth = 1, BlurRadius = 3, Opacity = 0.9 }
        };
        var stack = new StackPanel { Width = 84 };
        stack.Children.Add(icon);
        stack.Children.Add(text);

        var border = new Border
        {
            Child = stack,
            Padding = new Thickness(4),
            Margin = new Thickness(2, 2, 2, 8),
            CornerRadius = new CornerRadius(3),
            Background = Brushes.Transparent
        };
        border.MouseLeftButtonDown += (s, e) =>
        {
            SelectIcon(border);
            e.Handled = true;
            if (e.ClickCount == 2) open();
        };
        return border;
    }

    private void SelectIcon(Border? icon)
    {
        if (_selectedIcon != null) _selectedIcon.Background = Brushes.Transparent;
        _selectedIcon = icon;
        if (icon != null) icon.Background = new SolidColorBrush(Color.FromArgb(0x44, 0x58, 0xA6, 0xFF));
    }

    private void DesktopArea_MouseDown(object sender, MouseButtonEventArgs e) => SelectIcon(null);

    // ---------- panel handlers ----------

    private void MenuBtn_Click(object sender, RoutedEventArgs e) => MenuPopup.IsOpen = true;

    private void MenuTerminal_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        OpenTerminal();
    }

    private void MenuFiles_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        OpenHome();
    }

    private void MenuTrash_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        OpenTrash();
    }

    private void NewTerminal_Click(object sender, RoutedEventArgs e) => OpenTerminal();

    private void OpenHome_Click(object sender, RoutedEventArgs e) => OpenHome();

    private void PowerBtn_Click(object sender, RoutedEventArgs e) => PowerPopup.IsOpen = true;

    private void LogOut_Click(object sender, RoutedEventArgs e)
    {
        PowerPopup.IsOpen = false;
        Win.ShowLogin();
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        PowerPopup.IsOpen = false;
        Win.Reboot();
    }

    private void ShutDown_Click(object sender, RoutedEventArgs e)
    {
        PowerPopup.IsOpen = false;
        Win.Close();
    }

    // ---------- cinematic overlay (used by the terminal) ----------

    public void ShowOverlay(UIElement content, Func<bool>? canDismiss = null)
    {
        _canDismiss = canDismiss;
        OverlayHost.Content = content;
        Overlay.Visibility = Visibility.Visible;
        Backdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
    }

    public void SwapOverlay(UIElement content)
    {
        OverlayHost.Content = content;
        Overlay.Visibility = Visibility.Visible;
    }

    public void HideOverlay()
    {
        if (Overlay.Visibility != Visibility.Visible) return;
        Overlay.Visibility = Visibility.Collapsed;
        OverlayHost.Content = null;
        FocusActive();
    }

    private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_canDismiss?.Invoke() ?? true) HideOverlay();
    }
}