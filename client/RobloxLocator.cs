using Microsoft.Win32;
using System;
using System.Web;
using System.Text.RegularExpressions;

namespace RobloxChatLauncher.Utils
{
    public static class RobloxLocator
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
    public static class LaunchData
    {
        public static string LaunchUri
        {
            get; set;
        }
        public static string GetGameId()
        {
            if (string.IsNullOrEmpty(LaunchUri))
                return null;

            try
            {
                // 1. Find the 'placelauncherurl:' section
                string key = "placelauncherurl:";
                int startIndex = LaunchUri.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                if (startIndex == -1)
                    return null;

                string encodedUrl = LaunchUri.Substring(startIndex + key.Length);

                // Stop at the next '+' which separates segments (if it exists)
                int plusIndex = encodedUrl.IndexOf('+');
                if (plusIndex != -1)
                    encodedUrl = encodedUrl.Substring(0, plusIndex);

                // 2. Decode the URL (converts %3F, %3D, etc.)
                string decodedUrl = HttpUtility.UrlDecode(encodedUrl);

                // 3. Extract gameId from decoded query string
                // We decode first, then look for "gameId=" literally
                Uri uri = new Uri(decodedUrl);
                var queryParams = HttpUtility.ParseQueryString(uri.Query);

                return string.IsNullOrEmpty(queryParams["gameId"]) ? null : queryParams["gameId"];
            }
            catch
            {
                // Any parsing errors just return null
                return null;
            }
        }
    }
}
