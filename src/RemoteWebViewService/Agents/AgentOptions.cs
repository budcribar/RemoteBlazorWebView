using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PeakSWC.RemoteWebView.Agents
{
    public sealed class AgentOptions
    {
        public const string EnrollmentKeysVariable = "AGENT_ENROLLMENT_KEYS";
        public const string PublicBaseUrlVariable = "PUBLIC_BASE_URL";

        // Empty means any enrollment key is accepted.
        public IReadOnlyList<string> EnrollmentKeys { get; init; } = [];

        // Null means "build it from the request's scheme and host".
        public string? PublicBaseUrl { get; init; }

        public bool AcceptsAnyKey => EnrollmentKeys.Count == 0;

        public static AgentOptions FromConfiguration(IConfiguration configuration) =>
            Create(configuration[EnrollmentKeysVariable], configuration[PublicBaseUrlVariable]);

        public static AgentOptions Create(string? enrollmentKeys, string? publicBaseUrl) => new()
        {
            EnrollmentKeys = (enrollmentKeys ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            PublicBaseUrl = string.IsNullOrWhiteSpace(publicBaseUrl) ? null : publicBaseUrl.Trim().TrimEnd('/'),
        };

        public bool IsKeyAccepted(string? key)
        {
            if (AcceptsAnyKey)
                return true;
            if (string.IsNullOrEmpty(key))
                return false;

            var candidate = Encoding.UTF8.GetBytes(key);
            bool match = false;
            foreach (var allowed in EnrollmentKeys)
                match |= CryptographicOperations.FixedTimeEquals(candidate, Encoding.UTF8.GetBytes(allowed));
            return match;
        }
    }
}
