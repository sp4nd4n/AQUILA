using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HackerSimulator;

public static class ProfileStore
{
    private static string Dir => VmContext.DataDir;
    private static string PhotoDir => Path.Combine(Dir, "photos");
    private static string FilePath => Path.Combine(Dir, "profiles.json");

    public static List<Profile> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { return new(); }
    }

    public static void Save(IEnumerable<Profile> profiles)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath,
            JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string PhotoPath(string fileName) => Path.Combine(PhotoDir, fileName);

    public static string ImportPhoto(string sourcePath, string profileId)
    {
        Directory.CreateDirectory(PhotoDir);
        string name = profileId + "_" + DateTime.Now.Ticks + Path.GetExtension(sourcePath).ToLowerInvariant();
        File.Copy(sourcePath, PhotoPath(name), true);
        return name;
    }

    public static void DeletePhoto(string fileName)
    {
        try { if (!string.IsNullOrEmpty(fileName)) File.Delete(PhotoPath(fileName)); }
        catch { }
    }
}