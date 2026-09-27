# Terrain Road V4.1 — wdrożenie i obsługa

Data: 2026-09-26  
Projekt: `D:\Programy\UnityProjects\RageQuitting` · Unity `6000.3.18f1` · Blender `5.1.2`  
Scena: `Assets/Scenes/TerrainRoad_Lookdev.unity`

## Rezultat

V4.1 jest wariantem F8 w istniejącej scenie lookdev. F6 wybiera V3A, F7 wybiera V3B (domyślnie), a F8 wybiera V4.1. W kodzie wariant zachowuje enum `Variant.V4`; krótki opis w GUI wyświetla „V4.1”. Przełącznik korzysta z istniejącego cyklu kopii runtime TerrainData i trzech TerrainLayer: przełączanie zmienia wyłącznie kopie, a wyłączenie komponentu lub koniec Play przywraca oryginalne referencje.

Źródło to jeden okresowy kafel 4 × 4 m z ciągłą siatką wysokości 1025 × 1025 wierzchołków. Nie zawiera niezależnych, nakładających się płytek. Opis pól steruje wspólnie wysokością, kolorem i nachyleniem: większe fragmenty zwykle mają 10–25 cm, tworzą nieregularne grupy wzdłuż Terrain Z o długości około 40–80 cm, a wewnętrzne pola koloru mają 2–6 cm. Wygenerowany udział szaro-kamiennego koloru wynosi 32,4373%; wysokość na kaflu mieści się globalnie w zakresie 0,024374–0,070731 m. Ten zakres globalny nie jest miarą lokalnej wysokości pojedynczego fragmentu.

Po pierwszym kadrze z Unity poszerzono konkurencyjne mieszanie granic, zmieniając ostrość wykładnika z 8 na 5. Zmiana ogranicza twardy obrys, ale w scenie nadal widać część ciemnych kierunkowych konturów i regularność układu. To opis aktualnego stanu, nie deklaracja zgodności z referencją; dalszą ocenę artystyczną wykonuje użytkownik.

## Mapy i warstwy

Źródła: `ArtSource/TerrainRoadV4_1/TerrainRoadV4_1.blend` i deterministyczny generator `generate_terrain_road_v4_1.py` (ziarno 42609261). Mapy 2048 × 2048 są pod `Assets/Art/Environment/TerrainRoadLookdev/SurfaceV4_1/`: trzy albedo, wspólna normalna styczna, wysokość EXR, łagodna AO oraz maska URP RGBA (R metaliczność, G AO, B wysokość, A gładkość). Trzy szablony TerrainLayer są w `TerrainLayers/V4_1/`.

Wszystkie warstwy używają kafla 4 m, offsetu 0, skali normalnej 1, metaliczności i gładkości 0, remapów kanałów 0–1 oraz wyłączonego Height Blend. Import: albedo sRGB, normalna typu Normal Map, pozostałe mapy liniowe; Repeat, mipmapy, Trilinear, anizotropia 8 i kompresja wyłączona. Wygenerowany EXR jest 32-bitowy na kanał; Unity importuje go jako `RGBAHalf`.

Generator zapisuje projektowe ścieżki względem własnego położenia. Z katalogu projektu D: odtwórz mapy i edytowalny `.blend` poleceniem:

```powershell
& 'D:\Programy\Blender\blender.exe' --factory-startup --background --python-exit-code 1 --python 'D:\Programy\UnityProjects\RageQuitting\ArtSource\TerrainRoadV4_1\generate_terrain_road_v4_1.py'
```

Ponowne uruchomienie nadpisuje wyłącznie własny plik `.blend`, mapy i raporty V4.1; przed nim zachowaj ręczne edycje źródła. Po generacji uruchom przez połączony Unity Editor `import_and_configure_v41_textures.cs`. `create_v41_terrain_layers.cs` służy tylko do pierwszego utworzenia warstw i zgłasza błąd, gdy zasoby już istnieją; nie uruchamiaj go ponownie. `assign_v41_templates_to_scene.cs` zapisuje wyłącznie trzy referencje `v4TemplateLayers` w scenie. Nie edytuj pliku sceny YAML ręcznie.

## Powrót i wycofanie

W Play naciśnij F7, aby wrócić do domyślnego V3B; F6 wybiera V3A. Wyjście z Play także przywraca źródłowe referencje sceny. Pełne wycofanie przypisań V4.1 polega na przywróceniu trzech poprzednich referencji `v4TemplateLayers` w Unity Editorze; dostępny helper `ArtSource/TerrainRoadV4/assign_v4_templates_to_scene.cs` zapisuje referencje dawnych warstw. Etykieta GUI jest kodem: jeśli trzeba ją cofnąć, przywróć poprzedni tekst w `Assets/Scripts/Lookdev/TerrainRoadV3/TerrainRoadV3RuntimeSwitcher.cs` (nie zmienia się jej w Inspectorze). Zachowaj mapy i warstwy, dopóki scena albo inny zasób może się do nich odwoływać. Źródła, mapy i TerrainLayer starego V4 pozostały zachowane; wspólna scena lookdev została zaktualizowana przez zmianę trzech referencji F8.

## Walidacja i ograniczenia

Raport generatora: `Artifacts/TerrainRoadV4_1/TerrainRoadV4_1_Generation.json`. Odczyt wysokości zgadza się z polem źródłowym (maksymalny błąd 0), normalna ma średni/p95 błąd kątowy 0,799°/2,914°, a udział czarnych otworów wynosi 0. Raport importu i sceny: `UnityEditModeImportSceneValidation.json`; dowód 127 chronionych plików bez zmian: `ProtectedBaselineValidation.json`. Kadr F8 i pozostałe prywatne podglądy są w `Artifacts/TerrainRoadV4_1/`.

Podczas operacji wystąpiły znane błędy narzędziowe, zapisane w `Artifacts/TerrainRoadV4_1/ImportAttemptDiagnostics.json` i `Artifacts/TerrainRoadV4_1/PlayModeProbeOperationDiagnostics.json`: próba dry-run odrzuciła już istniejący plik, odpowiedź importu przekroczyła limit czasu mimo potwierdzonego zakończenia wszystkich siedmiu importów, walidator wstrzymał się na zabrudzonej scenie do jej jawnego zapisu w Editorze, a pierwsze tymczasowe ustawienie kadru nie znalazło typu enum; każde z tych zdarzeń zostało rozwiązane i nie wskazuje aktualnego błędu runtime.

PlayerLoop sprawdził F7 → F8 → F7 → F8, ruch W, F6 → F7 oraz wyłączenie i ponowne włączenie komponentu: 41 klatek, ruch postaci 0,5274 m i kamery 0,5009 m, bez zmian źródłowych danych terenu ani pliku sceny. Kopie runtime zostały zwolnione, ponowne włączenie utworzyło nowe kopie z domyślnym B, a ponowne wejście do Play również zaczęło od B. Syntetyczna trasa `CharacterController.Move` objęła 25,722 m i 146 kroków; promienie trafiły w teren 8/8 razy, kolizji bocznych było 0, a maksymalna szczelina stóp do terenu wyniosła 0,02027 m. Jest to kontrolowany test collidera, nie pełny naturalny spacer WASD.

Końcowy, ścisły i niefiltrowany preflight Gameplay uruchomiono poleceniem:

```powershell
.\Tools\AgentHarness\Invoke-UnityPreflight.ps1 -Tier Gameplay -Json
```

Przebieg przeszedł z exit 0: 14/14 kroków, 54/54 EditMode i 17/17 PlayMode; cztery testy przełącznika przeszły, nie było kroków o statusie innym niż `passed`, `warnings=[]`. Raport: `Artifacts/Validation/20260926T200031Z-3c29e614/summary.json`. Końcowy Editor pozostał w czystym Edit Mode, wariant V3B, bez RuntimeData, z pozycją `PlayerNew` `(14.15, 0.445746, 4.3)`; TerrainCollider nadal wskazuje to samo TerrainData co Terrain. Odczyt Console wykazał 0 errors; zapisane wcześniej ostrzeżenia TMP Ellipsis nie były czyszczone.

Nie zmierzono wydajności na GTX 1650 przy 1080p/60 FPS ani nie wykonano pełnego spaceru przez użytkownika. Migotanie czasowe i końcowa ocena artystyczna pozostają niepotwierdzone; mapowanie grup zakłada kierunek Terrain Z i nie obsługuje krzywizn drogi. Nie dodano geometrii Unity, POM ani shaderów. Relief wizualny pochodzi z mapy normalnych wypieczonej z wysokości. Przy wyłączonym Height Blend kanał wysokości maski nie zmienia mieszania warstw ani geometrii Terrainu. Ustawienia światła, pobocza, trawy, wysokości terenu, collidery i pozycja startowa pozostały poza zakresem tej zmiany.
