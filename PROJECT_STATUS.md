# PROJECT STATUS — NOVA FIX 2.0

**Product:** NOVA FIX 2.0 — Offline Windows Diagnostic, Repair & Optimization Suite
**Creator:** Danial Mousavi (Instagram @dani.m7016, email daniel.865559@gmail.com)
**Last updated:** 2026-11-26

## Current phase

Phase 1–3 complete: environment inspection, solution architecture scaffold,
permanent project memory and creator metadata.

## Environment notes (important for future sessions)

- Development sandbox: Linux, **no local .NET SDK**, NuGet/Microsoft CDNs blocked.
- GitHub (`github.com`, `api.github.com`, `codeload.github.com`) **is** reachable.
- Compile/test strategy: GitHub Actions `windows-latest` runner restores NuGet,
  builds `NOVA.FIX.sln` (x64 Release) and runs the test suites. Local scripts in
  `scripts/` reproduce the same steps on a Windows machine.
- Runtime offline rule applies to the **product**, not to CI: the product must
  never perform network calls; CI restore happens at build time only.

## Completed

- [x] Repository initialized on branch `arena/938745a0-nova-fix`
- [x] `PROJECT_MEMORY.md`, `PROJECT_STATUS.md`, `CREATOR_INFORMATION.md`,
      `FEATURE_MATRIX.md`, `ARCHITECTURE_DECISIONS.md`, `CHANGELOG.md`,
      `OFFLINE_DEPENDENCIES.md`, `TEST_RESULTS.md`
- [x] Solution-wide build props, global.json, NuGet.config, .gitignore
- [x] Creator attribution encoded in build metadata (`Company`, `Product`, `Authors`, `Copyright`)

## In progress / next

1. `NOVA.FIX.sln` + all `src/` projects (Domain, Core, Infrastructure, Diagnostics,
   AI, Repair, Optimization, Drivers, Storage, Security, Reporting, Localization,
   Recovery, App)
2. Design system (themes, icons, fonts) + navigation shell
3. Hardware/Windows inventory engines
4. Performance monitoring
5. Diagnostic rules engine + bundled rule database
6. Local AI (llama.cpp binding, RAG, evidence correlation)
7. Repair workflows + allowlisted action registry
8. Startup/Service/Driver/Storage modules
9. Rescue & Restore, Offline Update Manager, Autopilot, Report Center
10. Localization (en/fa RTL), Settings, About page
11. Tests (unit/integration/offline/UI) + CI workflow + installer packaging

## Known limitations

- The bundled LLM model weights cannot be committed to Git (size policy). The
  offline package script `scripts/package-offline.ps1` stages the GGUF model
  into `models/` during installer assembly; CI verifies the loader against a
  tiny fixture model and the deterministic engine always works without it.
