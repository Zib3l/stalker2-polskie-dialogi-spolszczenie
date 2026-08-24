# S.T.A.L.K.E.R. 2: Heart of Chornobyl — Polish dialogue (Polish voice mod)

Replaces the English/Ukrainian dialogue lines in S.T.A.L.K.E.R. 2 with a **Polish voice-over**,
using existing [GameReader](https://gamereader.pl) recordings (author: Rafko) — a free overlay
that reads game subtitles aloud.

**This repo is the project's primary knowledge base**: full mappings, the data schema, build
and deployment instructions, and a list of the pitfalls that actually corrupted results at
earlier stages.

*Wersja polska: [README.md](README.md)*

> ### ⛔ Before you build — read [docs/ZNANE-BLEDY.md](docs/ZNANE-BLEDY.md)
>
> Known, unresolved issue: the container produced by `UnrealReZen` can fail to register its
> packages (`NumPackages=0` in the game log). When that happens the engine loads the original
> asset headers from the game and takes only the raw audio bytes from the mod — so **longer
> Polish lines get truncated** exactly where the original recording ended. Check `Stalker2.log`
> after your first launch.

---

## Status: release 1.2

| | |
|---|---:|
| Normal spoken lines in the game | **19,071** |
| Covered — **English** variant | **17,604 (92.3%)** |
| Covered — **Ukrainian** variant | **17,562 (92.1%)** |
| Still to be recorded | **1,383 unique texts** |
| GameReader recordings in use | 15,871 of 17,387 (91%) |

Cutscenes are **not covered** — a separate, postponed topic.

> Every figure comes from a probe that reads bytes out of the game's paks (`RIFF` at the offset
> from `BulkData`), not from flags in helper files. Methodology: [docs/PUŁAPKI.md](docs/PUŁAPKI.md).

---

## Which variant to pick

**The English variant is recommended** — it covers 42 more lines.

Not because it patches better. The game ships separate voice data per language and the two are
**not equally complete**: in 42 events the Ukrainian media entry carries a `DebugName` belonging
to a *different* SID — the Ukrainian slot holds another line's recording. The patcher must skip
those, because overwriting them would break that other dialogue. This is a defect of the base
game and cannot be fixed from the mod side.

The Ukrainian variant makes sense if you want to leave the English dub untouched.

**Never install both at once.**

---

## Installation

1. Download **one** variant.
2. Copy the three files (`.pak`, `.ucas`, `.utoc`) into:
   ```
   <game>/Stalker2/Content/Paks/~mods/
   ```
3. In game, set the voice language to match the variant (English / Ukrainian). Leave subtitles
   set to Polish.

To uninstall, delete those three files.

**What this does NOT do:** it does not generate new voice-over (it reuses GameReader
recordings — you need them yourself, e.g. from one playthrough with GameReader running), it
does not cover cutscenes, and it does not modify the game executable.

---

## Documentation

| Document | Contents |
|---|---|
| [JAK-AKTUALIZOWAC-I-WDRAZAC.md](docs/JAK-AKTUALIZOWAC-I-WDRAZAC.md) | full pipeline, post-build checks, what to do after a game update |
| [SCHEMAT-DANYCH.md](docs/SCHEMAT-DANYCH.md) | data model, column reference, how to join the tables |
| [PUŁAPKI.md](docs/PUŁAPKI.md) | **15 mistakes that actually corrupted results** — read before analysing |
| [architecture.md](docs/architecture.md) | how the game stores audio, how the splice works |
| [ZNANE-BLEDY.md](docs/ZNANE-BLEDY.md) | open problems, incl. `NumPackages=0` |
| [unrealrezen-build.md](docs/unrealrezen-build.md) | how to build the **patched** UnrealReZen |

---

## Data

The repo ships **two master tables** instead of a dozen near-duplicate CSVs. Everything the
build consumes is **generated** from them — one source of truth, no silent drift.

| File | Rows | Cols | Unit |
|---|---:|---:|---|
| `data/MASTER_SIDS.csv` | 33,528 | 52 | one dialogue line in the game |
| `data/MASTER_RECORDINGS.csv` | 17,387 | 31 | one physical GameReader recording |
| `data/SPEAKER_VOICE_MAPPING.csv` | — | — | character code → name → suggested voice profile |

```bash
python scripts/analysis/make_build_inputs.py --audio-dir "<folder with output1 (N).ogg>"
```

`MASTER_SIDS.csv` answers everything through its `OverallStatus` column:

| Status | Count | Meaning |
|---|---:|---|
| `COVERED_IN_MOD` | 17,629 | Polish voice-over present |
| `NO_VOICE_ASSET_IN_GAME` | 7,973 | the text exists, but the game ships no audio to replace |
| `NON_VERBAL_OUT_OF_SCOPE` | 4,926 | stage directions, sounds |
| `OPEN_NEEDS_NEW_RECORDING` | 1,442 | needs recording |
| `CUTSCENE_OUT_OF_SCOPE` | 1,405 | cutscene |
| `INTERNAL_MISMATCH` | 129 | media point at a different SID |
| `OPEN_RECORDING_EXISTS` | 12 | recording exists, target unreachable |
| `OPEN_BUILD_FAILED` | 9 | build failed |
| `OPEN_REUSE_POSSIBLE` | 3 | coverable by reuse |

`MASTER_RECORDINGS.csv` does the same from the recording side (`UsageStatus`), with a SHA-1 of
every audio file. All 17,387 recordings are **byte-unique** — duplicates exist only at the text
level.

---

## Want to help?

What is missing most is **1,383 recordings**. Filter:

```
MASTER_SIDS.csv  ->  OverallStatus = OPEN_NEEDS_NEW_RECORDING
```

Profile: median 78 characters, 53 distinct speakers, mostly long one-off lines. The repeatable
town chatter ("Cześć.", "Na razie!", "Czego chcesz?") is **already covered** — there is no
economy of scale left here.

Open items that need a decision rather than work:

- **22 stage-direction cases** — the official text is `(umiera)` ("dies") while the recording
  says "Umieram..." ("I'm dying..."). That is not the same content. None of them shipped.
  Filter: `NeedsEditorialDecision = True`.
- **Cutscenes** — pipeline experimental and broken, see [ZNANE-BLEDY.md](docs/ZNANE-BLEDY.md).

---

## Building from source

```bash
python scripts/analysis/make_build_inputs.py --audio-dir "<...>"
dotnet run --project scripts/BatchEncoder -c Release        # ogg -> wav -> wem (both variants)
dotnet run --project scripts/FullPatcher  -c Release -- EN  # splice into game assets (EN or UA)
<patched>/UnrealReZen.exe --content-path build/ModOutput_EN ...
```

Or everything in one go: `.\build.ps1 -Variant EN` / `.\build.ps1 -Variant UA`.

The pipeline patches **all three populations** (MAIN + EXTRA via `SwitchContainerLeaves`
+ RECOVERED) — since 2026-08-24 the full release-1.2 coverage builds from this repo:
**17,621 lines EN / 17,579 UA** (the difference is 42 defective Ukrainian slots in the base
game, skipped via `build/UA_SKIP_SIDS.txt`).

Full instructions with verification steps:
[JAK-AKTUALIZOWAC-I-WDRAZAC.md](docs/JAK-AKTUALIZOWAC-I-WDRAZAC.md).

> **Most common mistake:** using the stock UnrealReZen instead of the patched build. It does not
> error out — it just produces a container roughly 3× too large that does not reference the
> game's own data. Check: `grep -c "unsupported version 8" <log>` must return **0**.

---

## License

TODO — not determined. This repo contains original code written for this project, alongside
dependencies on third-party tools (CUE4Parse, UnrealReZen, Wwise) under their own licenses.
Do not assume any license until this is clarified.

The GameReader recordings used by this pipeline are owned by a third party (author: Rafko,
[gamereader.pl](https://gamereader.pl)) and provided for non-commercial use within GameReader.
This project is not affiliated with it. If you want to use this pipeline or its results
**commercially**, contact Rafko and obtain his permission first — do not assume you have that
right just because you have technical access to these recordings.

The dialogue texts come from the game's official localisation (GSC Game World) and are included
solely so the mapping can be reproduced and verified.
