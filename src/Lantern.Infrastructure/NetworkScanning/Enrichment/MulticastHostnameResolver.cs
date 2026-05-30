using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Lantern.Infrastructure.NetworkScanning.Enrichment;

internal static class MulticastHostnameResolver
{
    private static readonly IPEndPoint MdnsEndpoint = new(IPAddress.Parse("224.0.0.251"), 5353);
    private static readonly IPEndPoint LlmnrEndpoint = new(IPAddress.Parse("224.0.0.252"), 5355);

    public static Task<string?> ResolveMdnsAsync(IPAddress address, TimeSpan timeout, CancellationToken cancellationToken)
        => ResolveAsync(address, MdnsEndpoint, useUnicastResponseBit: true, timeout, cancellationToken);

    public static Task<string?> ResolveLlmnrAsync(IPAddress address, TimeSpan timeout, CancellationToken cancellationToken)
        => ResolveAsync(address, LlmnrEndpoint, useUnicastResponseBit: false, timeout, cancellationToken);

    private static async Task<string?> ResolveAsync(
        IPAddress address,
        IPEndPoint endpoint,
        bool useUnicastResponseBit,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return null;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            using var client = new UdpClient(AddressFamily.InterNetwork);
            var reverseName = string.Join('.', address.GetAddressBytes().Reverse()) + ".in-addr.arpa";
            var query = BuildPtrQuery(reverseName, useUnicastResponseBit);
            await client.SendAsync(query, endpoint, timeoutSource.Token).ConfigureAwait(false);

            while (!timeoutSource.IsCancellationRequested)
            {
                var response = await client.ReceiveAsync(timeoutSource.Token).ConfigureAwait(false);
                var hostname = ReadPtrAnswer(response.Buffer, reverseName);
                if (!string.IsNullOrWhiteSpace(hostname))
                {
                    return hostname;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        catch (SocketException)
        {
        }

        return null;
    }

    private static byte[] BuildPtrQuery(string reverseName, bool useUnicastResponseBit)
    {
        using var stream = new MemoryStream();
        WriteUInt16(stream, useUnicastResponseBit ? 0 : Random.Shared.Next(1, ushort.MaxValue));
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 1);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);
        WriteName(stream, reverseName);
        WriteUInt16(stream, 12);
        WriteUInt16(stream, useUnicastResponseBit ? 0x8001 : 1);
        return stream.ToArray();
    }

    private static string? ReadPtrAnswer(byte[] message, string expectedName)
    {
        if (message.Length < 12)
        {
            return null;
        }

        var questionCount = ReadUInt16(message, 4);
        var recordCount = ReadUInt16(message, 6) + ReadUInt16(message, 8) + ReadUInt16(message, 10);
        var offset = 12;

        for (var index = 0; index < questionCount; index++)
        {
            ReadName(message, ref offset);
            offset += 4;
            if (offset > message.Length)
            {
                return null;
            }
        }

        for (var index = 0; index < recordCount; index++)
        {
            var owner = ReadName(message, ref offset);
            if (offset + 10 > message.Length)
            {
                return null;
            }

            var type = ReadUInt16(message, offset);
            var dataLength = ReadUInt16(message, offset + 8);
            offset += 10;
            if (offset + dataLength > message.Length)
            {
                return null;
            }

            if (type == 12 && string.Equals(owner, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                var dataOffset = offset;
                var hostname = ReadName(message, ref dataOffset);
                return CleanHostname(hostname);
            }

            offset += dataLength;
        }

        return null;
    }

    private static string ReadName(byte[] message, ref int offset)
    {
        var labels = new List<string>();
        var current = offset;
        var returnOffset = -1;
        var jumps = 0;

        while (current < message.Length && jumps++ < 32)
        {
            var length = message[current++];
            if (length == 0)
            {
                offset = returnOffset >= 0 ? returnOffset : current;
                return string.Join('.', labels);
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (current >= message.Length)
                {
                    break;
                }

                returnOffset = returnOffset >= 0 ? returnOffset : current + 1;
                current = ((length & 0x3F) << 8) | message[current];
                continue;
            }

            if (current + length > message.Length)
            {
                break;
            }

            labels.Add(Encoding.UTF8.GetString(message, current, length));
            current += length;
        }

        offset = message.Length;
        return string.Empty;
    }

    private static string? CleanHostname(string? hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return null;
        }

        var firstLabel = hostname.Trim().TrimEnd('.')
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstLabel) ? null : firstLabel;
    }

    private static ushort ReadUInt16(byte[] buffer, int offset)
        => BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(offset, sizeof(ushort)));

    private static void WriteUInt16(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)value);
        stream.Write(buffer);
    }

    private static void WriteName(Stream stream, string name)
    {
        foreach (var label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            if (bytes.Length > 63)
            {
                throw new ArgumentException("DNS labels cannot exceed 63 bytes.", nameof(name));
            }

            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }

        stream.WriteByte(0);
    }
}
