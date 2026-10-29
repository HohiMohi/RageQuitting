# Odłożony plan: izolacja testów i sprzątanie stanu

Data aktualizacji: 2026-10-29  
Status: **Etap 1 wdrożony i zwalidowany; dalsza migracja fixture etapami**  
Zakres: punkt 2 z 3 usprawnień workflow testów.

## Warunek wznowienia — spełniony 2026-10-29

Etap 5 został zaakceptowany wcześniej. Użytkownik 2026-10-29 zaakceptował Etap 6 i zakończenie pierwotnego planu, zgłaszając, że wszystko wydaje się działać poprawnie. Szczegółów wykonanych pozycji checklisty nie podano. Zgodnie z tą akceptacją warunek wznowienia spełniono; pierwszy etap audytu i implementacji opisano poniżej. To użytkownikowska akceptacja manualna; nie należy jej mylić z automatyczną walidacją ani rozszerzać zapisanych raportów na przypadki, których testy nie obejmowały. Główny plan i jego zachowane ograniczenia: [Loose-Soil-V1-Implementation.md](Loose-Soil-V1-Implementation.md).

Pierwsza partia audytu, wspólnego scope i migracji pięciu fixture wdrożona i zwalidowana; pozostałe fixture migrujemy osobnymi, przeglądalnymi etapami. Szczegóły pierwszej partii poniżej, a wcześniejsze założenia zachowano jako historyczny plan; przed kolejnymi zmianami sprawdź aktualny kod i odtwórz wąski przypadek.

Usprawnienie 1 (niezawodna ścieżka Unity) i usprawnienie 3 (wczesne analyzery / raportowanie wygenerowane) są ukończone. Ta notatka zapisuje propozycję dalszej pracy; nie zatwierdza finalnej architektury ani nie obiecuje, że wszystkie historyczne flakiness mają wspólną przyczynę.

## Proponowany kierunek

- Domyślnie PlayMode powinien tworzyć własną, świeżą scenę i obiekty; wspólne fixture powinno zapewniać setup, cleanup i odtworzenie stanu.
- Testy wymagające prawdziwego poziomu powinny używać odizolowanej kopii sceny albo sceny preview. Czysta matematyka EditMode nie wymaga wymuszania sceny.
- Sama pusta scena nie resetuje statyków, singletonów, Input System, `DontDestroyOnLoad` ani stanu sieci.
- Osobny proces Unity daje najsilniejszą izolację procesu, lecz kosztuje czas. Zachować go jako opcję dla problematycznych grup lub diagnostyki, nie przyjmować jako uniwersalnego domyślnego rozwiązania.
- Przed wdrożeniem doprecyzować projekt techniczny i granice wspólnego helpera. Nie robić masowego przepisywania fixture.

## Stan współdzielony do audytu

Sprawdzić singletony, rejestry, zdarzenia i pola statyczne; urządzenia, akcje i ustawienia Input System (właściwie użyć `InputTestFixture`); zmieniane ustawienia `Time` i fizyki; załadowane sceny oraz należące do testu obiekty `DontDestroyOnLoad`.

Przywracać stan frameworka i użytkownika sprzed testu. Niszczyć wyłącznie korzenie utworzone przez test; nie usuwać globalnie wszystkich obiektów ani runnera testowego. Właściciel każdego obiektu lub subskrypcji powinien być czytelny.

Dla NGO śledzić wszystkie repliki peerów. W teardown wykonać shutdown i z ograniczonym czasem oczekiwać, aż oba warunki będą spełnione: `IsListening == false` oraz `ShutdownInProgress == false`; dopiero potem niszczyć transporty i menedżery. Przywrócić własność singletonów. Cleanup i odtworzenie stanu próbować doprowadzić do końca nawet po błędzie setupu, asercji, timeoutcie lub teardown; zebrać błędy zamiast przerywać po pierwszym.

Asercje po teście mają wskazać test, który pozostawił obiekt lub aktywny transport. Oddzielić izolację wielu peerów wewnątrz jednego testu od wycieków pomiędzy testami. Wymieszanie replik `NetworkManager.Singleton` w fixture Stage5 nie dowodzi samo w sobie wycieku między testami.

## Etap 1 wdrożenia — scope i pierwsze migracje (2026-10-29)

Wdrożono pierwszy wspólny mechanizm i ograniczoną migrację pięciu fixture; nie zmigrowano wszystkich testów ani nie zakończono planu izolacji. Główny agent niezależnie przejrzał zmiany. Jedyna zmiana produkcyjna to `PlayerInteractionNew.OnDestroy`: najpierw wypisuje własny handler `EquippableItem.OnAnyItemEquipped`. Nie zmieniano gameplayu, scen, prefabów ani tuningu. Test regresyjny rzeczywistego cyklu `create/Start/Destroy` pozostawiał stary target po zniszczeniu (RED: 1), a po korekcie przechodził trzy cykle (GREEN: 3) i zachowywał niezależnych subskrybentów.

Nowy `Assets/Tests/PlayMode/PlayModeTestScope.cs` tworzy pustą, aktywną scenę runtime i własność rejestruje jawnie: `Own` dla utworzonych korzeni, `OwnManager` przed uruchomieniem sieci, `OwnTransientAsset` dla tymczasowych assetów i `OwnScene` dla dodatkowych utworzonych scen. Zachowuje wcześniejsze i nieposiadane sceny, obiekty, test runner oraz pożyczone korzenie. NGO cleanup zbiera repliki także przez wewnętrzne `NetworkObject.NetworkManagerOwner` i `ReferenceEquals` (w tym wrapper zniszczonego menedżera), z listami SpawnManager należących do testu menedżerów; sama `NetworkManager.Singleton` nie jest dowodem własności. Zamykanie czeka z limitem czasu na oba warunki `!IsListening` oraz `!ShutdownInProgress`; potem niszczy własne obiekty i sceny przed callbackami odtwarzania stanu. Próbuje wszystkich callbacków FIFO, zbiera błędy do `AssertCleanupSucceeded()` i jest idempotentne. Odtwarza dokładnie wcześniejszy singleton, również null lub nieaktywną instancję, `Time.timeScale`, `fixedDeltaTime`, `maximumDeltaTime`, `Physics.gravity`, `autoSyncTransforms` i `simulationMode`. Fixture z inputem zachowują istniejący `CrowbarInputSettingsScope` i kontrolę hasha assetu; generowane mapy gracza są zwalniane przez niszczenie obiektu przed usuwaniem należących do testu klawiatur/myszy. Nie ma globalnego resetu wszystkich statyków ani nowego procesu Unity.

Zmigrowane fixture i liczba istniejących przypadków: `SubstanceFractionalUnitsNetworkPlayModeTests` (1), `LooseSoilStage4NetworkPlayModeTests` (1), `LooseSoilStage5NetworkPlayModeTests` (2), `Stage3Movement` (2), `LooseSoilStage5Return` (16), razem 22 istniejące przypadki. Dodano 7 testów scope i 1 event lifecycle regression. Czerwone→zielone reprodukcje potwierdziły cztery usterki: przywrócenie nieaktywnego singletonu, orphan `DontDestroyOnLoad`, wrapper zniszczonego menedżera oraz pozostawionego subskrybenta statycznego zdarzenia. Pozostałe nowe testy sprawdzają kontynuację przywracania po wstrzykniętym błędzie callbacku (także dla inputu), dodatkową scenę, idempotencję oraz sekwencje A→B, B→A i A→A. Te sekwencje nie są permutacjami całego zestawu.

Ścisły, nieprzefiltrowany Gameplay po tej partii: [summary.json](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20261029T153314Z-ef5195a2/summary.json) — passed, exit 0, 14/14 kroków; EditMode 219/219, PlayMode 247/247, brak kroków niezaliczonych, ostrzeżeń i pominięć. Końcowe hashe źródeł potwierdzają brak niezamierzonych zmian; `InputSystem.inputsettings.asset` i `EditorBuildSettings.asset` są identyczne z baseline. Pierwsze próby Fast `20261029T144849Z-870de826` i `20261029T154302Z-afced85e` nie przeszły bramki analyzera i zablokowały kolejne kontrole; raporty builda pokazały zero błędów kompilatora i nie ustalono przyczyny. Niezależny analyzer recheck bez zmian źródeł/konfiguracji przeszedł (913 istniejących znalezisk wobec baseline 925; baseline 316 wpisów). Końcowy ścisły Fast uruchomiony po dokumentacji przez głównego agenta przeszedł: [summary.json](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20261029T154407Z-3781b972/summary.json), exit 0. Nie ma nowych narzędzi ani zmian baseline.

Artefakty reprodukcji i migracji (wyniki przed/po zachowano): `Artifacts/TestIsolation_20261029/red-original-fixture`, `red-current-ddol`, `player-interaction-event-red`, `player-interaction-event-green`, `destroyed-owner-red-compiled`, `destroyed-owner-green`, `scope-extra-scene`, `stage3-final-input-scope`, `stage5-return-input-scope`, `stage4-scope`, `stage5-scope`, `final-reviewed-suite` i `baseline-analyzer-recheck`. Testy z filtrami dającymi zero przypadków traktowano jako diagnostykę, nie dowód regresji.

Pozostałe prace: ocenić i migrować dalsze legacy fixture etapami, w tym te oparte o `InputTestFixture`; Stage6 Single-reload nadal ma własny lifecycle fixture. Scope współdzieli świat fizyki, odtwarza zapisane pola skalarne i jawnie zarejestrowane callbacki, a nie cały globalny stan silnika. Nie dowodzi obsługi anulowanego procesu w każdej fazie setupu/teardown ani niezależności pełnego zestawu od wszystkich kolejności. Historyczne incydenty poniżej zachowują wartość jako zapis dawnych symptomów; naprawa handlera potwierdza obecny błąd cyklu życia tego zdarzenia, lecz nie dowodzi, że to była przyczyna wszystkich wcześniejszych flakiness.

## Uzasadnienie: lokalne dowody historyczne

Poniższe logi uzasadniają wspólną infrastrukturę sprzątania. Nie zakładać, że problem nadal występuje ani że każdy przypadek flakiness ma ten sam root cause.

1. **25.10 — Input System.** `TerrainRoadV3RuntimeSwitcherTests.F6AndF7ChangeOnlyRuntimeNormalReferences` zgłosił `ArgumentNullException` dla `statePtr`. Pełna kolejność: 206/207; izolowana grupa Terrain: 6/6; po odizolowaniu inputu `PlayerNew` przed aktywacją: pełna kolejność 207/207. To mocny dowód na współdzielone zanieczyszczenie inputu, ale dokładny łańcuch poprzedzających testów nie został świeżo odtworzony. Raporty: [raport 20261025T211512Z-bb0b6c31](D:\Programy\UnityProjects\RageQuitting\Artifacts\Validation\20261025T211512Z-bb0b6c31\summary.json), [raport 20261025T211837Z-785a9772](D:\Programy\UnityProjects\RageQuitting\Artifacts\Validation\UnityPlayMode\20261025T211837Z-785a9772\unity-playmode-tests-report.json), [pełny raport 20261025T212124Z-cbcacb34](D:\Programy\UnityProjects\RageQuitting\Artifacts\Validation\20261025T212124Z-cbcacb34\summary.json); kontekst w głównym planie, okolice linii 78.
2. **26.10 — syntetyczny gracz sieciowy.** Pozostały gracz Stage4 o hashu `0x53544734` (`1398032180`) pojawił się w późniejszych testach disconnect / placement / saw oraz powodował duplikat rejestracji `ScenePlacedObjects`. Pełny przebieg `20261026T184025Z-989173c9` osiągnął 180 s. To bezpośredni odcisk wycieku obiektu między testami; nie przypisywać mu wyłącznej przyczyny timeoutu (w przebiegu były także błędy analyzerów). Źródła: [konsola z fingerprintem](D:\Programy\UnityProjects\RageQuitting\Artifacts\LooseSoilStage4_20261026\failed-full-console.json), [raport 20261026T184025Z-989173c9](D:\Programy\UnityProjects\RageQuitting\Artifacts\Validation\20261026T184025Z-989173c9\summary.json); główny plan, okolice linii 155. Lokalny cleanup fixture został już poprawiony.
3. **25.10 — teardown NGO.** `SubstanceFractionalUnitsNetwork` zakończył teardown błędem `ObjectDisposedException` na `NativeList` / `UnityTransport.Shutdown`. Potwierdza wadę cleanupu, ale samo nie dowodzi wpływu na następny przypadek. Raport: [20261025T210420Z-02bdf75f](D:\Programy\UnityProjects\RageQuitting\Artifacts\Validation\20261025T210420Z-02bdf75f\summary.json).

Kontrprzykłady i ograniczenia interpretacji:

- **27.10:** timeout `SingleCarryPlacementStage5` (217/218) wynikał z żądania przed oknem 50 ms; po poprawce harmonogramu fixture pełny przebieg dał 218/218 (`20261027T084206Z-1ba3c170` → `20261027T084825Z-dd420c5e`). Nie wykazano wycieku.
- **26.10:** przyczyna sporadycznej porażki `BridgeDeckPanelStage1` pozostaje nieznana.
- Odtworzony w tej partii test lifecycle potwierdził, że stary `PlayerInteractionNew` target mógł pozostać subskrybentem `EquippableItem.OnAnyItemEquipped`; naprawiono usunięcie własnego handlera przy `OnDestroy`. To nie dowodzi związku z każdą historyczną flakiness.

## Kandydaci i kolejność prac

Migracja pierwszej partii objęła `SubstanceFractionalUnitsNetworkPlayModeTests.cs`, `LooseSoilStage4NetworkPlayModeTests.cs`, `LooseSoilStage5NetworkPlayModeTests.cs` oraz fixture Stage3 i Stage5 wymienione wyżej. Następnie oceń pozostałe legacy fixture, w tym pozostałe testy bazujące na `InputTestFixture`, według aktualnych symptomów i wycieków; nie powtarzaj zakończonej migracji bez nowego dowodu. `Stage6 Single-reload` nadal używa własnego lifecycle fixture.

1. Najpierw audyt i odtworzenie możliwie wąskiego przypadku z zachowaniem kolejności.
2. Dodać wspólny mechanizm do jednego fixture i test regresyjny, który wiarygodnie obala wadliwy cleanup przed poprawką oraz przechodzi po niej.
3. Niezależnie przejrzeć zmianę, potem migrować kolejne fixture etapami.
4. Weryfikować test osobno, A→B, B→A, powtórzone A, kilka kontrolowanych kolejności / seedów jeśli runner je wspiera oraz pełny zestaw. Pojedynczy zielony pełny zestaw nie dowodzi niezależności.
5. Sprawdzić cleanup również po nieudanym teście. Naprawiać przyczynę; nie wyciszać logów, wyłączać testów ani arbitralnie wydłużać timeoutów.

## Walidacja przy przyszłym wdrożeniu

Jeśli zmiana obejmie runtime lifecycle, sieć lub integrację testów runtime, końcowa ścisła walidacja to `Gameplay`; `Fast` tylko dla zakresu rzeczywiście ograniczonego do narzędzi / Editor. Przyszły wykonawca ma wybrać tier zgodnie z rzeczywistym zakresem, uruchomić końcowy nieprzefiltrowany strict preflight i zaraportować status, exit code, `summary.json`, każdy niezaliczony krok i ograniczenia. Nie akceptować wyniku `AllowLowerTier`. Używać bieżącego CLI/wrappera Unity; pierwszy rzeczywisty wywołujący CLI/harness/preflight wymaga `require_escalated` i `login:false`; katalog roboczy: `D:\Programy\UnityProjects\RageQuitting`, dokumentacja robocza: `C:\Users\dunia\Documents\RageQuitting`. Istotne reprodukcje zachować przez `KeepArtifacts`; retencja domyślna wynosi 14 dni.

Kryteria ukończenia: brak należących do testu obiektów i aktywnych transportów NGO po teście; przywrócony stan globalny, inputu, `Time` i fizyki; teardown ograniczony czasowo z widocznymi błędami; wyniki niezależne od kontrolowanej kolejności; pełna walidacja właściwego zakresu. Zapis kluczowych faktów i identyfikatorów raportów w tej notatce pozwala odtworzyć kontekst po retencji artefaktów.
