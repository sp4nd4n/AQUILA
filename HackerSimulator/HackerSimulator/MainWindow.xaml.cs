using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        bool ctrlShift = Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift);

        // hidden profile editor: Ctrl+Shift+E from the desktop, and again to come back
        if (e.Key == Key.E && ctrlShift)
        {
            if (Host.Content is DesktopView)
            {
                ShowEditor();
                e.Handled = true;
            }
            else if (Host.Content is EditorView)
            {
                ShowDesktop();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Escape && Host.Content is EditorView)
        {
            ShowDesktop();
            e.Handled = true;
        }
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

    // kept so the profile editor's Close button keeps working
    public void ShowConsole() => ShowDesktop();

    public void ShowEditor() => Navigate(new EditorView());

    private void Navigate(UserControl view)
    {
        view.Opacity = 0;
        Host.Content = view;
        view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)));
    }
}