# TRACE

> **An ambient, native Windows network throughput instrument.**

[![Platform](https://img.shields.io/badge/Platform-Windows%2011-0078D4?logo=windows&logoColor=white)](https://github.com/ShauryaByte/TRACE)
[![Framework](https://img.shields.io/badge/Framework-.NET%2010%20%7C%20WinUI%203-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Privacy](https://img.shields.io/badge/Privacy-Local--First%20%7C%20Zero%20Telemetry-success.svg)](SECURITY.md)

TRACE is a lightweight, local-first Windows network instrument crafted to give you immediate, tactile visibility into your system's network activity. Instead of cluttering your desktop with heavy diagnostic suites, browser-based dashboards, or complex packet analyzers, TRACE acts like a precision desktop gauge—quiet, elegant, and always responsive.

Built natively in **C# 13**, **.NET 10**, and **WinUI 3 (Windows App SDK)** with direct **Windows Kernel ETW** integration, TRACE provides real-time system throughput, live per-process activity when elevated, local connection inspection, hardware privacy indicators, and a floating Mini Speed Window.

---

## Features

### ⚡ Live Throughput
- **Real-Time Speed Measurement**: Instantaneous system-wide download and upload rates updated every second.
- **Dynamic Waveform Flow**: Fluid, real-time rate visualization showing network surges and idle states.
- **Session Totals**: Cumulative bytes transferred across your entire session, calculated straight from the physical network interfaces.

### 📊 Application Activity
See exactly which applications are consuming bandwidth right now. When elevated, TRACE uses kernel-level event attribution to display real, measured transfer rates per process:

```text
Google Chrome     ↓ 18.4 Mbps    ↑ 0.3 Mbps
Steam             ↓  8.7 Mbps    ↑ 0.1 Mbps
Discord           ↓  0.2 Mbps    ↑ 0.0 Mbps
```

*These are real, live measurements captured from kernel socket transfers—never simulated or estimated.*

### 🔍 Inspect Diagnostics
Dive deeper into active network connections with two distinct levels:
- **Activity Mode**: Grouped by application name with connection counts, aggregated bandwidth, and current connection states.
- **Details Mode**: Process-level technical metadata for technical troubleshooting:
  - **PID**: Windows Process Identifier
  - **Protocol**: IPv4 / IPv6 TCP & UDP
  - **Local Endpoint**: Local binding address and port
  - **Remote Endpoint**: Connected remote host and service port
  - **Connection State**: Established, Listening, Time-Wait, Close-Wait, and more.

> ℹ️ *All endpoint information is retrieved from Windows IP Helper connection tables locally. TRACE never inspects or decrypts packet payloads.*

### 🎙️ Privacy Indicators
Hardware sensor monitoring directly in your instrument header:
- **Microphone**: Live detection of active microphone capture.
- **Camera**: Live detection of active video/webcam capture.
- Instant visual alerts whenever a sensor is engaged by any application.

### 🪟 Mini Speed Window
Need to keep an eye on network speed while gaming, streaming, or working?
- **Ultra-Compact Footprint**: A compact, floating instrument bar that stays pinned to your desktop.
- **Live Transfer Rates**: Continuous download and upload figures in your choice of units (`Mbps` or `MB/s`).
- **Camera & Mic Badges**: Hardware privacy indicators remain visible on the floating tile.
- **Interactive**: Drag anywhere on screen to reposition (position is automatically remembered). Click anytime to restore the main dashboard.

### 📌 Always on Top
Keep TRACE visible over full-screen IDEs, games, or browsers with a single toggle in the header or settings modal.

### 🎨 Themes & Accents
Match your Windows setup with 11 custom accent palettes (Electric Cyan, Cobalt Blue, Violet, Magenta, Crimson, Amber, Emerald, Mint, Cyberpunk, Sunset, Aurora) and 10 dark-mode background finishes (Midnight, Obsidian, Deep Ocean, Arctic Night, Cosmic, Ember, and more).

---

## Why TRACE?

Most network tools on Windows fall into two extremes:
1. **Network protocol analyzers** (like Wireshark) that flood your screen with thousands of packet rows and technical protocol headers when you just want to know what is downloading.
2. **Heavy electron or web dashboards** that consume hundreds of megabytes of memory and bundle unwanted security suites, background updaters, or cloud accounts.

**TRACE takes a different approach:**
It does one thing exceptionally well: make network throughput and active connections transparent, beautiful, and tactile, without turning your desktop into a server control room.

---

## Privacy by Design

TRACE is built on a strict **local-first, privacy-respecting architecture**.

### What TRACE Does NOT Do:
- ❌ **No Cloud Telemetry**: TRACE has zero cloud endpoints, zero analytics SDKs, and zero crash uploaders.
- ❌ **No Packet Payload Inspection**: TRACE never reads, parses, decrypts, or analyzes packet contents, message text, URLs, or HTTP headers.
- ❌ **No Persistent Activity Logging**: TRACE does not store persistent logs of your browsing habits, connection lists, remote IP addresses, or running application names. Periodic process-activity snapshots were permanently removed in the P0 privacy remediation.
- ❌ **No Traffic Modification**: TRACE does not act as a proxy, firewall, or packet filter; it cannot alter, redirect, or block network traffic.
- ❌ **No Threat Intelligence Feeds**: No external API calls are made to look up IP reputations or GeoIP coordinates.

### What TRACE Reads & Persists Locally:
- System network interface byte counters via Windows IP Helper (`GetIfTable2`).
- Active TCP/UDP connection state tables via Windows IP Helper (`GetExtendedTcpTable`, `GetExtendedUdpTable`).
- Windows Kernel Event Tracing for Windows (`TraceEvent` / `NT Kernel Logger`) network events (`TcpIp`/`UdpIp`) for PID-level byte attribution when elevated.
- Windows Privacy Sensor states for camera and microphone usage via Windows Runtime APIs.
- User visual preferences saved locally in `%LOCALAPPDATA%\TRACE\settings.json`.
- Essential local startup and error diagnostics in `%LOCALAPPDATA%\TRACE\app_activity.log` if unhandled initialization crashes occur.

---

## How It Works

TRACE utilizes a two-tier measurement architecture designed for accuracy, responsiveness, and minimal system overhead:

```text
SYSTEM-WIDE THROUGHPUT (Standard User)
┌──────────────────────────────┐
│  Windows Network Interfaces  │
└──────────────┬───────────────┘
               │  IP Helper API (GetIfTable2)
               ▼
┌──────────────────────────────┐
│  System-Wide Byte Counters   │
└──────────────┬───────────────┘
               │  Delta Rate Calculation (1 Hz)
               ▼
┌──────────────────────────────┐
│       TRACE Dashboard        │
└──────────────────────────────┘

PER-PROCESS ATTRIBUTION (Elevated Administrator)
┌──────────────────────────────┐
│      Windows Kernel ETW      │
│     (NT Kernel Logger)       │
└──────────────┬───────────────┘
               │  Kernel Network Events (TcpIp / UdpIp)
               ▼
┌──────────────────────────────┐
│    PID + Byte Attribution    │
└──────────────┬───────────────┘
               │  Process In-Memory Aggregation
               ▼
┌──────────────────────────────┐
│      Activity & Inspect      │
└──────────────────────────────┘
```

- **System-Wide Layer**: Measures raw byte deltas on the active physical network interfaces. This works under normal standard user permissions with zero system impact.
- **Kernel ETW Layer**: Subscribes to kernel-level transfer events to attribute exact byte volumes to individual Process IDs (PIDs). Because starting an `NT Kernel Logger` session accesses core OS telemetry, Windows requires Administrator elevation.
- **Measurement Reconciliation**: System-wide interface counters and kernel event streams operate at different network stack boundaries; while they track each other closely, minor timing and protocol wrapper variations between layers are natural and expected.

---

## Installation

TRACE is distributed as a standalone, self-contained Windows application (intended to be published on the [GitHub Releases](https://github.com/ShauryaByte/TRACE/releases) page). No installer or runtime setup is required.

1. Download the latest self-contained archive from the [Releases](https://github.com/ShauryaByte/TRACE/releases) page once published, or build directly from source.
2. Extract the archive to a folder of your choice (e.g., `C:\Tools\TRACE` or your desktop).
3. Run `TRACE.exe`.

> 💡 **Windows SmartScreen Note**: Because TRACE is an open-source project distributed directly without an expensive enterprise code-signing certificate, Windows SmartScreen may display an informational prompt on first launch. Click **More info** → **Run anyway**. You can inspect and build the full source code yourself to verify its integrity.

### Running as Administrator vs. Standard User

| Capability | Standard User | Administrator |
|---|:---:|:---:|
| System-Wide Download/Upload Speed | :white_check_mark: Yes | :white_check_mark: Yes |
| Live Waveform Visualization | :white_check_mark: Yes | :white_check_mark: Yes |
| Session Download / Upload Totals | :white_check_mark: Yes | :white_check_mark: Yes |
| Camera & Microphone Privacy Alerts | :white_check_mark: Yes | :white_check_mark: Yes |
| Mini Speed Window (Floating Tile) | :white_check_mark: Yes | :white_check_mark: Yes |
| Always-on-Top Window Pinning | :white_check_mark: Yes | :white_check_mark: Yes |
| Custom Themes & Accents | :white_check_mark: Yes | :white_check_mark: Yes |
| Connection List Inspection (Sockets) | :white_check_mark: Yes | :white_check_mark: Yes |
| **Real Per-Process Bandwidth Rates** | Limited / Activity Empty | :white_check_mark: **Full ETW Attribution** |

*When running unelevated, TRACE functions normally for all system throughput metrics and displays an honest notice in the Activity area explaining that per-process attribution requires Administrator rights.*

---

## First Run Walkthrough

1. **Launch `TRACE.exe`**: The dashboard opens in its default compact instrument layout.
2. **Check Live Telemetry**: Observe your real-time download and upload transfer rates and session totals.
3. **Explore Activity**: If running elevated, watch applications automatically populate with their individual download/upload bandwidth.
4. **Open Inspect**: Click the **Inspect** button (bottom right) to see grouped socket counts and technical endpoints.
5. **Try the Mini Window**: Click the **Mini** button in the top bar to collapse TRACE into a tiny desktop widget. Drag it to your preferred corner; click it anytime to return to the full dashboard.
6. **Customize Appearance**: Click the **Settings** gear icon to test different accent colors (Cyberpunk, Sunset, Emerald, etc.) or switch speed display from `Mbps` to `MB/s`.
7. **Pin to Desktop**: Click the pin icon in the title bar to keep TRACE floating above all active windows.

---

## Usage Guide

### Main Dashboard
- **Speed Cards**: Download (left) and Upload (right) throughput with dedicated units.
- **Waveform Area**: Live graphic pulse indicating current throughput load.
- **Session Totals**: Cumulative data transferred since TRACE was launched.
- **Signal Status**: Immediate network heartbeat (e.g. `System normal`).
- **Activity Section**: Live cards displaying active bandwidth-consuming applications.

### Inspect Diagnostics
- **Header Summary**: Total active sockets, unique remote endpoints, and current connection states.
- **Toggle View**: Switch between *Activity Groups* (by process name) and *All Connections* (raw socket table).
- **Endpoint Details**: Displays `PID`, `Protocol`, `Local Address`, `Remote Address`, and TCP connection state (`ESTABLISHED`, `LISTEN`, `TIME_WAIT`).

### Mini Speed Window
- **Draggable Tile**: Click and drag anywhere on the tile to position it anywhere across multiple monitors.
- **Automatic Persistence**: TRACE saves the exact coordinates of the Mini window in your settings.
- **Sensor Indicators**: A glowing dot indicates active microphone or camera access.
- **Restore**: A single click on the tile brings back the main dashboard.

---

## Limitations

For complete transparency, here are the technical boundaries of the current release:

- **Elevation Requirement**: Attributing individual bandwidth rates to specific Windows processes requires access to the Windows Kernel ETW `NT Kernel Logger` session, which Windows restricts to elevated (Administrator) processes.
- **Socket Table Scope**: Connection inspection utilizes standard Windows IP Helper connection tables (`GetExtendedTcpTable` / `GetExtendedUdpTable`). While all IPv4 and IPv6 traffic is fully measured in system throughput, some internal ephemeral IPv6 socket rows may not appear in the inspection table.
- **Not a Firewall or Antivirus**: TRACE does not block connections, filter content, or scan for malware.
- **Layer Discrepancies**: Network driver-level counters and application socket events capture data at slightly different boundaries of the Windows networking stack, which can result in minor, normal differences between total NIC bytes and aggregated socket sums.

---

## System Requirements

- **Operating System**: Windows 11 (64-bit, Version 22H2 / Build 22621 or newer; targeted at Windows SDK 10.0.26100.0)
- **Architecture**: x64 (Primary), ARM64 supported via publish profile
- **Runtime**: Self-contained (no separate .NET runtime installation needed for published releases)

---

## Building From Source

### Prerequisites
1. **Windows 11** with developer mode enabled.
2. [.NET 10.0 SDK](https://dotnet.microsoft.com/download) installed.
3. [Visual Studio 2022](https://visualstudio.microsoft.com/) (17.12 or newer) with:
   - *.NET Desktop Development*
   - *Windows App SDK C# Templates*

### Build Steps

1. **Clone the Repository**:
   ```powershell
   git clone https://github.com/ShauryaByte/TRACE.git
   cd TRACE
   ```

2. **Restore NuGet Packages**:
   ```powershell
   dotnet restore NetStats.csproj
   ```

3. **Build Debug**:
   ```powershell
   dotnet build NetStats.csproj -c Debug
   ```

4. **Build Release**:
   ```powershell
   dotnet build NetStats.csproj -c Release
   ```

5. **Publish Self-Contained x64 Release**:
   ```powershell
   dotnet publish NetStats.csproj -c Release /p:PublishProfile=win-x64
   ```
   The self-contained binary will be output to:
   `bin\Release\net10.0-windows10.0.26100.0\win-x64\publish\TRACE.exe`

---

## Author & Attribution

Developed with care by:

**Shaurya Singh**
- 📧 Email: [shauryaofficialx@gmail.com](mailto:shauryaofficialx@gmail.com)
- 🐙 GitHub: [@ShauryaByte](https://github.com/ShauryaByte)
- 💼 LinkedIn: [Shaurya Singh](https://www.linkedin.com/in/shauryasinghofficial)

---

## License

TRACE is open-source software licensed under the **[MIT License](LICENSE)**. You are free to inspect, modify, and distribute this software in accordance with the license conditions.
