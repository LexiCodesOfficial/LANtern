# LANtern Architecture

## System Shape

LANtern follows a modular clean architecture:

- Domain contains stable business concepts with no infrastructure dependencies.
- Application contains workflows and interfaces. It knows what must happen but not how SQLite, ARP, or Avalonia work.
- Infrastructure implements adapters for local scanning, SQLite, vendor lookup, and notifications.
- Desktop hosts Avalonia UI, dependency injection, and view models.

This keeps optional modules, such as authenticated Proxmox, Docker, Home Assistant, and router adapters, additive rather than invasive.

## Database Schema

```sql
CREATE TABLE Devices (
  Id TEXT PRIMARY KEY,
  FriendlyName TEXT NOT NULL,
  Hostname TEXT NULL,
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

CREATE UNIQUE INDEX IX_Devices_MacAddress
  ON Devices(MacAddress)
  WHERE MacAddress IS NOT NULL AND MacAddress <> '';

CREATE TABLE DeviceEvents (
  Id TEXT PRIMARY KEY,
  DeviceId TEXT NULL,
  Kind TEXT NOT NULL,
  Title TEXT NOT NULL,
  Description TEXT NOT NULL,
  OccurredUtc TEXT NOT NULL,
  FOREIGN KEY(DeviceId) REFERENCES Devices(Id)
);

CREATE TABLE DevicePorts (
  DeviceId TEXT NOT NULL,
  Port INTEGER NOT NULL,
  FirstSeenUtc TEXT NOT NULL,
  LastSeenUtc TEXT NOT NULL,
  PRIMARY KEY(DeviceId, Port),
  FOREIGN KEY(DeviceId) REFERENCES Devices(Id)
);

```

## Network Scanning Plan

1. Enumerate active private IPv4 network interfaces.
2. Expand each private subnet with a 254-host safety cap.
3. Read ARP/neighbour caches immediately, then use bounded ping and optional TCP discovery.
4. Browse mDNS and SSDP conservatively for local responders.
5. Resolve DHCP lease, multicast, NetBIOS, and reverse-DNS hostnames where possible.
6. Look up vendors from the cached official IEEE OUI CSV with imported and built-in fallbacks.
7. Probe a small set of common ports with short TCP timeouts.
8. Merge observations into the inventory and raise friendly timeline events.

## Classification Engine

Classification is rule-based in the MVP:

- Hostnames such as `desktop-*`, `laptop-*`, and `win-*` imply Windows PC.
- Vendors such as Espressif imply ESP32 or IoT devices.
- Port combinations, such as 9100 for printers or 8009/554 for TVs/media devices, improve confidence.
- Router-like addresses, gateway hostnames, or network vendors classify as Router.

Every classification returns a plain-English explanation so users understand the guess.

The engine is intentionally behind `IDeviceClassifier`, allowing later replacement by richer fingerprints, mDNS, SSDP, DHCP lease imports, MikroTik data, or user-trained rules.
