# Zasady używania lokalnego preflightu

## Cel i wymagania

Lokalny Agent Validation Harness sprawdza zmiany w projekcie Unity przed przekazaniem ich do review. Przed jakimkolwiek wywołaniem Unity wykonuje bramkę analizatorów. Następnie łączy kontrolę kompilacji i Console, testy EditMode, szybkie walidatory oraz — w tierze Gameplay — testy PlayMode, scenariusz `TutorialBoot` i host-only `network-smoke`.

Polecenia należy uruchamiać z katalogu projektu:

```text
D:\Programy\UnityProjects\RageQuitting
```

Unity Editor powinien być osiągalny przez Pipeline, pozostawać w stabilnym EditMode i nie kompilować ani nie aktualizować assetów. Walidacja nie może zapisywać scen ani assetów. Przed uruchomieniem należy świadomie ocenić istniejący dirty worktree i zachować niezwiązane zmiany innych autorów.


## Połączenie Unity CLI z działającym Editorem

Narzędzia harnessu korzystają ze wspólnego modułu `Tools\AgentHarness\UnityConnection.psm1`. Domyślna ścieżka do `unity.exe` jest wyznaczana z rzeczywistego Windows Known Folder `LocalApplicationData`; opcjonalne `RQ_UNITY_CLI_PATH` musi być ścieżką absolutną zakończoną nazwą `unity.exe`. Przed uruchomieniem CLI moduł sprawdza znaczniki projektu, deskryptor Pipeline, jego PID i linię poleceń procesu, aby potwierdzić dokładny katalog projektu. Nieczytelny lub niezgodny deskryptor/PID blokuje wywołanie. Przy brakującym deskryptorze widoczny Editor z pasującą albo nieustaloną tożsamością również blokuje CLI; dopiero czytelna lista procesów bez pasującego Edytora pozwala przejść do odczytowego wywołania discovery przez CLI. Brak dowodu, że Editor nie działa, nie jest traktowany jako dowód jego zatrzymania. Moduł normalizuje `UNITY_PROJECT_PATH` i `LOCALAPPDATA` na czas wywołania, a następnie przywraca poprzednie wartości.

`Invoke-HarnessCommand` oraz pięć skryptów-konsumentów (`Invoke-UnityPreflight.ps1`, `Invoke-UnityCompileCheck.ps1`, `Invoke-UnityQuickValidators.ps1`, `Invoke-UnityConsoleCheck.ps1` i `Invoke-UnityEditModeTests.ps1`) współdzielą te zabezpieczenia. Polecenia Unity CLI i harnessu uruchamiaj z katalogu projektu, w zwykłym PowerShell:

```powershell
Set-Location 'D:\Programy\UnityProjects\RageQuitting'
.\Tools\AgentHarness\Invoke-UnityConnectionDoctor.ps1 -Json
.\Tools\AgentHarness\Invoke-UnityCommand.ps1 -UnityArguments @('command', 'rq_validation_context')
.\Tools\AgentHarness\Invoke-UnityPreflight.ps1 -Tier Fast -Json
```

W Codex pierwsze wykonawcze wywołanie Unity CLI, harnessu lub preflightu uruchom przez `exec_command` poza sandboxem, ponieważ sandbox może ukrywać PID Edytora albo ograniczać odczyt deskryptora. To wymaganie dotyczy kontekstu wykonania polecenia; nie oznacza konieczności uruchomienia PowerShell jako Administrator. Nie łącz się bezpośrednio z portem Pipeline przez niezabezpieczone HTTP, nie zakładaj stałego portu i nie kopiuj deskryptora ani tokenu uwierzytelniającego do logów.

`Invoke-UnityConnectionDoctor.ps1 -Json` jest diagnostyczny i tylko do odczytu. Zwraca status oraz `nextAction`; kod `0` oznacza wyłącznie osiągalny, gotowy Editor dla dokładnie tego projektu, a `3` oznacza stan zablokowany lub nieustalony. Nie uruchamia naprawy. Tekst o błędach kompilacji lub Safe Mode znaleziony w historycznych liniach logu oznaczany jest jako niezweryfikowany: sam wpis nie dowodzi, że opisuje bieżącą sesję Edytora.

`Invoke-UnityCommand.ps1` przyjmuje argumenty jako tablicę `-UnityArguments`, dopina katalog projektu i JSON, a następnie zachowuje kod wyjścia Unity CLI. Nie ponawia automatycznie poleceń modyfikujących stan. Przykłady z innymi poleceniami stosuj tylko wtedy, gdy znasz ich skutek; przed uruchomieniem wrapper sprawdza, czy wskazany cel jest dokładnie chronionym projektem.

Jeśli kanał CLI jest jeszcze osiągalny, naprawę można uruchomić poleceniem CLI `rq_repair_unity_connection`. Gdy CLI jest nieosiągalne, użyj jawnie menu Unity `Tools > Agent Harness > Repair Unity CLI Connection`. Naprawa publikuje ponownie wyłącznie deskryptor już działającego serwera Pipeline. Odmawia działania, gdy serwer jest zatrzymany, Editor jest w Play Mode lub zmienia tryb, trwa kompilacja/import albo działają testy. Nie restartuje Edytora, nie zmienia AutoTick, nie zapisuje scen ani assetów, nie aktualizuje pakietów i nie wstrzykuje tymczasowego kodu źródłowego. Dla zatrzymanego serwera użyj świadomie istniejącej komendy Unity `Window > Pipeline > Start Server`. Jeśli Editor jest w Safe Mode, najpierw napraw rzeczywisty błąd kompilacji, a następnie uruchom Editor ponownie.

Weryfikacja połączenia wykazała poprawne działanie doctora i `rq_validation_context`; testowano też ponowną publikację deskryptora bez zatrzymywania serwera. Końcowy Fast dla partii kodu, ścisły i niefiltrowany, przeszedł: kod `0`, 11/11 kroków bez statusów innych niż `passed`, EditMode 219/219; raport: `Artifacts\Validation\20261028T165334Z-049a9981\summary.json`. Główny agent uruchomi osobny końcowy Fast po zmianach dokumentacji. Skupiony zestaw regresji PowerShell 5.1 uruchomiony przez głównego agenta zakończył się kodem `0`: `Tools\AgentHarness\Tests\UnityConnection.Tests.ps1`; podsumowanie sond: `Artifacts\UnityConnection_20261028\root-probes-summary.json`. Wcześniejszy Fast `20261028T165059Z-dcdbd574` zakończył się `not_available` (kod `3`): `console-baseline` nie mógł połączyć się z Pipeline na porcie 7801 mimo poprawnej kompilacji, a EditMode i quick validators nie miały wymaganych warunków wstępnych. Pomiędzy tym przebiegiem a końcowym Fast nie wykonano naprawy połączenia, więc przyczyna wcześniejszego braku połączenia pozostaje nieznana; końcowy sukces nie dowodzi, że ją ustalono. Nie testowano odzyskiwania po Safe Mode ani po zatrzymaniu serwera. Ta zmiana nie wprowadza zmian gameplayu i nie obejmowała PlayMode.

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

## Bramka analizatorów

Kontrola analizatorów jest pierwszą bramką preflightu i kończy się przed jakimkolwiek wywołaniem Unity. Błąd lub niepoprawny raport analizatorów blokuje dalsze kontrole, daje status końcowy `failed` i kod `1`, także z `AllowLowerTier`. Gdy ścisła kontrola jest niedostępna, dalsze kroki mają status `not_available`, a cały przebieg kończy się `not_available` z kodem `3`. `AllowLowerTier` może kontynuować wyłącznie diagnostyczny przebieg po niedostępności: zapisuje pominięte kroki jako `skipped` i ostrzeżenia, lecz nie daje podstawy do akceptacji. Kontrole `git-after`, ochrony przed mutacją oraz zapis JSON nadal są wykonywane.

Do wczesnej iteracji można uruchomić `Invoke-AnalyzerCheck.ps1 -Json` po zaimportowaniu zmienionego kodu przez Editor. To osobny szybki check; nie aktualizuje baseline analizatorów.
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

## Podsumowanie Markdown

Każdy preflight zapisuje autorytatywny `summary.json` oraz wygenerowany z niego `summary.md`; wynik JSON zawiera `markdownSummaryPath`. Renderer korzysta z raportów podrzędnych powiązanych ze krokami i zachowuje zarejestrowany status oraz kod, tier, czas UTC, filtry i scenariusz, wszystkie kroki niebędące `passed`, liczniki testów EditMode i PlayMode bez podwójnego liczenia scenariusza i network-smoke, ograniczoną listę błędów testów, odrębne bieżące i nowe diagnostyki analizatorów, kompilację i Console, ograniczenia zależne od hosta, ostrzeżenia oraz braki dowodów. Brakujące albo niepoprawne dane są oznaczane jako niedostępne, nie jako zero lub sukces. W tierze Fast brak kroku PlayMode jest jawnie opisany jako niewymagany. Raport nie oznacza ręcznej akceptacji.

Raport można wyeksportować niezależnie, wskazując dokładny plik źródłowy (nie wybieraj automatycznie najnowszego):

```powershell
.\Tools\AgentHarness\Export-UnityValidationSummary.ps1 -SummaryPath 'Artifacts\Validation\<run-id>\summary.json' -Json
.\Tools\AgentHarness\Export-UnityValidationSummary.ps1 -SummaryPath 'Artifacts\Validation\<run-id>\summary.json' -Publish -Json
```

`-Publish` publikuje wyłącznie `Generated\Latest-Validation.md` w obu korzeniach dokumentacji, przez `Invoke-DocumentationSync.ps1 -Document`, i potwierdza zgodność bajtów. Publikacja chroni treść redakcyjną bez znaczników wygenerowanego obszaru, odrzuca nakładające się ścieżki lub reparse points i nie obejmuje `AGENTS.md`. Zwykły preflight sam nie publikuje raportu. Status i kod eksportera opisują powodzenie generowania/publikacji; `validationStatus` i `validationExitCode` zachowują wynik walidacji źródłowej, więc błąd zapisu nie zmienia nieudanego przebiegu w sukces.

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

Do sprawdzenia treści i par lustrzanych kilku dokumentów uruchom:

```powershell
.\Tools\AgentHarness\Invoke-DocumentationCheck.ps1 -Document Loose-Soil-V1-Implementation.md,Bridge-Construction.md,Deferred-Test-Isolation-Plan.md,Agent-Validation-Harness.md -Json
```

Kontrola wybranych plików zapisuje raport JSON i porównuje bajty między katalogiem `Documentation` projektu a katalogiem dokumentacji roboczej; nie modyfikuje plików. `-All` sprawdza wszystkie dokumenty Markdown. Po edycji pojedynczego dokumentu parę należy zsynchronizować przed checkiem; `Invoke-DocumentationSync.ps1` zapisuje wyłącznie w kierunku ze źródła autorytatywnego w projekcie do dokumentacji roboczej, więc nie używaj go do niezamienionych lokalnych kopii.

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

Check porównuje dokładne bajty wybranych plików i zgłasza brakujące lub różne kopie lustrzane jako błąd. Przy pojedynczych parametrach `-Document` skan obejmuje tylko wskazane pliki i nie enumeruje pozostałych dokumentów ani `targetOnly` (`targetOnlyScope=not_scanned_selected_documents`). Domyślny tryb oraz `-All` skanują całe drzewa Markdown; pliki istniejące wyłącznie w mirrorze są wtedy raportowane informacyjnie jako `targetOnly` (`targetOnlyScope=all`) i nie powodują niepowodzenia.

Sync tworzy lub nadpisuje wybrane pliki w mirrorze, atomowo tam, gdzie jest to praktyczne, oraz tworzy brakujące katalogi docelowe. Nigdy nie usuwa plików istniejących wyłącznie w mirrorze i odmawia synchronizacji `AGENTS.md`.

Oba narzędzia odrzucają niebezpieczne ścieżki, nakładające się rooty, przejście przez reparse point oraz katalog artefaktów nakładający się na drzewa dokumentacji. Domyślne raporty trafiają odpowiednio do:

```text
Artifacts\Validation\DocumentationCheck\<run-id>\documentation-check-report.json
Artifacts\Validation\DocumentationSync\<run-id>\documentation-sync-report.json
```

Raporty są zapisane w JSON. Parametr `-Json` wypisuje na stdout zwartą kopertę z `reportPath`, `status` i `exitCode`. Najważniejsze kody wyjścia są wspólne: `0` oznacza sukces, `1` niezgodność, odrzucone dane wejściowe lub błąd operacji, `2` nieobsłużony błąd skryptu albo zapisu raportu, a `3` niedostępny wymagany root.

## Wspólny zakres testu PlayMode

`Assets/Tests/PlayMode/PlayModeTestScope.cs` to opt-in helper dla etapowej migracji fixture, nie automatyczne globalne sandboxowanie. Zarejestruj każdy utworzony korzeń, `NetworkManager`, tymczasowy asset i dodatkową scenę zaraz po utworzeniu; rejestruj `NetworkManager` przed startem NGO. Nie rejestruj runnera, scen bazowych ani pożyczonych obiektów. Setup inputu zachowuje odpowiedni input-settings scope i asset-hash guard; wytworzone mapy są zwalniane w lifecycle obiektu. Po `yield return scope.Cleanup()` zakończ cleanup, przywróć lokalne pola fixture (np. wskaźnik do bieżącego scope), a następnie wywołaj `scope.AssertCleanupSucceeded()`. Przykład dla fixture NGO:


```csharp
private PlayModeTestScope scope;

[UnitySetUp]
public IEnumerator SetUp()
{
    scope = new PlayModeTestScope(nameof(MyFixture));
    CaptureInputState();
    scope.RestoreWith(RestoreInputState);
    NetworkManager manager = scope.OwnManager(CreateConfiguredNetworkManager());
    Assert.That(manager.StartHost(), Is.True);
    yield return null;
}

[UnityTearDown]
public IEnumerator TearDown()
{
    if (scope == null) yield break;
    PlayModeTestScope current = scope;
    yield return current.Cleanup();
    scope = null;
    ClearFixtureReferences();
    current.AssertCleanupSucceeded();
}
```

`CaptureInputState`, `RestoreInputState`, `ClearFixtureReferences` i `CreateConfiguredNetworkManager` są placeholderami dla metod danego fixture; menedżer musi być skonfigurowany z transportem przed `StartHost`. Rejestruj callback odtworzenia po przechwyceniu stanu i przed mutacją inputu/sieci. Zakres fixture pozostaje opt-in: scope próbuje wszystkie odtworzenia i zgłasza zebrane błędy; nie resetuje arbitralnie globalnych statyków ani nie tworzy nowego procesu Unity. Nie wyprowadzaj z tego przykładu obowiązku użycia `InputTestFixture` przez każdy test. Nadal migruj fixture etapami i waliduj rzeczywiste sekwencje regresyjne.