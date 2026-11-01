using System;
using System.Diagnostics;
using System.IO;

namespace HackerSimulator;

/// <summary>Tells AQUILA which virtual machine folder it was started from (set by AQUILA Launcher).</summary>
public static class VmContext
{
    public static string DataDir { get; private set; } = "";
    public static string VmName { get; private set; } = "AQUILA";

    public static bool Init(string[] args, out string error)
    {
        error = "";
        string? dir = null;

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--vm") dir = args[i + 1];
            else if (args[i] == "--name") VmName = args[i + 1];
        }

        if (dir == null)
        {
            if (!Debugger.IsAttached)
            {
                error = "AQUILA runs inside a virtual machine.\n\nOpen AQUILA Launcher, create a virtual machine from an AQUILA .iso file and start it from there.";
                return false;
            }

            // debugging from Visual Studio only: use a throw-away folder with a dev account (dev / dev)
            DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AquilaDev");
            Directory.CreateDirectory(DataDir);
            AuthService.EnsureDevAccount();
            return true;
        }

        DataDir = dir;
        Directory.CreateDirectory(DataDir);
        if (!AuthService.HasAccount())
        {
            error = "This virtual machine has no user account.\n\nCreate a new virtual machine in AQUILA Launcher.";
            return false;
        }
        return true;
    }
}