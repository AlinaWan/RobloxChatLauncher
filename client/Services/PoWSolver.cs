using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace RobloxChatLauncher.Services
{
    public class PoWSolver
    {
        public static async Task<long> SolveAsync(string seed, int difficulty)
        {
            return await Task.Run(() =>
            {
                long nonce = 0;
                string target = new string('0', difficulty);

#if DEBUG
                Stopwatch sw = Stopwatch.StartNew();
                Debug.WriteLine($"[PoW] Starting solver. Seed: {seed}, Difficulty: {difficulty}");
#endif

                using (SHA256 sha256 = SHA256.Create())
                {
                    while (true)
                    {
                        // seed + nonce
                        string input = seed + nonce.ToString();
                        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                        byte[] hashBytes = sha256.ComputeHash(inputBytes);

                        // Convert to hex string
                        string hashString = Convert.ToHexString(hashBytes).ToLower();

                        if (hashString.StartsWith(target))
                        {
#if DEBUG
                            sw.Stop();
                            Debug.WriteLine($"[PoW] Solution found in {nonce:N0} attempts.");
                            Debug.WriteLine($"[PoW] Time elapsed: {sw.ElapsedMilliseconds}ms.");
                            Debug.WriteLine($"[PoW] Final nonce:  {nonce}");
                            Debug.WriteLine($"[PoW] Result hash:  {hashString}");
#endif
                            return nonce;
                        }

                        nonce++;
                    }
                }
            });
        }
    }
}