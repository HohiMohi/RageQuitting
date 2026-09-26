# Pobocze V1 — wdrożenie i obsługa

Data: 2026-09-26  
Projekt: `D:\Programy\UnityProjects\RageQuitting` · Unity `6000.3.18f1` · Blender `5.1.2`  
Scena: `Assets/Scenes/TerrainRoad_Lookdev.unity`

## Jak obejrzeć

Otwórz Assets/Scenes/TerrainRoad_Lookdev.unity, uruchom Play i kliknij Game view. Podejdź do środkowego zakrętu (Z 9–19). Domyślnie aktywny jest wariant B; F6 przełącza na A, a F7 na B.

## Zakres i rezultat

W środkowym zakręcie dodano około 10 m pobocza (Z 9–19): 50 statycznych kęp trawy w trzech rozmiarach (około 0,15/0,30/0,45 m) i 7 osadzonych skał w trzech rozmiarach (około 0,3/0,6/1 m). Trawa jest Opaque po obu stronach i rzuca cienie. Siatki nie mają Colliderów ani Rigidbody. Zasoby używają sześciu prefabów i dwóch wspólnych materiałów URP Lit.

Nowa darń jest globalną warstwą terenu: tekstury 2048² albedo, normalnej i maski Terrain Lit, kafel 4 m. Maska koduje metaliczność 0, AO, wysokość i gładkość 0. Atlas trawy i mapy skał mają 1024². Wagi zmieniły się w 900 komórkach spośród 924 komórek maski; poza nią i w pasie drogi (odległość do osi poniżej 1 m) zmiana wynosi 0. Wysokość terenu nie zmieniła się (delta 0); Terrain i TerrainCollider wskazują to samo `TD_TerrainRoad_RoadsideV1.asset`.

Droga V3 i jej trzy warstwy zostały zachowane bez zmian. Domyślnie aktywny pozostaje wariant B; przełączanie F6/F7 należy do istniejącego systemu V3. Pięć nakładających się starych obiektów wyłączono. Pozostała scena i współdzielone zasoby nie zostały zmienione.

## Zasoby i źródła

Nowe zasoby Unity są pod `Assets/Art/Environment/TerrainRoadLookdev/RoadsideV1/`: `Models/` (sześć FBX), `Prefabs/`, `Materials/`, `TerrainData/TD_TerrainRoad_RoadsideV1.asset`, `TerrainData/TL_RoadsideV1_Grass.terrainlayer` oraz sześć PNG. Źródłowy model Blender, generator i skrypty integracyjne znajdują się w `ArtSource/RoadsideV1/`: `RoadsideV1.blend`, `generate_roadside_v1.py`, `BuildRoadsideAssets.cs`, `ApplyRoadsideTerrain.cs`, `IntegrateRoadsideScene.cs`.

Import PNG jest zapisany w `.meta`: albedo sRGB, normalne jako NormalMap, mapy danych liniowe; Repeat, mipmapy, Trilinear, anizotropia 8. Zachowaj te ustawienia przy odbudowie zasobów.

## Odtworzenie

Generator Blendera nadpisuje własne wygenerowane pliki. Przed uruchomieniem zachowaj osobno lokalne edycje `.blend` i wygenerowanych zasobów.

```powershell
& 'D:\Programy\Blender\blender.exe' --factory-startup --background --python-exit-code 1 --python 'D:\Programy\UnityProjects\RageQuitting\ArtSource\RoadsideV1\generate_roadside_v1.py'
```

Kroki Unity wykonuj przez połączony Editor i istniejące skrypty `BuildRoadsideAssets.cs`, `ApplyRoadsideTerrain.cs`, `IntegrateRoadsideScene.cs`; nie ma tu osobnego, potwierdzonego importera. CLI jest pod `C:\Users\dunia\AppData\Local\Unity\bin\unity.exe`; uruchamiaj polecenia z projektu D:. Integracja zmienia aktywną scenę, więc przed wykonaniem sprawdź właściwy Editor/scenę i zapisz potrzebny stan.

Lista pięciu wyłączonych obiektów jest w `IntegrateRoadsideScene.cs` jako wywołania `DisableAt` z nazwą i pozycją XZ. Dopasowanie pozycji jest istotne, bo w scenie występują powtarzające się nazwy.

## Rollback

W Editorze wyłącz root `Roadside_V1`, przywróć Terrain oraz TerrainCollider do `Assets/Art/Environment/TerrainRoadLookdev/TerrainData/TD_TerrainRoad_ReliefV3.asset`, po czym włącz dokładnie pięć starych obiektów z listy `DisableAt` w `IntegrateRoadsideScene.cs`. Nie usuwaj nowych zasobów; rollback sceny nie wymaga ich kasowania. Zachowaj wspólną referencję Terrain/TerrainCollider do tego samego TerrainData.

## Walidacja

Walidacja sceny potwierdziła 50 kęp, 7 skał, prawidłowy obszar Z, brak Colliderów/Rigidbody, brak zmian wysokości i brak zmian wag poza maską i w około dwumetrowym środku drogi. Minimalna odległość wierzchołków pobocza od osi drogi wyniosła 1,515 m. Scena była czysta w Edit Mode. FBX źródłowe i kopie Unity miały zgodne SHA-256; odczyt importu potwierdził ustawienia tekstur i siatek.

Końcowy ścisły, niefiltrowany Gameplay preflight wykonał główny agent: exit 0, 14/14 kroków, 54/54 EditMode i 15/15 PlayMode; bez kroków niezaliczonych, pominiętych ani niejednoznacznych; `warnings=[]` w `summary.json`. Po pierwszym preflighcie natywny Unity Console raportował 0 errors, 76 warnings i 530 logs. Szczegóły: `Artifacts/Validation/20260926T135444Z-7624bfa0/summary.json`.

Dwie próby po 29 klatek z wejściem próbkowanym w PlayerLoop potwierdziły F6/F7 oraz ruch W i ruch kamery. Kontrolowana syntetyczna trasa CharacterController przeszła 25,722 m (146 kroków, 8/8 trafień promieni, bez błędów i kolizji bocznych); nie jest to pełny spacer WASD. Ponowne wejście do Play Mode potwierdziło domyślny wariant B i brak wycieków kopii TerrainData/warstw. W iteracji poprawiono rozbieżność identycznych kadrów przez jawne ustawienie transformacji kamery. Pomocniczy walidator miał odwrócone etykiety logs/errors; po poprawce potwierdzono 0 errors, 76 warnings i 530 logs w natywnym Console po pierwszym preflighcie — nie była to regresja gry. Historyczny timeout transportu importu pozostał zapisany w `ConsoleDiagnostics.json`; nie jest aktualnym błędem runtime.

## Ograniczenia

Oględziny sceny były statyczne, z bliska i pod kątem; nie stanowią akceptacji artystycznej. Migotanie czasowe i naturalny spacer wymagają oceny użytkownika. Nie zmierzono wydajności na GTX 1650 przy 1080p/60 FPS; walidacja odbyła się na RTX 5070 Laptop. Nie dodano runtime, shaderów, pakietów, LOD, wiatru ani systemu scatter. Schodki wypieku drogi V3 pozostają odłożone.

Główne raporty wdrożenia: `Artifacts/RoadsideV1/SceneValidation.json`, `AssetValidation.json`, `UnityImportValidation.txt`, `RoadsideV1_InputProbe_Attempt1.json`, `RoadsideV1_InputProbe_Attempt2.json`, `RoadsideV1_RuntimeReentry.json`, `RoadsideV1_SyntheticTraversal.json` oraz historyczne `ConsoleDiagnostics.json`.
