using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using RobloxChatLauncher.Core;
using RobloxChatLauncher.Localization;
using RobloxChatLauncher.Services;
using RobloxChatLauncher.Utils;

namespace RobloxChatLauncher
{
    [System.ComponentModel.DesignerCategory("Code")] // This covers the entire class so it only needs to be declared once here.
    public partial class ChatForm : Form
    {
        // Channel IDs and WebSocket
        private string channelId = "global";
        private RobloxChatLauncher.Services.RobloxAreaService _robloxService;
        private ClientWebSocket wsClient;
        private CancellationTokenSource wsCts = new CancellationTokenSource(); // Make sure it's not null when the form starts or it will raise an exception at runtime

        // Declare the keyboard handler at the class level so the whole form can access it (e.g., to dispose on close)
        private ChatKeyboardHandler keyboardHandler;

        // Collection of muted users (case-insensitive)
        private System.Collections.Generic.HashSet<string> mutedUsers = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Verification state tracking
        private VerificationService _verifyService = new VerificationService();
        private long _pendingRobloxId = 0;

        // Bool to track if the debug console is currently open
        private bool _isDebugConsoleOpen = false;

        internal static readonly HttpClient Client = new HttpClient()
        {
            // If the server doesn't respond in x seconds, throw an exception
            Timeout = TimeSpan.FromSeconds(60) // Set to 60 as Render free-tier may take time to wake up
        };

        // WebSocket connection and message handling
        private async Task ConnectWebSocket(CancellationToken ct)
        {
            int maxRetries = 12;
            int delayMilliseconds = 5000;

            for (int i = 1; i <= maxRetries; i++)
            {
                // EXIT if a newer connection request has started
                if (ct.IsCancellationRequested)
                    return;

                try
                {
                    wsClient?.Dispose();
                    wsClient = new ClientWebSocket();

                    this.Invoke((MethodInvoker)(() => chatBox.AppendText($"[{Strings.System}]: {string.Format(Strings.ConnectingToServer, channelId)}\r\n")));

                    // Use the passed 'ct' token here
                    await wsClient.ConnectAsync(new Uri($"wss://{Constants.BASE_URL}/"), ct);

                    var joinPayload = new
                    {
                        type = "join",
                        channelId = this.channelId,
                        // Only send the HWID if the user has successfully verified in the past
                        hwid = Properties.Settings1.Default.IsVerified
                            ? Services.VerificationService.GetMachineId()
                            : null
                    };
                    string json = JsonSerializer.Serialize(joinPayload);

                    await wsClient.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)),
                        WebSocketMessageType.Text, true, ct);

                    this.Invoke((MethodInvoker)(() => chatBox.AppendText($"[{Strings.System}]: {Strings.ConnectedSuccessfully}\r\n")));

                    _ = Task.Run(() => ReceiveLoop(ct), ct);
                    return;
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested)
                        return;

                    if (i == maxRetries)
                    {
                        this.Invoke((MethodInvoker)(() => chatBox.AppendText($"[{Strings.System}]: {string.Format(Strings.ConnectionFailed, ex.Message)}\r\n")));
                    }
                    else
                    {
                        // Task.Delay must also use the token so it wakes up immediately 
                        // if a new server is detected
                        await Task.Delay(delayMilliseconds, ct);
                    }
                }
            }
        }

        private async Task RestartWebSocketAsync()
        {
            // Handle WebSocket cleanup and new connection
            wsCts?.Cancel();
            wsCts?.Dispose();
            wsCts = new CancellationTokenSource();

            await ConnectWebSocket(wsCts.Token);
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            var buffer = new byte[4096];
            try
            {
                while (wsClient.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await wsClient.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        ct // Use the passed token
                    );

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // server closed connection (often unclean on PaaS)
                        _ = ConnectWebSocket(ct); // ct passed here
                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var data = JsonNode.Parse(message);
                    var dataType = data?["type"]?.ToString();
                    this.Invoke((MethodInvoker)delegate
                    {
                        // Normal chat message
                        if (dataType == "message")
                        {
                            string sender = data?["sender"]?.ToString() ?? string.Empty;
                            string text = data?["text"]?.ToString() ?? string.Empty;
                            string whisperType = data?["whisperType"]?.ToString() ?? string.Empty; // New field

                            // 1. Check mute status using the raw sender name
                            if (!mutedUsers.Contains(sender))
                            {
                                string displaySender = sender;

                                // 2. Handle display formatting client-side
                                if (whisperType == "from")
                                    displaySender = $"{string.Format(Strings.WhisperFrom, sender)}";
                                else if (whisperType == "to")
                                    displaySender = $"{string.Format(Strings.WhisperTo, sender)}";

                                chatBox.AppendText($"[{displaySender}]: {text}\r\n");
                            }
                        }
                        // Rejection handling
                        else if (data?["status"]?.ToString() == "rejected")
                        {
                            string reason = data["reason"]?.ToString() ?? string.Empty;
                            string messageText;

                            switch (reason)
                            {
                                case "moderation":
                                    messageText = $"{Strings.MessageRejectedModeration}";
                                    break;
                                case "queue_full":
                                    messageText = $"{Strings.MessageRejectedQueueFull}";
                                    break;
                                case "api_error":
                                    messageText = $"{Strings.MessageRejectedApiError}";
                                    break;
                                default:
                                    messageText = $"{Strings.MessageRejectedUnknown}";
                                    break;
                            }

                            chatBox.AppendText($"[{Strings.System}]: {messageText}\r\n");
                        }
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                chatBox.AppendText($"[{Strings.System}]: {string.Format(Strings.WSReceiveError, ex.Message)}\r\n");
            }
        }
        public async Task Send()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawInputText))
                {
                    ExitChatUI();
                    return;
                }

                string userMessage = rawInputText;
                ExitChatUI(); // Helper to reset targetOpacity/SyncInput

                // 1. Check if it's a command first
                bool isCommand = await HandleCommands(userMessage);

                // 2. If not a command, send normally via WebSocket
                if (!isCommand)
                {
                    await SendWebSocketMessage(userMessage);
                }
            }
            catch (Exception ex)
            {
                this.Invoke((MethodInvoker)delegate {
                    chatBox.AppendText($"[{Strings.Error}]: {string.Format(Strings.FailedToSendMessage, ex.Message)}\r\n");
                });
            }
        }

        // Small helper to keep Send() tidy
        private void ExitChatUI()
        {
            rawInputText = "";
            isChatting = false;
            targetOpacity = chatOffOpacity;
            SyncInput();
        }

        private async Task SendEmoteMailAsync(string emoteName)
        {
            if (!Properties.Settings1.Default.IsVerified)
            {
                chatBox.AppendText($"[{Strings.System}]: {Strings.MustBeVerifiedEmote}\r\n");
                return;
            }

            try
            {
                var payload = new
                {
                    jobId = channelId,
                    targetPlayer = "ignored", // The server will determine the actual target based on the JobId and HWID, so this is just a placeholder to satisfy the expected payload structure
                    type = "Emote",
                    data = new
                    {
                        name = emoteName
                    }
                };

                var request = new HttpRequestMessage(HttpMethod.Post,
                    $"https://{Constants.BASE_URL}/api/v1/mail");

                request.Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json");

                // IMPORTANT: HWID is required for server verification
                request.Headers.Add("x-hwid", VerificationService.GetMachineId());

                var response = await Client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    chatBox.AppendText($"[{Strings.System}]: {Strings.FailedToQueueEmote}\r\n");
                }
            }
            catch (Exception ex)
            {
                chatBox.AppendText($"[{Strings.System}]: {string.Format(Strings.EmoteError, ex.Message)}\r\n");
            }
        }

        // Echo Logic
        private async Task ExecuteEchoRequest(string userMessage)
        {
            // chatBox.AppendText($"You: {userMessage}\r\n");
            try
            {
                // 2. Network Call
                var content = new StringContent(userMessage, Encoding.UTF8, "text/plain");
                // PaaS echo server for POC demo testing
                var response = await Client.PostAsync($"https://{Constants.BASE_URL}/echo", content);

                if (response.IsSuccessStatusCode)
                {
                    string echoResponse = await response.Content.ReadAsStringAsync();

                    this.Invoke((MethodInvoker)delegate
                    {
                        chatBox.AppendText($"[{Strings.Server}]: {string.Format(Strings.EchoResponse, echoResponse)}\r\n");
                    });
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var data = JsonNode.Parse(json);
                    string reason = data?["reason"]?.ToString() ?? string.Empty;

                    string messageText;
                    switch (reason)
                    {
                        case "moderation":
                            messageText = $"{Strings.MessageRejectedModeration}";
                            break;
                        case "queue_full":
                            messageText = $"{Strings.MessageRejectedQueueFull}";
                            break;
                        case "api_error":
                            messageText = $"{Strings.MessageRejectedApiError}";
                            break;
                        default:
                            messageText = $"{Strings.MessageRejectedUnknown}";
                            break;
                    }

                    this.Invoke((MethodInvoker)delegate
                    {
                        chatBox.AppendText(messageText + "\r\n");
                    });
                }
            }
            // 3. Catch Timeout Specifically
            catch (TaskCanceledException)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    chatBox.AppendText($"[{Strings.System}]: {Strings.RequestTimedOut}\r\n");
                });
            }
            // 4. Catch General Errors (DNS, No Internet, etc.)
            catch (Exception ex)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    chatBox.AppendText($"[{Strings.System}]: {string.Format(Strings.ConnectionError, ex.Message)}\r\n");
                });
            }
            finally
            {
                // Ensure the chat always scrolls to the bottom and hides caret
                this.Invoke((MethodInvoker)delegate
                {
                    chatBox.SelectionStart = chatBox.Text.Length;
                    chatBox.ScrollToCaret();
                    NativeMethods.HideCaret(chatBox.Handle);
                });
            }
        }

        // WebSocket Sender
        private async Task SendWebSocketMessage(string text)
        {
            // Don't append the user's own message here; the server will broadcast it back
            if (wsClient == null || wsClient.State != WebSocketState.Open)
            {
                chatBox.AppendText($"[{Strings.System}]: {Strings.WSNotConnected}\r\n");
                return;
            }

            try
            {
                var payload = new
                {
                    type = "message",
                    text = text
                };
                string json = JsonSerializer.Serialize(payload);
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                await wsClient.SendAsync(new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text, true, wsCts.Token);

            }
            catch (TaskCanceledException)
            {
                chatBox.AppendText($"[{Strings.System}]: {Strings.WSSendTimedOut}\r\n");
            }
            catch (Exception ex)
            {
                chatBox.AppendText($"[{Strings.System}]: {string.Format(Strings.WSSendError, ex.Message)}\r\n");
            }
        }

        private async Task SendWhisperWebSocket(string target, string text)
        {
            if (wsClient?.State != WebSocketState.Open)
                return;

            var payload = new
            {
                type = "whisper",
                target = target,
                text = text
            };
            string json = JsonSerializer.Serialize(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            await wsClient.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, wsCts.Token);

            // DO NOT chatBox.AppendText here anymore. 
            // Wait for the server to send the "To {target}" message back.
        }

        private bool HandleMute(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                chatBox.AppendText($"[{Strings.System}]: {Strings.UsageMute}\r\n");
            }
            else
            {
                string speaker = args.Trim().Trim('"');

                mutedUsers.Add(speaker);
                chatBox.AppendText($"[{Strings.System}]: {string.Format(Strings.MutedSpeaker, speaker)}\r\n");
            }
            return true;
        }

        private bool HandleUnmute(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                chatBox.AppendText($"[{Strings.System}]: {Strings.UsageUnmute}\r\n");
                return true;
            }

            string speaker = args.Trim().Trim('"');

            if (mutedUsers.Remove(speaker))
                chatBox.AppendText($"[{Strings.System}]: {string.Format(Strings.UnmutedSpeaker, speaker)}\r\n");
            else
                chatBox.AppendText($"[{Strings.System}]: {string.Format(Strings.SpeakerNotMuted, speaker)}\r\n");

            return true;
        }

        private async Task<bool> HandleWhisperAsync(string args)
        {
            string target = "";
            string msg = "";

            if (args.StartsWith("\""))
            {
                int endQuoteIndex = args.IndexOf("\"", 1);
                if (endQuoteIndex != -1)
                {
                    target = args.Substring(1, endQuoteIndex - 1);
                    msg = args.Substring(endQuoteIndex + 1).Trim();
                }
            }
            else
            {
                string[] whisperParts = args.Split(' ', 2);
                if (whisperParts.Length == 2)
                {
                    target = whisperParts[0];
                    msg = whisperParts[1];
                }
            }

            if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(msg))
            {
                chatBox.AppendText($"[{Strings.System}]: {Strings.UsageWhisper}\r\n");
                return true;
            }

            await SendWhisperWebSocket(target, msg);
            return true;
        }

        private void HandleDebugConsole()
        {
            if (!_isDebugConsoleOpen)
            {
                // Create the window
                if (NativeMethods.AllocConsole())
                {
                    _isDebugConsoleOpen = true;

                    // Re-route the standard output streams so Console.WriteLine actually works
                    var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                    Console.SetOut(writer);
                    Console.SetError(writer);

                    // Get the handle to the console window
                    IntPtr consoleWindow = NativeMethods.GetConsoleWindow();
                    if (consoleWindow != IntPtr.Zero)
                    {
                        // Get the system menu for the console and delete the Close (SC_CLOSE) option
                        // This prevents users from accidentally closing the console and crashing the entire app since it's a child window of the main form
                        IntPtr sysMenu = NativeMethods.GetSystemMenu(consoleWindow, false);
                        if (sysMenu != IntPtr.Zero)
                        {
                            NativeMethods.DeleteMenu(sysMenu, NativeMethods.SC_CLOSE, NativeMethods.MF_BYCOMMAND);
                        }
                    }

                    // Set the output to UTF-8 so it can render the characters correctly
                    Console.OutputEncoding = System.Text.Encoding.UTF8;
                    Console.InputEncoding = System.Text.Encoding.UTF8;

                    Console.Title = $"{Strings.DebugConsoleTitle}";
                    Console.WriteLine($"{Strings.DebugConsoleHorizontalRule}");
                    Console.WriteLine($"{string.Format(Strings.DebugConsoleInitialized, DateTime.Now)}");
                    Console.WriteLine($"{Strings.DebugConsoleUseClose}");
                    Console.WriteLine($"{Strings.DebugConsoleHorizontalRule}");
                }
            }
            else
            {
                _isDebugConsoleOpen = false;

                // Redirect output back to null so the app doesn't crash 
                // trying to write to a console that no longer exists
                Console.SetOut(TextWriter.Null);
                Console.SetError(TextWriter.Null);

                NativeMethods.FreeConsole();
            }
        }

        // Comprehensive cleanup on form close to ensure all resources are properly released and no memory leaks occur
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Only dispose if RobloxProcess_Exited hasn't done it yet
            _robloxService?.Dispose();
            _robloxService = null;

            // Standard WebSocket cleanup
            try
            {
                wsCts?.Cancel();
            }
            catch (ObjectDisposedException) { }

            wsCts?.Dispose();
            wsClient?.Dispose();

            // Unhook the WinEvent hook to prevent memory leaks and potential issues with dangling hooks after the form is closed
            if (winEventHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(winEventHook);
                winEventHook = IntPtr.Zero;
            }

            // Dispose the keyboard handler which unhooks the global keyboard events to prevent memory leaks
            keyboardHandler?.Dispose();
            keyboardHandler = null;

            base.OnFormClosed(e);
        }
    }
}
