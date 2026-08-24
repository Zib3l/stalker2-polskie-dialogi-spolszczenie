# -*- coding: utf-8 -*-
"""
TTS transformation engine (v2).

Purpose: decide whether a difference between the game's OFFICIAL Polish subtitle text
and the GameReader/TTS recording script is a *pure rendering difference* (the recording
says the same words, so the WEM is safe to reuse) or a genuine content difference
(a new recording is required).

Method: a cascade of normalizers applied in increasing order of aggressiveness. The FIRST
level at which the two strings become identical is the verdict, and the transformations
that demonstrably contribute are recorded by name. Deliberately explainable -- a decision
is never made on a similarity score alone.

v2 additions, all driven by QC of the v1 output (see _QC_SAMPLE.txt):
  1. Polish phonetic respelling   "stalkerze" -> "stalkeze"  (rz == z-dot)
  2. Acronym spelled out          "IBAOC"     -> "Ibea-o-ce"
  3. Stage direction vs invented  "(umiera)"  -> "Umieram..."   <- NOT a reuse case

Read-only analysis helper. No game files touched.
"""
import re
import unicodedata
from difflib import SequenceMatcher

# ---------------------------------------------------------------- number words

ONES = {0: "zero", 1: "jeden", 2: "dwa", 3: "trzy", 4: "cztery", 5: "pięć", 6: "sześć",
        7: "siedem", 8: "osiem", 9: "dziewięć", 10: "dziesięć", 11: "jedenaście",
        12: "dwanaście", 13: "trzynaście", 14: "czternaście", 15: "piętnaście",
        16: "szesnaście", 17: "siedemnaście", 18: "osiemnaście", 19: "dziewiętnaście"}
TENS = {2: "dwadzieścia", 3: "trzydzieści", 4: "czterdzieści", 5: "pięćdziesiąt",
        6: "sześćdziesiąt", 7: "siedemdziesiąt", 8: "osiemdziesiąt", 9: "dziewięćdziesiąt"}
HUNDREDS = {1: "sto", 2: "dwieście", 3: "trzysta", 4: "czterysta", 5: "pięćset",
            6: "sześćset", 7: "siedemset", 8: "osiemset", 9: "dziewięćset"}


def num_to_words(n):
    """Nominative Polish for 0..999999 -- enough for in-game quantities, coupons, dates."""
    if n < 0:
        return ""
    if n < 20:
        return ONES[n]
    if n < 100:
        t, r = divmod(n, 10)
        return TENS[t] + ("" if r == 0 else " " + ONES[r])
    if n < 1000:
        h, r = divmod(n, 100)
        return HUNDREDS[h] + ("" if r == 0 else " " + num_to_words(r))
    if n < 1000000:
        th, r = divmod(n, 1000)
        if th == 1:
            word = "tysiąc"
        elif 2 <= th <= 4:
            word = num_to_words(th) + " tysiące"
        else:
            word = num_to_words(th) + " tysięcy"
        return word + ("" if r == 0 else " " + num_to_words(r))
    return ""


IRREG_NUM = {
    1: ["jeden", "jednego", "jednym", "jedna", "jedną", "jednej"],
    2: ["dwa", "dwie", "dwóch", "dwoma"],
    3: ["trzy", "trzech", "trzema"],
    4: ["cztery", "czterech", "czterema"],
    5: ["pięć", "pięciu", "pięcioma"],
    6: ["sześć", "sześciu", "sześcioma"],
    7: ["siedem", "siedmiu", "siedmioma"],
    8: ["osiem", "ośmiu", "ośmioma"],
    9: ["dziewięć", "dziewięciu"],
    10: ["dziesięć", "dziesięciu"],
    100: ["sto", "stu"],
    1000: ["tysiąc", "tysiąca", "tysięcy", "tysiące"],
}


def num_variants(n):
    """Plausible spoken renderings incl. common inflections, as bare letter keys."""
    base = num_to_words(n)
    if not base:
        return set()
    out = {base}
    out.update(IRREG_NUM.get(n, []))
    return {letters_only(x) for x in out if x}


# ---------------------------------------------------------------- abbreviations

ABBREV = {
    "np.": "na przykład", "itd.": "i tak dalej", "itp.": "i tym podobne",
    "tzn.": "to znaczy", "tzw.": "tak zwany", "m.in.": "między innymi",
    "godz.": "godzina", "min.": "minut", "sek.": "sekund", "ok.": "około",
    "str.": "strona", "nr": "numer", "dr": "doktor", "prof.": "profesor",
    "km": "kilometr", "kg": "kilogram", "mm": "milimetr", "cm": "centymetr",
    "szt.": "sztuk", "ul.": "ulica", "św.": "święty", "płk": "pułkownik",
    "kpt.": "kapitan", "por.": "porucznik", "sierż.": "sierżant", "gen.": "generał",
}

# ---------------------------------------------------------------- normalizers

DASHES = dict.fromkeys(map(ord, "‐‑‒–—―−"), "-")
QUOTES = {
    **dict.fromkeys(map(ord, "‘’‚‛′´`"), "'"),
    **dict.fromkeys(map(ord, "“”„‟«»″"), '"'),
}
SPACES = dict.fromkeys(map(ord, "       \t"), " ")


def n_unicode(s):
    """NFC, plus unify dash / quote / space variants and the ellipsis character."""
    s = unicodedata.normalize("NFC", s)
    s = s.translate(DASHES).translate(QUOTES).translate(SPACES)
    return s.replace("…", "...")


def n_case(s):
    return s.lower()


def n_whitespace(s):
    return re.sub(r"\s+", " ", s).strip()


def n_ellipsis(s):
    """Any run of 2+ dots is one pause marker."""
    return re.sub(r"\.{2,}", ".", s)


def n_hyphenjoin(s):
    """
    A hyphen or apostrophe BETWEEN two letters is a spelling aid, not a word break:
    the voice reads "Nu-da!" as one word "Nuda". Remove it without inserting a space,
    which plain punctuation-stripping would wrongly do.
    """
    return re.sub(r"(?<=\w)[-'’](?=\w)", "", s, flags=re.UNICODE)


def n_punct(s):
    """Drop everything that is not a letter, digit or space -- punctuation is not spoken."""
    return n_whitespace(re.sub(r"[^\w\s]", " ", s, flags=re.UNICODE))


def n_ttsmarkup(s):
    """Strip TTS / stage markup: [..], <..>, {..} and *emphasis*."""
    s = re.sub(r"\[[^\]]*\]", " ", s)
    s = re.sub(r"<[^>]*>", " ", s)
    s = re.sub(r"\{[^}]*\}", " ", s)
    s = re.sub(r"\*+", " ", s)
    return n_whitespace(s)


def n_elongation(s):
    """Collapse TTS stretching: 'nieeee' -> 'nie', 'hmmm' -> 'hm'."""
    return re.sub(r"(\w)\1{2,}", r"\1", s, flags=re.UNICODE)


def n_doubled(s):
    """Collapse ALL repeated letters -- catches a stretch written with only two chars."""
    return re.sub(r"(\w)\1+", r"\1", s, flags=re.UNICODE)


def n_abbrev(s):
    low = s.lower()
    for k, v in sorted(ABBREV.items(), key=lambda kv: -len(kv[0])):
        low = re.sub(r"(?<![\w.])" + re.escape(k) + r"(?![\w])", v, low)
    return low


def n_numbers(s):
    """Replace every digit run with its canonical spoken form."""
    def rep(m):
        try:
            n = int(m.group(0))
        except ValueError:
            return m.group(0)
        w = num_to_words(n)
        return " " + (letters_only(w) if w else m.group(0)) + " "
    return n_whitespace(re.sub(r"\d+", rep, s))


def n_nospace(s):
    """
    Drop word boundaries entirely. A fixed recording has no audible spaces, so an identical
    letter sequence is an identical utterance: "Ha-ha" == "Ha ha" == "Haha".
    """
    return re.sub(r"\s+", "", s, flags=re.UNICODE)


def letters_only(s):
    return re.sub(r"[^\w]", "", s, flags=re.UNICODE).lower()


def strip_diacritics(s):
    return "".join(c for c in unicodedata.normalize("NFD", s)
                   if unicodedata.category(c) != "Mn")


# --------------------------------------------- Polish phonetics & acronyms (v2)

def n_phonetic(s):
    """Polish spellings that are pronounced identically ('rz' == 'ż', 'ó' == 'u', 'ch' == 'h')."""
    s = s.replace("rz", "ż").replace("ó", "u").replace("ch", "h")
    return s.replace("dż", "ż").replace("dź", "ź")


# how a Polish voice says each letter of an acronym
LETTER_NAME = {
    "a": "a", "b": "be", "c": "ce", "d": "de", "e": "e", "f": "ef", "g": "gie",
    "h": "ha", "i": "i", "j": "jot", "k": "ka", "l": "el", "m": "em", "n": "en",
    "o": "o", "p": "pe", "q": "ku", "r": "er", "s": "es", "t": "te", "u": "u",
    "v": "fau", "w": "wu", "x": "iks", "y": "igrek", "z": "zet",
}

ACRONYM_RX = re.compile(r"\b[A-ZĄĆĘŁŃÓŚŹŻ]{2,8}\b")


def spell_acronym(tok):
    return "".join(LETTER_NAME.get(c, c) for c in tok.lower())


def _collapse(s):
    return re.sub(r"(.)\1+", r"\1", s)


def acronym_expansion_ok(official, tts):
    """
    True when every ALL-CAPS acronym in the official text is accounted for in the TTS text
    -- either spelled out letter by letter, or read as a plain word -- and nothing else of
    substance differs.
    """
    acs = ACRONYM_RX.findall(official)
    if not acs:
        return False
    o = letters_only(n_unicode(official))
    t = letters_only(n_unicode(tts))
    for ac in sorted(set(acs), key=len, reverse=True):
        a = ac.lower()
        spelled = letters_only(spell_acronym(a))
        if spelled and spelled in t:
            # letter-by-letter reading: IBAOC -> "Ibea-o-ce" == "i be a o ce"
            o = o.replace(a, "@")
            t = t.replace(spelled, "@")
        else:
            # read as a plain word, allowing repeated letters to collapse: MOTT -> "Mot"
            ad = _collapse(a)
            if ad and ad in _collapse(t):
                o = _collapse(o).replace(ad, "@")
                t = _collapse(t).replace(ad, "@")
            else:
                return False
    return n_phonetic(o) == n_phonetic(t)


STAGE_RX = re.compile(r"^\s*[(\[*].*[)\]*]\s*$", re.S)


def is_stage_direction(s):
    """A non-verbal cue, in any of the notations used across the two text sources:
    "(smiech)" in the official subtitles, "*Smiech*" or "[smiech]" in the TTS script."""
    return bool(STAGE_RX.match(s or ""))


# ---------------------------------------------------------------- cascade

# (label, chain) -- each level includes all the previous ones
LEVELS = [
    ("L0_identical",       []),
    ("L1_unicode",         [n_unicode]),
    ("L2_whitespace_case", [n_unicode, n_whitespace, n_case]),
    ("L3_ellipsis",        [n_unicode, n_ellipsis, n_whitespace, n_case]),
    ("L4_ttsmarkup",       [n_unicode, n_ttsmarkup, n_ellipsis, n_whitespace, n_case]),
    ("L5_punctuation",     [n_unicode, n_ttsmarkup, n_ellipsis, n_hyphenjoin, n_punct, n_case]),
    ("L6_abbrev",          [n_unicode, n_ttsmarkup, n_abbrev, n_ellipsis, n_hyphenjoin, n_punct, n_case]),
    ("L7_numbers",         [n_unicode, n_ttsmarkup, n_abbrev, n_ellipsis, n_hyphenjoin, n_punct, n_numbers, n_case]),
    ("L8_elongation",      [n_unicode, n_ttsmarkup, n_abbrev, n_ellipsis, n_hyphenjoin, n_punct, n_numbers, n_case, n_elongation]),
    ("L9_doubled",         [n_unicode, n_ttsmarkup, n_abbrev, n_ellipsis, n_hyphenjoin, n_punct, n_numbers, n_case, n_doubled]),
    ("L10_wordbreaks",     [n_unicode, n_ttsmarkup, n_abbrev, n_ellipsis, n_hyphenjoin, n_punct, n_numbers, n_case, n_doubled, n_nospace]),
    ("L11_phonetic",       [n_unicode, n_ttsmarkup, n_abbrev, n_ellipsis, n_hyphenjoin, n_punct, n_numbers, n_case, n_doubled, n_nospace, n_phonetic]),
    ("L12_diacritics",     [n_unicode, n_ttsmarkup, n_abbrev, n_ellipsis, n_hyphenjoin, n_punct, n_numbers, n_case, n_doubled, n_nospace, n_phonetic, strip_diacritics]),
]

# levels at or below this index are a pure rendering difference => SAFE.
# L12 (diacritics-blind) is deliberately NOT auto-safe: in Polish a missing diacritic can
# be a different word, so those go to manual review instead.
SAFE_MAX_LEVEL = 11


def apply_chain(s, chain):
    for f in chain:
        s = f(s)
    return s


def cascade(a, b):
    """First level at which the two texts coincide -- (label, index), or (None, None)."""
    for i, (label, chain) in enumerate(LEVELS):
        if apply_chain(a, chain) == apply_chain(b, chain):
            return label, i
    return None, None


# ---------------------------------------------------------------- diff analysis

def strip_ws(s):
    return re.sub(r"\s+", "", s)


def which_transforms(a, b):
    """Name every individual transformation that demonstrably helps close the gap."""
    found = []
    ba, bb = n_unicode(a), n_unicode(b)
    if a != b and strip_ws(a) == strip_ws(b):
        found.append("whitespace")
    if ba != a or bb != b:
        found.append("unicode_punct_variants")
    if bool(re.search(r"\d", ba)) != bool(re.search(r"\d", bb)):
        found.append("digits_to_words")
    if any(k in ba.lower() or k in bb.lower() for k in ABBREV):
        found.append("abbreviation")
    if ("..." in ba) != ("..." in bb):
        found.append("ellipsis_pause")
    if re.search(r"(\w)\1{2,}", ba, re.UNICODE) or re.search(r"(\w)\1{2,}", bb, re.UNICODE):
        found.append("elongation")
    if re.search(r"[\[\]<>{}*]", ba + bb):
        found.append("tts_markup")
    if ba.lower() == bb.lower() and ba != bb:
        found.append("case")
    ap, bp = n_punct(ba), n_punct(bb)
    if ap != ba or bp != bb:
        found.append("punctuation_only" if ap.lower() == bp.lower() else "punctuation")
    if n_hyphenjoin(ba) != ba or n_hyphenjoin(bb) != bb:
        found.append("hyphen_join")
    if strip_diacritics(ba.lower()) == strip_diacritics(bb.lower()) and ba.lower() != bb.lower():
        found.append("diacritics")
    if n_phonetic(letters_only(ba)) == n_phonetic(letters_only(bb)) and letters_only(ba) != letters_only(bb):
        found.append("polish_phonetic_respelling")
    if ACRONYM_RX.search(ba) and acronym_expansion_ok(a, b):
        found.append("acronym_spelled_out")
    return sorted(set(found))


def token_delta(a, b):
    """Word-level diff after L5 normalization -> (only_in_a, only_in_b, ratio)."""
    ta = apply_chain(a, LEVELS[5][1]).split()
    tb = apply_chain(b, LEVELS[5][1]).split()
    sm = SequenceMatcher(None, ta, tb)
    oa, ob = [], []
    for tag, i1, i2, j1, j2 in sm.get_opcodes():
        if tag in ("replace", "delete"):
            oa.extend(ta[i1:i2])
        if tag in ("replace", "insert"):
            ob.extend(tb[j1:j2])
    return oa, ob, sm.ratio()


def tokens_are_spelling_variants(oa, ob):
    """True when every differing token pairs 1:1 with a near-identical spelling."""
    if len(oa) != len(ob) or not oa:
        return False
    for x, y in zip(oa, ob):
        if x == y:
            continue
        if n_phonetic(x) == n_phonetic(y):
            continue
        if strip_diacritics(x) == strip_diacritics(y):
            continue
        if SequenceMatcher(None, x, y).ratio() < 0.75:
            return False
        if abs(len(x) - len(y)) > 3:
            return False
    return True


def numeric_pair_ok(oa, ob):
    """True when the differing tokens are exactly a digit <-> spoken-number correspondence."""
    if not oa or not ob:
        return False
    da = [t for t in oa if t.isdigit()]
    db = [t for t in ob if t.isdigit()]
    if not (da or db):
        return False
    src, dst = (da, ob) if da else (db, oa)
    joined = letters_only(" ".join(dst))
    for d in src:
        if not any(v and v in joined for v in num_variants(int(d))):
            return False
    return True


FILLER = {"no", "ej", "hej", "och", "ach", "eh", "yh", "hm", "hmm", "aha", "tak", "cóż",
          "więc", "stalkerze", "stalker", "stary", "bracie", "kolego", "proszę",
          "słuchaj", "wiesz"}

CLASS_NAMES = {
    "A": "A_SAFE_TTS_REUSE",
    "B": "B_VERY_LIKELY_SAME",
    "C": "C_DIFFERENT_TEXT",
    "D": "D_UNCERTAIN",
    "E": "E_STAGE_DIRECTION_INVENTED_LINE",
}


# ---------------------------------------------------------------- classification

def classify(official, tts):
    """
    A = safe TTS-rendering-only difference, B = very likely the same line but needs a
    human call, C = genuinely different text, D = uncertain,
    E = official text is a stage direction and the TTS author invented a spoken line.
    """
    official = official or ""
    tts = tts or ""
    res = {"CascadeLevel": "", "Transforms": "", "TokenRatio": 0.0,
           "OnlyInOfficial": "", "OnlyInTTS": "", "Class": "D", "Reason": ""}

    if not official.strip() or not tts.strip():
        res["Reason"] = "one side empty"
        return res

    # A stage direction is not a spoken line; if the TTS side speaks, the author invented
    # content. That is never "the same text", so it gets its own class rather than B/C.
    if is_stage_direction(official) and not is_stage_direction(tts):
        res.update(Class="E", CascadeLevel="n/a",
                   Transforms="stage_direction_to_invented_line",
                   Reason="official text is a non-verbal stage direction; the TTS author wrote a spoken line instead")
        return res

    label, idx = cascade(official, tts)
    tr = which_transforms(official, tts)
    oa, ob, ratio = token_delta(official, tts)
    res["Transforms"] = "|".join(tr)
    res["TokenRatio"] = round(ratio, 4)
    res["OnlyInOfficial"] = " ".join(oa)[:200]
    res["OnlyInTTS"] = " ".join(ob)[:200]

    if idx is not None:
        res["CascadeLevel"] = label
        if idx == 0:
            res.update(Class="A", Reason="byte-identical")
        elif idx <= SAFE_MAX_LEVEL:
            res.update(Class="A", Reason=f"identical after pure-rendering normalization ({label})")
        else:
            res.update(Class="B", Reason=f"identical only when diacritics are ignored ({label}) -- check for a real word change")
        return res

    res["CascadeLevel"] = "none"

    if acronym_expansion_ok(official, tts):
        res.update(Class="A", Reason="acronym spelled out / read as a word for the voice; rest identical")
        res["Transforms"] = "|".join(sorted(set(tr + ["acronym_spelled_out"])))
        return res

    if numeric_pair_ok(oa, ob):
        res.update(Class="A", Reason="residual difference is a digit <-> spoken-number correspondence")
        return res

    if tokens_are_spelling_variants(oa, ob):
        res.update(Class="A" if ratio >= 0.90 else "B",
                   Reason="differing tokens are 1:1 near-identical spellings (phonetic/typo respelling)")
        return res

    if not [t for t in (oa + ob) if t not in FILLER] and (oa or ob):
        res.update(Class="B", Reason="only filler/vocative words differ -- same line, different delivery")
        return res

    if ratio >= 0.92:
        res.update(Class="B", Reason=f"very high token overlap ({ratio:.3f}) but a real word differs")
    elif ratio >= 0.75:
        res.update(Class="D", Reason=f"partial overlap ({ratio:.3f}) -- needs a human ear")
    else:
        res.update(Class="C", Reason=f"low token overlap ({ratio:.3f}) -- genuinely different text")
    return res
