using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AquilaLauncher;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string _snapshot = "";

    public MainWindow()
    {
        InitializeComponent();
        Reload(null);
        _timer.Tick += (s, e) => CheckStatus();
        _timer.Start();
    }

    private void Reload(string? selectId)
    {
        var vms = VmStore.Load();
        foreach (var vm in vms) vm.Status = VmStore.IsRunning(vm.Id) ? "Running" : "Powered off";

        selectId ??= (VmList.SelectedItem as VmInfo)?.Id;
        VmList.ItemsSource = vms;
        VmList.SelectedItem = vms.FirstOrDefault(v => v.Id == selectId) ?? vms.FirstOrDefault();

        _snapshot = string.Join("|", vms.Select(v => v.Id + v.Status));
        UpdateDetails();
    }

    private void CheckStatus()
    {
        var vms = VmStore.Load();
        string snap = string.Join("|", vms.Select(v => v.Id + (VmStore.IsRunning(v.Id) ? "Running" : "Powered off")));
        if (snap != _snapshot) Reload(null);
    }

    private void UpdateDetails()
    {
        if (VmList.SelectedItem is not VmInfo vm)
        {
            EmptyPanel.Visibility = Visibility.Visible;
            DetailPanel.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyPanel.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;

        bool running = vm.Status == "Running";
        NameText.Text = vm.Name;
        StatusText.Text = vm.Status;
        StatusText.Foreground = (Brush)FindResource(running ? "OkBrush" : "MutedBrush");
        UserText.Text = vm.Username;
        IsoText.Text = vm.IsoName;
        CreatedText.Text = vm.Created.ToString("f");
        StartBtn.IsEnabled = !running;
        DeleteBtn.IsEnabled = !running;
    }

    private void VmList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDetails();

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NewVmWindow { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Created != null)
            Reload(dialog.Created.Id);
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (VmList.SelectedItem is not VmInfo vm) return;
        if (!VmStore.Start(vm, out string error))
        {
            MessageBox.Show(this, error, "AQUILA LAUNCHER", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Reload(vm.Id);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (VmList.SelectedItem is not VmInfo vm) return;

        var answer = MessageBox.Show(this,
            $"Delete the virtual machine \"{vm.Name}\"?\n\nIts files, account and saved profiles will be removed permanently.",
            "AQUILA LAUNCHER", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            VmStore.Delete(vm);
            Reload(null);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not delete it: " + ex.Message, "AQUILA LAUNCHER");
        }
    }
}