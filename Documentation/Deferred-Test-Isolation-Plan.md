# Odłożony plan: izolacja testów i sprzątanie stanu

Data: 2026-10-28  
Status: **plan odłożony, niewdrożony**  
Zakres: punkt 2 z 3 usprawnień workflow testów.

## Warunek wznowienia

Nie rozpoczynać audytu ani implementacji tego planu, dopóki pierwotny plan mechaniki **Sypka ziemia V1** nie zostanie ukończony, włącznie z Etapem 6 i ręczną oceną użytkownika. Stan na 2026-10-28: Etap 6 nie był jeszcze rozpoczęty. Użytkownik zaakceptował najnowszą funkcjonalność Etapu 5; nie przenosić do bieżącego stanu starszych wpisów o oczekującej ręcznej akceptacji. Główny plan: [Loose-Soil-V1-Implementation.md](Loose-Soil-V1-Implementation.md).

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

## Uzasadnienie: lokalne dowody historyczne

Poniższe logi uzasadniają wspólną infrastrukturę sprzątania. Nie zakładać, że problem nadal występuje ani że każdy przypadek flakiness ma ten sam root cause.

1. **25.10 — Input System.** `TerrainRoadV3RuntimeSwitcherTests.F6AndF7ChangeOnlyRuntimeNormalReferences` zgłosił `ArgumentNullException` dla `statePtr`. Pełna kolejność: 206/207; izolowana grupa Terrain: 6/6; po odizolowaniu inputu `PlayerNew` przed aktywacją: pełna kolejność 207/207. To mocny dowód na współdzielone zanieczyszczenie inputu, ale dokładny łańcuch poprzedzających testów nie został świeżo odtworzony. Raporty: [raport 20261025T211512Z-bb0b6c31](D:\Programy\UnityProjects\RageQuitting\Artifacts\Validation\20261025T211512Z-bb0b6c31\summary.json), [raport 20261025T211837Z-785a9772](D:\Programy\UnityProjects\RageQuitting\Artifacts\Validation\UnityPlayMode\20261025T211837Z-785a9772\unity-playmode-tests-report.json), [pełny raport 20261025T212124Z-cbcacb34](D:\Programy\UnityProjects\RageQuitting\Artifacts\Validation\20261025T212124Z-cbcacb34\summary.json); kontekst w głównym planie, okolice linii 78.
2. **26.10 — syntetyczny gracz sieciowy.** Pozostały gracz Stage4 o hashu `0x53544734` (`1398032180`) pojawił się w późniejszych testach disconnect / placement / saw oraz powodował duplikat rejestracji `ScenePlacedObjects`. Pełny przebieg `20261026T184025Z-989173c9` osiągnął 180 s. To bezpośredni odcisk wycieku obiektu między testami; nie przypisywać mu wyłącznej przyczyny timeoutu (w przebiegu były także błędy analyzerów). Źródła: [konsola z fingerprintem](D:\Programy\UnityProjects\RageQuitting\Artifacts\LooseSoilStage4_20261026\failed-full-console.json), [raport 20261026T184025Z-989173c9](D:\Programy\UnityProjects\RageQuitting\Artifacts\Validation\20261026T184025Z-989173c9\summary.json); główny plan, okolice linii 155. Lokalny cleanup fixture został już poprawiony.
3. **25.10 — teardown NGO.** `SubstanceFractionalUnitsNetwork` zakończył teardown błędem `ObjectDisposedException` na `NativeList` / `UnityTransport.Shutdown`. Potwierdza wadę cleanupu, ale samo nie dowodzi wpływu na następny przypadek. Raport: [20261025T210420Z-02bdf75f](D:\Programy\UnityProjects\RageQuitting\Artifacts\Validation\20261025T210420Z-02bdf75f\summary.json).

Kontrprzykłady i ograniczenia interpretacji:

- **27.10:** timeout `SingleCarryPlacementStage5` (217/218) wynikał z żądania przed oknem 50 ms; po poprawce harmonogramu fixture pełny przebieg dał 218/218 (`20261027T084206Z-1ba3c170` → `20261027T084825Z-dd420c5e`). Nie wykazano wycieku.
- **26.10:** przyczyna sporadycznej porażki `BridgeDeckPanelStage1` pozostaje nieznana.
- Podejrzenie subskrypcji `EquippableItem.OnAnyItemEquipped` w `PlayerInteractionNew.Start` / `OnDestroy` jest hipotezą do sprawdzenia w aktualnym kodzie, a nie potwierdzoną dawną przyczyną.

## Kandydaci i kolejność prac

Pierwsze pliki do przejrzenia: `Assets/Tests/PlayMode/SubstanceFractionalUnitsNetworkPlayModeTests.cs`, `LooseSoilStage4NetworkPlayModeTests.cs`, `LooseSoilStage5NetworkPlayModeTests.cs` oraz fixture związane z inputem. Przy powrocie sprawdzić aktualny kod i stan roboczy; historyczne poprawki mogły już rozwiązać część problemów.

1. Najpierw audyt i odtworzenie możliwie wąskiego przypadku z zachowaniem kolejności.
2. Dodać wspólny mechanizm do jednego fixture i test regresyjny, który wiarygodnie obala wadliwy cleanup przed poprawką oraz przechodzi po niej.
3. Niezależnie przejrzeć zmianę, potem migrować kolejne fixture etapami.
4. Weryfikować test osobno, A→B, B→A, powtórzone A, kilka kontrolowanych kolejności / seedów jeśli runner je wspiera oraz pełny zestaw. Pojedynczy zielony pełny zestaw nie dowodzi niezależności.
5. Sprawdzić cleanup również po nieudanym teście. Naprawiać przyczynę; nie wyciszać logów, wyłączać testów ani arbitralnie wydłużać timeoutów.

## Walidacja przy przyszłym wdrożeniu

Jeśli zmiana obejmie runtime lifecycle, sieć lub integrację testów runtime, końcowa ścisła walidacja to `Gameplay`; `Fast` tylko dla zakresu rzeczywiście ograniczonego do narzędzi / Editor. Przyszły wykonawca ma wybrać tier zgodnie z rzeczywistym zakresem, uruchomić końcowy nieprzefiltrowany strict preflight i zaraportować status, exit code, `summary.json`, każdy niezaliczony krok i ograniczenia. Nie akceptować wyniku `AllowLowerTier`. Używać bieżącego CLI/wrappera Unity; pierwszy rzeczywisty wywołujący CLI/harness/preflight wymaga `require_escalated` i `login:false`; katalog roboczy: `D:\Programy\UnityProjects\RageQuitting`, dokumentacja robocza: `C:\Users\dunia\Documents\RageQuitting`. Istotne reprodukcje zachować przez `KeepArtifacts`; retencja domyślna wynosi 14 dni.

Kryteria ukończenia: brak należących do testu obiektów i aktywnych transportów NGO po teście; przywrócony stan globalny, inputu, `Time` i fizyki; teardown ograniczony czasowo z widocznymi błędami; wyniki niezależne od kontrolowanej kolejności; pełna walidacja właściwego zakresu. Zapis kluczowych faktów i identyfikatorów raportów w tej notatce pozwala odtworzyć kontekst po retencji artefaktów.