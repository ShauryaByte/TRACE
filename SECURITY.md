# Security Policy

## Reporting a Vulnerability

Security and user privacy are foundational priorities for TRACE. If you discover a security vulnerability or potential privacy exposure in TRACE, please report it responsibly.

**Please do not report security vulnerabilities through public GitHub issues.**

Instead, report vulnerabilities privately by emailing:

**shauryaofficialx@gmail.com**

Please include:
- A descriptive subject line, for example: `[SECURITY] TRACE - Brief description of issue`
- Detailed steps to reproduce the behavior or a minimal proof-of-concept
- Affected TRACE version or commit hash
- The Windows build and architecture on which the issue was reproduced
- Any potential impact on system stability, process isolation, or user privacy

Please allow reasonable time for review. Response times may vary depending on severity and circumstances.

---

## Scope & Architectural Model

TRACE is an **ambient, local-first Windows network instrument**. When evaluating potential security or privacy concerns, keep the following architectural boundaries in mind:

- **Local-First**: TRACE runs entirely on your local machine. It contains zero cloud endpoints, zero analytics frameworks, zero telemetry uploaders, and zero external background network services.
- **Zero Traffic Payload Inspection**: TRACE reads network throughput metrics and connection table metadata. It never captures, inspects, parses, decrypts, or stores application payload data, URLs, cookies, or packet bodies.
- **No Persistent Activity History**: TRACE does not store persistent logs of running processes, per-process bandwidth histories, connection lists, or remote IP addresses.
- **Kernel ETW Session**: Per-process bandwidth attribution uses a local Windows Event Tracing for Windows (`TraceEvent`) kernel session (`NT Kernel Logger`). This requires elevated (Administrator) privileges to establish. TRACE only listens for network event metadata (`TcpIp`/`UdpIp` transfer sizes and PIDs) to aggregate rates in memory.

---

## Non-Goals & Out-of-Scope Behaviors

TRACE is **not** designed or marketed as a cybersecurity, firewall, or threat-defense product:

- TRACE does not block, filter, or modify network packets or socket traffic.
- TRACE does not detect malware, trojans, C2 beacons, or suspicious connections.
- TRACE does not perform DNS reputation lookups, IP threat intelligence feeds, or GeoIP enrichment.
- Unelevated instances cannot establish kernel ETW sessions by design; falling back to system-wide network interface counters is expected behavior, not a privilege vulnerability.

---

## Supported Versions

Only the latest release from the repository is actively supported with security patches:

| Version | Supported |
|---|:---:|
| Latest Release (`main` branch) | :white_check_mark: |
| Older Versions | :x: |
