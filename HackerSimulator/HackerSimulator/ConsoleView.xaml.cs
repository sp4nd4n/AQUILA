using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HackerSimulator;

public partial class ConsoleView : UserControl, IWindowContent
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const double DefaultFontSize = 16;

    private static readonly string[] Commands =
    {
        "help", "target", "recon", "nmap", "locate", "exploit", "extract", "autopwn", "status", "next",
        "list", "whoami", "id", "pwd", "uname", "ifconfig", "date", "ls", "cd", "echo",
        "sudo", "su", "clear", "passwd", "shutdown", "logout", "exit"
    };

    // these need root (run them with sudo, or from a root shell)
    private static readonly HashSet<string> RootCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "target", "use", "recon", "nmap", "scan", "locate", "geoip", "exploit", "run",
        "extract", "dump", "download", "autopwn"
    };

    private static readonly string[] StageNames =
    {
        "idle", "target acquired", "recon complete", "host scanned", "position fixed", "session open", "data extracted"
    };

    private static readonly string[] NextHints =
    {
        "target <name>", "recon", "nmap <ip>", "locate", "exploit", "extract", "target <name>"
    };

    private static readonly string[] Countries = { "DE", "NL", "SE", "CH", "IS", "RO", "CA", "SG", "JP", "FR", "NO", "FI" };

    private readonly DispatcherTimer _blink = new() { Interval = TimeSpan.FromMilliseconds(530) };
    private readonly Random _rng = new();
    private readonly List<string> _history = new();
    private int _histIdx;

    private bool _started;
    private bool _stopped;
    private bool _abort;
    private bool _busy;
    private bool _promptActive;
    private bool _cursorOn = true;
    private string _input = "";
    private Run? _inputRun;
    private Run? _cursorRun;

    // root / sudo state
    private bool _isRoot;
    private DateTime _sudoUntil = DateTime.MinValue;
    private TaskCompletionSource<string?>? _secretTcs;
    private TaskCompletionSource<bool>? _keyTcs;       // waiting for "press any key" (dossier card)
    private readonly StringBuilder _secret = new();

    // operation state
    private Profile? _target;
    private string _source = "";
    private int _stage;              // 0 idle, 1 target, 2 recon, 3 scanned, 4 located, 5 session, 6 extracted
    private int _targetVersion;
    private string _targetIp = "";
    private string _cwd = "~";
    private Task<City>? _locationTask;

    private bool Halt => _stopped || _abort;

    private InlineCollection Inlines => TermPara.Inlines;

    private Brush Green => (Brush)FindResource("GreenBrush");
    private Brush Cyan => (Brush)FindResource("CyanBrush");
    private Brush Amber => (Brush)FindResource("AmberBrush");
    private Brush Purple => (Brush)FindResource("PurpleBrush");
    private Brush White => (Brush)FindResource("WhiteBrush");
    private Brush Gray => (Brush)FindResource("GrayBrush");
    private Brush Red => (Brush)FindResource("RedBrush");
    private FontFamily Mono => (FontFamily)FindResource("MonoFont");
    private FontFamily DisplayFont => (FontFamily)FindResource("TitleFont");

    private static MainWindow Win => (MainWindow)Application.Current.MainWindow;

    private string User => (AuthService.CurrentUser ?? "operator").ToLowerInvariant();

    // ---------- window integration ----------

    public string WindowTitle => $"{(_isRoot ? "root" : User)}@aquila: {_cwd}";
    public event Action<string>? TitleChanged;
    public event Action? CloseRequested;

    public void FocusContent() => FocusConsole();

    public void Shutdown() => Stop();

    private void RaiseTitle() => TitleChanged?.Invoke(WindowTitle);

    public ConsoleView()
    {
        InitializeComponent();

        _blink.Tick += (s, e) =>
        {
            if (_cursorRun == null || !_promptActive) return;
            _cursorOn = !_cursorOn;
            _cursorRun.Foreground = _cursorOn ? White : Brushes.Transparent;
        };

        Loaded += (s, e) =>
        {
            _blink.Start();
            FocusConsole();
            if (!_started)
            {
                _started = true;
                ShowPrompt();            // no boot text: straight to the prompt
            }
        };
        Unloaded += (s, e) => _blink.Stop();
    }

    public void Stop()
    {
        _stopped = true;
        _blink.Stop();
        _secretTcs?.TrySetResult(null);
        _keyTcs?.TrySetResult(true);
        Hud.Deactivate();
    }

    private void FocusConsole() =>
        Dispatcher.BeginInvoke(new Action(() => Keyboard.Focus(Terminal)), DispatcherPriority.Input);

    private void ScrollDown() => Terminal.ScrollToEnd();

    private void CollapseSelection()
    {
        var end = Terminal.Document.ContentEnd;
        Terminal.Selection.Select(end, end);
    }

    private void Zoom(int step) =>
        Terminal.FontSize = Math.Clamp(Terminal.FontSize + step, 8, 44);

    // ---------- in-terminal input ----------

    private void ShowPrompt()
    {
        bool root = _isRoot;
        Inlines.Add(new Run("┌──(") { Foreground = Cyan });
        Inlines.Add(new Run((root ? "root" : User) + "㉿aquila")
        { Foreground = root ? Red : Green, FontWeight = FontWeights.Bold });
        Inlines.Add(new Run(")-[") { Foreground = Cyan });
        Inlines.Add(new Run(_cwd) { Foreground = White, FontWeight = FontWeights.Bold });
        Inlines.Add(new Run("]") { Foreground = Cyan });
        Inlines.Add(new LineBreak());
        Inlines.Add(new Run("└─") { Foreground = Cyan });
        Inlines.Add(new Run(root ? "# " : "$ ") { Foreground = root ? Red : Cyan, FontWeight = FontWeights.Bold });

        _inputRun = new Run { Foreground = White };
        _cursorRun = new Run("█") { Foreground = White };
        _cursorOn = true;
        Inlines.Add(_inputRun);
        Inlines.Add(_cursorRun);

        _input = "";
        _promptActive = true;
        ScrollDown();
        RaiseTitle();
    }

    private void SetInput(string text)
    {
        _input = text;
        if (_inputRun != null) _inputRun.Text = text;
        ScrollDown();
    }

    private static string ClipboardLine()
    {
        try
        {
            if (!Clipboard.ContainsText()) return "";
            string text = Clipboard.GetText().Replace("\r", "").Split('\n')[0];
            return new string(text.Where(c => !char.IsControl(c)).ToArray());
        }
        catch { return ""; }
    }

    private void Console_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        string t = new string(e.Text.Where(c => !char.IsControl(c)).ToArray());

        if (_secretTcs != null)                       // password prompt: collect silently
        {
            if (t.Length > 0) _secret.Append(t);
            e.Handled = true;
            return;
        }

        if (_busy || !_promptActive)
        {
            e.Handled = true;
            return;
        }

        if (t.Length == 0) return;
        if (!Terminal.Selection.IsEmpty) CollapseSelection();
        SetInput(_input + t);
        e.Handled = true;
    }

    private async void Console_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // ----- "press any key" (closes the dossier card) -----
        if (_keyTcs != null)
        {
            e.Handled = true;
            _keyTcs.TrySetResult(true);
            return;
        }

        var mods = Keyboard.Modifiers;
        bool ctrl = mods == ModifierKeys.Control;
        bool ctrlShift = mods == (ModifierKeys.Control | ModifierKeys.Shift);

        // ----- zoom in: Ctrl+Shift++ (the + key needs Shift; without Shift nothing happens) -----
        if (e.Key == Key.OemPlus || e.Key == Key.Add)
        {
            if (ctrlShift)
            {
                e.Handled = true;
                Zoom(1);
                return;
            }
            if (ctrl)
            {
                e.Handled = true;                     // Ctrl+= (no Shift) does nothing
                return;
            }
        }

        // ----- zoom out: Ctrl+-, reset: Ctrl+0 -----
        if (ctrl || ctrlShift)
        {
            if (e.Key == Key.OemMinus || e.Key == Key.Subtract) { e.Handled = true; Zoom(-1); return; }
            if (ctrl && (e.Key == Key.D0 || e.Key == Key.NumPad0)) { e.Handled = true; Terminal.FontSize = DefaultFontSize; return; }
        }

        // ----- space: the text box swallows it as typing, so handle it as a key -----
        if (e.Key == Key.Space && (mods == ModifierKeys.None || mods == ModifierKeys.Shift))
        {
            e.Handled = true;
            if (_secretTcs != null)
            {
                _secret.Append(' ');
            }
            else if (!_busy && _promptActive)
            {
                if (!Terminal.Selection.IsEmpty) CollapseSelection();
                SetInput(_input + " ");
            }
            return;
        }

        // ----- password prompt (sudo / su / passwd) -----
        if (_secretTcs != null)
        {
            if (ctrl && e.Key == Key.C)
            {
                e.Handled = true;
                _secretTcs.TrySetResult(null);
            }
            else if (ctrlShift && e.Key == Key.V)
            {
                e.Handled = true;
                _secret.Append(ClipboardLine());
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _secretTcs.TrySetResult(_secret.ToString());
            }
            else if (e.Key == Key.Back)
            {
                e.Handled = true;
                if (_secret.Length > 0) _secret.Length--;
            }
            return;
        }

        // ----- Ctrl+Shift+C copies, Ctrl+C interrupts -----
        if (ctrlShift && e.Key == Key.C)
        {
            e.Handled = true;
            if (!Terminal.Selection.IsEmpty) Terminal.Copy();
            return;
        }

        if (ctrl && e.Key == Key.C)
        {
            e.Handled = true;
            if (_busy) _abort = true;
            else if (_promptActive) CancelLine();
            return;
        }

        if (_busy || !_promptActive) return;

        if (ctrl && e.Key == Key.L)
        {
            e.Handled = true;
            string keep = _input;
            Inlines.Clear();
            ShowPrompt();
            SetInput(keep);
            return;
        }

        // ----- Ctrl+Shift+V pastes -----
        if (ctrlShift && e.Key == Key.V)
        {
            e.Handled = true;
            if (!Terminal.Selection.IsEmpty) CollapseSelection();
            SetInput(_input + ClipboardLine());
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await SubmitAsync();
                break;

            case Key.Back:
                e.Handled = true;
                if (_input.Length > 0) SetInput(_input[..^1]);
                break;

            case Key.Tab:
                e.Handled = true;
                Complete();
                break;

            case Key.Up:
                e.Handled = true;
                if (_history.Count > 0)
                {
                    _histIdx = Math.Max(0, _histIdx - 1);
                    SetInput(_history[_histIdx]);
                }
                break;

            case Key.Down:
                e.Handled = true;
                if (_history.Count > 0)
                {
                    _histIdx = Math.Min(_history.Count, _histIdx + 1);
                    SetInput(_histIdx < _history.Count ? _history[_histIdx] : "");
                }
                break;
        }
    }

    private void CancelLine()
    {
        if (_cursorRun != null) Inlines.Remove(_cursorRun);
        Inlines.Add(new Run("^C") { Foreground = White });
        Inlines.Add(new LineBreak());
        ShowPrompt();
    }

    private void Complete()
    {
        string lead = "";
        string body = _input;
        if (body.StartsWith("sudo ", StringComparison.OrdinalIgnoreCase))
        {
            lead = body[..5];
            body = body[5..];
        }

        int sp = body.IndexOf(' ');
        if (sp < 0)
        {
            var matches = Commands.Where(c => c.StartsWith(body, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 1)
            {
                SetInput(lead + matches[0] + " ");
            }
            else if (matches.Length > 1)
            {
                string prefix = CommonPrefix(matches);
                if (prefix.Length > body.Length) SetInput(lead + prefix);
            }
        }
        else if (body[..sp].Equals("target", StringComparison.OrdinalIgnoreCase))
        {
            string arg = body[(sp + 1)..];
            var names = ProfileStore.Load()
                .Select(p => p.Name)
                .Where(n => n.StartsWith(arg, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (names.Length == 1) SetInput(lead + body[..(sp + 1)] + names[0]);
        }
    }

    private static string CommonPrefix(string[] items)
    {
        string prefix = items[0];
        foreach (string s in items)
        {
            int i = 0;
            while (i < prefix.Length && i < s.Length &&
                   char.ToLowerInvariant(prefix[i]) == char.ToLowerInvariant(s[i])) i++;
            prefix = prefix[..i];
        }
        return prefix;
    }

    private async Task SubmitAsync()
    {
        string line = _input;
        _promptActive = false;
        if (!Terminal.Selection.IsEmpty) CollapseSelection();
        if (_cursorRun != null) Inlines.Remove(_cursorRun);
        Inlines.Add(new LineBreak());
        HideOverlay();

        _busy = true;
        _abort = false;

        try
        {
            if (line.Trim().Length > 0)
            {
                _history.Add(line);
                _histIdx = _history.Count;
                await ExecuteAsync(line);
            }
        }
        catch (Exception ex)
        {
            if (!_stopped) await Warn("internal error: " + ex.Message);
        }
        finally
        {
            Hud.Deactivate();
        }

        if (_stopped) return;

        if (_abort)
        {
            HideOverlay();
            Inlines.Add(new Run("^C") { Foreground = White });
            Inlines.Add(new LineBreak());
        }

        _busy = false;
        ShowPrompt();
    }

    // ---------- password prompts (sudo / su / passwd) ----------

    private async Task<string?> ReadSecretAsync(string prompt)
    {
        Inlines.Add(new Run(prompt) { Foreground = White });
        ScrollDown();
        _secret.Clear();
        _secretTcs = new TaskCompletionSource<string?>();
        FocusConsole();

        string? result = await _secretTcs.Task;
        _secretTcs = null;
        Inlines.Add(new LineBreak());
        ScrollDown();
        return result;
    }

    private async Task<bool> AuthenticateAsync(string prompt, int attempts, string retryMessage, string failMessage)
    {
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            string? pw = await ReadSecretAsync(prompt);
            if (pw == null || _stopped) return false;          // Ctrl+C

            await Task.Delay(800);                              // real sudo pauses a moment
            if (AuthService.Login(AuthService.CurrentUser ?? "", pw)) return true;

            if (attempt < attempts) await PrintAsync(0, (retryMessage, White));
        }
        await PrintAsync(0, (failMessage, White));
        return false;
    }

    private async Task<bool> EnsureSudoAsync()
    {
        if (_isRoot || DateTime.Now < _sudoUntil) return true;

        bool ok = await AuthenticateAsync($"[sudo] password for {User}: ", 3,
            "Sorry, try again.", "sudo: 3 incorrect password attempts");
        if (ok) _sudoUntil = DateTime.Now.AddMinutes(15);
        return ok;
    }

    private async Task PasswdAsync(string arg, bool elevated)
    {
        string target = arg.Length > 0 ? arg : User;
        bool own = target.Equals(User, StringComparison.OrdinalIgnoreCase);
        bool isRootAccount = target == "root";

        if (!own && !elevated)
        {
            await PrintAsync(30, ($"passwd: You may not view or modify password information for {target}.", White));
            return;
        }
        if (!own && !isRootAccount)
        {
            await PrintAsync(30, ($"passwd: user '{target}' does not exist", White));
            return;
        }

        await PrintAsync(0, ($"Changing password for {target}.", White));

        if (!elevated)
        {
            string? current = await ReadSecretAsync("Current password: ");
            if (current == null) return;
            await Task.Delay(700);
            if (!AuthService.Login(User, current))
            {
                await PrintAsync(0, ("passwd: Authentication failure", White));
                await PrintAsync(0, ("passwd: password unchanged", White));
                return;
            }
        }

        string? first = await ReadSecretAsync("New password: ");
        if (first == null) return;
        string? second = await ReadSecretAsync("Retype new password: ");
        if (second == null) return;

        if (first != second)
        {
            await PrintAsync(0, ("Sorry, passwords do not match.", White));
            await PrintAsync(0, ("passwd: Authentication token manipulation error", White));
            await PrintAsync(0, ("passwd: password unchanged", White));
            return;
        }
        if (first.Length == 0)
        {
            await PrintAsync(0, ("No password has been supplied.", White));
            await PrintAsync(0, ("passwd: Authentication token manipulation error", White));
            await PrintAsync(0, ("passwd: password unchanged", White));
            return;
        }

        if (own) AuthService.ForceSetPassword(first);
        await Task.Delay(500);
        await PrintAsync(0, ("passwd: password updated successfully", White));
    }

    // ---------- shutdown / restart (needs sudo) ----------

    /// <summary>sudo shutdown now = close AQUILA, sudo shutdown -r now = restart AQUILA.</summary>
    private async Task ShutdownAsync(string arg, bool elevated)
    {
        string[] tokens = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool restart = tokens.Any(t => t is "-r" or "--reboot");
        string? time = tokens.FirstOrDefault(t => !t.StartsWith('-'));

        // only an immediate shutdown is supported: "now", "0", "+0" (or no time at all)
        if (time != null && time is not ("now" or "0" or "+0"))
        {
            await PrintAsync(30, ($"Failed to parse time specification: {time}", White));
            return;
        }

        // without sudo it fails exactly like a normal user on Kali
        if (!elevated)
        {
            await PrintAsync(30, ("Failed to set wall message, ignoring: Interactive authentication required.", White));
            await PrintAsync(30, (restart
                ? "Call to Reboot failed: Interactive authentication required."
                : "Call to PowerOff failed: Interactive authentication required.", White));
            return;
        }

        string stamp = DateTime.Now.ToString("ddd yyyy-MM-dd HH:mm:ss ", Inv) + DateTimeOffset.Now.ToString("zzz", Inv);
        await PrintAsync(0, ($"Broadcast message from root@aquila ({stamp}):", White));
        await Blank();
        await PrintAsync(0, (restart ? "The system will reboot now!" : "The system will power off now!", White));
        await Blank();
        await Task.Delay(1400);

        if (_stopped) return;
        if (restart)
        {
            Win.Reboot();
        }
        else
        {
            Stop();
            Win.Close();
        }
    }

    // ---------- network gate ----------

    private async Task<bool> EnsureOnlineAsync(string cmd, string detail = "")
    {
        if (await NetworkCheck.IsOnlineAsync()) return true;
        if (Halt) return false;

        await Task.Delay(700);   // looks like it tried to connect

        string ip = detail.Length > 0 ? detail : (_targetIp.Length > 0 ? _targetIp : "0.0.0.0");
        switch (cmd)
        {
            case "target":
                await Bad("Network error: could not reach the intelligence servers (temporary failure in name resolution).");
                break;

            case "recon":
                await Info("Loading OSINT modules: theHarvester, sherlock, whois");
                await Bad("theHarvester: [Errno -3] Temporary failure in name resolution");
                break;

            case "nmap":
                await PrintAsync(100, ("Starting Nmap 7.94SVN ( https://nmap.org ) at " + NmapStamp(), White));
                await PrintAsync(100, ($"sendto in send_ip_packet_sd: sendto(5, packet, 44, 0, {ip}, 16) => Network is unreachable", White));
                await PrintAsync(100, ($"Offending packet: TCP 10.10.14.7:{_rng.Next(40000, 60000)} > {ip}:80 S ttl=52 id={_rng.Next(1000, 60000)} iplen=44 seq={_rng.Next()} win=1024 <mss 1460>", Gray));
                await PrintAsync(100, ($"Nmap done: 1 IP address (0 hosts up) scanned in {(0.2 + _rng.NextDouble() * 0.3).ToString("0.00", Inv)} seconds", White));
                break;

            case "locate":
                await Info($"GeoIP database lookup: {ip}");
                await Bad("GeoIP lookup failed: Network is unreachable");
                break;

            case "exploit":
                await Info("Starting Metasploit Framework console...");
                await Bad($"Exploit failed [unreachable]: Rex::ConnectionError The connection with ({ip}:80) failed: Network is unreachable");
                break;

            case "extract":
                await Bad("Session 1 lost: no route to host");
                break;

            default:
                await Bad("Network is unreachable");
                break;
        }

        await Warn("No uplink detected (eth0/tun0 down). Reconnect to the internet and try again.");
        return false;
    }

    private static string NmapStamp() =>
        DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv) + " " + DateTimeOffset.Now.ToString("zzz", Inv);

    // ---------- printing helpers ----------

    private async Task PrintAsync(int pauseMs, params (string text, Brush color)[] parts)
    {
        if (Halt) return;
        foreach (var (text, color) in parts)
            Inlines.Add(new Run(text) { Foreground = color });
        Inlines.Add(new LineBreak());
        ScrollDown();
        await Task.Delay(pauseMs);
    }

    private Task Info(string text, int pause = 90) => PrintAsync(pause, ("[*] ", Cyan), (text, White));
    private Task Good(string text, int pause = 90) => PrintAsync(pause, ("[+] ", Green), (text, White));
    private Task Bad(string text, int pause = 60) => PrintAsync(pause, ("[-] ", Red), (text, White));
    private Task Warn(string text, int pause = 60) => PrintAsync(pause, ("[!] ", Amber), (text, White));
    private Task Blank() => PrintAsync(0, ("", White));

    private async Task TypeAsync(int delay, params (string text, Brush color)[] parts)
    {
        foreach (var (text, color) in parts)
        {
            var run = new Run { Foreground = color };
            Inlines.Add(run);
            foreach (char c in text)
            {
                if (Halt) return;
                run.Text += c;
                await Task.Delay(delay);
            }
        }
        Inlines.Add(new LineBreak());
        ScrollDown();
        await Task.Delay(120);
    }

    private async Task ProgressAsync(string label, Brush color, int step = 60)
    {
        Inlines.Add(new Run(label + " ") { Foreground = Gray });
        var bar = new Run { Foreground = color };
        Inlines.Add(bar);
        for (int i = 0; i <= 20; i++)
        {
            if (Halt) return;
            bar.Text = "[" + new string('█', i) + new string('░', 20 - i) + "] " + (i * 5) + "%";
            await Task.Delay(step);
        }
        Inlines.Add(new LineBreak());
        ScrollDown();
    }

    private async Task StepAsync(string tag, string text, string result, Brush color)
    {
        Inlines.Add(new Run($"[ {tag} ]  ") { Foreground = Gray });
        Inlines.Add(new Run(text + "  ") { Foreground = White });
        var spin = new Run { Foreground = Cyan };
        Inlines.Add(spin);

        const string frames = "|/-\\";
        for (int i = 0; i < 14; i++)
        {
            if (Halt) return;
            spin.Text = frames[i % 4].ToString();
            await Task.Delay(70);
        }
        spin.Text = result;
        spin.Foreground = color;
        Inlines.Add(new LineBreak());
        ScrollDown();
    }

    private async Task<T> WaitWithSpinnerAsync<T>(Task<T> task, string label)
    {
        Inlines.Add(new Run("[*] ") { Foreground = Cyan });
        Inlines.Add(new Run(label + " ") { Foreground = White });
        var spin = new Run { Foreground = Cyan };
        Inlines.Add(spin);

        const string frames = "|/-\\";
        int i = 0;
        while (!task.IsCompleted && !Halt)
        {
            spin.Text = frames[i++ % 4].ToString();
            await Task.Delay(90);
        }
        spin.Text = "";
        Inlines.Add(new LineBreak());
        ScrollDown();

        if (Halt || !task.IsCompleted) return default!;
        return await task;
    }

    private async Task MsfTypeAsync(string module, string command)
    {
        if (Halt) return;
        Inlines.Add(new Run("msf6 ") { Foreground = Cyan });
        if (module.Length > 0)
        {
            Inlines.Add(new Run("exploit(") { Foreground = White });
            Inlines.Add(new Run(module) { Foreground = Red });
            Inlines.Add(new Run(")") { Foreground = White });
        }
        Inlines.Add(new Run(" > ") { Foreground = White });
        await TypeAsync(20, (command, White));
    }

    private async Task MeterTypeAsync(string command)
    {
        if (Halt) return;
        Inlines.Add(new Run("meterpreter") { Foreground = Red, FontWeight = FontWeights.Bold });
        Inlines.Add(new Run(" > ") { Foreground = White });
        await TypeAsync(20, (command, White));
    }

    private string RandomIp() =>
        $"{_rng.Next(11, 223)}.{_rng.Next(0, 256)}.{_rng.Next(0, 256)}.{_rng.Next(1, 255)}";

    private string RandomHex(int length)
    {
        const string digits = "0123456789abcdef";
        var chars = new char[length];
        for (int i = 0; i < length; i++) chars[i] = digits[_rng.Next(digits.Length)];
        return new string(chars);
    }

    private static string Slug(string s)
    {
        string r = Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
        return r.Length == 0 ? "target" : r;
    }

    private static string Handle(string s)
    {
        s = s.Trim();
        int i = s.LastIndexOf('/');
        if (i >= 0) s = s[(i + 1)..];
        return s.TrimStart('@');
    }

    // ---------- command dispatch ----------

    private async Task ExecuteAsync(string raw)
    {
        string line = raw.Trim();
        bool elevated = _isRoot;

        if (line.Equals("sudo", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("sudo ", StringComparison.OrdinalIgnoreCase))
        {
            string rest = line[4..].Trim();
            if (rest.Length == 0)
            {
                await PrintAsync(30, ("usage: sudo [-k] <command>", White));
                return;
            }
            if (rest == "-k")
            {
                _sudoUntil = DateTime.MinValue;
                return;
            }
            if (!await EnsureSudoAsync()) return;

            if (rest is "su" or "su -" or "-i" or "-s" or "bash" or "zsh" or "sh")
            {
                _isRoot = true;
                return;
            }

            line = rest;
            elevated = true;
        }
        else if (line is "su" or "su -" or "su root")
        {
            if (_isRoot) return;
            if (await AuthenticateAsync("Password: ", 1, "", "su: Authentication failure")) _isRoot = true;
            return;
        }

        string[] parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return;
        string cmd = parts[0].ToLowerInvariant();
        string arg = parts.Length > 1 ? parts[1] : "";

        if (RootCommands.Contains(cmd) && !elevated)
        {
            await Bad($"{cmd}: permission denied. This operation requires root privileges.");
            await Warn($"Try: sudo {line}");
            return;
        }

        switch (cmd)
        {
            case "help":
            case "?":
                await ShowHelpAsync();
                break;

            case "target":
            case "use":
                await TargetCmdAsync(arg);
                break;

            case "recon":
                await ReconAsync();
                break;

            case "nmap":
            case "scan":
                await NmapAsync(arg);
                break;

            case "locate":
            case "geoip":
                await LocateAsync();
                break;

            case "exploit":
            case "run":
                await ExploitAsync();
                break;

            case "extract":
            case "dump":
            case "download":
                await ExtractAsync();
                break;

            case "autopwn":
                await AutopwnAsync(arg);
                break;

            case "status":
                await StatusAsync();
                break;

            case "next":
                await Info("Suggested next step: " + NextHints[Math.Min(_stage, NextHints.Length - 1)]);
                break;

            case "list":
                await ShowListAsync();
                break;

            case "whoami":
                await PrintAsync(30, (elevated ? "root" : User, White));
                break;

            case "id":
                await PrintAsync(30, (elevated
                    ? "uid=0(root) gid=0(root) groups=0(root)"
                    : $"uid=1000({User}) gid=1000({User}) groups=1000({User}),27(sudo)", White));
                break;

            case "pwd":
                {
                    string home = elevated ? "/root" : "/home/" + User;
                    await PrintAsync(30, (home + (_cwd == "~" ? "" : _cwd[1..]), White));
                    break;
                }

            case "uname":
                await PrintAsync(30, ("Linux aquila 6.5.0-aquila3-amd64 #1 SMP PREEMPT_DYNAMIC AQUILA 6.5.6-1aquila2 x86_64 GNU/Linux", White));
                break;

            case "date":
                await PrintAsync(30, (DateTime.Now.ToString("ddd MMM d HH:mm:ss ", Inv) +
                                      DateTimeOffset.Now.ToString("zzz", Inv) + DateTime.Now.ToString(" yyyy", Inv), White));
                break;

            case "ifconfig":
                await IfconfigAsync();
                break;

            case "ls":
                await PrintAsync(30, ("Desktop  Documents  Downloads  ", White), ("exploits  loot  targets  tools", Cyan));
                break;

            case "cd":
                ChangeDirectory(arg);
                if (arg.Length > 0 && !IsKnownDir(arg))
                    await PrintAsync(30, ($"cd: no such file or directory: {arg}", White));
                break;

            case "echo":
                await PrintAsync(30, (arg, White));
                break;

            case "clear":
            case "cls":
                Inlines.Clear();
                break;

            case "passwd":
                await PasswdAsync(arg, elevated);
                break;

            case "shutdown":
                await ShutdownAsync(arg, elevated);
                break;

            case "logout":
                Win.ShowLogin();
                break;

            case "exit":
            case "quit":
                if (_isRoot) _isRoot = false;      // leave the root shell first
                else CloseRequested?.Invoke();      // otherwise close this terminal window
                break;

            default:
                await PrintAsync(30, ($"zsh: command not found: {cmd}", White));
                break;
        }
    }

    private static bool IsKnownDir(string arg) =>
        arg is "~" or ".." or "targets" or "~/targets" or "exploits" or "loot" or "tools";

    private void ChangeDirectory(string arg)
    {
        if (arg.Length == 0 || arg == "~") _cwd = "~";
        else if (arg == "targets" || arg == "~/targets") _cwd = "~/targets";
        else if (arg is "exploits" or "loot" or "tools") _cwd = "~/" + arg;
        else if (arg == "..")
        {
            int i = _cwd.LastIndexOf('/');
            _cwd = i <= 0 ? "~" : _cwd[..i];
        }
    }

    private async Task ShowHelpAsync()
    {
        async Task Row(string c, string d) => await PrintAsync(25, ("  " + c.PadRight(22), Cyan), (d, Gray));

        await PrintAsync(25, ("Operation  (requires root: use sudo, or open a root shell)", White));
        await Row("target <name>", "acquire a target (saved profiles, then public records)");
        await Row("recon", "passive OSINT and footprint enumeration");
        await Row("nmap <ip>", "port and service scan");
        await Row("locate", "satellite triangulation of the target");
        await Row("exploit", "launch the exploit chain, open a session");
        await Row("extract", "dump data from the open session");
        await Row("autopwn [name]", "run the whole chain automatically");
        await Row("status / next", "show progress / suggest the next step");
        await Blank();
        await PrintAsync(25, ("Privileges", White));
        await Row("sudo <command>", "run a command as root (asks for your password)");
        await Row("sudo su", "open a root shell (leave it with exit)");
        await Row("sudo -k", "forget the sudo password");
        await Blank();
        await PrintAsync(25, ("System", White));
        await Row("list", "show saved targets");
        await Row("whoami id pwd uname", "basic system info");
        await Row("ifconfig date ls cd", "network and shell basics");
        await Row("passwd", "change your password");
        await Row("shutdown now", "power off AQUILA (needs sudo)");
        await Row("shutdown -r now", "restart AQUILA (needs sudo)");
        await Row("clear logout exit", "screen / session / close this window");
    }

    private async Task ShowListAsync()
    {
        var all = ProfileStore.Load();
        if (all.Count == 0)
        {
            await Info("No saved targets.");
            return;
        }
        int i = 1;
        foreach (var p in all)
            await PrintAsync(30, ($"  {i++:00}  ", Gray), (p.DisplayName, White));
    }

    private async Task IfconfigAsync()
    {
        await PrintAsync(20, ("eth0: ", White), ("flags=4163<UP,BROADCAST,RUNNING,MULTICAST>  mtu 1500", Gray));
        await PrintAsync(20, ($"        inet 192.168.1.{_rng.Next(20, 200)}  netmask 255.255.255.0  broadcast 192.168.1.255", White));
        await PrintAsync(20, ($"        ether 08:00:27:{RandomHex(2)}:{RandomHex(2)}:{RandomHex(2)}  txqueuelen 1000  (Ethernet)", Gray));
        await PrintAsync(20, ("tun0: ", White), ("flags=4305<UP,POINTOPOINT,RUNNING,NOARP,MULTICAST>  mtu 1500", Gray));
        await PrintAsync(20, ("        inet 10.10.14.7  netmask 255.255.254.0  destination 10.10.14.7", White));
    }

    private async Task StatusAsync()
    {
        await PrintAsync(30, ("operator : ", Gray), (User, Purple));
        if (_target == null)
        {
            await PrintAsync(30, ("target   : ", Gray), ("none", Amber));
            return;
        }
        await PrintAsync(30, ("target   : ", Gray), (_target.DisplayName.ToUpperInvariant(), White), ($"  ({_source})", Gray));
        await PrintAsync(30, ("host     : ", Gray), (_stage >= 2 ? _targetIp : "unresolved", Cyan));
        await PrintAsync(30, ("stage    : ", Gray), ($"{Math.Min(_stage, 6)}/6  ", White), (StageNames[Math.Min(_stage, 6)], Green));
    }

    // ---------- the operation ----------

    private static Profile? FindProfile(string query, out List<Profile> matches)
    {
        var all = ProfileStore.Load();
        query = query.Trim();

        var exact = all.Where(p =>
            string.Equals(p.Name, query, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Alias, query, StringComparison.OrdinalIgnoreCase)).ToList();

        matches = exact.Count > 0
            ? exact
            : all.Where(p =>
                p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Alias.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        return matches.FirstOrDefault();
    }

    private void SetTarget(Profile p, string source)
    {
        _target = p;
        _source = source;
        _stage = 1;
        _targetVersion++;
        _targetIp = RandomIp();
        _cwd = "~/targets/" + Slug(p.DisplayName);
        _locationTask = GeoData.ResolveTargetAsync(p, _rng);   // address lookup runs in the background
    }

    private async Task TargetCmdAsync(string arg)
    {
        if (arg.Length == 0)
        {
            await PrintAsync(30, ("usage: ", Gray), ("target <name>", Cyan));
            return;
        }
        if (!await EnsureOnlineAsync("target")) return;

        await Info($"Searching local database for \"{arg}\"...", 300);
        var local = FindProfile(arg, out var matches);

        Profile? p;
        string source;

        if (local != null && matches.Count > 1)
        {
            await Bad("Multiple local records: " + string.Join(", ", matches.Select(m => m.DisplayName)));
            await Bad("Be more specific.");
            return;
        }

        if (local != null)
        {
            await Good("Record found in local database.");
            p = local;
            source = "local database";
        }
        else
        {
            await Bad("No local record.");
            p = await WaitWithSpinnerAsync(CelebrityService.LookupAsync(arg, CancellationToken.None),
                "Querying public intelligence sources");
            if (Halt) return;
            if (p == null)
            {
                await Bad("No match in public records. Target not acquired.");
                return;
            }
            await Good($"Public figure identified: {p.Name}" + (p.Alias.Length > 0 ? $"  ({p.Alias})" : ""));
            source = "public records";
        }

        SetTarget(p, source);
        await Good($"Target acquired: {p.DisplayName.ToUpperInvariant()}");
        await Info($"Workspace created: {_cwd}");
    }

    private async Task ReconAsync()
    {
        if (_target == null)
        {
            await Bad("No target set. Usage: target <name>");
            return;
        }
        if (!await EnsureOnlineAsync("recon")) return;

        var p = _target;
        string slug = Slug(p.DisplayName);
        Hud.Activate(p.DisplayName.ToUpperInvariant(), _targetIp);

        await Info("Loading OSINT modules: theHarvester, sherlock, whois");
        await ProgressAsync("[*] Enumerating digital footprint", Cyan);
        await Info("theHarvester 4.6.0  sources: google, bing, linkedin, hunter, shodan");

        int hosts = _rng.Next(3, 6);
        await Good($"{hosts} hosts discovered");
        string[] prefixes = { "router", "mail", "vpn", "nas", "pc" };
        for (int i = 0; i < 3; i++)
            await PrintAsync(70, ("      ", Gray), ($"{prefixes[_rng.Next(prefixes.Length)]}.{slug}.lan".PadRight(32), Cyan), (RandomIp(), Gray));
        await PrintAsync(90, ("[+] ", Green), ("Primary address resolved: ", White), (_targetIp, Cyan));

        await Info($"sherlock: checking username \"{slug}\" on {_rng.Next(38, 61)} sites");
        int found = 0;
        if (p.Instagram.Length > 0) { found++; await Good("Instagram : instagram.com/" + Handle(p.Instagram)); }
        if (p.Facebook.Length > 0) { found++; await Good("Facebook  : facebook.com/" + Handle(p.Facebook)); }
        if (p.Twitter.Length > 0) { found++; await Good("X         : x.com/" + Handle(p.Twitter)); }
        if (p.Snapchat.Length > 0) { found++; await Good("Snapchat  : snapchat.com/add/" + Handle(p.Snapchat)); }
        if (found == 0) await Bad("No public profiles confirmed");
        await Info($"{found} profiles confirmed, {_rng.Next(2, 9)} possible matches queued");
        await Good($"Footprint confidence: {_rng.Next(81, 98)}%");

        if (!Halt) _stage = Math.Max(_stage, 2);
    }

    private async Task NmapAsync(string arg)
    {
        var m = Regex.Match(arg, @"\b(?:\d{1,3}\.){3}\d{1,3}\b");
        if (!m.Success)
        {
            await PrintAsync(40, ("Nmap 7.94SVN ( https://nmap.org )", White));
            await PrintAsync(40, ("Usage: nmap [Scan Type(s)] [Options] {target specification}", White));
            await PrintAsync(40, ("TARGET SPECIFICATION:", White));
            await PrintAsync(40, ("  Can pass hostnames, IP addresses, networks, etc.", Gray));
            await PrintAsync(40, ("  Ex: scanme.nmap.org, microsoft.com/24, 192.168.0.1; 10.0.0-255.1-254", Gray));
            await PrintAsync(40, ("SEE THE MAN PAGE (https://nmap.org/book/man.html) FOR MORE OPTIONS AND EXAMPLES", White));
            return;
        }

        string ip = m.Value;
        if (!await EnsureOnlineAsync("nmap", ip)) return;

        bool known = _target != null && _stage >= 2 && ip == _targetIp;
        Hud.Activate(known ? _target!.DisplayName.ToUpperInvariant() : "UNKNOWN", ip);

        await PrintAsync(150, ("Starting Nmap 7.94SVN ( https://nmap.org ) at " + NmapStamp(), White));
        await ProgressAsync("Initiating SYN Stealth Scan", Cyan, 80);

        if (!known)
        {
            await PrintAsync(100, ("Note: Host seems down. If it is really up, but blocking our ping probes, try -Pn", White));
            await PrintAsync(100, ($"Nmap done: 1 IP address (0 hosts up) scanned in {(2 + _rng.NextDouble() * 2).ToString("0.00", Inv)} seconds", White));
            return;
        }

        await PrintAsync(100, ($"Nmap scan report for {ip}", White));
        await PrintAsync(100, ($"Host is up ({(0.02 + _rng.NextDouble() * 0.08).ToString("0.000", Inv)}s latency).", White));
        await PrintAsync(100, ($"Not shown: {_rng.Next(990, 997)} filtered tcp ports (no-response)", Gray));
        await PrintAsync(100, ("PORT      STATE  SERVICE      VERSION", Gray));

        (int port, string service, string version)[] catalog =
        {
            (21, "ftp", "vsftpd 3.0.3"),
            (22, "ssh", "OpenSSH 8.9p1 Ubuntu"),
            (80, "http", "nginx 1.18.0"),
            (443, "ssl/http", "nginx 1.18.0"),
            (445, "microsoft-ds", "Samba smbd 4.17"),
            (3306, "mysql", "MySQL 8.0.33"),
            (3389, "ms-wbt-server", "Microsoft Terminal Services"),
            (5900, "vnc", "VNC (protocol 3.8)"),
            (8080, "http-proxy", "Squid 5.7")
        };
        var open = catalog.OrderBy(_ => _rng.Next()).Take(_rng.Next(4, 7)).OrderBy(c => c.port);
        foreach (var c in open)
        {
            await PrintAsync(140,
                (($"{c.port}/tcp").PadRight(10), White),
                ("open".PadRight(7), Green),
                (c.service.PadRight(13), Cyan),
                (c.version, Gray));
        }

        await PrintAsync(100, ("Service Info: OS: Linux; CPE: cpe:/o:linux:linux_kernel", Gray));
        await PrintAsync(100, ($"Nmap done: 1 IP address (1 host up) scanned in {(8 + _rng.NextDouble() * 9).ToString("0.00", Inv)} seconds", White));

        if (!Halt) _stage = Math.Max(_stage, 3);
    }

    private async Task LocateAsync()
    {
        if (_target == null)
        {
            await Bad("No target set. Usage: target <name>");
            return;
        }
        if (_stage < 3)
        {
            await Bad("Host not fingerprinted yet. Scan it first (nmap).");
            return;
        }
        if (!await EnsureOnlineAsync("locate")) return;

        await Info($"GeoIP database lookup: {_targetIp}");
        await ProgressAsync("[*] Acquiring satellite uplink", Cyan, 70);
        await Info("Triangulating position (WGS84)...");
        await ShowMapAsync();

        if (!Halt) _stage = Math.Max(_stage, 4);
    }

    private async Task ExploitAsync()
    {
        if (_target == null)
        {
            await Bad("No target set. Usage: target <name>");
            return;
        }
        if (_stage < 4)
        {
            await Bad("No position fix on the target. Run locate first.");
            return;
        }
        if (!await EnsureOnlineAsync("exploit")) return;

        Hud.Activate(_target.DisplayName.ToUpperInvariant(), _targetIp);

        await Info("Starting Metasploit Framework console...");
        await ProgressAsync("[*] Bypassing firewall rules", Amber, 70);
        await Good("Firewall bypassed");
        await GlitchAsync();
        await HexDumpAsync();

        const string module = "multi/http/apache_normalize_path_rce";
        await MsfTypeAsync("", "use exploit/" + module);
        await Info("No payload configured, defaulting to linux/x64/meterpreter/reverse_tcp");
        await MsfTypeAsync(module, "set RHOSTS " + _targetIp);
        await PrintAsync(60, ("RHOSTS => " + _targetIp, White));
        await MsfTypeAsync(module, "set LHOST tun0");
        await PrintAsync(60, ("LHOST => 10.10.14.7", White));
        await MsfTypeAsync(module, "exploit");

        await Info("Started reverse TCP handler on 10.10.14.7:4444", 250);
        await Info("Running automatic check (\"set AutoCheck false\" to disable)", 400);
        await Good("The target appears to be vulnerable.", 300);
        await ProgressAsync("[*] Sending stage (3045380 bytes)", Cyan, 70);
        await ProgressAsync("[*] Brute-forcing credential hashes", Red, 90);
        await GlitchAsync();
        await Info($"Meterpreter session 1 opened (10.10.14.7:4444 -> {_targetIp}:{_rng.Next(40000, 60000)})");

        if (!Halt) _stage = Math.Max(_stage, 5);
    }

    private async Task ExtractAsync()
    {
        if (_target == null)
        {
            await Bad("No target set. Usage: target <name>");
            return;
        }
        if (_stage < 5)
        {
            await Bad("No active session. Run exploit first.");
            return;
        }
        if (!await EnsureOnlineAsync("extract")) return;

        var p = _target;
        Hud.Activate(p.DisplayName.ToUpperInvariant(), _targetIp);

        await MeterTypeAsync("sysinfo");
        await PrintAsync(60, ("Computer     : ", Gray), ("DESKTOP-" + RandomHex(7).ToUpperInvariant(), White));
        await PrintAsync(60, ("OS           : ", Gray), ("Windows 11 (10.0 Build 22631)", White));
        await PrintAsync(60, ("Architecture : ", Gray), ("x64", White));
        await MeterTypeAsync("hashdump");
        await PrintAsync(60, ($"{Slug(p.DisplayName)}:1001:aad3b435b51404eeaad3b435b51404ee:{RandomHex(32)}:::", Gray));
        await PrintAsync(60, ($"Guest:501:aad3b435b51404eeaad3b435b51404ee:{RandomHex(32)}:::", Gray));
        await MeterTypeAsync("run post/multi/gather/social_accounts");

        var steps = new List<(string tag, string text, string result, Brush color)>();
        if (!string.IsNullOrWhiteSpace(p.Phone)) steps.Add(("CELL ", "Triangulating mobile signal", "LOCKED", Green));
        if (!string.IsNullOrWhiteSpace(p.Email)) steps.Add(("MAIL ", "Intercepting mail server", "BREACHED", Green));
        if (!string.IsNullOrWhiteSpace(p.Instagram)) steps.Add(("INSTA", "Hijacking session token", "BREACHED", Purple));
        if (!string.IsNullOrWhiteSpace(p.Facebook)) steps.Add(("FB   ", "Bypassing 2FA handshake", "BREACHED", Purple));
        if (!string.IsNullOrWhiteSpace(p.Snapchat)) steps.Add(("SNAP ", "Cloning auth cookie", "BREACHED", Purple));
        if (!string.IsNullOrWhiteSpace(p.Twitter)) steps.Add(("X    ", "Spoofing API key", "BREACHED", Purple));
        if (!string.IsNullOrWhiteSpace(p.Workplace)) steps.Add(("WORK ", "Querying employer records", "FOUND", Cyan));
        if (!string.IsNullOrWhiteSpace(p.Address)) steps.Add(("GEO  ", "Resolving physical location", "FOUND", Amber));
        steps.Add(("DUMP ", "Compiling dossier", "DONE", Green));

        foreach (var s in steps)
        {
            if (Halt) return;
            await StepAsync(s.tag, s.text, s.result, s.color);
        }

        await Task.Delay(500);
        if (Halt) return;
        Hud.Deactivate();
        await FlashAsync();

        // ACCESS GRANTED appears in the terminal only
        Inlines.Add(new Run("[+] ") { Foreground = Green });
        Inlines.Add(new Run("ACCESS GRANTED") { Foreground = Green, FontWeight = FontWeights.Bold });
        Inlines.Add(new LineBreak());
        ScrollDown();
        await Task.Delay(700);

        // the info card pops up on the screen; any key closes it
        await ShowDossierAsync(p);
        await WaitAnyKeyAsync();
        HideOverlay();
        if (Halt) return;

        // the same information, as plain text in the terminal so it can be copied
        await PrintDossierAsync(p);
        await Good("Dossier compiled.");
        await Info("Session 1 closed. Logs wiped.");

        if (!Halt) _stage = 6;
    }

    private async Task AutopwnAsync(string arg)
    {
        if (!await EnsureOnlineAsync("autopwn")) return;

        if (arg.Length > 0)
        {
            int before = _targetVersion;
            await TargetCmdAsync(arg);
            if (Halt || _targetVersion == before) return;
        }

        if (_target == null)
        {
            await Bad("No target set. Usage: autopwn <name>");
            return;
        }

        await Info("Autopwn: running the automated attack chain");
        await ReconAsync(); if (Halt) return;
        await NmapAsync(_targetIp); if (Halt) return;
        await LocateAsync(); if (Halt) return;
        await ExploitAsync(); if (Halt) return;
        await ExtractAsync();
    }

    // ---------- effects ----------

    private async Task HexDumpAsync()
    {
        await Info("Dumping memory segment...");
        for (int i = 0; i < 12; i++)
        {
            if (Halt) return;
            var bytes = new byte[14];
            _rng.NextBytes(bytes);
            string hex = BitConverter.ToString(bytes).Replace('-', ' ');
            Inlines.Add(new Run($"  0x{_rng.Next(0x1000, 0xFFFF):X4}  ") { Foreground = Gray });
            Inlines.Add(new Run(hex) { Foreground = i % 4 == 0 ? Cyan : Gray });
            Inlines.Add(new LineBreak());
            ScrollDown();
            await Task.Delay(45);
        }
    }

    private async Task FlashAsync()
    {
        Terminal.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0.2, TimeSpan.FromMilliseconds(80))
            { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) });
        await Task.Delay(550);
    }

    private async Task GlitchAsync()
    {
        var shift = new TranslateTransform();
        Terminal.RenderTransform = shift;

        var frames = new DoubleAnimationUsingKeyFrames();
        double[] xs = { -14, 10, -7, 16, -10, 5, 0 };
        for (int i = 0; i < xs.Length; i++)
        {
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(xs[i],
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(45 * (i + 1)))));
        }
        shift.BeginAnimation(TranslateTransform.XProperty, frames);
        await Task.Delay(45 * xs.Length + 60);
    }

    private async Task WaitAnyKeyAsync()
    {
        _keyTcs = new TaskCompletionSource<bool>();
        FocusConsole();
        while (!_keyTcs.Task.IsCompleted && !Halt) await Task.Delay(40);
        _keyTcs = null;
    }

    // ---------- overlay (map and dossier card), shown over the whole desktop ----------

    private void ShowOverlay(UIElement content) =>
        DesktopView.Current?.ShowOverlay(content, () => !_busy);

    private void HideOverlay() => DesktopView.Current?.HideOverlay();

    private async Task ShowMapAsync()
    {
        var task = _locationTask ??= GeoData.ResolveTargetAsync(_target!, _rng);
        while (!task.IsCompleted && !Halt) await Task.Delay(60);
        if (Halt) return;
        var target = await task;

        var hops = GeoData.Hubs
            .Where(h => h != target)
            .OrderBy(_ => _rng.Next())
            .Take(5)
            .ToList();

        var map = new WorldMapView();
        ShowOverlay(new Viewbox
        {
            Child = map,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            Margin = new Thickness(40)
        });
        await Task.Delay(350);

        await map.PlayAsync(hops, target, () => Halt);
        if (Halt) return;

        await Task.Delay(1200);
        HideOverlay();
    }

    private static BitmapImage? LoadPhoto(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return null;
        string path = ProfileStore.PhotoPath(fileName);
        if (!File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private async Task AddRowAsync(Panel host, string label, string value, Brush color)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        var tb = new TextBlock
        {
            FontFamily = Mono,
            FontSize = 17,
            Margin = new Thickness(0, 0, 0, 7),
            TextWrapping = TextWrapping.Wrap
        };
        tb.Inlines.Add(new Run(label.PadRight(13)) { Foreground = Gray });
        var val = new Run { Foreground = color };
        tb.Inlines.Add(val);
        host.Children.Add(tb);

        foreach (char c in value)
        {
            if (Halt) return;
            val.Text += c;
            await Task.Delay(12);
        }
    }

    private async Task ShowDossierAsync(Profile p)
    {
        var photoBox = new Border
        {
            Width = 190,
            Height = 190,
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 28, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x06, 0x0A, 0x10)),
            BorderBrush = Green,
            BorderThickness = new Thickness(2)
        };
        var bmp = LoadPhoto(p.Photo);
        if (bmp != null)
            photoBox.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
        else
            photoBox.Child = new TextBlock
            {
                Text = "NO IMAGE",
                Foreground = Gray,
                FontFamily = Mono,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

        var rows = new StackPanel { MaxWidth = 720 };
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(rows, 1);
        layout.Children.Add(photoBox);
        layout.Children.Add(rows);

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x16, 0x1B, 0x22)),
            BorderBrush = Green,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(26),
            Margin = new Thickness(0, 12, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new DropShadowEffect { Color = Color.FromRgb(0x3F, 0xB9, 0x50), BlurRadius = 36, ShadowDepth = 0, Opacity = 0.25 },
            Opacity = 0,
            Child = layout
        };

        ShowOverlay(card);
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)));
        await Task.Delay(450);

        rows.Children.Add(new TextBlock
        {
            Text = "◢ CLASSIFIED // TARGET DOSSIER",
            FontFamily = Mono,
            FontSize = 12,
            Foreground = Amber,
            Margin = new Thickness(0, 0, 0, 4)
        });

        var title = new TextBlock
        {
            FontFamily = DisplayFont,
            FontSize = 32,
            FontWeight = FontWeights.Bold,
            Foreground = White,
            Margin = new Thickness(0, 0, 0, 12)
        };
        rows.Children.Add(title);
        foreach (char c in p.DisplayName.ToUpperInvariant())
        {
            if (Halt) return;
            title.Text += c;
            await Task.Delay(30);
        }

        await AddRowAsync(rows, "ALIAS", p.Alias, Purple);
        await AddRowAsync(rows, "BORN / AGE", p.Birthday, White);
        await AddRowAsync(rows, "PHONE", p.Phone, Cyan);
        await AddRowAsync(rows, "EMAIL", p.Email, Cyan);
        await AddRowAsync(rows, "ADDRESS", p.Address, Amber);
        await AddRowAsync(rows, "WORKPLACE", p.Workplace, White);
        await AddRowAsync(rows, "INSTAGRAM", p.Instagram, Purple);
        await AddRowAsync(rows, "FACEBOOK", p.Facebook, Purple);
        await AddRowAsync(rows, "SNAPCHAT", p.Snapchat, Purple);
        await AddRowAsync(rows, "X / TWITTER", p.Twitter, Purple);

        foreach (string raw in p.Extra.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int idx = raw.IndexOf(':');
            string label = idx > 0 ? raw[..idx].Trim().ToUpperInvariant() : "NOTE";
            string value = idx > 0 ? raw[(idx + 1)..].Trim() : raw;
            if (label.Length > 12) label = label[..12];
            await AddRowAsync(rows, label, value, Green);
        }
    }

    /// <summary>The dossier as plain terminal text (so it can be selected and copied).</summary>
    private async Task PrintDossierAsync(Profile p)
    {
        async Task Row(string label, string value, Brush color)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            await PrintAsync(35, (label.PadRight(14), Gray), (value, color));
        }

        string rule = new string('─', 44);
        await Blank();
        await PrintAsync(35, ("TARGET DOSSIER", Amber));
        await PrintAsync(35, (rule, Gray));
        await Row("NAME", p.DisplayName.ToUpperInvariant(), White);
        await Row("ALIAS", p.Alias, Purple);
        await Row("BORN / AGE", p.Birthday, White);
        await Row("PHONE", p.Phone, Cyan);
        await Row("EMAIL", p.Email, Cyan);
        await Row("ADDRESS", p.Address, Amber);
        await Row("WORKPLACE", p.Workplace, White);
        await Row("INSTAGRAM", p.Instagram, Purple);
        await Row("FACEBOOK", p.Facebook, Purple);
        await Row("SNAPCHAT", p.Snapchat, Purple);
        await Row("X / TWITTER", p.Twitter, Purple);

        foreach (string raw in p.Extra.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int idx = raw.IndexOf(':');
            string label = idx > 0 ? raw[..idx].Trim().ToUpperInvariant() : "NOTE";
            string value = idx > 0 ? raw[(idx + 1)..].Trim() : raw;
            if (label.Length > 13) label = label[..13];
            await Row(label, value, Green);
        }

        await PrintAsync(35, (rule, Gray));
        await Blank();
    }
}