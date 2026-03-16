using Microsoft.Win32;

namespace RobloxChatLauncher.Utils
{
    /// <summary>
    ///     Monitors a specified Windows registry key for changes and raises an event when a change is detected.
    ///     This class exists to fix aggressive bootstrappers like Fishstrap from hijacking our registry key
    ///     and breaking Roblox Chat Launcher. By monitoring the registry key, we can detect when it has been
    ///     changed and re-register the launcher.
    /// </summary>
    public class RegistryMonitor : IDisposable
    {
        private readonly RegistryKey _key;
        private readonly bool _watchSubtree;
        private readonly int _debounceMilliseconds;
        private bool _disposed;
        private CancellationTokenSource? _cts;

        public event Action? RegistryChanged;

        public RegistryMonitor(RegistryKey key, bool watchSubtree = false, int debounceMilliseconds = 0)
        {
            _key = key ?? throw new ArgumentNullException(nameof(key));
            _watchSubtree = watchSubtree;
            _debounceMilliseconds = debounceMilliseconds;
        }

        public void Start()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RegistryMonitor));
            if (_cts != null)
                return; // Already started

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        NativeMethods.RegNotifyChangeKeyValue(
                            _key.Handle,
                            _watchSubtree,
                            NativeMethods.RegChangeNotifyFilter.Value,
                            IntPtr.Zero,
                            false);

                        RegistryChanged?.Invoke();

                        await Task.Delay(_debounceMilliseconds, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested, ignore
                }
            }, token);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException) { /* Already gone, ignore */ }
            finally
            {
                _cts?.Dispose();
            }
        }
    }
}