namespace Lantern.Application.Abstractions;

public interface IVendorLookupService
{
    string? LookupVendor(string? macAddress);
}

public interface IVendorDatabaseService
{
    Task ImportAsync(string csvPath, CancellationToken cancellationToken = default);
}
