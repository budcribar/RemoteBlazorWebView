using System;
using Microsoft.Extensions.FileProviders;
using System.Security.Cryptography;
using System.IO;

namespace PeakSWC.RemoteWebView
{
    public static class ETagGenerator
    {
        public static string GenerateETag(IFileInfo fileInfo)
        {
            using var stream = fileInfo.CreateReadStream();
            using var hasher = SHA256.Create();
            byte[] hash = hasher.ComputeHash(stream);
            stream.Seek(0, SeekOrigin.Begin);
            return Convert.ToBase64String(hash);
        }
    }
}
