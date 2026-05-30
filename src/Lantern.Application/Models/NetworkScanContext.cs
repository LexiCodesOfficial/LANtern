using System.Net;

namespace Lantern.Application;

public sealed record NetworkScanContext(
    NetworkScanOptions Options,
    IReadOnlyList<IPAddress> CandidateAddresses,
    DateTimeOffset StartedUtc);
