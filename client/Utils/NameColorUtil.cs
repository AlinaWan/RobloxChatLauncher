using System.Drawing;

namespace RobloxChatLauncher.Utils
{
    /// <summary>
    /// Ported from ExtraDataInitializer.lua to maintain visual consistency with default chat colors:
    /// https://github.com/Roblox/Core-Scripts/blob/425d2d641bdc4b6c1104a9d5f6c53c9ea758c5cb/CoreScriptsRoot/Modules/Server/ServerChat/DefaultChatModules/ExtraDataInitializer.lua#
    /// 
    /// Originally by Xsitsu, licensed under the Apache License, Version 2.0:
    /// https://github.com/Roblox/Core-Scripts/blob/425d2d641bdc4b6c1104a9d5f6c53c9ea758c5cb/LICENSE.txt
    /// 
    /// Original Luau ModuleScript retrieved from Roblox DevForum, courtesy of 7z99:
    /// https://devforum.roblox.com/t/how-to-get-a-users-default-chat-colour-simple-module/957515
    /// 
    /// This ported version is part of Roblox Chat Launcher at https://github.com/AlinaWan/RobloxChatLauncher and is licensed under the GNU General Public License v3.0.
    /// </summary>
    public static class NameColorUtil
    {
        private static readonly Color[] NAME_COLORS =
        {
        Color.FromArgb(253, 41, 67),
        Color.FromArgb(1, 162, 255),
        Color.FromArgb(2, 184, 87),
        Color.FromArgb(170, 85, 255),   // Bright violet
        Color.FromArgb(255, 170, 0),    // Bright orange
        Color.FromArgb(255, 255, 0),    // Bright yellow
        Color.FromArgb(255, 102, 204),  // Light reddish violet
        Color.FromArgb(215, 197, 154)   // Brick yellow
    };

        private static int GetNameValue(string name)
        {
            int value = 0;

            for (int i = 0; i < name.Length; i++)
            {
                int cValue = name[i];
                int reverseIndex = name.Length - i;

                if (name.Length % 2 == 1)
                    reverseIndex--;

                if (reverseIndex % 4 >= 2)
                    cValue = -cValue;

                value += cValue;
            }

            return value;
        }

        public static Color GetNameColor(string name)
        {
            int colorOffset = 0;

            int index = (GetNameValue(name) + colorOffset) % NAME_COLORS.Length;

            if (index < 0)
                index += NAME_COLORS.Length;

            return NAME_COLORS[index];
        }
    }
}
