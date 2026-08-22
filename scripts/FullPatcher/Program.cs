// Stage 3 of the pipeline: byte-patch the game's own Wwise SoundBanks, swapping in the .wem files
// produced by BatchEncoder in place of the original English-language audio, matched by SID via
// data/FINAL_lektor_mapping.csv + build/FULL_BATCH_wem_mapping.csv.
// See README.md "Quick start" and docs/architecture.md for the full pipeline and why this works
// the way it does (FBulkDataMapEntry / .ubulk splice).
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Compression;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports.Wwise;
using CUE4Parse.UE4.Wwise;
using Serilog;

Log.Logger = new LoggerConfiguration().MinimumLevel.Warning().WriteTo.Console().CreateLogger();

var repoRoot = Config.FindRepoRoot();
var config = Config.Load(repoRoot);

string gameDir = config.GameDir;
string usmapPath = config.Usmap;
string wemDir = Path.Combine(repoRoot, "WwiseProject", "full_batch_wem", "Windows");
string buildDir = Path.Combine(repoRoot, "build");
string outputRoot = Path.Combine(buildDir, "ModOutput");
string mappingCsv = Path.Combine(buildDir, "FULL_BATCH_wem_mapping.csv");
string reportCsv = Path.Combine(buildDir, "FULL_PATCH_report.csv");
Directory.CreateDirectory(buildDir);

if (!File.Exists(config.OodleDllPath))
    throw new FileNotFoundException(
        $"Oodle DLL not found at '{config.OodleDllPath}'. Set oodleDllPath in config.json — " +
        "see README.md for where to obtain oo2core_9_win64.dll.");
OodleHelper.Initialize(config.OodleDllPath);

var versions = new VersionContainer(EGame.GAME_UE5_5);
var provider = new DefaultFileProvider(gameDir, SearchOption.AllDirectories, isCaseInsensitive: true, versions);
provider.MappingsContainer = new FileUsmapTypeMappingsProvider(usmapPath);
provider.Initialize();
provider.SubmitKey(new CUE4Parse.UE4.Objects.Core.Misc.FGuid(0), new CUE4Parse.Encryption.Aes.FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));
provider.Mount();
Console.WriteLine($"Mounted. Total files: {provider.Files.Count}");

// Build suffix index: for every VO_*.uasset path, index every suffix starting at each underscore boundary -> full path.
// (Event filenames are VO_<unknown-actor-prefix>_<SID>.uasset, so we need suffix matching to find them by SID alone.)
var suffixIndex = new Dictionary<string, string>(StringComparer.Ordinal);
int ambiguous = 0;
foreach (var path in provider.Files.Keys)
{
    if (!path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) continue;
    var fname = Path.GetFileNameWithoutExtension(path);
    if (!fname.StartsWith("VO_", StringComparison.Ordinal)) continue;
    var rest = fname[3..];
    int idx = 0;
    while (true)
    {
        if (!suffixIndex.TryAdd(rest[idx..], path)) ambiguous++;
        int next = rest.IndexOf('_', idx);
        if (next < 0) break;
        idx = next + 1;
    }
}
Console.WriteLine($"Suffix index built: {suffixIndex.Count} keys ({ambiguous} collisions, first-seen kept).");

if (!File.Exists(mappingCsv))
    throw new FileNotFoundException($"'{mappingCsv}' not found — run BatchEncoder first, it writes this file.");
var rows = ParseCsv(mappingCsv);
Console.WriteLine($"Loaded {rows.Count} rows from {mappingCsv}");

int okCount = 0, skipNoUasset = 0, skipNoMedia = 0, skipNoWem = 0, skipNoEntry = 0, skipNoPattern = 0, errCount = 0;
var sw = Stopwatch.StartNew();
using var report = new StreamWriter(reportCsv, false, Encoding.UTF8);
report.WriteLine("SID,Status,Detail");

for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
{
    var row = rows[rowIdx];
    string sid = row["SID"];
    string mediaIdStr = row["MediaId"];
    string wemName = row["WemName"];

    try
    {
        if (!suffixIndex.TryGetValue(sid, out var uassetPath))
        {
            skipNoUasset++;
            report.WriteLine($"\"{sid}\",NoUasset,");
            continue;
        }

        string wemPath = Path.Combine(wemDir, wemName);
        if (!File.Exists(wemPath))
        {
            skipNoWem++;
            report.WriteLine($"\"{sid}\",NoWem,{wemPath}");
            continue;
        }
        byte[] newWemBytes = File.ReadAllBytes(wemPath);

        var pkg = provider.LoadPackage(uassetPath);
        var iopkg = (IoPackage)pkg;
        var akEvent = pkg.GetExports().OfType<UAkAudioEvent>().FirstOrDefault();
        if (akEvent?.EventCookedData == null)
        {
            skipNoMedia++;
            report.WriteLine($"\"{sid}\",NoAkEvent,{uassetPath}");
            continue;
        }

        FWwiseMediaCookedData? targetMedia = null;
        foreach (var langEntry in akEvent.EventCookedData.Value.EventLanguageMap)
        {
            if (langEntry.Value == null) continue;
            if (!langEntry.Key.LanguageName.Text.Contains("English", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var media in langEntry.Value.Value.Media)
            {
                var baseName = media.DebugName.Text.Contains('\\') ? media.DebugName.Text[(media.DebugName.Text.LastIndexOf('\\') + 1)..] : media.DebugName.Text;
                if (!baseName.StartsWith("VO_", StringComparison.OrdinalIgnoreCase)) continue;
                if (media.MediaId.ToString() != mediaIdStr) continue;
                targetMedia = media;
                break;
            }
            if (targetMedia != null) break;
        }

        if (targetMedia == null || targetMedia.Value.PackagedFile?.BulkData?.WemFile is not FBulkDataDeferredByteData wemFile)
        {
            skipNoMedia++;
            report.WriteLine($"\"{sid}\",NoMediaMatch,{uassetPath}");
            continue;
        }

        ulong oldOffset = (ulong)wemFile.BulkData.Header.OffsetInFile;
        ulong oldSize = wemFile.BulkData.Header.SizeOnDisk;

        int entryIdx = -1;
        for (int i = 0; i < iopkg.BulkDataMap.Length; i++)
        {
            if (iopkg.BulkDataMap[i].SerialOffset == oldOffset && iopkg.BulkDataMap[i].SerialSize == oldSize)
            {
                entryIdx = i;
                break;
            }
        }
        if (entryIdx < 0)
        {
            skipNoEntry++;
            report.WriteLine($"\"{sid}\",NoBulkDataMapEntry,{uassetPath}");
            continue;
        }

        byte[] uassetBytes = provider.SaveAsset(uassetPath);
        string ubulkPath = Path.ChangeExtension(uassetPath, ".ubulk");
        byte[] ubulkBytes = provider.SaveAsset(ubulkPath);

        byte[] pattern = EncodeEntry(iopkg.BulkDataMap[entryIdx]);
        int pos = IndexOf(uassetBytes, pattern);
        if (pos < 0)
        {
            skipNoPattern++;
            report.WriteLine($"\"{sid}\",NoBytePattern,{uassetPath}");
            continue;
        }

        long delta = newWemBytes.Length - (long)oldSize;
        byte[] patchedUasset = (byte[])uassetBytes.Clone();

        // Patch our target entry's SerialSize (bytes [pos+16, pos+24))
        WriteU64LE(patchedUasset, pos + 16, (ulong)newWemBytes.Length);

        // Shift every other entry whose SerialOffset comes after ours
        int baseOffset = pos - entryIdx * 32;
        for (int j = 0; j < iopkg.BulkDataMap.Length; j++)
        {
            if (j == entryIdx) continue;
            if (iopkg.BulkDataMap[j].SerialOffset <= oldOffset) continue;
            int posJ = baseOffset + j * 32;
            ulong newOffsetJ = (ulong)((long)iopkg.BulkDataMap[j].SerialOffset + delta);
            WriteU64LE(patchedUasset, posJ, newOffsetJ);
        }

        // Splice new WEM bytes into the ubulk at oldOffset
        var newUbulk = new byte[ubulkBytes.Length + delta];
        Array.Copy(ubulkBytes, 0, newUbulk, 0, (int)oldOffset);
        Array.Copy(newWemBytes, 0, newUbulk, (int)oldOffset, newWemBytes.Length);
        Array.Copy(ubulkBytes, (int)(oldOffset + oldSize), newUbulk, (int)oldOffset + newWemBytes.Length, ubulkBytes.Length - (int)(oldOffset + oldSize));

        string outUasset = Path.Combine(outputRoot, uassetPath.Replace('/', Path.DirectorySeparatorChar));
        string outUbulk = Path.Combine(outputRoot, ubulkPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(outUasset)!);
        File.WriteAllBytes(outUasset, patchedUasset);
        File.WriteAllBytes(outUbulk, newUbulk);

        okCount++;
        report.WriteLine($"\"{sid}\",OK,");
    }
    catch (Exception ex)
    {
        errCount++;
        report.WriteLine($"\"{sid}\",Error,\"{ex.Message.Replace("\"", "'")}\"");
    }

    if ((rowIdx + 1) % 500 == 0)
    {
        Console.WriteLine($"Progress: {rowIdx + 1}/{rows.Count} | ok={okCount} noUasset={skipNoUasset} noMedia={skipNoMedia} noWem={skipNoWem} noEntry={skipNoEntry} noPattern={skipNoPattern} err={errCount} | {sw.Elapsed}");
    }
}

Console.WriteLine("\n=== FINAL SUMMARY ===");
Console.WriteLine($"Total rows: {rows.Count}");
Console.WriteLine($"OK (patched): {okCount}");
Console.WriteLine($"Skip - no uasset resolved: {skipNoUasset}");
Console.WriteLine($"Skip - no media match: {skipNoMedia}");
Console.WriteLine($"Skip - no wem file: {skipNoWem}");
Console.WriteLine($"Skip - no BulkDataMap entry: {skipNoEntry}");
Console.WriteLine($"Skip - no byte pattern found: {skipNoPattern}");
Console.WriteLine($"Errors: {errCount}");
Console.WriteLine($"Elapsed: {sw.Elapsed}");
Console.WriteLine($"Report written to: {reportCsv}");
Console.WriteLine($"Patched content written under: {outputRoot}");
Console.WriteLine("Next step: repack outputRoot with UnrealReZen (see README.md).");

static byte[] EncodeEntry(CUE4Parse.UE4.IO.Objects.FBulkDataMapEntry e)
{
    var buf = new byte[32];
    WriteU64LE(buf, 0, e.SerialOffset);
    WriteU64LE(buf, 8, e.DuplicateSerialOffset);
    WriteU64LE(buf, 16, e.SerialSize);
    WriteU32LE(buf, 24, e.Flags);
    buf[28] = e.CookedIndex.Value;
    return buf;
}

static void WriteU64LE(byte[] buf, int offset, ulong value)
{
    for (int i = 0; i < 8; i++) buf[offset + i] = (byte)(value >> (8 * i));
}

static void WriteU32LE(byte[] buf, int offset, uint value)
{
    for (int i = 0; i < 4; i++) buf[offset + i] = (byte)(value >> (8 * i));
}

static int IndexOf(byte[] haystack, byte[] needle)
{
    for (int i = 0; i <= haystack.Length - needle.Length; i++)
    {
        bool match = true;
        for (int j = 0; j < needle.Length; j++)
        {
            if (haystack[i + j] != needle[j]) { match = false; break; }
        }
        if (match) return i;
    }
    return -1;
}

static List<Dictionary<string, string>> ParseCsv(string path)
{
    var lines = File.ReadAllLines(path, Encoding.UTF8);
    var header = SplitCsvLine(lines[0]);
    var result = new List<Dictionary<string, string>>();
    for (int i = 1; i < lines.Length; i++)
    {
        if (string.IsNullOrWhiteSpace(lines[i])) continue;
        var fields = SplitCsvLine(lines[i]);
        var dict = new Dictionary<string, string>();
        for (int j = 0; j < header.Count && j < fields.Count; j++)
            dict[header[j].Trim('"')] = fields[j];
        result.Add(dict);
    }
    return result;
}

static List<string> SplitCsvLine(string line)
{
    var fields = new List<string>();
    var sb = new StringBuilder();
    bool inQuotes = false;
    for (int i = 0; i < line.Length; i++)
    {
        char c = line[i];
        if (inQuotes)
        {
            if (c == '"')
            {
                if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else inQuotes = false;
            }
            else sb.Append(c);
        }
        else
        {
            if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
    }
    fields.Add(sb.ToString());
    return fields;
}

// Shared config type/loader — duplicated verbatim in BatchEncoder/Program.cs so each tool stays a
// single self-contained file. See config.example.json for the full field list and comments.
record Config(string GameDir, string Usmap, string WwiseConsole, string Ffmpeg, string GameReaderAudioDir, string OodleDllPath)
{
    public static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            if (File.Exists(Path.Combine(d.FullName, "config.json"))) return d.FullName;
            d = d.Parent;
        }
        throw new FileNotFoundException(
            "config.json not found above " + AppContext.BaseDirectory +
            ". Copy config.example.json to config.json in the repo root and fill in your local paths.");
    }

    public static Config Load(string repoRoot)
    {
        var json = File.ReadAllText(Path.Combine(repoRoot, "config.json"));
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<Config>(json, opts)
            ?? throw new InvalidDataException("config.json did not parse to a valid config object.");
    }
}
