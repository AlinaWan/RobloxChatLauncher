namespace RobloxChatLauncher.UI
{
    // --------------------------------------------------
    // Smooth Panel and TextBox (for reducing flicker)
    // --------------------------------------------------
    using System.Windows.Forms;

    public class SmoothPanel : Panel
    {
        public SmoothPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | // Don't add ControlStyles.UserPaint here or it will render garbage pixels when resizing the window
                          ControlStyles.OptimizedDoubleBuffer, true);
        }
    }
    public class SmoothTextBox : TextBox
    {
        public SmoothTextBox()
        {
            this.DoubleBuffered = true;
            // This stops the background from "flashing" dark before the text draws
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
    }
}