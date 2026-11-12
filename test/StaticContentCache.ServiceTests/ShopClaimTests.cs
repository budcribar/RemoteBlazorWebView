using System.Security.Claims;
using PeakSWC.RemoteWebView;

namespace StaticContentCache.Tests;

public class ShopClaimTests
{
    [Fact]
    public void GetUserObjectId_uses_oid_then_name_identifier_then_sub()
    {
        Assert.Equal("user-1", Principal(new Claim(ClaimTypes.NameIdentifier, "user-1")).GetUserObjectId());
        Assert.Equal("user-2", Principal(new Claim("sub", "user-2")).GetUserObjectId());
        Assert.Equal("user-3", Principal(new Claim("oid", "user-3"), new Claim("sub", "other")).GetUserObjectId());
        Assert.Equal("directory", Principal(
            new Claim("oid", "directory"),
            new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", "legacy")).GetUserObjectId());
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims));
}
