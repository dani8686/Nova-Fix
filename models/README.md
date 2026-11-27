# Model weights (staged, not committed)

The bundled baseline GGUF is staged by `scripts/package-offline.ps1`:

| Tier | File | Target RAM |
|---|---|---|
| Lightweight | `novafix-lite.gguf` | ~1–2 GB |
| Balanced (default) | `novafix-balanced.gguf` | ~2–4 GB |
| Advanced | `novafix-advanced.gguf` | ~4–6 GB |

Git policy: weights are never committed (size). The installer always ships one.

Reference machine: Dell Precision 7520, i7-7th-gen, 16 GB RAM — CPU inference
(GpuLayerCount=0) must work; NVIDIA GPU is never required.
