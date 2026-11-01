using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace HackerSimulator;

public partial class EditorView : UserControl
{
    private readonly ObservableCollection<Profile> _profiles = new(ProfileStore.Load());
    private Profile? _current;
    private bool _loading;

    private static MainWindow Win => (MainWindow)Application.Current.MainWindow;

    public EditorView()
    {
        InitializeComponent();
        ProfileList.ItemsSource = _profiles;

        Loaded += (s, e) =>
        {
            if (_profiles.Count > 0) ProfileList.SelectedIndex = 0;
            else SetFormEnabled(false);
        };
        Unloaded += (s, e) => { CommitForm(); SaveAll(); };
    }

    private void SetFormEnabled(bool on)
    {
        FormPanel.IsEnabled = on;
        FormPanel.Opacity = on ? 1 : 0.35;
        DeleteBtn.IsEnabled = on;
    }

    private void SetStatus(string text, string brushKey)
    {
        Status.Foreground = (Brush)FindResource(brushKey);
        Status.Text = text;
    }

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CommitForm();
        _current = ProfileList.SelectedItem as Profile;
        if (_current == null) { SetFormEnabled(false); return; }
        SetFormEnabled(true);
        LoadForm(_current);
    }

    private void LoadForm(Profile p)
    {
        _loading = true;
        NameBox.Text = p.Name;
        AliasBox.Text = p.Alias;
        BirthBox.Text = p.Birthday;
        PhoneBox.Text = p.Phone;
        EmailBox.Text = p.Email;
        AddressBox.Text = p.Address;
        WorkBox.Text = p.Workplace;
        InstaBox.Text = p.Instagram;
        FbBox.Text = p.Facebook;
        SnapBox.Text = p.Snapchat;
        XBox.Text = p.Twitter;
        ExtraBox.Text = p.Extra;
        _loading = false;
        ShowPhoto(p.Photo);
        Status.Text = "";
    }

    private void CommitForm()
    {
        if (_current == null || _loading) return;
        var p = _current;
        p.Name = NameBox.Text.Trim();
        p.Alias = AliasBox.Text.Trim();
        p.Birthday = BirthBox.Text.Trim();
        p.Phone = PhoneBox.Text.Trim();
        p.Email = EmailBox.Text.Trim();
        p.Address = AddressBox.Text.Trim();
        p.Workplace = WorkBox.Text.Trim();
        p.Instagram = InstaBox.Text.Trim();
        p.Facebook = FbBox.Text.Trim();
        p.Snapchat = SnapBox.Text.Trim();
        p.Twitter = XBox.Text.Trim();
        p.Extra = ExtraBox.Text.TrimEnd();
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _current == null) return;
        _current.Name = NameBox.Text;   // live-updates the list
    }

    private void ShowPhoto(string fileName)
    {
        PhotoImg.Source = null;
        PhotoHint.Visibility = Visibility.Visible;
        if (string.IsNullOrEmpty(fileName)) return;

        string path = ProfileStore.PhotoPath(fileName);
        if (!File.Exists(path)) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // don't keep the file locked
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            PhotoImg.Source = bmp;
            PhotoHint.Visibility = Visibility.Collapsed;
        }
        catch { }
    }

    private void PhotoBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var dlg = new OpenFileDialog
        {
            Title = "Choose photo",
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            string newName = ProfileStore.ImportPhoto(dlg.FileName, _current.Id);
            ProfileStore.DeletePhoto(_current.Photo);
            _current.Photo = newName;
            ShowPhoto(newName);
        }
        catch (Exception ex)
        {
            SetStatus("✖ Could not load photo: " + ex.Message, "RedBrush");
        }
    }

    private void RemovePhotoBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        ProfileStore.DeletePhoto(_current.Photo);
        _current.Photo = "";
        ShowPhoto("");
    }

    private void AddBtn_Click(object sender, RoutedEventArgs e)
    {
        CommitForm();
        var p = new Profile { Name = "New Target" };
        _profiles.Add(p);
        ProfileList.SelectedItem = p;
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var answer = MessageBox.Show($"Delete profile \"{_current.DisplayName}\"? This can't be undone.",
            "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        var p = _current;
        int idx = _profiles.IndexOf(p);
        _current = null;                       // so the removed profile isn't committed again
        ProfileStore.DeletePhoto(p.Photo);
        _profiles.Remove(p);
        if (_profiles.Count > 0) ProfileList.SelectedIndex = Math.Min(idx, _profiles.Count - 1);
        SaveAll();
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        CommitForm();
        if (SaveAll()) SetStatus("✔ SAVED", "GreenBrush");
        else SetStatus("✖ Could not save profiles.", "RedBrush");
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Win.ShowConsole();

    private bool SaveAll()
    {
        try { ProfileStore.Save(_profiles); return true; }
        catch { return false; }
    }
}