using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

using Utils;
using ChatLauncherApp;

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

        string uri = args[0];

        // Console.WriteLine("Roblox Launch Detected");
        // DEBUG: Print full URI for inspection
        // Console.WriteLine(uri);
        // DEBUG: Keep the console open for inspection
        // Console.WriteLine("Keeping console open for inspection. Press Enter to continue launching Roblox...");
        // Console.ReadLine();

        string robloxExe = Utils.Utils.ResolveRobloxPlayerPath();
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
