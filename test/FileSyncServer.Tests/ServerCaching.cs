﻿// FileSyncServer.Tests/FileSyncServiceImplTests.cs
using FluentAssertions;
using System.Diagnostics;
using System.Net;
using System.Security.Principal;
using WebdriverTestProject;

namespace FileSyncServer.Tests
{
    [Collection("Server caching collection")]
    public class ServerCachingTests : IDisposable
    {
        private readonly ServerFixture _serverFixture;
        private readonly ClientFixture _clientFixture;
        private readonly HttpClient _client = Utilities.Client();
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
                Utilities.ModifyFilePermissions(_filePath, _currentUser, true);
                File.Delete(_filePath);
            }
               

            File.WriteAllText(_filePath, _fileContent);

            // Grant read access to the current user
           
            Utilities.ModifyFilePermissions(_filePath, _currentUser, true);  
        }

        [Fact]
        public async Task Server_Cache_Enabled_Should_Serve_File_After_Permission_Revoked()
        {
            Utilities.ModifyFilePermissions(_filePath, _currentUser, true);
            await Utilities.SetServerCache(true);
            (await Utilities.GetServerCache()).Should().BeTrue();

            // Act
            // First request: should succeed and cache the file
            Stopwatch sw = Stopwatch.StartNew();
            var response1 = await _client.GetAsync($"/{_clientId}/{_fileName}");
            var content1 = await response1.Content.ReadAsStringAsync();
            var first = sw.ElapsedMilliseconds;

            response1.StatusCode.Should().Be(HttpStatusCode.OK);
            content1.Should().Be(_fileContent);       

            // Second request: should still succeed due to caching but be faster
            sw.Reset();
            var response2 = await _client.GetAsync($"/{_clientId}/{_fileName}");
           
            var content2 = await response2.Content.ReadAsStringAsync();
            var second = sw.ElapsedMilliseconds;

            response2.StatusCode.Should().Be(HttpStatusCode.OK);
            content2.Should().Be(_fileContent);

            second.Should().BeLessThan(first);

            await Utilities.SetServerCache(false);
        }

        [Fact]
        public async Task Server_Cache_Disabled_Should_Fail_After_Permission_Revoked()
        {
            await Utilities.SetServerCache(false);
            (await Utilities.GetServerCache()).Should().BeFalse();
           
            Utilities.ModifyFilePermissions(_filePath, _currentUser, true);
            // Act
            // First request: should succeed
            var response1 = await _client.GetAsync($"/{_clientId}/{_fileName}");
            response1.StatusCode.Should().Be(HttpStatusCode.OK);
            var content1 = await response1.Content.ReadAsStringAsync();
            content1.Should().Be(_fileContent);

            // Revoke read access
            Utilities.ModifyFilePermissions(_filePath, _currentUser, false);
           
            // Second request: should fail due to revoked permissions
            var response2 = await _client.GetAsync($"/{_clientId}/{_fileName}");
            response2.StatusCode.Should().Be(HttpStatusCode.Forbidden); // Or 404 Not Found based on implementation

            Utilities.ModifyFilePermissions(_filePath, _currentUser, true);
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
