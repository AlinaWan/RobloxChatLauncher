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
        /// </summary>
        public static Image LoadIcon(string relativePath)
        {
            if (string.IsNullOrEmpty(BaseTexturesPath))
                return null;

            try
            {
                string fullPath = Path.Combine(BaseTexturesPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(fullPath) ? Image.FromFile(fullPath) : null;
            }
            catch { return null; }
        }
    }
}