using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HackerSimulator;

public partial class LoginView : UserControl
{
    private static MainWindow Win => (MainWindow)Application.Current.MainWindow;

    public LoginView()
    {
        InitializeComponent();

        UserBox.TextChanged += (s, e) =>
            UserHint.Visibility = UserBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        PassBox.PasswordChanged += (s, e) =>
            PassHint.Visibility = PassBox.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        Loaded += (s, e) => Focus();
    }

    private void Login_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool typing = Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is PasswordBox;

        // "e" with nothing focused opens the boot editor (password recovery)
        if (!typing && e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            Win.ShowGrub();
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (!typing) UserBox.Focus();
            else Submit();
        }
    }

    private void LogIn_Click(object sender, RoutedEventArgs e) => Submit();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        UserBox.Clear();
        PassBox.Clear();
        Message.Text = "";
        Focus();
    }

    private void PowerBtn_Click(object sender, RoutedEventArgs e) => PowerPopup.IsOpen = true;

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

    private void Submit()
    {
        if (AuthService.Login(UserBox.Text.Trim(), PassBox.Password))
        {
            Win.ShowDesktop();
            return;
        }

        Message.Text = "Authentication failure";
        PassBox.Clear();
        PassBox.Focus();
    }
}