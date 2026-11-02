using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace HackerSimulator;

/// <summary>Keyboard-only GRUB-style boot editor followed by a root recovery shell.</summary>
public partial class GrubView : UserControl
{
    private enum Phase { Edit, Booting, Shell }

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly Brush Ink = Frozen(0xC8, 0xC8, 0xC8);
    private static readonly Brush Inverse = Frozen(0xC8, 0xC8, 0xC8);
    private static readonly Brush Black = Frozen(0x00, 0x00, 0x00);

    private static readonly string[] Pool =
    {
        "BIOS-e820: [mem 0x{h8}-0x{h8}] usable",
        "NX (Execute Disable) protection: active",
        "SMBIOS 3.2.0 present.",
        "DMI: AQUILA Virtual Platform/Standard PC, BIOS 1.16.2 04/01/2014",
        "tsc: Detected {mhz}.000 MHz processor",
        "ACPI: RSDP 0x{h8} 000024 (v02 BOCHS )",
        "x2apic: enabled by BIOS, switching to x2apic ops",
        "PCI: MMCONFIG for domain 0000 [bus 00-ff] at [mem 0x{h8}-0x{h8}] (base 0x{h8})",
        "nvme nvme0: pci function 0000:00:{n}.0",
        "EXT4-fs (dm-0): mounted filesystem {uuid} ro with ordered data mode. Quota mode: none.",
        "usb 1-{n}: new high-speed USB device number {n} using xhci_hcd",
        "random: crng init done",
        "Freeing unused kernel image (initmem) memory: {k}K",
        "Write protecting the kernel read-only data: {k}k",
        "clocksource: Switched to clocksource tsc",
        "SELinux:  Initializing.",
        "kvm: Nested Virtualization enabled",
        "pci 0000:00:{n}.0: [8086:{h4}] type 00 class 0x060000",
        "ata{n}: SATA link up 6.0 Gbps (SStatus 133 SControl 300)",
        "scsi host{n}: ahci",
        "input: AT Translated Set 2 keyboard as /devices/platform/i8042/serio0/input/input{n}",
        "e1000e 0000:00:1f.{n} eth0: (PCI Express:2.5GT/s:Width x1) 08:00:27:{h4}:{h4}",
        "device-mapper: uevent: version 1.0.3",
        "Loaded X.509 cert 'AQUILA Secure Boot CA: {h8}'",
        "Initializing cgroup subsys cpuset",
        "mce: CPU{n}: Thermal monitoring enabled (TM1)"
    };

    private readonly Random _rng = new();
    private readonly List<string> _lines = new();
    private Phase _phase = Phase.Edit;
    private int _row, _col;
    private string _kver = "6.5.0-aquila3-amd64";

    // recovery shell
    private string _cmd = "";
    private bool _rw;
    private bool _busy;
    private Run? _cmdRun;
    private Run? _cursorRun;
    private TaskCompletionSource<string?>? _secretTcs;
    private readonly StringBuilder _secret = new();

    private static MainWindow Win => (MainWindow)Application.Current.MainWindow;

    public GrubView()
    {
        InitializeComponent();
        BuildContent();
        RenderEditor();
        Loaded += (s, e) => Focus();
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    // ---------- random content (new every time the menu opens) ----------

    private string RandomHex(int length)
    {
        const string digits = "0123456789abcdef";
        var chars = new char[length];
        for (int i = 0; i < length; i++) chars[i] = digits[_rng.Next(digits.Length)];
        return new string(chars);
    }

    private string Uuid() =>
        $"{RandomHex(8)}-{RandomHex(4)}-{RandomHex(4)}-{RandomHex(4)}-{RandomHex(12)}";

    private string Symbols()
    {
        const string s = "@#$%^&*~|<>?!";
        return new string(Enumerable.Range(0, 3).Select(_ => s[_rng.Next(s.Length)]).ToArray());
    }

    private void BuildContent()
    {
        string[] kernels = { "6.5.0-aquila3-amd64", "6.6.9-amd64", "6.1.0-aquila9-amd64", "6.8.11-amd64" };
        string[] words = { "lvm", "crypt", "ext4", "btrfs", "nvme", "xfs", "swap", "luks", "zram" };
        _kver = kernels[_rng.Next(kernels.Length)];

        string Noise() =>
            $"#= {RandomHex(5)} ~ {words[_rng.Next(words.Length)]} /dev/{words[_rng.Next(words.Length)]}{_rng.Next(0, 9)} {Symbols()}";

        _lines.Clear();
        _lines.Add("setparams 'AQUILA GNU/Linux'");
        _lines.Add("");
        _lines.Add("    " + Noise());
        _lines.Add("    load_video");
        _lines.Add("    insmod gzio");
        _lines.Add("    if [ x$grub_platform = xxen ]; then insmod xzio; insmod lzopio; fi");
        _lines.Add("    insmod part_gpt");
        _lines.Add("    insmod ext2");
        _lines.Add($"    set root='hd{_rng.Next(0, 2)},gpt{_rng.Next(1, 4)}'");
        _lines.Add($"    search --no-floppy --fs-uuid --set=root {Uuid()}");
        _lines.Add("    " + Noise());
        _lines.Add($"    echo    'Loading Linux {_kver} ...'");
        _lines.Add($"    linux   /boot/vmlinuz-{_kver} root=UUID={Uuid()} ro quiet splash");
        _lines.Add("    echo    'Loading initial ramdisk ...'");
        _lines.Add($"    initrd  /boot/initrd.img-{_kver}");
        _row = 0;
        _col = 0;
    }

    // ---------- editor ----------

    private void RenderEditor()
    {
        EditText.Inlines.Clear();
        for (int i = 0; i < _lines.Count; i++)
        {
            string line = _lines[i];
            if (i == _row)
            {
                int col = Math.Min(_col, line.Length);
                string ch = col < line.Length ? line[col].ToString() : " ";
                string after = col < line.Length ? line[(col + 1)..] : "";
                EditText.Inlines.Add(new Run(line[..col]));
                EditText.Inlines.Add(new Run(ch) { Background = Inverse, Foreground = Black });
                EditText.Inlines.Add(new Run(after));
            }
            else
            {
                EditText.Inlines.Add(new Run(line));
            }
            if (i < _lines.Count - 1) EditText.Inlines.Add(new LineBreak());
        }
    }

    private async void Grub_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        switch (_phase)
        {
            case Phase.Edit:
                HandleEditKey(e, ctrl);
                break;
            case Phase.Shell:
                await HandleShellKeyAsync(e);
                break;
            default:
                if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0) e.Handled = true;
                break;
        }
    }

    private void HandleEditKey(KeyEventArgs e, bool ctrl)
    {
        string line = _lines[_row];
        switch (e.Key)
        {
            case Key.Left:
                e.Handled = true;
                if (_col > 0) _col--;
                else if (_row > 0) { _row--; _col = _lines[_row].Length; }
                break;

            case Key.Right:
                e.Handled = true;
                if (_col < line.Length) _col++;
                else if (_row < _lines.Count - 1) { _row++; _col = 0; }
                break;

            case Key.Up:
                e.Handled = true;
                if (_row > 0) { _row--; _col = Math.Min(_col, _lines[_row].Length); }
                break;

            case Key.Down:
                e.Handled = true;
                if (_row < _lines.Count - 1) { _row++; _col = Math.Min(_col, _lines[_row].Length); }
                break;

            case Key.Home:
                e.Handled = true;
                _col = 0;
                break;

            case Key.End:
                e.Handled = true;
                _col = line.Length;
                break;

            case Key.Back:
                e.Handled = true;
                if (_col > 0)
                {
                    _lines[_row] = line.Remove(_col - 1, 1);
                    _col--;
                }
                else if (_row > 0)
                {
                    int prevLen = _lines[_row - 1].Length;
                    _lines[_row - 1] += line;
                    _lines.RemoveAt(_row);
                    _row--;
                    _col = prevLen;
                }
                break;

            case Key.Delete:
                e.Handled = true;
                if (_col < line.Length) _lines[_row] = line.Remove(_col, 1);
                else if (_row < _lines.Count - 1)
                {
                    _lines[_row] += _lines[_row + 1];
                    _lines.RemoveAt(_row + 1);
                }
                break;

            case Key.Enter:
                e.Handled = true;
                _lines[_row] = line[.._col];
                _lines.Insert(_row + 1, line[_col..]);
                _row++;
                _col = 0;
                break;

            case Key.Space:
                e.Handled = true;
                _lines[_row] = line.Insert(_col, " ");
                _col++;
                break;

            case Key.Tab:
                e.Handled = true;
                break;

            case Key.Escape:
                e.Handled = true;
                Win.ShowLogin();
                return;

            case Key.F10:
                e.Handled = true;
                _ = BootAsync();
                return;

            case Key.X when ctrl:
                e.Handled = true;
                _ = BootAsync();
                return;

            default:
                return;
        }
        RenderEditor();
    }

    private void Grub_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        string t = new string(e.Text.Where(c => !char.IsControl(c)).ToArray());
        if (t.Length == 0) return;
        e.Handled = true;

        if (_phase == Phase.Edit)
        {
            string line = _lines[_row];
            _lines[_row] = line.Insert(_col, t);
            _col += t.Length;
            RenderEditor();
        }
        else if (_phase == Phase.Shell)
        {
            if (_secretTcs != null) { _secret.Append(t); return; }
            if (_busy) return;
            SetCmd(_cmd + t);
        }
    }

    // ---------- boot ----------

    private async Task BootAsync()
    {
        if (_phase != Phase.Edit) return;

        string[] tokens = string.Join(" ", _lines).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        bool shell = tokens.Any(t => t is "init=/bin/bash" or "init=/bin/sh");
        _rw = false;
        foreach (string t in tokens)
        {
            if (t == "rw") _rw = true;
            else if (t == "ro") _rw = false;
        }

        _phase = Phase.Booting;
        EditorPage.Visibility = Visibility.Collapsed;
        ShellPage.Visibility = Visibility.Visible;
        ShellText.Inlines.Clear();

        Print($"Loading Linux {_kver} ...");
        await Task.Delay(500);
        Print("Loading initial ramdisk ...");
        await Task.Delay(700);

        foreach (string line in KernelLog(shell))
        {
            Print(line);
            await Task.Delay(_rng.Next(6, 34));
        }

        if (!shell)
        {
            // normal boot: back through the boot animation to the login
            await Task.Delay(600);
            Win.Reboot();
            return;
        }

        Print("bash: cannot set terminal process group (-1): Inappropriate ioctl for device");
        Print("bash: no job control in this shell");
        _phase = Phase.Shell;
        ShowShellPrompt();
    }

    private List<string> KernelLog(bool shell)
    {
        var log = new List<string>();
        double t = 0;

        string Stamp()
        {
            t += _rng.NextDouble() * 0.12;
            return "[" + t.ToString("0.000000", Inv).PadLeft(12) + "] ";
        }

        string Fill(string template) =>
            Regex.Replace(template, @"\{(h8|h4|n|mhz|k|uuid)\}", m => m.Groups[1].Value switch
            {
                "h8" => RandomHex(8),
                "h4" => RandomHex(4),
                "n" => _rng.Next(1, 9).ToString(),
                "mhz" => (1800 + _rng.Next(0, 2200)).ToString(),
                "k" => _rng.Next(512, 9000).ToString(),
                _ => Uuid()
            });

        log.Add(Stamp() + $"Linux version {_kver} (devel@aquila.local) (gcc-13 (Debian 13.2.0-4) 13.2.0) #1 SMP PREEMPT_DYNAMIC");

        int count = _rng.Next(70, 100);
        for (int i = 0; i < count; i++)
        {
            log.Add(Stamp() + Fill(Pool[_rng.Next(Pool.Length)]));
            if (i % 7 == 6) AddTree(log, Stamp);
        }

        log.Add(Stamp() + (shell ? "Run /bin/bash as init process" : "Run /sbin/init as init process"));
        return log;
    }

    private void AddTree(List<string> log, Func<string> stamp)
    {
        string[] words = { "mm", "slab", "irq", "pci", "sched", "net", "blk", "crypto", "fs", "acpi" };
        int rows = _rng.Next(4, 8);
        int depth = 0;
        for (int i = 0; i < rows; i++)
        {
            depth = Math.Clamp(depth + _rng.Next(-1, 2), 0, 3);
            bool last = i == rows - 1;
            string prefix = string.Concat(Enumerable.Repeat("│  ", depth)) + (last ? "└─ " : "├─ ");
            log.Add(stamp() + prefix +
                    $"0x{RandomHex(8)}  {words[_rng.Next(words.Length)]}.{words[_rng.Next(words.Length)]}  [{RandomHex(4)}]");
        }
    }

    // ---------- recovery shell ----------

    private void Print(string text)
    {
        ShellText.Inlines.Add(new Run(text) { Foreground = Ink });
        ShellText.Inlines.Add(new LineBreak());
        ShellPage.ScrollToEnd();
    }

    private void ShowShellPrompt()
    {
        ShellText.Inlines.Add(new Run("bash-5.2# ") { Foreground = Ink });
        _cmdRun = new Run { Foreground = Ink };
        _cursorRun = new Run("█") { Foreground = Ink };
        ShellText.Inlines.Add(_cmdRun);
        ShellText.Inlines.Add(_cursorRun);
        _cmd = "";
        ShellPage.ScrollToEnd();
    }

    private void SetCmd(string text)
    {
        _cmd = text;
        if (_cmdRun != null) _cmdRun.Text = text;
        ShellPage.ScrollToEnd();
    }

    private async Task HandleShellKeyAsync(KeyEventArgs e)
    {
        if (_secretTcs != null)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    e.Handled = true;
                    _secretTcs.TrySetResult(_secret.ToString());
                    break;
                case Key.Back:
                    e.Handled = true;
                    if (_secret.Length > 0) _secret.Length--;
                    break;
                case Key.Space:
                    e.Handled = true;
                    _secret.Append(' ');
                    break;
            }
            return;
        }

        if (_busy) return;

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await RunCommandAsync();
                break;
            case Key.Back:
                e.Handled = true;
                if (_cmd.Length > 0) SetCmd(_cmd[..^1]);
                break;
            case Key.Space:
                e.Handled = true;
                SetCmd(_cmd + " ");
                break;
        }
    }

    private async Task RunCommandAsync()
    {
        string line = _cmd.Trim();
        if (_cursorRun != null) ShellText.Inlines.Remove(_cursorRun);
        ShellText.Inlines.Add(new LineBreak());

        _busy = true;
        try
        {
            if (line.Length > 0) await ExecuteAsync(line);
        }
        finally
        {
            _busy = false;
        }
        ShowShellPrompt();
    }

    private async Task ExecuteAsync(string line)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string cmd = parts[0];
        string[] args = parts.Skip(1).ToArray();

        switch (cmd)
        {
            case "ls":
                if (args.Length > 0 && args[^1].TrimEnd('/') == "/home")
                    Print(AuthService.StoredUsername() ?? "");
                else if (args.Length == 0 || args[^1] == "/")
                    Print("bin  boot  dev  etc  home  lib  lib64  media  mnt  opt  proc  root  run  sbin  srv  sys  tmp  usr  var");
                else
                    Print($"ls: cannot access '{args[^1]}': No such file or directory");
                break;

            case "whoami":
                Print("root");
                break;

            case "pwd":
                Print("/");
                break;

            case "id":
                Print("uid=0(root) gid=0(root) groups=0(root)");
                break;

            case "clear":
                ShellText.Inlines.Clear();
                break;

            case "mount":
                if (args.Any(a => a.Contains("remount,rw"))) _rw = true;
                break;

            case "passwd":
                await PasswdAsync(args);
                break;

            case "reboot":
                if (args.Any(a => a is "-f" or "--force"))
                {
                    Print("Rebooting.");
                    await Task.Delay(1100);
                    Win.Reboot();
                }
                else
                {
                    Print("Failed to connect to bus: No such file or directory");
                    Print("Failed to talk to init daemon.");
                }
                break;

            default:
                Print($"bash: {cmd}: command not found");
                break;
        }
    }

    private async Task<string?> ReadSecretAsync(string prompt)
    {
        ShellText.Inlines.Add(new Run(prompt) { Foreground = Ink });
        ShellPage.ScrollToEnd();
        _secret.Clear();
        _secretTcs = new TaskCompletionSource<string?>();

        string? result = await _secretTcs.Task;
        _secretTcs = null;
        ShellText.Inlines.Add(new LineBreak());
        return result;
    }

    private async Task PasswdAsync(string[] args)
    {
        string user = args.Length > 0 ? args[^1] : "root";
        string? stored = AuthService.StoredUsername();
        bool isRoot = user == "root";
        bool isAccount = stored != null && string.Equals(user, stored, StringComparison.OrdinalIgnoreCase);

        if (!isRoot && !isAccount)
        {
            Print($"passwd: user '{user}' does not exist");
            return;
        }

        string? first = await ReadSecretAsync("New password: ");
        if (first == null) return;
        string? second = await ReadSecretAsync("Retype new password: ");
        if (second == null) return;

        if (first != second)
        {
            Print("Sorry, passwords do not match.");
            Print("passwd: Authentication token manipulation error");
            Print("passwd: password unchanged");
            return;
        }
        if (first.Length == 0)
        {
            Print("No password has been supplied.");
            Print("passwd: Authentication token manipulation error");
            Print("passwd: password unchanged");
            return;
        }
        if (!_rw)
        {
            // root filesystem is still read-only
            Print("passwd: Authentication token manipulation error");
            Print("passwd: password unchanged");
            return;
        }

        if (isAccount) AuthService.ForceSetPassword(first);
        Print("passwd: password updated successfully");
    }
}