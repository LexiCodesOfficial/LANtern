using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.RegularExpressions;
using Lantern.Application.Abstractions;

namespace Lantern.Infrastructure.Networking;

public sealed class SeedVendorLookupService : IVendorLookupService, IVendorDatabaseService
{
    private const string IeeeOuiCsvUrl = "https://standards-oui.ieee.org/oui/oui.csv";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromDays(30);
    private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private Lazy<IReadOnlyDictionary<string, string>> _vendors = new(LoadVendors);

    public string? LookupVendor(string? macAddress)
    {
        var prefix = NormalizePrefix(macAddress);
        if (prefix is null)
        {
            return null;
        }

        return _cache.GetOrAdd(prefix, key => _vendors.Value.TryGetValue(key, out var vendor) ? vendor : null);
    }

    public async Task ImportAsync(string csvPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
        {
            throw new FileNotFoundException("Choose an IEEE-style OUI CSV file first.", csvPath);
        }

        var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LANtern", "oui.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using var source = File.OpenRead(csvPath);
        await using var destination = File.Create(target);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        _cache.Clear();
        _vendors = new Lazy<IReadOnlyDictionary<string, string>>(LoadVendors);
    }

    private static IReadOnlyDictionary<string, string> LoadVendors()
    {
        var vendors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (prefix, vendor) in BuiltInVendors)
        {
            vendors[NormalizeAssignment(prefix)] = vendor;
        }

        var ieeeCachePath = GetIeeeCachePath();

        RefreshIeeeCacheIfNeeded(ieeeCachePath);

        if (File.Exists(ieeeCachePath))
        {
            foreach (var (prefix, vendor) in ParseVendorFile(ieeeCachePath))
            {
                vendors[prefix] = vendor;
            }
        }

        foreach (var path in GetCandidateVendorFiles())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var (prefix, vendor) in ParseVendorFile(path))
            {
                vendors[prefix] = vendor;
            }
        }

        return vendors;
    }

    private static IEnumerable<string> GetCandidateVendorFiles()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return GetIeeeCachePath();
        yield return Path.Combine(localAppData, "LANtern", "oui.csv");
        yield return Path.Combine(localAppData, "LANtern", "oui.txt");
        yield return Path.Combine(localAppData, "Lantern", "oui.csv");
        yield return Path.Combine(localAppData, "Lantern", "oui.txt");
        yield return Path.Combine(AppContext.BaseDirectory, "Data", "oui.csv");
        yield return Path.Combine(AppContext.BaseDirectory, "Data", "oui.txt");
    }

    private static string GetIeeeCachePath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LANtern", "ieee-oui.csv");

    private static void RefreshIeeeCacheIfNeeded(string path)
    {
        try
        {
            if (File.Exists(path) && DateTimeOffset.UtcNow - new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero) < CacheDuration)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(6)
            };

            using var response = httpClient.GetAsync(IeeeOuiCsvUrl).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var csv = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!csv.Contains("Assignment", StringComparison.OrdinalIgnoreCase) || !csv.Contains("Organization Name", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            File.WriteAllText(path, csv);
        }
        catch
        {
            // Vendor lookup should never block or fail LAN scanning. The built-in table remains the fallback.
        }
    }

    private static IEnumerable<(string Prefix, string Vendor)> ParseVendorFile(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (TryParseCsvVendor(line, out var csvPrefix, out var csvVendor))
            {
                yield return (csvPrefix, csvVendor);
                continue;
            }

            var txtMatch = Regex.Match(line, @"(?<prefix>[0-9A-Fa-f]{2}[-:][0-9A-Fa-f]{2}[-:][0-9A-Fa-f]{2}|[0-9A-Fa-f]{6})\s+(?:\(base 16\)\s+)?(?<vendor>.+)$");
            if (txtMatch.Success)
            {
                yield return (NormalizeAssignment(txtMatch.Groups["prefix"].Value), txtMatch.Groups["vendor"].Value.Trim());
            }
        }
    }

    private static bool TryParseCsvVendor(string line, out string prefix, out string vendor)
    {
        prefix = string.Empty;
        vendor = string.Empty;

        var fields = SplitCsvLine(line);
        if (fields.Count < 2)
        {
            return false;
        }

        var assignmentIndex = fields.FindIndex(field => Regex.IsMatch(field, "^[0-9A-Fa-f]{6}$"));
        if (assignmentIndex < 0)
        {
            return false;
        }

        var vendorIndex = fields.Count > assignmentIndex + 1 ? assignmentIndex + 1 : -1;

        // IEEE oui.csv shape: Registry,Assignment,Organization Name,Organization Address
        if (assignmentIndex == 1 && fields.Count > 2)
        {
            vendorIndex = 2;
        }

        if (vendorIndex < 0 || string.IsNullOrWhiteSpace(fields[vendorIndex]))
        {
            return false;
        }

        prefix = NormalizeAssignment(fields[assignmentIndex]);
        vendor = fields[vendorIndex].Trim();
        return true;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (character == ',' && !inQuotes)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        fields.Add(current.ToString().Trim());
        return fields;
    }

    private static string? NormalizePrefix(string? macAddress)
    {
        if (string.IsNullOrWhiteSpace(macAddress))
        {
            return null;
        }

        var assignment = NormalizeAssignment(macAddress);
        return assignment.Length < 6 ? null : assignment;
    }

    private static string NormalizeAssignment(string value)
    {
        var hex = new string(value.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());
        return hex.Length < 6 ? hex : hex[..6];
    }

    private static readonly IReadOnlyDictionary<string, string> BuiltInVendors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["00:03:93"] = "Apple",
        ["00:05:02"] = "Apple",
        ["00:0A:27"] = "Apple",
        ["00:0A:95"] = "Apple",
        ["00:0D:93"] = "Apple",
        ["00:11:24"] = "Apple",
        ["00:14:51"] = "Apple",
        ["00:16:CB"] = "Apple",
        ["00:17:F2"] = "Apple",
        ["00:19:E3"] = "Apple",
        ["00:1A:11"] = "Google",
        ["00:1B:63"] = "Apple",
        ["00:1C:B3"] = "Apple",
        ["00:1D:4F"] = "Apple",
        ["00:1E:52"] = "Apple",
        ["00:1E:C2"] = "Apple",
        ["00:22:41"] = "Apple",
        ["00:23:12"] = "Apple",
        ["00:25:00"] = "Apple",
        ["00:26:08"] = "Apple",
        ["00:26:4A"] = "Apple",
        ["00:1F:F3"] = "Apple",
        ["3C:22:FB"] = "Apple",
        ["40:A6:D9"] = "Apple",
        ["48:A9:1C"] = "Apple",
        ["58:55:CA"] = "Apple",
        ["60:F8:1D"] = "Apple",
        ["68:AB:BC"] = "Apple",
        ["70:56:81"] = "Apple",
        ["78:4F:43"] = "Apple",
        ["7C:C3:A1"] = "Apple",
        ["88:66:5A"] = "Apple",
        ["A4:83:E7"] = "Apple",
        ["AC:BC:32"] = "Apple",
        ["B8:09:8A"] = "Apple",
        ["C8:2A:14"] = "Apple",
        ["D0:03:4B"] = "Apple",
        ["DC:A9:04"] = "Apple",
        ["F0:18:98"] = "Apple",

        ["00:16:32"] = "Samsung",
        ["00:17:C9"] = "Samsung",
        ["00:1D:25"] = "Samsung",
        ["00:21:19"] = "Samsung",
        ["00:23:39"] = "Samsung",
        ["00:26:37"] = "Samsung",
        ["10:30:47"] = "Samsung",
        ["18:3A:2D"] = "Samsung",
        ["28:39:5E"] = "Samsung",
        ["38:01:95"] = "Samsung",
        ["5C:F6:DC"] = "Samsung",
        ["8C:77:16"] = "Samsung",
        ["A0:21:95"] = "Samsung",
        ["B4:62:93"] = "Samsung",
        ["C0:BD:D1"] = "Samsung",
        ["E8:50:8B"] = "Samsung",

        ["B8:27:EB"] = "Raspberry Pi",
        ["DC:A6:32"] = "Raspberry Pi",
        ["E4:5F:01"] = "Raspberry Pi",
        ["2C:CF:67"] = "Raspberry Pi",
        ["D8:3A:DD"] = "Raspberry Pi",

        ["18:FE:34"] = "Espressif",
        ["24:0A:C4"] = "Espressif",
        ["24:6F:28"] = "Espressif",
        ["2C:3A:E8"] = "Espressif",
        ["30:AE:A4"] = "Espressif",
        ["34:85:18"] = "Espressif",
        ["3C:61:05"] = "Espressif",
        ["7C:DF:A1"] = "Espressif",
        ["84:0D:8E"] = "Espressif",
        ["84:F3:EB"] = "Espressif",
        ["94:B5:55"] = "Espressif",
        ["A0:A3:B3"] = "Espressif",
        ["B4:E6:2D"] = "Espressif",
        ["C8:2B:96"] = "Espressif",
        ["D8:A0:1D"] = "Espressif",

        ["00:0C:42"] = "MikroTik",
        ["4C:5E:0C"] = "MikroTik",
        ["64:D1:54"] = "MikroTik",
        ["74:4D:28"] = "MikroTik",
        ["D4:CA:6D"] = "MikroTik",
        ["E4:8D:8C"] = "MikroTik",

        ["04:18:D6"] = "Ubiquiti",
        ["18:E8:29"] = "Ubiquiti",
        ["24:A4:3C"] = "Ubiquiti",
        ["44:D9:E7"] = "Ubiquiti",
        ["68:D7:9A"] = "Ubiquiti",
        ["74:83:C2"] = "Ubiquiti",
        ["74:AC:B9"] = "Ubiquiti",
        ["78:45:58"] = "Ubiquiti",
        ["80:2A:A8"] = "Ubiquiti",
        ["B4:FB:E4"] = "Ubiquiti",
        ["DC:9F:DB"] = "Ubiquiti",
        ["F0:9F:C2"] = "Ubiquiti",

        ["14:CC:20"] = "TP-Link",
        ["18:A6:F7"] = "TP-Link",
        ["50:C7:BF"] = "TP-Link",
        ["5C:E9:31"] = "TP-Link",
        ["60:E3:27"] = "TP-Link",
        ["98:DE:D0"] = "TP-Link",
        ["B0:4E:26"] = "TP-Link",
        ["C0:25:E9"] = "TP-Link",
        ["F4:F2:6D"] = "TP-Link",

        ["00:04:4B"] = "NVIDIA",
        ["00:17:AB"] = "Nintendo",
        ["00:19:C5"] = "Sony Interactive Entertainment",
        ["00:1D:D8"] = "Microsoft",
        ["7C:ED:8D"] = "Microsoft",
        ["98:5F:D3"] = "Microsoft",
        ["C8:3F:26"] = "Microsoft",

        ["00:80:77"] = "Brother",
        ["00:1B:A9"] = "Brother",
        ["30:05:5C"] = "Brother",
        ["00:1E:8F"] = "Canon",
        ["00:00:85"] = "Canon",
        ["00:26:73"] = "Ricoh",
        ["00:17:C8"] = "Kyocera",

        ["00:13:02"] = "Intel",
        ["00:15:17"] = "Intel",
        ["00:16:EA"] = "Intel",
        ["00:19:D1"] = "Intel",
        ["00:1B:21"] = "Intel",
        ["00:1C:BF"] = "Intel",
        ["00:1E:64"] = "Intel",
        ["00:21:5C"] = "Intel",
        ["00:22:FA"] = "Intel",
        ["00:24:D7"] = "Intel",
        ["00:26:C6"] = "Intel",
        ["34:13:E8"] = "Intel",
        ["68:5D:43"] = "Intel",
        ["A0:A8:CD"] = "Intel",

        ["00:14:22"] = "Dell",
        ["18:03:73"] = "Dell",
        ["24:B6:FD"] = "Dell",
        ["34:17:EB"] = "Dell",
        ["B8:AC:6F"] = "Dell",
        ["F8:BC:12"] = "Dell",

        ["00:12:FE"] = "Lenovo",
        ["00:21:CC"] = "Lenovo",
        ["08:9E:01"] = "Lenovo",
        ["14:9D:09"] = "Lenovo",
        ["28:D2:44"] = "Lenovo",
        ["54:EE:75"] = "Lenovo",
        ["D8:BB:C1"] = "Lenovo",

        ["00:1A:4B"] = "HP",
        ["00:1F:29"] = "HP",
        ["00:21:5A"] = "HP",
        ["00:23:7D"] = "HP",
        ["00:25:B3"] = "HP",
        ["2C:44:FD"] = "HP",
        ["3C:52:82"] = "HP",
        ["6C:02:E0"] = "HP",
        ["B4:B5:2F"] = "HP",

        ["00:04:20"] = "Slim Devices",
        ["00:08:9B"] = "ICP Electronics",
        ["00:11:32"] = "Synology",
        ["00:24:1D"] = "QNAP",
        ["24:5E:BE"] = "QNAP",
        ["70:85:C2"] = "ASRock",
        ["D8:5E:D3"] = "ASRock",
        ["00:0C:6E"] = "ASUSTek",
        ["04:92:26"] = "ASUSTek",
        ["10:7B:44"] = "ASUSTek",
        ["2C:4D:54"] = "ASUSTek",
        ["34:97:F6"] = "ASUSTek",
        ["38:D5:47"] = "ASUSTek",
        ["40:16:7E"] = "ASUSTek",
        ["60:45:CB"] = "ASUSTek",
        ["70:8B:CD"] = "ASUSTek",
        ["88:D7:F6"] = "ASUSTek",
        ["AC:22:0B"] = "ASUSTek",
        ["D4:5D:64"] = "ASUSTek",
        ["E0:3F:49"] = "ASUSTek",
        ["F0:2F:74"] = "ASUSTek",

        ["00:E0:4C"] = "Realtek",
        ["00:1D:7D"] = "Realtek",
        ["00:24:21"] = "Realtek",
        ["08:00:27"] = "VirtualBox",
        ["00:05:69"] = "VMware",
        ["00:0C:29"] = "VMware",
        ["00:1C:14"] = "VMware",
        ["00:50:56"] = "VMware",
        ["52:54:00"] = "QEMU/KVM",
        ["BC:24:11"] = "Proxmox Server Solutions GmbH"
    };
}
