# Tutorial Terrain Stage02 — migracja gameplayu

## Status

W repozytorium znajdują się scena Stage02, jej bazowe assety, ta dokumentacja oraz wymienione niżej wybrane raporty i skrypty tekstowe. Rendery, podglądy i prototypy pozostają w lokalnym archiwum i nie są commitowane.

Migracja sceny eksperymentalnej została zapisana i niezależnie skontrolowana w Editorze. Nadaje się do dalszej oceny i pracy w Editorze; nie jest potwierdzona jako gotowa runtime ani multiplayer.

## Własność i wejścia

- Scena Unity: [TutorialTerrainStage02.unity](../Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity)

- Scena: `Assets/Experiments/TutorialTerrainStage02/Scenes/TutorialTerrainStage02.unity`.
- Stage02 jest rozpoznawana przez `GameplaySceneRegistry`; włączony wpis Build Settings dodano na końcu listy dla bezpośredniego ładowania/restartu.
- Przycisk hosta `Tutorial` w lobby kieruje teraz do `TutorialTerrainStage02` przez `GameplaySceneRegistry.TutorialTerrainStage02SceneName`. Ładowanie odbywa się przez `NetworkManager.SceneManager.LoadScene`; Stage02 jest wpisana i włączona w Build Settings. `Tutorial_scene` pozostaje dostępna jako scena źródłowa, a FPP nie zmienia routingu. Scena bazowa Tutorial i pusta Stage01 nie zostały zmienione względem bazowych SHA-256.
- Stage02 ma własne TerrainData, materiał, dwie TerrainLayers oraz trzy osobne, zapisane dane NavMesh (główne i dwa proxy wykopów). Shader, albedo i mapa współrzędnych drogi są współdzielone ze Stage01 wyłącznie do odczytu.

## Zmiany

Projekt QualitySettings używa presetu Ultra z `useLegacyDetailDistribution=false`, czyli ustawienia authoringu rozmieszczenia Terrain Details. Stage02 TerrainData ma `detailScatterMode=CoverageMode`, zero `DetailPrototypes` i zero `TreePrototypes`; nie dodano palet ani instancji Details/Trees.

Cały gameplay tutorialu skopiowano jako scenę: gracza, UI, fabryki, most, wykopy, zasoby i systemy wody. Usunięto zastąpione stare siatki gruntu, koryta i ścieżek; propsy osadzono na Terrain. Własny Terrain zachowuje wysokości i alphamapy Stage01 oraz dwa otwory, obejmujące 7400 komórek maski. Leśne wykluczenie respawnu odwzorowuje naturalną drogę 27 obróconymi triggerami. Dolne koryto otrzymało wolumen `Not Walkable`, który zapobiega suchym trasom po bake na Terrain.

## Kontrola ręczna w Editorze

Zapisane i ponownie otwarte Stage02 zawierało 2624 GameObjects / 69 korzeni, brak brakujących skryptów i referencji między scenami oraz 72 prawidłowe, unikalne, niezerowe NGO scene hashes. Pięć tras zachodnich próbkowało się jako `PathComplete`; Camp→East było `PathPartial` i kończyło się przy brzegu, zgodnie z nieukończonym mostem. Dwa wykopy i trzy punkty dna rzeki nie próbkowały się jako `Walkable`; `WaterEntry` próbkowało się na obu brzegach. Źródłowa scena ma taki sam brak centralnej próbki `WaterSurface`.

## Rewizja oświetlenia 01 — 2026-10-06

Po zapisie i ponownym otwarciu sceny potwierdzono stałe oświetlenie Stage02. `Afternoon Sun` jest światłem kierunkowym czasu rzeczywistego o barwie (1, 0.88, 0.73), intensywności 1.15 i miękkich cieniach o sile 0.6. Ambient Trilight ma intensywność 1 oraz kolory nieba (0.58, 0.62, 0.72), horyzontu (0.42, 0.46, 0.55) i gruntu (0.28, 0.30, 0.38). Scena zachowuje własny proceduralny skybox, mgła jest wyłączona, a własny profil Volume używa ACES, ekspozycji +0.15, kontrastu +7, nasycenia -12 i filtra bieli.

Wyłączono `EnvironmentLightingController` oraz pięć lamp akcentujących. Odziedziczone APV, reflection probes i light probes są wyłączone; renderery nie używają probes ani lightmap, a scena nie ma przypisanego `LightingDataAsset` ani lightmap. Stage02 ma własne `LightingSettings` z `autoGenerate=false`, bez automatycznego bake. Gameplay, geometria i dane NavMesh zachowano; w porównaniu przed/po jedyną zatwierdzoną różnicą transformacji jest obrót słońca. W dniu tej rewizji, 2026-10-06, lobby nadal kierowało do `Tutorial_scene`; routing zmieniono 2026-10-07.

Po ponownym otwarciu maska Volume `MainCamera` wynosiła 257; pozostałe kamery zachowały maskę 256. Cztery obrazy przed/po (przegląd i detal) są renderami Unity URP w rozdzielczości 1536×1024. Nie wykonano testów, preflightu, buildu, Play Mode ani bake. Nie zaobserwowano błędów kompilacji C# ani shaderów w migawce Console, która zawierała wcześniejsze błędy przechwytywania i ostrzeżenia.

- [Raport rewizji oświetlenia](../ArtSource/TutorialTerrainStage02/LightingRevision01/LightingRevision01_Report.txt)
- Obrazy: przegląd przed (`lokalne archiwum: ArtSource/TutorialTerrainStage02/LightingRevision01/Lighting_Before_Overview.png`), przegląd po (`lokalne archiwum: ArtSource/TutorialTerrainStage02/LightingRevision01/Lighting_After_Overview.png`), detal przed (`lokalne archiwum: ArtSource/TutorialTerrainStage02/LightingRevision01/Lighting_Before_Detail.png`), detal po (`lokalne archiwum: ArtSource/TutorialTerrainStage02/LightingRevision01/Lighting_After_Detail.png`).

## Routing lobby do Stage02 — 2026-10-07

Przycisk hosta `Tutorial` w `MultiplayerStartScene` ładuje teraz scenę `TutorialTerrainStage02` przez `GameplaySceneRegistry.TutorialTerrainStage02SceneName` i `NetworkManager.SceneManager.LoadScene`. Zmiana sceny NGO jest wywoływana wyłącznie przez hosta i synchronizuje klientów. Stage02 jest wpisana i włączona w Build Settings. Editor odświeżył i skompilował projekt bez błędów C# po tej zmianie. Nie wykonano testów, preflightu, buildu, Play Mode ani weryfikacji sesji host-klient; routing multiplayer nie został sprawdzony w runtime.

## Ograniczenia i diagnostyka

Pozostało 17 odziedziczonych nierozwiązanych referencji: 2 pola `pickaxeModelPrefab` oraz 15 materiałów wizualizacji ReadyForMounting/factoryOutline. Nie dowodzi to zachowania runtime. Nie uruchomiono testów, preflightu, buildu ani Play Mode; nie ma dowodu poprawności multiplayer. Console odnotował błędy diagnostycznego `GetAreaCost` wywołanego na nieaktywnych agentach oraz ostrzeżenia. Tymczasowe błędy kompilacji helperów poprawiono; nie zaobserwowano błędów kompilacji projektu. Nie twierdzimy, że Console nie zawiera błędów.

Niezreferencjonowany `TutorialTerrainStage02_Main_NavMeshData_Rebaked.asset` i `.meta` pozostały, ponieważ Unity MCP odrzucił usunięcie pliku jako „User interactions are not supported for MCP tool calls”. Skan zależności potwierdził, że scena nie używa tego artefaktu. Procedura `.cs.txt` służy wyłącznie do ponownego bake głównego NavMesh i nie jest generatorem pełnej migracji.

## Materiały

- [Raport weryfikacji](../ArtSource/TutorialTerrainStage02/verification-report.txt)
- [Raport rozmieszczenia](../ArtSource/TutorialTerrainStage02/placement-report.txt)
- [Raport bake NavMesh](../ArtSource/TutorialTerrainStage02/navmesh-bake-report.txt)
- [Procedura ponownego bake głównego NavMesh](../ArtSource/TutorialTerrainStage02/RebakeTutorialTerrainStage02NavMesh.cs.txt)
- Przegląd (`lokalne archiwum: ArtSource/TutorialTerrainStage02/TutorialTerrainStage02_Overview.png`), widok z góry (`lokalne archiwum: ArtSource/TutorialTerrainStage02/TutorialTerrainStage02_Top.png`), most i wykopy (`lokalne archiwum: ArtSource/TutorialTerrainStage02/TutorialTerrainStage02_BridgeAndExcavations.png`)

Przed oceną gotowości należy sprawdzić obraz sceny w Editorze, rozstrzygnąć odziedziczone brakujące referencje oraz wykonać kontrole zachowania gracza, NPC, wody i sesji host-klient.