# S.T.A.L.K.E.R. 2: Heart of Chornobyl — polskie dialogi (spolszczenie lektorskie)

Podmienia angielskie/ukraińskie kwestie dialogowe w S.T.A.L.K.E.R. 2 na **polskiego lektora**,
wykorzystując istniejące nagrania [GameReadera](https://gamereader.pl) (autor: Rafko) —
darmowej nakładki, która czyta napisy gry na głos.

**To repo jest głównym źródłem wiedzy o projekcie**: pełne mapowania, schemat danych,
instrukcja budowania i wdrażania oraz spis pułapek, które realnie zafałszowały wyniki na
wcześniejszych etapach.

> ### ⛔ Zanim zbudujesz mod — przeczytaj [docs/ZNANE-BLEDY.md](docs/ZNANE-BLEDY.md)
>
> Znany, nierozwiązany problem: kontener tworzony przez `UnrealReZen` potrafi nie zarejestrować
> pakietów (`NumPackages=0` w logu gry). Wtedy silnik ładuje oryginalne nagłówki plików z gry,
> a z moda bierze tylko surowe bajty dźwięku — **dłuższe polskie kwestie zostają ucięte**
> dokładnie tam, gdzie kończyło się oryginalne nagranie. Sprawdź `Stalker2.log` po pierwszym
> uruchomieniu.

---

## Stan: wydanie 1.2

| | |
|---|---:|
| Normalnych mówionych kwestii w grze | **19 071** |
| Pokrytych — wariant **angielski** | **17 604 (92,3 %)** |
| Pokrytych — wariant **ukraiński** | **17 562 (92,1 %)** |
| Pozostaje do nagrania | **1 383 unikatowych tekstów** |
| Nagrań GameReadera wykorzystanych | 15 871 z 17 387 (91 %) |

Cutscenki **nie są objęte** — osobny, odłożony temat.

> Wszystkie liczby pochodzą z probe'a czytającego bajty z paków gry (`RIFF` pod offsetem
> z `BulkData`), nie z flag w plikach pomocniczych. Metodologia: [docs/PUŁAPKI.md](docs/PUŁAPKI.md).

---

## Który wariant wybrać

**Wariant angielski jest rekomendowany** — pokrywa 42 kwestie więcej.

Nie dlatego, że lepiej patchuje. Gra wysyła osobne dane głosowe dla obu języków i **nie są
one równie kompletne**: w 42 zdarzeniach ukraiński wpis media niesie `DebugName` należący do
innego SID-a. Patcher musi je pominąć, bo nadpisanie zepsułoby tamten dialog. To defekt
bazowej gry, nienaprawialny po stronie moda.

Wariant ukraiński ma sens, jeśli chcesz zostawić angielski dubbing nietknięty.

**Nigdy nie instaluj obu naraz.**

---

## Instalacja

1. Pobierz **jeden** wariant.
2. Skopiuj trzy pliki (`.pak`, `.ucas`, `.utoc`) do:
   ```
   <gra>/Stalker2/Content/Paks/~mods/
   ```
3. W grze ustaw język głosu zgodny z wariantem (angielski / ukraiński). Napisy zostaw polskie.

Deinstalacja: usuń te trzy pliki.

**Czego to NIE robi:** nie generuje nowego lektora (korzysta z gotowych nagrań GameReadera),
nie obejmuje cutscenek, nie modyfikuje pliku wykonywalnego gry.

---

## Dokumentacja

| Dokument | Co zawiera |
|---|---|
| [JAK-AKTUALIZOWAC-I-WDRAZAC.md](docs/JAK-AKTUALIZOWAC-I-WDRAZAC.md) | pełny pipeline, kontrole po buildzie, co robić po aktualizacji gry |
| [SCHEMAT-DANYCH.md](docs/SCHEMAT-DANYCH.md) | model danych, opis kolumn, jak złączyć tabele |
| [PUŁAPKI.md](docs/PUŁAPKI.md) | **15 błędów, które realnie zafałszowały wyniki** — przeczytaj przed analizą |
| [architecture.md](docs/architecture.md) | jak gra trzyma audio, jak działa splice |
| [ZNANE-BLEDY.md](docs/ZNANE-BLEDY.md) | otwarte problemy, m.in. `NumPackages=0` |
| [unrealrezen-build.md](docs/unrealrezen-build.md) | jak zbudować **załatany** UnrealReZen |

---

## Dane

Repo trzyma **dwie tabele master** zamiast kilkunastu podobnych CSV. Wszystko, czego
potrzebuje build, jest z nich **generowane** — jedno źródło prawdy, brak dryfu.

| Plik | Wierszy | Kolumn | Jednostka |
|---|---:|---:|---|
| `data/MASTER_SIDS.csv` | 33 528 | 52 | jedna kwestia dialogowa w grze |
| `data/MASTER_RECORDINGS.csv` | 17 387 | 31 | jedno fizyczne nagranie GameReadera |
| `data/SPEAKER_VOICE_MAPPING.csv` | — | — | kod postaci → imię → profil głosu |

```bash
python scripts/analysis/make_build_inputs.py --audio-dir "<katalog z output1 (N).ogg>"
```

`MASTER_SIDS.csv` odpowiada na wszystko przez kolumnę `OverallStatus`:

| Status | Liczba | Znaczenie |
|---|---:|---|
| `COVERED_IN_MOD` | 17 629 | jest polski lektor |
| `NO_VOICE_ASSET_IN_GAME` | 7 973 | tekst jest, ale gra nie ma pliku audio do podmiany |
| `NON_VERBAL_OUT_OF_SCOPE` | 4 926 | didaskalia, dźwięki |
| `OPEN_NEEDS_NEW_RECORDING` | 1 431 | trzeba nagrać |
| `CUTSCENE_OUT_OF_SCOPE` | 1 405 | cutscenka |
| `INTERNAL_MISMATCH` | 129 | media wskazują na inny SID |
| `OPEN_RECORDING_EXISTS` | 23 | nagranie istnieje, czeka na build (11 zmapowanych 2026-08-24) lub target nieosiągalny |
| `OPEN_BUILD_FAILED` | 9 | build się nie udał |
| `OPEN_REUSE_POSSIBLE` | 3 | do pokrycia przez reuse |

`MASTER_RECORDINGS.csv` robi to samo od strony nagrań (`UsageStatus`), z hashem SHA-1 każdego
pliku audio. Wszystkie 17 387 nagrań są **unikalne bajtowo** — duplikaty istnieją tylko na
poziomie tekstu.

> **Uwaga (2026-08-24):** statusy `UNUSED_POTENTIAL_LOST_MAPPING(_REVIEW)` w
> `MASTER_RECORDINGS.csv` (370 wierszy) są **historyczne** — 347 z tych nagrań zostało już
> odzyskanych i wdrożonych jako populacja RECOVERED, a kolejnych 11 (przypadki REVIEW z
> różnicami zapisu: cyfry vs słowa, interpunkcja) zmapowano ręcznie 2026-08-24
> (`MappedVia = RECOVERED`, `OverallStatus = OPEN_RECORDING_EXISTS` — czekają na build).
> Reszta to duplikaty pokryte bliźniaczym nagraniem albo nieudane patche (4 SID-y kapitana
> Smidta z EQ01: `NoMediaMatch`/`NoUasset`). Wiążący jest zawsze `MASTER_SIDS.csv`
> (`MappedVia`/`MappedRecordingN`), nie `UsageStatus` nagrań.

---

## Chcesz pomóc?

Najbardziej brakuje **1 376 nagrań** (1 431 kwestii po deduplikacji identycznych tekstów). Filtr:

```
MASTER_SIDS.csv  ->  OverallStatus = OPEN_NEEDS_NEW_RECORDING
```

Charakterystyka: mediana 78 znaków, 53 różnych mówców, w większości długie unikatowe kwestie
1:1. Powtarzalne kwestie miejskie („Cześć.", „Na razie!", „Czego chcesz?") są **już pokryte** —
nie ma tu efektu skali.

Otwarte pozycje wymagające decyzji, nie pracy:

- **22 przypadki didaskaliów** — tekst oficjalny to `(umiera)`, a nagranie mówi „Umieram...".
  To nie jest ta sama treść. Żaden nie jest w wydaniu. Filtr: `NeedsEditorialDecision = True`.
- **Cutscenki** — pipeline eksperymentalny i zepsuty, patrz [ZNANE-BLEDY.md](docs/ZNANE-BLEDY.md).

---

## Budowanie ze źródeł

```bash
python scripts/analysis/make_build_inputs.py --audio-dir "<...>"
dotnet run --project scripts/BatchEncoder -c Release        # ogg -> wav -> wem (oba warianty)
dotnet run --project scripts/FullPatcher  -c Release -- EN  # splice do assetów (EN lub UA)
<załatany>/UnrealReZen.exe --content-path build/ModOutput_EN ...
```

Albo wszystko naraz: `.\build.ps1 -Variant EN` / `.\build.ps1 -Variant UA`.

Pipeline patchuje **wszystkie trzy populacje** (MAIN + EXTRA przez `SwitchContainerLeaves`
+ RECOVERED) — od 2026-08-24 pełne pokrycie wydania 1.2 buduje się z tego repo:
**17 621 kwestii EN / 17 579 UA** (różnica to 42 wadliwe ukraińskie sloty w samej grze,
pomijane przez `build/UA_SKIP_SIDS.txt`).

Pełna instrukcja z kontrolami: [JAK-AKTUALIZOWAC-I-WDRAZAC.md](docs/JAK-AKTUALIZOWAC-I-WDRAZAC.md).

> **Najczęstszy błąd:** użycie UnrealReZen ze strony zamiast wersji z łatki. Nie wywala
> błędu — po prostu produkuje kontener ~3× za duży, który nie referencjonuje danych gry.
> Sprawdź: `grep -c "unsupported version 8" <log>` musi dać **0**.

---

## Podziękowania i prawa

**Nagrania są własnością Rafka** ([gamereader.pl](https://gamereader.pl)) i udostępnione do
użytku niekomercyjnego.

Teksty dialogów pochodzą z oficjalnej lokalizacji gry (GSC Game World) i są tu wyłącznie po
to, żeby dało się odtworzyć i zweryfikować mapowanie.

Kod narzędzi napisany na potrzeby tego projektu; zależności (CUE4Parse, UnrealReZen, Wwise)
na własnych licencjach.

**Licencja: TODO — nieustalona.** Nie zakładaj żadnej, dopóki się nie wyjaśni.
