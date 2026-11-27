# ARCHITECTURE DECISIONS — NOVA FIX 2.0

| # | Decision | Rationale |
|---|---|---|
| AD-01 | **.NET 8 (LTS), C# 12, WPF, MVVM** | Stable, self-contained x64 publish, first-class Windows API access, no third-party UI framework needed. |
| AD-02 | **Modular solution `NOVA.FIX.sln`** with Domain / Core / Infrastructure / Diagnostics / AI / Repair / Optimization / Drivers / Storage / Security / Reporting / Localization / Recovery / App | Clear boundaries, independently testable engines, App contains only UI. |
| AD-03 | **No third-party MVVM toolkit** — small in-repo `ObservableObject`, `RelayCommand`, `AsyncRelayCommand`, `ViewModelBase` | Fewer dependencies = simpler offline feed; full control; unit-testable. |
| AD-04 | **SQLite (`Microsoft.Data.Sqlite`) with FTS5** for diagnostic rules, event catalog, knowledge base, history | Spec-mandated; parameterized queries; full-text search for RAG retrieval. |
| AD-05 | **Hybrid AI**: deterministic rules engine + local LLM (llama.cpp GGUF via P/Invoke) + lexical/vector RAG + evidence correlation | Rule-based alone is explicitly insufficient; LLM must degrade gracefully on low-RAM machines. |
| AD-06 | **CPU-first inference**, no GPU requirement; model tiers lightweight/balanced/advanced | Reference machine is Dell Precision 7520 (i7-7th gen, 16 GB). |
| AD-07 | **AI safety boundary**: LLM never gets shell access; proposals are structured objects executed only through the allowlisted action registry | Prevents arbitrary command execution; enables risk/rollback reporting. |
| AD-08 | **Process execution service** with fixed verbs, argument arrays (never string-concatenated shell), timeouts, cancellation, captured stdout/stderr, elevation detection | Security requirement; testable with fixtures. |
| AD-09 | **Every system query is an isolated result** (`QueryResult<T>` with success/failure per probe) | One failed probe must never abort a scan. |
| AD-10 | **Bounded monitoring buffers** (ring buffers) + configurable sampling interval | UI must stay responsive and memory usage flat. |
| AD-11 | **Creator attribution** centralized in `CreatorInformation.cs`, referenced by build props and About page | Anti-forgetting contract. |
| AD-12 | **Localization** via in-repo JSON resource dictionaries (`en`, `fa`) loaded at startup with RTL flow-direction switch | No satellite-assembly complexity; fully offline; easy to extend. |
| AD-13 | **Themes** as merged ResourceDictionaries (dark default, light optional) with the exact spec palette | Premium dark UI requirement + light theme option. |
| AD-14 | **Reports** generated to local folder only (TXT/HTML/JSON/CSV), sanitized (no keys/passwords/creator email) | Offline + privacy requirements. |
| AD-15 | **Offline Update Manager** imports local ZIP/MSU/CAB/INF/EXE packages, verifies signature + hash + version before install | Spec §2.3, §18. |
| AD-16 | **Change journal + snapshots** (startup, services, drivers) underpin Rescue & Restore rollback | Spec §17; every modification logged with undo data. |
| AD-17 | **CI = GitHub Actions windows-latest** for restore/build/test; offline runtime tests run with network disabled on a Windows machine via `scripts/offline-acceptance.ps1` | Sandbox cannot compile WPF; runners provide the toolchain. |
| AD-18 | **Model weights staged at packaging time** (`scripts/package-offline.ps1`), not committed to Git | Repo size policy; installer still ships a fully bundled model. |
