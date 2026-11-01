using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace HackerSimulator;

public partial class HudPanel : UserControl
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly int[] Ports = { 22, 80, 443, 445, 3389, 8080 };
    private static readonly string[] Flags = { "[S]", "[S.]", "[.]", "[P.]", "[P.]", "[F.]" };

    private readonly Random _rng = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private readonly Queue<string> _packets = new();
    private readonly double[] _cpu = { 22, 31, 18, 40 };

    private string _dst = "103.21.4.5";
    private double _mem = 38;
    private int _tick;
    private DateTime _started = DateTime.Now;

    public HudPanel()
    {
        InitializeComponent();
        _timer.Tick += (s, e) => Update();
    }

    public void Activate(string target, string? ip = null)
    {
        if (!string.IsNullOrEmpty(ip)) _dst = ip;
        CapText.Text = $"tcpdump -i tun0 -nn host {_dst}";
        _packets.Clear();
        _tick = 0;
        _started = DateTime.Now;
        RenderStats();
        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        _timer.Start();
    }

    public void Deactivate()
    {
        _timer.Stop();
        BeginAnimation(OpacityProperty, null);
        Visibility = Visibility.Collapsed;
    }

    private void Update()
    {
        _tick++;
        AddPacket();
        if (_rng.Next(3) == 0) AddPacket();
        PktText.Text = string.Join("\n", _packets);
        if (_tick % 3 == 0) RenderStats();
    }

    private void AddPacket()
    {
        string time = DateTime.Now.ToString("HH:mm:ss.fff", Inv);
        string local = "10.10.14.7." + _rng.Next(32768, 61000);
        string remote = _dst + "." + Ports[_rng.Next(Ports.Length)];
        string flags = Flags[_rng.Next(Flags.Length)];
        bool outbound = _rng.Next(2) == 0;
        string src = outbound ? local : remote;
        string dst = outbound ? remote : local;

        _packets.Enqueue($"{time} {src} > {dst}: {flags} len {_rng.Next(0, 1448)}");
        while (_packets.Count > 22) _packets.Dequeue();
    }

    private void RenderStats()
    {
        CpuText.Inlines.Clear();

        for (int i = 0; i < _cpu.Length; i++)
        {
            _cpu[i] = Math.Clamp(_cpu[i] + (_rng.NextDouble() - 0.5) * 34, 4, 99);
            AddBar((i + 1).ToString(), _cpu[i], $"{_cpu[i]:0.0}%");
        }

        _mem = Math.Clamp(_mem + (_rng.NextDouble() - 0.5) * 3, 25, 70);
        AddBar("Mem", _mem, $"{_mem / 100 * 15.6:0.00}G/15.6G");
        AddBar("Swp", 0.5, "0K/2.00G");

        var up = TimeSpan.FromSeconds(11424 + (DateTime.Now - _started).TotalSeconds);
        var cyan = (Brush)FindResource("CyanBrush");
        var gray = (Brush)FindResource("GrayBrush");
        var white = (Brush)FindResource("WhiteBrush");

        CpuText.Inlines.Add(new Run("Tasks: ") { Foreground = gray });
        CpuText.Inlines.Add(new Run($"{214 + _rng.Next(0, 4)}") { Foreground = cyan });
        CpuText.Inlines.Add(new Run(", 1023 thr; ") { Foreground = gray });
        CpuText.Inlines.Add(new Run($"{_rng.Next(1, 5)}") { Foreground = cyan });
        CpuText.Inlines.Add(new Run(" running") { Foreground = gray });
        CpuText.Inlines.Add(new LineBreak());
        CpuText.Inlines.Add(new Run("Load average: ") { Foreground = gray });
        CpuText.Inlines.Add(new Run($"{_rng.NextDouble() * 1.5:0.00} {_rng.NextDouble() * 1.2:0.00} {_rng.NextDouble():0.00}") { Foreground = white });
        CpuText.Inlines.Add(new LineBreak());
        CpuText.Inlines.Add(new Run("Uptime: ") { Foreground = gray });
        CpuText.Inlines.Add(new Run(up.ToString(@"hh\:mm\:ss")) { Foreground = white });
    }

    private void AddBar(string label, double pct, string text)
    {
        const int width = 28;
        int filled = (int)Math.Round(pct / 100.0 * width);
        var barBrush = (Brush)FindResource(pct < 50 ? "GreenBrush" : pct < 80 ? "AmberBrush" : "RedBrush");

        CpuText.Inlines.Add(new Run(label.PadRight(4)) { Foreground = (Brush)FindResource("CyanBrush") });
        CpuText.Inlines.Add(new Run("[") { Foreground = (Brush)FindResource("WhiteBrush") });
        CpuText.Inlines.Add(new Run(new string('|', filled)) { Foreground = barBrush });
        CpuText.Inlines.Add(new Run(text.PadLeft(Math.Max(width - filled, text.Length))) { Foreground = (Brush)FindResource("WhiteBrush") });
        CpuText.Inlines.Add(new Run("]") { Foreground = (Brush)FindResource("WhiteBrush") });
        CpuText.Inlines.Add(new LineBreak());
    }
}