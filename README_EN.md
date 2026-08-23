# S.T.A.L.K.E.R. 2: Heart of Chornobyl — Polish dialogue (Polish voice mod)

A set of tools and SID → text → audio file → in-game identifier mapping that allows
English/Ukrainian dialogue lines in S.T.A.L.K.E.R. 2: Heart of Chornobyl to be replaced
with Polish voice audio, using existing [GameReader](https://gamereader.pl) recordings (author:
Rafko) — a free overlay application that reads game subtitles aloud in real time.

## What this actually does

End result: you launch the game, set the voice language to English, and **~78% of spoken
dialogue lines play in Polish**, read by the voice reader, instead of the original
English/Ukrainian voice acting. This is not a separate installation or a modification
of the game's executable file — it is a normal extractable mod: one `.pak/.ucas/.utoc`
set that you drop into `~mods` and can remove at any time to return to the original.
**The main pipeline is tested and confirmed to work in-game** — the cutscene pipeline is not.

**What this does NOT do:**
- It does not generate a new voice reader — it uses existing GameReader recordings (you must have them yourself,
  e.g. by playing through the game once with GameReader running).
- It does not cover cutscenes — those will still be in English/Ukrainian. Reason:
  in cutscenes the voice-reader audio is split into smaller parts and triggered at specific
  animation frames (a separate fragment for each cutscene moment), while GameReader records
  complete lines in a single file — matching one to the other 1:1 is not as straightforward
  as with normal dialogue and requires a separate mechanism (see
  [docs/architecture.md](docs/architecture.md#cutscenes-excluded)). To support this properly,
  there would need to be separate recordings for each cutscene fragment (not one recording of the
  complete line) — GameReader does not provide such recordings.
- It does not cover 100% of dialogue — about 22% of lines are either those GameReader has not yet recorded,
  or lines controlled by a different game system (see [Limitations](#limitations)).

**How it works, in short** (full description: [docs/architecture.md](docs/architecture.md)):

1. `data/FINAL_lektor_mapping.csv` (already prepared and included in the repo) tells for each dialogue line:
   what the text is, which GameReader audio file reads it, and under which exact identifier that
   line lives in the game's audio files (Wwise SoundBanks).
2. `BatchEncoder` takes the GameReader files referenced by this CSV and encodes them into the format the
   game engine expects (`.wem`, via Wwise).
3. `FullPatcher` opens the original game files, locates exactly the bytes containing the
   English recording for each line, and replaces them with the newly encoded Polish audio — without
   touching anything else in the file.
4. The result is packed (`UnrealReZen`) into the format the engine actually loads as a mod, and
   placed into `~mods`.

Everything below is the exact set of steps needed to reproduce this on your own.

## Status

- **Main dialogue pipeline: works** (confirmed in-game). Normal in-game dialogue, without
  cutscenes.
- **Cutscene pipeline: experimental / paused.** It is not part of this repo — see
  [docs/architecture.md](docs/architecture.md#cutscenes-excluded).

## What is in this repo, and what is not

**Included:**
- `data/FINAL_lektor_mapping.csv` — mapping for 16,402 dialogue lines (78% of all
  spoken lines in the game): SID → official Polish text → GameReader audio file name →
  the actual media identifier in the game's SoundBanks. This is the key file — without it
  nobody can reproduce this pipeline.
- `data/SPEAKER_VOICE_MAPPING.csv` — for 1,675 lines in the coverage gap (see
  [Limitations](#limitations)): character code → real in-game name → suggested voice profile →
  example line. A concrete list for future recording, not just a number.
- Source code for the custom tools (`scripts/BatchEncoder`, `scripts/FullPatcher`) — ready to
  compile with `dotnet build`, plus `build.ps1` tying the whole pipeline together in one command.
- Wwise source project (`WwiseProject/LektorProject`) — without generated banks/audio.
- Architecture documentation and the exact UnrealReZen patch (`docs/`, `patches/`).

**Not included (intentionally):**
- No audio files (`.ogg`/`.wav`/`.wem`) — neither GameReader recordings nor generated WEMs.
- No game files (`.pak`/`.ucas`/`.utoc`/`.uasset`/`.ubulk`, `Mappings.usmap`).
- No ready-made mod (packed `LektorMainOnly_P`).
- No backups, logs, working directories, or experimental cutscene tools.

## Requirements

- Windows, .NET SDK 10 (see each tool's `.csproj`)
- A legal copy of S.T.A.L.K.E.R. 2: Heart of Chornobyl (for `Paks` and your own `.usmap`)
- Your own set of GameReader recordings (`audio/` folder from its export) — not included
- Wwise Authoring **2026.1.2.9249** (for `WwiseConsole.exe`) — the exact version used to create
  `WwiseProject/LektorProject.wproj`; requires a free account on audiokinetic.com to download
  the installer at all
- [UnrealReZen](https://github.com/rm-NoobInCoding/UnrealReZen), built with the patch from
  `patches/UnrealReZen.patch` — see [docs/unrealrezen-build.md](docs/unrealrezen-build.md)

## What you need to prepare yourself before building the mod

1. **A legal copy of the game**, with `Stalker2\Content\Paks` available locally.
2. **Your own `.usmap` file**, generated from your own current game installation via
   [UE4SS](https://github.com/UE4SS-RE/RE-UE4SS) (public/older mappings usually do not match the latest patch) —
   additionally use the community fix for version 2.0
   ([mod 2341 on Nexus](https://www.nexusmods.com/stalker2heartofchornobyl/mods/2341)).
   Launch the game, press the default UE4SS mapping dump shortcut (Ctrl+Numpad6), copy the resulting
   `.usmap` and point `config.json` to it.
3. **GameReader recordings** — the folder containing its `output1 (N).ogg` files (the second take, `output2`,
   is not used by this pipeline). Point this folder to `gameReaderAudioDir` in `config.json`.
4. **Wwise Authoring 2026.1.2.9249** — downloading the installer requires a free account at
   audiokinetic.com.
5. **UnrealReZen**, built with the patch — see [docs/unrealrezen-build.md](docs/unrealrezen-build.md).
6. **`oo2core_9_win64.dll`** (Oodle library) — specify this in `config.json` as `oodleDllPath`.
   It can be found, for example, in a [FModel](https://github.com/4sval/FModel) installation
   or in the UnrealReZen release package.

None of the above is part of this repo — it is either copyrighted material (the game),
data from your own local installation, or somebody else's large recording library.

## Quick start

1. Clone this repo.
2. Copy `config.example.json` → `config.json` and fill in your paths (see above).
   `config.json` is in `.gitignore` — never commit real local paths.
3. Prepare the required inputs (see previous section).

**Simplest path:** after filling in `config.json` (including `unrealRezenExe` and `gameRoot`,
see `config.example.json`) simply run:

```powershell
.uild.ps1          # builds, encodes audio, patches, packs - ends at build\Release\
.uild.ps1 -Deploy  # same, plus automatically copies to ~mods
```

The following steps 4–8 are exactly what the script does internally — useful if something fails
and you need to find which stage, or if you prefer to control the process manually.

4. Build the tools:
   ```
   dotnet build scripts/BatchEncoder
   dotnet build scripts/FullPatcher
   ```

5. **Audio encoding** — run `BatchEncoder`: converts matched `.ogg` files to `.wav`
   (in parallel, through `ffmpeg`), then encodes everything to `.wem` in one pass via
   `WwiseConsole convert-external-source`.
   ```
   dotnet run --project scripts/BatchEncoder -c Release
   ```

6. **Patching** — run `FullPatcher`: for each line in the mapping, it finds the correct
   `VO_..._<SID>.uasset` resource in the game, locates the original audio byte range in its
   `BulkDataMap`, and inserts the new `.wem`.
   ```
   dotnet run --project scripts/FullPatcher -c Release
   ```

7. **Repacking** with UnrealReZen into an IoStore container (see
   `docs/unrealrezen-build.md` for exact build steps):
   ```
   UnrealReZen.exe --game-dir "<path to S.T.A.L.K.E.R. 2 Heart of Chornobyl>" ^
     --content-path "build\ModOutput" --engine-version GAME_UE5_5 ^
     --compression-format Oodle --output-path "<out>\LektorMainOnly_P.utoc"
   ```

8. **Deployment**: copy the generated `LektorMainOnly_P.pak/.ucas/.utoc` into
   `Stalker2\Content\Paks\~mods` (first remove old files there — see below).

## Deployment and testing

1. Always start with a clean `~mods` — otherwise `FullPatcher`/`UnrealReZen` may read a
   previous (possibly inconsistent) patch result instead of the original game file:
   ```powershell
   Remove-Item "<game>\Stalker2\Content\Paks\~mods\*"
   ```
2. Copy the freshly built `LektorMainOnly_P.pak/.ucas/.utoc` into `~mods\`.
3. Launch the game, **set the voice/audio language to English** (we patch only this language
   slot), enter a dialogue scene and check that Polish audio plays.
4. Check `%LOCALAPPDATA%\Stalker2\Saved\Logs\Stalker2.log`:
   - `Mounted IoStore container` — good, the mod loaded.
   - `Invalid container header` — harmless, can appear even with a correctly working package.
   - `ParserException`, `IndexOutOfRangeException`, or a crash while loading — bad, something in
     the patched package is corrupted.
5. To revert changes, simply remove the mod's `.pak/.ucas/.utoc` files from `~mods\`.

## Repository structure

```text
build.ps1                      - ties the whole pipeline together in one command (see Quick start)
data/
  FINAL_lektor_mapping.csv     - key SID -> text -> audio -> MediaId mapping
  SPEAKER_VOICE_MAPPING.csv    - coverage gap: character code -> name -> suggested voice
scripts/
  BatchEncoder/                - encodes matched audio through WwiseConsole
  FullPatcher/                 - inserts encoded WEMs where the original game audio resides
  mapping-reference/           - how FINAL_lektor_mapping.csv was created (not required for build)
WwiseProject/                  - Wwise source project (without generated banks/audio)
docs/
  architecture.md              - pipeline step by step
  unrealrezen-build.md         - exact UnrealReZen patch and build instructions
patches/
  UnrealReZen.patch            - patch (git apply) fixing IoStore container building
config.example.json            - local configuration template
```

## Limitations

- Covers 16,402 of ~21,040 spoken lines in the game (78%) — the rest are lines GameReader
  has not recorded yet, or lines controlled by the cutscene system (see above).
  Of these, about **1,675 lines have a real, ready-to-replace voice asset in the game, but are
  still missing a Polish recording** — this is a concrete, countable future target (for example,
  recording them through GameReader or with another voice reader/TTS), unlike lines outside the
  standard dialogue system, which this pipeline does not touch.
- Requires manual Wwise configuration matching the version used during development.
- `data/FINAL_lektor_mapping.csv` contains the game's official Polish dialogue text (the `Text` column) —
  required so that anyone can verify/extend the mapping without exporting it from the game again.

## License

TODO — not determined. This repo contains original code written for this project, alongside
dependencies on third-party tools (CUE4Parse, UnrealReZen, Wwise) under their own licenses.
Do not assume any license until this is clarified.

The GameReader recordings used by this pipeline are owned by a third party (author: Rafko,
[gamereader.pl](https://gamereader.pl)) and provided for non-commercial use within GameReader.
This project is not affiliated with it. If you want to use this pipeline or its results **commercially**,
contact Rafko and obtain his permission first — do not assume you have that right just because you have
technical access to these recordings.
