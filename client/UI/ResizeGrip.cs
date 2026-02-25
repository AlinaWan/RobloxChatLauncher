namespace RobloxChatLauncher.UI
{
    // --------------------------------------------------
    // Class for resize grip control
    // --------------------------------------------------
    using System.Drawing;
    using System.Windows.Forms;

    public class ResizeGrip : Control
    {
        private Point lastMousePos;
        private bool isResizing = false;
        public event EventHandler<Size> ResizeDragged;

        public ResizeGrip()
        {
            this.SetStyle(ControlStyles.SupportsTransparentBackColor |
                          ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.OptimizedDoubleBuffer, true);
            this.BackColor = Color.Transparent;
            this.Size = new Size(20, 20);
            this.Cursor = Cursors.SizeNWSE;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isResizing = true;
                lastMousePos = PointToScreen(e.Location);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (isResizing)
            {
                Point currentMousePos = PointToScreen(e.Location);
                int deltaX = currentMousePos.X - lastMousePos.X;
                int deltaY = currentMousePos.Y - lastMousePos.Y;

                ResizeDragged?.Invoke(this, new Size(deltaX, deltaY));
                lastMousePos = currentMousePos;
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            isResizing = false;
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(Color.FromArgb(150, 255, 255, 255), 2))
            {
                // Draw the ⌟ symbol
                // Vertical line
                e.Graphics.DrawLine(pen, Width - 5, Height - 12, Width - 5, Height - 5);
                // Horizontal line
                e.Graphics.DrawLine(pen, Width - 12, Height - 5, Width - 5, Height - 5);
            }
        }
    }
}