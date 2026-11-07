# Instrukcja obsługi diagnostyki SingleCarry

Ten dokument opisuje opcjonalną diagnostykę edytora Unity dla problemu rozmieszczania SingleCarry F/E KI-003. Projekt Unity znajduje się w `D:\Programy\UnityProjects\RageQuitting`. Wszystkie poniższe polecenia PowerShell uruchamiaj z katalogu głównego tego projektu. Manual jest kopiowany bajt po bajcie do katalogu dokumentacji projektu i katalogu narzędzia diagnostycznego, dlatego przykłady poleceń są względne względem katalogu głównego projektu, a odnośniki do artefaktów używają ścieżek bezwzględnych.

## Obecny status

KI-003 pozostaje otwarty i odłożony; nie blokuje bieżącego etapu produkcji. Nie ma wiarygodnego deterministycznego odtworzenia błędu, ustalonej przyczyny źródłowej ani obejścia. Oryginalne RPC/protokół, tolerancje i produkcyjne API pamięci podręcznej pozostają bez zmian.

Dwie sparowane serie z 6 listopada obejmują łącznie 80 prób: 40 przy `PacketDelayMs=0` i 40 przy `PacketDelayMs=100`, jitter/loss 0; w tych 80 próbach nie było odmów. Nie wyciągaj z tego wniosków o częstości w grze i nie twierdź, że problem został naprawiony. Niepowodzenia z 5 listopada są poza tym zbiorem. Bieżąca walidacja odnotowała również osobną naturalną odmowę podczas Verify: wynik 8/9; zdalny właściciel poruszał się i zatwierdził przy opóźnieniu 100 ms, po czym nastąpiła odmowa `InvalidAimOrigin` / `ResidualExceeded`, residual 0.2938803287 m > 0.15 m, mimo dostępnej odebranej pozy i kompletnego, zgodnego kontekstu cache. Pełny identyfikator trace: `ea00d2dd71d14c5a8c01f45a7bbef4d3`; zarchiwizowany trace: `Tools/Diagnostics/SingleCarryPlacement/Evidence/ValidationFindings/verify-fixed-natural-100ms-refusal-trace.json`. Osobna odmowa dla żądania 9 przy 0 ms w scenariuszu all-directions nie miała pełnej osi czasu ani potwierdzonej gałęzi; wystąpiła przy innych błędach IO i nie dowodzi tej samej przyczyny. Zobacz `Artifacts/SingleCarryDiagnosticsIsolation_20261106T180748Z/final-validation.json` oraz `Tools/Diagnostics/SingleCarryPlacement/Evidence/ValidationFindings`.

## Bezpieczeństwo i zakres

- Instrumentacja jest opcjonalna, działa wyłącznie w edytorze i jest warunkowana przez `UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS`; domyślnie pozostaje wyłączona. Diagnostyczne DTO, liczniki i rejestrator znajdują się w `Assets/Diagnostics/SingleCarryPlacement`; cztery główne komponenty zawierają niewielkie opcjonalne haki partial, które są pomijane podczas kompilacji, gdy symbol nie jest zdefiniowany.
- Podczas zbierania danych nie zmieniaj produkcyjnych RPC, tolerancji, zachowania rozmieszczania ani `TryGetLatestAuthoritativeWorldPose`. Publiczny hak `OnAuthorityPushTransformState` obserwuje przekazanie stanu do NGO; nie dowodzi fizycznej transmisji UDP. Zgodny callback rodzaju 4/5 przed i po wywołaniu bazowym odpowiada jednemu fizycznemu odbiorowi, a nie dwóm. Ticki lokalne właściciela, serwera i źródła należą do odrębnych domen; initial sync jest osobnym przypadkiem. Brakujące lub niejednoznaczne dane historyczne nie dowodzą przyczyny. Surowe składowe przesłanych kątów Eulera nie są zapisywane; porównanie kwaternionów jest odtwarzane i niezmienne względem znaku.
- Dotychczasowa walidacja używała pętli zwrotnej edytora Unity w jednym procesie. Nie wykonano prób z osobnym procesem/ręcznym klientem, WAN/Relay ani ręcznego odbioru wizualnego. Ta instrumentacja działa wyłącznie w edytorze i nie jest kompilowana do buildów Player.
- `AssetDatabase.SaveAssets()` zapisuje ustawienia PlayerSettings oraz **wszystkie zmienione zasoby edytora**. Przed przechwytywaniem danych zapisz swoją pracę i podaj `-SaveDirtyAssets` jako potwierdzenie. Launcher odmawia działania przy niezapisanych scenach i nigdy nie zapisuje ich samodzielnie.
- Każdy przebieg zapisuje `recovery.json` przed zmianą ustawień lub środowiska. Awaria albo wymuszone zakończenie procesu może pominąć `finally`; przed kolejnym przebiegiem jawnie wykonaj Restore. Launcher używa wyłącznej blokady projektu, zachowuje niezwiązane definicje kompilacji, przywraca własną definicję i zmienną środowiskową trace, weryfikuje stan na dysku, w pamięci i załadowany typ oraz sprawdza długość ścieżek przed zmianą ustawień edytora. Nie edytuj ręcznie PlayerSettings w celu odzyskania stanu.

## Polecenia

Launcher: `Tools/Diagnostics/SingleCarryPlacement/Invoke-SingleCarryDiagnostics.ps1`; analizator: `Tools/Diagnostics/SingleCarryPlacement/Analyze-SingleCarryDiagnostics.ps1`.

```powershell
# Jedna stała para (jeden naturalny przypadek ruchu przy 0 ms i jeden przy 100 ms); domyślny tryb i liczba par
& .\Tools\Diagnostics\SingleCarryPlacement\Invoke-SingleCarryDiagnostics.ps1 -Mode Collect -Pairs 1 -SaveDirtyAssets -Json

# Stała weryfikacja 9 przypadków; wymaga sześciu zamrożonych osi czasu bez przepełnienia
& .\Tools\Diagnostics\SingleCarryPlacement\Invoke-SingleCarryDiagnostics.ps1 -Mode Verify -SaveDirtyAssets -Json

# Analiza rekordów osi czasu z ukończonego przebiegu. Podaj rzeczywisty katalog przebiegu i ścieżkę pliku wynikowego.
& .\Tools\Diagnostics\SingleCarryPlacement\Analyze-SingleCarryDiagnostics.ps1 -TraceDirectory '<run>\Pair-01\Traces' -OutputPath '<run>\analysis.json'

# Przywrócenie stanu na podstawie manifestu odzyskiwania, gdy przebieg został przerwany lub przywrócenie jest niepewne
& .\Tools\Diagnostics\SingleCarryPlacement\Invoke-SingleCarryDiagnostics.ps1 -Mode Restore -RecoveryManifest '<run>\recovery.json' -SaveDirtyAssets -Json
```

`Collect` domyślnie wykonuje jedną parę i przyjmuje od 1 do 20 par; wykonuje zadaną liczbę prób i nigdy ich nie ponawia. Naturalne niepowodzenia asercji funkcjonalnych są zachowywane i powodują niezerowy kod zakończenia po wykonaniu wszystkich zleconych par; nieprawidłowe pary z powodu infrastruktury przerywają przebieg. `Verify` wymaga dziewięciu zaliczonych testów funkcjonalnych oraz sześciu zamrożonych osi czasu bez przepełnienia. Opcjonalne parametry to `-OutputRoot`, `-TimeoutSeconds` (30–900; domyślnie 180) i `-TestRunnerPath`. Ścieżki względne katalogu wynikowego i runnera są rozwiązywane względem katalogu głównego projektu. Domyślny katalog wynikowy to `Artifacts\SingleCarryDiagnostics`.

Punkt podmiany test runnera służy do kontrolowanej walidacji harnessu; nie zastępuje dowodów dowolnymi testami. Sztuczny runner użyty do weryfikacji sprzątania znajduje się w lokalnym `Artifacts/SingleCarryDiagnosticsIsolation_20261106T180748Z`, poza wersjonowanym archiwum dowodów; kończy się kodem 73 bez kontaktu z Unity. Nie traktuj tego oczekiwanego błędu wstrzykniętego jako dowodu dotyczącego produktu.

## Interpretacja dowodów

- Indeksy prób rozdzielają scenariusz i status testu funkcjonalnego od wyniku odpowiedzi rozmieszczania; samo zaliczenie testu harnessu nie oznacza, że rozmieszczenie zostało zaakceptowane.
- Analizator przyjmuje identyfikator przebiegu oraz rekordy osi czasu. Historyczne artefakty odmów w starszym formacie legacy nie są dodatkowymi próbami.
- Metadane opóźnienia pakietu są odrębne od zmierzonego czasu odpowiedzi; skonfigurowane 100 ms nie oznacza zmierzonego opóźnienia jednokierunkowego.
- Archiwum dowodów zawiera 52 surowe wpisy z manifestem i sumami SHA-256, dwie starsze odmowy, dwa zaakceptowane przykłady maksymalnego residualu z historycznych 80 prób, bieżące porównania odmowy i akceptacji, materiały harnessu oraz dokładne migawki bieżących źródeł. Zweryfikuj je względem `Tools/Diagnostics/SingleCarryPlacement/Evidence/raw-sha256-manifest.json`; `ValidationFindings/supplemental-validation-index.json` i jego manifest dowodów opisują nowsze ustalenia walidacji. Kontekst package-lock z chwili archiwizacji nie jest pomiarem dla poszczególnych prób. Pełne surowe przechwycenia 80 prób pozostają w lokalnych artefaktach.
- Historyczny pełny Gameplay w `Artifacts/SingleCarryStateDivergence_20261106T150940Z/final-validation.json` nie przeszedł (EditMode 230/230, PlayMode 260/262; dwa wskazane niepowodzenia testów akceptacyjnych). Izolowane zaliczenia go nie zastąpiły. Autorytatywnym raportem tego zadania jest `Artifacts/SingleCarryDiagnosticsIsolation_20261106T180748Z/final-validation.json`; aktualny wynik sprawdź w polach `finalGameplay`. Zachowaj wcześniejsze niepowodzenia i statusy jako historię.

## Powiązane dokumenty

- [Odłożony problem KI-003](D:/Programy/UnityProjects/RageQuitting/Documentation/Known-Issues.md)
- [Implementacja SingleCarry i historia walidacji](D:/Programy/UnityProjects/RageQuitting/Documentation/SingleCarry-WASD-Synchronization-Followup-Plan.md)
- Bieżący raport: `D:\Programy\UnityProjects\RageQuitting\Artifacts\SingleCarryDiagnosticsIsolation_20261106T180748Z\final-validation.json`
- Katalog dowodów: `D:\Programy\UnityProjects\RageQuitting\Tools\Diagnostics\SingleCarryPlacement\Evidence`
