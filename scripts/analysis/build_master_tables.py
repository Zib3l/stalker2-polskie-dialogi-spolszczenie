# -*- coding: utf-8 -*-
"""
Consolidate every scattered CSV of this project into TWO master tables.

Why two and not one: a recording and a game SID are different things in a many-to-many
relation (one recording serves many SIDs; one SID may have several candidate recordings).
Forcing them into a single table would either duplicate rows or leave half the columns
empty. So:

    MASTER_SIDS.csv        one row per game dialogue SID   (33 528)
    MASTER_RECORDINGS.csv  one row per physical recording  (17 387)

They join on RecordingIndexN <-> MappedRecordingN / CandidateRecordings.

Read-only against every source. Writes only the two tables + README into this folder.
"""
import csv, os, sys, re, json, collections

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
csv.field_size_limit(10**9)

BASE = r"G:\ClaudeProjekt\Stalker 2\scripts"
OUT = os.path.join(BASE, "analysis_2026-08-24_long_research")
SCR = (r"C:\Users\zib3l\AppData\Local\Temp\claude\g--ClaudeProjekt-Stalker-2"
       r"\96b33127-edde-4fbb-8ff1-a14259f3e8fc\scratchpad")

def rd(p):
    if not os.path.exists(p):
        print(f"  !! missing: {p}")
        return []
    return list(csv.DictReader(open(p, encoding="utf-8-sig", newline="")))

def P(*a):
    return os.path.join(BASE, *a)

NRX = re.compile(r"output\d+ \((\d+)\)")
def nidx(s):
    m = NRX.search(s or "")
    return m.group(1) if m else ""

def wr(name, rows, cols):
    p = os.path.join(OUT, name)
    with open(p, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=cols, extrasaction="ignore")
        w.writeheader(); w.writerows(rows)
    print(f"  wrote {name}: {len(rows)} rows x {len(cols)} cols")

# ================================================================= load sources
print("loading sources ...")
live    = {r["SID"]: r for r in rd(os.path.join(OUT, "_LIVE_FULL_POPULATION_V2.csv"))}
master  = {r["SID"]: r for r in rd(P("MASTER_DIALOGUE_DATABASE.csv"))}
cover   = {r["SID"]: r for r in rd(os.path.join(OUT, "01_FINAL_COVERAGE.csv"))}
etap2   = {r["SID"]: r for r in rd(os.path.join(OUT, "_ETAP2_CORPUS_REVIEW.csv"))}
edit22  = {r["SID"]: r for r in rd(os.path.join(OUT, "14_EDITORIAL_STAGE_DIRECTIONS.csv"))}
wemmap  = {r["SID"]: r for r in rd(P("FULL_BATCH_wem_mapping.csv"))}
mainmap = {r["SID"]: r for r in rd(P("FINAL_lektor_mapping.csv"))}
extra   = {r["SID"]: r for r in rd(P("analysis_2026-08-24_extra_dialogue_full", "PATCH_LIST_FINAL.csv"))}
cutmap  = {r["SID"]: r for r in rd(P("CUTSCENE_SID_MAPPING_FULL.csv"))}
recs    = rd(P("analysis_2026-08-24_unused_gamereader", "ALL_GAMEREADER_RECORDINGS.csv"))
revaud  = {r["RecordingIndexN"]: r for r in rd(os.path.join(SCR, "REVERSE_RECORDING_AUDIT.csv"))}
deep    = {r["RecordingIndexN"]: r for r in rd(os.path.join(SCR, "ORPHAN_DEEP_MATCH.csv"))}
hashes  = json.load(open(os.path.join(SCR, "audio_hashes.json"))) if os.path.exists(os.path.join(SCR, "audio_hashes.json")) else {}

BUILDS = {
    "MAIN":    P("analysis_2026-08-24_mainmod_rebuild", "REBUILD_PATCH_REPORT.csv"),
    "MAIN_EN": P("analysis_2026-08-24_mainmod_english_rebuild", "REBUILD_PATCH_REPORT.csv"),
    "MAIN_UA": P("analysis_2026-08-24_mainmod_ua_combined", "REBUILD_PATCH_REPORT.csv"),
    "EXTRA":   P("analysis_2026-08-24_extra_dialogue_full", "EXTRA_DIALOGUE_PATCH_REPORT.csv"),
    # release 1.2 -- the two shipped variants
    "V12_EN":  P("analysis_2026-08-24_release_1.2", "english", "REBUILD_PATCH_REPORT.csv"),
    "V12_UA":  P("analysis_2026-08-24_release_1.2", "ukrainian", "REBUILD_PATCH_REPORT.csv"),
}
bstat = {k: {r["SID"]: r.get("Status", "") for r in rd(p)} for k, p in BUILDS.items()}
SHIPPED = {s for st in bstat.values() for s, v in st.items() if v == "OK"}
print(f"  live={len(live)} master={len(master)} cover={len(cover)} recs={len(recs)} shipped={len(SHIPPED)}")

# the 41 known Ukrainian-side-mismatch SIDs
p41 = P("analysis_2026-08-24_mainmod_rebuild", "REBUILD_41_SKIPPED_SIDS.txt")
S41 = set()
if os.path.exists(p41):
    S41 = {l.strip() for l in open(p41, encoding="utf-8-sig") if l.strip() and not l.startswith("#")}

# recording -> SIDs, from BOTH mapping tables (reading only one is the classic mistake here)
rec2sid = collections.defaultdict(set)
sid2rec_main, sid2rec_extra = {}, {}
for s, r in mainmap.items():
    n = nidx(r.get("Audio1", ""))
    if n:
        rec2sid[n].add(s); sid2rec_main[s] = n
for s, r in extra.items():
    n = (r.get("SourceLineIndexN") or "").strip()
    if n:
        rec2sid[n].add(s); sid2rec_extra[s] = n
recovered = {r["SID"]: r for r in rd(P("analysis_2026-08-24_release_1.2", "PATCH_LIST_RECOVERED.csv"))}
sid2rec_recovered = {}
for s2, r in recovered.items():
    n = (r.get("SourceLineIndexN") or "").strip()
    if n:
        rec2sid[n].add(s2); sid2rec_recovered[s2] = n
print(f"  recovered (1.2) patch rows: {len(recovered)}")

# cutscene recordings
cut_rec, cutrec2sid = set(), collections.defaultdict(set)
for rel in ["CUTSCENE_PATCH_LIST_FULL.csv", "CUTSCENE_PATCH_LIST.csv"]:
    for r in rd(P(rel)):
        n = nidx(r.get("Audio1", ""))
        if n:
            cut_rec.add(n); cutrec2sid[n].add(r["SID"])

# ================================================================= MASTER_SIDS
print("\nbuilding MASTER_SIDS ...")
sid_rows = []
for sid in sorted(master):
    lv, ms = live.get(sid, {}), master[sid]
    cv, e2 = cover.get(sid, {}), etap2.get(sid, {})
    st = {k: bstat[k].get(sid, "ABSENT") for k in BUILDS}
    shipped = sid in SHIPPED
    n_main, n_extra = sid2rec_main.get(sid, ""), sid2rec_extra.get(sid, "")
    n_rec = sid2rec_recovered.get(sid, "")

    is_cut = (lv.get("IsCutscenePath") == "True") or sid in cutmap or sid.startswith("C_")
    if shipped:
        overall = "COVERED_IN_MOD"
    elif is_cut:
        overall = "CUTSCENE_OUT_OF_SCOPE"
    elif lv.get("IsNonVerbal") == "True":
        overall = "NON_VERBAL_OUT_OF_SCOPE"
    elif lv.get("LiveStatus") in ("UNRESOLVED_ASSET", "NON_VERBAL_UNRESOLVED", "NO_AUDIO"):
        overall = "NO_VOICE_ASSET_IN_GAME"
    elif lv.get("LiveStatus") == "INTERNAL_MISMATCH":
        overall = "INTERNAL_MISMATCH"
    elif cv.get("Category") == "C_COVERABLE_WITH_UNUSED_RECORDING":
        overall = "OPEN_RECORDING_EXISTS"
    elif cv.get("Category") == "C2_COVERABLE_BY_REUSING_A_USED_RECORDING":
        overall = "OPEN_REUSE_POSSIBLE"
    elif cv.get("Category") == "F_ATTEMPTED_BUT_FAILED":
        overall = "OPEN_BUILD_FAILED"
    elif cv.get("Category") == "E_NEEDS_NEW_RECORDING":
        overall = "OPEN_NEEDS_NEW_RECORDING"
    else:
        overall = "UNKNOWN"

    sid_rows.append({
        "SID": sid,
        "OverallStatus": overall,
        "CoverageCategory": cv.get("Category", ""),
        "InShippedMod": shipped,
        "IsCutscene": is_cut,
        "IsNonVerbal": lv.get("IsNonVerbal", ""),
        "Speaker": ms.get("Speaker", ""),
        "EnglishText": ms.get("EnglishText", ""),
        "PolishText_Official": ms.get("PolishText_Official", ""),
        "UkrainianText_Official": ms.get("UkrainianText_Official", ""),
        # --- live audio ground truth
        "LiveStatus": lv.get("LiveStatus", ""),
        "AnyRealAudio": lv.get("AnyRealAudio", ""),
        "IsSwitchContainer": lv.get("IsSwitchContainer", ""),
        "PatchTypeNeeded": ("SWITCH_CONTAINER_PATCH" if lv.get("IsSwitchContainer") == "True"
                            else "NORMAL_MEDIA_PATCH" if lv.get("AnyRealAudio") == "True" else ""),
        "EN_HasRealAudio": lv.get("EN_HasRealAudio", ""),
        "UA_HasRealAudio": lv.get("UA_HasRealAudio", ""),
        "EN_TopMedia": lv.get("EN_TopMedia", ""), "UA_TopMedia": lv.get("UA_TopMedia", ""),
        "EN_Leaves": lv.get("EN_Leaves", ""), "UA_Leaves": lv.get("UA_Leaves", ""),
        "VoiceVariants": lv.get("VoiceVariants", ""),
        "ResolvedUassetPath": lv.get("ResolvedUassetPath", ""),
        "ResolveMethod": lv.get("ResolveMethod", ""),
        # --- internal reference
        "InternalRefVerdict": lv.get("InternalRefVerdict", ""),
        "ForeignSid": lv.get("ForeignSid", ""),
        "Is41UkrainianMismatch": sid in S41,
        # --- media ids / wem
        "EnglishMediaId": mainmap.get(sid, {}).get("EnglishMediaId", ""),
        "UkrainianMediaId": mainmap.get(sid, {}).get("UkrainianMediaId", ""),
        "PolishMediaId": wemmap.get(sid, {}).get("MediaId", ""),
        "PolishWemName": wemmap.get(sid, {}).get("WemName", ""),
        # --- recording link
        "MappedRecordingN": n_main or n_extra or n_rec,
        "MappedVia": "MAIN" if n_main else ("EXTRA" if n_extra else ("RECOVERED" if n_rec else "")),
        "CandidateRecordings": cv.get("MatchingRecordings", ""),
        "CandidateUnusedRecordings": cv.get("MatchingUnusedRecordings", ""),
        # --- text comparison
        "GameReaderMatched": ms.get("GameReaderMatched", ""),
        "GameReaderMatchType": ms.get("GameReaderMatchType", ""),
        "GameReaderScore": ms.get("GameReaderScore", ""),
        "GameReaderTTS_Text": e2.get("GameReaderTTS_Text", ""),
        "TTSClass": e2.get("Class", ""),
        "TTSCascadeLevel": e2.get("CascadeLevel", ""),
        "TTSTransforms": e2.get("Transforms", ""),
        "TextIdentical": e2.get("Identical", ""),
        # --- build status
        "Status_MAIN": st["MAIN"], "Status_MAIN_EN": st["MAIN_EN"],
        "Status_MAIN_UA": st["MAIN_UA"], "Status_EXTRA": st["EXTRA"],
        "Status_Release12_English": st["V12_EN"], "Status_Release12_Ukrainian": st["V12_UA"],
        # --- flags
        "NeedsEditorialDecision": sid in edit22,
        "EditorialNote": edit22.get(sid, {}).get("Decision", ""),
        "CutsceneRoot": cutmap.get(sid, {}).get("CutsceneRoot", ""),
        "LegacyHasVoiceAssetFlag": ms.get("HasVoiceAsset", ""),
    })

wr("MASTER_SIDS.csv", sid_rows, list(sid_rows[0].keys()))
print("  OverallStatus:", dict(collections.Counter(r["OverallStatus"] for r in sid_rows).most_common()))

# ============================================================== MASTER_RECORDINGS
print("\nbuilding MASTER_RECORDINGS ...")
rec_rows = []
for r in recs:
    n = r["RecordingIndexN"]
    ra, dp = revaud.get(n, {}), deep.get(n, {})
    consumers = sorted(rec2sid.get(n, set()))
    shipped_consumers = [s for s in consumers if s in SHIPPED]
    o1, o2 = os.path.basename(r["GameReaderSourceOgg1"]), os.path.basename(r["GameReaderSourceOgg2"])
    h1, h2 = hashes.get(o1, {}), hashes.get(o2, {})

    cat = ra.get("Category", "")
    if cat in ("A_USED", "B_MULTI_USED"):
        usage = "USED_MULTI" if cat == "B_MULTI_USED" else "USED"
    elif cat == "E_CUTSCENE":
        usage = "UNUSED_CUTSCENE"
    elif cat == "D_DUPLICATE":
        usage = "UNUSED_DUPLICATE_TEXT"
    elif cat == "C1_ORPHAN_POTENTIAL_LOST_MAPPING":
        usage = "UNUSED_POTENTIAL_LOST_MAPPING"
    else:
        dv = dp.get("DeepVerdict", "")
        usage = {
            "POTENTIAL_LOST_MAPPING": "UNUSED_POTENTIAL_LOST_MAPPING",
            "POTENTIAL_LOST_MAPPING_REVIEW": "UNUSED_POTENTIAL_LOST_MAPPING_REVIEW",
            "TARGET_IS_CUT_OR_DLC_CONTENT": "UNUSED_TARGET_HAS_NO_VOICE_ASSET",
            "NO_MATCH_CONFIRMED": "UNUSED_NO_MATCH_IN_GAME",
            "TARGET_ALREADY_SHIPPED": "UNUSED_TARGET_ALREADY_COVERED",
            "TARGET_UNUSABLE_OTHER": "UNUSED_TARGET_UNUSABLE",
        }.get(dv, "UNUSED_UNCLASSIFIED")

    rec_rows.append({
        "RecordingIndexN": n,
        "UsageStatus": usage,
        "IsUsed": bool(shipped_consumers),
        "UsedBySIDCount": len(shipped_consumers),
        "UsedBySIDs": ";".join(shipped_consumers[:40]),
        "IsCutsceneRecording": n in cut_rec,
        "CutsceneSIDs": ";".join(sorted(cutrec2sid.get(n, set()))[:20]),
        "GameReaderTTS_Text": r["GameReaderTTS_Text"],
        "TextLength": len(r["GameReaderTTS_Text"]),
        "Ogg1": r["GameReaderSourceOgg1"], "Ogg2": r["GameReaderSourceOgg2"],
        "Ogg1_Sha1": h1.get("sha1", ""), "Ogg1_Bytes": h1.get("size", ""),
        "Ogg2_Sha1": h2.get("sha1", ""), "Ogg2_Bytes": h2.get("size", ""),
        "IdenticalAudioTwins": ra.get("IdenticalAudioTwins", ""),
        "IdenticalTextTwins": ra.get("IdenticalTextTwins", ""),
        "TwinAlreadyUsed": ra.get("TwinAlreadyUsed", ""),
        "PolishWemName": ";".join(sorted({wemmap.get(s, {}).get("WemName", "")
                                          for s in shipped_consumers if wemmap.get(s)})[:5]),
        "GameSIDsWithSameText": ra.get("GameSIDsWithSameText", ""),
        "OpenTargetSIDCount": ra.get("GameSIDsOpenWithRealAudio", ""),
        "OpenTargetSIDs": ra.get("OpenCandidateSIDs", ""),
        "BestCandidateSID": dp.get("MatchedSID", "") or ra.get("BestCandidateSID", ""),
        # the deep pass carries the matched text; first-pass candidates need it looked up
        "BestCandidateOfficialText": (
            dp.get("MatchedOfficialText")
            or live.get(dp.get("MatchedSID") or ra.get("BestCandidateSID", ""), {})
                   .get("PolishText_Official", "")),
        "BestCandidateEnglishText": ra.get("BestCandidateEnglishText", ""),
        "BestCandidateIsSwitch": dp.get("TargetIsSwitch", "") or ra.get("BestCandidateIsSwitch", ""),
        "BestCandidateLiveStatus": dp.get("TargetLiveStatus", ""),
        "DeepMatchClass": dp.get("MatchClass", ""),
        "DeepMatchTransforms": dp.get("MatchTransforms", ""),
        "FirstPassCategory": cat,
        "DeepVerdict": dp.get("DeepVerdict", ""),
    })

wr("MASTER_RECORDINGS.csv", rec_rows, list(rec_rows[0].keys()))
print("  UsageStatus:", dict(collections.Counter(r["UsageStatus"] for r in rec_rows).most_common()))
print("\ndone.")
