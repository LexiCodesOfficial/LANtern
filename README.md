# LANtern

**A friendly, local-first network mapper for homes and homelabs.**

LANtern turns a local network into a readable device inventory. It discovers devices on private IPv4 networks, remembers them between scans, and explains what it finds in plain English.

Instead of presenting a wall of IP addresses and ports, LANtern answers practical questions:

- What is connected to my network?
- Which devices are new or missing?
- What is this unknown device?
- Why does LANtern think this is a server, printer, router, or smart-home device?
- Which common local services are available?

LANtern is designed for normal users, families, students, makers, and homelab enthusiasts. It is a local inventory tool, not an aggressive security scanner.

## Features

### Friendly device inventory

- Remembers devices in a local SQLite database.
- Matches devices by MAC address when available, so IP address changes do not create duplicates.
- Shows active hostname, IP address, MAC address, vendor, device type, status, first seen, and last seen.
- Supports custom names, notes, and room, person, or group labels.
- Tracks new devices, returning devices, status changes, hostname changes, and IP changes in a timeline.

### Safe local discovery

- Scans active private IPv4 interfaces only by default.
- Caps broad network ranges to 254 candidate addresses per interface.
- Starts with the local ARP or neighbour cache for fast results.
- Uses bounded ping fallback and optional conservative TCP discovery.
- Browses mDNS and SSDP locally without internet-scale probing.
- Streams partial results so devices appear while enrichment continues.

### Device identification

- Resolves hostnames through DHCP lease data, mDNS, LLMNR, reverse DNS, NetBIOS, and Windows ping name lookup where available.
- Downloads and caches the official [IEEE OUI CSV](https://standards-oui.ieee.org/oui/oui.csv) for MAC vendor lookup.
- Supports manual IEEE-style OUI CSV imports from Settings.
- Classifies common devices with beginner-friendly explanations.
- Recognizes useful local services such as SSH, SMB, RDP, Jellyfin, Home Assistant, Portainer, Proxmox, MQTT, and direct printing.

### Everyday usability

- Follows the system light or dark theme, with a persisted override.
- Offers instant search and filters for status, type, vendor, unknown devices, and recently seen devices.
- Supports optional best-effort desktop notifications.
- Includes Home, Homelab, and Classroom scan profiles.
- Supports configurable automatic scans while the app is open.
- Exports the device list and timeline to CSV.
- Creates a simple local PDF report.
- Can share an optional mobile-friendly, read-only dashboard on the local network.

### Optional integrations

- Imports active DHCP lease hostnames from MikroTik RouterOS API port `8728`.
- Detects local Proxmox, Docker / Portainer, and Home Assistant endpoints.
- Keeps integrations local and read-only by default.

## Install

Download the package for your platform from the repository's GitHub Releases page. Each package has a SHA-256 checksum file beside it.

### Windows

Run:

```text
LANtern-Setup-*-win-x64.exe
```

The installer does not require administrator rights. It installs LANtern for the current user, offers an optional desktop shortcut, and preserves local inventory and settings during upgrades and uninstalls.

### Linux

Download the archive for your processor:

```text
LANtern-*-linux-x64.tar.gz
LANtern-*-linux-arm64.tar.gz
```

Extract it and run:

```bash
tar -xzf LANtern-*-linux-x64.tar.gz
cd LANtern-*-linux-x64
chmod +x LANtern
./LANtern
```

LANtern is self-contained, but Avalonia still relies on standard desktop libraries provided by the Linux distribution.

### macOS

Download the ZIP for your Mac:

```text
LANtern-*-osx-arm64.zip
LANtern-*-osx-x64.zip
```

Extract `LANtern.app`, move it into `Applications`, and open it. Current development builds are not Apple-notarized, so macOS may require you to right-click the app and select **Open** the first time.

## Run From Source

### Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Windows, Linux, or macOS

The release workflow produces self-contained packages for Windows x64, Linux x64, Linux ARM64, Intel macOS, and Apple Silicon macOS. Windows currently receives the most hands-on testing.

### Start the desktop app

```powershell
dotnet restore
dotnet run --project src/Lantern.Desktop/Lantern.Desktop.csproj
```

### Verify the solution

```powershell
dotnet build Lantern.slnx --no-restore
dotnet run --no-build --no-restore --project tests/Lantern.SmokeTests/Lantern.SmokeTests.csproj
```

## Build The Windows Installer

Install [Inno Setup 6](https://jrsoftware.org/isinfo.php), then run:

```powershell
.\installer\build-installer.ps1
```

The script publishes a self-contained Windows x64 application, compiles the installer, and writes a SHA-256 checksum next to it:

```text
artifacts/
|-- installer/
|   |-- LANtern-Setup-1.0.1-win-x64.exe
|   `-- LANtern-Setup-1.0.1-win-x64.sha256.txt
`-- publish/
    `-- win-x64/
```

To package a specific version:

```powershell
.\installer\build-installer.ps1 -Version 1.0.1
```

## Build Portable Packages

Use the portable packaging script for Linux and macOS releases:

```powershell
./installer/build-portable.ps1 -Runtime linux-x64
./installer/build-portable.ps1 -Runtime linux-arm64
./installer/build-portable.ps1 -Runtime osx-x64
./installer/build-portable.ps1 -Runtime osx-arm64
```

The script publishes a self-contained app, includes the MIT license, creates a `.tar.gz` or `.zip` package under `artifacts/packages`, and writes a SHA-256 checksum.

```text
artifacts/
`-- packages/
    |-- LANtern-1.0.1-linux-x64.tar.gz
    |-- LANtern-1.0.1-linux-arm64.tar.gz
    |-- LANtern-1.0.1-osx-x64.zip
    `-- LANtern-1.0.1-osx-arm64.zip
```

macOS packages should be built on macOS for distribution. The script creates a standard `.app` bundle and uses native icon tooling when available.

## Publish A GitHub Release

The workflow in [`.github/workflows/release-packages.yml`](.github/workflows/release-packages.yml) builds the Windows installer and portable Linux and macOS packages for pushes to `main`, pull requests, and manual runs.

Push a numeric version tag to publish a GitHub Release with platform packages and SHA-256 checksums attached:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

Release tags must use the `vMAJOR.MINOR.PATCH` format.

## How Scanning Works

LANtern uses a staged pipeline so the interface stays responsive:

1. Load remembered devices from SQLite immediately.
2. Read the local ARP or neighbour cache.
3. Run a bounded ping sweep across private local addresses.
4. Browse local SSDP and mDNS responders.
5. Optionally use short TCP connection probes for devices that ignore ping.
6. Enrich discoveries with MAC addresses, hostnames, vendors, and selected common ports.
7. Classify devices and explain the reasoning.
8. Save inventory changes and timeline events continuously.

See [docs/scanner-pipeline.md](docs/scanner-pipeline.md) for implementation details.

## Safety Model

LANtern intentionally stays small, understandable, and local:

- Private RFC1918 IPv4 ranges only by default.
- Broad subnets are capped to a `/24`-sized scan by default.
- Bounded concurrency and short timeouts throughout the pipeline.
- Conservative probes for selected common local services only.
- No raw packet blasting.
- No internet-scale scanning.
- No administration actions against discovered services.

## Data And Privacy

LANtern is local-first.

- Device inventory is stored in `%LOCALAPPDATA%\LANtern\lantern.db` on Windows.
- Settings are stored in `%LOCALAPPDATA%\LANtern\settings.json` on Windows.
- Reports and CSV files are written to `Documents\LANtern Exports`.
- The IEEE OUI vendor database is cached locally after download.
- No LANtern cloud service is required.

On Linux and macOS, LANtern uses the operating system's local application-data directory through .NET.

The optional companion dashboard listens on the local network only after it is enabled in Settings. It is read-only, but anyone who can reach that local port can view the shared device list.

## MikroTik Hostnames

Some routers expose an active DHCP hostname that cannot be discovered reliably through reverse DNS or multicast lookup.

LANtern can import those names from MikroTik RouterOS:

1. Enable the RouterOS API service on port `8728`.
2. Create a read-only RouterOS account.
3. Open LANtern Settings.
4. Enable MikroTik DHCP lease hostnames.
5. Enter the router address and read-only credentials.

Use a dedicated read-only account. LANtern stores these settings locally.

## Project Structure

```text
src/
|-- Lantern.Domain/          Core device, event, and insight models
|-- Lantern.Application/     Use cases, abstractions, filtering, and classification
|-- Lantern.Infrastructure/  SQLite, scanning providers, exports, notifications, and integrations
`-- Lantern.Desktop/         Avalonia desktop application

tests/
`-- Lantern.SmokeTests/      Dependency-free executable smoke checks

docs/
|-- architecture.md
`-- scanner-pipeline.md
```

LANtern uses C#, .NET 9, Avalonia UI, and SQLite. The architecture is split into replaceable layers so new local integrations can be added without pushing database or UI concerns into the scanner.

## Current Boundaries

- Automatic scans run while the desktop app is open.
- The companion dashboard is read-only and intended for trusted private networks.
- Native notifications are best-effort because support differs by operating system.
- Linux ARP discovery expects the standard `ip` command, and Linux desktop notifications use `notify-send` when it is installed.
- macOS release bundles are currently unsigned and not notarized by Apple.
- Full authenticated Proxmox, Portainer, and Home Assistant inventory is not enabled by default.
- LANtern is an approachable LAN inventory tool, not a replacement for Nmap or enterprise monitoring software.

## Documentation

- [Architecture](docs/architecture.md)
- [Scanner pipeline](docs/scanner-pipeline.md)

## License

LANtern is available under the [MIT License](LICENSE). Third-party dependencies retain their respective licenses.
