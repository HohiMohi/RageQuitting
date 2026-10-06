# Etap 01 — kształt terenu tutorialowego

Data: 2026-10-06  
Branch: `ArtStyleTest` · Unity `6000.3.18f1` · URP `17.3`

## Cel i wynik

W repozytorium znajdują się ta dokumentacja oraz wybrane małe raporty i skrypty tekstowe. Katalog Stage01 jest częściowo commitowany: współdzielone shadery, mapa współrzędnych i albedo są w repozytorium. Sama scena Stage01, TerrainData, Volume oraz jej kopie materiału i warstw, renderowane podglądy i prototypy pozostają w lokalnym archiwum.

Przygotowano osobną scenę eksperymentalną z natywnym Terrain do dalszej integracji terenu tutorialowego. Oryginalna scena `Tutorial_scene` pozostała niezmieniona. Scena etapu jest aktywna i zawiera jeden Terrain z `TerrainCollider`; nie dodano dekoracji, prototypów ani instancji Terrain Details/Trees.

Terrain zajmuje obszar świata X `[-50, 40]`, Z `[-45, 45]`. Bazowa płaszczyzna gruntu wynosi Y `0`; łagodne nierówności poza korytarzem drogi mieszczą się w przyjętym zakresie około ±1 m. Przez środek przechodzi obniżone koryto rzeki, a dwa wykopy mają otwarte otwory Terrain. Scena używa samych otworów Terrain — nie zawiera dna ani ścian wykopów.

## Parametry Terrain

| Właściwość | Wartość |
|---|---:|
| Pozycja Terrain | `(-50, -4, -45)` |
| Rozmiar Terrain | `(90, 8, 90) m` |
| TerrainData size | `(90, 8, 90) m` |
| Granice świata X / Z | `[-50, 40]` / `[-45, 45] m` |
| Bazowy poziom gruntu | `Y = 0 m` |
| Heightmap / alphamap / holes | `1025 / 1024 / 1024` |
| Prototypy detali / drzew | `0 / 0` |

## Ukształtowanie i historia drogi

Rzeka biegnie wzdłuż osi Z; jej przekrój opisuje profil w osi X. Brzegi łączą poziom bazowy z dnem na głębokości `3 m`; płaskie dno zajmuje X `[6.5, 9.5]`. Próbki dla X `4`, `8` i `12` wynosiły odpowiednio około `-1.333335`, `-2.99994` i `-1.333251 m`.

Pierwszy etap używał łagodnych nierówności Perlin z seedem `2301` (skale `18 m` i `9 m`, wagi `0.8` i `0.2`). Jego prostokątny korytarz drogi oraz światowe UV wspólne dla tekstur drogi i gleby należą do historycznego stanu początkowego. RoadRevision01 odtworzył pofałdowanie w miejscu starego korytarza w swoim wyznaczonym zakresie, zachowując koryto, wykopy i strefy chronione. Poza obszarem starej i nowej drogi wraz z pasami przejścia teren zachowuje wcześniejsze ukształtowanie.

## Otwory wykopów — stan początkowy

Poniższe wymiary dokumentują początkowy prostokątny wariant otworów; RoadRevision01 pozostawił otwory i ich próbki bez zmian. Ich granice zaokrąglono do granic komórek siatki o kroku `0.08789063 m`. Pomiar początkowego dopasowania krawędzi do geometrii źródłowej wykazał maksymalną rozbieżność `0.03125 m`.

| Otwór | Zakres X | Wspólny zakres Z |
|---|---:|---:|
| West | `[-4.472656, 2.03125] m` | `[-2.197266, 2.197266] m` |
| East | `[13.98438, 20.48828] m` | `[-2.197266, 2.197266] m` |

Każdy otwór początkowo zajmował `3700` komórek. Raycasty nadal nie trafiają w środek obu otworów, a trafiają w dno i skarpę rzeki.

## Warstwy, światło i import tekstur

Scena używa dwóch natywnych Terrain Layers: drogi i gleby. Gleba zachowuje teksturowanie world-space z częstotliwością `0.15` powtórzenia na metr. Droga ma własne, zakodowane współrzędne w metrach opisane w sekcji RoadRevision01. Warstwy pozostają malowalne w natywnym Terrain; malowanie zmienia pokrycie warstwą drogi/gleby, nie kierunek ani oś tekstury drogi.

Skopiowane albedo mają import `Default`, `None`, sRGB, Repeat, mipmapy włączone i maksymalny rozmiar `2048`; format importu to RGB24, bo źródła nie mają kanału alpha. Ustawienie kompresji platformy Standalone pozostało Automatic.

Światło, ambient i Volume skopiowano z tutorialu. Aktywny Volume korzysta z własnej kopii profilu. Oświetlenie zachowuje źródłową, ciepłą paletę; gradingu nie korygowano.

## Weryfikacja pierwotnego etapu

Początkowa weryfikacja odbyła się przez odczyt sceny i pomiary w Editorze. Cztery przypadki dopasowania geometrii `FoundationExcavation` wyrenderowano i sprawdzono raycastami: oba wykopy w położeniu początkowym oraz po dynamicznym opuszczeniu powierzchni o `1.2 m`. W tych próbach promień Terrain nie trafiał w otwór, a collider skopiowanej powierzchni dynamicznej trafiał na Y `+0.08 m` początkowo i `-1.12 m` po obniżeniu. Te obrazy i wyniki należą do pierwszego etapu.

## RoadRevision01 — 2026-10-06

Droga została wygenerowana ponownie in-place w `Assets/Experiments/TutorialTerrainStage01`, z niezmienną kopią stanu sprzed poprawki jako źródłem. Kopia BEFORE obejmuje scenę, TerrainData, materiał, warstwy, albedo i ich metadane oraz dwa podglądy; `native-terrain-state.bin` przechowuje pierwotny stan TerrainData. Materiały źródłowe pierwotnej sceny `Tutorial_scene`, Stage17, koryto, otwory, albedo, światło i grading pozostały bez zmian.

### Przebieg i kształt

Oś West jest krzywą centripetal Catmull–Rom (`alpha = 0.5`) resamplowaną co `0.1 m`: `(-15,-22)`, `(-19,-17)`, `(-24,-10)`, `(-21,-4)`, `(-14,-2)`, `(-9,0)`, `(-7.5,0)`, `(-4.5,0)`. Długość wynosi `39.284 m`, liczba próbek `394`.

Oś East: `(20.5,0)`, `(23,0)`, `(26,0)`, `(27.5,0)`, `(29.5,0.45)`, `(32.1,0)`. Długość wynosi `11.702 m`, liczba próbek `119`.

Szerokość drogi wynosi `2.8–3.2 m` (seed `2302`, niezależne szumy obu krawędzi), z szerokością dokładnie `3 m` przy wykopach i środku gate `(26,0)`. Krawędź warstwy drogi wygasa na `0.25 m`. Płaska droga ma pobocza po `1 m` i przechodzi w teren na odcinku `3 m`; przywrócono poprzedni corridor relief, a chronione strefy zachowano. Odczyt pełnej alfa przy gate wynosi `2.988 m` przy kroku siatki `0.08789 m`; jest to wynik dyskretyzacji, nie naruszenie docelowej szerokości `3 m`.

W regeneracji zmieniono `50 872` próbki heightmapy, z maksymalną bezwzględną zmianą `0.4949036 m`. Koryto rzeki i otwory nie zmieniły się względem BEFORE (`riverChanges=0`, `holesChanges=0`); dziewięć chronionych punktów pozostało na Y `0`. Scena po zapisie i ponownym otwarciu ma jedną załadowaną scenę, siedem rootów i brak obiektów pomocniczych; prototypy i instancje Trees/Details wynoszą zero.

### Współrzędne drogi i teksturowanie

Własne shadery `TutorialTerrainStage01Terrain.shader` i `TutorialTerrainStage01TerrainBase.shader` korzystają z `TutorialTerrainStage01TerrainLitInput.hlsl` oraz `TutorialTerrainStage01TerrainLitPasses.hlsl`; kod oświetlenia jest identyczny z Stage17. Asset `TutorialTerrainRoadCoordinates.asset` to tekstura natywna `1024×1024`, `RGFloat` (`R32G32_SFloat`), linear, Clamp, Bilinear, bez mipmap. Kanały przechowują per-fragment odległość wzdłuż drogi `s` i signed distance `d` w metrach. Shader skaluje współrzędne przez `0.25` i obraca je o `+45°`. Gleba zachowuje world UV `0.15`.

Pozostałe dwie Terrain Layers są natywnie malowalne. Próba przez `TerrainData.SetAlphamaps` na tymczasowej kopii potwierdziła zmianę pokrycia road/soil z `1/0` na `0/1`; odtworzenie miało deltę `0`, kopię zniszczono, a zapisane maski nie zmieniły się. To potwierdza API malowania, nie próbę ręcznego malowania pędzlem w GUI.

Duża zmiana trasy wymaga aktualizacji punktów oraz skryptów `PrepareTutorialTerrainRoad.cs.txt` i `BakeTutorialTerrainRoadCoordinates.cs.txt`; baker nie działa w runtime. Dołączone skrypty są źródłami authoringu osi i mapy, nie generatorem pełnej sceny Stage02. Baker ma zakodowaną ścieżkę i wymaga aktywnej sceny Stage01; przy braku tej sceny w repozytorium jego ponowne użycie wymaga lokalnego archiwum albo dostosowania skryptu. `BasemapDistance=1000 m` utrzymuje poprawne mapowanie całej sceny, ale basemap poza tą odległością nie ma nowego mapowania. Skrypt `CaptureTutorialTerrainRoadRevision01.cs.txt` zapisuje podglądy.

### Kontrole i ograniczenia

Po ponownym otwarciu sceny główny agent niezależnie potwierdził collider: brak trafień w obu pitach oraz trafienia w dno rzeki `Y=-2.99994 m`, skarpę `Y=-1.333335 m` i drogę `Y=0`. Odczyty światła, ambient, Volume i czterech kamer były identyczne z BEFORE. SHA-256 hash 134 chronionych plików — w tym Tutorial_scene, Stage17, Packages, ProjectSettings oraz własnych albedo/layers/Volume i metadanych — pozostał bez zmian. Shader zwrócił `isSupported=true` i `ShaderUtil.GetShaderMessages=0`. Zaimportowano zmieniony plik `.shader` i dołączony plik `.hlsl`; następnie wykonano dodatkowe `Refresh`, które nie wprowadziło zmian w assetach.

Wszystkie wcześniejsze błędy geometrii, maski i UV oraz zaokrąglenia pomiarów zostały poprawione przed przyjęciem. Nie uruchamiano formalnych testów automatycznych, preflightu, builda, Play Mode ani bake'u NavMesh — są wyłączone decyzją użytkownika. Weryfikacja opisana powyżej obejmuje ręczne odczyty danych i Editor API; nie wykonywano formalnych testów ani ręcznego malowania pędzlem w GUI.

## Pliki i podglądy

- Scena: TutorialTerrainStage01.unity (`lokalne archiwum: Assets/Experiments/TutorialTerrainStage01/Scenes/TutorialTerrainStage01.unity`)
- Assety sceny: sama scena, TerrainData, Volume oraz kopie materiału i TerrainLayers są w lokalnym archiwum; współdzielone shadery, mapa współrzędnych i albedo są commitowane.
- Stan BEFORE i podglądy początkowe: RoadRevision01/Before (`lokalne archiwum: ArtSource/TutorialTerrainStage01/RoadRevision01/Before`)
- Przegląd początkowego terenu: TutorialTerrain_Overview.png (`lokalne archiwum: ArtSource/TutorialTerrainStage01/TutorialTerrain_Overview.png`)
- Widok początkowego terenu z góry: TutorialTerrain_Top.png (`lokalne archiwum: ArtSource/TutorialTerrainStage01/TutorialTerrain_Top.png`)
- Pierwotny widok rzeki i wykopów: TutorialTerrain_RiverAndExcavations.png (`lokalne archiwum: ArtSource/TutorialTerrainStage01/TutorialTerrain_RiverAndExcavations.png`)
- Początkowe dopasowanie wykopów: TutorialTerrain_ExcavationFit.png (`lokalne archiwum: ArtSource/TutorialTerrainStage01/TutorialTerrain_ExcavationFit.png`) — cztery historyczne przypadki.
- Roboczy mieszany wariant głębokości: TutorialTerrain_ExcavationFit_MixedDepth.png (`lokalne archiwum: ArtSource/TutorialTerrainStage01/TutorialTerrain_ExcavationFit_MixedDepth.png`) — nie jest podglądem finalnym.
- Nowy przegląd: Overview_Final.png (`lokalne archiwum: ArtSource/TutorialTerrainStage01/RoadRevision01/Overview_Final.png`) (`1536×1024`)
- Nowy widok z góry: Top_Final.png (`lokalne archiwum: ArtSource/TutorialTerrainStage01/RoadRevision01/Top_Final.png`) (`1536×1024`)
- Nowy widok zakrętu: Bend_Final.png (`lokalne archiwum: ArtSource/TutorialTerrainStage01/RoadRevision01/Bend_Final.png`) (`1536×1024`)
- Mapa współrzędnych: [TutorialTerrainRoadCoordinates.asset](../Assets/Experiments/TutorialTerrainStage01/TutorialTerrainRoadCoordinates.asset)
- Shader terenu: [TutorialTerrainStage01Terrain.shader](../Assets/Experiments/TutorialTerrainStage01/TutorialTerrainStage01Terrain.shader)
- Shader basemap: [TutorialTerrainStage01TerrainBase.shader](../Assets/Experiments/TutorialTerrainStage01/TutorialTerrainStage01TerrainBase.shader)
- Input HLSL: [TutorialTerrainStage01TerrainLitInput.hlsl](../Assets/Experiments/TutorialTerrainStage01/TutorialTerrainStage01TerrainLitInput.hlsl)
- Passes HLSL: [TutorialTerrainStage01TerrainLitPasses.hlsl](../Assets/Experiments/TutorialTerrainStage01/TutorialTerrainStage01TerrainLitPasses.hlsl)
- Oświetlenie HLSL: [TutorialTerrainStage01TerrainLighting.hlsl](../Assets/Experiments/TutorialTerrainStage01/TutorialTerrainStage01TerrainLighting.hlsl)
- Przygotowanie drogi: [PrepareTutorialTerrainRoad.cs.txt](../ArtSource/TutorialTerrainStage01/RoadRevision01/PrepareTutorialTerrainRoad.cs.txt)
- Baker współrzędnych: [BakeTutorialTerrainRoadCoordinates.cs.txt](../ArtSource/TutorialTerrainStage01/RoadRevision01/BakeTutorialTerrainRoadCoordinates.cs.txt)
- Skrypt podglądów i kontroli: CaptureTutorialTerrainRoadRevision01.cs.txt (`lokalne archiwum: ArtSource/TutorialTerrainStage01/RoadRevision01/CaptureTutorialTerrainRoadRevision01.cs.txt`)
- Raport geometrii: [geometry-report.txt](../ArtSource/TutorialTerrainStage01/RoadRevision01/geometry-report.txt)
- Raport sceny i renderu: [final-render-report.txt](../ArtSource/TutorialTerrainStage01/RoadRevision01/final-render-report.txt)
- Pierwotny raport weryfikacji: verification-report.txt (`lokalne archiwum: ArtSource/TutorialTerrainStage01/verification-report.txt`)
- Pierwotne pomiary authoringowe: authoring-measurements.txt (`lokalne archiwum: ArtSource/TutorialTerrainStage01/authoring-measurements.txt`)

## Następny etap

Kolejny etap obejmuje integrację Terrain z działającą sceną, przeniesienie i osadzenie obiektów, dopasowanie systemu kopania oraz aktualizację NavMesh. Te prace nie należą do wyniku tego etapu.

