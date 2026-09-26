# Terrain Road V3 — wdrożenie i obsługa

Data: 2026-09-26

## Aktualizacja — 2026-09-26

Opis V3 poniżej jest historycznym punktem powrotu. Aktualna scena używa TD_TerrainRoad_RoadsideV1.asset i nowej warstwy darni; szczegóły bieżącego wdrożenia opisuje [Pobocze V1](Roadside-V1-Implementation.md). Wyniki V3 pozostają zachowane bez przepisywania.

## Rezultat

V3 to prototyp wyglądu drogi w scenie `Assets/Scenes/TerrainRoad_Lookdev.unity`. Zastosowano standardowy URP Terrain Lit i wypieczone mapy materiału. Nie dodano geometrii drogi ani paralaksy POM. Oświetlenie, post-processing, trawa, współdzielone prefabrykaty i scena `Tutorial_scene` pozostały bez zmian. Współdzielony prefab gracza nie był edytowany; instancja gracza w scenie lookdev zaczyna teraz przy XZ `(14.15, 4.3)`.

Źródłem jest okresowy model 4 m przygotowany w Blenderze 5.1.2: ubita ziemia z nieregularnymi, płaskimi odłamkami kamienia, głównie 10–25 cm. Udział powierzchni kamiennej w pomiarze źródła wynosi 37,18%. Pliki źródłowe do edycji to `ArtSource/TerrainRoadV3/TerrainRoadV3.blend` i `ArtSource/TerrainRoadV3/generate_terrain_road_v3.py`.

## Materiały i mapy

W `Assets/Art/Environment/TerrainRoadLookdev/` znajdują się:

- `SurfaceV3/` — dziewięć map 2048 px: trzy palety albedo bez oświetlenia, normalne styczne A/B, wysokości A/B jako liniowe EXR float32, wspólna łagodna mapa AO oraz wspólna maska RGBA (metaliczność 0, AO, wysokość B, gładkość 0).
- `Materials/M_TerrainRoad_TerrainLit_V3.mat` — materiał Terrain Lit.
- `TerrainLayers/TL_Road_ReliefV3.terrainlayer`, `TL_Stone_Grey_ReliefV3.terrainlayer`, `TL_Earth_Dark_ReliefV3.terrainlayer` — warstwy V3.
- `TerrainData/TD_TerrainRoad_ReliefV3.asset` — TerrainData sceny lookdev.

Wariant A ma dokładnie połowę źródłowej wysokości Z wariantu B; B odpowiada około 2–4 cm reliefu w skali źródła 4 m. Oba warianty zmieniają wyłącznie wygląd. Skala normalnych wynosi 1. Trzy warstwy drogi używają wspólnej normalnej i maski, UV 4 m i zerowego offsetu. Height Blend jest wyłączony. Import tekstur: albedo jako sRGB, normalne jako Normal Map, pozostałe dane liniowe; Repeat, mipmapy, Trilinear, anizotropia 8.

Makrowysokość V3 wyliczono jako `hV3 = hBase + 0.5 * (hV2 - hBase)`: zmniejszono o połowę wyłącznie wkład stemplowania względem bazowego terenu, zachowując bazowe wzniesienie. Maksymalny błąd numeryczny wysokości to 0,0000611 m (kwantyzacja); poza stałą maską drogi delta wynosi 0. Wagi alphamap V2 i V3 są identyczne. V2 pozostaje zachowane, a trawa nie została zmieniona.

## Przełączanie wariantów

Otwórz `Assets/Scenes/TerrainRoad_Lookdev.unity`, uruchom Play i kliknij Game view. F6 wybiera A (płytszy relief), F7 wybiera B (wyraźniejszy relief i wariant domyślny). Ekranowy opis pokazuje aktywny wariant.

`Assets/Scripts/Lookdev/TerrainRoadV3/TerrainRoadV3RuntimeSwitcher.cs` wymaga Terrain i TerrainCollider. Przy włączeniu tworzy kopie TerrainData oraz trzech warstw; przy wyłączeniu lub końcu Play przywraca oryginalne referencje i usuwa kopie. Zmieniają się tylko normalne warstw. Wysokości terenu, collider i pozycje kolorów pozostają stałe.

Pędzel drogi pozostaje dotychczasowy: `Window/Terrain/Road Relief Brush`. Do ręcznego użycia przypisz teren lookdev, nowe warstwy kamienia i ciemnej ziemi V3 oraz oryginalną, czytelną maskę ograniczającą stały pas drogi. Menu importu jest nadal przeznaczone dla V2; nie używaj go jako importera V3.

## Ponowne generowanie wypieków

Z katalogu projektu uruchom Blender 5.1.2:

```powershell
& 'D:\Programy\Blender\blender.exe' --background --python 'D:\Programy\UnityProjects\RageQuitting\ArtSource\TerrainRoadV3\generate_terrain_road_v3.py'
```

Generator korzysta z bezwzględnej ścieżki projektu `D:\Programy\UnityProjects\RageQuitting` i zapisuje wypieki do `Assets/Art/Environment/TerrainRoadLookdev/SurfaceV3`; raporty trafiają do `Artifacts/TerrainRoadV3`. Ponowne uruchomienie nadpisuje wygenerowane pliki. Zachowaj osobno zmiany w źródłowym pliku `.blend` i skrypcie generatora. Pomiar wypieków wykonuje `ArtSource/TerrainRoadV3/measure_terrain_road_v3_bakes.py`.

## Walidacja i ograniczenia

Końcowy, ścisły preflight Gameplay bez filtrów uruchomiono poleceniem `.\Tools\AgentHarness\Invoke-UnityPreflight.ps1 -Tier Gameplay -Json`. Przebieg `20260926T115740Z-6bc8dbad` nie powiódł się wyłącznie na UNT0039; wymaganie naprawiono przez `RequireComponent`, bez wyciszania błędów bazowych. Końcowy przebieg `20260926T120619Z-7a2d624a` przeszedł: exit code 0, 14/14 kroków, 54/54 testów EditMode i 15/15 PlayMode; kroki niezaliczone: brak. `summary.json` zawiera `warnings=[]`. Po pełnej serii natywny Unity Console miał 0 errors, 76 warnings i 530 logs. Raport: `Artifacts/Validation/20260926T120619Z-7a2d624a/summary.json`. Raporty pilota są w `Artifacts/TerrainRoadV3/`, w tym metryki wypieków, walidacja integracji i test własności kopii runtime. Skróty źródeł V3 pozostały niezmienione po testach runtime i końcowym preflighcie.

Krótki test ruchu kontrolowanego przeszedł 25,722 m i 146 bezpośrednich kroków `CharacterController.Move`, z 8/8 trafieniami promieni i bez blokad bocznych ani błędów runtime. To nie jest pełny spacer WASD. Pierwsza próba wejścia próbkowała bufory InputSystem w callbacku edytora, a nie w PlayerLoop, i nie jest miarodajna; poprawiona próba z coroutine w rzeczywistej pętli PlayerLoop potwierdziła F6/F7 oraz wejście W. Raportem referencyjnym jest `TerrainRoadV3_InputProbe_Attempt2.json`; ogólny `TerrainRoadV3_InputProbe.json` to starsza, nieudana próba.

Widoczne przy bliskiej kamerze drobne regularne schodki/ząbki na krawędzi pochodzą z siatki 512 i triangulacji źródłowego wypieku w Blenderze, a nie z siatki Unity Terrain 257. Mapowanie normalnych nie zmienia mikro-sylwetki ani kolizji. Testy statyczne i krótka próba ruchu nie rozstrzygają w pełni migotania czasowego, zachowania LOD, jakości artystycznej ani zgodności z referencją; końcowy spacer i ocena należą do użytkownika. Nie wykonano benchmarku na GTX 1650 przy 1080p/60 FPS; testy odbyły się na RTX 5070 Laptop. Oświetlenie sceny pozostało bez zmian.

