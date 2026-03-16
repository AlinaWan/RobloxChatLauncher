using Microsoft.Win32;
using System;
using System.Threading;

namespace RobloxChatLauncher.Utils
{
    /// <summary>
    ///     Monitors a specified Windows registry key for changes and raises an event when a change is detected.
    ///     This class exists to fix aggressive bootstrappers like Fishstrap from hijacking our registry key
    ///     and breaking Roblox Chat Launcher. By monitoring the registry key, we can detect when it has been
    ///     changed and re-register the launcher.
    /// </summary>
    public class RegistryMonitor
    {
        readonly RegistryKey registryKey;
        readonly bool watchSubtree;

        public event Action RegistryChanged;

        public RegistryMonitor(RegistryKey key, bool watchSubtree = false)
        {
            registryKey = key;
            this.watchSubtree = watchSubtree;
        }

        public void Start()
        {
            Thread thread = new Thread(MonitorThread);
            thread.IsBackground = true;
            thread.Start();
        }

        void MonitorThread()
        {
            while (true)
            {
                NativeMethods.RegNotifyChangeKeyValue(
                    registryKey.Handle,
                    watchSubtree,
                    NativeMethods.RegChangeNotifyFilter.Value,
                    IntPtr.Zero,
                    false);

                RegistryChanged?.Invoke();
            }
        }
    }
}