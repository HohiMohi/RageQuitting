# Zasady używania lokalnego preflightu

## Cel i wymagania

Lokalny Agent Validation Harness sprawdza zmiany w otwartym projekcie Unity przed przekazaniem ich do review. Łączy analizatory, kontrolę kompilacji i Console, testy EditMode, szybkie walidatory oraz — w tierze Gameplay — testy PlayMode, scenariusz `TutorialBoot` i host-only `network-smoke`.

Polecenia należy uruchamiać z katalogu projektu:

```text
D:\Programy\UnityProjects\RageQuitting
```

Unity Editor powinien być osiągalny przez Pipeline, pozostawać w stabilnym EditMode i nie kompilować ani nie aktualizować assetów. Walidacja nie może zapisywać scen ani assetów. Przed uruchomieniem należy świadomie ocenić istniejący dirty worktree i zachować niezwiązane zmiany innych autorów.

## Wybór tieru

### Fast

`Fast` stosujemy dla dokumentacji, lokalnych narzędzi, skryptów Editor oraz wąskich zmian bez wpływu na runtime. Tier obejmuje podstawowe kontrole środowiska i Git, analizatory, kompilację, Console baseline, testy EditMode oraz quick validators.

```powershell
.\Tools\AgentHarness\Invoke-UnityPreflight.ps1 -Tier Fast -Json
```

### Gameplay

`Gameplay` stosujemy dla runtime C#, multiplayera, fizyki, inputu, scen, prefabów, ScriptableObjectów oraz assetów gameplayowych. Rozszerza Fast o testy PlayMode, scenariusz gameplayowy i network-smoke.

```powershell
.\Tools\AgentHarness\Invoke-UnityPreflight.ps1 -Tier Gameplay -Json
```

### Auto

`Auto` wolno używać tylko przy czystym worktree albo wtedy, gdy wszystkie obecne zmiany należą do jednego zadania. W dirty worktree zawierającym pracę z kilku źródeł automatyczny wybór może przypisać tier na podstawie niezwiązanych plików. Główny agent wybiera dokładny tier i końcową komendę w prompcie implementacyjnym, a implementator nie może tego tieru obniżyć.

## Iteracja i końcowa walidacja

Podczas implementacji można ograniczać testy parametrami:

```powershell
.\Tools\AgentHarness\Invoke-UnityPreflight.ps1 -Tier Fast -EditModeFilter "Pełna.Nazwa.Testu" -Json
.\Tools\AgentHarness\Invoke-UnityPreflight.ps1 -Tier Gameplay -PlayModeFilter "Pełna.Nazwa.Testu" -Scenario TutorialBoot -Json
```

`EditModeFilter` trafia wyłącznie do runnera EditMode, a `PlayModeFilter` wyłącznie do runnera PlayMode. Historyczny parametr `Filter` jest aliasem `EditModeFilter` i nie filtruje PlayMode. `Scenario` wybiera scenariusz gameplayowy; dla Gameplay domyślną wartością jest `TutorialBoot`.

Filtry służą iteracji. Po ostatniej poprawce trzeba uruchomić jeden niefiltrowany strict `Fast` albo `Gameplay`, odpowiednio do zakresu zadania. Wynik filtrowany nie zastępuje końcowej walidacji całego wybranego tieru.

## AllowLowerTier i KeepArtifacts

`AllowLowerTier` służy wyłącznie diagnostyce. Pozwala pominąć niedostępne, niższe lub jeszcze niezaimplementowane kontrole, ale nie zamienia rzeczywistego `failed` ani `blocked` w sukces. Wynik z pominiętymi wymaganymi krokami nie może stanowić podstawy akceptacji.

```powershell
.\Tools\AgentHarness\Invoke-UnityPreflight.ps1 -Tier Gameplay -AllowLowerTier -Json
```

`KeepArtifacts` stosujemy przy ważnym repro, spodziewanej awarii lub diagnostyce. Bez tego parametru działa domyślna retencja, która usuwa katalogi walidacyjne starsze niż 14 dni. Parametr nie zmienia wyniku testów.

```powershell
.\Tools\AgentHarness\Invoke-UnityPreflight.ps1 -Tier Gameplay -KeepArtifacts -Json
```

Preflight nie obsługuje parametru `ArtifactDirectory`.

## Statusy i kody wyjścia

| Status | Kod | Znaczenie |
|---|---:|---|
| `passed` | `0` | Wszystkie wymagane i wykonane kroki przeszły. |
| `failed` | `1` | Test, walidator, kontrola Git albo inny wymagany krok zakończył się błędem. |
| `failed` | `2` | Preflight napotkał nieobsłużony błąd samego skryptu lub jego orkiestracji. |
| `blocked` | `3` | Stan Edytora lub projektu uniemożliwił bezpieczne wykonanie. |
| `not_available` | `3` | Wymagana kontrola albo zależność nie była dostępna. |

Poszczególne kroki mogą mieć również status `skipped`. Przy `AllowLowerTier` pominięcie niedostępnego kroku może pozwolić na diagnostyczny wynik `passed`, ale taki przebieg nie jest końcowym dowodem akceptacji.

## Raporty i artefakty

Każdy przebieg tworzy katalog pod:

```text
Artifacts\Validation\<run-id>
```

Głównym raportem jest `summary.json`. Zawiera wybrany tier, rozwiązane filtry i scenariusz, status, kod wyjścia, backend, kroki, ostrzeżenia, listę artefaktów oraz snapshoty Git przed i po. Tryb `-Json` wypisuje na stdout pojedynczą kopertę z `summaryPath`, `status`, `exitCode` i `resolvedTier`.

Katalog przebiegu może także zawierać raporty podrzędnych runnerów, zachowane stdout/stderr, konteksty Edytora, wyniki testów i dane kontroli mutacji Git. Raport agenta musi podać status, kod wyjścia, ścieżkę `summary.json`, wszystkie kroki inne niż `passed` oraz ograniczenia.

## Oczekiwanie i czasy wykonania

Preflight uruchamiamy synchronicznie na pierwszym planie. Agent powinien użyć jednego długiego, ograniczonego oczekiwania i ograniczonego outputu zamiast często odpytywać proces.

- Fast trwa zwykle 16–24 sekundy; przyjmujemy budżet 30 sekund.
- Gameplay trwa zwykle 75–90 sekund.
- Po 180 sekundach należy jednokrotnie sprawdzić postęp, bez uruchamiania drugiego preflightu.
- Jeśli proces robi postęp, należy czekać do timeoutu runnera.
- Timeout albo brak postępu jest rzeczywistą awarią lub blokadą. Trzeba zachować i zgłosić raporty, stdout/stderr oraz końcowy kontekst Edytora.

Normalny przebieg nie wymaga powiadamiania ani wybudzania agenta przez użytkownika.

## Full i kontrole ręczne

Tier `Full` pozostaje nieukończony. Dla zadania wymagającego Full należy uruchomić strict Gameplay, dobrać dostępne testy celowane i jawnie zgłosić brakujące kontrole Full: `all-tests`, `scene-prefab-validation`, `expanded-multiplayer`, `coverage` i `windows-build`. Nie wolno przedstawiać Gameplay jako pełnego zastępstwa za Full.

Kontrola wizualna należy do użytkownika i nie jest krokiem automatycznego preflightu. Automatyczne screenshots oraz GitHub Actions pozostają poza zakresem lokalnego harnessu.

Okno narzędzia jest dostępne w Unity pod menu:

```text
Tools/RageQuitting/Validation
```

## Samodzielne narzędzia dokumentacji

`Invoke-DocumentationCheck.ps1` i `Invoke-DocumentationSync.ps1` są samodzielnymi narzędziami. Nie uruchamiają się automatycznie ani w preflighcie, ani w oknie `Tools/RageQuitting/Validation`.

Dokumentacja w `D:\Programy\UnityProjects\RageQuitting\Documentation` jest źródłem autorytatywnym. Synchronizacja działa wyłącznie w kierunku:

```text
D:\Programy\UnityProjects\RageQuitting\Documentation -> C:\Users\dunia\Documents\RageQuitting
```

Kontrola bez parametrów oraz z `-All` porównuje wszystkie dokumenty Markdown. Parametr `-Document` ogranicza ją do wskazanych plików:

```powershell
.\Tools\AgentHarness\Invoke-DocumentationCheck.ps1 -Json
.\Tools\AgentHarness\Invoke-DocumentationCheck.ps1 -All -Json
.\Tools\AgentHarness\Invoke-DocumentationCheck.ps1 -Document Agent-Validation-Harness.md -Json
```

Synchronizacja wymaga `-Document` albo `-All`; nie przyjmuje obu trybów jednocześnie. `-WhatIf` pokazuje plan bez kopiowania:

```powershell
.\Tools\AgentHarness\Invoke-DocumentationSync.ps1 -Document Agent-Validation-Harness.md -Json
.\Tools\AgentHarness\Invoke-DocumentationSync.ps1 -All -Json
.\Tools\AgentHarness\Invoke-DocumentationSync.ps1 -Document Agent-Validation-Harness.md -WhatIf -Json
```

Check porównuje dokładne bajty plików i zgłasza brakujące lub różne kopie lustrzane jako błąd. Pliki istniejące wyłącznie w mirrorze są raportowane informacyjnie jako `targetOnly` i nie powodują niepowodzenia.

Sync tworzy lub nadpisuje wybrane pliki w mirrorze, atomowo tam, gdzie jest to praktyczne, oraz tworzy brakujące katalogi docelowe. Nigdy nie usuwa plików istniejących wyłącznie w mirrorze i odmawia synchronizacji `AGENTS.md`.

Oba narzędzia odrzucają niebezpieczne ścieżki, nakładające się rooty, przejście przez reparse point oraz katalog artefaktów nakładający się na drzewa dokumentacji. Domyślne raporty trafiają odpowiednio do:

```text
Artifacts\Validation\DocumentationCheck\<run-id>\documentation-check-report.json
Artifacts\Validation\DocumentationSync\<run-id>\documentation-sync-report.json
```

Raporty są zapisane w JSON. Parametr `-Json` wypisuje na stdout zwartą kopertę z `reportPath`, `status` i `exitCode`. Najważniejsze kody wyjścia są wspólne: `0` oznacza sukces, `1` niezgodność, odrzucone dane wejściowe lub błąd operacji, `2` nieobsłużony błąd skryptu albo zapisu raportu, a `3` niedostępny wymagany root.
