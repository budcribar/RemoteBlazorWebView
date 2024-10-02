using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;

namespace FileSyncServer
{
    public static class Utility
    {
        public static async Task<bool> GetServerCache()
        {
            var client = Client();
            var status = await (await client.GetAsync("/cache")).Content.ReadAsStringAsync();
            return ParseCacheStatus(status).UseServerCache;
        }

        public static async Task SetClientCache(bool isEnabled)
        {
            var client = Client();
            var enabled = isEnabled ? "enable" : "disable";
            var res = await (await client.GetAsync($"/cache/client/{enabled}")).Content.ReadAsStringAsync();
        }

        public static async Task<bool> GetClientCache()
        {
            var client = Client();
            var status = await (await client.GetAsync("/cache")).Content.ReadAsStringAsync();
            return ParseCacheStatus(status).UseClientCache;
        }

        public static async Task SetServerCache(bool isEnabled)
        {
            var client = Client();
            var enabled = isEnabled ? "enable" : "disable";
            var res = await (await client.GetAsync($"/cache/server/{enabled}")).Content.ReadAsStringAsync();
        }

        public static string BASE_URL = "https://localhost:5001";

        public static HttpClient Client(HttpMessageHandler? handler = null)
        {
            // Instantiate HttpClient with the provided handler or default handler
            var client = handler != null ? new HttpClient(handler) : new HttpClient();

            // Set the base address
            client.BaseAddress = new Uri(BASE_URL);

            // Set the timeout
            client.Timeout = TimeSpan.FromMinutes(5); // Adjust as necessary

            // Set the default HTTP version
            client.DefaultRequestVersion = HttpVersion.Version11;

            return client;
        }

        public static (bool UseServerCache, bool UseClientCache) ParseCacheStatus(string input)
            {
                if (string.IsNullOrWhiteSpace(input))
                    throw new ArgumentException("Input string cannot be null or empty.");

                // Split the input by comma to separate key-value pairs
                string[] pairs = input.Split(',');

                if (pairs.Length != 2)
                    throw new ArgumentException("Input string must contain exactly two key-value pairs separated by a comma.");

                bool useServerCache = false;
                bool useClientCache = false;
                bool serverCacheSet = false;
                bool clientCacheSet = false;

                foreach (var pair in pairs)
                {
                    // Split each pair by colon
                    string[] keyValue = pair.Split(':');

                    if (keyValue.Length != 2)
                        throw new ArgumentException($"Invalid key-value pair format: '{pair}'. Expected 'Key: Value'.");

                    // Trim whitespaces
                    string key = keyValue[0].Trim();
                    string value = keyValue[1].Trim();

                    // Parse the value to boolean
                    if (!bool.TryParse(value, out bool boolValue))
                        throw new FormatException($"Value for '{key}' is not a valid boolean: '{value}'.");

                    // Assign to the correct variable based on the key
                    switch (key)
                    {
                        case "UseServerCache":
                            useServerCache = boolValue;
                            serverCacheSet = true;
                            break;

                        case "UseClientCache":
                            useClientCache = boolValue;
                            clientCacheSet = true;
                            break;

                        default:
                            throw new ArgumentException($"Unexpected key: '{key}'. Expected 'UseServerCache' or 'UseClientCache'.");
                    }
                }

                if (!serverCacheSet || !clientCacheSet)
                    throw new ArgumentException("Both 'UseServerCache' and 'UseClientCache' must be specified.");

                return (useServerCache, useClientCache);
            }
      
        public static void KillExistingProcesses(string processName)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    Console.WriteLine($"Killing process: {process.ProcessName} (ID: {process.Id})");
                    process.Kill();
                    process.WaitForExit(); // Optionally wait for the process to exit
                }
            }
            catch (Exception ex)
            {

                Console.WriteLine($"Error killing process: {ex.Message}");
            }
        }

        /// <summary>
        /// Modifies the Read and Delete permissions for a specified user on a given file.
        /// </summary>
        /// <param name="filePath">The path to the file.</param>
        /// <param name="user">The user account (e.g., "DOMAIN\\Username").</param>
        /// <param name="grantRead">True to grant Read and Delete permissions; False to remove them.</param>
        /// <param name="disableInheritance">Optional. True to disable inheritance on the file; False to leave it as is.</param>
        public static void ModifyFilePermissions(string filePath, string user, bool grantRead, bool disableInheritance = true)
        {
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
    }
}

