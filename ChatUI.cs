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
                text = "Press / key | Ctrl+Shift+C to hide";
                color = Color.FromArgb(180, 200, 200, 200); // Gray placeholder
            }
            else
            {
                text = RawText + (IsChatting && caretVisible ? "|" : "");
                color = ForeColor; // White text
            }

            // Set a 10px margin so text doesn't hit the edge
            Rectangle textRect = new Rectangle(10, 0, ClientRectangle.Width - 50, ClientRectangle.Height);

            TextRenderer.DrawText(
                e.Graphics,
                text,
                Font,
                textRect,
                color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            // Draw the arrow icon on the right
            TextRenderer.DrawText(e.Graphics, "➤", Font,
                new Rectangle(Width - 35, 0, 30, Height), color,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }

    // --------------------------------------------------
    // Class for round hide/unhide button
    // --------------------------------------------------
    public class RoundButton : Control
    {
        public event EventHandler Clicked;
        public event EventHandler<Point> Dragged; // Notify form of movement
        public event EventHandler DragEnded;

        private Image imgOn;
        private Image imgOff;
        private System.Windows.Forms.Timer holdTimer;
        private bool isDragging = false;
        private Point lastMousePos;
        private const int HOLD_THRESHOLD = 500; // Milliseconds to hold before release (for dragging)

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsActive { get; set; } = true;

        public RoundButton()
        {
            this.SetStyle(ControlStyles.Selectable, false);
            LoadRobloxIcons();

            holdTimer = new System.Windows.Forms.Timer { Interval = HOLD_THRESHOLD };
            holdTimer.Tick += (s, e) =>
            {
                holdTimer.Stop();
                isDragging = true;
                this.Cursor = Cursors.SizeAll;
            };

            // Fix the purple issue
            this.SizeChanged += (s, e) =>
            {
                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    path.AddEllipse(0, 0, Width, Height);
                    this.Region = new Region(path);
                }
            };
        }

        // Mouse event overrides for dragging behavior
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                lastMousePos = e.Location;
                holdTimer.Start();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (isDragging)
            {
                // Calculate how much the mouse moved since last frame
                int deltaX = e.X - lastMousePos.X;
                int deltaY = e.Y - lastMousePos.Y;
                Dragged?.Invoke(this, new Point(deltaX, deltaY));
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            holdTimer.Stop();
            if (isDragging)
            {
                isDragging = false;
                this.Cursor = Cursors.Hand;
                DragEnded?.Invoke(this, EventArgs.Empty);
            }
            else if (ClientRectangle.Contains(e.Location))
            {
                Clicked?.Invoke(this, EventArgs.Empty);
            }
            base.OnMouseUp(e);
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
            // Important: Use AntiAlias for smooth circles
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.Clear(Color.Magenta); // Background transparency key

            // Draw the dark circular background (Roblox style)
            using (var brush = new SolidBrush(Color.FromArgb(50, 50, 50)))
                e.Graphics.FillEllipse(brush, 0, 0, Width, Height);

            // Draw the chat icon (imgOn or imgOff)
            Image currentImg = IsActive ? imgOn : imgOff;

            if (currentImg != null)
            {
                // Center the icon image inside the circle
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

        private Point defaultOffset = new Point(10, 40); // Original offset
        private Point currentOffset = new Point(10, 40); // Tracks user customization
        private bool isUserMovingWindow = false;

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

        // Sets a rounded region for the given control as
        // Windows Forms does not natively support rounded corners.
        private void SetRoundedRegion(Control control, int radius)
        {
            System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
            path.StartFigure();
            path.AddArc(new Rectangle(0, 0, radius, radius), 180, 90);
            path.AddArc(new Rectangle(control.Width - radius, 0, radius, radius), 270, 90);
            path.AddArc(new Rectangle(control.Width - radius, control.Height - radius, radius, radius), 0, 90);
            path.AddArc(new Rectangle(0, control.Height - radius, radius, radius), 90, 90);
            path.CloseFigure();
            control.Region = new Region(path);
        }

        void OnRobloxLocationChanged(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            if (hwnd != robloxProcess.MainWindowHandle || isUserMovingWindow)
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

                // Use the dynamic offset instead of +10, +40
                Location = new Point(rect.Left + currentOffset.X, rect.Top + currentOffset.Y);
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
            this.Width = 500;
            this.Height = 400;
            this.TopMost = true;

            // Circular Toggle Button (Parented to Form, not Container)
            toggleBtn = new RoundButton
            {
                Location = new Point(115, 2),
                Size = new Size(45, 45),
                Cursor = Cursors.Hand
            };

            toggleBtn.Clicked += (s, e) =>
            {
                isWindowHidden = !isWindowHidden;
                mainContainer.Visible = !isWindowHidden;
            };

            toggleBtn.Dragged += (s, delta) =>
            {
                isUserMovingWindow = true;
                // Update the offset based on drag
                currentOffset.X += delta.X;
                currentOffset.Y += delta.Y;

                // Immediate visual update
                this.Location = new Point(this.Location.X + delta.X, this.Location.Y + delta.Y);
            };

            toggleBtn.DragEnded += (s, e) =>
            {
                isUserMovingWindow = false;

                // 1. Get where Roblox is right now
                NativeMethods.GetWindowRect(robloxProcess.MainWindowHandle, out NativeMethods.RECT rect);

                // 2. Calculate our current offset relative to Roblox's top-left
                int relativeX = this.Location.X - rect.Left;
                int relativeY = this.Location.Y - rect.Top;

                // 3. Snap check: If relative position is close to default, snap to it
                int snapDistance = 20; // Adjust as needed
                if (Math.Abs(relativeX - defaultOffset.X) < snapDistance &&
                    Math.Abs(relativeY - defaultOffset.Y) < snapDistance)
                {
                    currentOffset = defaultOffset;
                }
                else
                {
                    // Otherwise, save this new position as the permanent offset
                    currentOffset = new Point(relativeX, relativeY);
                }

                // 4. Force one update to snap the window visually
                this.Location = new Point(rect.Left + currentOffset.X, rect.Top + currentOffset.Y);
            };

            this.Controls.Add(toggleBtn);

            // 1. Update Main Container styling
            mainContainer = new Panel
            {
                Location = new Point(7, 54),
                Size = new Size(472, 297), // THIS IS THE REAL SIZE OF THE CHAT WINDOW
                BackColor = Color.FromArgb(35, 45, 55), // Semi-transparent Dark Blue-Gray
                Padding = new Padding(10) // Give text breathing room
            };
            this.Controls.Add(mainContainer);

            // Apply rounded corners after the control is created
            mainContainer.HandleCreated += (s, e) => SetRoundedRegion(mainContainer, 20);

            // 2. Update Chat History (The top part)
            chatBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(35, 45, 55), // Match container
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Regular),
                TabStop = false
            };

            // 3. Update Input Bar (The bottom part)
            inputBox = new ChatInputBox
            {
                Dock = DockStyle.Bottom,
                Height = 45, // Slightly taller
                BackColor = Color.FromArgb(25, 25, 25), // Darker than history
                ForeColor = Color.White,
                Enabled = false,
                Margin = new Padding(5) // Space between history and input
            };

            mainContainer.Controls.Add(chatBox);
            mainContainer.Controls.Add(inputBox);

            // Ensure the input box also has slightly rounded corners
            inputBox.HandleCreated += (s, e) => SetRoundedRegion(inputBox, 10);

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