# Analiza: czy brakujące SID-y można pokryć istniejącymi nagraniami GameReader?

> **⚠️ NIEAKTUALNE LICZBY (oznaczone 2026-08-24).** Analiza brakujących dialogów jest
> ZAMKNIĘTA. Liczby pokrycia i luki w tym pliku (m.in. `1675`, `~1330`, `78%`, `21 040`)
> są zastąpione. Aktualnie: **19 071** normalnych mówionych kwestii, **17 231 pokrytych
> (90,4%)**, **1397 unikatowych nagrań nadal brakuje**. Źródło prawdy:
> `Stalker 2/FINAL_STATUS.md` oraz
> `Stalker 2/scripts/analysis_2026-08-24_long_research/11_FINAL_SUMMARY.txt`.
> Plik zachowany jako historia analizy — nie usuwać.


**Stan na:** 2026-08-23 · **Status:** wyłącznie analiza — żaden plik źródłowy/mapping nie został
zmieniony. Ten plik jest nowym plikiem diagnostycznym.

## 1. Dane wejściowe faktycznie dostępne w tym repo

| Plik | Zawartość | Wiersze |
|---|---|---|
| `data/FINAL_lektor_mapping.csv` | SID → **polski** tekst oficjalny → plik audio GameReader → MediaId, dla linii **już pokrytych** | 16 402 (unikalne SID) |
| `data/SPEAKER_VOICE_MAPPING.csv` | Zagregowane po postaci: SpeakerCodename → LineCount (ile linii tej postaci brakuje) → **jeden przykładowy** `ExampleSID`/`ExampleEnglishText` | 120 grup postaci, suma `LineCount` = 1 675 |
| `build/FULL_BATCH_wem_mapping.csv`, `build/FULL_PATCH_report.csv` | Artefakty pochodne tych samych 16 402 pokrytych SID-ów | 16 402 każdy |

**Czego w repo NIE ma** (potwierdzone przeszukaniem całego katalogu projektu pod kątem `*.csv`/`*.json`):

- Pełnej listy wszystkich `sid_phrase_*` z gry (pokrytych i niepokrytych) z tekstem per SID.
  Taki plik (`Localization_PL.json`) jest jawnie wskazany w
  [scripts/mapping-reference/match_lektor.ps1](../scripts/mapping-reference/match_lektor.ps1) jako
  wejście spoza tego repo, na innej ścieżce (`G:\ClaudeProjekt\Stalker 2\...`) — **poza
  katalogiem tego projektu, więc celowo nie był odczytywany w tej analizie.**
- Surowego eksportu linii GameReader (`subtitlesPL (TTS ready).txt`) — też poza repo.
- Jakiejkolwiek listy per-SID dla ~4 638 brakujących linii. `SPEAKER_VOICE_MAPPING.csv` to
  **agregat** (1 przykładowa linia na grupę postaci), nie pełna lista 1 675 linii.
- Tekstu angielskiego dla żadnego z 16 402 pokrytych SID-ów. Kolumna `Text` w
  `FINAL_lektor_mapping.csv` to **polski** tekst oficjalny (potwierdzone treścią: „Cześć.”, „Do
  zobaczenia.” itd.), nie angielski. Dopasowanie w tym pipeline już od początku działa na tekście
  polskim (`match_lektor.ps1` normalizuje i porównuje polski `sid_phrase_*` z polską listą linii
  GameReader), nie angielskim.

**Konsekwencja:** `ExampleEnglishText` w `SPEAKER_VOICE_MAPPING.csv` (jedyny tekst, jaki mamy dla
brakujących linii) jest w **innym języku** niż `Text` w `FINAL_lektor_mapping.csv` (polski). Nie
da się ich bezpośrednio porównać tekstowo bez tłumaczenia — a zgadywania tłumaczeń miałem
wyraźnie unikać.

## 2. Liczba SID z polskim audio

**16 402** (dokładnie `FINAL_lektor_mapping.csv`, bez duplikatów SID).

## 3. Liczba SID bez polskiego audio

- Całkowita liczba wypowiadanych linii w grze: **~21 040** — to liczba udokumentowana w
  `README.md`/`docs/architecture.md`, **nie da się jej zweryfikować z danych w tym repo** (brak
  pełnej listy SID-ów z gry). Traktuję ją jako daną wejściową, nie wynik własnego pomiaru.
- Implikowana luka: 21 040 − 16 402 = **4 638**.
- Z tego **1 675** ma znany kod postaci i jeden przykładowy tekst (`SPEAKER_VOICE_MAPPING.csv`).
- Pozostałe **2 963** = **UNKNOWN** — brak jakichkolwiek danych w repo (ani SID, ani tekstu, ani
  postaci). Znana część tej reszty to udokumentowane w `architecture.md` cutsceny (75 unikalnych
  kwestii spośród 225 par SID/asset, 30/292 cutscenek) — czyli dużo mniej niż 2 963, więc
  większość tej reszty jest **całkowicie nieopisana** w repo.

## 4. Liczba unikalnych tekstów

- W zbiorze **pokrytym** (16 402 SID): **14 951** unikalnych dokładnych tekstów polskich.
- W zbiorze **brakującym**: **UNKNOWN** — nie mamy tekstu per SID, tylko 120 przykładów
  reprezentujących 1 675 linii (i zero danych dla pozostałych 2 963).

## 5. EXACT TEXT MATCH

Policzalne tylko **wewnątrz** zbioru pokrytego (bo tylko tam mamy tekst per SID):

- Teksty występujące u 2+ SID-ów: **636** grup.
- SID-y uczestniczące w takiej grupie: **2 087**.
- „Nadwyżkowe” SID-y ponad pierwszy w grupie (czyli już zrealizowany reużyty zapis): **1 451**.
- We wszystkich 636 grupach duplikatów **każdy SID w grupie wskazuje na ten sam plik `Audio1`**
  (0 grup z rozjazdem audio przy identycznym tekście) — pipeline już poprawnie reużywa jednego
  nagrania GameReader dla wielu SID-ów o tym samym tekście, dokładnie tak jak opisuje hipoteza.

Dopasowanie **brakujące → pokryte** (czyli to, co naprawdę chcemy wiedzieć): **NIE DA SIĘ
POLICZYĆ** — nie mamy tekstu polskiego dla żadnego z brakujących SID-ów, a mamy tylko angielski
przykład dla 120 grup postaci (nieporównywalny bezpośrednio z polskim `Text`).

## 6. NORMALIZED MATCH

Wewnątrz zbioru pokrytego: normalizacja (lowercase, redukcja spacji, usunięcie podstawowej
interpunkcji) zmniejsza liczbę unikalnych tekstów z 14 951 do **14 769** — czyli **169** grup
normalizowanych łączy 2+ różne warianty dokładnego tekstu (typowo różnice w interpunkcji/wielkości
liter).

Dopasowanie brakujące → pokryte: **UNKNOWN**, z tego samego powodu co w punkcie 5.

## 7. FUZZY MATCH

Nie wykonywano (miało być osobną kategorią do ręcznej kontroli) — i tak zablokowane brakiem
tekstu dla strony „brakującej”. Uwaga: `MatchType=fuzzy` w `FINAL_lektor_mapping.csv` (2 933 z
16 402 wierszy) to **inna rzecz** — to dopasowanie polskiego tekstu oficjalnego do linii GameReader
przy drobnych różnicach (np. edycje pod TTS), a nie dopasowanie różnych SID-ów do siebie
nawzajem. Nie mylić tych dwóch osi.

## 8. Rzeczywista liczba unikalnych brakujących tekstów

**UNKNOWN.** Wymaga danych, których nie ma w tym repo (patrz §1).

## 9. TOP 100 powtarzających się tekstów

Można to policzyć **tylko dla zbioru pokrytego** (jedyny zbiór z tekstem per SID). Pełna tabela
(100 pozycji, z liczbą wystąpień SID i liczbą różnych plików `Audio1`) zapisana jako
`top100_covered_duplicates.csv` w scratchpadzie sesji (mogę przesłać na żądanie). Top 15 poniżej:

| Rank | Tekst (PL) | Liczba SID | Różnych plików Audio1 |
|---|---|---|---|
| 1 | „Na razie." | 41 | 1 |
| 2 | „O czym rozmawialiśmy?" | 32 | 1 |
| 3 | „Czego chcesz?" | 29 | 1 |
| 4 | „Później, dobra?" | 23 | 1 |
| 5 | „Uważaj na siebie." | 23 | 1 |
| 6 | „Nie teraz." | 22 | 1 |
| 7 | „Tak." | 19 | 1 |
| 8 | „O czym to ja mówiłem?" | 18 | 1 |
| 9 | „(krzyczy)" | 17 | 1 |
| 10 | „Do zobaczenia." | 16 | 1 |
| 11 | „Powodzenia." | 16 | 1 |
| 12 | „Cholera!" | 16 | 1 |
| 13 | „Nie widzisz, że jestem zajęty?" | 15 | 1 |
| 14 | „Cześć." | 14 | 1 |
| 15 | „A więc…" | 14 | 1 |

To dokładnie ten typ krótkich, powtarzalnych kwestii NPC, o który pytałeś — i w zbiorze
**pokrytym** widać jasno, że mechanizm „1 tekst = 1 reużywalny plik audio” **już działa** w
obecnym pipeline.

## 10. Przykłady największych grup — zbiór brakujący (1 675, po postaciach)

Same dane co dostępne: `SpeakerCodename` → `LineCount` → 1 przykładowa angielska kwestia. Kilka
największych grup, z widocznym wzorcem krótkich, powtarzalnych barków:

| Postać | LineCount | Przykład (EN) |
|---|---|---|
| pc (Skif) | 244 | „C-Consciousness? That rings a bell." |
| banzaj_0 (Banzai) | 201 | „We opened this closet up…" |
| gonta (Honta) | 182 | „Fuck, Skif! Everyone move!" |
| vozatyj_0 (Harcmistrz) | 178 | „It's become our home…" |
| PC (Skif, drugi kod) | 130 | „Do you remember anything about the disaster in '86?" |

Dodatkowo wśród **jednoliniowych** grup (`LineCount=1`) powtarza się wiele pożegnań/powitań w
stylu „Hello, stalker.", „Hi, stalker!", „So long, stalker!", „Buh-bye.", „Bye-bye!", „Yes,
stalker?" u różnych, niepowiązanych postaci (marsal, malar, moroz, sanchez, kudzo, vova_pantera,
s_hyphen_t_igor_valunec i inni) — **wzorcowo pasuje** do hipotezy z Twojego opisu (różni NPC,
identyczny/bardzo podobny tekst). **Nie mogę tego jednak potwierdzić dopasowaniem tekstowym** —
mam tylko 1 przykład na postać (nie wiem, czy to jedyna linia tej postaci w grupie, czy tylko
próbka), i jest on po angielsku, a jedyny tekst do porównania w zbiorze pokrytym jest po polsku.

## 11. Wnioski

1. Mechanizm „jeden polski zapis lektora dla wielu SID-ów o tym samym tekście” **realnie istnieje
   i już działa** w obecnym pipeline — potwierdzone bezpośrednio na 636 grupach / 2 087 SID w
   zbiorze pokrytym, z zerowymi rozjazdami audio.
2. To jest jednak dowód **pośredni** dla hipotezy o zbiorze brakującym, nie dowód bezpośredni —
   bo nie mamy tekstu per SID dla żadnej z ~4 638 brakujących linii.
3. Jedyny wgląd w tekst brakujących linii to 120 przykładów w `SPEAKER_VOICE_MAPPING.csv`
   (1 na grupę postaci, po angielsku) — wizualnie bardzo zgodny z hipotezą (dużo krótkich barków
   typu „Hello, stalker."/„Buh-bye."), ale to obserwacja jakościowa, nie policzalne dopasowanie.
4. 2 963 z 4 638 brakujących linii (64%) nie ma **żadnych** danych w tym repo — nawet takich jak
   dla pozostałych 1 675. To największa niewiadoma w całej analizie.
5. Językowy rozjazd (polski `Text` w zbiorze pokrytym vs. angielski `ExampleEnglishText` w
   zbiorze brakującym) jest strukturalną przeszkodą, niezależną od ilości danych — nawet z pełną
   listą brakujących SID-ów, jeśli miałaby tylko angielski tekst, wprost porównanie z
   `FINAL_lektor_mapping.csv` (polskim) nadal wymagałoby albo tłumaczenia, albo dotarcia do tego
   samego źródła polskiego `sid_phrase_*`, którego użyto do zbudowania zbioru pokrytego.

## 12. Czy hipoteza jest potwierdzona, częściowo potwierdzona czy obalona

**Częściowo potwierdzona.** Mechanizm reużycia jednego zapisu dla wielu SID-ów o identycznym
tekście jest realny i działa w praktyce (dowód bezpośredni, §5/§9). Natomiast **konkretna liczba
z ~4 638 brakujących SID-ów, którą dałoby się pokryć istniejącymi nagraniami GameReader, jest w
tej chwili nieustalona (UNKNOWN)** — nie dlatego, że hipoteza jest błędna, tylko dlatego, że dane
potrzebne do policzenia tego per SID (polski tekst każdej z brakujących linii) nie znajdują się w
tym repozytorium.

## 13. Co dalej

Żeby domknąć tę analizę do konkretnej liczby, potrzebny jest **jeden dodatkowy plik**: pełna lista
`sid_phrase_*` → polski tekst dla WSZYSTKICH linii gry (pokrytych i niepokrytych) — dokładnie to,
co `match_lektor.ps1` już wczytuje jako `Localization_PL.json`, tylko że ten plik leży poza
katalogiem tego projektu i nie był tu czytany zgodnie z instrukcją. Jeśli dostarczysz ten plik (lub
wskażesz go w obrębie katalogu projektu), mogę:

1. Zbudować pełny indeks: znormalizowany tekst PL → wszystkie SID-y → istniejący polski WEM →
   brakujące SID-y (dokładnie jak w Twoich sekcjach 6–9).
2. Policzyć realną liczbę EXACT/NORMALIZED/FUZZY dopasowań brakujące→pokryte.
3. Podać dokładną liczbę z ~4 638, którą można pokryć już istniejącymi nagraniami GameReader —
   bez dodatkowych nagrań.

Bez tego pliku dalsza analiza tekstowa na poziomie pojedynczych SID-ów nie jest możliwa w oparciu
wyłącznie o dane z tego repo.
