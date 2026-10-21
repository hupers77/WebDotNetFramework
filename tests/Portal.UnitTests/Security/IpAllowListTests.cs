using System.Net;
using Portal.Security.Network;

namespace Portal.UnitTests.Security;

public sealed class IpAllowListTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void 목록없음_모두허용(string? list)
    {
        var allow = IpAllowList.Parse(list);

        Assert.True(allow.IsUnrestricted);
        Assert.True(allow.Allows(IPAddress.Parse("8.8.8.8")));
    }

    [Theory]
    [InlineData("10.1.2.3", true)]
    [InlineData("10.1.255.1", true)]
    [InlineData("10.2.0.1", false)]
    [InlineData("203.0.113.7", true)]
    [InlineData("203.0.113.8", false)]
    [InlineData("::ffff:10.1.0.9", true)] // IPv6 형식으로 온 IPv4
    [InlineData("2001:db8::1", true)]
    public void CIDR와단일IP(string address, bool expected)
    {
        var allow = IpAllowList.Parse("10.1.0.0/16, 203.0.113.7 ; 2001:db8::/32");

        Assert.Equal(expected, allow.Allows(IPAddress.Parse(address)));
    }

    [Fact]
    public void 제한이있는데주소를모르면_차단() =>
        Assert.False(IpAllowList.Parse("10.0.0.0/8").Allows(null));

    [Theory]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0")]
    [InlineData("10.0.0.0/8, abc")]
    [InlineData("10.1/16")]
    public void 형식오류_예외(string list) =>
        Assert.Throws<FormatException>(() => IpAllowList.Parse(list));
}
