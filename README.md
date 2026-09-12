# TRACE

TRACE is a lightweight native Windows network monitor that gives you a clear, real-time view of your connection. It tracks system-wide download and upload throughput as a standard user and, when launched with Administrator privileges, attributes live bandwidth consumption to the specific applications using your network.

Built with C# 13, .NET 10, and WinUI 3 (Windows App SDK), TRACE focuses on delivering immediate, tactile visibility into your throughput without the bulk of heavy diagnostic suites or web-based dashboards.

---

## Features

- **Live throughput**: Instant download and upload speeds with dynamic waveform visualization and session transfer totals.
- **Per-app attribution**: See which applications are actively using network bandwidth (requires Administrator privileges).
- **Inspect diagnostics**: Process-level socket and connection details, including PID, protocol, endpoints, and TCP state.
- **Privacy sensor alerts**: Live indicators showing active camera and microphone use.
- **Mini Speed Window**: Compact floating widget that displays current transfer rates and stays pinned to your desktop.
- **Always on top**: Pin the main instrument or mini widget above other windows while gaming, browsing, or working.
- **Customizable themes**: Multiple dark-mode finishes and accent palettes to suit your desktop setup.
- **Native Windows UI**: Clean, fluid interface built with WinUI 3.

---

## Interface

### Dashboard
The primary view showing real-time network throughput, an ambient waveform of current throughput load, and cumulative session transfer totals.

### Activity
Displays a live list of applications consuming bandwidth, broken down by download and upload transfer rates when running with Administrator privileges.

### Details
A technical view for inspecting active TCP and UDP connections with PID, protocol (IPv4/IPv6), local and remote addresses, ports, and connection states (`ESTABLISHED`, `LISTEN`, `TIME_WAIT`).

### Mini
A small floating instrument that stays out of your way. Drag it anywhere on your desktop to keep a continuous eye on live speeds. Clicking it restores the full dashboard.

---

## Privacy & Permissions

TRACE is designed to be local-first and transparent:

- **Operates locally**: TRACE runs entirely on your device and does not send telemetry, analytics, or usage data to external servers.
- **No payload inspection**: Network traffic is never intercepted, decrypted, or inspected for contents, URLs, or message bodies.
- **No traffic modification**: TRACE is an observer; it does not block, filter, or redirect network packets.
- **No periodic activity logging**: Process network activity is monitored in memory and not continuously written to disk.
- **Local configuration**: User preferences (such as window positions and theme selection) are saved locally in `%LOCALAPPDATA%\TRACE\settings.json`.
- **Diagnostics**: Unhandled application errors may be recorded locally in `%LOCALAPPDATA%\TRACE\app_activity.log` to aid troubleshooting.

### Administrator Permission
- **Standard User**: System-wide download/upload rates, session totals, connection inspection, hardware privacy indicators, and the Mini window work out of the box.
- **Administrator**: Attributing bandwidth to individual processes relies on Event Tracing for Windows (ETW kernel network events), which requires Administrator elevation. When run without elevation, system throughput remains fully functional while process attribution honestly displays an informational notice requiring Administrator privileges.

---

## Download

Releases will be published on the [GitHub Releases](https://github.com/ShauryaByte/TRACE/releases) page as they become available. You can also build TRACE directly from source.

---

## Requirements

- **Operating System**: Windows 11, Version 22H2 or newer
- **Architectures**: x64, ARM64, x86
- **Runtime**: Self-contained for standalone published builds

---

## Building from Source

### Prerequisites
- Windows 11
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- Visual Studio 2022 (with *.NET Desktop Development* and *Windows App SDK* workloads)

### Build Steps

1. **Clone the repository**:
   ```powershell
   git clone https://github.com/ShauryaByte/TRACE.git
   cd TRACE
   ```

2. **Restore dependencies**:
   ```powershell
   dotnet restore NetStats.csproj
   ```

3. **Build Release**:
   ```powershell
   dotnet build NetStats.csproj -c Release -r win-x64
   ```

4. **Publish self-contained executable**:
   ```powershell
   dotnet publish NetStats.csproj -c Release /p:PublishProfile=win-x64
   ```
   The self-contained binary will be generated under `bin\Release\net10.0-windows10.0.26100.0\win-x64\publish\TRACE.exe`.

---

## Documentation & Community

- [Contributing Guidelines](CONTRIBUTING.md) - How to report issues and contribute to TRACE.
- [Security Policy](SECURITY.md) - Responsible vulnerability reporting and security practices.
- [Code of Conduct](CODE_OF_CONDUCT.md) - Community standards and expectations.
- [License](LICENSE) - Distributed under the MIT License.

Developed by [Shaurya Singh](https://github.com/ShauryaByte).
