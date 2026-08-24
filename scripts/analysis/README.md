# scripts/analysis

Narzędzia analityczne — wszystkie **read-only** wobec gry.

| Narzędzie | Do czego |
|---|---|
| `FullPopulationProbe/` | pełny probe: dla każdego SID-a czyta `Media[]` **i** `SwitchContainerLeaves[]`, schodzi do bajtów w `.ubulk` i sprawdza `RIFF`. To jest źródło prawdy o tym, co gra faktycznie ma. |
| `tts_engine.py` | kaskada normalizatorów tekstu — orzeka, czy tekst nagrania i tekst gry to ta sama kwestia, i **nazywa** różnicę (wielokropek, cudzysłów, fonetyka, akronim, liczba słownie…). Nie używa wyniku fuzzy jako decyzji. |
| `build_master_tables.py` | scala wszystkie źródła w `data/MASTER_SIDS.csv` i `data/MASTER_RECORDINGS.csv`. |
| `make_build_inputs.py` | generuje listy wejściowe buildu z tabeli master (jedno źródło prawdy). |

## Probe

```bash
dotnet run --project FullPopulationProbe -c Release -- <katalog-wyjściowy> \
    --game-dir "<gra>/Stalker2/Content/Paks" \
    --usmap    "<...>/Mappings.usmap" \
    --master-db "<...>/MASTER_DIALOGUE_DATABASE.csv"
```

Potrzebuje `oodle-data-shared.dll` w katalogu podanym przez `--root` (domyślnie bieżący).

Wynik: `_LIVE_FULL_POPULATION_V2.csv` — status audio dla każdego SID-a, plus audyt
wewnętrznych referencji Wwise i porównanie ze starą flagą `HasVoiceAsset`.

**Uruchom go po każdej aktualizacji gry** — `MediaId` i offsety potrafią się zmienić.

## tts_engine.py

```python
from tts_engine import classify
classify("Ciekawe, jakie badania prowadzą w IBAOC…",
         "Ciekawe, jakie badania prowadzą w Ibea-o-ce...")
# -> Class 'A' (bezpieczny reuse), powód: akronim rozpisany fonetycznie
```

Klasy: `A` bezpieczny reuse, `B` prawie na pewno to samo, `C` inna treść,
`D` niepewne, `E` didaskalia zastąpione zmyśloną kwestią.

Przy wyszukiwaniu par (nie przy porównaniu 1:1) **dodaj strażnik podobieństwa** —
patrz [PUŁAPKI.md](../../docs/PUŁAPKI.md) pkt 8.
