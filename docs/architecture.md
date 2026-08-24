# Architecture

```
GameReader (your own PL lektor audio, not included)
        +
data/FINAL_lektor_mapping.csv (included — SID -> text -> audio filename -> in-game MediaId)
        ↓
Audio Encoding     (scripts/BatchEncoder → ffmpeg → Wwise)
        ↓
Wwise WEM          (WwiseProject, via WwiseConsole)
        ↓
Bulk Patching      (scripts/FullPatcher)
        ↓
UnrealReZen        (repack to IoStore — see docs/unrealrezen-build.md)
        ↓
LektorMainOnly_P mod
```

## Why data/FINAL_lektor_mapping.csv is the key artifact

This file is what makes the rest of the pipeline runnable by someone who isn't the original
author. Each row ties one in-game dialogue line (`SID`) to:

- `Text` — the official Polish subtitle text (used to identify the line for a human reviewer).
- `Audio1` / `Audio2` — the filenames (not full paths) of GameReader's own two takes for that
  line, e.g. `output1 (1234).ogg`. You supply the actual files yourself (see `gameReaderAudioDir`
  in `config.json`) — only `Audio1` is used by the pipeline.
- `EnglishMediaId` / `UkrainianMediaId` — the real Wwise media IDs inside the game's own
  SoundBanks that this line's audio currently occupies. This is what lets `FullPatcher` find the
  *exact* bytes to overwrite without needing to re-derive it from the game files itself.
- `MatchType` / `Score` — `exact` (verbatim text match against GameReader's own line list) or
  `fuzzy` (bigram/Dice similarity ≥ 0.85, for lines where GameReader's TTS-pronunciation text
  differs slightly from the game's own subtitle text).

**Updated 2026-08-24 (release 1.2).** The game has **19,071** normal spoken dialogue lines (audio byte-verified, cutscenes and non-verbal cues excluded); current builds cover **17,604 of them in the English slot (92.3%) and 17,562 in the Ukrainian slot (92.1%)**. The mapping shipped in this repo (`FINAL_lektor_mapping.csv`, 16,402 rows) is the MAIN-build population; later builds add more via `SwitchContainerLeaves` support. Of the 1,467 uncovered lines, **1,383 unique recordings are genuinely missing** (the earlier `~21,040` / `78%` / `1,675` / `1,397` figures are superseded). The remainder are either
lines GameReader hasn't recorded yet, or lines driven by the game's cutscene/Sequencer system
rather than the standard per-line dialogue events this pipeline targets (see "Cutscenes" below).

## Stages

### 1. Dialogue Mapping (already done — `data/FINAL_lektor_mapping.csv`)

Not something you need to run. Kept reproducible via `scripts/mapping-reference/` (see its own
note below) for anyone who wants to verify or extend the mapping — e.g. after a game patch adds
new lines, or once GameReader records more of the missing ~22%.

### 2. Audio Encoding — `scripts/BatchEncoder`

- **Input:** `data/FINAL_lektor_mapping.csv` + your own GameReader audio folder
  (`gameReaderAudioDir` in `config.json`).
- **Process:** for every row with a resolvable `Audio1` file, converts it `.ogg → .wav` via
  `ffmpeg` (in parallel), then runs `WwiseConsole convert-external-source` once for the whole
  batch to produce `.wem` files.
- **Output:** `WwiseProject/full_batch_wem/Windows/*.wem`, and `build/FULL_BATCH_wem_mapping.csv`
  (the SID → MediaId → wem-filename join table `FullPatcher` consumes next).

### 3. Wwise WEM — `WwiseProject/LektorProject`

Only the Wwise project **source** (settings/structure) is in this repo. All generated banks,
converted audio, and the tool's own cache are build output — never commit them (see
`.gitignore`).

### 4. Bulk Patching — `scripts/FullPatcher`

- **Input:** the original game's `Paks` (read-only, path from `config.json` → `gameDir`), a
  `.usmap` you generate from your own game install (see README "What you need to prepare"), and
  the `.wem` files from stage 2.
- **Process:** for each row, resolves the line's real `VO_<...>_<SID>.uasset` event asset, finds
  its English-language media entry by `MediaId`, locates that media's exact byte range inside the
  package's `BulkDataMap`, and splices in the new `.wem` — resizing and shifting any
  later-positioned entries in the same file to stay consistent. See the in-code comments in
  `Program.cs` for the exact binary layout (`FBulkDataMapEntry`, 32 bytes, `SerialOffset`/
  `SerialSize` fields).
- **Output:** `build/ModOutput/` — a patched content tree mirroring the game's own folder
  structure, ready to repack. Also `build/FULL_PATCH_report.csv` (per-row status, useful for
  debugging any misses).

### 5. UnrealReZen — repack to IoStore

`build/ModOutput/` is loose, patched `.uasset`/`.ubulk` pairs — the game only loads content
packaged as an IoStore container (`.pak`/`.ucas`/`.utoc`). This game's cooked audio assets are
IoStore-native, so a plain legacy `.pak` (e.g. via `repak`) is **not** sufficient — it loads
without crashing but the replaced audio silently never plays.

[UnrealReZen](https://github.com/rm-NoobInCoding/UnrealReZen) does the IoStore repack correctly,
but needs a small patch to build and produce a container the engine actually accepts — see
**[docs/unrealrezen-build.md](unrealrezen-build.md)**. Not vendored here (external project, its
own license) — clone and patch it per that doc.

### 6. Result — deploy

The repacked `LektorMainOnly_P.pak/.ucas/.utoc` goes into
`Stalker2\Content\Paks\~mods\`. See README.md "Deploying & testing".

## Cutscenes (excluded)

A small number of story cutscenes (30 out of 292 in the game) drive dialogue through Unreal
Sequencer tracks instead of the standard per-line `VO_<sid>.uasset` AkEvent pattern this pipeline
targets. Each such cutscene has a master sequence holding a `MovieSceneSubtitleEventSection` per
line of subtitle, and a paired `*_VO_EN.uasset` sequence holding one `MovieSceneAkAudioEventSection`
per audio take — a normal `UAkAudioEvent`, patchable with the exact same byte-splice mechanism as
the main pipeline in principle. Two separate problems had to be solved (in the private working
copy — not shipped here), and one of them never got fully fixed:

**1. Mapping a GameReader line to the right Sequencer section — solved.** Both section types carry
a `SectionRange` (a frame range). Matching each subtitle section to the audio section with the
largest frame-range overlap correctly identified 21/21 known lines in the prologue test (validated
by ear). At full scale: 225 (SID, audio-asset) pairs across 214 unique assets, 75 unique lines, 74
of which have GameReader audio.

**2. Byte-patching those assets without crashing the game — root-caused, fixed, validated (but not
shipped).** Unlike normal dialogue events, these lines' audio is embedded *inline* inside the
`.uasset` itself (no separate `.ubulk`), and naively splicing at `BulkDataMap[i].SerialOffset`
corrupts the file — that offset turned out to be relative to `Summary.TotalHeaderSize`, not
file-absolute the way it is for `.ubulk`-backed files. Once found, the fix was one arithmetic
change (splice at `SerialOffset + TotalHeaderSize` instead of `SerialOffset`), and was validated
the same way the main pipeline's mechanism was: repack the patched asset into a real IoStore
container via UnrealReZen, then re-load it with CUE4Parse and confirm the export deserializes, the
target media re-reads as valid `RIFF` audio at its new size, and every untouched sibling media in
the same file kept its exact original byte size. At full scale, 225/225 patched files passed this
check with zero crashes/corruption.

**Despite passing that check, real in-game playback still had two unresolved bugs**, which is why
cutscenes are not shipped:
- **Truncation.** `MovieSceneAkAudioEventSection.SectionRange` is a *fixed* frame window baked
  into the cutscene's Sequencer asset — a different asset than the one being patched — sized to
  the original English audio's duration. Sequencer hard-cuts playback at that boundary regardless
  of how long the newly-spliced Polish audio actually is, so longer Polish lines get cut short.
  Fixing this means editing the Sequencer asset's own `SectionRange`, a different code path not
  yet built.
- **An unexplained English-then-Polish double-playback**, observed on at least one patched line in
  a real in-game test. Ruled out: a duplicate Sequencer trigger, a duplicate main-pipeline entry
  for the same asset, and a duplicate SoundBank. The actual mechanism was never identified.

Given both, cutscene support is paused as experimental — `LektorMainOnly_P` (this repo's pipeline)
covers ordinary in-game dialogue only, not cutscenes, and none of the cutscene-specific tooling is
published here.

## `scripts/mapping-reference/` — how the CSV was built

Kept for transparency and reproducibility, **not required to build the mod**:

- `mapping-reference/match_lektor.ps1` — exact text match, GameReader lines ↔ official Polish
  `sid_phrase_*` locres text (exported via
  [S2HOCMM](https://gitlab.com/PatrykPniewski/s2hocmm) from the game's `LocalizationDB`).
- `mapping-reference/FuzzyMatcher/` — bigram/Dice fuzzy match for the exact-match misses
  (mostly GameReader's TTS-pronunciation text edits vs. the game's actual subtitle text).
- `mapping-reference/merge_final_mapping2.ps1` — merges the exact+fuzzy text matches with the
  SID→MediaId resolution (walking each event's `EventCookedData.EventLanguageMap[...].Media[]`
  via CUE4Parse, keeping only entries whose `DebugName` matches the SID) into the final CSV.

All three still reference the original development machine's paths and expect intermediate files
this repo doesn't ship (raw GameReader export, raw locres JSON dump). Adapt before rerunning.
