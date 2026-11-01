namespace HackerSimulator;

/// <summary>AQUILA has no sound. These empty methods only keep old calls compiling.</summary>
public static class Sfx
{
    public static bool Enabled { get; set; }
    public static void Tick() { }
    public static void Beep() { }
    public static void Glitch() { }
    public static void Granted() { }
}