using System.Net.WebSockets;
using System.Text;
using System.Windows.Forms;
using Newtonsoft.Json;

using RobloxChatLauncher.Services;
using RobloxChatLauncher.Utils;

namespace RobloxChatLauncher
{
    [System.ComponentModel.DesignerCategory("Code")] // This covers the entire class so it only needs to be declared once here.
    public partial class ChatForm : Form
    {
        // Declare the keyboard handler at the class level so the whole form can access it (e.g., to dispose on close)
        private ChatKeyboardHandler keyboardHandler;

        // Collection of muted users (case-insensitive)
        private System.Collections.Generic.HashSet<string> mutedUsers = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Verification state tracking
        private VerificationService _verifyService = new VerificationService();
        private long _pendingRobloxId = 0;

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

                    this.Invoke((MethodInvoker)(() => chatBox.AppendText($"[System]: Connecting to server {channelId}...\r\n")));

                    // Use the passed 'ct' token here
                    await wsClient.ConnectAsync(new Uri($"wss://{Constants.Constants.BASE_URL}/"), ct);

                    var joinPayload = new
                    {
                        type = "join",
                        channelId = this.channelId,
                        // Only send the HWID if the user has successfully verified in the past
                        hwid = Properties.Settings1.Default.IsVerified
                            ? Services.VerificationService.GetMachineId()
                            : null
                    };
                    string json = JsonConvert.SerializeObject(joinPayload);

                    await wsClient.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)),
                        WebSocketMessageType.Text, true, ct);

                    this.Invoke((MethodInvoker)(() => chatBox.AppendText($"[System]: Connected successfully!\r\n")));

                    _ = Task.Run(() => ReceiveLoop(ct), ct);
                    return;
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested)
                        return;

                    if (i == maxRetries)
                    {
                        this.Invoke((MethodInvoker)(() => chatBox.AppendText($"[System]: Connection failed: {ex.Message}\r\n")));
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
                    dynamic data = JsonConvert.DeserializeObject(message);

                    this.Invoke((MethodInvoker)delegate
                    {
                        // Normal chat message
                        if (data.type == "message")
                        {
                            string sender = (string)data.sender;
                            string text = (string)data.text;

                            string rawName = sender;

                            // Handle whisper message formats to extract the actual speaker name for mute checking
                            // If it's an incoming whisper: "From Guest 12345"
                            if (sender.StartsWith("From "))
                            {
                                rawName = sender.Substring(5); // Remove "From "
                            }
                            // If it's an outgoing whisper: "To Guest 12345"
                            else if (sender.StartsWith("To "))
                            {
                                rawName = sender.Substring(3); // Remove "To "
                            }

                            // Now check the cleaned name against the mute list
                            if (!mutedUsers.Contains(rawName))
                            {
                                chatBox.AppendText($"[{sender}]: {text}\r\n");
                            }
                        }
                        // Rejection handling
                        else if (data.status == "rejected")
                        {
                            string reason = data.reason;
                            string messageText;

                            switch (reason)
                            {
                                case "moderation":
                                    messageText = "Your message was not sent as it violates community guidelines.";
                                    break;
                                case "queue_full":
                                    messageText = "Your message was rejected because the server queue is full. Please try again shortly.";
                                    break;
                                case "api_error":
                                    messageText = "Your message could not be processed due to a server error. Please try again.";
                                    break;
                                default:
                                    messageText = "Your message was not sent due to unknown reasons.";
                                    break;
                            }

                            chatBox.AppendText($"[System]: {messageText}\r\n");
                        }
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                chatBox.AppendText($"[System]: WS Receive Error: {ex.Message}\r\n");
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
                    chatBox.AppendText($"[Error]: Failed to send message: {ex.Message}\r\n");
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

        // Echo Logic
        private async Task ExecuteEchoRequest(string userMessage)
        {
            // chatBox.AppendText($"You: {userMessage}\r\n");
            try
            {
                // 2. Network Call
                var content = new StringContent(userMessage, Encoding.UTF8, "text/plain");
                // PaaS echo server for POC demo testing
                var response = await client.PostAsync($"https://{Constants.Constants.BASE_URL}/echo", content);

                if (response.IsSuccessStatusCode)
                {
                    string echoResponse = await response.Content.ReadAsStringAsync();

                    this.Invoke((MethodInvoker)delegate
                    {
                        chatBox.AppendText($"[Server]: {echoResponse} (Only you can see this message.)\r\n");
                    });
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
                    string reason = data?.reason;

                    string messageText;
                    switch (reason)
                    {
                        case "moderation":
                            messageText = "Your message was not sent as it violates community guidelines.";
                            break;
                        case "queue_full":
                            messageText = "Your message was rejected because the server queue is full. Please try again shortly.";
                            break;
                        case "api_error":
                            messageText = "Your message could not be processed due to a server error. Please try again.";
                            break;
                        default:
                            messageText = "Your message was not sent due to unknown reasons.";
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
                    chatBox.AppendText("[System]: Request timed out. (Render server may be waking up)\r\n");
                });
            }
            // 4. Catch General Errors (DNS, No Internet, etc.)
            catch (Exception ex)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    chatBox.AppendText($"[System]: Connection error: {ex.Message}\r\n");
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
                chatBox.AppendText("[System]: WebSocket not connected. Use '/rc' or '/reconnect' to connect to server.\r\n");
                return;
            }

            try
            {
                var payload = new
                {
                    type = "message",
                    text = text
                };
                string json = JsonConvert.SerializeObject(payload);
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                await wsClient.SendAsync(new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text, true, wsCts.Token);

            }
            catch (TaskCanceledException)
            {
                chatBox.AppendText("[System]: WS send timed out. (Render server may be waking up)\r\n");
            }
            catch (Exception ex)
            {
                chatBox.AppendText($"[System]: WS Send Error: {ex.Message}\r\n");
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
            string json = JsonConvert.SerializeObject(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            await wsClient.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, wsCts.Token);

            // DO NOT chatBox.AppendText here anymore. 
            // Wait for the server to send the "To {target}" message back.
        }

        private bool HandleMute(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                chatBox.AppendText("[System]: Usage: /mute <speaker>\r\n");
            }
            else
            {
                mutedUsers.Add(args.Trim());
                chatBox.AppendText($"[System]: Speaker '{args.Trim()}' has been muted.\r\n");
            }
            return true;
        }

        private bool HandleUnmute(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                chatBox.AppendText("[System]: Usage: /unmute <speaker>\r\n");
                return true;
            }

            string speaker = args.Trim();

            if (mutedUsers.Remove(speaker))
                chatBox.AppendText($"[System]: Speaker '{speaker}' has been unmuted.\r\n");
            else
                chatBox.AppendText($"[System]: Speaker '{speaker}' was not muted.\r\n");

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
                chatBox.AppendText("[System]: Usage: /w \"<speaker 12345>\" message or /w <speaker> message\r\n");
                return true;
            }

            await SendWhisperWebSocket(target, msg);
            return true;
        }

        private void OpenDebugConsole()
        {
            // Create the window
            if (NativeMethods.AllocConsole())
            {
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

                Console.Title = "Roblox Chat Launcher Debugger";
                Console.WriteLine("===================================================");
                Console.WriteLine($"DEBUG CONSOLE INITIALIZED AT {DateTime.Now}");
                Console.WriteLine($"Use '/closeconsole or '/closedebug' to close");
                Console.WriteLine("===================================================");
            }
        }

        private void CloseDebugConsole()
        {
            // Redirect output back to null so the app doesn't crash 
            // trying to write to a console that no longer exists
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);

            NativeMethods.FreeConsole();
        }

        // Commented out because it will always force a Global
        // connection before the JobId is found
        /*
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _ = ConnectWebSocket(wsCts.Token);
        }
        */

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
