# Terrain Road V4 — wdrożenie i obsługa

Data: 2026-09-26  
Projekt: `D:\Programy\UnityProjects\RageQuitting` · Unity `6000.3.18f1` · Blender `5.1.2`  
Scena: `Assets/Scenes/TerrainRoad_Lookdev.unity`

## Aktualizacja — 2026-09-26

Nowszy, poprawiony wariant materiału F8 opisuje [Terrain Road V4.1](TerrainRoad-V4_1-Implementation.md). Ten dokument zachowuje historię i parametry starszego V4; jego źródła i zasoby pozostały niezmienione.

## Rezultat i zakres

V4 dodaje trzeci wariant wyglądu drogi do istniejącej sceny lookdev. Bazą jest edytowalny, deterministyczny kafel 4 m z Blender 5.1.2: ubita ziemia z kierunkowymi, wyszczerbionymi fragmentami o czworokątnym obrysie i mniejszymi płaskimi podziałami koloru. Większość fragmentów ma 10–25 cm, większe występują rzadziej; relief mapy wysokości odpowiada około 2–4 cm. Udział szaro-kamiennej powierzchni w pomiarze źródła wynosi 36,98%, a 84,7% fragmentów jest skierowanych w granicach ±25° względem osi Terrain Z.

Pobocze V1, trawa, warstwa darni, oświetlenie, postać, wysokości terenu, alphamapy i kolizje pozostały bez zmian. Nie dodano krzywych mapowania, POM, geometrii Unity, nowego shadera ani zmian pędzla.

## Przełączanie wariantów

W scenie `TerrainRoad_Lookdev.unity` uruchom Play i kliknij Game view: **F6** wybiera V3A, **F7** wybiera V3B (wariant domyślny), a **F8** wybiera V4. Każdy wariant używa kompletnego zestawu map i właściwości trzech warstw drogi.

Włączony przełącznik tworzy jedną kopię runtime TerrainData oraz trzy kopie TerrainLayer na sesję. Przełączanie wariantów aktualizuje kopie; wyłączenie komponentu lub koniec Play przywraca oryginalne referencje i usuwa kopie. Gdy brakuje poprawnego zestawu V4, system ostrzega jednokrotnie i pozostawia aktywny V3.

## Źródła, mapy i import

Źródła znajdują się w `ArtSource/TerrainRoadV4/`: `TerrainRoadV4.blend`, `generate_terrain_road_v4.py`, `measure_terrain_road_v4_bakes.py` oraz pomocnicze skrypty tworzenia warstw, przypisania ich do sceny, walidacji i prób runtime. Mapy są w `Assets/Art/Environment/TerrainRoadLookdev/SurfaceV4/` (2048²): trzy albedo, normalna, wysokość EXR 32-bit, AO i maska RGBA. Kanały maski: R metaliczność 0, G AO, B wysokość, A gładkość 0. Trzy szablony TerrainLayer są w `TerrainLayers/V4/`.

Warstwy mają UV 4 m, offset 0, skalę normalnej 1 i wyłączone Height Blend. Import: albedo sRGB; pozostałe tekstury liniowe, normalna jako Normal Map; Repeat, mipmapy, Trilinear, anizotropia 8, kompresja wyłączona. Unity importuje źródłowy EXR 32-bit jako RGBAHalf.

Z katalogu projektu odtwórz wypieki poleceniem zapisanym w nagłówku generatora:

```powershell
& 'D:\Programy\Blender\blender.exe' --factory-startup --background --python-exit-code 1 --python 'D:\Programy\UnityProjects\RageQuitting\ArtSource\TerrainRoadV4\generate_terrain_road_v4.py'
```

Generator używa stałej ścieżki `D:\Programy\UnityProjects\RageQuitting`, ustalonego ziarna i nadpisuje własny plik `.blend`, wygenerowane mapy oraz raporty. Przed regeneracją zachowaj ręczne zmiany w `.blend` i generatorze. `create_v4_terrain_layers.cs` służy do pierwszego utworzenia warstw i zgłasza błąd, gdy pliki już istnieją; nie uruchamiaj go ponownie dla istniejących zasobów. `assign_v4_templates_to_scene.cs` zapisuje przypisania w scenie lookdev. To pomocnicze skrypty wdrożeniowe, nie importer do codziennego użycia. CLI Unity: `C:\Users\dunia\AppData\Local\Unity\bin\unity.exe`; edycje sceny wykonuj przez połączony Editor.

## Walidacja i ograniczenia

Wypieki sprawdzono pod kątem zgodności kierunku normalnych z wysokością i ciągłości krawędzi kafla. Wartość 36,98% oznacza widoczne pokrycie wypieczonej powierzchni kamieniem. Raport pomiarów: `Artifacts/TerrainRoadV4/TerrainRoadV4_BakeMeasurement.json`; metryki zasobów: `Artifacts/TerrainRoadV4/TerrainRoadV4_AssetMetrics.json`. Próba PlayerLoop sprawdziła F7/F8/W/F6/F7; kamera przemieściła się o 0,541 m, a postać o 0,566 m bez błędów. Syntetyczne przejście `CharacterController.Move` wyniosło 25,722 m, obejmowało 146 kroków, 8/8 trafień promieni i zero kolizji bocznych; nie jest to pełny naturalny spacer WASD. Ponowne wejście do Play potwierdziło start od V3B i brak pozostawionych kopii runtime. Raporty prób i walidacji znajdują się w `Artifacts/TerrainRoadV4/`.

Końcowy, ścisły i niefiltrowany Gameplay preflight uruchomiono poleceniem:

```powershell
.\Tools\AgentHarness\Invoke-UnityPreflight.ps1 -Tier Gameplay -Json
```

Przeszedł z exit 0: 14/14 kroków, 54/54 testy EditMode, 17/17 PlayMode, bez kroków niezaliczonych i `warnings=[]`. Raport: `Artifacts/Validation/20260926T175235Z-51dd80c6/summary.json`. Walidacja funkcjonalna odbyła się na RTX 5070 Laptop; nie wykonano pomiarów wydajności. Nie wykonano galerii zrzutów ani benchmarku GTX 1650 przy 1080p/60 FPS. Wyraźne, ciemne krawędzie fragmentów mogą być mocne, a powtarzalność kafla widoczna z dystansu. Migotanie czasowe, LOD i pełny naturalny spacer nie zostały rozstrzygnięte; ocenę artystyczną i końcowy spacer wykonuje użytkownik.

W trakcie prac poprawiono wybór składowej `Vector4.a` na `Vector4.w`. Odmowa dostępu sandboxa do SDK/Pipeline została rozwiązana po eskalacji. Nieoczekiwane przesunięcie pozycji startowej postaci cofnięto, a nieprawidłowy zrzut z początku sceny odrzucono i zastąpiono późniejszą próbą. Nie ustalono ani nie dopisuje się przyczyn tych dwóch zdarzeń.

Po preflight scena `TerrainRoad_Lookdev` była w czystym Edit Mode (bez Play ani kompilacji), a końcowy odczyt Unity Console wykazał 0 errors.
