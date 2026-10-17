using System.Net;
using System.Net.Sockets;

namespace VFilter.Core.Proxy;

/// <summary>
/// Matches an address against list entries holding an address or a CIDR range in canonical form
/// (shared contract 0.3.2, as the website stores them).
/// </summary>
public static class IpMatcher
{
    public static bool Contains(string entry, IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(address);

        address = Normalize(address);
        var slash = entry.IndexOf('/', StringComparison.Ordinal);

        if (slash < 0)
        {
            return IPAddress.TryParse(entry, out var single) && Normalize(single).Equals(address);
        }

        if (!IPAddress.TryParse(entry.AsSpan(0, slash), out var network)
            || !int.TryParse(entry.AsSpan(slash + 1), out var prefix))
        {
            return false;
        }

        network = Normalize(network);

        if (network.AddressFamily != address.AddressFamily)
        {
            return false;
        }

        var networkBytes = network.GetAddressBytes();
        var addressBytes = address.GetAddressBytes();

        if (prefix < 0 || prefix > networkBytes.Length * 8)
        {
            return false;
        }

        for (var i = 0; i < networkBytes.Length; i++)
        {
            var bits = Math.Clamp(prefix - (i * 8), 0, 8);
            var mask = (byte)(0xFF << (8 - bits));

            if ((networkBytes[i] & mask) != (addressBytes[i] & mask))
            {
                return false;
            }
        }

        return true;
    }

    public static bool AnyContains(IEnumerable<string> entries, IPAddress address) =>
        entries.Any(entry => Contains(entry, address));

    private static IPAddress Normalize(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
