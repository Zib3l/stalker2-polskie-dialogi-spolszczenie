# S.T.A.L.K.E.R. 2: Heart of Chornobyl — polskie dialogi (spolszczenie)

Zestaw narzędzi i mapowanie SID → tekst → plik audio → identyfikator w grze, pozwalające
podmienić angielskie/ukraińskie kwestie dialogowe w S.T.A.L.K.E.R. 2: Heart of Chornobyl na
polski lektor, wykorzystując istniejące nagrania [GameReadera](https://gamereader.pl) (autor:
Rafko) — darmowej, nakładkowej aplikacji, która czyta na głos napisy gry w czasie rzeczywistym.

## Co to właściwie daje

Efekt końcowy: uruchamiasz grę, ustawiasz język głosu na angielski, a **~78% wypowiadanych
kwestii dialogowych leci po polsku**, czytane przez lektora, zamiast oryginalnego
angielskiego/ukraińskiego voice-actingu. To nie jest osobna instalacja ani modyfikacja pliku
wykonywalnego gry — to zwykły, wypakowywalny mod: jeden zestaw `.pak/.ucas/.utoc`, który wrzucasz
do `~mods` i w każdej chwili możesz usunąć, żeby wrócić do oryginału. **Ten główny pipeline jest
przetestowany i potwierdzony jako działający w grze** — pipeline dla cutscenek nie.

**Czego to NIE robi:**
- Nie generuje nowego lektora — wykorzystuje gotowe nagrania GameReadera (musisz je mieć sam,
  np. przechodząc grę raz z uruchomionym GameReaderem).
- Nie obejmuje przerywników filmowych (cutscenek) — te wciąż będą po angielsku/ukraińsku. Powód:
  w cutscenkach audio lektora jest podzielone na mniejsze części i wywoływane pod konkretną
  klatkę animacji (osobny fragment na osobny moment cutscenki), podczas gdy GameReader nagrywa
  całe, kompletne kwestie w jednym pliku — dopasowanie jednego do drugiego 1:1 nie działa tak
  prosto jak przy zwykłych dialogach i wymaga osobnego mechanizmu (patrz
  [docs/architecture.md](docs/architecture.md#cutscenes-excluded)). Żeby to realnie wspierać,
  trzeba by mieć osobne nagrania na każdy fragment danej cutscenki (nie jedno nagranie całej
  kwestii) — takich nagrań GameReader nie dostarcza.
- Nie pokrywa 100% dialogów — ok. 22% linii to albo te, których GameReader jeszcze nie nagrał,
  albo linie sterowane innym systemem gry (patrz [Ograniczenia](#ograniczenia)).

**Jak to działa, w skrócie** (pełny opis: [docs/architecture.md](docs/architecture.md)):

1. `data/FINAL_lektor_mapping.csv` (już gotowy, dołączony do repo) mówi dla każdej kwestii
   dialogowej: jaki to tekst, który plik GameReadera go czyta, i pod jakim dokładnie
   identyfikatorem ta kwestia siedzi w plikach dźwiękowych gry (Wwise SoundBanki).
2. `BatchEncoder` bierze Twoje pliki GameReadera wskazane w tym CSV i koduje je do formatu, jakiego
   oczekuje silnik gry (`.wem`, przez Wwise).
3. `FullPatcher` otwiera oryginalne pliki gry, odnajduje dokładnie te bajty, w których siedzi
   angielskie nagranie danej kwestii, i podmienia je na nowo zakodowane polskie audio — bez
   ruszania niczego innego w pliku.
4. Wynik pakuje się (`UnrealReZen`) do formatu, jaki silnik gry faktycznie wczytuje jako mod, i
   wrzuca do `~mods`.

Wszystko poniżej to konkretne kroki, jak to u siebie odtworzyć.

## Status

- **Główny pipeline dialogowy: działa** (potwierdzone w grze). Zwykłe dialogi w grze, bez
  przerywników filmowych (cutscenek).
- **Pipeline dla cutscenek: eksperymentalny / wstrzymany.** Nie jest częścią tego repo — patrz
  [docs/architecture.md](docs/architecture.md#cutscenes-excluded).

## Co jest w tym repo, a czego nie ma

**Jest:**
- `data/FINAL_lektor_mapping.csv` — mapowanie 16 402 kwestii dialogowych (78% wszystkich
  wypowiadanych linii w grze): SID → oficjalny polski tekst → nazwa pliku audio GameReadera →
  prawdziwy identyfikator media w SoundBankach gry. To jest kluczowy plik — bez niego nikt nie
  odtworzy tego pipeline'u.
- `data/SPEAKER_VOICE_MAPPING.csv` — dla 1675 linii z luki pokrycia (patrz
  [Ograniczenia](#ograniczenia)): kod postaci → prawdziwe imię w grze → sugerowany profil głosu →
  przykładowa linijka. Konkretna lista do przyszłego dogrania, nie tylko sama liczba.
- Kod źródłowy własnych narzędzi (`scripts/BatchEncoder`, `scripts/FullPatcher`) — gotowe do
  kompilacji przez `dotnet build`, plus `build.ps1` spinający cały pipeline w jedno polecenie.
- Projekt źródłowy Wwise (`WwiseProject/LektorProject`) — bez wygenerowanych banków/audio.
- Dokumentacja architektury i dokładna łatka na UnrealReZen (`docs/`, `patches/`).

**Nie ma (celowo):**
- Żadnych plików audio (`.ogg`/`.wav`/`.wem`) — ani nagrań GameReadera, ani wygenerowanych WEM-ów.
- Żadnych plików gry (`.pak`/`.ucas`/`.utoc`/`.uasset`/`.ubulk`, `Mappings.usmap`).
- Gotowego moda (spakowanego `LektorMainOnly_P`).
- Backupów, logów, katalogów roboczych ani eksperymentalnych narzędzi do cutscenek.

## Wymagania

- Windows, .NET SDK 10 (patrz `.csproj` każdego narzędzia)
- Legalna kopia S.T.A.L.K.E.R. 2: Heart of Chornobyl (dla `Paks` i własnego `.usmap`)
- Własny zestaw nagrań GameReadera (folder `audio/` z jego eksportu) — nie jest dołączony
- Wwise Authoring **2026.1.2.9249** (dla `WwiseConsole.exe`) — dokładna wersja, z jaką stworzono
  `WwiseProject/LektorProject.wproj`; wymaga darmowej rejestracji konta na audiokinetic.com, żeby
  w ogóle pobrać instalator
- [UnrealReZen](https://github.com/rm-NoobInCoding/UnrealReZen), zbudowany z łatką z
  `patches/UnrealReZen.patch` — patrz [docs/unrealrezen-build.md](docs/unrealrezen-build.md)

## Czego musisz sam przygotować, zanim zbudujesz moda

1. **Legalna kopia gry**, z dostępnym lokalnie `Stalker2\Content\Paks`.
2. **Własny plik `.usmap`**, wygenerowany z Twojej własnej, aktualnej instalacji gry przez
   [UE4SS](https://github.com/UE4SS-RE/RE-UE4SS) (publiczne/starsze mapowania zwykle nie pasują
   do najnowszego patcha) — dodatkowo skorzystaj z community fixa dla wersji 2.0
   ([mod 2341 na Nexusie](https://www.nexusmods.com/stalker2heartofchornobyl/mods/2341)).
   Uruchom grę, wciśnij domyślny skrót UE4SS do zrzutu mapowań (Ctrl+Numpad6), skopiuj powstały
   `.usmap` i wskaż go w `config.json`.
3. **Nagrania GameReadera** — folder z jego plikami `output1 (N).ogg` (drugie ujęcie, `output2`,
   nie jest używane w tym pipeline). Wskaż ten folder jako `gameReaderAudioDir` w `config.json`.
4. **Wwise Authoring 2026.1.2.9249** — pobranie instalatora wymaga darmowego konta na
   audiokinetic.com.
5. **UnrealReZen**, zbudowany z łatką — patrz [docs/unrealrezen-build.md](docs/unrealrezen-build.md).
6. **`oo2core_9_win64.dll`** (biblioteka Oodle) — do wskazania w `config.json` jako
  `oodleDllPath`. Można ją znaleźć np. w instalacji [FModel](https://github.com/4sval/FModel)
  albo w paczce release'owej UnrealReZen.

Żadne z powyższego nie jest częścią tego repo — to albo materiał chroniony prawem autorskim
(gra), albo dane związane z Twoją własną instalacją, albo cudza, duża biblioteka nagrań.

## Szybki start

1. Sklonuj to repo.
2. Skopiuj `config.example.json` → `config.json` i uzupełnij swoje ścieżki (patrz wyżej).
   `config.json` jest w `.gitignore` — nigdy nie commituj prawdziwych lokalnych ścieżek.
3. Przygotuj wymagane wejścia (punkt wyżej).

**Najprostsza ścieżka:** po wypełnieniu `config.json` (w tym `unrealRezenExe` i `gameRoot`,
patrz `config.example.json`) uruchom po prostu:
```powershell
.\build.ps1          # buduje, koduje audio, patchuje, pakuje - kończy na build\Release\
.\build.ps1 -Deploy   # to samo, plus automatycznie kopiuje do ~mods
```
Poniższe kroki 4–8 to dokładnie to, co ten skrypt robi w środku — przydatne, jeśli coś nie
zadziała i trzeba znaleźć, na którym etapie, albo jeśli wolisz kontrolować to ręcznie.

4. Zbuduj narzędzia:
   ```
   dotnet build scripts/BatchEncoder
   dotnet build scripts/FullPatcher
   ```
5. **Kodowanie audio** — uruchom `BatchEncoder`: konwertuje dopasowane pliki `.ogg` na `.wav`
   (równolegle, przez `ffmpeg`), potem jednym przebiegiem koduje wszystko do `.wem` przez
   `WwiseConsole convert-external-source`.
   ```
   dotnet run --project scripts/BatchEncoder -c Release
   ```
6. **Patchowanie** — uruchom `FullPatcher`: dla każdej linii z mapowania odnajduje właściwy
   zasób `VO_..._<SID>.uasset` w grze, lokalizuje bajtowy zakres oryginalnego audio w jego
   `BulkDataMap` i wstawia w to miejsce nowy `.wem`.
   ```
   dotnet run --project scripts/FullPatcher -c Release
   ```
7. **Repakowanie** narzędziem UnrealReZen do kontenera IoStore (patrz
   [docs/unrealrezen-build.md](docs/unrealrezen-build.md) po dokładne kroki budowy):
   ```
   UnrealReZen.exe --game-dir "<ścieżka do S.T.A.L.K.E.R. 2 Heart of Chornobyl>" ^
     --content-path "build\ModOutput" --engine-version GAME_UE5_5 ^
     --compression-format Oodle --output-path "<out>\LektorMainOnly_P.utoc"
   ```
8. **Wdrożenie**: skopiuj powstałe `LektorMainOnly_P.pak/.ucas/.utoc` do
   `Stalker2\Content\Paks\~mods\` (najpierw wyczyść tam stare pliki — patrz niżej).

## Wdrażanie i testowanie

1. Zawsze zaczynaj od czystego `~mods` — inaczej `FullPatcher`/`UnrealReZen` mogą odczytać
   poprzedni (być może niespójny) wynik patcha zamiast oryginalnego pliku gry:
   ```powershell
   Remove-Item "<gra>\Stalker2\Content\Paks\~mods\*"
   ```
2. Skopiuj świeżo zbudowane `LektorMainOnly_P.pak/.ucas/.utoc` do `~mods\`.
3. Uruchom grę, **ustaw język audio/głosu na angielski** (patchujemy tylko ten slot językowy),
   wejdź w scenę z dialogiem i sprawdź, czy leci polskie audio.
4. Sprawdź `%LOCALAPPDATA%\Stalker2\Saved\Logs\Stalker2.log`:
   - `Mounted IoStore container` — dobrze, mod się załadował.
   - `Invalid container header` — nieszkodliwe, pojawia się nawet przy poprawnie działającej
     paczce.
   - `ParserException`, `IndexOutOfRangeException` albo crash przy ładowaniu — źle, coś w
     spatchowanej paczce jest uszkodzone.
5. Żeby cofnąć zmiany — po prostu usuń `.pak`/`.ucas`/`.utoc` moda z `~mods\`.

## Struktura repozytorium

```
build.ps1                      - spina cały pipeline w jedno polecenie (patrz Szybki start)
data/
  FINAL_lektor_mapping.csv     - kluczowe mapowanie SID -> tekst -> audio -> MediaId
  SPEAKER_VOICE_MAPPING.csv    - luka pokrycia: kod postaci -> imię -> sugerowany głos
scripts/
  BatchEncoder/                - koduje dopasowane audio przez WwiseConsole
  FullPatcher/                 - wstawia zakodowane WEM-y w miejsce oryginalnego audio gry
  mapping-reference/           - jak powstał FINAL_lektor_mapping.csv (nie wymagane do builda)
WwiseProject/                  - projekt źródłowy Wwise (bez wygenerowanych banków/audio)
docs/
  architecture.md              - pipeline krok po kroku
  unrealrezen-build.md         - dokładna łatka i instrukcja builda UnrealReZen
patches/
  UnrealReZen.patch            - łatka (git apply) naprawiająca budowanie kontenera IoStore
config.example.json            - szablon lokalnej konfiguracji
```

## Ograniczenia

- Pokrywa 16 402 z ~21 040 wypowiadanych linii w grze (78%) — reszta to linie, których
  GameReader jeszcze nie nagrał, albo linie sterowane przez system cutscenek (patrz wyżej).
  Z tego ok. **1675 linii ma realny, gotowy do podmiany zasób głosowy w grze, ale wciąż brakuje
  do nich polskiego nagrania** — to konkretny, policzalny cel na przyszłość (np. dogranie ich
  przez GameReadera albo innym lektorem/TTS), w odróżnieniu od linii spoza standardowego
  systemu dialogowego, których ten pipeline w ogóle nie dotyka.
- Wymaga ręcznej konfiguracji Wwise w wersji zgodnej z tą użytą podczas developmentu.
- `data/FINAL_lektor_mapping.csv` zawiera oficjalny polski tekst dialogów gry (kolumna `Text`) —
  potrzebny, żeby ktokolwiek mógł zweryfikować/rozszerzyć mapowanie bez ponownego eksportowania
  go z gry.

## Licencja

TODO — nieustalona. To repo zawiera własny kod napisany na potrzeby tego projektu, obok
zależności od narzędzi firm trzecich (CUE4Parse, UnrealReZen, Wwise) na ich własnych licencjach.
Nie zakładaj żadnej licencji, dopóki to się nie wyjaśni.

**Nagrania GameReadera używane przez ten pipeline są cudzą własnością** (autor: Rafko,
[gamereader.pl](https://gamereader.pl)) i udostępnione do niekomercyjnego użytku w ramach
GameReadera. Ten projekt jest z nim niepowiązany. Jeśli chcesz wykorzystać ten pipeline lub jego
efekty **komercyjnie**, najpierw napisz do Rafko i uzyskaj jego zgodę — nie zakładaj, że masz do
tego prawo tylko dlatego, że masz do tych nagrań techniczny dostęp.
