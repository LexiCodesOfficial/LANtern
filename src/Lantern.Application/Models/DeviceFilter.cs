using Lantern.Domain;

namespace Lantern.Application;

public sealed record DeviceFilter(
    string SearchText = "",
    DeviceStatus? Status = null,
    DeviceType? DeviceType = null,
    string? Vendor = null,
    bool UnknownOnly = false,
    bool RecentlySeenOnly = false);
