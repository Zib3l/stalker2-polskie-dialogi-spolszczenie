# Schemat danych

Wszystkie dane projektu mieszczą się w **dwóch tabelach master** plus kilku listach roboczych.
Nie trzeba szukać po kilkudziesięciu plikach.

| Plik | Wierszy | Kolumn | Jednostka |
|---|---:|---:|---|
| `data/analysis/MASTER_SIDS.csv` | 33 528 | 50 | jedna kwestia dialogowa w grze |
| `data/analysis/MASTER_RECORDINGS.csv` | 17 387 | 31 | jedno fizyczne nagranie GameReadera |

## Dlaczego dwie tabele, a nie jedna

Nagranie i SID są w relacji **wiele-do-wielu**: jedno nagranie obsługuje wiele SID-ów, a jeden
SID może mieć kilku kandydatów. W jednej tabeli dałoby to albo zduplikowane wiersze, albo
połowę kolumn pustych.

## Jak je złączyć

```
MASTER_SIDS.MappedRecordingN      <->  MASTER_RECORDINGS.RecordingIndexN
MASTER_SIDS.CandidateRecordings   <->  MASTER_RECORDINGS.RecordingIndexN   (lista, ';')
MASTER_RECORDINGS.UsedBySIDs      <->  MASTER_SIDS.SID                     (lista, ';')
```

Integralność zweryfikowana: **0 osieroconych kluczy w obie strony**.

---

## Model danych — jak to się łączy w grze

```
SID (klucz z locres)  ->  sid_phrase_<SID>            = tekst napisu
                      ->  VO_<prefiks_aktora>_<SID>.uasset   = zdarzenie Wwise
                              |
                              +-- EventCookedData.EventLanguageMap["English(US)"]
                              |        +-- Media[]                  <- zwykła kwestia
                              |        +-- SwitchContainerLeaves[]  <- kwestia General_* (5-6 wariantów głosu)
                              |                +-- Media[]
                              |
                              +-- EventCookedData.EventLanguageMap["Ukrainian(UA)"]
                                       (ta sama struktura)

Media -> MediaId -> PackagedFile.BulkData.WemFile -> Header(Offset, Size)
      -> bajty w <asset>.ubulk pod tym offsetem -> pierwsze 4 bajty == "RIFF"
```

**Mod podmienia bajty WEM w slocie jednego języka**, zostawiając drugi nietknięty. Stąd dwa
warianty wydania.

---

## MASTER_SIDS.csv — od czego zacząć

Kolumna `OverallStatus` odpowiada na pytanie „co jest, a czego nie". Sumuje się do 33 528.

| Status | Znaczenie |
|---|---|
| `COVERED_IN_MOD` | jest polski lektor w wydanym buildzie |
| `NO_VOICE_ASSET_IN_GAME` | tekst w grze jest, ale gra **nie ma pliku audio** do podmiany (treść wycięta, DLC, dev) |
| `NON_VERBAL_OUT_OF_SCOPE` | didaskalia, dźwięki, muzyka — poza dubbingiem |
| `OPEN_NEEDS_NEW_RECORDING` | trzeba nagrać |
| `CUTSCENE_OUT_OF_SCOPE` | cutscenka — osobny, odłożony temat |
| `OPEN_RECORDING_EXISTS` | nagranie już istnieje, wystarczy zmapować |
| `INTERNAL_MISMATCH` | media w grze wskazują na inny SID — patcher słusznie odrzuca |
| `OPEN_BUILD_FAILED` | próbowano zpatchować, build się nie udał |
| `OPEN_REUSE_POSSIBLE` | do pokrycia przez ponowne użycie nagrania |

### Ważniejsze kolumny

- `InShippedMod` — czy jest w wydanym buildzie
- `LiveStatus`, `AnyRealAudio` — **prawda ustalona bajtowo**, nie z flagi
- `IsSwitchContainer`, `PatchTypeNeeded` — czy potrzebny patch SwitchContainer, czy zwykły `Media[]`
- `EN_HasRealAudio` / `UA_HasRealAudio` — osobno per język; **tu widać różnicę EN vs UA**
- `Status_MAIN` / `Status_MAIN_EN` / `Status_MAIN_UA` / `Status_EXTRA` — status per build
- `Is41UkrainianMismatch` — SID-y z wadliwym ukraińskim slotem w samej grze
- `NeedsEditorialDecision` — przypadki didaskaliów czekające na decyzję
- `TTSClass`, `TTSTransforms`, `TTSCascadeLevel` — czy tekst nagrania = tekst gry i czym się różni
- `InternalRefVerdict`, `ForeignSid` — audyt wewnętrznych referencji Wwise
- `EnglishMediaId`, `UkrainianMediaId`, `PolishMediaId`, `PolishWemName` — identyfikatory mediów
- `LegacyHasVoiceAssetFlag` — **stara, niepewna flaga; nie używać do decyzji**

---

## MASTER_RECORDINGS.csv

Kolumna `UsageStatus`. Sumuje się do 17 387.

| Status | Znaczenie |
|---|---|
| `USED` / `USED_MULTI` | wykorzystane przez 1 / wiele SID-ów |
| `UNUSED_CUTSCENE` | należy do cutscenek — poza zakresem |
| `UNUSED_TARGET_HAS_NO_VOICE_ASSET` | **tekst jest w grze, ale gra nie ma audio do podmiany** |
| `UNUSED_POTENTIAL_LOST_MAPPING` | realny kandydat — nagranie czeka, target istnieje |
| `UNUSED_DUPLICATE_TEXT` | ten sam tekst nagrany kilka razy |
| `UNUSED_NO_MATCH_IN_GAME` | brak odpowiednika (głównie krótkie okrzyki bojowe) |

### Ważniejsze kolumny

- `IsUsed`, `UsedBySIDs`, `UsedBySIDCount` — kto faktycznie tego używa
- `Ogg1_Sha1` / `Ogg2_Sha1` — hash audio. **Wszystkie 17 387 nagrań są unikalne bajtowo**;
  `IdenticalTextTwins` pokazuje duplikaty *tekstu*, nie dźwięku
- `BestCandidateSID` + `BestCandidateOfficialText` — proponowany target dla nieużywanych
- `DeepMatchClass`, `DeepMatchTransforms` — jak dopasowano (akronim, liczba słownie…)
- `IsCutsceneRecording` — czy nagranie należy do puli cutscenkowej

---

## Listy robocze (`data/`)

| Plik | Do czego |
|---|---|
| `FINAL_lektor_mapping.csv` | mapowanie buildu MAIN: SID → tekst → plik audio → MediaId |
| `PATCH_LIST_EXTRA.csv` | mapowanie buildu EXTRA (kwestie w SwitchContainer) |
| `PATCH_LIST_RECOVERED.csv` | mapowanie odzyskanych nagrań (wydanie 1.2) |
| `FULL_BATCH_wem_mapping.csv` | SID → MediaId → nazwa `.wem` |
| `SPEAKER_VOICE_MAPPING.csv` | kod postaci → imię w grze → sugerowany profil głosu |
| `analysis/TTS_TRANSFORMATIONS.csv` | statystyki transformacji zapisu TTS |
| `analysis/EDITORIAL_STAGE_DIRECTIONS.csv` | przypadki do decyzji redakcyjnej |

**Uwaga:** budowanie moda czyta **trzy** listy patcha (`FINAL_lektor_mapping`,
`PATCH_LIST_EXTRA`, `PATCH_LIST_RECOVERED`). Czytanie tylko pierwszej to klasyczny błąd —
patrz [PUŁAPKI.md](PUŁAPKI.md).
