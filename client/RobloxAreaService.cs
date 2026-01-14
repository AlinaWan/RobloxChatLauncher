using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace RobloxChatLauncher.Services
{
	public class RobloxAreaService : IDisposable
	{
		// Some constants/patterns inspired by Bloxstrap (MIT licensed)
		// See: https://github.com/bloxstraplabs/bloxstrap/blob/main/Bloxstrap/Integrations/ActivityWatcher.cs
		private const string GameJoiningEntry = "[FLog::Output] ! Joining game";
		private const string GameJoiningEntryPattern = @"! Joining game '([0-9a-f\-]{36})'";

		private readonly string _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");
		private CancellationTokenSource? _cts;
		private string? _currentJobId;
        private Process? _robloxProcess;
        private DateTime _sessionStartTime;

        public event EventHandler<string>? OnJobIdChanged;

        private bool _disposed = false; // To prevent redundant calls raising exception on Roblox termination

		public void Start(Process robloxProc)
        {
            _robloxProcess = robloxProc;
            _sessionStartTime = robloxProc.StartTime;

            Console.WriteLine($"[DEBUG]: Watcher: Searching for logs in: {_logDirectory}");
			if (!Directory.Exists(_logDirectory))
			{
				Console.WriteLine("[DEBUG]: Watcher: ERROR: Log directory does not exist!");
				return;
			}

			var files = Directory.GetFiles(_logDirectory, "*.log");
			Console.WriteLine($"[DEBUG]: Watcher: Found {files.Length} log files.");

			_cts = new CancellationTokenSource();
			Task.Run(() => WatchLoop(_cts.Token));
		}

        private async Task WatchLoop(CancellationToken token)
        {
            string? lastTrackedFile = null;

            while (!token.IsCancellationRequested)
            {
                var newestLog = GetNewestLog();

                // Check if the file is truly different and exists
                if (newestLog != null && newestLog.FullName != lastTrackedFile)
                {
                    // Verify the file isn't empty (Roblox is still initializing it)
                    if (newestLog.Length > 0)
                    {
                        lastTrackedFile = newestLog.FullName;
                        await TailFile(newestLog.FullName, token);
                    }
                }

                await Task.Delay(1000, token);
            }
        }

        private async Task TailFile(string filePath, CancellationToken token)
        {
            Console.WriteLine($"[DEBUG]: Watcher: Initializing {Path.GetFileName(filePath)}");

            try
            {
                string? initialJobId = GetLastJobIdInFile(filePath);

                // ONLY fire the event if it's a NEW JobId
                if (initialJobId != null && initialJobId != _currentJobId)
                {
                    Console.WriteLine($"[DEBUG]: Watcher: Found NEW JobId on startup: {initialJobId}");
                    _currentJobId = initialJobId;
                    OnJobIdChanged?.Invoke(this, _currentJobId);
                }
                else if (initialJobId == _currentJobId)
                {
                    Console.WriteLine("[DEBUG]: Watcher: JobId hasn't changed, skipping UI update.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG]: Watcher: Startup scan failed: {ex.Message}");
            }

            // 2. REAL-TIME TAILING
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			stream.Seek(0, SeekOrigin.End); // Jump to end for future lines
			using var reader = new StreamReader(stream);

			while (!token.IsCancellationRequested)
			{
				// Check if Roblox started a NEW log file
				var newest = GetNewestLog();
				if (newest != null && newest.FullName != filePath)
				{
					Console.WriteLine("[DEBUG]: Watcher: Switching to newer log file.");
					break;
				}

				string? line = await reader.ReadLineAsync();
				if (line == null)
				{
					await Task.Delay(1000, token); // Wait for Roblox to write more
					continue;
				}

				if (line.Contains(GameJoiningEntry))
				{
					Match match = Regex.Match(line, GameJoiningEntryPattern);
					if (match.Success)
					{
						string jobId = match.Groups[1].Value;
						if (jobId != _currentJobId)
						{
							_currentJobId = jobId;
							OnJobIdChanged?.Invoke(this, _currentJobId);
						}
					}
				}
			}
		}

		// Helper method to read the file safely while Roblox is running
		// Otherwise we can't read it due to file locks
		private string? GetLastJobIdInFile(string filePath)
		{
			using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using var reader = new StreamReader(stream);

			// We only care about the end of the file where the latest "Joining game" would be
			// Let's read the whole thing safely into a list of lines
			var allLines = new List<string>();
			while (!reader.EndOfStream)
			{
				allLines.Add(reader.ReadLine() ?? "");
				if (allLines.Count > 500)
					allLines.RemoveAt(0); // Keep memory low
			}

			// Look backwards for the pattern
			for (int i = allLines.Count - 1; i >= 0; i--)
			{
				if (allLines[i].Contains(GameJoiningEntry))
				{
					Match match = Regex.Match(allLines[i], GameJoiningEntryPattern);
					if (match.Success)
						return match.Groups[1].Value;
				}
			}
			return null;
		}

        private FileInfo? GetNewestLog()
        {
            if (!Directory.Exists(_logDirectory) || _robloxProcess == null)
                return null;

            return new DirectoryInfo(_logDirectory)
                .GetFiles("*.log")
                // Rule 1: Must be modified in the last minute
                // Rule 2: File Creation must be AFTER Roblox started (to ignore logs from previous joins)
                .Where(f => f.LastWriteTime > DateTime.Now.AddMinutes(-1) &&
                            f.CreationTime >= _sessionStartTime.AddSeconds(-5)) // 5s buffer for safety
                .OrderByDescending(x => x.LastWriteTime)
                .FirstOrDefault();
        }

        public void Dispose()
		{
			if (_disposed)
				return;
			_disposed = true;

			try
			{
				// Only cancel if it hasn't been disposed yet
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