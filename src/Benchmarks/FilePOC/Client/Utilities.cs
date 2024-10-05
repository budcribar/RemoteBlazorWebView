using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http.Headers;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

public static class Utilities
{
    public static void ModifyFilePermissions(string filePath, bool grantRead, bool disableInheritance = true)
    {
        string user = WindowsIdentity.GetCurrent().Name;
        // Validate input parameters
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

        if (string.IsNullOrWhiteSpace(user))
            throw new ArgumentException("User cannot be null or empty.", nameof(user));

        FileInfo fileInfo = new FileInfo(filePath);

        if (!fileInfo.Exists)
            throw new FileNotFoundException("The specified file does not exist.", filePath);

        try
        {
            // Get the current ACL (Access Control List) of the file
            FileSecurity fileSecurity = fileInfo.GetAccessControl();

            if (grantRead)
            {
                // Define the access rule to grant Read and Delete permissions
                FileSystemAccessRule allowReadDeleteRule = new FileSystemAccessRule(
                    user,
                    FileSystemRights.Read | FileSystemRights.Delete,
                    InheritanceFlags.None,
                    PropagationFlags.NoPropagateInherit,
                    AccessControlType.Allow);

                // Check if the rule already exists to prevent duplicates
                bool ruleExists = false;
                foreach (FileSystemAccessRule rule in fileSecurity.GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount)))
                {
                    if (rule.IdentityReference.Value.Equals(user, StringComparison.OrdinalIgnoreCase) &&
                        rule.FileSystemRights.HasFlag(FileSystemRights.Read) &&
                        rule.FileSystemRights.HasFlag(FileSystemRights.Delete) &&
                        rule.AccessControlType == AccessControlType.Allow)
                    {
                        ruleExists = true;
                        break;
                    }
                }

                if (!ruleExists)
                {
                    // Add the access rule since it doesn't exist
                    fileSecurity.AddAccessRule(allowReadDeleteRule);
                    Console.WriteLine($"Granted Read and Delete permissions to {user}.");
                }
                else
                {
                    Console.WriteLine($"Read and Delete permissions for {user} are already granted.");
                }
            }
            else
            {
                // Define the access rule to remove Read and Delete permissions
                FileSystemAccessRule allowReadDeleteRule = new FileSystemAccessRule(
                    user,
                    FileSystemRights.Read | FileSystemRights.Delete,
                    InheritanceFlags.None,
                    PropagationFlags.NoPropagateInherit,
                    AccessControlType.Allow);

                // Remove all matching Allow Read and Delete rules for the user
                fileSecurity.RemoveAccessRuleAll(allowReadDeleteRule);

            }

            // Optionally, handle inheritance
            if (disableInheritance)
            {
                bool isInheritanceEnabled = !fileSecurity.AreAccessRulesProtected;

                if (isInheritanceEnabled)
                {
                    // Disable inheritance and remove inherited rules
                    fileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                    Console.WriteLine("Inheritance disabled and inherited rules removed.");
                }
                else
                {
                    Console.WriteLine("Inheritance is already disabled.");
                }
            }

            // Apply the updated ACL to the file
            fileInfo.SetAccessControl(fileSecurity);
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"Access denied: {ex.Message}");
            // Handle according to your application's requirements
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while modifying file permissions: {ex.Message}");
            // Handle according to your application's requirements
            throw;
        }
    }
    private static byte[] CreateSimplePngIcon(int width, int height, Color color)
    {
        using (Bitmap bmp = new Bitmap(width, height))
        using (Graphics gfx = Graphics.FromImage(bmp))
        using (SolidBrush brush = new SolidBrush(color))
        {
            gfx.FillRectangle(brush, 0, 0, width, height);
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
    }
    public static string CreateTestEnvironment(string directoryPath, string title = "Test Page")
    {
        // Ensure the directory exists
        Directory.CreateDirectory(directoryPath);

        // Create CSS file
        string cssContent = @"
body { font-family: Arial, sans-serif; margin: 0; padding: 20px; }
h1 { color: #333; }
.test-div { border: 1px solid #ddd; padding: 10px; margin-top: 20px; }";
        File.WriteAllText(Path.Combine(directoryPath, "styles.css"), cssContent);

        // Create JavaScript file
        string jsContent = @"
function showMessage() {
    alert('Hello from JavaScript!');
}
function loadJsonData() {
    fetch('data.json')
        .then(response => response.json())
        .then(data => {
            document.getElementById('jsonData').textContent = JSON.stringify(data);
        });
}
function runArrayVerification() {
    verifyArrayIntegrity();
}";
        File.WriteAllText(Path.Combine(directoryPath, "script.js"), jsContent);

        // Create JSON file
        string jsonContent = @"{""message"": ""This is a test JSON file"", ""number"": 42}";
        File.WriteAllText(Path.Combine(directoryPath, "data.json"), jsonContent);

        // Create XML file
        string xmlContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<root>
    <message>This is a test XML file</message>
    <number>42</number>
</root>";
        File.WriteAllText(Path.Combine(directoryPath, "data.xml"), xmlContent);

        // Create SVG file
        string svgContent = @"<svg xmlns=""http://www.w3.org/2000/svg"" width=""100"" height=""100"">
    <circle cx=""50"" cy=""50"" r=""40"" stroke=""black"" stroke-width=""3"" fill=""red"" />
</svg>";
        File.WriteAllText(Path.Combine(directoryPath, "image.svg"), svgContent);

        // Create a simple text file
        File.WriteAllText(Path.Combine(directoryPath, "sample.txt"), "This is a sample text file.");

        // Create a web manifest file
        string manifestContent = @"{
    ""name"": ""Test Web App"",
    ""short_name"": ""TestApp"",
    ""start_url"": ""/"",
    ""display"": ""standalone"",
    ""background_color"": ""#ffffff"",
    ""theme_color"": ""#000000"",
    ""icons"": [{
        ""src"": ""icon.png"",
        ""sizes"": ""192x192"",
        ""type"": ""image/png""
    }]
}";
        File.WriteAllText(Path.Combine(directoryPath, "manifest.webmanifest"), manifestContent);

        // Create a 192x192 PNG image (simple colored square)
        byte[] pngData = CreateSimplePngIcon(192, 192, Color.Blue);
        File.WriteAllBytes(Path.Combine(directoryPath, "icon.png"), pngData);

        // Create large JavaScript file with array
        var random = new Random();
        var largeArray = new int[1000000]; // 1 million integers
        for (int i = 0; i < largeArray.Length; i++)
        {
            largeArray[i] = random.Next(1000); // Limit to 0-999 for easier sum calculation
        }
        var arrayHash = CalculateSimpleHash(largeArray);

        string largeJsContent = $@"
const largeArray = [{string.Join(",", largeArray)}];

function verifyArrayIntegrity() {{
    const calculatedHash = calculateSimpleHash(largeArray);
    const expectedHash = {arrayHash}; // Note: No quotes, as this is a number
    
    if (calculatedHash === expectedHash) {{
        console.log('Array integrity verified successfully');
        document.getElementById('arrayVerificationResult').textContent = 'Array integrity verified successfully';
    }} else {{
        console.error('Array integrity check failed');
        document.getElementById('arrayVerificationResult').textContent = 'Array integrity check failed';
    }}
}}

function calculateSimpleHash(arr) {{
    return arr.reduce((sum, num) => sum + num, 0);
}}";
        File.WriteAllText(Path.Combine(directoryPath, "largeArray.js"), largeJsContent);

        // --- Begin Enhancements ---

        // Create files with special characters in filenames
        string specialCharFileName = "spécial_fïle_名.txt";
        File.WriteAllText(Path.Combine(directoryPath, specialCharFileName), "File with special characters in the filename.");

        // Create a file with different encoding (e.g., UTF-16)
        string utf16Content = "This is a UTF-16 encoded file.";
        File.WriteAllText(Path.Combine(directoryPath, "utf16.txt"), utf16Content, Encoding.Unicode);

        // Create a large file to test streaming and buffering
        string largeTextFilePath = Path.Combine(directoryPath, "largeFile.txt");
        using (var writer = new StreamWriter(largeTextFilePath))
        {
            for (int i = 0; i < 1000000; i++)
            {
                writer.WriteLine($"This is line {i}");
            }
        }

        // Create files with uncommon MIME types
        string uncommonMimeTypeContent = "<test>This is a test file with an uncommon MIME type.</test>";
        File.WriteAllText(Path.Combine(directoryPath, "file.uncommon"), uncommonMimeTypeContent);

        // Create a binary file (e.g., a simple binary blob)
        byte[] binaryData = new byte[256];
        new Random().NextBytes(binaryData);
        File.WriteAllBytes(Path.Combine(directoryPath, "binary.bin"), binaryData);

        // Create a hidden file
        //string hiddenFilePath = Path.Combine(directoryPath, ".hiddenfile");
        //File.WriteAllText(hiddenFilePath, "This is a hidden file.");
        //File.SetAttributes(hiddenFilePath, FileAttributes.Hidden);

        // Create a file with a very long filename
        //string longFileName = new string('a', 255) + ".txt";
        //File.WriteAllText(Path.Combine(directoryPath, longFileName), "File with a very long filename.");

        // Create nested directories
        string nestedDirPath = Path.Combine(directoryPath, "nested", "deeply", "nested", "directory");
        Directory.CreateDirectory(nestedDirPath);
        File.WriteAllText(Path.Combine(nestedDirPath, "nestedfile.txt"), "This is a file in a nested directory.");

        // Create a file with no extension
        File.WriteAllText(Path.Combine(directoryPath, "noextension"), "This file has no extension.");

        // --- End Enhancements ---

        // Create HTML file
        string htmlContent = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{title}</title>
    <base href=""/"" />
    <link rel=""stylesheet"" href=""styles.css"">
    <script src=""script.js""></script>
    <script src=""largeArray.js""></script>
    <link rel=""manifest"" href=""manifest.webmanifest"">
    <link rel=""icon"" href=""icon.png"" type=""image/png"">
</head>
<body onload=""loadJsonData()"">
    <h1>Welcome to the {title}</h1>
    <p>This is a test page created at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>
    <div class=""test-div"">
        <h2>Test Components</h2>
        <button onclick=""showMessage()"">Click me!</button>
        <p>JSON Data: <span id=""jsonData""></span></p>
        <img src=""/image.svg"" alt=""Test SVG"" width=""50"" height=""50"">
        <iframe src=""sample.txt"" width=""300"" height=""50""></iframe>
    </div>
    <div class=""test-div"">
        <h2>Large Array Verification</h2>
        <button onclick=""runArrayVerification()"">Verify Large Array</button>
        <p>Verification Result: <span id=""arrayVerificationResult""></span></p>
    </div>
    <div class=""test-div"">
        <h2>Additional Resources</h2>
        <ul>
            <li><a href=""data.xml"">XML File</a></li>
            <li><a href=""data.json"">JSON File</a></li>
            <li><a href=""styles.css"">CSS File</a></li>
            <li><a href=""script.js"">JavaScript File</a></li>
            <li><a href=""largeArray.js"">Large Array JavaScript File</a></li>
            <li><a href=""{specialCharFileName}"">File with Special Characters</a></li>
            <li><a href=""utf16.txt"">UTF-16 Encoded File</a></li>
            <li><a href=""largeFile.txt"">Large Text File</a></li>
            <li><a href=""file.uncommon"">File with Uncommon MIME Type</a></li>
            <li><a href=""binary.bin"">Binary File</a></li>
          
            <li><a href=""nested/deeply/nested/directory/nestedfile.txt"">Nested File</a></li>
            <li><a href=""noextension"">File with No Extension</a></li>
        </ul>
    </div>
</body>
</html>";

        string filePath = Path.Combine(directoryPath, "index.html");
        File.WriteAllText(filePath, htmlContent, Encoding.UTF8);
        return filePath;
    }


    /// <summary>
    /// Waits until the server's health endpoint returns a successful response or until a timeout occurs.
    /// </summary>
    /// <param name="healthCheckUrl">The URL of the server's health check endpoint.</param>
    /// <param name="httpHandler">The HTTP handler configured to bypass SSL certificate validation.</param>
    /// <param name="timeoutSeconds">Maximum time to wait for the server to become healthy.</param>
    /// <param name="retryIntervalSeconds">Time interval between health check attempts.</param>
    /// <returns>True if the server is healthy within the timeout; otherwise, false.</returns>
    public static async Task<bool> WaitForServerHealthAsync(string healthCheckUrl, int timeoutSeconds = 60, int retryIntervalSeconds = 1)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var delay = TimeSpan.FromSeconds(retryIntervalSeconds);
        var startTime = DateTime.UtcNow;

        Console.WriteLine("Checking server health...");

        while (DateTime.UtcNow - startTime < timeout)
        {
            try
            {
                var response = await httpClient.GetAsync(healthCheckUrl);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("Server is healthy.");
                    return true;
                }
                else
                {
                    Console.WriteLine($"Health check failed with status code: {response.StatusCode}. Retrying in {delay.Seconds} seconds...");
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Health check request failed: {ex.Message}. Retrying in {delay.Seconds} seconds...");
            }

            await Task.Delay(delay);
        }

        Console.WriteLine("Server health check timed out.");
        return false;
    }

    /// <summary>
    /// Creates three test .txt files in a temporary directory and returns their full paths.
    /// </summary>
    /// <returns>List of file paths to the created test .txt files.</returns>
    public static List<string> CreateTestFiles(string tempDirectory)
    {
        try
        {
            // Ensure the temporary directory exists
            if (!Directory.Exists(tempDirectory))
            {
                Directory.CreateDirectory(tempDirectory);
                Console.WriteLine($"Created temporary directory: {tempDirectory}");
            }
            else
            {
                Console.WriteLine($"Temporary directory already exists: {tempDirectory}");
            }

            var filesToSync = new List<string>();
            for (int i = 1; i <= 3; i++)
            {
                var fileName = $"test{i}.txt";
                var filePath = Path.Combine(tempDirectory, fileName);

                // Create the file with sample content if it doesn't exist
                if (!File.Exists(filePath))
                {
                    string sampleContent = $"This is test{i}.txt created on {DateTime.Now}.";
                    File.WriteAllText(filePath, sampleContent);
                    Console.WriteLine($"Created test file: {filePath}");
                  
                }
                else
                {
                    Console.WriteLine($"Test file already exists: {filePath}");
                }
                //if (i == 3)
                //    ModifyFilePermissions(filePath, false);

                filesToSync.Add(filePath);
            }

            return filesToSync;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating test files: {ex.Message}");
            throw; // Rethrow to handle it in the calling method
        }
    }

    static long CalculateSimpleHash(int[] array)
    {
        long sum = 0;
        foreach (int num in array)
        {
            sum += num;
        }
        return sum;
    }
}