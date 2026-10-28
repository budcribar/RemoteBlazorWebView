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
            if (fileInfo == null || !fileInfo.Exists || fileInfo.IsDirectory)
                return string.Empty;

            using var stream = fileInfo.CreateReadStream();
            if (stream == null)
                return string.Empty;
            using var hasher = SHA256.Create();
            byte[] hash = hasher.ComputeHash(stream);
            if (stream.CanSeek)
            {
                stream.Seek(0, SeekOrigin.Begin);
            }
            return Convert.ToBase64String(hash);
        }
    }
}
