// tools\MakeSecret.cs -- build-time helper, NOT part of the shipped app.
//
// Seals a plaintext AI key with the same passphrase Secret.cs uses, so the result can be
// pasted into Secret.Blob.  Usage (from the repo's Windows project folder):
//
//   $csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
//   & $csc /nologo /target:exe /out:"$env:TEMP\mksecret.exe" `
//       /r:System.dll /r:System.Security.dll /r:System.Web.Extensions.dll `
//       (Get-ChildItem "src\*.cs" | ForEach-Object FullName) "tools\MakeSecret.cs"
//   & "$env:TEMP\mksecret.exe" "the-plaintext-key"
//
// It prints the base64 blob.  Keep the plaintext key out of git.
using System;

namespace AnkiAssistant
{
    public static class MakeSecret
    {
        public static void Main(string[] args)
        {
            if (args.Length == 0 || args[0].Trim().Length == 0)
            {
                Console.WriteLine("usage: mksecret.exe <plaintext-key>");
                Environment.Exit(2);
            }
            string key = args[0].Trim();
            string blob = Secret.SealForBuild(key);
            Console.WriteLine("plain length = " + key.Length);
            Console.WriteLine("blob length  = " + blob.Length);
            if (!Secret.BlobDecrypts(blob))
            {
                Console.WriteLine("ROUND TRIP FAILED");
                Environment.Exit(1);
            }
            Console.WriteLine("round trip   = OK");
            Console.WriteLine("BLOB = " + blob);
        }
    }
}
