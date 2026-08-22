# Building UnrealReZen for this pipeline

[UnrealReZen](https://github.com/rm-NoobInCoding/UnrealReZen) repacks a loose, patched content
tree (`build/ModOutput/`, produced by `scripts/FullPatcher`) into a proper IoStore container the
game actually loads. The published release build does not work correctly for this game as of the
2.0 "Back to the Zone" patch — three real bugs, fixed and verified end-to-end (confirmed working
in-game). The fix is provided as [`patches/UnrealReZen.patch`](../patches/UnrealReZen.patch).

## Why a plain `repak`-built `.pak` doesn't work

STALKER 2's cooked audio/event assets are IoStore-native (resolved by chunk ID, not by a
pak-relative path). A legacy-format `.pak` override loads without crashing but the engine's
IoStore-resolved asset loader doesn't consult it — the replaced audio silently never plays. You
need a real `.pak`/`.ucas`/`.utoc` triplet built as an IoStore container, which is what
UnrealReZen does.

## Build steps

1. Clone UnrealReZen and check out the commit the patch was made against:
   ```
   git clone https://github.com/rm-NoobInCoding/UnrealReZen.git
   cd UnrealReZen
   git checkout bf9e8de
   ```
2. Apply the patch:
   ```
   git apply "<path to this repo>/patches/UnrealReZen.patch"
   ```
3. Build:
   ```
   dotnet build UnrealReZen/UnrealReZen.csproj -c Release
   ```
4. Copy native dependencies into the build output folder — these are **not** rebuilt from C#
   source, so grab them from UnrealReZen's own prebuilt release zip (its GitHub Releases page):
   - `oo2core_9_win64.dll`
   - `CUE4Parse-Natives.dll`

   Put both next to the freshly built `UnrealReZen.exe`
   (`UnrealReZen/bin/Release/net10.0/`).

## What the patch fixes, and why

- **`UnrealReZen.csproj`** — retargets `net8.0` → `net10.0` and switches the `CUE4Parse`
  reference from a (now-missing) local submodule path to the NuGet package, pinned to
  `1.2.2.202608` (the version this whole pipeline was developed and verified against — a newer
  release may or may not still parse this game's Wwise/bulk-data layout correctly).
- **`Core/FIoDependency.cs`** — `ShaderMapHashes` used `entry.ShaderMapHashes.Select(a => a.Hash)`,
  a property that no longer exists on `FFilePackageStoreEntry` in this CUE4Parse version (API
  drift). Replaced with an empty list — safe here since this mod never adds new shader-map
  dependencies, only overwrites existing bulk audio payloads.
- **`Program.cs`, two related fixes to the manifest-building step** (`BuildManifest`):
  1. **VFS disposed too early.** The original code disposed `provider.MountedVfs` *before*
     `BuildManifest` ran, but `BuildManifest` lazily reads `entry.IoStoreReader.ContainerHeader` —
     so that read threw `ObjectDisposedException` on effectively every file. Moved the dispose to
     *after* `BuildManifest` completes.
  2. **Wrong dependency entry picked for files in a large shared container.** The original code
     looped over *every* entry in a container's `StoreEntries` array (up to ~122,771 entries for
     the game's giant shared container) and `TryAdd`-ed all of them under the same
     `chunkId.ChunkId` key. Since `TryAdd` only succeeds once, this silently kept whichever entry
     happened to be first in that huge array — an essentially random, unrelated package's
     dependency info, not the file's own. Fixed by looking up the file's own entry directly via
     `Array.IndexOf(header.PackageIds, chunkId.AsPackageId())`. When a container header genuinely
     can't be read for a given entry (rare, handle-lifecycle edge case), a minimal
     but structurally valid fallback entry (`ExportCount=1, ExportBundleCount=1,
     ImportedPackages=[]`) is used instead of leaving the chunk's dependency entry missing
     entirely — the real engine flags a missing entry as `Invalid container header` and ignores
     the whole container.
  3. **Duplicate chunk IDs for assets appearing in more than one base-game container.** Some
     packages (this matters most for the cutscene-adjacent asset tree) exist in more than one
     base pak/container — e.g. a base pakchunk plus a later patch/DLC pakchunk overriding it. The
     original code added one `ManifestFile` entry *per occurrence*, which put two entries under
     the identical `ChunkId` in the output `.utoc`'s chunk table and corrupted the engine's chunk
     lookup at runtime. Fixed by keeping only the first occurrence per unique `ChunkId`
     (`matches.DistinctBy(mm => mm.ChunkId.ChunkId)`).
  4. Removed a `provider.LoadLocalization(ELanguage.English)` call that crashed with an unrelated
     culture-parsing error and wasn't needed for this pipeline's use.

`Invalid container header` warnings in `%LOCALAPPDATA%\Stalker2\Saved\Logs\Stalker2.log` after
deploying a mod are normal/harmless on their own — they appear even on a known-good build. Use
them only as one signal among others (does the mod load, does the audio actually play) rather
than as proof of a problem by themselves.

## Repack command

```
UnrealReZen.exe --game-dir "<path to S.T.A.L.K.E.R. 2 Heart of Chornobyl>" ^
  --content-path "<this repo>\build\ModOutput" --engine-version GAME_UE5_5 ^
  --compression-format Oodle --output-path "<out>\LektorMainOnly_P.utoc"
```
