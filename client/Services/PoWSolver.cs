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
                Console.WriteLine($"[PoW] Starting solver. Seed: {seed}, Difficulty: {difficulty}");
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
                            Console.WriteLine($"[PoW] Solution found in {nonce:N0} attempts.");
                            Console.WriteLine($"[PoW] Time elapsed: {sw.ElapsedMilliseconds}ms.");
                            Console.WriteLine($"[PoW] Final nonce:  {nonce}");
                            Console.WriteLine($"[PoW] Result hash:  {hashString}");
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