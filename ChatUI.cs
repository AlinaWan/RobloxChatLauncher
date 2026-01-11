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
            this.SizeChanged += (s, e) =>
            {
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
                string versionFolder = Utils.RobloxLocator.GetRobloxVersionFolder();
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
    public partial class ChatForm : Form
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

        private string channelId;
        private ClientWebSocket wsClient;
        private CancellationTokenSource wsCts;
        private const string BASE_URL = "RobloxChatLauncherDemo.onrender.com";

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
            this.Invoke((MethodInvoker)delegate
            {
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
            toggleBtn.Clicked += (s, e) =>
            {
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

            // Get the gameId from LaunchData
            string gameId = Utils.LaunchData.GetGameId();

            if (!string.IsNullOrEmpty(gameId))
            {
                channelId = gameId;
                chatBox.AppendText($"[Server]: Attempting to use server channel: {channelId}.\r\n");
            }
            else
            {
                channelId = "global";
                chatBox.AppendText("[Server]: Warning: Attempting to use the global channel. This is likely because you joined using the play button. Join a server directly to use server-scoped channels.\r\n");
            }
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
    }
}