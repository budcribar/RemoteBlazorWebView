// FileSyncServer.Tests/FileSyncServiceImplTests.cs
using FluentAssertions;
using System.Net;
using System.Security.Principal;

namespace FileSyncServer.Tests
{
    [Collection("Server caching collection")]
    public class ServerCachingTests : IDisposable
    {
        private readonly ServerFixture _serverFixture;
        private readonly ClientFixture _clientFixture;
        private readonly HttpClient _client = Utility.Client();
        private readonly string _testRootDirectory = Path.Combine(Directory.GetCurrentDirectory(), "client_cache");
        private readonly string _clientId = string.Empty;
        private readonly string _fileName = "testfile.txt";
        private readonly string _fileContent = "This is a test file.";
        private readonly string _filePath = string.Empty;
        // Determine the current user
        private readonly string _currentUser = WindowsIdentity.GetCurrent().Name;
        public ServerCachingTests(ServerFixture serverFixture, ClientFixture clientFixture)
        {
            _serverFixture = serverFixture;
            _clientFixture = clientFixture;
            _clientId = clientFixture.ClientId.ToString();

            // Determine the path to the client executable
            var testDirectory = Directory.GetCurrentDirectory();

            // Ensure test files exist in the client's cache directory
            var clientCachePath = Path.Combine(Directory.GetCurrentDirectory(), "client_cache");
            Directory.CreateDirectory(clientCachePath);
          
            // Create a test file with read permissions
            _filePath = Path.Combine(clientCachePath, _fileName);

            if (File.Exists(_filePath))
            {
                Utility.ModifyFilePermissions(_filePath, _currentUser, true);
                File.Delete(_filePath);
            }
               

            File.WriteAllText(_filePath, _fileContent);

            // Grant read access to the current user
           
            Utility.ModifyFilePermissions(_filePath, _currentUser, true);  
        }

        [Fact]
        public async Task Server_Cache_Enabled_Should_Serve_File_After_Permission_Revoked()
        {
            await Utility.SetServerCache(true);
            (await Utility.GetServerCache()).Should().BeTrue();
           
            Utility.ModifyFilePermissions(_filePath, _currentUser, true);

            // Act
            // First request: should succeed and cache the file
            var response1 = await _client.GetAsync($"/{_clientId}/{_fileName}");
            response1.StatusCode.Should().Be(HttpStatusCode.OK);
            var content1 = await response1.Content.ReadAsStringAsync();
            content1.Should().Be(_fileContent);

            // Revoke read access
            Utility.ModifyFilePermissions(_filePath, _currentUser,false);

            // Second request: should still succeed due to caching
            var response2 = await _client.GetAsync($"/{_clientId}/{_fileName}");
            response2.StatusCode.Should().Be(HttpStatusCode.OK);
            var content2 = await response2.Content.ReadAsStringAsync();
            content2.Should().Be(_fileContent);

            Utility.ModifyFilePermissions(_filePath, _currentUser, true);
            await Utility.SetServerCache(false);
        }

        [Fact]
        public async Task Server_Cache_Disabled_Should_Fail_After_Permission_Revoked()
        {
            await Utility.SetServerCache(false);
            (await Utility.GetServerCache()).Should().BeFalse();
           
            Utility.ModifyFilePermissions(_filePath, _currentUser, true);
            // Act
            // First request: should succeed
            var response1 = await _client.GetAsync($"/{_clientId}/{_fileName}");
            response1.StatusCode.Should().Be(HttpStatusCode.OK);
            var content1 = await response1.Content.ReadAsStringAsync();
            content1.Should().Be(_fileContent);

            // Revoke read access
            Utility.ModifyFilePermissions(_filePath, _currentUser, false);
           
            // Second request: should fail due to revoked permissions
            var response2 = await _client.GetAsync($"/{_clientId}/{_fileName}");
            response2.StatusCode.Should().Be(HttpStatusCode.Forbidden); // Or 404 Not Found based on implementation

            Utility.ModifyFilePermissions(_filePath, _currentUser, true);
        }

        public void Dispose()
        {
            // Cleanup: Delete the temporary test directory
            if (Directory.Exists(_testRootDirectory))
            {
                try
                {
                    Directory.Delete(_testRootDirectory, recursive: true);
                }
                catch (Exception)
                {
                    // Log the exception or handle accordingly
                }
            }
        }
    }
}
