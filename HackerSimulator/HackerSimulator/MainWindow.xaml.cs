using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace HackerSimulator;

public partial class MainWindow : Window
{
    private DesktopView? _desktop;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (s, e) => ShowBoot();
    }

    // boot animation -> login
    public void ShowBoot(Action? next = null) => Navigate(new BootSplashView(next ?? ShowLogin));

    public void ShowLogin()
    {
        EndSession();
        Navigate(new LoginView());
    }

    // password recovery boot editor (opened with "e" on the login screen)
    public void ShowGrub()
    {
        EndSession();
        Navigate(new GrubView());
    }

    public void Reboot()
    {
        EndSession();
        ShowBoot();
    }

    private void EndSession()
    {
        _desktop?.Shutdown();
        _desktop = null;
        AuthService.Logout();
    }

    public void ShowDesktop()
    {
        _desktop ??= new DesktopView();
        Navigate(_desktop);
    }

    private void Navigate(UserControl view)
    {
        view.Opacity = 0;
        Host.Content = view;
        view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)));
    }
}