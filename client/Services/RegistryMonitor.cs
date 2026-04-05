using Microsoft.Win32;

namespace RobloxChatLauncher.Utils
{
    /// <summary>
    ///     Monitors a specified Windows registry key for changes and raises an event when a change is detected.
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

            _ = Task.Run(() =>
            {
                using AutoResetEvent regEvent = new(false);
                var waitHandles = new WaitHandle[] { regEvent, token.WaitHandle };

                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        NativeMethods.RegNotifyChangeKeyValue(
                            _key.Handle,
                            _watchSubtree,
                            NativeMethods.RegChangeNotifyFilter.Value,
                            regEvent.SafeWaitHandle.DangerousGetHandle(),
                            true); // true = async

                        // Wait for either registry change or cancellation
                        int signaled = WaitHandle.WaitAny(waitHandles);
                        if (signaled == 1) // token triggered
                            break; // Handles OperationCanceledException

                        RegistryChanged?.Invoke();

                        if (_debounceMilliseconds > 0)
                            Thread.Sleep(_debounceMilliseconds);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Ignore disposed handles
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
                _key?.Close();
            }
            catch (ObjectDisposedException) { /* Already gone, ignore */ }
            finally
            {
                _cts?.Dispose();
            }
        }
    }
}