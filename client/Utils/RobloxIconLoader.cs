using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace RobloxChatLauncher.Utils
{
    public static class RobloxIconLoader
    {
        private static readonly string BaseTexturesPath;
        private static readonly Dictionary<string, Image> _cache = new Dictionary<string, Image>();

        static RobloxIconLoader()
        {
            try
            {
                string versionFolder = RobloxLocator.GetVanillaRobloxVersionFolder();
                BaseTexturesPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Roblox", "Versions", versionFolder, "content", "textures");
            }
            catch
            {
                BaseTexturesPath = null; // fallback if Roblox path cannot be determined
            }
        }

        /// <summary>
        /// Loads one or more icons from the Roblox textures folder.
        /// Paths are relative to the `textures/` folder.
        /// </summary>
        public static Image LoadIcon(string relativePath)
        {
            if (string.IsNullOrEmpty(BaseTexturesPath))
                return null;

            // 1. Check Cache first (really elegant and fast)
            if (_cache.TryGetValue(relativePath, out var cachedImage))
                return cachedImage;

            try
            {
                string fullPath = Path.Combine(BaseTexturesPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                    return null;

                // 2. Load without locking the file
                using (var stream = new MemoryStream(File.ReadAllBytes(fullPath)))
                {
                    var img = Image.FromStream(stream);
                    _cache[relativePath] = img; // Store for next time
                    return img;
                }
            }
            catch { return null; }
        }
    }
}