// FullPopulationProbe -- read-only, FULL-POPULATION (every SID in MASTER_DIALOGUE_DATABASE),
// switch-container-aware live probe. Replaces the stale MASTER_DB HasVoiceAsset flag with
// ground truth read byte-level from the shipped paks.
//
// Per SID, per language (English(US) / Ukrainian(UA)):
//   EventCookedData.EventLanguageMap[lang].Media[]                 (top-level)
//   EventCookedData.EventLanguageMap[lang].SwitchContainerLeaves[].Media[]   (switch leaves)
// each media -> PackagedFile.BulkData.WemFile -> Header(Offset,Size) -> real bytes from the
// package's own .ubulk -> first 4 bytes literally == "RIFF".
//
// Also emits an INTERNAL REFERENCE verdict per SID:
//   MATCH    -- at least one media DebugName contains this SID's own token
//   ALIAS    -- no own-token media, but the foreign SID token found inside resolves to another
//               known SID whose official text is identical (a legitimate shared/aliased recording)
//   MISMATCH -- foreign token resolves to a known SID with DIFFERENT text
//   UNKNOWN  -- foreign token not resolvable, or no media at all
//
// 100% read-only. Writes only CSV into the given output directory. No build/patch/deploy.

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Compression;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets.Exports.Wwise;
using CUE4Parse.UE4.Wwise;
using Serilog;

Log.Logger = new LoggerConfiguration().MinimumLevel.Warning().WriteTo.Console().CreateLogger();

// Paths come from the command line (or config.json) so this runs on any machine.
//   FullPopulationProbe <outDir> [--game-dir <Paks>] [--usmap <Mappings.usmap>] [--master-db <csv>]
string root = Environment.CurrentDirectory;
string gameDir = "", usmapPath = "", masterDbCsv = "";
for (int a = 0; a < args.Length - 1; a++)
{
    if (args[a] == "--game-dir") gameDir = args[a + 1];
    else if (args[a] == "--usmap") usmapPath = args[a + 1];
    else if (args[a] == "--master-db") masterDbCsv = args[a + 1];
    else if (args[a] == "--root") root = args[a + 1];
}
if (gameDir.Length == 0) gameDir = Path.Combine(root, "Paks");
if (usmapPath.Length == 0) usmapPath = Path.Combine(root, "Mappings.usmap");
if (masterDbCsv.Length == 0) masterDbCsv = Path.Combine(root, "MASTER_DIALOGUE_DATABASE.csv");
if (!Directory.Exists(gameDir)) { Console.Error.WriteLine($"missing game Paks dir: {gameDir}"); return; }
if (!File.Exists(usmapPath)) { Console.Error.WriteLine($"missing usmap: {usmapPath}"); return; }
if (!File.Exists(masterDbCsv)) { Console.Error.WriteLine($"missing master db: {masterDbCsv}"); return; }
string outDir = args.Length > 0 ? args[0] : $@"{root}\scripts\analysis_2026-08-24_long_research";
Directory.CreateDirectory(outDir);

string outFull = $@"{outDir}\_LIVE_FULL_POPULATION_V2.csv";
string outSummary = $@"{outDir}\_LIVE_FULL_POPULATION_V2_SUMMARY.txt";
string outOrphanAssets = $@"{outDir}\_VO_ASSETS_WITHOUT_SID.csv";

string oodlePath = Path.Combine(AppContext.BaseDirectory, "oodle-data-shared.dll");
if (!File.Exists(oodlePath))
    {
    var src = Path.Combine(root, "oodle-data-shared.dll");
    if (!File.Exists(src)) { Console.Error.WriteLine($"missing oodle-data-shared.dll next to {root}"); return; }
    File.Copy(src, oodlePath, overwrite: true);
}
OodleHelper.Initialize(oodlePath);

var versions = new VersionContainer(EGame.GAME_UE5_5);
var provider = new DefaultFileProvider(gameDir, SearchOption.AllDirectories, isCaseInsensitive: true, versions);
provider.MappingsContainer = new FileUsmapTypeMappingsProvider(usmapPath);
provider.Initialize();
provider.SubmitKey(new CUE4Parse.UE4.Objects.Core.Misc.FGuid(0), new CUE4Parse.Encryption.Aes.FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));
provider.Mount();
Console.WriteLine($"Mounted (base game only, read-only). Total files: {provider.Files.Count}");

// ---- resolver v2 -------------------------------------------------------------------
// v1 used a single case-SENSITIVE suffix index. Diagnosed blind spots (see _RESOLVE_DIAG.csv):
//   * case drift in the SID token   (LiterEDeltaBravo  vs  ...Deltabravo)
//   * reordered tokens              (supack_<Hub>_<speaker>  vs  <speaker>_supack_<Hub>)
//   * an extra token inserted       (..._MutantShoo_3_1_  vs  ..._MutantShoo_3_Noon_1_)
// v2 therefore tries three strategies, most-conservative first, and records which one hit.
// A tail-id match ALONE is not accepted: low-numbered ids collide across unrelated assets
// (e.g. OCTE_Comment_PretorFight_fight_3386 vs VO_EN_E09_MQ01_Main_polkovnik_korsunov_3386),
// so a token-overlap threshold must also be met.
var suffixOrdinal = new Dictionary<string, List<string>>(StringComparer.Ordinal);
var suffixNoCase = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
var byTailId = new Dictionary<string, List<string>>(StringComparer.Ordinal);
var allVoAssets = new List<string>();
foreach (var path in provider.Files.Keys)
{
    if (!path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) continue;
    var fname = Path.GetFileNameWithoutExtension(path);
    if (!fname.StartsWith("VO_", StringComparison.Ordinal)) continue;
    allVoAssets.Add(path);
    var rest = fname[3..];
    int idx = 0;
    while (true)
    {
        var key = rest[idx..];
        if (!suffixOrdinal.TryGetValue(key, out var l1)) { l1 = new List<string>(); suffixOrdinal[key] = l1; }
        l1.Add(path);
        if (!suffixNoCase.TryGetValue(key, out var l2)) { l2 = new List<string>(); suffixNoCase[key] = l2; }
        l2.Add(path);
        int next = rest.IndexOf('_', idx);
        if (next < 0) break;
        idx = next + 1;
    }
    var tm = Regex.Match(fname, @"_(\d{2,8})$");
    if (tm.Success)
    {
        var k = tm.Groups[1].Value;
        if (!byTailId.TryGetValue(k, out var lt)) { lt = new List<string>(); byTailId[k] = lt; }
        lt.Add(path);
    }
}
Console.WriteLine($"VO assets: {allVoAssets.Count}; suffix keys: {suffixOrdinal.Count}; tail-id keys: {byTailId.Count}");

var claimedAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

string Pick(List<string> c) =>
    c.FirstOrDefault(p => p.Contains("/Voiceover/", StringComparison.OrdinalIgnoreCase)) ?? c[0];

static HashSet<string> Toks(string s) =>
    new(s.Split('_', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length > 1), StringComparer.OrdinalIgnoreCase);

(string? path, string method) ResolveSid2(string sid)
{
    if (suffixOrdinal.TryGetValue(sid, out var c1)) return (Pick(c1), "SUFFIX_EXACT");
    if (suffixNoCase.TryGetValue(sid, out var c2)) return (Pick(c2), "SUFFIX_CASE_INSENSITIVE");

    var m = Regex.Match(sid, @"_(\d{2,8})$");
    if (!m.Success || !byTailId.TryGetValue(m.Groups[1].Value, out var cands)) return (null, "NONE");

    var sidToks = Toks(sid);
    if (sidToks.Count == 0) return (null, "NONE");
    string? best = null; double bestScore = 0;
    foreach (var p in cands)
    {
        var fn = Path.GetFileNameWithoutExtension(p);
        var at = Toks(fn.StartsWith("VO_", StringComparison.Ordinal) ? fn[3..] : fn);
        double score = (double)sidToks.Count(t => at.Contains(t)) / sidToks.Count;
        if (score > bestScore) { bestScore = score; best = p; }
    }
    // 0.60 chosen so a reordered/extra-token name still hits while an id collision
    // (which shares no meaningful token at all) is rejected.
    if (best != null && bestScore >= 0.60) return (best, $"TAILID_TOKENS:{bestScore:F2}");
    return (null, "NONE");
}

var masterRows = ParseCsv(masterDbCsv);
var masterBySid = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
foreach (var r in masterRows) masterBySid[r["SID"]] = r;
Console.WriteLine($"MASTER_DIALOGUE_DATABASE rows: {masterRows.Count}");

// index of all known SIDs, longest-first, for resolving foreign DebugName tokens
var allSidsByLength = masterBySid.Keys.OrderByDescending(s => s.Length).ToList();
var sidSet = new HashSet<string>(masterBySid.Keys, StringComparer.Ordinal);

var ubulkCache = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
byte[]? GetUbulk(string uassetPath)
{
    string ubulkPath = Path.ChangeExtension(uassetPath, ".ubulk");
    if (ubulkCache.TryGetValue(ubulkPath, out var cached)) return cached;
    byte[]? data = null;
    if (provider.Files.ContainsKey(ubulkPath))
    {
        try { data = provider.SaveAsset(ubulkPath); } catch { data = null; }
    }
    if (ubulkCache.Count > 400) ubulkCache.Clear();
    ubulkCache[ubulkPath] = data;
    return data;
}

bool IsRiff(byte[]? buf, long off, long size)
{
    if (buf == null || size < 4 || off < 0 || off + 4 > buf.LongLength) return false;
    return buf[off] == (byte)'R' && buf[off + 1] == (byte)'I' && buf[off + 2] == (byte)'F' && buf[off + 3] == (byte)'F';
}

string BaseName(string d) => d.Contains('\\') ? d[(d.LastIndexOf('\\') + 1)..] : d;
bool ContainsSid(string d, string sid) => BaseName(d).Contains(sid, StringComparison.Ordinal);
string ExtractVoiceVariant(string d)
{
    var m = Regex.Match(BaseName(d), @"Voice[A-Za-z0-9]+", RegexOptions.IgnoreCase);
    return m.Success ? m.Value : "";
}
bool IsNonVerbalText(string text)
{
    if (string.IsNullOrWhiteSpace(text)) return true;
    var t = text.Trim();
    if (t.StartsWith("(") && t.EndsWith(")")) return true;
    if (t is "..." or "…" or "-" or "—") return true;
    return false;
}

// Given a foreign DebugName like "S2_Live\VO_kogut_E03_MQ06_kogut_0_DefeatTopic_DefeatTopic_53952.wav",
// try to find which known SID it refers to (longest suffix match on the numeric-terminated token).
var foreignCache = new Dictionary<string, string>(StringComparer.Ordinal);
string ResolveForeignSid(string debugName)
{
    var bn = BaseName(debugName);
    if (foreignCache.TryGetValue(bn, out var hit)) return hit;
    string found = "";
    // fast path: trailing numeric id -> candidate SIDs ending with that id
    var m = Regex.Match(bn, @"_(\d{3,7})\.wav$", RegexOptions.IgnoreCase);
    if (m.Success)
    {
        string idTok = "_" + m.Groups[1].Value;
        foreach (var s in allSidsByLength)
        {
            if (!s.EndsWith(idTok, StringComparison.Ordinal)) continue;
            if (bn.Contains(s, StringComparison.Ordinal)) { found = s; break; }
        }
        if (found.Length == 0)
        {
            foreach (var s in allSidsByLength)
                if (s.EndsWith(idTok, StringComparison.Ordinal)) { found = s; break; }
        }
    }
    if (foreignCache.Count > 200000) foreignCache.Clear();
    foreignCache[bn] = found;
    return found;
}

string Norm(string s) => Regex.Replace((s ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}]", "");

var fullWriter = new StreamWriter(outFull, false, new UTF8Encoding(true));
fullWriter.WriteLine("SID,Speaker,EnglishText,PolishText_Official,ResolvedUassetPath,ResolveMethod,IsCutscenePath,IsNonVerbal," +
    "EN_TopMedia,EN_TopReal,EN_TopRiff,EN_Leaves,EN_LeafMedia,EN_LeafReal,EN_LeafRiff,EN_HasRealAudio,EN_IsSwitch," +
    "UA_TopMedia,UA_TopReal,UA_TopRiff,UA_Leaves,UA_LeafMedia,UA_LeafReal,UA_LeafRiff,UA_HasRealAudio,UA_IsSwitch," +
    "AnyRealAudio,AnyMediaAtAll,IsSwitchContainer,VoiceVariants,InternalRefVerdict,ForeignSid,ForeignSample," +
    "MasterHasVoiceAsset,GameReaderMatched,GameReaderMatchType,GameReaderScore,LiveStatus");

int nInput = 0, nResolved = 0, nUnresolved = 0, nErr = 0;
int nRealAudio = 0, nSwitch = 0, nNoAudio = 0, nNonVerbal = 0, nMismatch = 0, nAlias = 0, nUnknownRef = 0, nMatchRef = 0;
int nFlagFalseButReal = 0, nFlagTrueButNoAudio = 0;
var methodStats = new SortedDictionary<string, int>(StringComparer.Ordinal);
var sw = Stopwatch.StartNew();

foreach (var kv in masterBySid)
{
    string sid = kv.Key;
    var master = kv.Value;
    nInput++;
    string englishText = master.GetValueOrDefault("EnglishText", "");
    string polishText = master.GetValueOrDefault("PolishText_Official", "");
    string speaker = master.GetValueOrDefault("Speaker", "");
    bool masterFlag = master.GetValueOrDefault("HasVoiceAsset", "").Equals("TRUE", StringComparison.OrdinalIgnoreCase);
    bool grMatched = master.GetValueOrDefault("GameReaderMatched", "").Equals("TRUE", StringComparison.OrdinalIgnoreCase);
    string grType = master.GetValueOrDefault("GameReaderMatchType", "");
    string grScore = master.GetValueOrDefault("GameReaderScore", "");
    bool nonVerbal = IsNonVerbalText(englishText);

    try
    {
        var (uassetPath, resolveMethod) = ResolveSid2(sid);
        if (uassetPath != null) claimedAssets.Add(uassetPath);
        { var mk = resolveMethod.StartsWith("TAILID") ? "TAILID_TOKENS" : resolveMethod; methodStats.TryGetValue(mk, out var mc); methodStats[mk] = mc + 1; }
        if (uassetPath == null)
        {
            nUnresolved++;
            if (nonVerbal) nNonVerbal++;
            fullWriter.WriteLine($"{Csv(sid)},{Csv(speaker)},{Csv(englishText)},{Csv(polishText)},,{Csv(resolveMethod)},False,{nonVerbal}," +
                "0,0,0,0,0,0,0,False,False,0,0,0,0,0,0,0,False,False,False,False,False,,UNKNOWN,,," +
                $"{masterFlag},{grMatched},{Csv(grType)},{Csv(grScore)},{Csv(nonVerbal ? "NON_VERBAL_UNRESOLVED" : "UNRESOLVED_ASSET")}");
            continue;
        }
        nResolved++;
        bool cutscene = uassetPath.Contains("/Cutscenes/", StringComparison.OrdinalIgnoreCase);
        var ubulk = GetUbulk(uassetPath);
        var pkg = provider.LoadPackage(uassetPath);
        var akEvent = pkg.GetExports().OfType<UAkAudioEvent>().FirstOrDefault();

        if (akEvent?.EventCookedData == null)
        {
            fullWriter.WriteLine($"{Csv(sid)},{Csv(speaker)},{Csv(englishText)},{Csv(polishText)},{Csv(uassetPath)},{Csv(resolveMethod)},{cutscene},{nonVerbal}," +
                "0,0,0,0,0,0,0,False,False,0,0,0,0,0,0,0,False,False,False,False,False,,UNKNOWN,,," +
                $"{masterFlag},{grMatched},{Csv(grType)},{Csv(grScore)},{Csv("NO_EVENT_DATA")}");
            continue;
        }

        var counts = new Dictionary<string, int[]>(); // top, topReal, topRiff, leaves, leafMedia, leafReal, leafRiff
        var variants = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        string foreignSample = "";
        bool anyOwnToken = false;

        foreach (var langEntry in akEvent.EventCookedData.Value.EventLanguageMap)
        {
            if (langEntry.Value == null) continue;
            string lang = langEntry.Key.LanguageName.Text;
            string langKey = lang.Contains("English", StringComparison.OrdinalIgnoreCase) ? "EN"
                : lang.Contains("Ukrainian", StringComparison.OrdinalIgnoreCase) ? "UA" : "";
            if (langKey.Length == 0) continue;
            var ecd = langEntry.Value.Value;
            var c = new int[7];

            c[0] = ecd.Media.Length;
            foreach (var m in ecd.Media)
            {
                var wf = m.PackagedFile?.BulkData?.WemFile as FBulkDataDeferredByteData;
                long off = wf != null ? (long)wf.BulkData.Header.OffsetInFile : -1;
                long size = wf != null ? (long)wf.BulkData.Header.SizeOnDisk : -1;
                if (IsRiff(ubulk, off, size)) c[2]++;
                if (ContainsSid(m.DebugName.Text, sid)) { c[1]++; anyOwnToken = true; }
                else if (foreignSample.Length == 0) foreignSample = m.DebugName.Text;
                var v = ExtractVoiceVariant(m.DebugName.Text); if (v.Length > 0) variants.Add(v);
            }

            c[3] = ecd.SwitchContainerLeaves.Length;
            foreach (var leaf in ecd.SwitchContainerLeaves)
                foreach (var m in leaf.Media)
                {
                    c[4]++;
                    var wf = m.PackagedFile?.BulkData?.WemFile as FBulkDataDeferredByteData;
                    long off = wf != null ? (long)wf.BulkData.Header.OffsetInFile : -1;
                    long size = wf != null ? (long)wf.BulkData.Header.SizeOnDisk : -1;
                    if (IsRiff(ubulk, off, size)) c[6]++;
                    if (ContainsSid(m.DebugName.Text, sid)) { c[5]++; anyOwnToken = true; }
                    else if (foreignSample.Length == 0) foreignSample = m.DebugName.Text;
                    var v = ExtractVoiceVariant(m.DebugName.Text); if (v.Length > 0) variants.Add(v);
                }

            counts[langKey] = c;
        }

        var en = counts.TryGetValue("EN", out var e) ? e : new int[7];
        var ua = counts.TryGetValue("UA", out var u) ? u : new int[7];
        bool enReal = en[1] > 0 || en[5] > 0;
        bool uaReal = ua[1] > 0 || ua[5] > 0;
        bool enSwitch = en[3] > 0, uaSwitch = ua[3] > 0;
        bool anyReal = enReal || uaReal;
        bool anyMedia = (en[0] + en[4] + ua[0] + ua[4]) > 0;
        bool isSwitch = enSwitch || uaSwitch;

        // internal reference verdict
        string refVerdict, foreignSid = "";
        if (anyOwnToken) { refVerdict = "MATCH"; nMatchRef++; }
        else if (!anyMedia) { refVerdict = "UNKNOWN"; nUnknownRef++; }
        else
        {
            foreignSid = ResolveForeignSid(foreignSample);
            if (foreignSid.Length == 0) { refVerdict = "UNKNOWN"; nUnknownRef++; }
            else if (masterBySid.TryGetValue(foreignSid, out var fm)
                     && Norm(fm.GetValueOrDefault("EnglishText", "")) == Norm(englishText)
                     && Norm(englishText).Length > 0)
            { refVerdict = "ALIAS"; nAlias++; }
            else { refVerdict = "MISMATCH"; nMismatch++; }
        }

        string liveStatus;
        if (anyReal) { liveStatus = isSwitch ? "REAL_AUDIO_SWITCH" : "REAL_AUDIO"; nRealAudio++; if (isSwitch) nSwitch++; }
        else if (nonVerbal) { liveStatus = "NON_VERBAL"; nNonVerbal++; }
        else if (refVerdict is "ALIAS") liveStatus = "REAL_AUDIO_ALIASED";
        else if (anyMedia) liveStatus = "INTERNAL_MISMATCH";
        else { liveStatus = "NO_AUDIO"; nNoAudio++; }

        if (!masterFlag && anyReal) nFlagFalseButReal++;
        if (masterFlag && !anyReal) nFlagTrueButNoAudio++;

        fullWriter.WriteLine($"{Csv(sid)},{Csv(speaker)},{Csv(englishText)},{Csv(polishText)},{Csv(uassetPath)},{Csv(resolveMethod)},{cutscene},{nonVerbal}," +
            $"{en[0]},{en[1]},{en[2]},{en[3]},{en[4]},{en[5]},{en[6]},{enReal},{enSwitch}," +
            $"{ua[0]},{ua[1]},{ua[2]},{ua[3]},{ua[4]},{ua[5]},{ua[6]},{uaReal},{uaSwitch}," +
            $"{anyReal},{anyMedia},{isSwitch},{Csv(string.Join("|", variants))},{Csv(refVerdict)},{Csv(foreignSid)},{Csv(foreignSample)}," +
            $"{masterFlag},{grMatched},{Csv(grType)},{Csv(grScore)},{Csv(liveStatus)}");
    }
    catch (Exception ex)
    {
        nErr++;
        fullWriter.WriteLine($"{Csv(sid)},{Csv(speaker)},{Csv(englishText)},{Csv(polishText)},,\"EXC\",False,{nonVerbal}," +
            "0,0,0,0,0,0,0,False,False,0,0,0,0,0,0,0,False,False,False,False,False,,UNKNOWN,,," +
            $"{masterFlag},{grMatched},{Csv(grType)},{Csv(grScore)},{Csv("EXCEPTION: " + ex.Message)}");
    }

    if (nInput % 1000 == 0)
        Console.WriteLine($"Progress {nInput}/{masterBySid.Count} | real={nRealAudio} switch={nSwitch} noaudio={nNoAudio} mismatch={nMismatch} alias={nAlias} err={nErr} | {sw.Elapsed}");
}

fullWriter.Flush(); fullWriter.Dispose();

// reverse census: shipped VO assets that NO SID in the text database resolved to
var orphanAssets = allVoAssets.Where(p => !claimedAssets.Contains(p)).ToList();
using (var ow = new StreamWriter(outOrphanAssets, false, new UTF8Encoding(true)))
{
    ow.WriteLine("AssetPath,FileName,TopFolder,IsCutscene,IsDLC");
    foreach (var p in orphanAssets)
    {
        int i = p.IndexOf("/Voiceover/", StringComparison.OrdinalIgnoreCase);
        string top = i < 0 ? "(not-voiceover)" : p[(i + 11)..].Split('/')[0];
        ow.WriteLine($"{Csv(p)},{Csv(Path.GetFileNameWithoutExtension(p))},{Csv(top)},{p.Contains("/Cutscene", StringComparison.OrdinalIgnoreCase)},{p.Contains("DLC", StringComparison.OrdinalIgnoreCase)}");
    }
}
Console.WriteLine($"VO assets claimed by a SID: {claimedAssets.Count}; orphan VO assets: {orphanAssets.Count}");

var summary = new List<string>
{
    "=== _LIVE_FULL_POPULATION_SUMMARY.txt ===",
    "Full-population, switch-container-aware, RIFF-byte-verified live probe of EVERY SID in",
    "MASTER_DIALOGUE_DATABASE.csv. Read-only. No build/patch/deploy.",
    "",
    $"INPUT SIDs                            = {nInput}",
    $"  resolved to a VO .uasset            = {nResolved}",
    $"  NOT resolvable                      = {nUnresolved}",
    $"  exceptions                          = {nErr}",
    "",
    $"REAL AUDIO (own-token media found)    = {nRealAudio}",
    $"  of which SwitchContainer            = {nSwitch}",
    $"NO AUDIO (zero media anywhere)        = {nNoAudio}",
    $"NON_VERBAL                            = {nNonVerbal}",
    "",
    "=== INTERNAL REFERENCE AUDIT ===",
    $"MATCH                                 = {nMatchRef}",
    $"ALIAS (foreign token, identical text) = {nAlias}",
    $"MISMATCH (foreign token, other text)  = {nMismatch}",
    $"UNKNOWN                               = {nUnknownRef}",
    "",
    "=== MASTER_DB HasVoiceAsset FLAG vs LIVE TRUTH ===",
    $"flag=FALSE but real audio exists      = {nFlagFalseButReal}   <-- stale-flag false negatives",
    $"flag=TRUE but no real audio           = {nFlagTrueButNoAudio}  <-- stale-flag false positives",
    "",
    "",
    "=== RESOLVER v2 METHOD BREAKDOWN ===",
    string.Join(Environment.NewLine, methodStats.Select(kv => $"{kv.Key,-28} = {kv.Value}")),
    "",
    "=== REVERSE CENSUS: shipped VO assets vs the text database ===",
    $"VO_*.uasset shipped                   = {allVoAssets.Count}",
    $"  claimed by at least one SID         = {claimedAssets.Count}",
    $"  orphan (no SID resolves to them)    = {allVoAssets.Count - claimedAssets.Count}",
    "",
    $"Elapsed: {sw.Elapsed}",
};
File.WriteAllLines(outSummary, summary, new UTF8Encoding(true));
foreach (var l in summary) Console.WriteLine(l);
Console.WriteLine("DONE.");

static string Csv(string? s) => "\"" + (s ?? "").Replace("\"", "'") + "\"";

static List<Dictionary<string, string>> ParseCsv(string path)
{
    var text = File.ReadAllText(path, Encoding.UTF8);
    var rows = SplitCsvAll(text);
    var header = rows[0].Select(h => h.Trim('"', '\uFEFF')).ToList();
    var result = new List<Dictionary<string, string>>();
    for (int i = 1; i < rows.Count; i++)
    {
        if (rows[i].Count == 1 && rows[i][0].Length == 0) continue;
        var dict = new Dictionary<string, string>();
        for (int j = 0; j < header.Count && j < rows[i].Count; j++) dict[header[j]] = rows[i][j];
        if (dict.ContainsKey("SID") && dict["SID"].Length > 0) result.Add(dict);
    }
    return result;
}

// full RFC4180 reader: handles embedded newlines inside quoted fields
static List<List<string>> SplitCsvAll(string text)
{
    var rows = new List<List<string>>();
    var row = new List<string>();
    var sb = new StringBuilder();
    bool inQuotes = false;
    for (int i = 0; i < text.Length; i++)
    {
        char c = text[i];
        if (inQuotes)
        {
            if (c == '"')
            {
                if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                else inQuotes = false;
            }
            else sb.Append(c);
        }
        else
        {
            if (c == '"') inQuotes = true;
            else if (c == ',') { row.Add(sb.ToString()); sb.Clear(); }
            else if (c == '\r') { }
            else if (c == '\n') { row.Add(sb.ToString()); sb.Clear(); rows.Add(row); row = new List<string>(); }
            else sb.Append(c);
        }
    }
    row.Add(sb.ToString());
    rows.Add(row);
    return rows;
}
