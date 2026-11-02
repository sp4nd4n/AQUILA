using System;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace HackerSimulator;

public class Profile : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private string _name = "";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            PropertyChanged?.Invoke(this, new(nameof(Name)));
            PropertyChanged?.Invoke(this, new(nameof(DisplayName)));
        }
    }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(_name) ? "(unnamed)" : _name;

    public string Photo { get; set; } = "";
    public string Alias { get; set; } = "";
    public string Birthday { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Address { get; set; } = "";
    public string Workplace { get; set; } = "";
    public string Instagram { get; set; } = "";
    public string Facebook { get; set; } = "";
    public string Snapchat { get; set; } = "";
    public string Twitter { get; set; } = "";
    public string Extra { get; set; } = "";
}