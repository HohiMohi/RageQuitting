# Znane problemy odłożone na później

## KI-003: Okazjonalna odmowa SingleCarry F/E podczas ruchu

- **Status:** otwarty, odłożony; nie blokuje bieżącego etapu produkcji
- **Priorytet:** niski, ponowna diagnoza przed dalszymi zmianami synchronizacji F/E
- **System:** SingleCarry placement
- **Tryb:** multiplayer; owner porusza się klawiszami WASD i zatwierdza `E`
- **Data potwierdzenia:** 2026-11-05; aktualizacja stanu 2026-11-06

### Opis i potwierdzony zakres

W rzadkiej, naturalnej sekwencji zdalny owner może zobaczyć poprawny podgląd i otrzymać odmowę serwera po zatwierdzeniu `E`, po czym obiekt pozostaje trzymany, preview/WASD wracają, a istniejąca walidacja odrzuca pozycję. Potwierdzony historyczny warunek testowy to Unity Editor loopback z remote ownerem, rzeczywisty ruch `CharacterController` podczas `F`, natychmiastowe `E` oraz `NetworkSimulator.PacketDelayMs` 100, jitter 0 i loss 0. W naturalnej odmowie kompletna odebrana poza i kontekst cache pasowały do odebranej pozy; `InvalidAimOrigin` / `ResidualExceeded` wystąpiło, gdy zmierzony residual przekroczył istniejący limit 0,15 m. Dwie historyczne wartości residualu to 0,251468873 m i 0,271069615 m.

Nie ustalono wiarygodnego deterministycznego repro, przyczyny ani zweryfikowanego obejścia. Nie zwiększano tolerancji i nie zmieniano RPC/protokołu ani produkcyjnego cache API. Nie traktować odzyskania ruchu po odmowie jako naprawy źródła problemu.

### Możliwe rozwiązanie do rozważenia: przytrzymanie E

**Propozycja niewdrożona i niezweryfikowana:** zamiast natychmiast wysyłać żądanie po naciśnięciu `E`, owner przytrzymuje `E` przez krótki czas, np. 0,2 s (wartość wstępna, niewybrana testowo). Od początku przytrzymania sterowany ruch i rozglądanie są blokowane, a podgląd i promień pozostają spójne. `NetworkTransform` nadal replikuje pozycję/rotację; żądanie jest wysyłane dopiero po ukończeniu przytrzymania. Celem jest danie czasu na przekazanie ustabilizowanej pozy przed żądaniem, co może ograniczyć odmowy. Nie gwarantuje to otrzymania stanu przez serwer w stałym czasie ani nie potwierdza usunięcia przyczyny KI-003.

Do przyszłego ustalenia pozostaje zachowanie przy wcześniejszym puszczeniu `E` lub przerwaniu, faktyczne przesunięcia przez grawitację/impulsy/platformy mimo blokady wejścia oraz to, czy blokady trwają do odpowiedzi serwera. Ta propozycja nie rozstrzyga tych decyzji ani nie zmienia mechaniki.

### Wyniki i kwalifikacje

Dwie sparowane serie z 2026-11-06 obejmowały łącznie 80 prób: 40 przy 0 ms i 40 przy 100 ms, bez odmów w tych 80 próbach. To nie jest estymata częstości w grze i nie dowodzi usunięcia problemu. Odmowy z 2026-11-05 pozostają poza tymi seriami.

Nowsza walidacja narzędzia zakończyła się Verify 8/9: naturalna próba remote ownera przy 100 ms odmówiono jako `InvalidAimOrigin` / `ResidualExceeded`, residual 0,2938803287 m > 0,15 m, z dostępnością odebranej pozy, kompletnym cache i pełnym kontekstem. Trace i indeks: `Tools/Diagnostics/SingleCarryPlacement/Evidence/ValidationFindings/verify-fixed-natural-100ms-refusal-trace.json` oraz `verify-fixed-trace-index.json`. Ta próba jest poza historycznym zbiorem 80.

Wcześniejszy Verify 5/9 zawierał trzy błędy IO ścieżek diagnostycznych, naprawione przez krótkie nazwy GUID i kontrolę budżetu ścieżki; historia błędów pozostaje zachowana. Osobna próba 0 ms „all directions” zakończyła się `InvalidAimOrigin` w request 9, ale bez pełnej osi czasu/ustalonej gałęzi i przy innych błędach IO, zatem nie potwierdza tej samej przyczyny.

### Status odbioru

Instrumentacja jest opcjonalna, wyłącznie Editorowa, domyślnie wyłączona i nie zmienia RPC, tolerancji ani produkcyjnego cache. W raporcie OFF 9/9 i początkowe ON 9/9; Collect jednej pary 2/2; Verify 8/9 zakończył się błędem funkcjonalnym KI-003, który pozostaje nieblokujący dla bieżącego etapu. Ręczny odbiór host + osobny klient i wizualna akceptacja nie zostały wykonane. Wcześniejszy pełny Gameplay z 2026-11-06 nie przeszedł (PlayMode 260/262); zachować jako historycznie nieudany wynik, niezależny od późniejszej próby Verify. Końcowy Gameplay tego zadania: raport [final-validation.json](D:/Programy/UnityProjects/RageQuitting/Artifacts/SingleCarryDiagnosticsIsolation_20261106T180748Z/final-validation.json), pole `finalGameplay`.

Szczegóły i instrukcje obsługi: [plan i historia](SingleCarry-WASD-Synchronization-Followup-Plan.md), [manual diagnostyczny](D:/Programy/UnityProjects/RageQuitting/Tools/Diagnostics/SingleCarryPlacement/README.md).

## Cel dokumentu

Ten dokument rejestruje potwierdzone problemy, które nie blokują obecnego
etapu produkcji i nie wymagają natychmiastowej poprawki. Wpis pozostaje tutaj
do czasu rozpoczęcia prac nad wskazanym systemem albo podniesienia jego
priorytetu.

Każdy problem powinien zawierać kroki reprodukcji, rezultat obecny,
oczekiwane zachowanie oraz aktualny status.

## KI-001: Cofanie animacji przechyłu taczki po utracie synchronizacji

- **Status:** odłożony
- **Priorytet:** niski
- **System:** minigra wylewania betonu
- **Tryb:** multiplayer, dwóch graczy
- **Data zgłoszenia:** 2026-08-26

### Opis

Po rozpoczęciu przechylania taczki zbyt duża różnica między pozycjami
kursorów graczy cofa visual taczki do początkowej pozycji transportowej.
Problem nie kończy minigry natychmiast, jeśli różnica nie utrzyma się przez
czas wymagany do krytycznej porażki.

### Kroki reprodukcji

1. Wejdź dwoma graczami do minigry wylewania betonu.
2. Zwiększ progres obu graczy tak, aby taczka zmieniła początkowe położenie.
3. Zwiększ progres jednego gracza tak, aby różnica kursorów przekroczyła
   akceptowaną tolerancję.
4. Obserwuj położenie taczki.

### Rezultat obecny

Taczka wraca do początkowej pozycji transportowej.

### Rezultat oczekiwany

Po przekroczeniu tolerancji dalsze przechylanie powinno zostać zatrzymane,
ale taczka powinna zachować ostatni osiągnięty kąt do czasu ponownego
zsynchronizowania kursorów albo rozstrzygnięcia krytycznej porażki.

### Obejście

Utrzymywać różnicę kursorów w akceptowanej tolerancji podczas całej minigry.

## KI-002: Katapultowanie narzędzi przy uruchomieniu sceny

- **Status:** odłożony
- **Priorytet:** niski
- **System:** narzędzia i fizyka świata
- **Tryb:** uruchamianie sceny
- **Data zgłoszenia:** 2026-08-26

### Opis

Podczas inicjalizacji sceny narzędzia otrzymują niepożądany impuls fizyczny i
zostają rozrzucone wokół stojaka zamiast pozostać w przygotowanych pozycjach.

### Kroki reprodukcji

1. Uruchom scenę zawierającą narzędzia, na przykład `Tutorial_scene`.
2. Obserwuj narzędzia znajdujące się przy stojaku bez wykonywania interakcji.

### Rezultat obecny

Narzędzia zostają katapultowane i rozrzucone wokół stojaka.

### Rezultat oczekiwany

Narzędzia pozostają stabilnie na swoich skonfigurowanych miejscach do czasu
interakcji gracza albo zadziałania uzasadnionej siły zewnętrznej.

### Obejście

Ręcznie zebrać rozrzucone narzędzia albo ponownie umieścić je przy stojaku.
