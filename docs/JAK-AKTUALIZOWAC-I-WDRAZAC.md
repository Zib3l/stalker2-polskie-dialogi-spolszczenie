# Jak to się aktualizuje i wdraża

Pełna ścieżka od surowych nagrań do działającego moda, plus co robić przy każdej aktualizacji
gry lub przy dołożeniu nowych nagrań.

---

## 0. Czego potrzebujesz

| Rzecz | Uwagi |
|---|---|
| S.T.A.L.K.E.R. 2 zainstalowany | ścieżka do `Stalker2/Content/Paks` |
| .NET 10 SDK | do zbudowania narzędzi |
| Python 3.10+ | skrypty analityczne |
| Wwise 2026.1.x | `WwiseConsole.exe` do kodowania WEM |
| ffmpeg | konwersja `.ogg` → `.wav` |
| **Załatany UnrealReZen** | **NIE wersja ze strony** — patrz [unrealrezen-build.md](unrealrezen-build.md) |
| `Mappings.usmap` | zrzucony z gry przez UE4SS |
| Nagrania GameReadera | katalog z `output1 (N).ogg` |

Skopiuj `config.example.json` → `config.json` i uzupełnij ścieżki.

> **Uwaga o `Mappings.usmap`:** zrzucaj go, gdy odpowiedni system gry jest załadowany
> (np. w trakcie cutscenki), nie z menu głównego — inaczej część struktur będzie pusta.

---

## 1. Pipeline w skrócie

```
data/MASTER_SIDS.csv                     <- jedyne źródło prawdy
        |
        |  scripts/analysis/make_build_inputs.py
        v
build/FINAL_lektor_mapping.csv           <- populacja MAIN
build/PATCH_LIST_EXTRA.csv               <- kwestie SwitchContainer
build/PATCH_LIST_RECOVERED.csv           <- odzyskane nagrania
build/FULL_BATCH_wem_mapping.csv         <- SID -> MediaId / WemName
        |
        |  ffmpeg:  output1 (N).ogg -> .wav
        |  Wwise:   .wav -> .wem            (convert-external-source)
        v
   pula WEM-ów
        |
        |  FullPatcher   (splice bajtow WEM do assetow gry)
        v
   ModOutput<Wariant>/   <- drzewo .uasset + .ubulk
        |
        |  UnrealReZen (ZALATANY!)
        v
   <Nazwa>.pak / .ucas / .utoc
        |
        v
   Stalker2/Content/Paks/~mods/
```

---

## 2. Budowanie od zera

```bash
# 1. wygeneruj listy wejściowe z tabeli master
python scripts/analysis/make_build_inputs.py --audio-dir "<katalog z output1 (N).ogg>"

# 2. zakoduj audio (ffmpeg + Wwise)
dotnet run --project scripts/BatchEncoder -c Release

# 3. zpatchuj assety gry — osobno dla każdego wariantu językowego
dotnet run --project scripts/FullPatcher -c Release

# 4. spakuj (ZAŁATANYM UnrealReZen)
<ścieżka-do-załatanego>/UnrealReZen.exe \
  --content-path "<ModOutput...>" \
  --compression-format Zlib \
  --engine-version GAME_UE5_5 \
  --game-dir "<katalog gry>" \
  --output-path "<Release>/<Nazwa>.utoc"
```

`build.ps1` spina kroki 1–4 w jedno polecenie.

> **Zawsze czyść `~mods` przed uruchomieniem patchera/enkodera** — inaczej narzędzia
> przeczytają własny poprzedni output jako źródło.

---

## 3. Obowiązkowa kontrola po spakowaniu

Trzy sprawdzenia, które wyłapują wszystkie błędy, jakie ten projekt realnie zaliczył:

```bash
# 1. czy użyto załatanego UnrealReZen  -> MUSI być 0
grep -c "unsupported version 8" <pack_log>

# 2. czy .ucas ma sensowny rozmiar
#    powinien być MNIEJSZY niż suma .ubulk w drzewie (u nas ~0,8x)
#    jeśli jest 2-3x WIĘKSZY -> użyto złego binarium, przepakuj

# 3. raport patcha
#    Errors: 0
#    <drugi język>-untouched check: FAIL=0
#    Cutscene guard: PASS
```

Dodatkowo po uruchomieniu gry sprawdź `Stalker2.log`:
`Invalid container header` / `NumPackages=0` oznacza, że kontener **nie działa** — silnik
ładuje oryginalne assety. Patrz [ZNANE-BLEDY.md](ZNANE-BLEDY.md).

---

## 4. Wdrożenie

Skopiuj **trzy pliki jednego wariantu** do:

```
<gra>/Stalker2/Content/Paks/~mods/
```

**Nigdy obu wariantów naraz** — patchują ten sam zestaw assetów w różnych slotach
językowych i będą się nadpisywać.

| Wariant | Slot | W grze ustaw język głosu |
|---|---|---|
| English (**rekomendowany**) | angielski | angielski |
| Ukrainian | ukraiński | ukraiński |

Wariant angielski pokrywa **42 kwestie więcej** — nie dlatego, że lepiej patchuje, tylko
dlatego, że ukraińskie dane głosowe w samej grze są dla tych zdarzeń wadliwe.

Deinstalacja: usuń te trzy pliki z `~mods`.

---

## 5. Co robić po aktualizacji gry

Aktualizacja gry może przesunąć offsety, zmienić strukturę assetów albo dodać kwestie.

1. **Zrzuć nowy `Mappings.usmap`** (UE4SS, w trakcie załadowanej sceny).
2. **Uruchom live-probe** — odtwarza prawdę o audio od zera:
   ```bash
   dotnet run --project scripts/analysis/FullPopulationProbe -c Release -- <katalog-wyjściowy>
   ```
   Wypluwa `_LIVE_FULL_POPULATION_V2.csv` — status audio dla każdego SID-a, zweryfikowany
   bajtowo (`RIFF`).
3. **Przebuduj tabele master:**
   ```bash
   python scripts/analysis/build_master_tables.py
   ```
4. **Porównaj** nowy `MASTER_SIDS.csv` ze starym — interesują cię SID-y, które zmieniły
   `LiveStatus` albo `IsSwitchContainer`.
5. Przebuduj mod od kroku 1 z sekcji 2.

**Nie zakładaj, że stare mapowanie nadal pasuje.** `MediaId` i offsety potrafią się zmienić
między patchami gry.

---

## 6. Co robić po dołożeniu nowych nagrań

1. Dopisz nagrania do puli (`output1 (N).ogg`, kolejne `N`).
2. Dopasuj je do SID-ów — użyj kaskady z `scripts/analysis/tts_engine.py`,
   **nie** samego wyniku fuzzy (patrz [PUŁAPKI.md](PUŁAPKI.md) pkt 7).
3. Zaktualizuj `MASTER_SIDS.csv` (`MappedRecordingN`, `MappedVia`) i
   `MASTER_RECORDINGS.csv` (`IsUsed`, `UsedBySIDs`).
4. Przebuduj od kroku 1 z sekcji 2.

Kandydaci czekający na obsłużenie: filtruj `MASTER_SIDS.csv` po
`OverallStatus = OPEN_NEEDS_NEW_RECORDING` (1 442 SID-y = 1 383 unikatowych tekstów).

---

## 7. Czego NIE ruszać

- **Cutscenki** — pipeline jest eksperymentalny i zepsuty (obcinanie audio, podwójne
  odtwarzanie). Patchery mają twardy bezpiecznik: abort, jeśli SID zaczyna się od `C_` lub
  asset leży pod `/Cutscenes/`. Nie usuwaj tego bezpiecznika.
- **Wynik fuzzy jako decyzja** — patrz [PUŁAPKI.md](PUŁAPKI.md).
- **Wersja stock UnrealReZen** — buduj z łatki.
- **22 przypadki didaskaliów** (`(umiera)` → „Umieram...") — decyzja redakcyjna, nie
  techniczna. Żaden nie jest w wydaniu.
