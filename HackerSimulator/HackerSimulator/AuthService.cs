using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace HackerSimulator;

public static class AuthService
{
    private static string Dir => VmContext.DataDir;
    private static string FilePath => Path.Combine(Dir, "auth.json");

    public class Account
    {
        public string Username { get; set; } = "";
        public string Salt { get; set; } = "";
        public string Hash { get; set; } = "";
    }

    public static string? CurrentUser { get; private set; }

    public static bool HasAccount() => Load() != null;

    public static string? StoredUsername() => Load()?.Username;

    public static void CreateAccount(string username, string password)
    {
        Save(username.Trim(), password);
        CurrentUser = username.Trim();
    }

    /// <summary>Debug builds only: makes sure a dev account (dev / dev) exists.</summary>
    public static void EnsureDevAccount()
    {
        if (!HasAccount()) Save("dev", "dev");
    }

    public static bool Login(string username, string password)
    {
        var acc = Load();
        if (acc == null) return false;
        if (!string.Equals(acc.Username, username.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (!Verify(acc, password)) return false;
        CurrentUser = acc.Username;
        return true;
    }

    /// <summary>Sets a new password without asking for the old one (passwd as root, recovery shell).</summary>
    public static void ForceSetPassword(string newPassword)
    {
        var acc = Load();
        if (acc == null) return;
        Save(acc.Username, newPassword);
    }

    public static void Logout() => CurrentUser = null;

    private static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);

    private static bool Verify(Account acc, string password)
    {
        byte[] expected = Convert.FromBase64String(acc.Hash);
        byte[] actual = Derive(password, Convert.FromBase64String(acc.Salt));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static void Save(string username, string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        var acc = new Account
        {
            Username = username,
            Salt = Convert.ToBase64String(salt),
            Hash = Convert.ToBase64String(Derive(password, salt))
        };
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(acc));
    }

    private static Account? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return JsonSerializer.Deserialize<Account>(File.ReadAllText(FilePath));
        }
        catch { return null; }
    }
}