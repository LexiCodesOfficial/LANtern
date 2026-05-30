# LANtern

LANtern is a friendly local area network mapper for homes, homelabs, students, families, and small teams.

Instead of presenting raw scan output, LANtern keeps a remembered device inventory and explains what it sees in plain English: new devices, returning devices, IP changes, likely device types, and calm security insights.

## Architecture

LANtern is split into clean, replaceable layers:

- `Lantern.Domain`: core device, event, status, and insight models.
- `Lantern.Application`: use cases, repository abstractions, classification, scan orchestration, filtering, and insights.
- `Lantern.Infrastructure`: SQLite persistence, local network scanning, ARP/MAC lookup, vendor lookup, and notifications.
- `Lantern.Desktop`: Avalonia desktop UI and view models.

## MVP Capabilities

- Local subnet discovery with ping sweep, hostname lookup, ARP cache MAC retrieval, vendor lookup, and common-port probing.
- SQLite device inventory keyed by MAC address where available, so devices survive IP address changes.
- Device classification with beginner-friendly explanations.
- Change timeline for new devices, status changes, IP changes, hostname changes, and device disappearance.
- Dashboard totals for total, online, offline, new, and unknown devices.
- Device detail page with editable friendly name and notes.
- Search and filters by online status, type, vendor, unknown devices, and recently seen devices.
- Optional best-effort desktop notifications using the host operating system.

## Running

```powershell
dotnet restore
dotnet run --project src/Lantern.Desktop/Lantern.Desktop.csproj
```

LANtern stores its SQLite database under the user's local application data folder.

## Building The Windows Installer

Install Inno Setup 6, then run:

```powershell
.\installer\build-installer.ps1
```

The script publishes a self-contained Windows x64 build and creates an installer under `artifacts\installer`.
The installer uses the current user's local application data folder, so administrator rights are not required.
Inventory and settings remain available across upgrades and uninstalls.

## Publishing A GitHub Release

GitHub Actions builds the Windows installer for pushes to `main`, pull requests, and manual workflow runs.
To create a GitHub Release with the installer attached, push a numeric version tag:

```powershell
git tag v0.2.0
git push origin v0.2.0
```

Release tags must use the `vMAJOR.MINOR.PATCH` format, such as `v0.2.0`.
The tag version is injected into the published application and installer metadata automatically.
Each release includes the installer and its SHA-256 checksum file.

## Delivered Roadmap

### MVP

- [x] Reliable local scan across private IPv4 interfaces.
- [x] Remembered device list and timeline.
- [x] Friendly names, notes, filters, and classification explanations.
- [x] Calm security insights for common services.

### 0.5

- [x] Multi-interface private subnet handling with safe host caps and a visible subnet summary.
- [x] Automatic IEEE OUI refresh plus manual IEEE-style CSV import.
- [x] Configurable in-app scan schedule, native notification attempts, and persisted preferences.
- [x] Timeline and device-list CSV exports.

### 0.8

- [x] Conservative service discovery via mDNS browse and SSDP.
- [x] Device avatars and persistent room, person, or group labels.
- [x] Home, homelab, and classroom scan profiles.

### 1.0

- [x] Local PDF reports.
- [x] MikroTik DHCP hostname import plus read-only Proxmox, Docker/Portainer, and Home Assistant endpoint summaries.
- [x] Optional mobile-friendly read-only dashboard shared on the private LAN.

LANtern keeps the later modules intentionally local and read-only. Authenticated service inventory remains a natural extension point rather than default scan behavior.
