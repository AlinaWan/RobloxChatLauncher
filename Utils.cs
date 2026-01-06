using Microsoft.Win32;

namespace Utils
{
    public static class Utils
    {
        public static string GetRobloxVersionFolder()
        {
            using var key = Registry.ClassesRoot.OpenSubKey(@"roblox-player\shell\open\command");
            return key?.GetValue("version") as string;
        }

        public static string ResolveRobloxPlayerPath()
        {
            var versionFolder = GetRobloxVersionFolder();
            if (string.IsNullOrEmpty(versionFolder))
                return null;

            var exePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox", "Versions", versionFolder, "RobloxPlayerBeta.exe");

            return File.Exists(exePath) ? exePath : null;
        }
    }
}