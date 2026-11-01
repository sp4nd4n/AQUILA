using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace HackerSimulator;

public partial class FileManagerView : UserControl, IWindowContent
{
    private readonly string _user = (AuthService.CurrentUser ?? AuthService.StoredUsername() ?? "user").ToLowerInvariant();
    private readonly Stack<string> _back = new();
    private string _path = "";
    private string _title = "File Manager";
    private Border? _selected;

    public string WindowTitle => _title;
    public event Action<string>? TitleChanged;
    public event Action? CloseRequested;

    private string Home => "/home/" + _user;

    public FileManagerView(string? start = null)
    {
        InitializeComponent();

        AddPlace("Home", Home);
        AddPlace("Desktop", Home + "/Desktop");
        AddPlace("Documents", Home + "/Documents");
        AddPlace("Downloads", Home + "/Downloads");
        AddPlace("Trash", "trash:");
        AddPlace("File System", "/");

        Navigate(start ?? Home, push: false);
    }

    public void FocusContent() => Focus();

    public void Shutdown() { }

    // ---------- virtual file system ----------

    private (string name, bool dir)[] Entries(string path)
    {
        (string, bool)[] Dirs(params string[] names)
        {
            var result = new (string, bool)[names.Length];
            for (int i = 0; i < names.Length; i++) result[i] = (names[i], true);
            return result;
        }

        if (path == "/") return Dirs("bin", "boot", "dev", "etc", "home", "lib", "media", "mnt", "opt", "root", "run", "sbin", "srv", "tmp", "usr", "var");
        if (path == "/home") return Dirs(_user);
        if (path == Home) return Dirs("Desktop", "Documents", "Downloads", "exploits", "loot", "targets", "tools");
        if (path == Home + "/Documents") return new[] { ("notes.txt", false) };
        if (path == Home + "/tools") return Dirs("nmap", "metasploit-framework", "sherlock", "theHarvester");
        return Array.Empty<(string, bool)>();
    }

    private string Combine(string path, string name) => path == "/" ? "/" + name : path + "/" + name;

    private string Parent(string path)
    {
        if (path == "trash:") return Home;
        int i = path.LastIndexOf('/');
        return i <= 0 ? "/" : path[..i];
    }

    // ---------- navigation ----------

    private void Navigate(string path, bool push = true)
    {
        if (push && _path.Length > 0) _back.Push(_path);
        _path = path;
        Refresh();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_back.Count == 0) return;
        _path = _back.Pop();
        Refresh();
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        if (_path == "/") return;
        Navigate(Parent(_path));
    }

    private void Refresh()
    {
        Items.Children.Clear();
        _selected = null;
        EmptyText.Visibility = Visibility.Collapsed;

        if (_path == "trash:")
        {
            PathText.Text = "trash:///";
            _title = "Trash - File Manager";
            EmptyText.Text = "Trash is empty";
            EmptyText.Visibility = Visibility.Visible;
            StatusText.Text = "0 items";
        }
        else
        {
            PathText.Text = _path;
            string last = _path == "/" ? "File System" : _path[(_path.LastIndexOf('/') + 1)..];
            _title = (_path == Home ? "Home" : last) + " - File Manager";

            var entries = Entries(_path);
            foreach (var (name, dir) in entries) Items.Children.Add(MakeItem(name, dir));

            if (entries.Length == 0)
            {
                EmptyText.Text = "Folder is empty";
                EmptyText.Visibility = Visibility.Visible;
            }
            StatusText.Text = entries.Length == 1 ? "1 item" : $"{entries.Length} items";
        }

        TitleChanged?.Invoke(_title);
    }

    // ---------- UI pieces ----------

    private void AddPlace(string label, string path)
    {
        var text = new TextBlock
        {
            Text = label,
            Foreground = (Brush)FindResource("WhiteBrush"),
            FontSize = 13
        };
        var row = new Border
        {
            Child = text,
            Padding = new Thickness(12, 7, 12, 7),
            CornerRadius = new CornerRadius(3),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand
        };
        row.MouseEnter += (s, e) => row.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonDown += (s, e) => Navigate(path);
        Places.Children.Add(row);
    }

    private UIElement MakeItem(string name, bool dir)
    {
        var icon = dir ? Icons.Folder(48) : Icons.File(48);
        var label = new TextBlock
        {
            Text = name,
            Foreground = (Brush)FindResource("WhiteBrush"),
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        };
        var stack = new StackPanel { Width = 92 };
        stack.Children.Add(icon);
        stack.Children.Add(label);

        var item = new Border
        {
            Child = stack,
            Padding = new Thickness(4),
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(3),
            Background = Brushes.Transparent
        };
        item.MouseLeftButtonDown += (s, e) =>
        {
            if (_selected != null) _selected.Background = Brushes.Transparent;
            _selected = item;
            item.Background = new SolidColorBrush(Color.FromArgb(0x55, 0x2F, 0x6F, 0xEB));

            if (e.ClickCount == 2 && dir) Navigate(Combine(_path, name));
        };
        return item;
    }
}