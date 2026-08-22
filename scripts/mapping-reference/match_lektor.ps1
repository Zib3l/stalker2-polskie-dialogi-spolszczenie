# REFERENCE / HISTORICAL — not required to build the mod. data/FINAL_lektor_mapping.csv (repo
# root) is the finished output of this pipeline stage; this script is kept only so the exact-match
# step is reproducible. Needs its own local inputs (GameReader's PL line/audio export, a Polish
# sid_phrase_* text dump exported via S2HOCMM — see docs/architecture.md). Adapt paths before use.
#
# Matches GameReader's Polish TTS audio lines to STALKER 2 dialogue SIDs
# using the official Polish localization JSON dumped via S2HOCMM.

$Root = "G:\ClaudeProjekt\Stalker 2"
$GameReaderLines = "$Root\S.T.A.L.K.E.R. 2\subtitles\wersja po edycji do TTS\subtitlesPL (TTS ready).txt"
$AudioDir = "$Root\S.T.A.L.K.E.R. 2\audio"
$LocFile = "$Root\loc_json\Localization_PL.json"
$OutCsv = "$Root\scripts\sid_audio_mapping.csv"

function Normalize($s) {
    $s.Trim().ToLower() -replace '\s+', ' ' -replace '[.,!?„”"…:;]', ''
}

# 1. Index GameReader lines by normalized text -> line number (1-based).
#    Keep a list per key since some lines repeat (e.g. "Co?").
$grLines = Get-Content -Path $GameReaderLines -Encoding UTF8
$grIndex = @{}
for ($i = 0; $i -lt $grLines.Count; $i++) {
    $key = Normalize $grLines[$i]
    if (-not $key) { continue }
    if (-not $grIndex.ContainsKey($key)) { $grIndex[$key] = [System.Collections.Generic.List[int]]::new() }
    $grIndex[$key].Add($i + 1)
}
Write-Host "Indexed $($grIndex.Count) unique GameReader lines (from $($grLines.Count) total)."

# 2. Load Polish localization JSON, keep only sid_phrase_* keys (actual spoken VO lines,
#    as opposed to sid_topic_* which are player dialogue-choice labels).
$loc = Get-Content -Path $LocFile -Raw -Encoding UTF8 | ConvertFrom-Json
$phraseProps = $loc.PSObject.Properties | Where-Object { $_.Name -like 'sid_phrase_*' -and $_.Value }
Write-Host "Loaded $($phraseProps.Count) non-empty sid_phrase_* entries from $LocFile."

# 3. Match: for each SID's Polish text, find it in the GameReader index.
$results = [System.Collections.Generic.List[object]]::new()
$matchedCount = 0
foreach ($prop in $phraseProps) {
    $sid = $prop.Name -replace '^sid_phrase_', ''
    $key = Normalize $prop.Value
    if ($grIndex.ContainsKey($key)) {
        $matchedCount++
        foreach ($n in $grIndex[$key]) {
            $results.Add([pscustomobject]@{
                SID     = $sid
                AKEvent = "VO_$sid"
                LineNum = $n
                Text    = $prop.Value
                Audio1  = "$AudioDir\output1 ($n).ogg"
                Audio2  = "$AudioDir\output2 ($n).ogg"
            })
        }
    }
}

$results | Export-Csv -Path $OutCsv -NoTypeInformation -Encoding UTF8
Write-Host "Matched $matchedCount of $($phraseProps.Count) sid_phrase_ entries ($($results.Count) rows, some SIDs share duplicate lines)."
Write-Host "Written to $OutCsv"
