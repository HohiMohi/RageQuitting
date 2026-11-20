# Tutorial Terrain Stage02 — migracja gameplayu

## Status

W repozytorium znajdują się scena Stage02, jej bazowe assety, ta dokumentacja oraz wymienione niżej wybrane raporty i skrypty tekstowe. Rendery, podglądy i prototypy pozostają w lokalnym archiwum i nie są commitowane.

Migracja sceny eksperymentalnej została zapisana i niezależnie skontrolowana w Editorze. Nadaje się do dalszej oceny i pracy w Editorze; nie jest potwierdzona jako gotowa runtime ani multiplayer.

## Własność i wejścia

- Scena Unity: [TutorialTerrainStage02.unity](../Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity)

- Scena: `Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity`.
- Stage02 jest rozpoznawana przez `GameplaySceneRegistry`; włączony wpis Build Settings dodano na końcu listy dla bezpośredniego ładowania/restartu.
- Przycisk hosta `Tutorial` w lobby kieruje teraz do `TutorialTerrainStage02` przez `GameplaySceneRegistry.TutorialTerrainStage02SceneName`. Ładowanie odbywa się przez `NetworkManager.SceneManager.LoadScene`; Stage02 jest wpisana i włączona w Build Settings. `Tutorial_scene` pozostaje dostępna jako scena źródłowa, a FPP nie zmienia routingu. Scena bazowa Tutorial i pusta Stage01 nie zostały zmienione względem bazowych SHA-256.
- Stage02 ma własne TerrainData, materiał, dwie TerrainLayers, mapę drogi RGFloat oraz trzy osobne, zapisane dane NavMesh (główne i dwa proxy wykopów). Shader i albedo pozostają współdzielone ze Stage01 wyłącznie do odczytu; mapa RGFloat należy do Stage02.

## Zmiany

Projekt QualitySettings używa presetu Ultra z `useLegacyDetailDistribution=false`, czyli ustawienia authoringu rozmieszczenia Terrain Details. Stage02 TerrainData ma `detailScatterMode=CoverageMode`, zero `DetailPrototypes` i zero `TreePrototypes`; nie dodano palet ani instancji Details/Trees.

Cały gameplay tutorialu skopiowano jako scenę: gracza, UI, fabryki, most, wykopy, zasoby i systemy wody. Usunięto zastąpione stare siatki gruntu, koryta i ścieżek; propsy osadzono na Terrain. Pierwotny pomiar migracji zapisał zachowanie wysokości i alphamap Stage01 oraz dwa otwory obejmujące 7400 komórek maski; nie jest to pomiar rewizji szerokości rzeki. Leśne wykluczenie respawnu odwzorowuje naturalną drogę 27 obróconymi triggerami. Dolne koryto otrzymało wolumen `Not Walkable`, który zapobiega suchym trasom po bake na Terrain.

## Kontrola ręczna w Editorze

Pierwotny pomiar migracji (nie stan po rewizji) odnotował 2624 GameObjects / 69 korzeni, 72 prawidłowe, unikalne, niezerowe NGO scene hashes, pięć zachodnich tras `PathComplete` i Camp→East jako `PathPartial`. Liczby te nie opisują bieżącej, ponownie otwartej sceny. Rewizja szerokości rzeki ma osobną walidację poniżej.

## Rewizja oświetlenia 01 — 2026-10-06

Po zapisie i ponownym otwarciu sceny potwierdzono stałe oświetlenie Stage02. `Afternoon Sun` jest światłem kierunkowym czasu rzeczywistego o barwie (1, 0.88, 0.73), intensywności 1.15 i miękkich cieniach o sile 0.6. Ambient Trilight ma intensywność 1 oraz kolory nieba (0.58, 0.62, 0.72), horyzontu (0.42, 0.46, 0.55) i gruntu (0.28, 0.30, 0.38). Scena zachowuje własny proceduralny skybox, mgła jest wyłączona, a własny profil Volume używa ACES, ekspozycji +0.15, kontrastu +7, nasycenia -12 i filtra bieli.

Wyłączono `EnvironmentLightingController` oraz pięć lamp akcentujących. Odziedziczone APV, reflection probes i light probes są wyłączone; renderery nie używają probes ani lightmap, a scena nie ma przypisanego `LightingDataAsset` ani lightmap. Stage02 ma własne `LightingSettings` z `autoGenerate=false`, bez automatycznego bake. Gameplay, geometria i dane NavMesh zachowano; w porównaniu przed/po jedyną zatwierdzoną różnicą transformacji jest obrót słońca. W dniu tej rewizji, 2026-10-06, lobby nadal kierowało do `Tutorial_scene`; routing zmieniono 2026-10-07.

Po ponownym otwarciu maska Volume `MainCamera` wynosiła 257; pozostałe kamery zachowały maskę 256. Cztery obrazy przed/po (przegląd i detal) są renderami Unity URP w rozdzielczości 1536×1024. Nie wykonano testów, preflightu, buildu, Play Mode ani bake. Nie zaobserwowano błędów kompilacji C# ani shaderów w migawce Console, która zawierała wcześniejsze błędy przechwytywania i ostrzeżenia.

- [Raport rewizji oświetlenia](../ArtSource/TutorialTerrainStage02/LightingRevision01/LightingRevision01_Report.txt)
- Obrazy: przegląd przed (`lokalne archiwum: ArtSource/TutorialTerrainStage02/LightingRevision01/Lighting_Before_Overview.png`), przegląd po (`lokalne archiwum: ArtSource/TutorialTerrainStage02/LightingRevision01/Lighting_After_Overview.png`), detal przed (`lokalne archiwum: ArtSource/TutorialTerrainStage02/LightingRevision01/Lighting_Before_Detail.png`), detal po (`lokalne archiwum: ArtSource/TutorialTerrainStage02/LightingRevision01/Lighting_After_Detail.png`).

## Routing lobby do Stage02 — 2026-10-07

Przycisk hosta `Tutorial` w `MultiplayerStartScene` ładuje teraz scenę `TutorialTerrainStage02` przez `GameplaySceneRegistry.TutorialTerrainStage02SceneName` i `NetworkManager.SceneManager.LoadScene`. Zmiana sceny NGO jest wywoływana wyłącznie przez hosta i synchronizuje klientów. Stage02 jest wpisana i włączona w Build Settings. Editor odświeżył i skompilował projekt bez błędów C# po tej zmianie. Nie wykonano testów, preflightu, buildu, Play Mode ani weryfikacji sesji host-klient; routing multiplayer nie został sprawdzony w runtime.

## Rewizja szerokości rzeki 01 — 2026-10-07

Ta rewizja dotyczy wyłącznie `TutorialTerrainStage02`. Opis źródłowej `Tutorial_scene` pozostaje opisem źródłowym: teren 90×90, rzeka szerokości 12 m i siedem paneli mostu. Źródłowa scena, Stage01, oryginalne warianty mostu oraz niezwiązany font zachowały bazowe SHA-256. Shader i albedo drogi nadal są ze Stage01 i tylko do odczytu; nowa mapa drogi RGFloat jest własną kopią Stage02.

Własny Terrain Stage02 ma rozmiar 84×90 m, origin (−50, −4, −45), wysokość 8 m, heightmapę 1025 oraz alphamapę i maskę otworów 1024. Zachodnia część sceny zachowała transformacje, wschodnia została przesunięta o −6 m, a Goal znajduje się przy X=20. Rzeka obejmuje X=2…8 (6 m), a dno X=4.25…5.75 przy Y=−3. Root `East_FoundationExcavation` przesunięto z X=0 do X=−6; potomkowie zachowali lokalne transformaty, np. centrum dołu z X=17.25 do X=11.25. Niezależny audyt po zapisie i ponownym otwarciu potwierdził 747 zachodnich transformacji bez zmian, 214 wschodnich z przesunięciem −6 m, 13 transformacji centralnych rozpatrzonych osobno oraz zero rozbieżności i błędów parsowania. Maksymalny błąd maski otworów wyniósł 2.74 cm.

Mapa drogi Stage02 to własny asset RGFloat 1024×1024 o rect (−50, −45, 84, 90); zachowano shader, albedo i kierunek smug. Własne kopie belki, krótkiego stężenia, ScriptableObjects, receptur i katalogu znajdują się w `Assets/Experiments/TutorialTerrainStage02/BridgeWidthRevision`. Główna belka ma 6.8 m długości, zakres wizualny z końcami 7.8 m, collider 8 m i chwyt ±4.6 m. Pomost ma 8 m, trzy panele po 2.64 m z przerwami 0.04 m i dwie poprzeczki. Scena zawiera 13 `BridgeComponents` z unikalnymi ID 0–12 i etapami [0,1], [2,3], [4,5], [6,7], [8,9], [10,11,12]; usunięto nieużywany wpis ID8 i dodatkowe sloty. Obie listy NGO dostały po dwa prefaby przenoszonych elementów, a dotychczasowe wpisy zachowano. Pięć nieużywanych kopii `Back*` pozostało poza sceną, katalogiem i listami NGO, zgodnie z ograniczeniem usuwania w MCP. Koszt głównej belki zmniejszono z dwóch Wooden Log do jednego Wooden Log, jednej deski i kompletu łączników; czasy i nastawy minigry pozostały bez zmian. Niezależny odczyt końcowej sceny wykazał 72 `NetworkObjects`, zero zerowych hashy, zero powtórzeń; odczytano wszystkie wpisy. Nie jest to test sesji host–klient.

Wszystkie trzy własne powierzchnie NavMesh zostały przebudowane. Niezależne próbkowanie wykazało Camp→Grove/Sawmill/ForgeApproach/Cave jako `PathComplete`, Camp→Goal jako `PathPartial`, EastApproach→Goal jako `PathComplete`; dno rzeki nie ma próbki `Walkable` w promieniu 0.2 m. Zapisana i ponownie otwarta scena Stage02 była czysta i nie była w Play Mode; miała 2307 obiektów i zero brakujących skryptów. Pozostało 17 odziedziczonych nierozwiązanych referencji (2 `pickaxeModelPrefab`, 15 ghost/outline). Nie sprawdzono zachowania runtime.

Archiwum rewizji zawiera kopie bazowe, raporty i podglądy. Obrazy `Before_overview`, `Before_river_detail`, `After_overview`, `After_river_detail` oraz `Technical_completed_bridge_preview.png` są lokalnymi podglądami 1536×1024; ujęcia Before odtworzono z zamrożonych danych, nie są live-capture sprzed zmian. Techniczny podgląd chwilowo pokazuje ukończony most, po czym przywrócono stan nieukończonej sceny. Nie są to deklarowane pliki commitowane.

- [Walidacja końcowej sceny](../ArtSource/TutorialTerrainStage02/RiverWidthRevision01/final-scene-validation.txt)
- [Audyt przesunięć i manifest ruchu](../ArtSource/TutorialTerrainStage02/RiverWidthRevision01/position-movement-manifest.txt)
- [Walidacja NavMesh](../ArtSource/TutorialTerrainStage02/RiverWidthRevision01/final-navmesh-validation.txt)
- [Manifest kopii bazowych](../ArtSource/TutorialTerrainStage02/RiverWidthRevision01/baseline-backup-manifest.txt)
- [Procedura remapowania Terrain i drogi](../ArtSource/TutorialTerrainStage02/RiverWidthRevision01/NativeTerrainAndRoadRemapProcedure.cs.txt), [końcowe komendy Editor/bake/podgląd](../ArtSource/TutorialTerrainStage02/RiverWidthRevision01/FinalEditorCommandBatches.cs.txt), [niezależny audyt transformacji](../ArtSource/TutorialTerrainStage02/RiverWidthRevision01/IndependentMovementReview.cs.txt) i [podsumowanie audytu](../ArtSource/TutorialTerrainStage02/RiverWidthRevision01/IndependentMovementReview.txt).

`NativeTerrainAndRoadRemapProcedure.cs.txt` zawiera procedurę remapowania z zamrożonymi wejściami BIN, a `FinalEditorCommandBatches.cs.txt` korekty, bake i render. To nie jest pełny generator rewizji: jednorazowe komendy tworzenia prefabów i przebudowy ich geometrii nie zostały zachowane. Nie wykonano testów, preflightu, buildu, Play Mode ani testu host–klient. Console zawiera poprawione diagnostyczne błędy eksploracyjnych helperów `SerializedProperty`; nie zaobserwowano błędów kompilacji projektu ani shaderów, ale Console nie była pusta.

## Ograniczenia i diagnostyka

Pozostało 17 odziedziczonych nierozwiązanych referencji: 2 pola `pickaxeModelPrefab` oraz 15 materiałów wizualizacji ReadyForMounting/factoryOutline. Nie dowodzi to zachowania runtime. Nie uruchomiono testów, preflightu, buildu ani Play Mode; nie ma dowodu poprawności multiplayer. Console odnotował błędy diagnostycznego `GetAreaCost` wywołanego na nieaktywnych agentach oraz ostrzeżenia. Tymczasowe błędy kompilacji helperów poprawiono; nie zaobserwowano błędów kompilacji projektu. Nie twierdzimy, że Console nie zawiera błędów.

Pięć nieużywanych kopii `Back*` pozostało poza sceną, katalogiem i NGO, ponieważ MCP ograniczył ich usunięcie. Procedura remapowania i końcowe komendy nie są pełnym generatorem prefabów; zachowano raporty z końcowymi ustawieniami i wynikami.

## Materiały

- [Raport weryfikacji](../ArtSource/TutorialTerrainStage02/verification-report.txt)
- [Raport rozmieszczenia](../ArtSource/TutorialTerrainStage02/placement-report.txt)
- [Raport bake NavMesh](../ArtSource/TutorialTerrainStage02/navmesh-bake-report.txt)
- [Procedura ponownego bake głównego NavMesh](../ArtSource/TutorialTerrainStage02/RebakeTutorialTerrainStage02NavMesh.cs.txt)
- Przegląd (`lokalne archiwum: ArtSource/TutorialTerrainStage02/TutorialTerrainStage02_Overview.png`), widok z góry (`lokalne archiwum: ArtSource/TutorialTerrainStage02/TutorialTerrainStage02_Top.png`), most i wykopy (`lokalne archiwum: ArtSource/TutorialTerrainStage02/TutorialTerrainStage02_BridgeAndExcavations.png`)

Przed oceną gotowości należy sprawdzić obraz sceny w Editorze, rozstrzygnąć odziedziczone brakujące referencje oraz wykonać kontrole zachowania gracza, NPC, wody i sesji host-klient.

## Pliki lokalnych eksperymentów — 2026-10-08

Nowe pliki w `ArtSource/` są domyślnie lokalne i ignorowane przez Git. Nowy raport lub skrypt potrzebny w repozytorium należy świadomie dodać przez `git add -f` po przeglądzie.

Nowe foldery w `Assets/Experiments/` są domyślnie ignorowane. Wyjątki obejmują `ArtStyleStage22`, `TutorialTerrainStage01` i `TutorialTerrainStage02` wraz z plikami `.meta` tych folderów. Nieużywane warianty `*ShortBack*` w `TutorialTerrainStage02/BridgeWidthRevision/` są ignorowane. Przyszłe lokalne próby można zapisywać w `Assets/LocalExperiments/`; katalogu nie tworzono podczas tej zmiany.

Pliki już śledzone lub staged pozostają w Git. Reguły nie przestają ich śledzić, nie ukrywają późniejszych zmian ani nie usuwają historii. Nie usuwano ani nie przenoszono plików i nie ustawiano `skip-worktree` ani `assume-unchanged`. Assety potrzebne scenie pozostają zachowane.
