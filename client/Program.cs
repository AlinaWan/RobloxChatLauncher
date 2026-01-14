using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

using RobloxChatLauncher.Utils;
using RobloxChatLauncher;

class Program
{
    static Process robloxProcess;
    static ChatForm chatForm;
    static ChatKeyboardHandler keyboardHandler;

    static void Main(string[] args)
    {
        RegisterAsRobloxLauncher();

        if (args.Length == 0)
        {
            Console.WriteLine("Launcher registered. Waiting for roblox-player launch.");
            return;
        }

        RobloxChatLauncher.Utils.LaunchData.LaunchUri = args[0];
        string uri = RobloxChatLauncher.Utils.LaunchData.LaunchUri;
        string gameId = RobloxChatLauncher.Utils.LaunchData.GetGameId();

        // We use RobloxChatLauncher.Services.RobloxAreaService
        // to detect the JobId now.
        // Do NOT use the gameId from the URI anymore

        string robloxExe = RobloxChatLauncher.Utils.RobloxLocator.ResolveRobloxPlayerPath();
        if (robloxExe == null)
        {
            Console.WriteLine("RobloxPlayerBeta.exe not found.");
            return;
        }

        robloxProcess = Process.Start(new ProcessStartInfo
        {
            FileName = robloxExe,
            Arguments = uri,
            UseShellExecute = false
        });

        Thread chatThread = new Thread(() =>
        {
            chatForm = new ChatForm(robloxProcess);
            keyboardHandler = new ChatKeyboardHandler(chatForm);
            Application.Run(chatForm);
        });

        chatThread.SetApartmentState(ApartmentState.STA);
        chatThread.Start();
    }

    static void RegisterAsRobloxLauncher()
    {
        string exePath = Process.GetCurrentProcess().MainModule.FileName;
        using var key = Registry.ClassesRoot.CreateSubKey(@"roblox-player\shell\open\command");
        key.SetValue("", $"\"{exePath}\" \"%1\"");
    }
}
