# PROJECT MEMORY — NOVA FIX 2.0

> **Anti-forgetting contract.** Read this file (and `PROJECT_STATUS.md`) before every
> development session. Never restart from scratch. Never lose creator identity.
> Never introduce mandatory online dependencies. Never replace real features with mocks.

## 1. Product vision

NOVA FIX is a complete, beautiful, intelligent, genuinely useful, **100% offline**
Windows diagnostic, repair, recovery and optimization environment.

- Product name: **NOVA FIX**
- Category: Windows Diagnostic, Repair, Performance Optimization and System Maintenance Software.
- Tagline: **Diagnose. Repair. Optimize. Verify.**
- Version: 2.0

## 2. Permanent creator identity (NEVER CHANGE)

| Field | Value |
|---|---|
| Creator / Developer | **Danial Mousavi** |
| Instagram | **@dani.m7016** |
| Email | **daniel.865559@gmail.com** |

Single source of truth: `src/NOVA.FIX.Core/Branding/CreatorInformation.cs`.
All components MUST read creator details from that one file. Do not duplicate
conflicting creator info anywhere. The coding agent is a development tool, not
the creator. Never substitute OpenAI, Microsoft, Dell, or any fictional entity.

Mandatory attribution locations: About page, credits, installer metadata,
executable metadata, README.md, PROJECT_STATUS.md, developer docs, release docs,
product information page, source-code project metadata.

The creator email is attribution only — it must never be inserted into ordinary
diagnostic reports or transmitted anywhere.

## 3. Absolute offline requirement

- No HTTP APIs, cloud AI, telemetry, analytics, remote config, CDN resources,
  internet fonts/icons/images, remote databases, online auth, license servers.
- Must launch and operate fully in Airplane Mode: first launch, setup, hardware
  detection, scanning, monitoring, diagnostics, AI inference, repair, reporting,
  localization, settings, help.
- No hidden network fallbacks. If local data is unavailable, work offline — never
  silently attempt a network request.
- Updates arrive only via locally imported signed update packages.
- Offline reference data (compatibility DB, driver catalog, knowledge base) must
  display its age/version honestly; never claim stale data is "latest".

## 4. Architectural decisions (summary — details in ARCHITECTURE_DECISIONS.md)

1. C# / .NET 8 / WPF / MVVM modular solution (`NOVA.FIX.sln`), projects under `src/`.
2. SQLite for diagnostic rules, event catalog, knowledge base (FTS), history.
3. Hybrid AI: deterministic rules + local LLM (llama.cpp GGUF, CPU-first, no GPU
   required) + local RAG with citations + evidence correlation.
4. AI has NO shell access. All execution goes through validated services and an
   allowlisted action registry with target/purpose/risk/precondition/verification/rollback.
5. Bundled baseline model required in installer; deterministic engine must remain
   fully operational when the model cannot load, with an honest explanation.
6. Reference hardware: Dell Precision 7520, 7th-gen Intel Core i7, 16 GB RAM,
   CPU inference must work. NVIDIA GPU must never be required.
7. Dark premium theme (#0B0F14 background, #35C9F0 cyan, #7C6CFF purple …) plus
   light theme; English + Persian (RTL).
8. Target: x64 Release, self-contained deployment, fully bundled resources.

## 5. Permanent restrictions (never violate)

- Never remove, replace or fictionalize the creator identity.
- Never introduce mandatory online dependencies or hidden network calls.
- Never claim a feature is complete unless implemented and tested.
- Never fabricate diagnostic values; unsupported sensors show honest "Unavailable".
- Never silently delete user data; cleanup requires exact paths/sizes + confirmation.
- Never disable critical services automatically; never install unsigned drivers.
- Never let the LLM execute arbitrary commands.
- Never describe offline reference data as the latest available.
- Keep `PROJECT_MEMORY.md`, `PROJECT_STATUS.md`, `FEATURE_MATRIX.md`,
  `ARCHITECTURE_DECISIONS.md`, `CHANGELOG.md`, `CREATOR_INFORMATION.md`,
  `OFFLINE_DEPENDENCIES.md`, `TEST_RESULTS.md` updated.

## 6. Session protocol

1. Read `PROJECT_MEMORY.md` + `PROJECT_STATUS.md` + `FEATURE_MATRIX.md`.
2. Inspect existing source before modifying any module.
3. Preserve working functionality; make the smallest justified change; run tests.
4. If context runs out, update the memory files before stopping.
