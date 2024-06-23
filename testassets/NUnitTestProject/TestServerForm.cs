using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenQA.Selenium;
using System.Threading;
using System.Diagnostics;
using System;
using System.Threading.Tasks;
using PeakSWC.RemoteWebView;
using Google.Protobuf.WellKnownTypes;
using System.Linq;
using System.Threading.Channels;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using System.Net.Http;

namespace WebdriverTestProject
{

    [TestClass]
    public class TestServerForm : TestRemoteBlazorForm
    {
        private ClientIPC.ClientIPCClient? client;

        public override void Test2Client5Refresh() { }

        protected override async Task TestClient(int num)
        {
            await Startup(num);

            Stopwatch sw = new();
            sw.Start();

            Assert.AreEqual(num, _driver.Count, $"Was not able to create expected {num} _drivers");

            // Verify we can hang out before attaching browser
            if (num == 10)
                Thread.Sleep(TimeSpan.FromMinutes(2));

            for (int i = 0; i < num; i++) _driver[i].Url = url + $"app/{ids[i]}";
            Console.WriteLine($"Navigate home in {sw.Elapsed}");

            Thread.Sleep(3000);
            sw.Restart();

            for (int i = 0; i < num; i++)
            {
                for (int j = 0; j < NUM_LOOPS_WAITING_FOR_PAGE_LOAD; j++)
                {
                    try
                    {
                        var link = _driver[i].FindElement(By.PartialLinkText("Counter"));
                        break;
                    }
                    catch (Exception) { }
                    Thread.Sleep(100);
                }
            }
            var httpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWebText, new HttpClientHandler());

            var channel = GrpcChannel.ForAddress(grpcUrl, new GrpcChannelOptions { HttpHandler = httpHandler });
            client = new ClientIPC.ClientIPCClient(channel);
            var response = await client!.GetServerStatusAsync(new Empty { });
            var totalReadTime = response.ConnectionResponses.Sum(x => x.TotalReadTime);
            var maxFileReadTime = response.ConnectionResponses.Sum(x => x.MaxFileReadTime);
            var totalFilesRead = response.ConnectionResponses.Sum(x => x.TotalFilesRead);
            var totalBytesRead = response.ConnectionResponses.Sum(x => x.TotalBytesRead);

            Assert.AreEqual(24 * num, totalFilesRead, "Failed on total files read");  
            Assert.AreEqual(1251016 * num, totalBytesRead, "Failed on total bytes read"); // This will vary depending on the size of the Javascript  
            Console.WriteLine($"TotalBytesRead {totalBytesRead}");
            Console.WriteLine($"TotalReadTime {totalReadTime}");


            // Verify Server entries are cleared when browser is disconnected
            for (int i = 0; i < num; i++)
            {
                _driver[i].Dispose();

                for (int j = 0; j < 100; j++)
                {
                    Thread.Sleep(100);
                    response = await client!.GetServerStatusAsync(new Empty { });
                    Assert.IsTrue(j < 90, "Server did not shutdown via browser shutdown");
                    if (response.ConnectionResponses.Count == num - (i + 1))
                        break;
                }

                for (int j = 0; j < 100; j++)
                {
                    Thread.Sleep(100);

                    Assert.IsTrue(j < 90, "Client did not shutdown via browser shutdown");

                    if (CountClients() == num - (i + 1))
                        break;
                }
            }



        }

        [TestMethod]
        public async Task Test10Client()
        {
            await TestClient(10);
        }
    }
}