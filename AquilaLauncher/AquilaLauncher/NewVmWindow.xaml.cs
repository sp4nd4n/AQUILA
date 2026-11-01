using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace AquilaLauncher;

public partial class NewVmWindow : Window
{
    public VmInfo? Created { get; private set; }

    public NewVmWindow()
    {
        InitializeComponent();
        Loaded += (s, e) => NameBox.Focus();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose an AQUILA image",
            Filter = "AQUILA image (*.iso)|*.iso"
        };
        if (dialog.ShowDialog(this) == true) IsoBox.Text = dialog.FileName;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowMessage(string text, bool error)
    {
        StatusText.Text = text;
        StatusText.Foreground = (Brush)FindResource(error ? "DangerBrush" : "MutedBrush");
    }

    private string? Validate()
    {
        string name = NameBox.Text.Trim();
        if (name.Length == 0) return "Give the virtual machine a name.";
        if (VmStore.NameExists(name)) return "A virtual machine with this name already exists.";

        if (IsoBox.Text.Length == 0) return "Choose an AQUILA .iso file.";
        if (!VmStore.ValidateIso(IsoBox.Text, out string isoError)) return isoError;

        if (FullBox.Text.Trim().Length == 0) return "Enter your full name.";
        if (!Regex.IsMatch(UserBox.Text.Trim(), @"^[a-zA-Z][a-zA-Z0-9_.-]{2,23}$"))
            return "Username: 3 to 24 characters, start with a letter; letters, digits, _ . - only.";
        if (PassBox.Password.Length < 4) return "The password must be at least 4 characters.";
        if (PassBox.Password != ConfirmBox.Password) return "The passwords do not match.";
        return null;
    }

    private void SetBusy(bool busy)
    {
        IsEnabled = !busy;
        Bar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        string? error = Validate();
        if (error != null)
        {
            ShowMessage(error, true);
            return;
        }

        ShowMessage("Preparing…", false);
        SetBusy(true);
        var progress = new Progress<(double pct, string text)>(p =>
        {
            Bar.Value = p.pct;
            StatusText.Text = p.text;
        });

        try
        {
            Created = await VmStore.CreateAsync(NameBox.Text.Trim(), IsoBox.Text.Trim(), FullBox.Text.Trim(),
                UserBox.Text.Trim(), PassBox.Password, progress);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            SetBusy(false);
            ShowMessage("Could not create the virtual machine: " + ex.Message, true);
        }
    }
}