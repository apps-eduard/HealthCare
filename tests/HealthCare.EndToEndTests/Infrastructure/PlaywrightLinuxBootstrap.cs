using System.Runtime.CompilerServices;

namespace HealthCare.EndToEndTests;

internal static class PlaywrightLinuxBootstrap
{
    [ModuleInitializer]
    internal static void ConfigureLinuxLibraryPath()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var sysrootLib = Path.Combine(home, "playwright-sysroot", "usr", "lib", "x86_64-linux-gnu");
        var sysrootLibAlt = Path.Combine(home, "playwright-sysroot", "lib", "x86_64-linux-gnu");
        if (!Directory.Exists(sysrootLib))
        {
            return;
        }

        var existing = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
        var prefix = Directory.Exists(sysrootLibAlt)
            ? $"{sysrootLib}:{sysrootLibAlt}"
            : sysrootLib;
        Environment.SetEnvironmentVariable(
            "LD_LIBRARY_PATH",
            string.IsNullOrWhiteSpace(existing) ? prefix : $"{prefix}:{existing}");
    }
}
