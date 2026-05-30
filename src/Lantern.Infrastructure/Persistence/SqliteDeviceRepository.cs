using System.Globalization;
using Lantern.Application.Abstractions;
using Lantern.Domain;
using Microsoft.Data.Sqlite;

namespace Lantern.Infrastructure.Persistence;

public sealed class SqliteDeviceRepository : IDeviceRepository
{
    private readonly string _databasePath;
    private bool _initialized;

    public SqliteDeviceRepository(string databasePath)
    {
        _databasePath = databasePath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA busy_timeout = 5000;

            CREATE TABLE IF NOT EXISTS Devices (
              Id TEXT PRIMARY KEY,
              FriendlyName TEXT NOT NULL,
              Hostname TEXT NULL,
              HostnameSource TEXT NOT NULL DEFAULT 'Unknown',
              MacAddress TEXT NULL,
              Vendor TEXT NULL,
              DeviceType TEXT NOT NULL,
              Notes TEXT NOT NULL,
              LocationLabel TEXT NOT NULL DEFAULT '',
              FirstSeenUtc TEXT NOT NULL,
              LastSeenUtc TEXT NOT NULL,
              Status TEXT NOT NULL,
              LastIpAddress TEXT NULL,
              ClassificationExplanation TEXT NOT NULL,
              IsUserNamed INTEGER NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS IX_Devices_MacAddress
              ON Devices(MacAddress)
              WHERE MacAddress IS NOT NULL AND MacAddress <> '';

            CREATE TABLE IF NOT EXISTS DeviceEvents (
              Id TEXT PRIMARY KEY,
              DeviceId TEXT NULL,
              Kind TEXT NOT NULL,
              Title TEXT NOT NULL,
              Description TEXT NOT NULL,
              OccurredUtc TEXT NOT NULL,
              FOREIGN KEY(DeviceId) REFERENCES Devices(Id)
            );

            CREATE TABLE IF NOT EXISTS DevicePorts (
              DeviceId TEXT NOT NULL,
              Port INTEGER NOT NULL,
              FirstSeenUtc TEXT NOT NULL,
              LastSeenUtc TEXT NOT NULL,
              PRIMARY KEY(DeviceId, Port),
              FOREIGN KEY(DeviceId) REFERENCES Devices(Id)
            );

            DROP TABLE IF EXISTS DeviceObservations;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureColumnAsync(connection, "Devices", "HostnameSource", "TEXT NOT NULL DEFAULT 'Unknown'", cancellationToken);
        await EnsureColumnAsync(connection, "Devices", "LocationLabel", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        _initialized = true;
    }

    public async Task<IReadOnlyList<NetworkDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Devices ORDER BY FriendlyName";
        return await ReadDevicesAsync(command, cancellationToken);
    }

    public async Task<NetworkDevice?> GetDeviceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Devices WHERE Id = $id LIMIT 1";
        command.Parameters.AddWithValue("$id", id.ToString());
        return (await ReadDevicesAsync(command, cancellationToken)).FirstOrDefault();
    }

    public async Task<NetworkDevice?> FindByMacAddressAsync(string macAddress, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Devices WHERE MacAddress = $mac LIMIT 1";
        command.Parameters.AddWithValue("$mac", macAddress);
        return (await ReadDevicesAsync(command, cancellationToken)).FirstOrDefault();
    }

    public async Task<NetworkDevice?> FindByIpAddressAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Devices WHERE LastIpAddress = $ip LIMIT 1";
        command.Parameters.AddWithValue("$ip", ipAddress);
        return (await ReadDevicesAsync(command, cancellationToken)).FirstOrDefault();
    }

    public async Task SaveDeviceAsync(NetworkDevice device, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Devices (Id, FriendlyName, Hostname, HostnameSource, MacAddress, Vendor, DeviceType, Notes, LocationLabel, FirstSeenUtc, LastSeenUtc, Status, LastIpAddress, ClassificationExplanation, IsUserNamed)
            VALUES ($id, $friendlyName, $hostname, $hostnameSource, $macAddress, $vendor, $deviceType, $notes, $locationLabel, $firstSeenUtc, $lastSeenUtc, $status, $lastIpAddress, $classificationExplanation, $isUserNamed)
            ON CONFLICT(Id) DO UPDATE SET
              FriendlyName = excluded.FriendlyName,
              Hostname = excluded.Hostname,
              HostnameSource = excluded.HostnameSource,
              MacAddress = excluded.MacAddress,
              Vendor = excluded.Vendor,
              DeviceType = excluded.DeviceType,
              Notes = excluded.Notes,
              LocationLabel = excluded.LocationLabel,
              FirstSeenUtc = excluded.FirstSeenUtc,
              LastSeenUtc = excluded.LastSeenUtc,
              Status = excluded.Status,
              LastIpAddress = excluded.LastIpAddress,
              ClassificationExplanation = excluded.ClassificationExplanation,
              IsUserNamed = excluded.IsUserNamed;
            """;

        AddDeviceParameters(command, device);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddEventAsync(NetworkEvent networkEvent, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DeviceEvents (Id, DeviceId, Kind, Title, Description, OccurredUtc)
            VALUES ($id, $deviceId, $kind, $title, $description, $occurredUtc);
            """;
        command.Parameters.AddWithValue("$id", networkEvent.Id.ToString());
        command.Parameters.AddWithValue("$deviceId", (object?)networkEvent.DeviceId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$kind", networkEvent.Kind.ToString());
        command.Parameters.AddWithValue("$title", networkEvent.Title);
        command.Parameters.AddWithValue("$description", networkEvent.Description);
        command.Parameters.AddWithValue("$occurredUtc", Format(networkEvent.OccurredUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NetworkEvent>> GetEventsAsync(Guid? deviceId = null, int take = 100, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = deviceId is null
            ? "SELECT * FROM DeviceEvents ORDER BY OccurredUtc DESC LIMIT $take"
            : "SELECT * FROM DeviceEvents WHERE DeviceId = $deviceId ORDER BY OccurredUtc DESC LIMIT $take";
        command.Parameters.AddWithValue("$take", take);
        if (deviceId is not null)
        {
            command.Parameters.AddWithValue("$deviceId", deviceId.Value.ToString());
        }

        var events = new List<NetworkEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new NetworkEvent(
                Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
                reader.IsDBNull(reader.GetOrdinal("DeviceId")) ? null : Guid.Parse(reader.GetString(reader.GetOrdinal("DeviceId"))),
                Enum.Parse<NetworkEventKind>(reader.GetString(reader.GetOrdinal("Kind"))),
                reader.GetString(reader.GetOrdinal("Title")),
                reader.GetString(reader.GetOrdinal("Description")),
                ParseDate(reader.GetString(reader.GetOrdinal("OccurredUtc")))));
        }

        return events;
    }

    public async Task ReplaceOpenPortsAsync(Guid deviceId, IEnumerable<int> ports, DateTimeOffset seenUtc, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = "DELETE FROM DevicePorts WHERE DeviceId = $deviceId";
            delete.Parameters.AddWithValue("$deviceId", deviceId.ToString());
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var port in ports.Distinct())
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = "INSERT INTO DevicePorts (DeviceId, Port, FirstSeenUtc, LastSeenUtc) VALUES ($deviceId, $port, $firstSeenUtc, $lastSeenUtc)";
            insert.Parameters.AddWithValue("$deviceId", deviceId.ToString());
            insert.Parameters.AddWithValue("$port", port);
            insert.Parameters.AddWithValue("$firstSeenUtc", Format(seenUtc));
            insert.Parameters.AddWithValue("$lastSeenUtc", Format(seenUtc));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<int>> GetOpenPortsAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Port FROM DevicePorts WHERE DeviceId = $deviceId ORDER BY Port";
        command.Parameters.AddWithValue("$deviceId", deviceId.ToString());
        var ports = new List<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ports.Add(reader.GetInt32(0));
        }

        return ports;
    }

    public async Task PurgeAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM DevicePorts;
            DELETE FROM DeviceEvents;
            DELETE FROM Devices;
            VACUUM;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteConnection CreateConnection()
        => new($"Data Source={_databasePath};Cache=Shared;Default Timeout=5");

    private static async Task EnsureColumnAsync(SqliteConnection connection, string tableName, string columnName, string definition, CancellationToken cancellationToken)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({tableName})";
        await using var reader = await check.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(reader.GetOrdinal("name")), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await reader.DisposeAsync();
        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition}";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<NetworkDevice>> ReadDevicesAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var devices = new List<NetworkDevice>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            devices.Add(new NetworkDevice
            {
                Id = Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
                FriendlyName = reader.GetString(reader.GetOrdinal("FriendlyName")),
                Hostname = GetNullableString(reader, "Hostname"),
                HostnameSource = Enum.Parse<HostnameSource>(reader.GetString(reader.GetOrdinal("HostnameSource"))),
                MacAddress = GetNullableString(reader, "MacAddress"),
                Vendor = GetNullableString(reader, "Vendor"),
                DeviceType = Enum.Parse<DeviceType>(reader.GetString(reader.GetOrdinal("DeviceType"))),
                Notes = reader.GetString(reader.GetOrdinal("Notes")),
                LocationLabel = reader.GetString(reader.GetOrdinal("LocationLabel")),
                FirstSeenUtc = ParseDate(reader.GetString(reader.GetOrdinal("FirstSeenUtc"))),
                LastSeenUtc = ParseDate(reader.GetString(reader.GetOrdinal("LastSeenUtc"))),
                Status = Enum.Parse<DeviceStatus>(reader.GetString(reader.GetOrdinal("Status"))),
                LastIpAddress = GetNullableString(reader, "LastIpAddress"),
                ClassificationExplanation = reader.GetString(reader.GetOrdinal("ClassificationExplanation")),
                IsUserNamed = reader.GetInt32(reader.GetOrdinal("IsUserNamed")) == 1
            });
        }

        return devices;
    }

    private static void AddDeviceParameters(SqliteCommand command, NetworkDevice device)
    {
        command.Parameters.AddWithValue("$id", device.Id.ToString());
        command.Parameters.AddWithValue("$friendlyName", device.FriendlyName);
        command.Parameters.AddWithValue("$hostname", (object?)device.Hostname ?? DBNull.Value);
        command.Parameters.AddWithValue("$hostnameSource", device.HostnameSource.ToString());
        command.Parameters.AddWithValue("$macAddress", (object?)device.MacAddress ?? DBNull.Value);
        command.Parameters.AddWithValue("$vendor", (object?)device.Vendor ?? DBNull.Value);
        command.Parameters.AddWithValue("$deviceType", device.DeviceType.ToString());
        command.Parameters.AddWithValue("$notes", device.Notes);
        command.Parameters.AddWithValue("$locationLabel", device.LocationLabel);
        command.Parameters.AddWithValue("$firstSeenUtc", Format(device.FirstSeenUtc));
        command.Parameters.AddWithValue("$lastSeenUtc", Format(device.LastSeenUtc));
        command.Parameters.AddWithValue("$status", device.Status.ToString());
        command.Parameters.AddWithValue("$lastIpAddress", (object?)device.LastIpAddress ?? DBNull.Value);
        command.Parameters.AddWithValue("$classificationExplanation", device.ClassificationExplanation);
        command.Parameters.AddWithValue("$isUserNamed", device.IsUserNamed ? 1 : 0);
    }

    private static string? GetNullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string Format(DateTimeOffset value)
        => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}
