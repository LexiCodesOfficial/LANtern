namespace Lantern.Domain;

public enum HostnameSource
{
    Unknown,
    ReverseDns,
    Llmnr,
    MulticastDns,
    NetBios,
    DhcpLease
}
