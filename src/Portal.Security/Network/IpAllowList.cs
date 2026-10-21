using System.Net;
using System.Net.Sockets;

namespace Portal.Security.Network;

/// <summary>
/// 협력사별 접속 허용 IP 대역 (<c>TB_COMPANY.ALLOW_IP_LIST</c>, 쉼표로 구분한 CIDR 또는 단일 IP).
/// 비어 있으면 제한하지 않는다. 형식이 잘못된 항목은 무시하지 않고 예외로 알린다(잘못 입력해 전부 허용되는 일을 막는다).
/// </summary>
public sealed class IpAllowList
{
    private readonly List<IPNetwork> networks;

    private IpAllowList(List<IPNetwork> networks) => this.networks = networks;

    /// <summary>제한 없음.</summary>
    public bool IsUnrestricted => networks.Count == 0;

    /// <summary>목록 문자열을 해석한다.</summary>
    /// <exception cref="FormatException">CIDR 형식이 아닌 항목이 있을 때.</exception>
    public static IpAllowList Parse(string? list)
    {
        var result = new List<IPNetwork>();
        if (string.IsNullOrWhiteSpace(list))
        {
            return new IpAllowList(result);
        }

        foreach (var raw in list.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IsStrictAddress(raw.Split('/')[0]))
            {
                throw new FormatException($"허용 IP 목록 형식 오류: '{raw}'");
            }

            if (IPNetwork.TryParse(raw, out var network))
            {
                result.Add(network);
            }
            else if (IPAddress.TryParse(raw, out var single))
            {
                result.Add(new IPNetwork(single, single.AddressFamily == AddressFamily.InterNetwork ? 32 : 128));
            }
            else
            {
                throw new FormatException($"허용 IP 목록 형식 오류: '{raw}'");
            }
        }

        return new IpAllowList(result);
    }

    // IPAddress.Parse는 "10.0.0"을 10.0.0.0으로 받아 주므로, IPv4는 점 4칸 형식만 허용한다
    private static bool IsStrictAddress(string address) =>
        address.Contains(':', StringComparison.Ordinal) || address.Split('.').Length == 4;

    /// <summary>주소가 허용 대역 안에 있는지. IPv4가 IPv6 형식(::ffff:a.b.c.d)으로 와도 같은 주소로 본다.</summary>
    public bool Allows(IPAddress? address)
    {
        if (IsUnrestricted)
        {
            return true;
        }

        if (address is null)
        {
            return false;
        }

        var candidates = address.IsIPv4MappedToIPv6 ? new[] { address, address.MapToIPv4() } : [address];
        return networks.Exists(n => Array.Exists(candidates, n.Contains));
    }
}
