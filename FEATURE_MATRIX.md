# FEATURE MATRIX — NOVA FIX 2.0

Legend: **P** planned · **I** implemented · **T** implemented + tested · **—** not started
(Updated as work proceeds; never mark T without passing tests.)

| # | Module / Capability | Status | Notes |
|---|---|---|---|
| 1 | **System Overview** — live CPU/RAM/disk/GPU/battery/uptime dashboard, bounded charts, pause/resume | P | |
| 2 | **Deep Scanner** — hardware, Windows, software, storage inventory; per-probe failure isolation | P | |
| 3 | **AI Problem Detective** — evidence correlation, severity, confidence, citations | P | |
| 4 | **One-Click Repair** — DISM/SFC, Search, Update, Defender, Services, Network, Storage workflows | P | |
| 5 | **Performance Lab** — measurements + before/after optimization recommendations | P | |
| 6 | **Startup Manager** — Run keys, Startup folders, tasks; enable/disable/export/backup | P | |
| 7 | **Service Manager** — inspection, controlled modification, backup/rollback | P | |
| 8 | **Driver Intelligence** — HW IDs, DriverStore, INF validation/install, backup/rollback | P | |
| 9 | **Storage Guardian** — volumes, SMART, temp/large files, cleanup with confirmation | P | |
| 10 | **Windows Compatibility Center** — versioned offline compatibility DB + import packs | P | |
| 11 | **Rescue & Restore** — restore points, snapshots, change journal, rollback | P | |
| 12 | **Report Center** — TXT/HTML/JSON/CSV export, sanitized, local only | P | |
| 13 | **NOVA Autopilot** — Safe/Guided/Advanced modes, plan → approve → execute → verify | P | |
| 14 | **Offline Update Manager** — import/inspect/verify/install local packages | P | |
| 15 | **Local AI Settings** — model profile, resources, pause analysis | P | |
| 16 | **Settings & About** — themes, languages (en/fa RTL), intervals, About attribution | P | |
| — | Creator attribution in About/credits/installer/executable/README/docs | P | |
| — | 100% offline operation (no runtime networking anywhere) | P | |
| — | Local LLM bundled, CPU inference, graceful degradation | P | |
| — | Local RAG (SQLite FTS + vectors) with citations | P | |
| — | Offline update packages (ZIP/MSU/CAB/INF/EXE) with signature/hash checks | P | |
| — | Tests: unit / integration / offline / UI + CI pipeline | P | |
| — | x64 Release build + installer + portable build | P | |
