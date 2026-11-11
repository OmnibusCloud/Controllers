using System.Net;
using System.Net.Sockets;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// Tells a public internet address from one that belongs to a machine's own surroundings:
/// the loopback, private and link-local ranges, carrier-grade NAT, documentation and
/// benchmark blocks, multicast, and the IPv6 forms that carry an IPv4 address inside. A
/// public origin never lives at such an address, so a name that resolves to one is not
/// contacted.
/// </summary>
public static class ShardAddress
{
    #region Functions

    /// <summary>Whether an address is a public internet address.</summary>
    /// <param name="address">The address.</param>
    /// <returns>True for a public address.</returns>
    public static bool IsPublic(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return IsPublicV4(address.GetAddressBytes());

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return false;

        if (address.IsIPv4MappedToIPv6)
            return IsPublicV4(address.MapToIPv4().GetAddressBytes());

        return IsPublicV6(address.GetAddressBytes());
    }

    #endregion

    #region Tools

    private static bool IsPublicV4(byte[] bytes)
    {
        var a = bytes[0];
        var b = bytes[1];
        var c = bytes[2];

        if (a == 0 || a == 10 || a == 127)
            return false; // "this network", private, loopback

        if (a == 100 && b >= 64 && b <= 127)
            return false; // carrier-grade NAT

        if (a == 169 && b == 254)
            return false; // link-local, cloud metadata

        if (a == 172 && b >= 16 && b <= 31)
            return false; // private

        if (a == 192 && b == 168)
            return false; // private

        if (a == 192 && b == 0 && (c == 0 || c == 2))
            return false; // protocol assignments, documentation

        if (a == 198 && (b == 18 || b == 19))
            return false; // benchmarking

        if (a == 198 && b == 51 && c == 100)
            return false; // documentation

        if (a == 203 && b == 0 && c == 113)
            return false; // documentation

        return a < 224; // multicast and reserved above
    }

    private static bool IsPublicV6(byte[] bytes)
    {
        // Unspecified (::), loopback (::1) and the retired IPv4-compatible form (::a.b.c.d).
        if (bytes.Take(12).All(value => value == 0))
            return false;

        if ((bytes[0] & 0xFE) == 0xFC)
            return false; // unique local fc00::/7

        if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
            return false; // link-local fe80::/10

        if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0xC0)
            return false; // site-local fec0::/10 (deprecated)

        if (bytes[0] == 0xFF)
            return false; // multicast

        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8)
            return false; // documentation 2001:db8::/32

        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x00)
            return false; // Teredo 2001::/32: the real peer is hidden inside

        // 6to4 (2002::/16) and NAT64 (64:ff9b::/96) carry an IPv4 address: judge that one.
        if (bytes[0] == 0x20 && bytes[1] == 0x02)
            return IsPublicV4([bytes[2], bytes[3], bytes[4], bytes[5]]);

        if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B && bytes.Skip(4).Take(8).All(value => value == 0))
            return IsPublicV4([bytes[12], bytes[13], bytes[14], bytes[15]]);

        return true;
    }

    #endregion
}
