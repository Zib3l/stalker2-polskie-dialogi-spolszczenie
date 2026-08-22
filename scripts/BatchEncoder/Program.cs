// Stage 2 of the pipeline: encode GameReader's matched Polish audio (from data/FINAL_lektor_mapping.csv)
// into Wwise .wem files, via ffmpeg (ogg -> wav) and WwiseConsole (wav -> wem).
// See README.md "Quick start" and docs/architecture.md for the full pipeline.
using System.Diagnostics;
using System.Text;
using System.Text.Json;

var repoRoot = Config.FindRepoRoot();
var config = Config.Load(repoRoot);

string csvPath = Path.Combine(repoRoot, "data", "FINAL_lektor_mapping.csv");
string project = Path.Combine(repoRoot, "WwiseProject", "LektorProject", "LektorProject.wproj");
string wavDir = Path.Combine(repoRoot, "WwiseProject", "full_batch_wav");
string wemOutDir = Path.Combine(repoRoot, "WwiseProject", "full_batch_wem");
string buildDir = Path.Combine(repoRoot, "build");

Directory.CreateDirectory(wavDir);
Directory.CreateDirectory(wemOutDir);
Directory.CreateDirectory(buildDir);

string ResolveAudio(string fileName) => Path.Combine(config.GameReaderAudioDir, fileName);

var rows = ParseCsv(csvPath);
Console.WriteLine($"Loaded {rows.Count} rows from {csvPath}");

var validRows = rows.Where(r => !string.IsNullOrWhiteSpace(r["Audio1"]) && File.Exists(ResolveAudio(r["Audio1"])) && !string.IsNullOrWhiteSpace(r["EnglishMediaId"])).ToList();
Console.WriteLine($"Rows with valid Audio1 + EnglishMediaId: {validRows.Count}");
if (validRows.Count < rows.Count)
{
    Console.WriteLine($"NOTE: {rows.Count - validRows.Count} rows were skipped because their Audio1 file wasn't found under gameReaderAudioDir");
    Console.WriteLine($"({config.GameReaderAudioDir}). Make sure that folder holds GameReader's output1 (N).ogg files.");
}

var mappingOut = new List<(string SID, string MediaId, string WavName, string WemName)>();
foreach (var r in validRows)
{
    var wavName = $"{r["SID"]}.wav";
    mappingOut.Add((r["SID"], r["EnglishMediaId"], wavName, Path.ChangeExtension(wavName, ".wem")));
}

// Write SID->MediaId mapping CSV for FullPatcher to consume later.
string wemMappingCsv = Path.Combine(buildDir, "FULL_BATCH_wem_mapping.csv");
using (var sw = new StreamWriter(wemMappingCsv, false, Encoding.UTF8))
{
    sw.WriteLine("SID,MediaId,WavName,WemName");
    foreach (var m in mappingOut)
        sw.WriteLine($"\"{m.SID}\",\"{m.MediaId}\",\"{m.WavName}\",\"{m.WemName}\"");
}
Console.WriteLine($"Wrote {wemMappingCsv}");

// Step 1: convert ogg -> wav in parallel via ffmpeg
int done = 0;
int total = validRows.Count;
var sw1 = Stopwatch.StartNew();
Parallel.ForEach(validRows, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, r =>
{
    var wavPath = Path.Combine(wavDir, $"{r["SID"]}.wav");
    if (File.Exists(wavPath))
    {
        Interlocked.Increment(ref done);
        return;
    }
    var psi = new ProcessStartInfo
    {
        FileName = config.Ffmpeg,
        Arguments = $"-y -loglevel error -i \"{ResolveAudio(r["Audio1"])}\" \"{wavPath}\"",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    using var proc = Process.Start(psi)!;
    proc.WaitForExit();
    int d = Interlocked.Increment(ref done);
    if (d % 500 == 0) Console.WriteLine($"  ffmpeg progress: {d}/{total} ({sw1.Elapsed})");
});
Console.WriteLine($"ffmpeg conversion done: {done}/{total} in {sw1.Elapsed}");

// Step 2: build .wsources XML for the whole batch
var xml = new StringBuilder();
xml.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
xml.AppendLine($"<ExternalSourcesList SchemaVersion=\"1\" Root=\"{wavDir}\">");
foreach (var m in mappingOut)
    xml.AppendLine($"  <Source Path=\"{m.WavName}\" Conversion=\"Vorbis Quality High\"/>");
xml.AppendLine("</ExternalSourcesList>");
string wsourcesPath = Path.Combine(repoRoot, "WwiseProject", "full_batch.wsources");
File.WriteAllText(wsourcesPath, xml.ToString(), Encoding.UTF8);
Console.WriteLine($"Wrote {wsourcesPath} with {mappingOut.Count} sources.");

// Step 3: run WwiseConsole convert-external-source once for the whole batch
Console.WriteLine("Running WwiseConsole convert-external-source (this can take a while for ~16K files)...");
var sw2 = Stopwatch.StartNew();
var wpsi = new ProcessStartInfo
{
    FileName = config.WwiseConsole,
    Arguments = $"convert-external-source \"{project}\" --source-file \"{wsourcesPath}\" --output \"{wemOutDir}\"",
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
};
using var wproc = Process.Start(wpsi)!;
wproc.OutputDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine("  [wwise] " + e.Data); };
wproc.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine("  [wwise-err] " + e.Data); };
wproc.BeginOutputReadLine();
wproc.BeginErrorReadLine();
wproc.WaitForExit();
Console.WriteLine($"WwiseConsole finished in {sw2.Elapsed}, exit code {wproc.ExitCode}");

// Step 4: sanity check output count
var wemFiles = Directory.GetFiles(wemOutDir, "*.wem", SearchOption.AllDirectories);
Console.WriteLine($"Total WEM files produced: {wemFiles.Length} (expected ~{mappingOut.Count})");

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

// Shared config type/loader — duplicated verbatim in FullPatcher/Program.cs so each tool stays a
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
