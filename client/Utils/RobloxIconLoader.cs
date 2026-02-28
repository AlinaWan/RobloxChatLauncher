using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace RobloxChatLauncher.Utils
{
    public static class RobloxIconLoader
    {
        private static readonly string BaseTexturesPath;

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
        /// Returns a dictionary of icon name → Image.
        /// </summary>
        public static Dictionary<string, Image> LoadIcons(params string[] relativePaths)
        {
            var result = new Dictionary<string, Image>();

            if (string.IsNullOrEmpty(BaseTexturesPath))
                return result;

            foreach (var relPath in relativePaths)
            {
                try
                {
                    string fullPath = Path.Combine(BaseTexturesPath, relPath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(fullPath))
                        result[relPath] = Image.FromFile(fullPath);
                }
                catch
                {
                    // ignore individual failures, fallback handled by caller
                }
            }

            return result;
        }
    }
}