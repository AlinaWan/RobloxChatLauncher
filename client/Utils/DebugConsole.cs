namespace RobloxChatLauncher.Utils
{
    public static class DebugConsole
    {
        public static bool Enabled = false;

        public static void WriteLine(string text)
        {
            if (!Enabled)
                return;

            try
            {
                Console.WriteLine(text);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to write to console: " + ex.Message);
            }
        }
    }
}