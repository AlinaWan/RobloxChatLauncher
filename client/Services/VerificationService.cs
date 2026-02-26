using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace RobloxChatLauncher.Services
{
    public class VerificationService
    {
        private static readonly HttpClient client = new HttpClient();

        // Helper class to handle deserialization
        private class VerificationResponse
        {
            [JsonProperty("code")]
            public string Code
            {
                get; set;
            }

            [JsonProperty("robloxId")]
            public long RobloxId
            {
                get; set;
            }
        }

        public static string GetMachineId()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
            {
                return key?.GetValue("MachineGuid")?.ToString() ?? "unknown-hwid";
            }
        }

        public async Task<(string Code, long RobloxId)> StartVerification(string username)
        {
            var payload = new
            {
                robloxUsername = username
            };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"https://{Constants.Constants.BASE_URL}/api/v1/verify/generate", content);
            var json = await response.Content.ReadAsStringAsync();

            // Deserialize into our helper class
            var result = JsonConvert.DeserializeObject<VerificationResponse>(json);

            // Map the class properties to the ValueTuple requested by the method signature
            return (result.Code, result.RobloxId);
        }

        public async Task<bool> ConfirmVerification(long robloxId)
        {
            var payload = new
            {
                robloxId,
                hwid = GetMachineId()
            };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"https://{Constants.Constants.BASE_URL}/api/v1/verify/confirm", content);

            if (response.IsSuccessStatusCode)
            {
                // Save to local Settings managed by Windows LocalAppData
                Properties.Settings1.Default.RobloxUserId = robloxId;
                Properties.Settings1.Default.IsVerified = true;
                Properties.Settings1.Default.Save();
                return true;
            }
            return false;
        }

        public async Task<bool> Unverify()
        {
            try
            {
                string hwid = GetMachineId();
                var payload = new
                {
                    hwid
                };
                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                // 1. Tell the server to delete the link
                var response = await client.PostAsync($"https://{Constants.Constants.BASE_URL}/api/v1/verify/unverify", content);

                // 2. Clear local settings regardless of server response
                Properties.Settings1.Default.RobloxUserId = 0;
                Properties.Settings1.Default.IsVerified = false;
                Properties.Settings1.Default.Save();

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}