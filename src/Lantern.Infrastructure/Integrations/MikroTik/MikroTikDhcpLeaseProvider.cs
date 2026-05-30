using Lantern.Application;
using Lantern.Application.Abstractions;
using System.Net.Sockets;
using System.Text;

namespace Lantern.Infrastructure.Integrations.MikroTik;

public sealed class MikroTikDhcpLeaseProvider : IDhcpLeaseProvider
{
    private readonly MikroTikSettingsStore _settingsStore;

    public MikroTikDhcpLeaseProvider(MikroTikSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public string Name => "MikroTik DHCP";
    public bool IsEnabled => _settingsStore.Current.IsEnabled && !string.IsNullOrWhiteSpace(_settingsStore.Current.Host);

    public async Task<IReadOnlyList<DhcpLeaseRecord>> GetLeasesAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return [];
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));

        try
        {
            var settings = _settingsStore.Current;
            using var client = new TcpClient();
            await client.ConnectAsync(settings.Host, settings.Port, timeout.Token).ConfigureAwait(false);

            await using var stream = client.GetStream();
            if (!await LoginAsync(stream, settings.Username, settings.Password, timeout.Token).ConfigureAwait(false))
            {
                return [];
            }

            await WriteSentenceAsync(stream, [
                "/ip/dhcp-server/lease/print",
                "=.proplist=address,active-address,mac-address,host-name,active-host-name,comment,status"
            ], timeout.Token).ConfigureAwait(false);

            var leases = new List<DhcpLeaseRecord>();
            while (true)
            {
                var sentence = await ReadSentenceAsync(stream, timeout.Token).ConfigureAwait(false);
                if (sentence.Count == 0 || sentence[0] == "!done")
                {
                    break;
                }

                if (sentence[0] != "!re")
                {
                    continue;
                }

                var fields = ParseFields(sentence);
                var ipAddress = GetField(fields, "active-address") ?? GetField(fields, "address");
                var macAddress = GetField(fields, "mac-address");
                var hostname = CleanHostname(GetField(fields, "active-host-name") ?? GetField(fields, "host-name"));

                if (!string.IsNullOrWhiteSpace(ipAddress) && (!string.IsNullOrWhiteSpace(hostname) || !string.IsNullOrWhiteSpace(macAddress)))
                {
                    leases.Add(new DhcpLeaseRecord(ipAddress, macAddress, hostname, Name));
                }
            }

            await WriteSentenceAsync(stream, ["/quit"], timeout.Token).ConfigureAwait(false);
            return leases;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }

    private static async Task<bool> LoginAsync(NetworkStream stream, string username, string password, CancellationToken cancellationToken)
    {
        await WriteSentenceAsync(stream, ["/login", $"=name={username}", $"=password={password}"], cancellationToken).ConfigureAwait(false);

        while (true)
        {
            var sentence = await ReadSentenceAsync(stream, cancellationToken).ConfigureAwait(false);
            if (sentence.Count == 0 || sentence[0] == "!done")
            {
                return true;
            }

            if (sentence[0] == "!trap" || sentence[0] == "!fatal")
            {
                return false;
            }
        }
    }

    private static async Task WriteSentenceAsync(NetworkStream stream, IReadOnlyList<string> words, CancellationToken cancellationToken)
    {
        foreach (var word in words)
        {
            var bytes = Encoding.UTF8.GetBytes(word);
            await WriteLengthAsync(stream, bytes.Length, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        await WriteLengthAsync(stream, 0, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<string>> ReadSentenceAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var words = new List<string>();
        while (true)
        {
            var length = await ReadLengthAsync(stream, cancellationToken).ConfigureAwait(false);
            if (length == 0)
            {
                return words;
            }

            var buffer = new byte[length];
            await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            words.Add(Encoding.UTF8.GetString(buffer));
        }
    }

    private static async Task WriteLengthAsync(NetworkStream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[5];
        var count = 0;

        if (length < 0x80)
        {
            buffer[count++] = (byte)length;
        }
        else if (length < 0x4000)
        {
            buffer[count++] = (byte)((length >> 8) | 0x80);
            buffer[count++] = (byte)length;
        }
        else if (length < 0x200000)
        {
            buffer[count++] = (byte)((length >> 16) | 0xC0);
            buffer[count++] = (byte)(length >> 8);
            buffer[count++] = (byte)length;
        }
        else if (length < 0x10000000)
        {
            buffer[count++] = (byte)((length >> 24) | 0xE0);
            buffer[count++] = (byte)(length >> 16);
            buffer[count++] = (byte)(length >> 8);
            buffer[count++] = (byte)length;
        }
        else
        {
            buffer[count++] = 0xF0;
            buffer[count++] = (byte)(length >> 24);
            buffer[count++] = (byte)(length >> 16);
            buffer[count++] = (byte)(length >> 8);
            buffer[count++] = (byte)length;
        }

        await stream.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ReadLengthAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var first = await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
        if ((first & 0x80) == 0)
        {
            return first;
        }

        if ((first & 0xC0) == 0x80)
        {
            return ((first & ~0xC0) << 8) + await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        if ((first & 0xE0) == 0xC0)
        {
            return ((first & ~0xE0) << 16)
                + (await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false) << 8)
                + await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        if ((first & 0xF0) == 0xE0)
        {
            return ((first & ~0xF0) << 24)
                + (await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false) << 16)
                + (await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false) << 8)
                + await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        return (await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false) << 24)
            + (await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false) << 16)
            + (await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false) << 8)
            + await ReadByteAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ReadByteAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer[0];
    }

    private static Dictionary<string, string> ParseFields(IEnumerable<string> sentence)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in sentence)
        {
            if (!word.StartsWith('='))
            {
                continue;
            }

            var separator = word.IndexOf('=', 1);
            if (separator <= 1)
            {
                continue;
            }

            fields[word[1..separator]] = word[(separator + 1)..];
        }

        return fields;
    }

    private static string? GetField(IReadOnlyDictionary<string, string> fields, string name)
        => fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static string? CleanHostname(string? hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return null;
        }

        var trimmed = hostname.Trim().TrimEnd('.');
        return trimmed.Length == 0 ? null : trimmed;
    }
}
