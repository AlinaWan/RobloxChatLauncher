using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text.Json;

using RobloxChatLauncher.Utils;
using RobloxChatLauncher.Localization;

namespace RobloxChatLauncher.Services
{
    public class RobloxAreaService : IDisposable
    {
        /**
         * Portions of this file are derived from Bloxstrap's ActivityWatcher, which is licensed under the MIT License, with modifications to fit the needs of this project.
         * This modified version is part of Roblox Chat Launcher at https://github.com/AlinaWan/RobloxChatLauncher and is licensed under the GNU General Public License v3.0.
         * 
         * Original source:
         * =====================================
         * 
         * https://github.com/bloxstraplabs/bloxstrap/blob/9a062367f78b2e5e48ff53d233c001536978230e/Bloxstrap/Integrations/ActivityWatcher.cs
         * 
         * License text for the original source:
         * =====================================
         * 
         * MIT License
         * 
         * Copyright (c) 2022 pizzaboxer
         * 
         * Permission is hereby granted, free of charge, to any person obtaining a copy
         * of this software and associated documentation files (the "Software"), to deal
         * in the Software without restriction, including without limitation the rights
         * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
         * copies of the Software, and to permit persons to whom the Software is
         * furnished to do so, subject to the following conditions:
         * 
         * The above copyright notice and this permission notice shall be included in all
         * copies or substantial portions of the Software.
         * 
         * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
         * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
         * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
         * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
         * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
         * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
         * SOFTWARE.
         */
        private const string GameMessageEntry                = "[FLog::Output] [BloxstrapRPC]";
        private const string GameJoiningEntry                = "[FLog::Output] ! Joining game";

        // these entries are technically volatile!
        // they only get printed depending on their configured FLog level, which could change at any time
        // while levels being changed is fairly rare, please limit the number of varying number of FLog types you have to use, if possible

        private const string GameTeleportingEntry            = "[FLog::GameJoinUtil] GameJoinUtil::initiateTeleportToPlace";
        private const string GameJoiningPrivateServerEntry   = "[FLog::GameJoinUtil] GameJoinUtil::joinGamePostPrivateServer";
        private const string GameJoiningReservedServerEntry  = "[FLog::GameJoinUtil] GameJoinUtil::initiateTeleportToReservedServer";
        private const string GameJoiningUniverseEntry        = "[FLog::GameJoinLoadTime] Report game_join_loadtime:";
        private const string GameJoiningUDMUXEntry           = "[FLog::Network] UDMUX Address = ";
        private const string GameJoinedEntry                 = "[FLog::Network] serverId:";
        private const string GameDisconnectedEntry           = "[FLog::Network] Time to disconnect replication data:";
        private const string GameLeavingEntry                = "[FLog::SingleSurfaceApp] leaveUGCGameInternal";

        private const string GameJoiningEntryPattern         = @"! Joining game '([0-9a-f\-]{36})' place ([0-9]+) at ([0-9\.]+)";
        private const string GameJoiningPrivateServerPattern = @"""accessCode"":""([0-9a-f\-]{36})""";
        private const string GameJoiningUniversePattern      = @"universeid:([0-9]+).*userid:([0-9]+)";
        private const string GameJoiningUDMUXPattern         = @"UDMUX Address = ([0-9\.]+), Port = [0-9]+ \| RCC Server Address = ([0-9\.]+), Port = [0-9]+";
        private const string GameJoinedEntryPattern          = @"serverId: ([0-9\.]+)\|[0-9]+";
        private const string GameMessageEntryPattern         = @"\[BloxstrapRPC\] (.*)";

        private readonly string _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");
        private CancellationTokenSource? _cts;

        // Activity tracking
        public ActivityData Data { get; set; } = new();
        public List<ActivityData> History { get; private set; } = new();
        public bool IsInGame { get; private set; } = false;
        private bool _teleportMarker = false;
        private bool _reservedTeleportMarker = false;

        private DateTime LastRPCRequest;

        public string LogLocation = null!;

        private Process? _robloxProcess;
        private DateTime _sessionStartTime;

        public event EventHandler<string>? OnLogEntry;
        public event EventHandler? OnGameJoin;
        public event EventHandler? OnGameLeave;
        public event EventHandler<Message>? OnRPCMessage;
        public event EventHandler? OnLogOpen;
        public event EventHandler? OnAppClose;

        private bool _disposed = false;

        public async Task Start(Process robloxProc)
        {
            string logIdent = ($"{Strings.Watcher}::Start");

            // okay, here's the process:
            //
            // - tail the latest log file from %localappdata%\roblox\logs
            // - check for specific lines to determine player's game activity as shown below:
            //
            // - get the place id, job id and machine address from '! Joining game '{{JOBID}}' place {{PLACEID}} at {{MACHINEADDRESS}}' entry
            // - confirm place join with 'serverId: {{MACHINEADDRESS}}|{{MACHINEPORT}}' entry
            // - check for leaves/disconnects with 'Time to disconnect replication data: {{TIME}}' entry
            //
            // we'll tail the log file continuously, monitoring for any log entries that we need to determine the current game activity

            _robloxProcess = robloxProc;
            _sessionStartTime = robloxProc.StartTime;

            FileInfo logFileInfo;
            string logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");

            if (!Directory.Exists(logDirectory))
                return;

            DebugConsole.WriteLine($"{logIdent}: Opening Roblox log file...");

            while (true)
            {
                logFileInfo = new DirectoryInfo(logDirectory)
                    .GetFiles()
                    .Where(x => x.Name.Contains("Player", StringComparison.OrdinalIgnoreCase) && x.CreationTime <= DateTime.Now)
                    .OrderByDescending(x => x.CreationTime)
                    .FirstOrDefault();

                if (logFileInfo == null)
                {
                    await Task.Delay(1000);
                    continue;
                }

                // ignore logs created before the Roblox process started
                if (logFileInfo.CreationTime < _sessionStartTime.AddSeconds(-5))
                {
                    await Task.Delay(1000);
                    continue;
                }

                if (logFileInfo.CreationTime.AddSeconds(15) > DateTime.Now)
                    break;

                DebugConsole.WriteLine($"{logIdent}: Could not find recent enough log file, waiting... (newest is {logFileInfo.Name})");
                await Task.Delay(1000);
            }

            LogLocation = logFileInfo.FullName;

            OnLogOpen?.Invoke(this, EventArgs.Empty);

            var logFileStream = logFileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            logFileStream.Seek(0, SeekOrigin.End); // start at the end of the file

            DebugConsole.WriteLine($"{logIdent}: Opened {LogLocation}");

            using var streamReader = new StreamReader(logFileStream);

            while (!_disposed)
            {
                string? log = await streamReader.ReadLineAsync();

                if (log is null)
                    await Task.Delay(1000);
                else
                    ReadLogEntry(log);
            }
        }
        
        private void ReadLogEntry(string entry)
        {
            string logIdentity = ($"{Strings.Watcher}::ReadLogEntry");

            OnLogEntry?.Invoke(this, entry);

            int logMessageIdx = entry.IndexOf(' ');
            if (logMessageIdx == -1)
                return;

            string logMessage = entry[(logMessageIdx + 1)..];

            if (logMessage.StartsWith(GameLeavingEntry))
            {
                DebugConsole.WriteLine($"{logIdentity}: User is back into the desktop app");
                OnAppClose?.Invoke(this, EventArgs.Empty);

                if (Data.PlaceId != 0 && !IsInGame)
                {
                    DebugConsole.WriteLine($"{logIdentity}: User appears to be leaving from a cancelled/errored join");
                    Data = new();
                }

                OnGameLeave?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (!IsInGame && Data.PlaceId == 0)
            {
                // We are not in a game, nor are in the process of joining one
                if (logMessage.StartsWith(GameJoiningPrivateServerEntry))
                {
                    Data.ServerType = ServerType.Private;

                    var match = Regex.Match(logMessage, GameJoiningPrivateServerPattern);
                    if (match.Groups.Count != 2)
                    {
                        DebugConsole.WriteLine($"{logIdentity}: Failed to assert format for game join private server entry");
                        return;
                    }

                    Data.AccessCode = match.Groups[1].Value;
                }
                else if (logMessage.StartsWith(GameJoiningEntry))
                {
                    var match = Regex.Match(logMessage, GameJoiningEntryPattern);
                    if (match.Groups.Count != 4)
                    {
                        DebugConsole.WriteLine($"{logIdentity}: Failed to assert format for game join entry");
                        return;
                    }

                    IsInGame = false;
                    Data.PlaceId = long.Parse(match.Groups[2].Value);
                    Data.JobId = match.Groups[1].Value;
                    Data.MachineAddress = match.Groups[3].Value;

                    DebugConsole.WriteLine($"{logIdentity}: Joining Game ({Data})");
                    OnGameJoin?.Invoke(this, EventArgs.Empty);
                }
            }
            else if (!IsInGame && Data.PlaceId != 0)
            {
                // We are not confirmed to be in a game, but we are in the process of joining one
                if (logMessage.StartsWith(GameJoiningUniverseEntry))
                {
                    var match = Regex.Match(logMessage, GameJoiningUniversePattern);
                    if (match.Groups.Count != 3)
                    {
                        DebugConsole.WriteLine($"{logIdentity}: Failed to assert format for game join universe entry");
                        return;
                    }

                    Data.UniverseId = long.Parse(match.Groups[1].Value);
                    Data.UserId = long.Parse(match.Groups[2].Value);
                }
                else if (logMessage.StartsWith(GameJoiningUDMUXEntry))
                {
                    var match = Regex.Match(logMessage, GameJoiningUDMUXPattern);
                    if (match.Groups.Count != 3 || match.Groups[2].Value != Data.MachineAddress)
                    {
                        DebugConsole.WriteLine($"{logIdentity}: Failed to assert format for game join UDMUX entry");
                        return;
                    }

                    Data.MachineAddress = match.Groups[1].Value;
                    DebugConsole.WriteLine($"{logIdentity}: Server is UDMUX protected ({Data})");
                }
                else if (logMessage.StartsWith(GameJoinedEntry))
                {
                    var match = Regex.Match(logMessage, GameJoinedEntryPattern);
                    if (match.Groups.Count != 2 || match.Groups[1].Value != Data.MachineAddress)
                    {
                        DebugConsole.WriteLine($"{logIdentity}: Failed to assert format for game joined entry");
                        return;
                    }

                    IsInGame = true;
                    Data.TimeJoined = DateTime.Now;
                    DebugConsole.WriteLine($"{logIdentity}: Joined Game ({Data})");
                    OnGameJoin?.Invoke(this, EventArgs.Empty);
                }
            }
            else if (IsInGame && Data.PlaceId != 0)
            {
                // We are confirmed to be in a game
                if (logMessage.StartsWith(GameDisconnectedEntry))
                {
                    DebugConsole.WriteLine($"{logIdentity}: Disconnected from Game ({Data})");
                    Data.TimeLeft = DateTime.Now;
                    History.Insert(0, Data);
                    IsInGame = false;
                    Data = new();
                    OnGameLeave?.Invoke(this, EventArgs.Empty);
                }
                else if (logMessage.StartsWith(GameTeleportingEntry))
                {
                    DebugConsole.WriteLine($"{logIdentity}: Initiating teleport to server ({Data})");
                    _teleportMarker = true;
                }
                else if (logMessage.StartsWith(GameJoiningReservedServerEntry))
                {
                    _teleportMarker = true;
                    _reservedTeleportMarker = true;
                }
                else if (logMessage.StartsWith(GameMessageEntry))
                {
                    var match = Regex.Match(logMessage, GameMessageEntryPattern);
                    if (match.Groups.Count != 2)
                    {
                        DebugConsole.WriteLine($"{logIdentity}: Failed to assert format for RPC message entry");
                        return;
                    }

                    string messagePlain = match.Groups[1].Value;
                    Message? message;

                    DebugConsole.WriteLine($"{logIdentity}: Received message: '{messagePlain}'");

                    if ((DateTime.Now - LastRPCRequest).TotalSeconds <= 1)
                    {
                        DebugConsole.WriteLine($"{logIdentity}: Dropping message as ratelimit has been hit");
                        return;
                    }

                    try
                    {
                        message = JsonSerializer.Deserialize<Message>(messagePlain);
                    }
                    catch (Exception)
                    {
                        DebugConsole.WriteLine($"{logIdentity}: Failed to parse message! (JSON deserialization threw an exception)");
                        return;
                    }

                    if (message is null || string.IsNullOrEmpty(message.Command))
                    {
                        DebugConsole.WriteLine($"{logIdentity}: Failed to parse message! (Command is empty or null)");
                        return;
                    }

                    if (message.Command == "SetLaunchData")
                    {
                        string? data;
                        try
                        {
                            data = message.Data.Deserialize<string>();
                        }
                        catch (Exception)
                        {
                            DebugConsole.WriteLine($"{logIdentity}: Failed to parse message! (JSON deserialization threw an exception)");
                            return;
                        }

                        if (data is null || data.Length > 200)
                        {
                            DebugConsole.WriteLine($"{logIdentity}: Data cannot be longer than 200 characters");
                            return;
                        }

                        Data.RPCLaunchData = data;
                    }

                    OnRPCMessage?.Invoke(this, message);
                    LastRPCRequest = DateTime.Now;
                }
            }
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

    public class ActivityData
    {
        public long PlaceId;
        public string? JobId;
        public string? MachineAddress;
        public long UniverseId;
        public long UserId;
        public ServerType ServerType;
        public bool IsTeleport;
        public string? RPCLaunchData;
        public DateTime TimeJoined;
        public DateTime TimeLeft;
        public string? AccessCode;

        public override string ToString()
        {
            return $"PlaceId={PlaceId}, JobId={JobId}, MachineAddress={MachineAddress}, UniverseId={UniverseId}, UserId={UserId}, ServerType={ServerType}";
        }
    }

    public enum ServerType
    {
        Public,
        Private,
        Reserved
    }

    public class Message
    {
        public string Command { get; set; } = null!;
        public JsonElement Data { get; set; }
    }
}
