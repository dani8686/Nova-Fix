# NOVA FIX — Architecture Guide

## Solution layout

```
NOVA.FIX.sln
src/
  NOVA.FIX.Domain/          Pure models + contracts (no dependencies)
  NOVA.FIX.Core/            CreatorInformation, MVVM, DI, logging, settings, paths
  NOVA.FIX.Infrastructure/  SQLite, safe ProcessRunner, shims
  NOVA.FIX.Diagnostics/     WMI probes, performance monitor, DeepScanner, rules
  NOVA.FIX.AI/              EvidenceCorrelator, SqliteKnowledgeBase, RagService,
                            LlamaCppLanguageModel (LLamaSharp / llama.cpp, CPU)
  NOVA.FIX.Repair/          ActionRegistry (allowlist), ActionVerbs, RepairExecutor
  NOVA.FIX.Optimization/    StartupManager, ServiceManager, PerformanceLab
  NOVA.FIX.Drivers/         DriverIntelligence (INF parse, versions, signatures)
  NOVA.FIX.Storage/         StorageGuardian (volumes, disks, SMART, cleanup list)
  NOVA.FIX.Security/        (reserved: signature/elevation helpers)
  NOVA.FIX.Reporting/       TXT/HTML/JSON/CSV + Sanitize
  NOVA.FIX.Localization/    en/fa JSON dictionaries, RTL
  NOVA.FIX.Recovery/        RescueManager (restore points, snapshots, journal)
  NOVA.FIX.Updates/         OfflineUpdateManager (import/inspect/verify/install)
  NOVA.FIX.Autopilot/       AutopilotEngine (plan → approve → execute → verify)
  NOVA.FIX.App/             WPF shell, 16 module pages, themes, About
tests/
  NOVA.FIX.UnitTests / IntegrationTests / OfflineTests / UITests
data/                       Bundled rules, knowledge, localization, catalogs
```

## Safety architecture

```
AI / user proposal (RepairProposal, action id)
        │
        ▼
ActionRegistry ──── rejects unknown ids
        │
        ▼
RepairExecutor ──── approval gate, parameter validation, critical-service deny list
        │
        ▼
ActionVerbs ─────── fixed executables + argument arrays (never shell strings)
        │
        ▼
ProcessRunner ───── timeout, cancellation, captured output
        │
        ▼
change journal + action history (SQLite) → Rescue & Restore rollback
```

The LLM never touches any layer below `RepairProposal`.

## Offline data flow

- `data/diagnostic-rules/rules.json` → DeepScanner deterministic rules
- `data/knowledge/articles.json` → SQLite FTS5 `knowledge.db` (RAG)
- `data/localization/{en,fa}.json` → LocalizationService
- `data/windows-compatibility/compat-db.json` → Compatibility Center (shows build date)
- `models/novafix-*.gguf` → LlamaCppLanguageModel (staged at packaging)

## AI honesty rules

| Output | Label |
|---|---|
| Rule engine finding | `IsAiInterpreted=false`, "rule-based inference" |
| LLM finding | `IsAiInterpreted=true`, "probabilistic interpretation" + model profile |
| RAG without model | `HonestFallbackNote` set, citations are passages |
| Missing model | `LoadError` explains limitation; deterministic engine unaffected |
