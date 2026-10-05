using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AnkiAssistant
{
    /// <summary>
    /// 内置的 AI Key（密文形式），对应安卓版的 Secret.java。
    ///
    /// 目标：**新设备装完就能用**，不用再手输 Key。
    ///
    /// 做法：
    ///   * exe 里只有密文（Blob），没有明文；明文只在运行时于内存里出现一小会儿
    ///   * 解密口令不写成完整字符串，而是拆成 4 段、分散在不同方法里，
    ///     其中一段反序存放 —— 直接扫 exe 拿不到口令
    ///   * 口令 + salt 走 PBKDF2（12000 轮）派生 32 字节，前 16 字节做 AES-128-CBC 密钥，
    ///     后 16 字节做 HMAC-SHA256 密钥（先加密后 MAC，改一个字节都验不过）
    ///
    /// 边界说清楚：**只要密钥随程序分发，就一定能被逆向出来**，加密只是把"随手提取"
    /// 提高到"需要真正逆向"。真正的防线是设置里手填的 Key 优先于内置 Key。
    ///
    /// 重新生成密文：`java tools/MakeSecret.java "新Key"`（或 Windows 侧 tools\MakeSecret.cs）。
    /// 约定：本文件不要提交口令的完整串。
    /// </summary>
    public static class Secret
    {
        // 由 tools\MakeSecret.cs 生成的密文（格式：base64(salt[16] + iv[16] + ct + mac[32])）
        private const string Blob =
            "y4hNWtf5pi8ehT8R6lyvMcFP0dJg8mhRaLhAHhyyyJP+bDEmBfW+C8A9xvHUQqbyjrBOFh9P9uU63XvCwhWZ1JDKFbugrgkY7NVtqfFDWGG6UX5r5Sy7WyjXGoy0pYNg12ec4cD3v/tIjtHOGv42h5hrtfd51XGfL2ZnFqkXfak=";

        private const int Iter = 12000;

        private static string cached;

        /// <summary>内置 Key；没有内置或解不出来就返回空串（设置里手填照样能用）。</summary>
        public static string BuiltinKey(string provider)
        {
            if (AiClient.PZhipu.Equals(provider)) return Zhipu();
            return "";
        }

        /// <summary>实际使用的 Key：设置里手填的优先，其次内置。provider 为空时按默认预设算。</summary>
        public static string Resolve(string provider, string userKey)
        {
            if (userKey != null && userKey.Trim().Length > 0) return userKey.Trim();
            if (provider == null || provider.Length == 0) provider = AiClient.DefaultPreset;
            return BuiltinKey(provider);
        }

        /// <summary>内置 Key 是否可用于该预设（设置页用它显示"已内置"提示）。</summary>
        public static bool HasBuiltin(string provider)
        {
            return BuiltinKey(provider).Length > 0;
        }

        public static string Zhipu()
        {
            if (cached != null) return cached;
            cached = Unseal(AssemblePass());
            return cached;
        }

        // ------------------------------------------------------------ 口令拼装

        private static string AssemblePass()
        {
            StringBuilder sb = new StringBuilder(40);
            sb.Append("Lm4kQp8w");        // 1/4
            sb.Append(Part1());           // 2/4（反序存放）
            sb.Append("vUeZ2nRcXh");      // 3/4
            sb.Append(Part2());           // 4/4（字符数组形式）
            return sb.ToString();
        }

        private static string Part1()
        {
            string r = "A9tYb7Kd3sWg1m";
            char[] a = r.ToCharArray();
            Array.Reverse(a);
            return new string(a);
        }

        private static string Part2()
        {
            char[] c = { 'F', 'q', '6', 'H', 'p', 'T', '2', 'z' };
            return new string(c);
        }

        // ------------------------------------------------------------ 解密

        private static string Unseal(string pass)
        {
            return UnsealBlob(Blob, pass);
        }

        private static string UnsealBlob(string blob, string pass)
        {
            try
            {
                byte[] all = Convert.FromBase64String(blob);
                if (all.Length < 16 + 16 + 32 + 1) return "";

                byte[] salt = new byte[16];
                byte[] iv = new byte[16];
                Buffer.BlockCopy(all, 0, salt, 0, 16);
                Buffer.BlockCopy(all, 16, iv, 0, 16);

                int ctLen = all.Length - 16 - 16 - 32;
                byte[] ct = new byte[ctLen];
                byte[] mac = new byte[32];
                Buffer.BlockCopy(all, 32, ct, 0, ctLen);
                Buffer.BlockCopy(all, 32 + ctLen, mac, 0, 32);

                byte[] keys = Derive(pass, salt, 32);
                byte[] aesKey = new byte[16];
                byte[] macKey = new byte[16];
                Buffer.BlockCopy(keys, 0, aesKey, 0, 16);
                Buffer.BlockCopy(keys, 16, macKey, 0, 16);

                // 先验 MAC 再解密：防篡改，也避免把坏数据喂给 Cipher
                using (HMACSHA256 h = new HMACSHA256(macKey))
                {
                    byte[] head = new byte[16 + ctLen];
                    Buffer.BlockCopy(iv, 0, head, 0, 16);
                    Buffer.BlockCopy(ct, 0, head, 16, ctLen);
                    if (!FixedEquals(h.ComputeHash(head), mac)) return "";
                }

                using (Aes aes = Aes.Create())
                {
                    aes.KeySize = 128;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = aesKey;
                    aes.IV = iv;
                    using (ICryptoTransform dec = aes.CreateDecryptor())
                    {
                        byte[] plain = dec.TransformFinalBlock(ct, 0, ct.Length);
                        return Encoding.UTF8.GetString(plain);
                    }
                }
            }
            catch (Exception)
            {
                // 解不出来不是致命问题：设置里手填 Key 照样能用
                return "";
            }
        }

        private static byte[] Derive(string pass, byte[] salt, int n)
        {
            // .NET Framework 4.x 的 Rfc2898DeriveBytes 默认 HMAC-SHA1，12000 轮
            using (Rfc2898DeriveBytes k = new Rfc2898DeriveBytes(pass, salt, Iter))
            {
                return k.GetBytes(n);
            }
        }

        private static bool FixedEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        // ------------------------------------------------------------ 生成（工具用）

        /// <summary>把明文 Key 封成 Blob（tools\MakeSecret.cs 调用；程序运行时不走这里）。</summary>
        public static string Seal(string key, string pass)        {
            byte[] salt = new byte[16];
            byte[] iv = new byte[16];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
                rng.GetBytes(iv);
            }

            byte[] keys = Derive(pass, salt, 32);
            byte[] aesKey = new byte[16];
            byte[] macKey = new byte[16];
            Buffer.BlockCopy(keys, 0, aesKey, 0, 16);
            Buffer.BlockCopy(keys, 16, macKey, 0, 16);

            byte[] ct;
            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = aesKey;
                aes.IV = iv;
                using (ICryptoTransform enc = aes.CreateEncryptor())
                {
                    byte[] p = Encoding.UTF8.GetBytes(key);
                    ct = enc.TransformFinalBlock(p, 0, p.Length);
                }
            }

            byte[] mac;
            using (HMACSHA256 h = new HMACSHA256(macKey))
            {
                byte[] head = new byte[16 + ct.Length];
                Buffer.BlockCopy(iv, 0, head, 0, 16);
                Buffer.BlockCopy(ct, 0, head, 16, ct.Length);
                mac = h.ComputeHash(head);
            }

            byte[] all = new byte[16 + 16 + ct.Length + 32];
            Buffer.BlockCopy(salt, 0, all, 0, 16);
            Buffer.BlockCopy(iv, 0, all, 16, 16);
            Buffer.BlockCopy(ct, 0, all, 32, ct.Length);
            Buffer.BlockCopy(mac, 0, all, 32 + ct.Length, 32);
            return Convert.ToBase64String(all);
        }

        /// <summary>自检用：用同一个口令把 Seal 的结果解开，确认往返一致。</summary>
        public static bool VerifyEnvelope()
        {
            string probe = "probe-key-" + Guid.NewGuid().ToString("N");
            string pass = AssemblePass();
            string blob = Seal(probe, pass);
            return probe.Equals(UnsealBlob(blob, pass));
        }

        /// <summary>构建工具用：用内置口令把明文封成可粘进 Blob 的字符串。</summary>
        public static string SealForBuild(string key)
        {
            return Seal(key, AssemblePass());
        }

        /// <summary>构建工具用：确认某段密文能用内置口令解开（不打印明文）。</summary>
        public static bool BlobDecrypts(string blob)
        {
            string k = UnsealBlob(blob, AssemblePass());
            return k.Length > 0;
        }
    }
}

