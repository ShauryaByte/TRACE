# Contributing to TRACE

Thank you for your interest in contributing to TRACE!

TRACE is an ambient, native Windows network throughput instrument built with **C#**, **.NET 10**, **WinUI 3 / Windows App SDK**, and **Windows Kernel ETW**.

Please take a few minutes to review these guidelines before submitting code or opening an issue.

---

## Core Principles

Every contribution must align with TRACE's foundational design principles:

1. **Local-First & Privacy-Respecting**: TRACE never collects, persists, or transmits user activity, browsing data, process lists, or network metrics. No cloud services, external analytics, or outbound telemetry may ever be introduced.
2. **Instrument, Not Control Panel**: TRACE is designed to feel like a high-precision, beautifully crafted physical instrument on your Windows desktop. We avoid cluttered UIs, complex multi-tier menus, and bloat.
3. **Honest Capabilities**: TRACE measures throughput rates and connection metadata. It does not inspect packet payloads or pretend to be a security scanner, firewall, or antivirus suite.
4. **Reliability & Performance**: Measurement loops must be lightweight, efficient, and resilient against missing permissions or missing kernel events.

---

## Development Prerequisites

To build and run TRACE locally from source, you will need:

- **Operating System**: Windows 11 (build 22000 or higher recommended, targeted at 10.0.26100.0)
- **SDK**: [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- **IDE**: [Visual Studio 2022](https://visualstudio.microsoft.com/) (v17.12+ recommended) with:
  - *.NET Desktop Development* workload
  - *Windows App SDK C# Templates*
  - Alternatively: Visual Studio Code with the C# Dev Kit
- **Elevation**: Administrator privileges are required if you want to debug or test the kernel ETW per-process telemetry path. Unelevated testing is equally important.

---

## Building and Testing Locally

1. **Clone the repository**:
   ```powershell
   git clone https://github.com/ShauryaByte/TRACE.git
   cd TRACE
   ```

2. **Restore dependencies**:
   ```powershell
   dotnet restore NetStats.csproj
   ```

3. **Build in Debug**:
   ```powershell
   dotnet build NetStats.csproj -c Debug
   ```

4. **Build in Release**:
   ```powershell
   dotnet build NetStats.csproj -c Release
   ```

5. **Run the built executable**:
   ```powershell
   # Debug
   .\bin\Debug\net10.0-windows10.0.26100.0\win-x64\TRACE.exe

   # Release
   .\bin\Release\net10.0-windows10.0.26100.0\win-x64\TRACE.exe
   ```

6. **Publish Self-Contained Bundle** (optional):
   ```powershell
   dotnet publish NetStats.csproj -c Release /p:PublishProfile=win-x64
   ```

---

## Pull Request Guidelines

Before opening a pull request, please ensure:

- [ ] The solution builds cleanly with **0 errors and 0 warnings** in both `Debug` and `Release` configurations.
- [ ] You have tested the application in **both** unelevated (normal user) and elevated (Administrator) modes.
- [ ] No background logs, persistent activity files, or personal debug traces are being written to disk.
- [ ] No network calls, telemetry SDKs, or cloud dependencies have been added.
- [ ] Existing UI branding, styling tokens, and window ergonomics remain intact.
- [ ] Your code follows standard C# 13 and .NET naming conventions and WinUI MVVM patterns.

### Branch Naming Convention

- `fix/<issue-description>` for bug fixes
- `feature/<feature-name>` for approved enhancements
- `docs/<doc-change>` for documentation updates

---

## Questions and Discussions

If you have questions about architecture or want to propose a substantial change, feel free to open a [GitHub Discussion](https://github.com/ShauryaByte/TRACE/discussions) or submit a feature proposal issue first so we can collaborate on design before you write code.
