using System.Net;
using System.Net.Sockets;

namespace Portal.Framework.Web.Api.Outbound;

/// <summary>
/// 송신 호출이 연결해도 되는 주소 (SSRF 방지). 루프백·링크로컬(클라우드 메타데이터 169.254.x 포함)·미지정·멀티캐스트는 언제나 막고,
/// 사설 대역(10/8, 172.16/12, 192.168/16, 100.64/10, fc00::/7)은 대상에 사내망 허용이 켜져 있을 때만 연다.
/// 이름이 아니라 실제 연결할 주소로 검사하므로 DNS가 내부 주소를 돌려주는 경우도 막힌다.
/// IPv4를 품은 IPv6 주소(IPv4 대응 ::ffff:, NAT64 64:ff9b::/96, 6to4 2002::/16)는 안의 IPv4로 다시 검사하고,
/// 시험·벤치마크용 대역(192.0.0/24, 198.18/15)도 사설로 본다.
/// </summary>
public static class OutboundAddressPolicy
{
    /// <summary>주소에 연결해도 되는지.</summary>
    public static bool IsAllowed(IPAddress address, bool allowPrivate)
    {
        address = EmbeddedIPv4(address) ?? address;

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.Broadcast) || address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            if (b[0] == 0 || b[0] >= 224 || (b[0] == 169 && b[1] == 254))
            {
                return false;
            }

            var isPrivate = b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] is >= 64 and <= 127)
                || (b[0] == 192 && b[1] == 0 && b[2] == 0) || (b[0] == 198 && b[1] is 18 or 19);
            return allowPrivate || !isPrivate;
        }

        // IPv6 고유 로컬(fc00::/7)은 사설로 본다
        return allowPrivate || (address.GetAddressBytes()[0] & 0xFE) != 0xFC;
    }

    /// <summary>IPv6 주소 안에 담긴 IPv4 (::ffff:a.b.c.d, 64:ff9b::a.b.c.d, 2002:aabb:ccdd::). 없으면 null.</summary>
    private static IPAddress? EmbeddedIPv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return null;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4();
        }

        var b = address.GetAddressBytes();
        // NAT64 잘 알려진 접두사 64:ff9b::/96
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b.AsSpan(4, 8).IndexOfAnyExcept((byte)0) < 0)
        {
            return new IPAddress(b.AsSpan(12, 4));
        }

        // 6to4 2002:WWXX:YYZZ::/48
        return b[0] == 0x20 && b[1] == 0x02 ? new IPAddress(b.AsSpan(2, 4)) : null;
    }
}
