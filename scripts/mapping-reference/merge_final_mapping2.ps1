# REFERENCE / HISTORICAL — not required to build the mod. data/FINAL_lektor_mapping.csv (repo
# root) is the finished output of this merge step; kept only for reproducibility. Consumes
# intermediate CSVs (sid_audio_mapping_combined.csv, sid_media_mapping.csv) that this repo does
# not include — see docs/architecture.md for how those were produced. Adapt paths before use.

$Root = "G:\ClaudeProjekt\Stalker 2"
$AudioMap = Import-Csv "$Root\scripts\sid_audio_mapping_combined.csv"
$MediaMap = Import-Csv "$Root\scripts\sid_media_mapping.csv"

$mediaLookup = @{}
foreach ($row in $MediaMap) {
    if ($row.Found -eq "true" -and -not $mediaLookup.ContainsKey($row.SID)) {
        $mediaLookup[$row.SID] = $row
    }
}

$seen = @{}
$results = foreach ($row in $AudioMap) {
    $sid = $row.SID
    if ($seen.ContainsKey($sid)) { continue }
    $seen[$sid] = $true
    if ($mediaLookup.ContainsKey($sid)) {
        $m = $mediaLookup[$sid]
        [pscustomobject]@{
            SID             = $sid
            MatchType       = $row.MatchType
            Score           = $row.Score
            Text            = $row.Text
            Audio1          = $row.Audio1
            Audio2          = $row.Audio2
            EnglishMediaId  = $m.EnglishMediaId
            UkrainianMediaId = $m.UkrainianMediaId
        }
    }
}

$results | Export-Csv "$Root\scripts\FINAL_lektor_mapping.csv" -NoTypeInformation -Encoding UTF8
Write-Host "Unique audio-matched SIDs: $($seen.Count)"
Write-Host "Media-verified SIDs available: $($mediaLookup.Count)"
Write-Host "Final complete chain: $($results.Count)"
