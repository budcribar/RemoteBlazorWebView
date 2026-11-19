using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PeakSWC.RemoteWebView.Agents
{
    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalApi.AgentRegistry, UrlFormat = ExperimentalApi.UrlFormat)]
    public sealed class AgentOptions
    {
        public const string EnrollmentKeysVariable = "AGENT_ENROLLMENT_KEYS";
        public const string PublicBaseUrlVariable = "PUBLIC_BASE_URL";
        public const string AdminKeyVariable = "AGENT_ADMIN_KEY";
        public const string AdminKeyHeader = "x-admin-key";

        // Empty means any enrollment key is accepted, unless RequireKeys is set: then no agent is accepted.
        public IReadOnlyList<string> EnrollmentKeys { get; init; } = [];

        // True outside the Development environment: an empty EnrollmentKeys list refuses every agent instead of accepting any key.
        public bool RequireKeys { get; init; }

        // Null means "build it from the request's scheme and host".
        public string? PublicBaseUrl { get; init; }

        // Null means no admin key: StartSession always asks for consent.
        public string? AdminKey { get; init; }

        public bool AcceptsAnyKey => EnrollmentKeys.Count == 0 && !RequireKeys;

        public bool RefusesEveryKey => EnrollmentKeys.Count == 0 && RequireKeys;

        public bool AdminKeyConfigured => AdminKey != null;

        /// <summary>
        /// Reads the agent settings. Outside the Development environment the enrollment keys are required:
        /// with <see cref="EnrollmentKeysVariable"/> unset, the relay refuses every agent rather than accepting any key.
        /// </summary>
        public static AgentOptions FromConfiguration(IConfiguration configuration) =>
            Create(configuration[EnrollmentKeysVariable], configuration[PublicBaseUrlVariable], configuration[AdminKeyVariable],
                requireKeys: !IsDevelopment(configuration));

        // The host copies ASPNETCORE_ENVIRONMENT / DOTNET_ENVIRONMENT into the "environment" key; unset means Production.
        private static bool IsDevelopment(IConfiguration configuration) =>
            string.Equals(configuration["environment"], "Development", StringComparison.OrdinalIgnoreCase);

        public static AgentOptions Create(string? enrollmentKeys, string? publicBaseUrl, string? adminKey = null, bool requireKeys = false) => new()
        {
            RequireKeys = requireKeys,
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
            if (EnrollmentKeys.Count == 0)
                return !RequireKeys;
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
