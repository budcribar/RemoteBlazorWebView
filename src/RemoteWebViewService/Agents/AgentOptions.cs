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
        public const string AdminKeyVariable = "AGENT_ADMIN_KEY";
        public const string AdminKeyHeader = "x-admin-key";

        // Empty means any enrollment key is accepted.
        public IReadOnlyList<string> EnrollmentKeys { get; init; } = [];

        // Null means "build it from the request's scheme and host".
        public string? PublicBaseUrl { get; init; }

        // Null means no admin key: StartSession always asks for consent.
        public string? AdminKey { get; init; }

        public bool AcceptsAnyKey => EnrollmentKeys.Count == 0;

        public bool AdminKeyConfigured => AdminKey != null;

        public static AgentOptions FromConfiguration(IConfiguration configuration) =>
            Create(configuration[EnrollmentKeysVariable], configuration[PublicBaseUrlVariable], configuration[AdminKeyVariable]);

        public static AgentOptions Create(string? enrollmentKeys, string? publicBaseUrl, string? adminKey = null) => new()
        {
            EnrollmentKeys = (enrollmentKeys ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            PublicBaseUrl = string.IsNullOrWhiteSpace(publicBaseUrl) ? null : publicBaseUrl.Trim().TrimEnd('/'),
            AdminKey = string.IsNullOrWhiteSpace(adminKey) ? null : adminKey.Trim(),
        };

        /// <summary>True when an admin key is configured and <paramref name="key"/> equals it (constant-time).</summary>
        public bool IsAdminKeyValid(string? key) =>
            AdminKey != null && key != null &&
            // Hashing first makes the comparison independent of the key length too.
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(key)), SHA256.HashData(Encoding.UTF8.GetBytes(AdminKey)));

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
