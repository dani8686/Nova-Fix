# CHANGELOG — NOVA FIX 2.0

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/), project version 2.0.0.

## [2.0.0] — 2026-11-27

### Added

- Complete WPF application: App shell, MainWindow navigation, Dark/Light themes,
  16 module ViewModels + Views, custom SparklineControl, UiConverters,
  AppBootstrap composition root.
- Creator attribution in sidebar and Settings/About (Danial Mousavi, @dani.m7016).
- `app.manifest` (PerMonitorV2 DPI, asInvoker), `assets/icons/novafix.ico`.
- `data/windows-compatibility/compat-db.json` (Dell Precision 7520 note).
- GitHub Actions CI: restore → build x64 → 4 test suites → self-contained
  publish → artifacts (test-results + novafix-win-x64).
- Offline scripts: build-offline, test-offline, make-offline-feed,
  package-offline, offline-acceptance.
- Inno Setup installer script with creator metadata.
- Docs: ARCHITECTURE.md, DEVELOPER_GUIDE.md, offline help, models/packages READMEs.

### Fixed

- 20+ CI compile/test iterations: usings, X509Certificate2, registry-based
  service start type, FTS5 external-content sync, RepairViewModel ctor order,
  XAML CommandParameter/NavButton style, test escapes and SQLite pool cleanup.

## [2.0.0] — 2026-11-26

### Added

- Initial implementation of the NOVA FIX 2.0 solution architecture.
- Permanent project memory files (`PROJECT_MEMORY.md`, `PROJECT_STATUS.md`,
  `FEATURE_MATRIX.md`, `ARCHITECTURE_DECISIONS.md`, `CHANGELOG.md`,
  `CREATOR_INFORMATION.md`, `OFFLINE_DEPENDENCIES.md`, `TEST_RESULTS.md`).
- Centralized creator attribution (`CreatorInformation.cs`) and build metadata.
- Solution-wide build configuration (`Directory.Build.props`, `global.json`,
  `NuGet.config`).

[2.0.0]: https://github.com/dani8686/Nova-Fix
