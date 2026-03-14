using System.Text.Json;

namespace RobloxChatLauncher.Models
{
    public class Message
    {
        public string Command { get; set; } = null!;
        public JsonElement Data
        {
            get; set;
        }
    }
}
