namespace FileSyncServer.Tests;

public static class LargeFileSetup
{
    public static void EnsureLargeFileExists(string filePath, long sizeInBytes)
    {
        if (!File.Exists(filePath))
        {
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            fs.SetLength(sizeInBytes);
        }
    }
}
