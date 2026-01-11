using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Text;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Diagnostics;
using Gma.System.MouseKeyHook;
using Newtonsoft.Json;

using Utils;

namespace ChatLauncherApp
{
    // --------------------------------------------------
    // Native Win32 helpers
    // --------------------------------------------------
    static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        // GetForegroundWindow is used to determine which window is currently active (focused)
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(
            IntPtr hWnd,
            out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern bool HideCaret(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ToUnicodeEx(
            uint wVirtKey,
            uint wScanCode,
            byte[] lpKeyState,
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff,
            int cchBuff,
            uint wFlags,
            IntPtr dwhkl);

        [DllImport("user32.dll")]
        public static extern bool GetKeyboardState(byte[] lpKeyState);

        [DllImport("user32.dll")]
        public static extern IntPtr GetKeyboardLayout(uint idThread);

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        public const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        public delegate void WinEventDelegate(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc,
            uint idProcess,
            uint idThread,
            uint dwFlags);

        [DllImport("user32.dll")]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    }
    public partial class ChatForm : Form
    {
        // WebSocket connection and message handling
        private async Task ConnectWebSocket()
        {
            // The Render server may take time to wake up, so we implement retries
            int maxRetries = 12;
            int delayMilliseconds = 5000; // seconds between tries
            // Goal: Retry for up to 1 minute as
            // that's how long Render free-tier usually takes to wake up

            for (int i = 1; i <= maxRetries; i++)
            {
                try
                {
                    // Console.WriteLine($"[DEBUG] Attempt {i}/{maxRetries} - starting connection...");

                    // Clean up old client if it exists
                    wsCts?.Cancel();
                    wsClient?.Dispose();
                    wsClient = new ClientWebSocket();
                    wsCts = new CancellationTokenSource();

                    // Console.WriteLine("[DEBUG] ClientWebSocket created");

                    this.Invoke((MethodInvoker)(() => chatBox.AppendText($"[System]: Connecting to server (Attempt {i}/{maxRetries})...\r\n")));

                    // Console.WriteLine($"[DEBUG] Connecting to wss://{BASE_URL}/ ...");
                    // Try to connect
                    await wsClient.ConnectAsync(new Uri($"wss://{BASE_URL}/"), wsCts.Token);
                    // Console.WriteLine("[DEBUG] Connected to WebSocket successfully");

                    // If we reach here, connection was successful
                    var joinPayload = new
                    {
                        type = "join",
                        channelId = this.channelId
                    };
                    string json = JsonConvert.SerializeObject(joinPayload);
                    await wsClient.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)),
                        WebSocketMessageType.Text, true, wsCts.Token);

                    this.Invoke((MethodInvoker)(() => chatBox.AppendText("[System]: Connected successfully!\r\n")));

                    _ = Task.Run(ReceiveLoop);
                    return; // Exit the method successfully
                }
                catch (Exception ex)
                {
                    // Log full exception details to see inner exceptions and stack trace
                    // Console.WriteLine($"[DEBUG] Attempt {i} failed:");
                    // Console.WriteLine(ex.ToString());

                    if (i == maxRetries)
                    {
                        this.Invoke((MethodInvoker)(() => chatBox.AppendText($"[System]: Connection failed after {maxRetries} tries: {ex.Message}\r\n")));
                    }
                    else
                    {
                        // Wait before trying again
                        await Task.Delay(delayMilliseconds);
                    }
                }
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[4096];

            try
            {
                while (wsClient.State == WebSocketState.Open)
                {
                    var result = await wsClient.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        wsCts.Token
                    );

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    dynamic data = JsonConvert.DeserializeObject(message);

                    this.Invoke((MethodInvoker)delegate
                    {
                        // Normal chat message
                        if (data.type == "message")
                        {
                            chatBox.AppendText($"[{data.sender}]: {data.text}\r\n");
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
            // If empty, just exit chat mode and return
            if (string.IsNullOrWhiteSpace(rawInputText))
            {
                isChatting = false;
                rawInputText = "";
                targetOpacity = chatOffOpacity;
                SyncInput();
                return;
            }

            string userMessage = rawInputText;

            // 1. Immediate UI Feedback
            rawInputText = "";
            isChatting = false;
            targetOpacity = chatOffOpacity;
            SyncInput();

            // --- ROUTING LOGIC ---
            // Check if it's an echo command
            if (userMessage.StartsWith("/echo ", StringComparison.OrdinalIgnoreCase))
            {
                // Extract the text after "/echo "
                string echoPayload = userMessage.Substring(6);
                await ExecuteEchoRequest(echoPayload);
            }
            else
            {
                // Default: Send via WebSocket for the channel broadcast
                await SendWebSocketMessage(userMessage);
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
                var response = await client.PostAsync($"https://{BASE_URL}/echo", content);

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
                chatBox.AppendText("[System]: WebSocket not connected. Try /echo for HTTP mode.\r\n");
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

        void SyncInput()
        {
            inputBox.RawText = rawInputText;
            inputBox.IsChatting = isChatting;
            inputBox.Invalidate();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _ = ConnectWebSocket();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // WebSocket cleanup
            wsCts?.Cancel();
            wsClient?.Dispose();

            // Hook cleanup
            if (winEventHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(winEventHook);
                winEventHook = IntPtr.Zero;
            }

            base.OnFormClosed(e);
        }
    }

    // --------------------------------------------------
    // Keyboard hook (layout-correct, shift-safe)
    // --------------------------------------------------
    class ChatKeyboardHandler : IDisposable
    {
        IKeyboardMouseEvents hook;
        ChatForm form;
        bool chatMode;

        static bool IsNonTextKey(Keys key) =>
            key == Keys.Escape ||
            key == Keys.Enter ||
            key == Keys.Back ||
            key == Keys.ControlKey ||
            key == Keys.ShiftKey ||
            key == Keys.Menu; // Both alt keys

        public ChatKeyboardHandler(ChatForm chatForm)
        {
            form = chatForm;
            hook = Hook.GlobalEvents();
            hook.KeyDown += OnKeyDown;
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            // 1. Ignore all input if the chat window is minimized
            // This handles cases where the user minimizes Roblox
            if (form.WindowState == FormWindowState.Minimized)
                return;

            // 2. Ignore all input if Roblox is NOT the active (focused) window
            // We get the current foreground window and compare it to Roblox's handle
            // This handles cases where the user alt-tabs away or clicks another window
            IntPtr foregroundWindow = NativeMethods.GetForegroundWindow();
            if (!form.IsRobloxForegroundProcess())
                return;

            if (!chatMode)
            {
                // Toggle UI Visibility: Ctrl + Shift + C
                if (e.Control && e.Shift && e.KeyCode == Keys.C)
                {
                    form.ToggleVisibility();
                    e.Handled = true;
                    return;
                }
                if (e.KeyCode == Keys.OemQuestion) // slash key
                {
                    chatMode = true;
                    form.StartChatMode();
                    e.Handled = true;
                }
                return;
            }

            if (e.KeyCode == Keys.Escape)
            {
                chatMode = false;           // Stop intercepting keys in this app
                form.CancelChatMode();      // Update UI (opacity/caret) but keep text
                                            // DO NOT set e.Handled = true; 
                                            // This allows the Escape key to "pass through" to the game/Windows
                return;
            }

            if (e.KeyCode == Keys.Enter)
            {
                // Use _ = to explicitly fire and forget the task
                _ = form.Send();
                chatMode = false;
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Back)
            {
                form.Backspace();
                e.Handled = true;
                return;
            }

            string text = TranslateKey(e);
            if (!string.IsNullOrEmpty(text))
            {
                form.AppendTextFromKey(text);
                e.Handled = true;
            }
        }

        string TranslateKey(KeyEventArgs e)
        {
            // Don't translate control keys into text characters
            if (IsNonTextKey(e.KeyCode))
                return null;

            byte[] state = new byte[256];
            if (!NativeMethods.GetKeyboardState(state))
                return null;

            StringBuilder sb = new StringBuilder(8);
            IntPtr layout = NativeMethods.GetKeyboardLayout(0);

            int result = NativeMethods.ToUnicodeEx(
                (uint)e.KeyValue,
                0,
                state,
                sb,
                sb.Capacity,
                0,
                layout);

            return result > 0 ? sb.ToString() : null;
        }

        public void Dispose()
        {
            hook.KeyDown -= OnKeyDown;
            hook.Dispose();
        }
    }
}
