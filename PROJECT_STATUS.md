# PROJECT STATUS — NOVA FIX 2.0

**Product:** NOVA FIX 2.0 — Offline Windows Diagnostic, Repair & Optimization Suite
**Creator:** Danial Mousavi (Instagram @dani.m7016, email daniel.865559@gmail.com)
**Last updated:** 2026-11-26

## Current phase

**Phase 4 complete — first fully green CI (2026-11-27).**

- All 16 src projects + App compile on GitHub Actions `windows-latest`.
- All 4 test suites pass: Unit, Integration, Offline, UI (view-model).
- Self-contained win-x64 publish artifact produced (~112 MB).
- 20+ CI fix rounds resolved compile/API/XAML/test issues (see CHANGELOG).

Remaining before release: staged GGUF model in packaging, Inno Setup run on a
Windows machine, full airplane-mode acceptance via `scripts/offline-acceptance.ps1`,
memory-doc final pass, FEATURE_MATRIX evidence fill.

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

1. ✅ Solution builds green in CI (run 45920603634, branch `arena/938745a0-nova-fix`)
2. ✅ All 16 module ViewModels + Views + themes + About attribution
3. ✅ Unit/Integration/Offline/UI test suites passing in CI
4. ⬜ Stage bundled GGUF (`scripts/package-offline.ps1`) on a Windows prep machine
5. ⬜ Inno Setup installer build + install verification
6. ⬜ Airplane-mode acceptance run (spec §24) with `scripts/offline-acceptance.ps1`
7. ⬜ FEATURE_MATRIX evidence fill from TEST_RESULTS; final memory-doc pass
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
