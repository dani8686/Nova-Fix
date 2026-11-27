# NOVA FIX — Developer Guide

## Prerequisites (build machine)

- Windows 10/11 x64
- .NET 8 SDK (8.0.4xx)
- Optional: Inno Setup 6 (installer), PowerShell 7

## Build

```powershell
# Online prep machine — one time:
.\scripts\make-offline-feed.ps1     # fills packages\offline

# Offline build machine:
.\scripts\build-offline.ps1
.\scripts\test-offline.ps1

# Full package (model staging + portable zip + installer):
.\scripts\package-offline.ps1

# Airplane-mode acceptance:
.\scripts\offline-acceptance.ps1
```

## Session protocol (anti-forgetting)

1. Read `PROJECT_MEMORY.md`, `PROJECT_STATUS.md`, `FEATURE_MATRIX.md`.
2. Read `CREATOR_INFORMATION.md` — creator identity is permanent.
3. Inspect existing code before editing; preserve working functionality.
4. Update status files before stopping.

## Creator attribution

Single source of truth:
`src/NOVA.FIX.Core/Branding/CreatorInformation.cs`
Never duplicate or fictionalize creator details elsewhere.

## Testing strategy

- **UnitTests** — pure logic: allowlist, deletion gate, reports, RAG, localization,
  version compare, autopilot safety.
- **IntegrationTests** — real WMI/perf probes on the Windows runner; assert
  isolated results, never exceptions.
- **OfflineTests** — spec §24 checks runnable without network.
- **UITests** — shell navigation + About attribution (view-model level).
