# NOVA FIX 2.0

**Diagnose. Repair. Optimize. Verify.**

NOVA FIX is a complete, professional, **100% offline** Windows desktop application
for system diagnostics, repair, performance optimization, recovery and maintenance.
It combines a premium dark dashboard with an intelligent, evidence-driven local AI
diagnostics center — with **no internet connection, no API key, no subscription,
no account, and no cloud dependency** at runtime.

---

## Creator & attribution

> **Created and Developed by Danial Mousavi**
>
> - **Instagram:** [@dani.m7016](https://instagram.com/dani.m7016)
> - **Email:** [daniel.865559@gmail.com](mailto:daniel.865559@gmail.com)

This attribution is a permanent part of the product specification. It appears in
the About page, credits, installer metadata, executable metadata, this README,
`PROJECT_STATUS.md`, developer documentation, release documentation, the product
information page, and source-code project metadata. The single source of truth in
code is [`src/NOVA.FIX.Core/Branding/CreatorInformation.cs`](src/NOVA.FIX.Core/Branding/CreatorInformation.cs).

---

## Primary modules

| # | Module | What it does |
|---|---|---|
| 1 | System Overview | Live CPU / RAM / disk / GPU / battery dashboard with real charts |
| 2 | Deep Scanner | Full hardware, Windows, software and storage inventory |
| 3 | AI Problem Detective | Evidence-based problem correlation with confidence & citations |
| 4 | One-Click Repair | DISM/SFC, Search, Update, Defender, Services, Network, Storage workflows |
| 5 | Performance Lab | Real measurements and before/after optimization |
| 6 | Startup Manager | Run keys, Startup folders, scheduled tasks — backup & restore |
| 7 | Service Manager | Inspection and controlled, reversible service changes |
| 8 | Driver Intelligence | Hardware IDs, DriverStore, offline INF install/rollback |
| 9 | Storage Guardian | Volumes, SMART, temp/large files, safe cleanup |
| 10 | Windows Compatibility Center | Versioned offline compatibility database |
| 11 | Rescue & Restore | Restore points, snapshots, change journal, rollback |
| 12 | Report Center | TXT / HTML / JSON / CSV reports — local export only |
| 13 | NOVA Autopilot | Guided Safe / Guided / Advanced automation with approval gates |
| 14 | Offline Update Manager | Import & verify local update packages (ZIP/MSU/CAB/INF/EXE) |
| 15 | Local AI Settings | Model profile, resource limits, pause analysis |
| 16 | Settings & About | Themes, English/Persian RTL, intervals, creator attribution |

## Technology

- C# 12 · .NET 8 · WPF · MVVM · SQLite (FTS5) · CIM/WMI · Performance Counters ·
  Event Log · PowerShell (allowlisted verbs) · llama.cpp local inference

## Building

```powershell
# Offline build (restore from the bundled feed only):
.\scripts\build-offline.ps1

# Tests:
.\scripts\test-offline.ps1

# Full package (model staging + installer + portable zip):
.\scripts\package-offline.ps1
```

See [`OFFLINE_DEPENDENCIES.md`](OFFLINE_DEPENDENCIES.md) for the complete
dependency manifest and [`docs/`](docs/) for developer documentation.

## Project memory

Before any development session read:

- [`PROJECT_MEMORY.md`](PROJECT_MEMORY.md) — vision, creator identity, offline rules
- [`PROJECT_STATUS.md`](PROJECT_STATUS.md) — current phase
- [`FEATURE_MATRIX.md`](FEATURE_MATRIX.md) — implemented vs planned
- [`ARCHITECTURE_DECISIONS.md`](ARCHITECTURE_DECISIONS.md)
- [`CHANGELOG.md`](CHANGELOG.md)
- [`CREATOR_INFORMATION.md`](CREATOR_INFORMATION.md)
- [`OFFLINE_DEPENDENCIES.md`](OFFLINE_DEPENDENCIES.md)
- [`TEST_RESULTS.md`](TEST_RESULTS.md)

## License / offline notice

NOVA FIX performs **no** runtime networking. All AI inference, knowledge
retrieval, diagnostics and reporting happen on the local machine. Update
packages are imported manually from local media and verified before installation.
