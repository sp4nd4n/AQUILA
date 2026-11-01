using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AquilaLauncher;

public class VmInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Username { get; set; } = "";
    public string IsoName { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;

    [JsonIgnore] public string Folder { get; set; } = "";
    [JsonIgnore] public string Status { get; set; } = "Powered off";
}

public static class VmStore
{
    private static readonly Dictionary<string, Process> Running = new();

    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AquilaLauncher", "VMs");

    public static List<VmInfo> Load()
    {
        var list = new List<VmInfo>();
        if (!Directory.Exists(Root)) return list;

        foreach (string dir in Directory.GetDirectories(Root))
        {
            try
            {
                string file = Path.Combine(dir, "vm.json");
                if (!File.Exists(file)) continue;
                var vm = JsonSerializer.Deserialize<VmInfo>(File.ReadAllText(file));
                if (vm == null) continue;
                vm.Folder = dir;
                list.Add(vm);
            }
            catch { }
        }
        return list.OrderBy(v => v.Created).ToList();
    }

    public static bool NameExists(string name) =>
        Load().Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

    public static bool IsRunning(string id) =>
        Running.TryGetValue(id, out var p) && !p.HasExited;

    /// <summary>Checks that the file really is an AQUILA image.</summary>
    public static bool ValidateIso(string path, out string error)
    {
        error = "";
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var manifest = zip.GetEntry("aquila.manifest");
            if (manifest == null)
            {
                error = "This file is not an AQUILA image (aquila.manifest is missing).";
                return false;
            }

            using var reader = new StreamReader(manifest.Open());
            using var doc = JsonDocument.Parse(reader.ReadToEnd());
            if (!doc.RootElement.TryGetProperty("magic", out var magic) || magic.GetString() != "AQUILA-ISO")
            {
                error = "This file is not an AQUILA image.";
                return false;
            }
            if (zip.GetEntry("system/Aquila.exe") == null)
            {
                error = "The image is incomplete (Aquila.exe is missing).";
                return false;
            }
            return true;
        }
        catch
        {
            error = "This file is not a valid AQUILA image.";
            return false;
        }
    }

    public static async Task<VmInfo> CreateAsync(string name, string isoPath, string fullName,
        string username, string password, IProgress<(double pct, string text)> progress)
    {
        string id = Guid.NewGuid().ToString("N");
        string folder = Path.Combine(Root, id);
        string system = Path.Combine(folder, "system");
        string data = Path.Combine(folder, "data");

        var info = new VmInfo
        {
            Id = id,
            Name = name,
            FullName = fullName,
            Username = username,
            IsoName = Path.GetFileName(isoPath),
            Created = DateTime.Now,
            Folder = folder
        };

        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(system);
                Directory.CreateDirectory(data);

                using var zip = ZipFile.OpenRead(isoPath);
                var entries = zip.Entries
                    .Where(e => e.FullName.StartsWith("system/", StringComparison.OrdinalIgnoreCase) && e.Name.Length > 0)
                    .ToList();

                long total = Math.Max(1, entries.Sum(e => e.Length));
                long done = 0;
                string systemFull = Path.GetFullPath(system) + Path.DirectorySeparatorChar;

                foreach (var entry in entries)
                {
                    string rel = entry.FullName.Substring("system/".Length).Replace('/', Path.DirectorySeparatorChar);
                    string dest = Path.GetFullPath(Path.Combine(system, rel));
                    if (!dest.StartsWith(systemFull, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The image contains an invalid path.");

                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    entry.ExtractToFile(dest, true);
                    done += entry.Length;
                    progress.Report((done * 100.0 / total, "Installing " + entry.Name));
                }

                progress.Report((100, "Creating your account..."));
                WriteAccount(data, username, password);

                File.WriteAllText(Path.Combine(folder, "vm.json"),
                    JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
            });
        }
        catch
        {
            try { Directory.Delete(folder, true); } catch { }
            throw;
        }

        return info;
    }

    // same format as AQUILA's own account file (salted PBKDF2 hash)
    private static void WriteAccount(string dataDir, string username, string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        var account = new
        {
            Username = username,
            Salt = Convert.ToBase64String(salt),
            Hash = Convert.ToBase64String(hash)
        };
        File.WriteAllText(Path.Combine(dataDir, "auth.json"), JsonSerializer.Serialize(account));
    }

    public static bool Start(VmInfo vm, out string error)
    {
        error = "";
        string exe = Path.Combine(vm.Folder, "system", "Aquila.exe");
        if (!File.Exists(exe))
        {
            error = "The system files of this virtual machine are missing. Delete it and create a new one.";
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("--vm");
            psi.ArgumentList.Add(Path.Combine(vm.Folder, "data"));
            psi.ArgumentList.Add("--name");
            psi.ArgumentList.Add(vm.Name);

            var process = Process.Start(psi);
            if (process != null) Running[vm.Id] = process;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void Delete(VmInfo vm)
    {
        Running.Remove(vm.Id);
        if (Directory.Exists(vm.Folder)) Directory.Delete(vm.Folder, true);
    }
}