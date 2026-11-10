using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FileSyncServer.Tests.Remote
{
    /// <summary>
    /// A local port that passes connections on to another one, so a test can cut a browser's connections the way a
    /// network failure does (the browser's own "offline" switch leaves a stream that is already open running).
    /// </summary>
    public sealed class TcpForwarder : IDisposable
    {
        private readonly int _targetPort;
        private readonly object _gate = new();
        private readonly List<TcpClient> _open = [];
        private TcpListener? _listener;
        private CancellationTokenSource? _stop;

        public int Port { get; }

        public TcpForwarder(int targetPort)
        {
            _targetPort = targetPort;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Connect();
        }

        /// <summary>Accept connections (again).</summary>
        public void Connect()
        {
            lock (_gate)
            {
                if (_listener != null) return;
                _stop = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Loopback, Port);
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Start();
                _ = AcceptAsync(_listener, _stop.Token);
            }
        }

        /// <summary>Close every connection and refuse new ones until <see cref="Connect"/>.</summary>
        public void Disconnect()
        {
            lock (_gate)
            {
                _stop?.Cancel();
                _listener?.Stop();
                _listener = null;
                foreach (var client in _open)
                {
                    try { client.Close(); } catch { /* already closed */ }
                }
                _open.Clear();
            }
        }

        private async Task AcceptAsync(TcpListener listener, CancellationToken stop)
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    var browser = await listener.AcceptTcpClientAsync(stop).ConfigureAwait(false);
                    _ = ForwardAsync(browser, stop);
                }
            }
            catch (Exception) when (stop.IsCancellationRequested || listener.Server.SafeHandle.IsClosed)
            {
                // Disconnect() or Dispose()
            }
        }

        private async Task ForwardAsync(TcpClient browser, CancellationToken stop)
        {
            var target = new TcpClient { NoDelay = true };
            browser.NoDelay = true;
            lock (_gate)
            {
                if (stop.IsCancellationRequested) { browser.Close(); target.Close(); return; }
                _open.Add(browser);
                _open.Add(target);
            }

            try
            {
                await target.ConnectAsync(IPAddress.Loopback, _targetPort, stop).ConfigureAwait(false);
                var up = browser.GetStream().CopyToAsync(target.GetStream(), stop);
                var down = target.GetStream().CopyToAsync(browser.GetStream(), stop);
                await Task.WhenAny(up, down).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // either side went away
            }
            finally
            {
                try { browser.Close(); } catch { }
                try { target.Close(); } catch { }
                lock (_gate) { _open.Remove(browser); _open.Remove(target); }
            }
        }

        public void Dispose() => Disconnect();
    }
}
