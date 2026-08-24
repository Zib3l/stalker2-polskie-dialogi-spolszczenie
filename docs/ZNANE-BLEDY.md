# Znane błędy — stan na 2026-08-24

## ⛔ KRYTYCZNY: kontenery budowane tym pipeline'em nie rejestrują pakietów

> **Potwierdzone ponownie 2026-08-24** na świeżym buildzie (UnrealReZen `bf9e8de` + pełny
> `patches/UnrealReZen.patch`, kompresja Oodle, 35 243 pliki):
>
> ```
> LogIoDispatcher: Display: Toc loaded : .../~mods/LektorMain_EN_P.utoc, Id=1ab9f282a683ddc4, ..., EntryCount=35243
> LogIoDispatcher: Warning: Invalid container header in file '.../~mods/LektorMain_EN_P'
> LogFilePackageStore: Mounting container: Id=ffffffffffffffff, Order=103, NumPackages=0
> ```
>
> TOC ładuje się z prawdziwym `Id`, ale nagłówek kontenera jest odrzucany i magazyn pakietów
> nie przyjmuje niczego. Mod gra po polsku (surowe bajty WEM są czytane z naszego kontenera),
> ale obowiązuje oryginalny `SerialSize` — dłuższe polskie kwestie są ucinane. Łatka
> UnrealReZen w obecnym kształcie **nie rozwiązuje** tego problemu.

**Mod zbudowany według instrukcji z README ucina dłuższe polskie kwestie.** Przyczyna jest
znana i leży w narzędziu pakującym (`UnrealReZen`), nie w mapowaniu ani w samych nagraniach.

### Objaw

Kwestia po polsku urywa się w połowie — zawsze dokładnie w momencie, w którym kończyło się
oryginalne angielskie nagranie. Krótsze kwestie (mieszczące się w długości oryginału) grają
poprawnie, dlatego problem długo uchodził za sporadyczny.

### Dowód

W `%LOCALAPPDATA%\Stalker2\Saved\Logs\Stalker2.log`, przy zamontowaniu naszego moda:

```
LogIoDispatcher: Warning: Invalid container header in file '.../~mods/LektorMainOnly_P'
LogFilePackageStore: Mounting container: Id=ffffffffffffffff, Order=103, NumPackages=0
```

Dla porównania kontener bazowy gry:

```
LogFilePackageStore: Mounting container: Id=2628b4310249af2e, Order=3, NumPackages=122771
```

**`NumPackages=0`** oraz `Id=ffffffffffffffff` zamiast prawdziwego identyfikatora z TOC
oznaczają, że nagłówek kontenera został odrzucony i magazyn pakietów nie przyjął z naszego
moda **niczego**.

### Mechanizm

Skoro pakiety nie są zarejestrowane, silnik ładuje **oryginalny plik `.uasset` z gry**, ale
surowe bajty dźwięku pobiera po identyfikatorze chunka **z naszego kontenera**. W efekcie:

- obowiązuje oryginalny `BulkDataMap.SerialSize`, czyli **rozmiar angielskiego nagrania**
- z naszego dłuższego polskiego pliku silnik odczytuje tylko tyle bajtów
- wszystko, co zapisujemy w nagłówku `.uasset`, jest ignorowane

To jedno wyjaśnienie pokrywa każdą obserwację: podmiana samych bajtów działa (dlatego mod
w ogóle mówi po polsku), a każda zmiana nagłówka — nie.

### Uwaga historyczna

Ostrzeżenie `Invalid container header` było przez długi czas opisywane w notatkach projektu
jako nieszkodliwe, ponieważ mod „działał". **To ustalenie jest wycofane.** Ostrzeżenie jest
bezpośrednim sygnałem tego błędu.

### Naprawa

Poprawić generowanie nagłówka kontenera w `UnrealReZen` tak, aby `NumPackages > 0`.
W `patches/UnrealReZen.patch` są już trzy poprawki tego narzędzia — to będzie czwarta
i jedyna, która blokuje poprawność wyniku.

**Weryfikacja nie wymaga uruchamiania gry na długo** — wystarczy zbudować mod, odpalić grę
i sprawdzić w logu, czy linia `LogFilePackageStore: Mounting container:` dla naszego pliku
pokazuje prawdziwe `Id=` oraz `NumPackages` równe liczbie spakowanych pakietów.

---

## Powiązane: rozmiar mediów zapisany dwukrotnie

Niezależnie od powyższego ustalono, że rozmiar nagrania jest w plikach gry przechowywany
**w dwóch miejscach**:

| miejsce | czy pipeline to aktualizuje |
|---|---|
| wpis `BulkDataMap` | tak |
| bank Wwise (`BKHD`/`HIRC`), sąsiedni region bulk | **nie** |

Dowód (`VO_lesij_ANCQ02_Comment_Fight_angry_FF_51569`):

- oryginał gry: dźwięk `RIFF` = 33 397 B, bank podaje 33 397 → zgodne
- po podmianie: dźwięk = 30 653 B, bank **nadal 33 397** → niezgodne
- bank ukraiński (nietknięty) pozostaje zgodny, co potwierdza znaczenie tego pola

Poprawka została napisana i zweryfikowana na poziomie bajtów w prywatnej kopii roboczej, ale
**nie da się jej potwierdzić w grze**, dopóki nie zostanie naprawiony nagłówek kontenera —
bo zmiany w `.uasset` i tak nie docierają do silnika.

---

## Cutscenki

Cutscenki są **poza zakresem tego wydania**. Szczegóły w README. Prace nad nimi ujawniły oba
powyższe błędy.

Dodatkowo ustalono, że jeden napis w cutscence odpowiada zwykle **kilku** sekcjom audio
(z 1135 przeanalizowanych kwestii tylko 468 mieści się w jednej sekcji). Nagranie wstawione
w jedną sekcję zostawia pozostałe z oryginalnym angielskim dźwiękiem — stąd słyszalne
nakładanie się języków. Wyciszenie tych fragmentów usuwa problem i jest to jedyna poprawka
z tego obszaru potwierdzona w grze.
