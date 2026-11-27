# OFFLINE DEPENDENCIES — NOVA FIX 2.0

This document lists everything the product needs **at runtime** (must be bundled)
and everything needed **at build time** (reproducible offline feed).

## Runtime (bundled with installer — zero network access)

| Dependency | Purpose | Bundled where |
|---|---|---|
| .NET 8 Desktop Runtime (self-contained) | App host | publish/ (self-contained) |
| `Microsoft.Data.Sqlite` + native SQLite | rules/event catalog/knowledge/history DBs | app publish |
| llama.cpp native library (`llama.dll`) | local LLM inference | app publish |
| Baseline GGUF model (balanced tier) | local AI | `models/` inside installer |
| Diagnostic rules DB | deterministic engine | `local/novafix.db` |
| Knowledge base (FTS) + embeddings index | RAG | `local/knowledge.db` |
| Event catalog, repair definitions, driver catalog, hardware IDs, compatibility DB | diagnostics | `data/` → `local/` |
| Offline documentation (help) | in-app help | `docs/offline/` |
| Fonts (incl. Vazirmatn for Persian), icons, themes, images | UI | `assets/` |
| Localization dictionaries (`en`, `fa`) | UI language | app resources |

## Build time (offline feed assembly — done once on a connected machine)

Packages captured into `packages/offline/` by `scripts/make-offline-feed.ps1`:

- Microsoft.Data.Sqlite / SQLitePCLRaw.*
- Microsoft.NET.Test.Sdk, xunit, xunit.runner.visualstudio, coverlet.collector
- (any additional packages pinned in `Directory.Packages.props` if introduced)

Build from the complete source package without internet:

```powershell
# On a Windows machine with .NET 8 SDK installed:
.\scripts\build-offline.ps1        # restores ONLY from packages/offline
.\scripts\test-offline.ps1         # runs all test suites
.\scripts\package-offline.ps1      # stages model + data, builds installer & portable zip
```

## Honesty rules

- The offline compatibility/driver/knowledge databases display their **build date
  and version** in the UI. They are never described as "latest".
- If the bundled model cannot load (resource constraints), the app states the
  limitation clearly and the deterministic engine keeps working. A rule-based
  answer is never presented as LLM output.
