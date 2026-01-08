using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Text;
using System.Net.Http;
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

    // --------------------------------------------------
    // Custom input box (fake caret, custom paint)
    // --------------------------------------------------
    class ChatInputBox : TextBox
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsChatting
        {
            get; set;
        }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string RawText { get; set; } = "";

        bool caretVisible = true;
        System.Windows.Forms.Timer caretTimer;

        public ChatInputBox()
        {
            SetStyle(ControlStyles.UserPaint, true);
            ReadOnly = true;
            BorderStyle = BorderStyle.FixedSingle;

            // We need to use a fake caret because since we never truly focus the overlay,
            // the win32 caret doesn't work
            caretTimer = new System.Windows.Forms.Timer { Interval = 500 };
            caretTimer.Tick += (s, e) =>
            {
                caretVisible = !caretVisible;
                Invalidate();
            };
            caretTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);

            string text;
            Color color;

            if (!IsChatting && string.IsNullOrEmpty(RawText))
            {
                text = "Press / to type | Ctrl+Shift+C to toggle visibility";
                color = Color.FromArgb(128, Color.Gray);
            }
            else
            {
                text = RawText + (IsChatting && caretVisible ? "|" : "");
                color = ForeColor;
            }

            TextRenderer.DrawText(
                e.Graphics,
                text,
                Font,
                ClientRectangle,
                color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    // --------------------------------------------------
    // Class for round hide/unhide button
    // --------------------------------------------------
    public class RoundButton : Control
    {
        public event EventHandler Clicked;
        private Image imgOn;
        private Image imgOff;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsActive { get; set; } = true;

        public RoundButton()
        {
            this.SetStyle(ControlStyles.Selectable, false);
            LoadRobloxIcons();

            // Fix the purple stroke issue
            this.SizeChanged += (s, e) => {
                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    path.AddEllipse(0, 0, Width, Height);
                    this.Region = new Region(path);
                }
            };
        }

        private void LoadRobloxIcons()
        {
            try
            {
                string versionFolder = Utils.Utils.GetRobloxVersionFolder();
                string basePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Roblox", "Versions", versionFolder, "content", "textures", "ui", "TopBar");

                // Roblox also has `chatOn@2x.png`, `chatOn@3x.png`, `chatOff@2x.png`, and `chatOff@3x.png`
                // if needed in the future for higher DPI displays.
                // There are also voice chat icons in `content/textures/ui/VoiceChat/` if voice chat
                // support is ever added.
                string pathOn = Path.Combine(basePath, "chatOn.png");
                string pathOff = Path.Combine(basePath, "chatOff.png");

                if (File.Exists(pathOn))
                    imgOn = Image.FromFile(pathOn);
                if (File.Exists(pathOff))
                    imgOff = Image.FromFile(pathOff);
            }
            catch { /* Fallback to manual drawing or default icons if path fails */ }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.Clear(Color.Magenta); // Background transparency key

            // Draw the dark circular background
            using (var brush = new SolidBrush(Color.FromArgb(50, 50, 50)))
                e.Graphics.FillEllipse(brush, 0, 0, Width, Height);

            // Draw the appropriate Roblox icon
            Image currentImg = IsActive ? imgOn : imgOff;

            if (currentImg != null)
            {
                // Center the image within the button
                int x = (Width - currentImg.Width) / 2;
                int y = (Height - currentImg.Height) / 2;
                e.Graphics.DrawImage(currentImg, x, y);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            Clicked?.Invoke(this, e);
            base.OnClick(e);
        }
    }

    // --------------------------------------------------
    // Chat window
    // --------------------------------------------------
    class ChatForm : Form
    {
        Process robloxProcess;
        Panel mainContainer; // New container for the window
        TextBox chatBox;
        ChatInputBox inputBox;
        RoundButton toggleBtn;
        bool isWindowHidden = false;
        bool overlayTopMostActive;

        System.Windows.Forms.Timer fadeTimer;

        IntPtr winEventHook = IntPtr.Zero;
        NativeMethods.WinEventDelegate winEventDelegate;

        float chatOnOpacity = 1.0f;
        float chatOffOpacity = 0.7f;
        float targetOpacity = 0.7f;
        const float fadeStep = 0.05f;

        bool isChatting;
        string rawInputText = "";

        void OnRobloxLocationChanged(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            if (hwnd != robloxProcess.MainWindowHandle)
                return;

            if (NativeMethods.IsIconic(hwnd))
            {
                BeginInvoke((MethodInvoker)(() =>
                    WindowState = FormWindowState.Minimized));
                return;
            }

            NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect);

            BeginInvoke((MethodInvoker)(() =>
            {
                if (WindowState == FormWindowState.Minimized)
                    WindowState = FormWindowState.Normal;

                Location = new Point(rect.Left + 10, rect.Top + 40);
            }));
        }

        public bool IsRobloxForegroundProcess()
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero)
                return false;

            NativeMethods.GetWindowThreadProcessId(fg, out uint pid);
            return pid == (uint)robloxProcess.Id;
        }

        private static readonly HttpClient client = new HttpClient()
        {
            // If the server doesn't respond in x seconds, throw an exception
            Timeout = TimeSpan.FromSeconds(60) // Set to 60 as Render free-tier may take time to wake up
        };

        public void ToggleVisibility()
        {
            this.Invoke((MethodInvoker)delegate {
                isWindowHidden = !isWindowHidden;
                mainContainer.Visible = !isWindowHidden;

                // Update the visual state of the button
                toggleBtn.IsActive = !isWindowHidden;
                toggleBtn.Invalidate();
            });
        }

        public ChatForm(Process proc)
        {
            robloxProcess = proc;
            robloxProcess.EnableRaisingEvents = true;
            robloxProcess.Exited += RobloxProcess_Exited;

            this.ShowInTaskbar = false; // Hide the GUI process from taskbar

            // Form Transparency/Styling
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.Magenta;
            this.TransparencyKey = Color.Magenta; // Makes form background invisible
            this.Width = 350;
            this.Height = 300;
            this.TopMost = true;

            // Circular Toggle Button (Parented to Form, not Container)
            toggleBtn = new RoundButton { Location = new Point(114, 2), Size = new Size(45, 45), Cursor = Cursors.Hand };
            toggleBtn.Clicked += (s, e) => {
                isWindowHidden = !isWindowHidden;
                mainContainer.Visible = !isWindowHidden;
            };
            this.Controls.Add(toggleBtn);

            // Main Window Container
            mainContainer = new Panel
            {
                Location = new Point(10, 65),
                Size = new Size(330, 220),
                BackColor = Color.FromArgb(45, 47, 49) // Roblox Gray
            };
            this.Controls.Add(mainContainer);

            // Chat History
            chatBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(45, 47, 49),
                ForeColor = Color.White,
                TabStop = false
            };

            // Non-clickable Input Bar
            inputBox = new ChatInputBox
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White,
                Enabled = false // THIS prevents clicking/focus
            };

            mainContainer.Controls.Add(chatBox);
            mainContainer.Controls.Add(inputBox);

            // Hide real Win32 caret defensively
            chatBox.GotFocus += (s, e) => NativeMethods.HideCaret(chatBox.Handle);
            chatBox.MouseDown += (s, e) => NativeMethods.HideCaret(chatBox.Handle);
            inputBox.GotFocus += (s, e) => ActiveControl = null;
            inputBox.MouseDown += (s, e) => ActiveControl = null;

            fadeTimer = new System.Windows.Forms.Timer { Interval = 50 };
            fadeTimer.Tick += UpdateOpacity;
            fadeTimer.Start();

            winEventDelegate = OnRobloxLocationChanged;

            winEventHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero,
                winEventDelegate,
                (uint)robloxProcess.Id,
                0,
                NativeMethods.WINEVENT_OUTOFCONTEXT);
        }

        private void RobloxProcess_Exited(object sender, EventArgs e)
        {
            // Invoke on UI thread
            if (!IsDisposed)
                BeginInvoke((MethodInvoker)(() => Close()));
        }

        void UpdateOpacity(object sender, EventArgs e)
        {
            // Z-order scoping
            // The overlay will always stay above Roblox,
            // but not necessarily above other windows if those windows are above Roblox.
            bool robloxActive = this.IsRobloxForegroundProcess();

            if (robloxActive && !overlayTopMostActive)
            {
                TopMost = true;
                overlayTopMostActive = true;
            }
            else if (!robloxActive && overlayTopMostActive)
            {
                TopMost = false;
                overlayTopMostActive = false;
            }

            // Fade logic
            if (Math.Abs(Opacity - targetOpacity) < 0.01f)
                return;
            Opacity += Opacity < targetOpacity ? fadeStep : -fadeStep;
        }

        public void StartChatMode()
        {
            isChatting = true;
            // Don't clear the input bar
            // rawInputText = "";
            targetOpacity = chatOnOpacity;
            SyncInput();
        }

        public void AppendTextFromKey(string text)
        {
            rawInputText += text;
            SyncInput();
        }

        public void Backspace()
        {
            if (rawInputText.Length > 0)
                rawInputText = rawInputText[..^1];
            SyncInput();
        }

        public void CancelChatMode()
        {
            isChatting = false;
            targetOpacity = chatOffOpacity;
            SyncInput();
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
            if (!string.IsNullOrWhiteSpace(rawInputText))
            {
                string userMessage = rawInputText;

                // 1. Immediate UI Feedback
                chatBox.AppendText($"You: {userMessage}\r\n");
                rawInputText = "";
                isChatting = false;
                targetOpacity = chatOffOpacity;
                SyncInput();

                try
                {
                    // 2. Network Call
                    var content = new StringContent(userMessage, Encoding.UTF8, "text/plain");
                    // PaaS echo server for POC demo testing
                    var response = await client.PostAsync("https://RobloxChatLauncherDemo.onrender.com/echo", content);

                    if (response.IsSuccessStatusCode)
                    {
                        string echoResponse = await response.Content.ReadAsStringAsync();

                        this.Invoke((MethodInvoker)delegate
                        {
                            chatBox.AppendText($"Server: {echoResponse}\r\n");
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
                                messageText = "Your last message was not sent as it violates community guidelines.";
                                break;
                            case "queue_full":
                                messageText = "Your last message was rejected because the server queue is full. Please try again shortly.";
                                break;
                            case "api_error":
                                messageText = "Your last message could not be processed due to a server error. Please try again.";
                                break;
                            default:
                                messageText = "Your last message was not sent due to unknown reasons.";
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
                        chatBox.AppendText("System: Request timed out. (Render server may be waking up)\r\n");
                    });
                }
                // 4. Catch General Errors (DNS, No Internet, etc.)
                catch (Exception ex)
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        chatBox.AppendText($"System: Connection error: {ex.Message}\r\n");
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
        }

        void SyncInput()
        {
            inputBox.RawText = rawInputText;
            inputBox.IsChatting = isChatting;
            inputBox.Invalidate();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
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
