using RobloxChatLauncher.Utils;

namespace RobloxChatLauncher.UI
{
    // --------------------------------------------------
    // Class for round hide/unhide button
    // --------------------------------------------------
    using System.ComponentModel;
    using System.Drawing;
    using System.Windows.Forms;

    public class RoundButton : Control
    {
        public event EventHandler Clicked;
        public event EventHandler<Point> Dragged; // Notify form of movement
        public event EventHandler DragEnded;

        private Image imgOn;
        private Image imgOff;
        private System.Windows.Forms.Timer holdTimer;
        private System.Windows.Forms.Timer visualTimer; // Timer for smooth animation
        private bool isDragging = false;
        private Point lastMousePos;
        private float holdProgress = 0f; // 0 to 1.0
        private DateTime holdStartTime;
        private const int HOLD_THRESHOLD = 300; // Milliseconds to hold before release (for dragging)

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsActive { get; set; } = true;

        public RoundButton()
        {
            this.DoubleBuffered = true; // Prevents flickering during animation
            this.SetStyle(ControlStyles.Selectable, false);
            InitializeRobloxIcons();

            // Timer for the logic
            holdTimer = new System.Windows.Forms.Timer { Interval = HOLD_THRESHOLD };
            holdTimer.Tick += (s, e) =>
            {
                holdTimer.Stop();
                visualTimer.Stop();
                holdProgress = 0;
                isDragging = true;
                this.Cursor = Cursors.SizeAll;
                this.Invalidate();
            };

            // Timer for the visual progress
            visualTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60 FPS
            visualTimer.Tick += (s, e) =>
            {
                var elapsed = (DateTime.Now - holdStartTime).TotalMilliseconds;
                holdProgress = (float)Math.Min(elapsed / HOLD_THRESHOLD, 1.0);
                this.Invalidate(); // Redraw the progress bar
            };

            this.SizeChanged += (s, e) => UpdateRegion();
        }

        private void UpdateRegion()
        {
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                path.AddEllipse(0, 0, Width, Height);
                this.Region = new Region(path);
            }
        }

        // Mouse event overrides for dragging behavior
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (isDragging)
            {
                // Calculate the movement delta
                int deltaX = e.X - lastMousePos.X;
                int deltaY = e.Y - lastMousePos.Y;

                Dragged?.Invoke(this, new Point(deltaX, deltaY));
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                lastMousePos = e.Location;
                holdStartTime = DateTime.Now;
                holdProgress = 0;
                holdTimer.Start();
                visualTimer.Start();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            holdTimer.Stop();
            visualTimer.Stop();

            // If we released before the drag threshold, it's a click
            if (!isDragging && holdProgress < 1.0 && holdProgress > 0)
            {
                Clicked?.Invoke(this, EventArgs.Empty);
            }

            holdProgress = 0;
            isDragging = false;
            this.Cursor = Cursors.Hand;
            DragEnded?.Invoke(this, EventArgs.Empty);
            this.Invalidate();
            base.OnMouseUp(e);
        }

        private void InitializeRobloxIcons()
        {
            // Load only the icons we need
            var icons = RobloxIconLoader.LoadIcons(
                "ui/TopBar/chatOn.png",
                "ui/TopBar/chatOff.png"
            );

            if (icons.TryGetValue("ui/TopBar/chatOn.png", out var loadedOn))
                imgOn = loadedOn;

            if (icons.TryGetValue("ui/TopBar/chatOff.png", out var loadedOff))
                imgOff = loadedOff;

            // imgOn/imgOff are either loaded or remain null if not found
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
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

            // Draw the Progress Ring
            if (holdProgress > 0 && holdProgress < 1.0)
            {
                float penWidth = 4f;
                // Deflate rectangle so the stroke isn't cut off by the Region clip
                RectangleF rect = new RectangleF(penWidth / 2, penWidth / 2,
                                               Width - penWidth, Height - penWidth);

                using (Pen progressPen = new Pen(Color.DeepSkyBlue, penWidth))
                {
                    float sweepAngle = holdProgress * 360f;
                    // -90 degrees starts the arc at the 12 o'clock position
                    e.Graphics.DrawArc(progressPen, rect, -90, sweepAngle);
                }
            }
        }

        protected override void OnClick(EventArgs e)
        {
            Clicked?.Invoke(this, e);
            base.OnClick(e);
        }
    }
}