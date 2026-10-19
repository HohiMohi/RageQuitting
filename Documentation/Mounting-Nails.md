# Gwoździe montażowe — Etapy 1–4

## Status

Etap 1 został ręcznie sprawdzony i zaakceptowany 11.10.2026. Etap 2 wraz z poprawkami placementu F/E na poruszanym celu i izolacji kontaktu single-carry został ponownie ręcznie sprawdzony; użytkownik potwierdził „Okej, poprawki działają” i zezwolił na etap 3. Etapy 3 i 4 są wdrożone i przeszły opisane niżej testy automatyczne. Użytkownik ręcznie zaakceptował Etap 3 dnia 15.10.2026. Użytkownik potwierdził, że wymienione punkty checklisty Etapu 4 oraz poprawki podglądu F/E na poruszanym celu i zwykłego rzutu E działają („Okej, poprawki działają”). Etapy 1–4 są ręcznie zaakceptowane.

## Status fizycznych płyt

Etap 1 (single-carry, geometria, pasywne sloty i fixture) został ręcznie zaakceptowany 17.10.2026 („Wydaje się, że wszystko działa”). Etap 2, wcześniejszy HUD i kolorowa ramka oraz follow-up prowadzenia perspektywicznego F/E zostały ręcznie sprawdzone i zaakceptowane 18.10.2026. Etap 3 resetu młotem i ochrony ukończonej płyty został ręcznie zaakceptowany 18.10.2026 po poprawce grawitacji; geometryczna poprawka prześwitu z 19.10.2026 została potwierdzona przez użytkownika jako działająca. Użytkownik potwierdził działanie poprawki paneli. Następnie zlecił porządkowanie sceny fixture, które zostało wykonane i technicznie zwalidowane; nie ma osobnej wizualnej akceptacji samej zmiany porządkowania. Checklisty Etapu 4 nie są potwierdzeniem osobnego sprawdzenia każdego scenariusza. Gwoździe można przybijać przed ukończeniem panelu; nie dodano polityki blokującej ich wbijanie.

Historyczna walidacja techniczna Etapu 1 fizycznych płyt: [summary](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20261017T180307Z-54056cce/summary.json), strict unfiltered Gameplay `passed`, exit 0, 14/14 kroków, bez ostrzeżeń i kroków niezaliczonych. EditMode 104/104, PlayMode 115/115; analyzer 921/316 grup, zero nowych względem chronionego baseline 925/316. Offline ActualFAndECommit oraz testy NGO jawnie ustawiły `placementDistance=5f` na controllerze/preview obu kopii; potwierdzają produkcyjną ścieżkę przy tej konfiguracji, nie domyślne 2 m. NGO loopback testował hosta i zdalnego ownera w jednym procesie; nie jest to osobny proces klienta ani WAN/Relay. Odbiór wizualny i checklista Stage1 zostały wykonane i zaakceptowane 17.10.2026.

Historyczny raport końcowy po follow-upie z 2026-10-15: [summary](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20261015T191713Z-b6f585f6/summary.json). Status `failed`, kod 1 wyłącznie przez analyzers; wszystkie pozostałe kroki przeszły, w tym compile, Console, EditMode 101/101 i PlayMode 106/106 bez pominięć, validators, gameplay-scenario, network-smoke i mutation guard. Analyzer: 936 wystąpień/326 grup wobec chronionego baseline 925/316; fingerprinty i liczniki bez zmian względem raportu Etapu 4 sprzed poprawek (delta 0), baseline bez zmian. Targeted follow-up: [summary](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20261015T191425Z-6c638452/summary.json), EditMode 7/7 i PlayMode 33/33; tylko analyzer nie przeszedł.

Raport Etapu 4 przed follow-upem (historyczny): [summary](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20261015T173521Z-cdc6e8b2/summary.json). Status `failed`, kod 1 wyłącznie przez analyzer ratchet: 936/326 wobec chronionego baseline 925/316; wszystkie pozostałe kroki przeszły, w tym EditMode 101/101 i PlayMode 103/103, bez pominięć. W próbkach SRP 17.3.0 było 15 nowych wystąpień w 10 grupach; cztery starsze wystąpienia zostały wcześniej rozwiązane, co dawało wzrost netto o 11. Fingerprinty i liczniki były identyczne z zaakceptowanym Stage 3 (delta 0), baseline bez zmian. Targeted Etap 4: [summary](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20261015T173245Z-078d2c35/summary.json), EditMode 7/7 i PlayMode 49/49; kroki poza analyzerem przeszły.

Aktualny strict, unfiltered Gameplay z 2026-10-16: [summary](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20261016T152055Z-4a19af9b/summary.json). Status `passed`, kod `0`; wszystkie 14 kroków przeszły, EditMode 101/101 i PlayMode 106/106, bez błędów ani pominięć. Analyzer wykrył 921 wystąpień w 316 grupach wobec chronionego baseline 925/316, bez nowych diagnostyk; baseline i reguły pozostały bez zmian.

Usunięto wyłącznie `Assets/Samples/Scriptable Render Pipeline Core` oraz `Assets/Samples/Scriptable Render Pipeline Core.meta`; zawartość obejmowała tylko `17.3.0/Common`. Zainstalowane pakiety URP/SRP 17.3.0 pozostały bez zmian. Usunięcie wyeliminowało 15 nowych wystąpień w 10 grupach. Backup 103 assetów: [srp-core-common-before-removal.unitypackage](D:/Programy/UnityProjects/RageQuitting/Artifacts/SrpSampleRemoval20261016/srp-core-common-before-removal.unitypackage).

Pierwsza próba eksportu kopii zapasowej przez CLI przekroczyła limit 5 s, ale powstałe archiwum zostało zweryfikowane. Pierwsza próba Gameplay zatrzymała się na Console z powodu komunikatu `[Package Manager Window] Operation cancelled`; dalsze testy nie zostały wtedy uruchomione ([raport](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20261016T151933Z-0dcba3b1/summary.json)). Pełne logi Console zapisano przed jej wyczyszczeniem. Po sprawdzeniu stanu Package Manager, wyczyszczeniu przechwyconej Console i bez zmian w baseline, powyższy pełny przebieg przeszedł. Ograniczenia sieciowe wymieniono poniżej: nie potwierdzono osobnego procesu klienta, buildu multiplayer, WAN ani Relay.

Testy Etapu 4 sprawdzają rzeczywisty moment animacji młota w trybie offline oraz wygenerowane RPC dla luźnego gwoździa w sieci, bez pełnego cyklu animacji nowego uderzenia w luźny gwóźdź. NGO uruchamia hosta, zdalnego ownera, pierwotnego obserwatora i czwartego klienta late join w jednym procesie z loopback UTP; kolizje lustrzane i grawitacja są izolowane. W teście late join prędkość gwoździa jest jawnie zatrzymywana przed sprawdzeniem snapshotu pozy. To nie dowodzi osobnego procesu klienta, buildu multiplayer, WAN ani Relay. Ogólny network-smoke jest host-only (peerCount 1, remoteClientValidated false, Relay false). Ograniczenia starszych testów Stage 3 pozostają opisane w ich historycznym zakresie; test wyścigu pickupu nadal używa bezpośredniego seam serwera przy końcowym trafieniu.

Historyczny raport Stage1: [summary](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20261011T175025Z-2030c610/summary.json). Testy NGO Stage2 oraz wcześniejsze raporty pozostają historią walidacji; ich zakres nie jest rozszerzony przez Etap 3.

## Zakres wdrożony

Gwoźdź jest osobnym `SingleMountingNail` prefabem, resource i profilem, korzystającym z istniejącego modelu `Bridge_Nail_LowPoly`. Nie zmieniono `ForgedNailBundle`, mostu, paneli ani pieca. Dodano wpis do `DefaultNetworkPrefabs`.

- Profil: `Assets/ScriptableObjectAssets/New/MountingNailProfiles/StandardMountingNailProfile.asset`
- Prefab: `Assets/Prefabs/New/Resources/MetalProducts/SingleMountingNail.prefab`
- ResourceSO: `Assets/ScriptableObjectAssets/New/BaseResourceSO/MetalProducts/SingleMountingNail.asset`

Stany gwoździa to `Loose`, `Held`, `Prepared`, `Partial`, `EmbeddedSingle` i `EmbeddedPair`. Stan `EmbeddedPair` oznacza końcowe zatwierdzenie połączenia oryginalnej pary. Przygotowanie może nastąpić wyłącznie w callbacku `ISingleCarryPlacementCommitListener` po zaakceptowanym placement F/E: offline po sprzątnięciu holdera, online po zatwierdzeniu przez serwer i sprzątnięciu. `ISingleCarryPlaceable` zachowuje kompatybilność. Drop, Esc, anulowanie i odrzucone żądanie nie przygotowują gwoździa. Nieprawidłowy kontakt albo kierunek nie blokuje poprawnego placement — w takim przypadku gwóźdź pozostaje `Loose`.

## Sterowanie i kontakt — etap 1

F rozpoczyna preview placementu, E zatwierdza, a Esc anuluje. Strzałki obracają gwóźdź w lokalnych osiach X/Z z prędkością 90°/s, korzystając z istniejącego controllera. Czubek `Tip` w lokalnym +Z musi znaleźć się najwyżej 2 cm przed przecinaną solidną powierzchnią. Wykrywanie pomija własne collidery i triggery. Nie ma wymogu pionowego ustawienia ani automatycznego obrotu. Po zaakceptowanym placement z poprawnym kontaktem stan `Prepared` zamraża pozę także na ścianie lub suficie. Zaakceptowany placement bez poprawnego kontaktu pozostawia gwóźdź w stanie `Loose`.

`Prepared` wraca do `Loose` po 3 s, utracie kontaktu albo zmianie celu. Ruch fizycznego rootu celu o więcej niż 1 cm lub obrót o więcej niż 2° względem pozycji wejściowej również zwalnia gwóźdź. Przy zwolnieniu wraca grawitacja i następuje jednorazowe boczne wytrącenie 0,15 m/s przy `Head` (impuls obrotowy). Kilka colliderów należących do tego samego Rigidbody, NetworkObject albo resource rootu jest traktowanych jako jeden stabilny cel. Podniesienie `Prepared` od razu ustawia `Held`, bez impulsu; kolejne F może ponownie przygotować gwóźdź. Niesiony cel nie może przygotować gwoździa.

## Etap 2 — wbijanie w pojedynczy obiekt

`IndustrialHammer` uruchamia uderzenie w rzeczywistym momencie `Strike → ImpactFreeze`. Domyślnie pojedynczy obiekt wymaga jednego poprawnego trafienia; `RequiredHitsForSingleObject` pozwala skonfigurować większą liczbę trafień. Pierwsze poprawne trafienie usuwa trzysekundowy timer przygotowania, zamraża wymaganą liczbę trafień i końcową głębokość 0,30 m. Następne poprawne trafienia zagłębiają gwóźdź proporcjonalnie do postępu. Stany `Partial` i `EmbeddedSingle` należą do niezależnego obiektu: gwóźdź podąża za pierwszym celem, zachowując względem niego lokalny punkt wejścia i obrót.

Przygotowany gwóźdź jest celem przez odsłonięty odcinek od `Head` do punktu wejścia; namierzanie używa tolerancji 0,06 m wokół tego odcinka, a odcinek zakopany jest pomijany. Celowanie priorytetyzuje gwoździe pod kursorem oraz sprawdza zasięg i widoczność. Wymagane jest właściwe narzędzie; luźny gwóźdź nie zostaje wbity, a trafienie pod gwoździem nie zadaje obrażeń. Gracz lub NPC może być celem wbicia, lecz nie otrzymuje obrażeń. Pierwsze trafienie nie może użyć niesionego celu. Po wygaśnięciu przygotowania wbicie jest odrzucane nawet wtedy, gdy kliknięcie nastąpiło przed końcem timera, ale uderzenie dotarło po nim. Od Etapu 4 takie uderzenie może jednak zwolnić gwóźdź do `Loose` i nadać mu impuls.

Gwóźdź w stanach `Partial` i `EmbeddedSingle` blokuje podniesienie przez gracza/NPC. Sam cel pozostaje możliwy do przenoszenia. HUD pokazuje stan i postęp uderzeń zamiast zasobu HP; zwykłe przygotowanie i podnoszenie pozostają dostępne, a zamontowany gwóźdź pokazuje informację i postęp.

## Sieć i autorytet

Serwer rozstrzyga stan, timer i impuls. Kopie gwoździa po stronie klientów są kinematic; ownership przenoszenia pozostaje w istniejącym systemie carry. Stan jest utrwalany przez `NetworkVariables` i `NetworkTransform`, w tym dla late join. Test NGO używa rzeczywistego owner RPC oraz minimalnego playera z produkcyjnymi komponentami carry i placement.

Placement zatwierdzany z klienta zachowuje identyczność celu przez referencję do `NetworkObject` i tożsamość fizycznego rootu; klient i serwer przeliczają tę samą lokalną pozycję i orientację względem tego celu w chwili obsługi żądania. Walidacja ponownie sprawdza anchor i promień celowania, limit 2 m, widoczność (także gdy origin zaczyna wewnątrz przeszkody), kontakt i zajętość. Przesunięcie/obrót celu oraz wyjście celu z pierwotnego promienia są dozwolone, jeśli nadal spełnione są te kontrole. Referencja nieaktualna po despawnie/respawnie, utrata kontaktu, przeszkoda, zajętość lub sfałszowana geometria powodują odmowę bez upuszczenia obiektu; można ponowić próbę. Dowód pozy pozostaje ograniczony do 2 mm i orientacji do 0,1°. Dla celów bez `NetworkObject` sam protokół placementu nadal wymaga ścisłej pozycji świata. Etap 3 dodaje im trwałą tożsamość oraz serwerowe podążanie osadzonego gwoździa za celem. Callback gwoździa wykonuje się raz po autorytatywnej akceptacji, a duplikat RPC nie odnawia terminu `Prepared`.

Własny `NetworkManager` serwera autorytatywnie weryfikuje młot z inventory serwera, ograniczony i zaufany origin celowania, zasięg, widoczność, cooldown oraz replay numerów sekwencji. Offline `MountingNail.TryApplyHammerImpact` jest wywoływane w rzeczywistym momencie animacji młota; sieciowy impuls luźnego gwoździa przechodzi przez wygenerowane RPC i walidację akcji serwera. Trafienie `IndustrialHammer` w luźny gwóźdź dodaje do jego bieżącej prędkości poziomą prędkość od gracza (domyślnie 1 m/s) i składową 0,25 m/s w górę. Nie stosuje się kompensacji opóźnienia: serwer rozstrzyga uderzenie w chwili otrzymania żądania. Stan `Partial`/`EmbeddedSingle`, liczba trafień, wymagana liczba i zamrożona głębokość replikują się przez `NetworkVariables`, a światowa poza przez istniejący `ClientNetworkTransform` używany przy ownership serwera, również dla late join. Kopie klientów są kinematic, ownership gwoździa pozostaje u serwera. Despawn celu z `destroy=false` i zwykły `Destroy` usuwają zamontowany gwóźdź.

Pełny `PlayerNew`, wizualne sprawdzenie sceny/inputu, wieloprocesowy host + klient + late join oraz przypadek trafienia w obiekt z prawdziwym systemem zdrowia wymagają ręcznego sprawdzenia. Automaty NGO używają minimalnego produkcyjnego komponentu gracza; dodatkowy atakujący to host obok zdalnego ownera, nie drugi zdalny klient. Testy zdrowia są osobne i offline.

## Etap 3 — sztywne połączenie pary

Etap 3 dodaje stan `EmbeddedPair`. `RequiredHitsForTwoObjects` domyślnie wynosi 3; pojedynczy obiekt nadal wymaga 1 trafienia. Zasięg i końcowa głębokość wynoszą 0,30 m. Pierwsze trafienie zapamiętuje oryginalną parę. Po pierwszym i drugim trafieniu oba obiekty pozostają niezależne — nie ma jeszcze jointa. Drugie trafienie nie sprawdza ponownie drugiego celu. Dopiero trzecie, końcowe trafienie ponownie waliduje tę samą parę. Jeśli wówczas drugi cel jest poza kontaktem, usunięty, zablokowany lub niesiony, gwóźdź wraca do `EmbeddedSingle` na pierwszym celu bez jointa i bez zastępowania celu. Dotyczy to także sytuacji, gdy niesiony jest pierwszy cel. Nie ma automatycznego snapowania. Gracz i NPC mogą być celami pojedynczego wbicia, ale nie można ich łączyć w parę gwoździ.

Po końcowym trafieniu serwer tworzy jeden `FixedJoint` na nieuporządkowaną parę, z `breakForce` i `breakTorque` ustawionymi na nieskończoność, jeśli co najmniej jeden cel ma Rigidbody. Cel statyczny bez Rigidbody jest wtedy kotwicą świata. Jeśli oba cele są statyczne i nie mają Rigidbody, rejestr utrzymuje logiczne połączenie i blokadę pickup bez `FixedJoint`. Wiele niezależnych gwoździ tej samej pary współdzieli jeden joint; osobne pary tworzą łańcuch, np. trzech obiektów. Serwer zarządza jointami i własnością połączonej fizyki, a late join otrzymuje stan. Aktywny pin blokuje pickup obu celów przez gracza/NPC; podczas stanu `Partial` pierwszy cel nadal można podnieść. Aktorów nie łączy się w pary, a gwóźdź wbity w gracza/NPC pozostaje pojedynczy. Usunięcie pierwszego celu usuwa gwóźdź także w stanie `Partial`. Utrata drugiego celu jest rozstrzygana dopiero przy trzecim trafieniu i wtedy kończy się pojedynczym wbiciem na pierwszym celu; niesienie dowolnego celu przy finale również daje `EmbeddedSingle` na pierwszym. Po utworzeniu połączenia utrata pierwszego celu usuwa gwóźdź, a drugiego cofa go do pojedynczego wbicia. Usunięcie ostatniego gwoździa danej krawędzi usuwa połączenie tej pary. Blokada pickup danego celu znika dopiero, gdy nie ma on innych krawędzi w łańcuchu. Ownership NetworkObject wraca dopiero, gdy żaden jego fizyczny root nie ma połączenia. Dla NoNI flagi fizyki pozostają w trybie follow, gdy nadal śledzi go inny `Partial`/`Single` gwóźdź; są przywracane dopiero, gdy nie ma już połączeń ani follow. Stan jest odtwarzany dla late join.

Cel bez `NetworkObject` identyfikuje wypieczony `UnityEditor.GlobalObjectId` (zawiera GUID sceny i trwały identyfikator obiektu). Cel z `NetworkObject` identyfikuje bieżący `NetworkObjectId` spawnu oraz `PhysicalRootPath` oparty na indeksach rodzeństwa wewnątrz NetworkObject. Ten root path dotyczy wyłącznie NI. Pierwszy cel bez NGO podąża za serwerową pozą już od pierwszego zaakceptowanego trafienia; `FollowCount` jest niezależny od blokady pickup. Kopiowanie obiektu sceny wymaga ponownego wypieczenia ID przez `MountingNailSceneTargetAuthoring`. Produkty tworzone w runtime wymagają NetworkObject lub przyszłej integracji fabryki.

## Etap 4 — odrzut luźnego gwoździa

Offline `MountingNail.TryApplyHammerImpact` korzysta z rzeczywistego momentu animacji młota. W sieci zatwierdzone przez serwer RPC dodaje luźnemu gwoździowi składową poziomą 1 m/s od gracza (wartość domyślna) i 0,25 m/s w górę do jego bieżącej prędkości. Zachowana jest jego poza oraz bieżąca prędkość liniowa i kątowa. Serwer przejmuje ownership gwoździa i stosuje jeden impuls; kopie klientów pozostają kinematic. Istniejące kontrole serwera weryfikują właściwe narzędzie, zaufany promień celowania, zasięg, widoczność, cooldown i replay. Numer sekwencji jest zużywany przed walidacją trafienia jak dotąd; cooldown przesuwa się tylko po udanym trafieniu. Uderzenie nie przygotowuje gwoździa ani nie zadaje obrażeń. Jeśli przygotowanie lub kontakt przestały być ważne w chwili uderzenia, wbicie jest odrzucane, gwóźdź wraca do `Loose` i może otrzymać odrzut — również gdy kliknięcie nastąpiło przed upływem terminu. `Partial` kontynuuje osadzanie, a ukończony `EmbeddedSingle`/`EmbeddedPair` pozostaje zamontowany. Trzymane, zabezpieczone oraz połączone gwoździe, gdy są celem, są chronione. Luźny gwóźdź można podnieść zwykłą interakcją, a następnie ponownie przygotować przez F/E. Etap 4 nie zmieniał assetów ani scen.

## Konfiguracja

Aktywne parametry profilu:

| Parametr | Wartość |
|---|---:|
| Długość | 0,35 m (bez zmiany; szerokość i collider są dopasowane) |
| Masa | 0,1 kg |
| Mnożnik szerokości wizualu | 8× |
| Średnica fizyczna przy szerokości 8× | ok. 1,784 cm |
| Trafienia dla pojedynczego obiektu | 1 |
| Tolerancja namierzania | 0,06 m |
| Końcowa głębokość pojedynczego obiektu | 0,30 m |
| Czas `Prepared` | 3 s |
| Tolerancja czubka | 0,02 m |
| Próg ruchu celu | 0,01 m / 2° |
| Boczne wytrącenie przy zwolnieniu | 0,15 m/s |

Aktywne parametry obejmują Etap 3: 3 trafienia dla pary, zasięg 0,30 m i głębokość końcowa 0,30 m; dla pojedynczego obiektu nadal 1 trafienie. Etap 4 używa impulsu luźnego gwoździa: 1 m/s poziomo i 0,25 m/s pionowo. Zmiany długości lub masy należy wprowadzić przed Play/spawn; wymagają restartu Play, aby przeliczyć wizual, czubek i collidery. Nie ma gwarancji hot reload profilu.

## Obszar sprawdzania i ręczny odbiór

Aktualne fixture: w scenie gameplay `Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity` pod `BridgeAssemblySupplies` pozostał zestaw z dawnego obszaru testowego: 48 gwoździ i 3 fizyczne panele. To liczby tego zachowanego zestawu, nie całkowity stan supplies/paneli w scenie; pozostałe istniejące supplies i panele nie zostały naruszone. Pozostałe rekwizyty testowe są w `Assets/Tests/Fixtures/MountingNailTestScene.unity` (`NailTestArea`), wyłączonej z Build Settings. `MountingNailTestAreaAuthoring` i `BridgeDeckPanelTestAreaAuthoring` kierują authoring do tej sceny; zależne EditMode testy używają disposable preview scenes i tymczasowych fixture clones. Validator produkcyjny nadal sprawdza główny most, bez zależności od fixture. Szczegóły i audyty: [porządkowanie fixture](Bridge-Construction.md). Scena fixture nie była weryfikowana jako samodzielna scena gry/network bootstrap.

Historyczna migawka układu przed porządkowaniem z 19.10.2026: korzeń `NailTestArea` leżał przy world `(-6,0,2)`; zawierał 12 gwoździ, `IndustrialHammer`, cztery `Board`, statyczne podłoże/ścianę/sufit i dwie dynamiczne kostki Stage3. W `Stage1TestFixture` przy world `(-6,0,7.2)` znajdowały się trzy fizyczne płyty, dwa gotowe dźwigary, trzy miejsca na rozpiętości 6.56 m oraz 36 dodatkowych gwoździ. Oryginalny audyt 67 ID zachowano, a dwa nowe girders miały własne ID. Historyczne potwierdzenie zapisu sceny: [saved-stage02-audit.txt](D:/Programy/UnityProjects/RageQuitting/Artifacts/DeckPanelStage1_20261017/saved-stage02-audit.txt).

## Fizyczne płyty — Etap 1

`WoodenDeckPanelMountable` jest osobnym produktem single-carry: jeden niosący, model/collider o stałym wymiarze `2.16 m × 0.20 m × 2.40 m`, masa spoczynkowa Rigidbody `1 kg`. Flaga `physicalPanelPlacement` jawnie wybiera ten workflow; nowe profile domyślnie mają ją wyłączoną, więc zachowują legacy. Płyta pozostaje samodzielnym przedmiotem. Po luźnej płycie można chodzić; zachowuje istniejącą izolację kontaktu podczas niesienia. Receptury, piec i parametry gwoździ nie uległy zmianie.

W fizycznym wariancie sloty są niezależne od prerequisite, kolejności i `CanBeMounted`. Nie ma assist, snap, capture, `Aligning`, `GapSetting`, fastenerów ani solidnych colliderów slotów. Legacy `StartFullyCompleted` zachowuje dotychczasowy visual i collider. Nie wprowadzono blokady przybijania przed ukończeniem wcześniejszych etapów.

Zwykłe E dla szerokiej płyty wyszukuje wolną pozycję przed graczem w ograniczonym zasięgu interakcji (domyślnie 2.5 m), uwzględniając rozmiar płyty, holdera i przeszkody po drodze. Gdy miejsca nie ma, płyta pozostaje w rękach bez zmiany ownership. To dotyczy wyłącznie fizycznych płyt; inne przedmioty i gwoździe zachowują dotychczasowe zachowanie. Rzut `VelocityChange` (domyślnie 1.5 m/s) pozostaje bez zmian. F/E/Esc placement i obrót nie dodają impulsu zwykłego E. Host/klient są server-authoritative; klient czeka na potwierdzenie przed lokalnym zwolnieniem. Cztery lookupi pickup/release w `MountableBridgeComponent` używają instancyjnego `NetworkManager`, aby poprawnie rozróżniać klienta w NGO loopback.

Historyczna checklista odbioru Etapu 1 (zaakceptowana 17.10.2026; odnotowana wcześniej przed potwierdzeniem):

- podnieś płytę solo, przenieś i upuść; sprawdź standardowy pickup;
- użyj E przy wolnym miejscu i przy zablokowanym miejscu: w drugim przypadku płyta ma pozostać w rękach;
- sprawdź F/E/Esc i obrót, w tym brak impulsu rzutu;
- przejdź po luźnej płycie i sprawdź brak pchania podczas niesienia;
- potwierdź brak snapu/capture oraz host/klient.

### Fizyczne płyty — Etap 2

Ocena korzysta z osi lokalnych płyty i slotu. Aktualny `WoodenDeckPanelWorkflow` używa niezależnego limitu `0.05 m` wzdłuż slotu i `0.10 m` w poprzek; profile bez włączonych oddzielnych tolerancji zachowują starszy radialny limit `0.05 m`. Wysokość wynosi `0.03 m`, a kąt względem rotacji bazowej lub jej obrotu `Y=180°` maksymalnie `3°`. Pas `±0.005 m` wokół osi dzielącej nie zalicza się do żadnej ćwiartki. Wejście gwoździa ocenia się w lokalnych osiach płyty.

Każda z czterech ćwiartek wymaga osobnego gwoździa `EmbeddedPair`: pierwszy cel musi być dokładnie ocenianą płytą, drugi jawnie wymienionym głównym dźwigarem tej samej sekcji. `EmbeddedSingle`, `Partial`, odwrotna kolejność celów, wbicie od spodu, belka poprzeczna i dźwigar z innej sekcji nie liczą się. Duplikat w zaliczonej ćwiartce nie uzupełnia brakującej. Pierwszy w pełni wbity gwóźdź łączący parę tworzy sztywne połączenie i blokuje pickup obu celów, niezależnie od tego, czy zalicza progres panelu; kolejne gwoździe tej samej pary współdzielą to połączenie. Gwóźdź `Partial` zachowuje dotychczasowe reguły przenoszenia.

Lokalne ukończenie wymaga poprawnej pozy, czterech różnych ćwiartek, wolnego slotu i przenikania nie większego niż `1 mm`. Serwer przypisuje jeden panel do jednego slotu. Ukończenie jest lokalne i trwa do restartu; panel nie jest przyciągany, przesuwany, skalowany, zużywany ani zastępowany. Stan panelu replikuje licznik 0/4, brakujące ćwiartki, przyczynę oceny i ukończenie dla late join. Ramka slotu jest obrysem bez wypełnienia i solidnych colliderów.

F ustawia panel na trafionym punkcie z korektą wyłącznie po normalnej powierzchni. Pusta przestrzeń między dźwigarami nie stanowi podparcia: środek panelu musi fizycznie dotykać podłoża. E/drop nadal działa, w tym przy ogólnym wolnym placement, gdy panel nie jest wyrównany do slotu. W momencie opisanym w tej sekcji reset gwoździ i ochrona gotowej płyty należały do kolejnego Etapu 3; obecnie Etap 3 jest wdrożony. Błędny układ można zresetować młotem zgodnie z sekcją Etapu 3 poniżej.

Historyczna walidacja sprzed późniejszych poprawek: strict unfiltered Gameplay [summary](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20261018T094710Z-709011d5/summary.json), `passed`, exit 0, 14/14 kroków; EditMode 113/113, PlayMode 117/117, analyzer 921/316, bez nowych diagnostyk. Test NGO sprawdzał transport i stan hosta/late clienta w jednym procesie z kontrolowanymi obiektami-markerami i zapisem stanu; nie sprawdzał pełnego zdalnego przepływu młot → ocena. Ogólny network-smoke jest host-only; mogła się w nim rozpocząć inicjalizacja Unity Services przed wejściem do ciała testu. Testy Stage1/Stage2 i poprawki historyczne opisują raporty [Stage1 9/9](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/UnityPlayMode/20261018T094250Z-a5606201/unity-playmode-tests-report.json), [wcześniejsze nieudane przebiegi](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20261017T203303Z-9e5702ef/summary.json), [przebieg diagnostyczny](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20261018T090925Z-f94c9208/summary.json). Niektóre wczesne próby korzystały ze starych DLL i nie są dowodem; późniejsze raporty są po odświeżeniu kompilacji. Nie potwierdzono osobnego procesu klienta, multiplayer buildu, WAN ani Relay. Wymóg ręcznej kontroli z tamtego raportu został spełniony dla bazowego Stage2 i wcześniejszej korekty HUD/ramki. Najnowszy Gameplay po poprawce oraz zakres walidacji opisano w `Bridge-Construction.md`, sekcja „Etap 2 — informacja zwrotna po odłożeniu płyty”.

Historyczna checklista bazowego Etapu 2 (ręcznie sprawdzona wraz z wcześniejszym HUD i kolorową ramką):

- sprawdź widoczność ramki podczas F oraz zgodność hosta/klienta;
- potwierdź, że pierwsza pełna para blokuje pickup;
- zalicz cztery ćwiartki w dowolnej kolejności, a następnie spróbuj duplikatu;
- potwierdź, że pojedynczy/częściowy, poprzeczny, odwrotny i pod spodem gwóźdź nie zaliczają ćwiartki;
- sprawdź tolerancje pozycji, wysokości, rotacji, pas osi dzielącej oraz limit przenikania 1 mm;
- sprawdź zajęty slot, przybijanie przed ukończeniem oraz late join ze stanem 0/4 i 4/4;
- trzymając gwóźdź lub inny przedmiot, sprawdź, że HUD nadal pokazuje pozycję i mocowania wskazywanej odłożonej płyty.

### Follow-up prowadzenia perspektywicznego F/E (18.10.2026; zaakceptowany)

Jedna autorska `LineRenderer` tworzy pusty przestrzenny obrys płyty z dwóch pierścieni i czterech pionowych krawędzi, zapisanych jako 17 punktów i 12 unikalnych krawędzi. Nie ma wypełnienia, collidera ani kopii materiału. Lokalne wysokości wynoszą `Y=0.008 m` i `Y=0.208 m`; dolna szerokość to `0.0195 m`, górna `0.015 m`.

W trybie F ramka sprawdza pozę kandydata tymi samymi regułami pozy, zajętości i penetracji rzeczywistych child-colliderów co ocena odłożonej płyty. Kolor zielony oznacza wyrównanie, pomarańczowy nieprawidłowe mocowanie; ogólny stan `free/blocked` jest odrębny i pomarańczowy nie zabrania E, jeśli miejsce pozostaje ogólnie wolne. HUD pokazuje `Mount: Aligned`, korekty Along/Across/Height ze znakiem `+/-` w cm oraz kąt w stopniach, albo `Slot occupied`, `Adjacent overlap`, `No slot`. Lokalna strzałka świata o długości `0.2–0.8 m` wskazuje korektę i jest ukryta dla poprawnej pozy, korekty wyłącznie kątowej i braku slotu. Po E strzałka znika; HUD i ramka oceny odłożonej płyty są widoczne tylko, gdy gracz wskazuje tę płytę.

Esc, wyjście z placementu, zmiana/wyłączenie źródła i wyczyszczenie pending usuwają podgląd F i strzałkę; HUD/ramka po E podlegają wskazaniu odłożonej płyty. Nie dodano RPC, zmian autorytetu ani zapisu ukończenia. Korekta `Physics.SyncTransforms` dla ruszonego celu klienta pozostaje. Solver podparcia nadal wymaga kontaktu środka panelu; szczelina między dźwigarami go nie podtrzymuje. Brak snapu, auto-rotacji, przyciągania i przesuwania panelu podczas oceny; reguły ukończenia i gwoździ pozostają bez zmian.

Końcowy strict unfiltered Gameplay: `passed`, exit 0, 14/14 kroków, EditMode 116/116, PlayMode 118/118, bez pominięć; analyzer 921/316 wobec chronionego 925/316, bez nowych diagnostyk. Raport: [summary](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20261018T115728Z-a48c6d2a/summary.json). Ukierunkowane testy: F 2/2 ([raport](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/UnityPlayMode/20261018T115355Z-3eeffd75/unity-playmode-tests-report.json)) i Bridge 2/2 ([raport](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/UnityPlayMode/20261018T115616Z-5ab4d6c7/unity-playmode-tests-report.json)). Testy NGO są loopback w jednym procesie; ogólny network-smoke jest host-only. Osobny proces, WAN/Relay/build i pełna ścieżka F→serwer→HUD nie zostały potwierdzone; izolacja inicjalizacji Unity Services w smoke nie jest gwarantowana. Widok wymaga ręcznej oceny.

Follow-up prowadzenia perspektywicznego został zaakceptowany 18.10.2026; poniższa lista opisuje zakres odbioru: perspektywa obrysu i kontaktu; zielony aligned kontra pomarańczowa korekta podczas F i po E przy wskazaniu odłożonej płyty; 10 cm w poprzek kontra 5 cm wzdłuż, 3 cm wysokości i 3° kąta; `Y=180°`; sąsiednia penetracja 1 mm; E przy ogólnie wolnym, ale niewyrównanym miejscu; sprzątanie po Esc i zmianie slotu; host i klient. Poprzedni HUD, kolorowa ramka i ten follow-up zostały zaakceptowane. Etap 3 resetu panelu został ręcznie zaakceptowany po poprawce grawitacji; status Etapu 4 znajduje się w osobnej sekcji.

Bazowy Etap 2, wcześniejszy HUD/ramka oraz follow-up prowadzenia perspektywicznego zostały ręcznie zaakceptowane 18.10.2026. Etap 3 resetu panelu został ręcznie zaakceptowany po poprawce grawitacji na hoście i kliencie. Etap 4 jest wdrożony i technicznie zwalidowany; ostatnia informacja użytkownika nie potwierdza osobnego sprawdzenia każdego scenariusza z checklisty. Porządkowanie sceny wykonano na jego prośbę; brak osobnej wizualnej akceptacji cleanup.

Historyczne korekty fixture i diagnostyki z 18.10.2026: prefaby źródłowe Stage2 oznaczono jako nie-scenowe, by NGO nie obejmowało ich automatycznym spawnem; Stage1 synchronizuje fizykę przed wstępnym raycastem; test rozłączenia Stage6 przechwytuje stan w callbacku, a następnie na czas izolacji queued transform stosuje freeze per Rigidbody. Pierwotna przyczyna rozbieżności `0.206 m` nie została ustalona. Trzy testy rzeczywistego F tymczasowo klonują Input Settings (`IgnoreFocus`/`AllAlways`) i filtrują klawiaturę do assetów należących do fixture; oryginalna referencja i właściwości Input Settings zostały przywrócone, a hash zserializowanego assetu zweryfikowano jako byte-identical. Wczesne próby na nieodświeżonych DLL są nieważne; późniejsze próby potwierdziły świeże timestampy kompilacji. Próba Gameplay z 09:46 była zablokowana przez dirty scene ([raport](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20261018T094612Z-452bc40c/summary.json)). Po porównaniu kopii i potwierdzeniu identyczności bajtowej zapisano scenę bez zmian; późniejszy finalny Gameplay zakończył się sukcesem ([raport](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20261018T094710Z-709011d5/summary.json)). Wcześniejsza rozbieżność anchora `29 m` ma nieustaloną przyczynę; trace pokazał zbieżność do `0.0185 m` ([trace](D:/Programy/UnityProjects/RageQuitting/Artifacts/DeckPanelStage2_20261018/network-anchor-settle.txt)). Nie stanowi to dowodu pełnego zdalnego hammer → assessment; brak testu osobnego procesu, buildu multiplayer, WAN i Relay. Powyższe raporty i opisy dotyczą stanu historycznego; aktualny wynik końcowy resetu panelu jest podany w sekcji „Fizyczne płyty — Etap 3” na końcu dokumentu.

## Historyczna ręczna checklista gwoździ — Etap 1

- podnieś i upuść gwóźdź;
- sprawdź F, E i Esc;
- sprawdź poprawny i błędny kontakt czubka z podłogą, ścianą i sufitem;
- potwierdź wygaśnięcie po 3 s;
- porusz i obróć cel, a następnie sprawdź zwolnienie gwoździa;
- podnieś `Prepared` gwóźdź i przygotuj go ponownie;
- porównaj zachowanie hosta i klienta.

Ręczna checklista poprawek Stage2 (zaakceptowany odbiór użytkownika) oraz Stage2:

- jako klient wybierz F/E na graczu, NPC i desce; przetestuj przesunięcie i obrót celu pomiędzy podglądem, naciśnięciem E i obsługą żądania przez serwer, także gdy cel odjedzie od pierwotnego promienia; po zaakceptowanym placement utrzymaj cel nieruchomo aż do pierwszego uderzenia młotem;
- sprawdź, że nieaktualny cel po despawnie/respawnie, utrata kontaktu, przeszkoda i zajętość są odrzucane bez dropu i pozwalają ponowić próbę;
- potwierdź, że poprawnie przygotowany gwóźdź nadal zwalnia po przesunięciu celu ponad 0,01 m lub obrocie ponad 2°;
- trzymaj deskę i gwóźdź blisko gracza oraz obracaj kamerą: collidery powinny pozostać dostępne dla celowania, a obiekt nie powinien pchać ani katapultować gracza; po dropie sprawdź przywrócenie kolizji i fizyki;
- porównaj stan hosta, klienta-obserwatora i late join;

- sprawdź masywność/widoczność gwoździa i podnoszenie;
- sprawdź F/E oraz domyślne wbicie jednym poprawnym trafieniem;
- sprawdź kliknięcie przed końcem 3 s, którego impact następuje po wygaśnięciu — wbicie powinno zostać odrzucone, ale luźny gwóźdź może otrzymać impuls Etapu 4;
- sprawdź, że wbity gwóźdź blokuje podnoszenie, a cel z wbitym gwoździem nadal można przenosić;
- sprawdź cele Player/NPC bez obrażeń HP;
- przed Play ustaw `RequiredHitsForSingleObject=3`: po pierwszym trafieniu timer znika, postęp wynosi 1/3, 2/3 i 3/3; po teście przywróć wartość 1;
- sprawdź zgodność hosta i klienta oraz dołączającego late join.

Etap 2 w historycznym workflow gwoździ i jego poprawki zostały zaakceptowane ręcznie przez użytkownika. Powyższe starsze checklisty gwoździ dokumentują zakres dawnych prób i nie zmieniają statusu Etapu 2 fizycznych płyt.

Ręczna checklista odbioru Etap 3:

- ustaw `RequiredHitsForTwoObjects=3`; sprawdź postęp 1/3 i 2/3, gdy dwa cele nadal poruszają się niezależnie; dopiero trzecie trafienie powinno utworzyć joint;
- sprawdź parę ruchomą oraz nieruchomą (w tym statyczny cel bez Rigidbody), kilka niezależnych gwoździ w tej samej parze i łańcuch trzech obiektów;
- w Play Mode usuń pierwszy cel w stanie `Partial` i sprawdź natychmiastowe usunięcie gwoździa; w osobnej próbie usuń lub zablokuj drugi cel albo zacznij nieść którykolwiek cel przed finałem: pośredni hit nie kończy procesu, a trzecie trafienie daje `EmbeddedSingle` na pierwszym celu bez jointa i substytucji; niesienie któregokolwiek celu przy finale daje ten sam wynik; gwoździa nie da się wyciągnąć;
- sprawdź podnoszenie obu stron przez gracza i NPC w stanach częściowym i końcowym, a także dozwolone podnoszenie pierwszej strony podczas `Partial`;
- porównaj hosta, klienta i late join; w PlayMode Editorze usuń cel, aby sprawdzić lifecycle cleanup (gwoździa nie da się wyciągnąć narzędziem); sprawdź utratę pierwszego i drugiego celu oraz zdjęcie blokad i przywrócenie poprzedniego ownership po usunięciu ostatniego gwoździa z krawędzi.

Etap 3 został ręcznie zaakceptowany 15.10.2026. Etap 4 jest wdrożony. Użytkownik sprawdził wymienione punkty checklisty Etapu 4 bez problemów. Poniższe poprawki dotyczące podglądu F/E na poruszanej desce i rzutu E są osobną zmianą; ten historyczny opis nie rozstrzyga ich ręcznego odbioru.
Poprawka podglądu wywołuje `Physics.SyncTransforms` w `PlayerSingleCarryPlacementController.RefreshPreview` przed raycastem i walidacją penetracji, aby odczytywać collidery po zmianie położenia/obrotu deski; zatwierdzenie nadal synchronizuje aktualny świat. Nie zmienia globalnego `autoSyncTransforms`, tolerancji, zasięgu ani zajętości. W teście pionowego gwoździa i drewnianej deski, przy kontrolowanych zmianach Transform kopii klienta przed krokiem fizyki, podgląd był niepoprawny w 8/16 prób przed zmianą i 0/16 po zmianie. To test podglądu; istniejące testy zakotwiczonego F/E sprawdzają zgodność zatwierdzenia. Nie zmieniano scen ani assetów.

E poza trybem F używa serializowanego pola gracza `PlayerInteractionNew.singleCarryDropThrowMetersPerSecond` (domyślnie 1,5 m/s), a zmiana prędkości jest niezależna od masy. Offline, host i klient używają tego samego łagodnego rzutu. Online serwer weryfikuje wyłącznego holdera, wyprowadza kierunek, prędkość i odsunięcie z pozycji serwera, przyjmuje żądanie bez payloadu, przejmuje ownership przed impulsem i odrzuca powtórzone żądanie byłego holdera; offline rzut odbywa się lokalnie. Zatwierdzony rzut nie powoduje cofnięcia obiektu; kopie bez prawa symulowania fizyki pozostają kinematyczne zgodnie z autorytetem `NetworkTransform`, także dla late join i niezabezpieczonego `BaseResourceNew` oraz `MountableBridgeComponent`. Ta zmiana nie dodaje rzutu dla shared carry, NPC, wymuszonej zmiany stanu, disconnect ani transportu. F/E w aktywnym trybie F zatwierdza precyzyjnie bez impulsu; odrzut młotem dla luźnego gwoździa pozostaje bez zmian. Ręczna checklista Etapu 4:

- uderz młotem w luźny gwóźdź i sprawdź kierunek oraz odczuwalny impuls; następnie podnieś go zwykłą interakcją i ponownie przygotuj przez F/E;
- po przygotowaniu odczekaj do wygaśnięcia terminu, po czym uderz: wbicie ma się nie udać, a gwóźdź ma pozostać luźny i otrzymać impuls; sprawdź ponowne F i przygotowanie;
- potwierdź, że `Partial` nadal kontynuuje montaż, a ukończone `EmbeddedSingle`/`EmbeddedPair` pozostają zamontowane po uderzeniu;
- wykonaj pełny host/klient: kierunek i ciągłość pozy/prędkości gwoździa, late join podczas ruchu i po jego zatrzymaniu, rozłączenie gracza uderzającego, cleanup celu oraz ponowne uruchomienie sceny.

Pozostałe starsze checklisty dotyczące disconnect cleanup, carry ghost/interpolacji i innych zachowań carry zachowują własny status ręcznej weryfikacji.
Nowe testy uruchamiają hosta, klienta zdalnego i klienta dołączającego później w jednym procesie loopback UTP. Wykorzystują gwóźdź 0,1 kg i testową deskę 20 kg oraz sprawdzają masę po zwolnieniu; konfiguracja gracza 2,25 m/s potwierdza wartość inną niż domyślna. Testy sprawdzają RPC, ścieżkę hosta, autorytet i odrzucenie powtórzonego żądania. Snapshot dotyczy zatrzymanego przedmiotu. Nie potwierdzają osobnego procesu klienta, buildu ani WAN; ogólny network-smoke jest host-only (peerCount 1, remoteClientValidated false, Relay false). Zakres tych testów jest odrębny od historycznych testów Etapu 4 z czterema uczestnikami.

Ręczna checklista poprawki podglądu i rzutu:

- jako klient w widoku z góry sprawdź podgląd F/E pionowego gwoździa na desce wcześniej przesuniętej, przeniesionej lub obróconej, po jej odłożeniu i zatrzymaniu; poprawny podgląd powinien być stabilny, a przygotowanie powinno się udać;
- porównaj hosta i klienta: podnieś gwóźdź i deskę zwykłą interakcją, a następnie podczas trzymania aktywuj E poza trybem F i sprawdź łagodny rzut do przodu z domyślną prędkością 1,5 m/s; podnieś obiekty ponownie zwykłą interakcją;
- podczas aktywnego F użyj E do precyzyjnego zatwierdzenia i sprawdź brak dodatkowego impulsu; osobno potwierdź, że impuls młota dla luźnego gwoździa pozostał bez zmian.

### Powiązanie z Etapem 2 fizycznych płyt (18.10.2026)

Historyczny opis wcześniejszej korekty: po odłożeniu płyty informacja o położeniu
(`Placement correct` albo powód błędu) oraz mocowaniach (`Fastenings: n/4 -
missing ...`) jest niezależna od postępu wbijania. Prawidłowa pozycja przy 0/4
nie kończy montażu; ukończenie wymaga poprawnej pozycji i wszystkich czterech
prawidłowych ćwiartek. Nie zmieniono mechaniki gwoździ. Wcześniej opisano, że
podgląd F pozostaje bez zmian; ten zakres został rozszerzony przez nowy
follow-up prowadzenia perspektywicznego wyżej. Szczegóły, tolerancje, kolory,
czyszczenie UI oraz zakres walidacji opisano w `Bridge-Construction.md`.

Bazowy Etap 2, wcześniejsza korekta HUD/ramki i follow-up prowadzenia perspektywicznego zostały zaakceptowane przez użytkownika 18.10.2026. Poniższa lista opisuje zakres odbioru: poprawna płyta 0/4, błędna pozycja, zmiana celu/F/podniesienie, ukończenie 4/4, zgodność hosta/klienta/late join oraz widoczność pozycji i licznika mocowań podczas trzymania gwoździa lub innego przedmiotu.

## Fizyczne płyty — Etap 3

**Status (18.10.2026):** wdrożony, poprawka grawitacji technicznie zwalidowana i ręcznie zaakceptowana przez użytkownika na hoście oraz kliencie. Początkowe, nigdy niepodnoszone płyty po uderzeniu podskakują, opadają i lądują.

Trafienie młotem przemysłowym w nieukończony panel usuwa bez odzysku każdy osadzony w nim gwóźdź, którego pierwszym celem jest panel: `Partial`, `EmbeddedSingle` lub `EmbeddedPair`. Usuwa też prawdziwy `EmbeddedPair`, gdy panel jest drugim celem. `Partial` w innym obiekcie, który jedynie planuje połączyć się z panelem, pozostaje. `Prepared` nie jest usuwany; przesunięcie powierzchni anuluje przygotowanie zgodnie z istniejącą regułą. Usuwane połączenia zwalniają jointy i blokady pickup; niezależne połączenia są zachowane. Nie wprowadzono ograniczenia wbijania gwoździ przed ukończeniem panelu.

Po odroczonym usunięciu jointów serwer nakłada zmianę prędkości niezależną od masy: `2 m/s` w górę i `0.5 m/s` poziomo od gracza. Domyślne wartości są konfigurowalne i ograniczane zakresem w `BridgeDeckPanelConstructionWorkflowSO`. Autorytet i cykl życia są ponownie sprawdzane przed impulsem; serwer pozostaje właścicielem fizyki. Trzymany przez gracza lub NPC panel nie jest resetowany. Poprawnie ukończony panel jest chroniony, także po wcześniejszym ukończeniu; panel z czterema ćwiartkami w błędnej pozie pozostaje nieukończony. Kandydat gwoździa zachowuje priorytet nad panelem, nawet jeśli trafienie nie może zmienić stanu gwoździa. Masa, tłumienie, impuls i reguły ukończenia/gwoździ pozostały bez zmian.

Przyczyna błędu grawitacji: `MountableBridgeComponent.OnNetworkSpawn` ustawiał fizykę przed inicjalizacją authority przez późniejszy sibling `ClientNetworkTransform`, więc niepodniesiona płyta serwerowa mogła pozostać z `useGravity=false` i `isKinematic=true`. `OnNetworkPostSpawn` ponownie ustawia fizykę zrzuconej, niepodniesionej płyty; zaakceptowany reset w `BridgeDeckPanelSectionNetworkState` jawnie przywraca `useGravity=true` po kontroli, czy płyta nie jest trzymana. Nie zmienia to masy, tłumienia ani impulsu.

Końcowy ścisły, nieprzefiltrowany Gameplay preflight przeszedł: exit 0, 14/14 kroków, EditMode 117/117 i PlayMode 121/121, zero pominięć i inconclusive. Analyzer: 921 wystąpień wobec chronionego baseline 925/316, bez nowych diagnostyk i bez zmiany baseline. [Raport](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20261018T172716Z-cc135b9e/summary.json). Świeży filtr testów sieciowych przeszedł 9/9: [raport](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/UnityPlayMode/20261018T172606Z-f2029bc4/unity-playmode-tests-report.json). Smoke rzeczywistej sceny addytywnie wczytuje TutorialTerrainStage02 i sprawdza wyłącznie start/spawn hosta z niepodniesioną płytą; nie uderza w tę instancję. Zdalny RPC młota z ownerem, observerem i late join oraz spadek pod prawdziwą grawitacją testowany jest na runtime instancji authored prefabu w jednoproc. UTP. Nie potwierdzono osobnych procesów, WAN/Relay, buildu gracza ani pełnego zdalnego cyklu animacji. Po Gameplay Console miało 0 błędów i 11 ostrzeżeń; wszystkie 11 tekstów wystąpiło wcześniej (porównanie dotyczy treści komunikatu, nie stack trace/origin): [snapshot](D:/Programy/UnityProjects/RageQuitting/Artifacts/DeckPanelStage3Gravity_20261018/Validation/post-gameplay-console.json), [porównanie](D:/Programy/UnityProjects/RageQuitting/Artifacts/DeckPanelStage3Gravity_20261018/Validation/post-gameplay-console-comparison.json). Historyczny błąd AI Relay był w wcześniejszym baseline; jego brak w późniejszym snapshotcie nie dowodzi naprawy Relay. Poprzednia próba strict z timeoutem PlayMode jest opisana w [checkpoint](D:/Programy/UnityProjects/RageQuitting/Artifacts/DeckPanelStage3Gravity_20261018/Validation/final-worker-checkpoint.txt); nie jest aktualnym wynikiem.

Historyczna walidacja Etapu 3 przed poprawką grawitacji: ścisły Gameplay przeszedł 14/14 kroków, EditMode 117/117 i PlayMode 120/120; analyzer 921 wobec baseline 925/316. [Raport historyczny](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20261018T152306Z-c16f3daa/summary.json), [checkpoint](D:/Programy/UnityProjects/RageQuitting/Artifacts/DeckPanelStage3_20261018/Validation/final-worker-checkpoint.txt), [Console po tamtym przebiegu](D:/Programy/UnityProjects/RageQuitting/Artifacts/DeckPanelStage3_20261018/Validation/post-gameplay-console.json). Aktualną poprawkę i wynik 117/121 opisuje sekcja powyżej.

Użytkownik ręcznie sprawdził poprawkę grawitacji i zaakceptował Etap 3 na hoście oraz kliencie. W testach offline sprawdzono oba kierunki kolejności w tej samej klatce: świeża ocena chroni panel, gdy końcowy gwóźdź ukończy go przed resetem; reset jako pierwszy unieważnia usunięte gwoździe.

### Follow-up geometrii płyt i prześwitu przy przyczółkach (19.10.2026)

Skrócono fizyczny panel wzdłuż mostu z historycznych `2.64 m` do `2.16 m` (wymiary X/Y/Z: `2.16 × 0.20 × 2.40 m`; masa `1 kg`). Model, profil, obrys i collider są zgodne; skala korzenia wynosi `1` i runtime nie skaluje produktu. Główne sloty i fixture mają środki lokalnego X `-2.20/0/+2.20 m`, rozpiętość `6.56 m` i nominalne szczeliny `4 cm`. W głównym moście sloty przesunięto po `48 cm` do środka, co daje nominalnie `12 cm` prześwitu do wewnętrznych krawędzi rzeczywistych przyczółków przy X `±3.4 m` (zewnętrzne krawędzie paneli przy `±3.28 m`). Główne sloty mają local Y `0.095` (światowe `1.425 m`) zgodnie z rzeczywistą wysokością podparcia collidera; widoczny wierzch siatki belki jest na `1.400 m`. Fixture używa innych, ogólnych dźwigarów i zachowuje local Y `0.275`. Fizyczne dźwigary długości `8 m`, ich collidery, ogólny prefab sekcji `14 m` z siedmioma slotami, przyczółki oraz reguły tolerancji, ćwiartek, gwoździ, carry, resetu, sieci i receptur pozostały bez zmian. Istniejące panele zachowują pozycje i dziedziczą nowy rozmiar.

Strict unfiltered Gameplay z 19.10.2026: `passed`, exit 0, 14/14 kroków; EditMode 123/123 i PlayMode 125/125, bez błędów, pominięć ani inconclusive. Oba nowe testy prześwitu przy przyczółku przeszły dla orientacji `0°/180°` oraz podparcia fixture. Analyzer: 921 wystąpień/316 grup wobec niezmienionego baseline 925/316, bez nowych diagnostyk; source hash guard wykrył 0 mutacji. Console po przebiegu: 0 błędów i 11 ostrzeżeń; porównanie wyłącznie po tekście komunikatu znalazło wszystkie ostrzeżenia w Console sprzed zmiany. Console nie była jawnie czyszczona, a zwykły cykl Play może resetować log. [Raport Gameplay](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20261019T140756Z-64ae9443/summary.json), [checkpoint i zakres ręcznej akceptacji](D:/Programy/UnityProjects/RageQuitting/Artifacts/DeckPanelAbutmentClearance_20261018/Validation/Resume_20261019/review-checkpoint.json), [audyt geometrii sceny](D:/Programy/UnityProjects/RageQuitting/Artifacts/DeckPanelAbutmentClearance_20261018/Validation/Resume_20261019/review-layout.json), [porównanie Console](D:/Programy/UnityProjects/RageQuitting/Artifacts/DeckPanelAbutmentClearance_20261018/Validation/Resume_20261019/console-comparison.json).

Ukierunkowane wywołanie Pipeline run_tests z 18.10.2026 zakończyło się timeoutem narzędzia po 240000 ms i kodem CLI 1 bez zapisanego surowego wyniku; nie było zaliczonym testem. Zastępuje je powyższy pełny przebieg z 19.10.2026. Nie wykonano osobnego procesu klienta, WAN/Relay ani player buildu; istniejące testy NGO używały lokalnych peerów w jednym procesie, a network smoke jest host-only. Użytkownik potwierdził, że poprawka fizycznych paneli działa. Porządkowanie sceny wykonano na jego prośbę i technicznie zwalidowano; nie ma osobnej wizualnej akceptacji tej zmiany. Nie zgłaszał osobnej weryfikacji każdego scenariusza Etapu 4.

## Fizyczne płyty — Etap 4

**Status:** wdrożony i technicznie zwalidowany; użytkownik nie zgłaszał osobnego sprawdzenia każdego scenariusza z checklisty. Końcowy strict Gameplay przeszedł 14/14 kroków, EditMode 124/124 i PlayMode 125/125.

Trzy główne sloty paneli są zintegrowane z `GameplayManager`. `NailTestArea` wykluczono dokładnymi referencjami komponentu i dźwigara. Wcześnie ukończona płyta zostaje od razu zapisana jako zamontowana, złożona i ukończona, pozostaje fizycznie na miejscu i jest chroniona przed resetem; po spełnieniu wcześniejszych wymagań globalny progress pomija ją automatycznie. Ukończenie zgłasza się raz. Księga świata i lokalna pokazuje `Secured panels: n / 3`, aktualizuje otwartą stronę bez zmiany wyboru, a HUD podaje ukończone/wymagane liczby dla przyszłych etapów.

Testy NGO użyły hosta, zdalnego klienta i late join w jednym procesie z kontrolowanymi payloadami/tabelami ukończeń. Nie sprawdzono pełnego młot → globalny snapshot w osobnym procesie klienta, odrzuconej mutacji uprawnień, WAN/Relay ani player buildu; network-smoke jest host-only. Raporty i ograniczenia opisuje [Bridge-Construction](Bridge-Construction.md#fizyczne-płyty--etap-4-globalny-progress-i-księga-18102026).

Historyczna checklista scenariuszy ręcznych: przetestuj trzy panele w dowolnej kolejności i równolegle z dwoma graczami; sprawdź wykluczenie fixture, wczesne ukończenie i automatyczne pominięcie bez ponownego gwoździowania, pojedyncze zdarzenie, licznik księgi/HUD z otwartą stroną, brak podwójnego visuala/collidera, late join, disconnect wykonującego gracza i restart sceny.
