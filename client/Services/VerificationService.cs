using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using RobloxChatLauncher.Core;

namespace RobloxChatLauncher.Services
{
    public enum VerificationResult
    {
        Success,
        CodeNotFound,
        HardwareIdFailed,
        ServerError
    }

    public class VerificationService
    {
        // Helper class to handle deserialization
        private class VerificationResponse
        {
            [JsonPropertyName("code")]
            public string Code
            {
                get; set;
            }

            [JsonPropertyName("robloxId")]
            public long RobloxId
            {
                get; set;
            }
        }

        private class LoginResponse
        {
            [JsonPropertyName("robloxId")]
            public long RobloxId
            {
                get; set;
            }
        }

        public static string GetMachineId()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
            {
                var value = key?.GetValue("MachineGuid")?.ToString();
        
                if (string.IsNullOrWhiteSpace(value))
                    throw new Exception("Machine GUID not available");
        
                return value;
            }
        }

        public async Task<(string Code, long RobloxId)> StartVerification(string username)
        {
            var payload = new
            {
                robloxUsername = username
            };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await ChatForm.Client.PostAsync($"https://{Constants.BASE_URL}/api/v1/verify/generate", content);
            var json = await response.Content.ReadAsStringAsync();

            // Deserialize into our helper class
            var result = JsonSerializer.Deserialize<VerificationResponse>(json);
            
            // Map the class properties to the ValueTuple requested by the method signature
            return (result?.Code ?? string.Empty, result?.RobloxId ?? 0);
        }

        public async Task<VerificationResult> ConfirmVerification(long robloxId)
        {
            string hwid;
        
            try
            {
                hwid = GetMachineId();
            }
            catch
            {
                return VerificationResult.HardwareIdFailed;
            }
        
            var payload = new
            {
                robloxId,
                hwid
            };
        
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        
            try
            {
                var response = await ChatForm.Client.PostAsync(
                    $"https://{Constants.BASE_URL}/api/v1/verify/confirm",
                    content
                );
        
                if (response.IsSuccessStatusCode)
                {
                    Properties.Settings1.Default.RobloxUserId = robloxId;
                    Properties.Settings1.Default.IsVerified = true;
                    Properties.Settings1.Default.Save();
        
                    return VerificationResult.Success;
                }
        
                if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    return VerificationResult.CodeNotFound;
                }
        
                return VerificationResult.ServerError;
            }
            catch
            {
                return VerificationResult.ServerError;
            }
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
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                // 1. Tell the server to delete the link
                var response = await ChatForm.Client.PostAsync($"https://{Constants.BASE_URL}/api/v1/verify/unverify", content);

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

        public async Task<bool> Login()
        {
            try
            {
                string hwid = GetMachineId();
                var payload = new
                {
                    hwid
                };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await ChatForm.Client.PostAsync($"https://{Constants.BASE_URL}/api/v1/verify/login", content);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<LoginResponse>(json);

                    Properties.Settings1.Default.RobloxUserId = result?.RobloxId ?? 0;
                    Properties.Settings1.Default.IsVerified = true;
                    Properties.Settings1.Default.Save();
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// Simply wipes local verification status without telling the server to delete the database entry.
        /// </summary>
        public void Logout()
        {
            Properties.Settings1.Default.IsVerified = false;
            Properties.Settings1.Default.Save();
        }
    }
}
