using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    /// <summary>Whether a new remote session may start for a shop group.</summary>
    public readonly record struct SessionGateDecision(bool Allowed, string? ErrorCode = null, string? Message = null);

    /// <summary>
    /// Caps concurrent live remote sessions per group. The native relay and the fake sign-in register
    /// <see cref="AllowAllShopSessionPolicy"/>, which never refuses. RemoteTicket replaces it when that provider is on.
    /// </summary>
    public interface IShopSessionPolicy
    {
        Task<SessionGateDecision> CheckCreateSessionAsync(string group, int activeInGroupCount, CancellationToken ct = default);
    }

    /// <summary>No shop registry: every group may open as many sessions as the process allows.</summary>
    public sealed class AllowAllShopSessionPolicy : IShopSessionPolicy
    {
        public static readonly AllowAllShopSessionPolicy Instance = new();

        public Task<SessionGateDecision> CheckCreateSessionAsync(string group, int activeInGroupCount, CancellationToken ct = default) =>
            Task.FromResult(new SessionGateDecision(true));
    }

    /// <summary>
    /// Live sessions in a group: the registration completed and the session has not been cancelled.
    /// Shop Identity and <see cref="RemoteWebViewService"/> share this count.
    /// </summary>
    public static class ShopSessionActivity
    {
        public static int Count(ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> sessions, string? group)
        {
            if (sessions == null || string.IsNullOrEmpty(group))
                return 0;

            var count = 0;
            foreach (var pending in sessions.Values)
            {
                if (!pending.Task.IsCompletedSuccessfully)
                    continue;
                var state = pending.Task.Result;
                if (!string.Equals(state.Group, group, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsLive(state))
                    count++;
            }
            return count;
        }

        private static bool IsLive(ServiceState state)
        {
            try
            {
                return !state.Token.IsCancellationRequested;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }
}
