public class LargeFileSetup
{
    public static void EnsureLargeFileExists(string filePath, long sizeInBytes)
    {
        if (!File.Exists(filePath))
        {
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            fs.SetLength(sizeInBytes);
            // Optionally, write random data
            // byte[] data = new byte[8192];
            // new Random().NextBytes(data);
            // for (long i = 0; i < sizeInBytes; i += data.Length)
            // {
            //     fs.Write(data, 0, (int)Math.Min(data.Length, sizeInBytes - i));
            // }
        }
    }
}
