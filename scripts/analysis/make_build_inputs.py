# -*- coding: utf-8 -*-
"""
Generate every build input from the single source of truth (data/MASTER_SIDS.csv).

The repo deliberately ships only the two master tables, not a dozen near-duplicate CSVs.
Everything the build pipeline consumes is derivable from MASTER_SIDS, so it is generated
here instead of being committed as a separate file that can silently drift.

Produces into build/:
    FINAL_lektor_mapping.csv     MAIN population   -> BatchEncoder, FullPatcher
    FULL_BATCH_wem_mapping.csv   SID -> MediaId / WemName
    PATCH_LIST_EXTRA.csv         EXTRA population  (SwitchContainer lines)
    PATCH_LIST_RECOVERED.csv     RECOVERED population (release 1.2)

Usage:
    python scripts/analysis/make_build_inputs.py [--audio-dir "<path to GameReader audio>"]

The audio directory is only needed to write absolute .ogg paths into the patch lists;
pass the folder holding "output1 (N).ogg". Defaults to the value in config.json if present.
"""
import argparse, csv, json, os, sys

csv.field_size_limit(10 ** 9)

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MASTER = os.path.join(ROOT, "data", "MASTER_SIDS.csv")
OUTDIR = os.path.join(ROOT, "build")


def read(path):
    return list(csv.DictReader(open(path, encoding="utf-8-sig", newline="")))


def write(name, rows, cols):
    os.makedirs(OUTDIR, exist_ok=True)
    p = os.path.join(OUTDIR, name)
    with open(p, "w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=cols, extrasaction="ignore")
        w.writeheader()
        w.writerows(rows)
    print(f"  {name:30} {len(rows):6} rows")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--audio-dir", default=None,
                    help='folder containing "output1 (N).ogg"')
    args = ap.parse_args()

    audio = args.audio_dir
    if not audio:
        cfg = os.path.join(ROOT, "config.json")
        if os.path.exists(cfg):
            try:
                audio = json.load(open(cfg, encoding="utf-8")).get("GameReaderAudioDir")
            except Exception:
                audio = None
    if not audio:
        print("note: no audio dir given -- .ogg paths in the patch lists will be left blank")
        audio = ""

    def ogg(n):
        return os.path.join(audio, f"output1 ({n}).ogg") if (audio and n) else ""

    if not os.path.exists(MASTER):
        sys.exit(f"missing {MASTER}")
    rows = read(MASTER)
    print(f"MASTER_SIDS.csv: {len(rows)} rows")
    print("generating build inputs:")

    # --- MAIN: everything mapped through the MAIN population
    main_rows = [{
        "SID": r["SID"],
        "MatchType": r["GameReaderMatchType"],
        "Score": r["GameReaderScore"],
        "Text": r["PolishText_Official"],
        "Audio1": ogg(r["MappedRecordingN"]),
        "Audio2": ogg(r["MappedRecordingN"]).replace("output1", "output2"),
        "EnglishMediaId": r["EnglishMediaId"],
        "UkrainianMediaId": r["UkrainianMediaId"],
    } for r in rows if r["MappedVia"] == "MAIN"]
    write("FINAL_lektor_mapping.csv", main_rows,
          ["SID", "MatchType", "Score", "Text", "Audio1", "Audio2",
           "EnglishMediaId", "UkrainianMediaId"])

    # NOTE: FULL_BATCH_wem_mapping.csv is written by BatchEncoder (the sole owner of that file,
    # with per-language MediaId columns) — generating it here too caused the two writers to
    # silently overwrite each other's schema.

    # --- EXTRA and RECOVERED populations share one schema
    def patch_rows(via):
        return [{
            "SID": r["SID"],
            "PatchType": r["PatchTypeNeeded"],
            "SourceLineIndexN": r["MappedRecordingN"],
            "GameReaderTTS_Text": r["GameReaderTTS_Text"],
            "SourceGameReaderOgg": ogg(r["MappedRecordingN"]),
            "WemName": f"gr_N{r['MappedRecordingN']}.wem",
            "WemSourceKind": "NEEDS_FRESH_ENCODE",
        } for r in rows if r["MappedVia"] == via and r["MappedRecordingN"]]

    cols = ["SID", "PatchType", "SourceLineIndexN", "GameReaderTTS_Text",
            "SourceGameReaderOgg", "WemName", "WemSourceKind"]
    write("PATCH_LIST_EXTRA.csv", patch_rows("EXTRA"), cols)

    write("PATCH_LIST_RECOVERED.csv", patch_rows("RECOVERED"), cols)

    # --- UA variant skip list: SIDs whose Ukrainian media slot is defective in the base game
    # (Is41UkrainianMismatch / no real UA audio) — FullPatcher must not touch them in UA builds.
    ua_skip = [r["SID"] for r in rows
               if r["MappedVia"] and (r["Is41UkrainianMismatch"] == "True"
                                      or (r["OverallStatus"] == "COVERED_IN_MOD"
                                          and r["UA_HasRealAudio"] == "False"))]
    p = os.path.join(OUTDIR, "UA_SKIP_SIDS.txt")
    with open(p, "w", encoding="utf-8") as f:
        f.write("\n".join(sorted(set(ua_skip))))
    print(f"  {'UA_SKIP_SIDS.txt':30} {len(set(ua_skip)):6} SIDs")

    print(f"\nwritten to {OUTDIR}")


if __name__ == "__main__":
    main()
