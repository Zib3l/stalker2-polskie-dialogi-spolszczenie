// REFERENCE / HISTORICAL — not required to build the mod.
//
// data/FINAL_lektor_mapping.csv (repo root) is the already-computed output of this pipeline
// stage. This file is kept only for transparency/reproducibility of how that CSV was derived.
// It still has hardcoded paths from the machine it was developed on and expects its own local
// inputs that aren't part of this repo (a GameReader PL line/audio export, and a Polish
// sid_phrase_* text dump extracted from the game's LocalizationDB via CUE4Parse — see
// docs/architecture.md, "Dialogue Mapping"). Adapt the paths below before running it again.
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

string root = @"G:\ClaudeProjekt\Stalker 2";
string gameReaderLinesPath = Path.Combine(root, @"S.T.A.L.K.E.R. 2\subtitles\wersja po edycji do TTS\subtitlesPL (TTS ready).txt");
string audioDir = Path.Combine(root, @"S.T.A.L.K.E.R. 2\audio");
string locFile = Path.Combine(root, @"loc_json\Localization_PL.json");
string exactCsv = Path.Combine(root, @"scripts\sid_audio_mapping.csv");
string outCsv = Path.Combine(root, @"scripts\sid_audio_mapping_fuzzy.csv");
string combinedCsv = Path.Combine(root, @"scripts\sid_audio_mapping_combined.csv");

var punctRegex = new Regex(@"[^\p{L}\p{N} ]", RegexOptions.Compiled);
var wsRegex = new Regex(@"\s+", RegexOptions.Compiled);

string Normalize(string s)
{
    var t = punctRegex.Replace(s.Trim().ToLowerInvariant(), "");
    t = wsRegex.Replace(t, " ").Trim();
    return t;
}

HashSet<string> Bigrams(string s)
{
    var set = new HashSet<string>();
    if (s.Length < 2) { if (s.Length > 0) set.Add(s); return set; }
    for (int i = 0; i < s.Length - 1; i++) set.Add(s.Substring(i, 2));
    return set;
}

double Dice(HashSet<string> a, HashSet<string> b)
{
    if (a.Count == 0 || b.Count == 0) return 0.0;
    var (small, large) = a.Count <= b.Count ? (a, b) : (b, a);
    int matches = 0;
    foreach (var item in small) if (large.Contains(item)) matches++;
    return (2.0 * matches) / (a.Count + b.Count);
}

Console.WriteLine("Loading GameReader lines...");
var grRawLines = File.ReadAllLines(gameReaderLinesPath, Encoding.UTF8);

var buckets = new Dictionary<int, List<GrLine>>();
for (int i = 0; i < grRawLines.Length; i++)
{
    var norm = Normalize(grRawLines[i]);
    if (norm.Length == 0) continue;
    var len = norm.Length;
    if (!buckets.TryGetValue(len, out var list)) { list = new List<GrLine>(); buckets[len] = list; }
    list.Add(new GrLine(norm, i + 1, Bigrams(norm)));
}
Console.WriteLine($"Indexed {grRawLines.Length} GameReader lines into {buckets.Count} length buckets.");

var alreadyMatched = new HashSet<string>();
if (File.Exists(exactCsv))
{
    foreach (var line in File.ReadLines(exactCsv, Encoding.UTF8).Skip(1))
    {
        // SID is the first CSV field, simple quoted-CSV split is fine here (no embedded commas in SID).
        var firstComma = line.IndexOf("\",\"", StringComparison.Ordinal);
        if (firstComma > 1) alreadyMatched.Add(line.Substring(1, firstComma - 1));
    }
}
Console.WriteLine($"{alreadyMatched.Count} SIDs already matched exactly, skipping those.");

Console.WriteLine("Loading Polish localization JSON...");
using var fs = File.OpenRead(locFile);
using var doc = await JsonDocument.ParseAsync(fs);

var phraseEntries = new List<(string Sid, string Text)>();
foreach (var prop in doc.RootElement.EnumerateObject())
{
    if (!prop.Name.StartsWith("sid_phrase_", StringComparison.Ordinal)) continue;
    var val = prop.Value.GetString();
    if (string.IsNullOrEmpty(val)) continue;
    var sid = prop.Name.Substring("sid_phrase_".Length);
    if (alreadyMatched.Contains(sid)) continue;
    phraseEntries.Add((sid, val));
}
Console.WriteLine($"{phraseEntries.Count} unmatched sid_phrase_ entries to fuzzy-match.");

const double Threshold = 0.85;
const int Window = 4;
var results = new ConcurrentBag<(string Sid, int LineNum, double Score, string LocText, string MatchedGr)>();
int checkedCount = 0;

Parallel.ForEach(phraseEntries, entry =>
{
    var norm = Normalize(entry.Text);
    if (norm.Length == 0) return;
    var bg = Bigrams(norm);
    int len = norm.Length;

    double bestScore = 0.0;
    int bestLine = -1;
    string? bestText = null;

    for (int L = Math.Max(1, len - Window); L <= len + Window; L++)
    {
        if (!buckets.TryGetValue(L, out var list)) continue;
        foreach (var cand in list)
        {
            var score = Dice(bg, cand.Bigrams);
            if (score > bestScore)
            {
                bestScore = score;
                bestLine = cand.LineNum;
                bestText = cand.Norm;
            }
        }
    }

    if (bestScore >= Threshold)
        results.Add((entry.Sid, bestLine, bestScore, entry.Text, bestText!));

    var c = Interlocked.Increment(ref checkedCount);
    if (c % 500 == 0) Console.WriteLine($"... checked {c} / {phraseEntries.Count}, matched so far: {results.Count}");
});

Console.WriteLine($"Fuzzy-matched {results.Count} of {phraseEntries.Count} (threshold {Threshold}).");

string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

using (var w = new StreamWriter(outCsv, false, Encoding.UTF8))
{
    w.WriteLine("SID,AKEvent,LineNum,Score,LocText,MatchedGR,Audio1,Audio2");
    foreach (var r in results.OrderBy(r => r.Sid, StringComparer.Ordinal))
    {
        var a1 = Path.Combine(audioDir, $"output1 ({r.LineNum}).ogg");
        var a2 = Path.Combine(audioDir, $"output2 ({r.LineNum}).ogg");
        w.WriteLine(string.Join(",", Csv(r.Sid), Csv($"VO_{r.Sid}"), r.LineNum, r.Score.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture), Csv(r.LocText), Csv(r.MatchedGr), Csv(a1), Csv(a2)));
    }
}
Console.WriteLine($"Written to {outCsv}");

using (var w = new StreamWriter(combinedCsv, false, Encoding.UTF8))
{
    w.WriteLine("SID,AKEvent,LineNum,MatchType,Score,Text,Audio1,Audio2");
    if (File.Exists(exactCsv))
    {
        foreach (var line in File.ReadLines(exactCsv, Encoding.UTF8).Skip(1))
        {
            // Re-emit exact-match rows with a MatchType column inserted; reuse original fields via simple split.
            var parts = SplitCsvLine(line);
            if (parts.Length < 6) continue;
            w.WriteLine(string.Join(",", Csv(parts[0]), Csv(parts[1]), parts[2], "\"exact\"", "1.000", Csv(parts[3]), Csv(parts[4]), Csv(parts[5])));
        }
    }
    foreach (var r in results.OrderBy(r => r.Sid, StringComparer.Ordinal))
    {
        var a1 = Path.Combine(audioDir, $"output1 ({r.LineNum}).ogg");
        var a2 = Path.Combine(audioDir, $"output2 ({r.LineNum}).ogg");
        w.WriteLine(string.Join(",", Csv(r.Sid), Csv($"VO_{r.Sid}"), r.LineNum, "\"fuzzy\"", r.Score.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture), Csv(r.LocText), Csv(a1), Csv(a2)));
    }
}
Console.WriteLine($"Combined CSV written to {combinedCsv}");

static string[] SplitCsvLine(string line)
{
    // Minimal quoted-CSV splitter sufficient for our own Export-Csv output (fields always quoted, "" for embedded quotes).
    var fields = new List<string>();
    int i = 0;
    while (i < line.Length)
    {
        if (line[i] == '"')
        {
            int start = ++i;
            var sb = new StringBuilder();
            while (i < line.Length)
            {
                if (line[i] == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                    i++; break;
                }
                sb.Append(line[i]); i++;
            }
            fields.Add(sb.ToString());
            if (i < line.Length && line[i] == ',') i++;
        }
        else
        {
            int start = i;
            while (i < line.Length && line[i] != ',') i++;
            fields.Add(line.Substring(start, i - start));
            if (i < line.Length && line[i] == ',') i++;
        }
    }
    return fields.ToArray();
}

record GrLine(string Norm, int LineNum, HashSet<string> Bigrams);
