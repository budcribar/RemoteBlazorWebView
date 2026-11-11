using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using System;

namespace PeakSWC.RemoteWebView
{
    public static class StaticMethods
    {
        public static TBuilder ConditionallyRequireAuthorization<TBuilder>(this TBuilder builder) where TBuilder : class, IEndpointConventionBuilder
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
#if AUTHORIZATION
            return builder.RequireAuthorization(SessionAccessRequirement.PolicyName);
#else
            return builder;
#endif

        }
    }
}
