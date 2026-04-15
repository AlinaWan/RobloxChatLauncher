using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Buffers.Text;

namespace RobloxChatLauncher.Services
{
    public class PoWSolver
    {
        public static async Task<long> SolveAsync(string seed, int difficulty)
        {
            return await Task.Run(() =>
            {
                long nonce = 0;
                byte[] seedBytes = Encoding.UTF8.GetBytes(seed);

#if DEBUG
                Stopwatch sw = Stopwatch.StartNew();
                Console.WriteLine($"[PoW] Starting solver. Seed: {seed}, Difficulty: {difficulty}");
#endif

                using (SHA256 sha256 = SHA256.Create())
                {
                    // Pre-allocate buffer: seed length + space for a long (up to 20 digits)
                    byte[] buffer = new byte[seedBytes.Length + 20];
                    Buffer.BlockCopy(seedBytes, 0, buffer, 0, seedBytes.Length);

                    // Span to represent the part of the buffer where the nonce goes
                    Span<byte> nonceSpan = buffer.AsSpan(seedBytes.Length);

                    while (true)
                    {
                        // Write nonce directly to buffer as UTF8
                        if (!Utf8Formatter.TryFormat(nonce, nonceSpan, out int bytesWritten))
                        {
                            nonce++;
                            continue;
                        }

                        // Compute hash using the exact size needed
                        byte[] hash = sha256.ComputeHash(buffer, 0, seedBytes.Length + bytesWritten);

                        if (IsMatch(hash, difficulty))
                        {
#if DEBUG
                            sw.Stop();
                            Console.WriteLine($"[PoW] Solution found in {nonce:N0} attempts.");
                            Console.WriteLine($"[PoW] Time elapsed: {sw.ElapsedMilliseconds}ms.");
                            Console.WriteLine($"[PoW] Final nonce:  {nonce}");
                            Console.WriteLine($"[PoW] Result hash:  {BitConverter.ToString(hash).Replace("-", "").ToLower()}");
#endif
                            return nonce;
                        }

                        nonce++;
                    }
                }
            });
        }
        private static bool IsMatch(byte[] hash, int difficulty)
        {
            // Check all full bytes (each byte is 2 hex chars)
            int fullBytesToCheck = difficulty / 2;
            for (int i = 0; i < fullBytesToCheck; i++)
            {
                if (hash[i] != 0)
                    return false;
            }

            // If difficulty is odd, check the next half-byte
            if (difficulty % 2 != 0)
            {
                // The first 4 bits of the next byte must be 0
                // (e.g., the byte must be less than 0x10)
                if (hash[fullBytesToCheck] >= 0x10)
                    return false;
            }

            return true;
        }
    }
}