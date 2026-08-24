# Pułapki — przeczytaj, zanim zaczniesz analizować

Każdy punkt na tej liście **realnie zafałszował wyniki** na jakimś etapie tego projektu.
Wszystkie zostały wykryte, potwierdzone i naprawione. Spisane, żeby nikt nie musiał ich
odkrywać drugi raz.

---

## 1. Pusty `Media[]` NIE oznacza braku audio

Najdroższy błąd w historii projektu. Narzędzia sprawdzały wyłącznie
`EventLanguageMap[lang].Media[]`. Dla kwestii `General_*` (chatter NPC) ta tablica jest
**legalnie pusta**, a prawdziwe audio siedzi w:

```
EventLanguageMap[lang].SwitchContainerLeaves[].Media[]
```

Jeden liść na wariant głosu NPC (`VoiceA`…`VoiceM`), typowo 5–6 na język, każdy z własnym
WEM. Skutek pominięcia: tysiące SID-ów błędnie oznaczonych jako „brak audio".

**Zawsze sprawdzaj obie ścieżki.**

---

## 2. `GameReaderPolishText` to kopia tekstu oficjalnego, nie tekst nagrania

W starym `MASTER_DIALOGUE_DATABASE.csv` kolumna `GameReaderPolishText` zawiera **to samo**, co
`PolishText_Official`. Porównanie tych dwóch kolumn porównuje tekst sam ze sobą i zawsze
zwraca „identyczne" — wynik bezwartościowy.

Prawdziwy skrypt lektora jest w `MASTER_RECORDINGS.GameReaderTTS_Text`, kluczowany indeksem
`N` z nazwy pliku `output1 (N).ogg`.

**Każda analiza tekstowa musi używać `N`.**

---

## 3. Są DWIE (od 1.2 — TRZY) tabele mapowań, nie jedna

| Tabela | Populacja |
|---|---|
| `FINAL_lektor_mapping.csv` | build MAIN |
| `PATCH_LIST_EXTRA.csv` | build EXTRA (SwitchContainer), kolumna `SourceLineIndexN` |
| `PATCH_LIST_RECOVERED.csv` | odzyskane nagrania (1.2) |

Czytanie tylko pierwszej powoduje, że **769 faktycznie używanych nagrań wygląda na
niewykorzystane**. Kolumna `MASTER_RECORDINGS.IsUsed` już to uwzględnia.

---

## 4. „Niewykorzystane nagranie" ≠ „nie ma tego w grze"

Największe koszyki niewykorzystanych nagrań mają zupełnie normalne wyjaśnienia:

- **cutscenki** — poza zakresem tego moda
- **gra nie ma pliku audio do podmiany** — tekst istnieje w lokalizacji, ale żaden asset VO nie
  jest wysłany. GameReader nagrywał z dumpu napisów, a napisy zawierają kwestie, które nigdy
  nie są wypowiadane (treść wycięta, warianty niedostępne w grze). Mod działa przez
  **podmianę** — jeśli nie ma czego podmienić, nagranie nie ma gdzie trafić.
- **duplikaty tekstu** — ta sama kwestia nagrana kilka razy, mod używa jednego wariantu

---

## 5. `provider.Files.Keys` (CUE4Parse) zwraca duplikaty

Surowa enumeracja daje 80 951 wpisów `VO_*.uasset`, ale unikatów jest **44 323**. Liczenie
z surowej enumeracji zawyża wyniki ~2×.

**Deduplikuj przed liczeniem.**

---

## 6. Resolver SID → asset ma ślepe punkty

Nazwa pliku to `VO_<nieprzewidywalny_prefiks_aktora>_<SID>.uasset`. Indeks sufiksowy działa,
ale wrażliwy na wielkość liter gubi przypadki:

- dryf wielkości liter — `LiterEDeltaBravo` vs `...Deltabravo`
- przestawione tokeny — `supack_<Hub>_<mówca>` vs `<mówca>_supack_<Hub>`
- wstawiony token — `..._MutantShoo_3_1_` vs `..._MutantShoo_3_Noon_1_`

Rozwiązanie: trzy strategie od najostrożniejszej (dokładny sufiks → sufiks bez wielkości liter
→ dopasowanie po końcowym ID **z progiem pokrycia tokenów**). Samo dopasowanie po ID jest
niebezpieczne — niskie ID kolidują między niepowiązanymi assetami.

---

## 7. Nigdy nie traktuj wyniku fuzzy jako decyzji

Ocena „fuzzy 0,88" nie mówi, czy nagranie pasuje. W tym projekcie **305 z 326** przypadków
oznaczonych jako fuzzy okazało się bezpiecznym reuse — różnice były wyłącznie w zapisie.
Wcześniejszy wniosek „0 nadaje się do użycia" był artefaktem metody.

Używaj kaskady normalizatorów (`scripts/analysis/tts_engine.py`), która **nazywa** różnicę,
zamiast punktować podobieństwo.

---

## 8. Reguła liczbowa bez strażnika daje fałszywe dopasowania

`numeric_pair_ok()` sprawdza, czy liczby po obu stronach sobie odpowiadają — ale **nie
sprawdza reszty zdania**. Przy nieograniczonym wyszukiwaniu par dopasuje

```
"… sektor 9, Uskok w zasięgu wzroku."   <->   "Gdy zostałem ewakuowany … w tysiąc
                                                dziewięćset osiemdziesiątym szóstym…"
```

bo „dziewięć" zawiera się w „dziewięćset". Przy wyszukiwaniu par **wymagaj progu podobieństwa
znakowego ≥ 0,85 po pełnej normalizacji**.

---

## 9. Didaskalia to nie kwestia mówiona

Osobna klasa: tekst oficjalny to didaskalia w nawiasie, a autor skryptu TTS napisał w ich
miejsce wypowiadaną kwestię:

```
"(umiera)"  ->  "Umieram..."
```

To **nie** jest reuse tej samej treści — nagranie mówi zdanie, którego w grze nie ma. Decyzja
redakcyjna, nie techniczna. Lista: `data/analysis/EDITORIAL_STAGE_DIRECTIONS.csv`.

Uwaga na odwrotny przypadek: `"(śmiech)"` → `"*Śmiech*"` to **ten sam** didaskal w innej
notacji i jest bezpieczny.

---

## 10. Warianty EN i UA nie są symetryczne

Gra wysyła osobne dane głosowe dla angielskiego i ukraińskiego, i **nie są one równie
kompletne**. 42 zdarzenia mają wpisy media w obu językach, ale ukraiński wpis niesie
`DebugName` należący do **innego SID-a** — w ukraińskim slocie leży nagranie innej kwestii.

Patcher musi to wykrywać i odmawiać patchowania, bo nadpisanie zepsułoby tamten dialog.
Stąd wariant angielski pokrywa 42 kwestie więcej. To defekt bazowej gry, nienaprawialny po
stronie moda.

---

## 11. `Invalid container header` NIE jest nieszkodliwym ostrzeżeniem

Jeśli `Stalker2.log` pokazuje `NumPackages=0` dla naszego kontenera, silnik ładuje
**oryginalny** `.uasset` z gry, a z moda bierze tylko surowe bajty. Każda edycja nagłówka
`.uasset` jest wtedy martwa, a audio czytane jest tylko do **oryginalnego** `SerialSize` —
to prawdziwa przyczyna obcinania dłuższych kwestii.

Szczegóły: [ZNANE-BLEDY.md](ZNANE-BLEDY.md).

---

## 12. Zawsze czyść `~mods` przed uruchomieniem patchera

Inaczej narzędzia przeczytają **własny poprzedni output** jako źródło i wynik będzie
skumulowany, a nie odtworzony od zera.

---

## 13. Musisz uzyc ZALATANEGO UnrealReZen, nie wersji ze strony

Wersja stock UnrealReZen **nie obsluguje IoStore w wersji 8**, ktorej uzywa ta gra. Nie
przerwie pracy — zbuduje kontener, ale:

- w logu pojawi sie `Io Store "...global.utoc" has unsupported version 8` (dziesiatki razy)
- narzedzie nie zreferencjonuje istniejacych danych gry
- `.ucas` spuchnie ~3x (u nas: 8,24 GB zamiast 2,7 GB z tego samego drzewa)
- `.utoc` bedzie ~2x wiekszy

**Kontrola po spakowaniu:**

```
grep -c "unsupported version 8" <log>     # musi byc 0
```

oraz `.ucas` powinien byc MNIEJSZY niz suma `.ubulk` w drzewie zrodlowym (u nas ok. 0,8x).
Jesli jest wiekszy — uzyles zlego binarium.

Buduj z lataka: `patches/UnrealReZen.patch`, instrukcja w
[unrealrezen-build.md](unrealrezen-build.md).

---

## 14. Wadliwych slotów UA nie wykryjesz po `DebugName`

41 z 42 SID-ów `Is41UkrainianMismatch` ma w ukraińskim wpisie media `DebugName`, który
**wygląda poprawnie** (zawiera właściwy SID) i MediaId zgodny z tabelą — strażnik oparty
o `DebugName` przepuszcza je wszystkie, mimo że per weryfikacja bajtowa slot nie ma
prawdziwego audio (`UA_HasRealAudio=False`).

Jedyna niezawodna metoda: **jawna lista pominięć z tabeli master**
(`build/UA_SKIP_SIDS.txt`, generowana przez `make_build_inputs.py`). FullPatcher w wariancie
UA wymaga tej listy i twardo pomija te SID-y (`UaDefectiveSlotSkip` w raporcie).

---

## 15. Jeden plik — jeden właściciel

`FULL_BATCH_wem_mapping.csv` był pisany przez **dwa** narzędzia (`make_build_inputs.py`
i `BatchEncoder`) w **różnych schematach** — które uruchomiłeś później, taki schemat
dostawał FullPatcher, z błędem `KeyNotFoundException` w najlepszym razie, z cichym złym
mapowaniem w najgorszym. Od 2026-08-24 plik pisze wyłącznie BatchEncoder
(z kolumnami `EnglishMediaId` i `UkrainianMediaId`).
