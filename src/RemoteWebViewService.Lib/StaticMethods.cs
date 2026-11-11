using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using System;

namespace PeakSWC.RemoteWebView
{
    public static class StaticMethods
    {
        /// <summary>A session page: with sign-in on, it needs the session policy (<see cref="SessionAccessHandler"/>).</summary>
        public static TBuilder RequireSessionAccess<TBuilder>(this TBuilder builder, IRelayAuthorization authorization) where TBuilder : class, IEndpointConventionBuilder
        {
            ArgumentNullException.ThrowIfNull(builder);
            return authorization.Enabled ? builder.RequireAuthorization(SessionAccessRequirement.PolicyName) : builder;
        }
    }
}
