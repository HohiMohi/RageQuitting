# DeepBump F9 — wdrożenie i obsługa

Data: 2026-09-27

## Rezultat

W scenie `Assets/Scenes/TerrainRoad_Lookdev.unity` klawisz **F9** przełącza widok drogi na wariant DeepBump: kolor V1 i normalne DeepBump V2. Wariant F8 RoadBands V1.1 Smooth pozostał bez zmian. Domyślnym wariantem F7 nadal jest V3B; F6 nadal wybiera V3A.

Do projektu skopiowano istniejące mapy bez ich generowania i bez zmian artystycznych. Oryginały oraz materiały źródłowe próby znajdują się w `ArtSource/DeepBumpV3Mix/`, a zaimportowane mapy w `Assets/Art/Environment/TerrainRoadLookdev/SurfaceDeepBumpV3Mix/`. Wartości SHA-256 oryginałów zapisano w `ArtSource/DeepBumpV3Mix/provenance.json`:

- `road_albedo.png`: `C3168D8742CB5EABC2886BD1C134A0E48B6A438EC4D286F9F82BC5F905C86B55`
- `road_normal_deepbump16.png`: `BF6C42699CFF0E232AF59CA251FDB213F5363A9B813EC149C1CDE76E1FD5B44D`

`byteIdentical: true` w pliku pochodzenia potwierdza identyczność kopii względem zaakceptowanych wejść. Obrazy wejściowe normalnych są 16-bitowe; zaimportowana tekstura Unity działa w formacie `R8G8B8A8_UNorm`, czyli 8 bitów na kanał. To ograniczenie importu GPU nie zmienia zachowanego pliku źródłowego. Plik normalnych oraz mieszany plik Blender są objęte Git LFS.

## Zasoby i ustawienia

- `Assets/Art/Environment/TerrainRoadLookdev/SurfaceDeepBumpV3Mix/` zawiera mapę koloru, mapę normalnych i stałą maskę RGBA o wartości `(0, 255, 128, 0)`.
- `Assets/Art/Environment/TerrainRoadLookdev/TerrainLayers/DeepBumpV3Mix/` zawiera trzy osobne warstwy dla drogi, kamienia i ziemi. Warstwy współdzielą mapy; każda ma kafelkowanie `4 × 4 m`, offset `(0, 0)`, skalę normalnych `1`, metaliczność `0` i gładkość `0`, z neutralnymi remapami diffuse i maski. Height Blend pozostał wyłączony.
- Mapy 1254 × 1254 mają ustawiony Max Size 2048 i NPOT None; zaimportowano je bez kompresji, z Repeat, mipmapami, filtrowaniem Trilinear i anizotropią 8. Kolor używa sRGB; normalne mają typ Normal Map, przestrzeń liniową, bez konwersji wysokości i bez odwracania zielonego kanału.
- Oryginalne pliki wejściowe, mieszany plik Blender, podgląd, walidacja i pochodzenie zachowano w `ArtSource/DeepBumpV3Mix/`. Dane walidacji i pochodzenia są tam dostępne do audytu.

## Przełączanie wariantów

Otwórz `TerrainRoad_Lookdev`, wejdź w Play Mode, ustaw fokus na Game View i naciśnij:

| Klawisz | Wariant |
|---|---|
| F6 | V3A |
| F7 | V3B — wariant domyślny |
| F8 | RoadBands V1.1 Smooth |
| F9 | DeepBump V1 + V2 |

Przełącznik używa jednego runtime’owego `TerrainData` oraz trzech jego klonów warstw i ponownie wykorzystuje je przy kolejnych przełączeniach. Dostępność F8 i F9 jest sprawdzana niezależnie. Brak konfiguracji jednego z tych wariantów wyświetla ostrzeżenie raz na włączenie komponentu i zachowuje aktualny wygląd. F9 nie zmienia konfiguracji F8. Wyłączenie komponentu lub wyjście z Play Mode przywraca oryginalne referencje Terrain i collidera oraz usuwa należące do przełącznika kopie. Ponowne włączenie zaczyna od V3B. Podczas weryfikacji edytor w trybie Edit Mode był czysty: bez kopii runtime i z oryginalnym colliderem, a aktywna konfiguracja DeepBump miała trzy warstwy i Height Blend wyłączony. Audyt stanu bazowego potwierdził ochronę terenu i wag splatmap, trawy, pobocza, światła i współdzielonych prefabów; 356 pozostałych plików audytu nie zmieniło się.

## Sprawdzenie techniczne i ograniczenia

- Końcowe, ukierunkowane testy PlayMode: **6/6 zaliczonych**, kod wyjścia `0`; raport: `Artifacts/DeepBumpF9/targeted-final/unity-playmode-tests-report.json`.
- Próba runtime: **34/34 kontroli zaliczonych**, bez błędów konsoli. Sprawdzono wybór F9, powtórne przełączanie, niezmienność F8, przywracanie F6/F7, parę Terrain/collider oraz odtworzenie kopii po ponownym włączeniu: `Artifacts/DeepBumpF9/runtime-smoke.json`.
- Próba ruchu potwierdziła przemieszczenie `5.324265 m`, uziemienie na początku i końcu oraz aktywną kamerę: `Artifacts/DeepBumpF9/movement-camera-smoke.json`.
- Zrzut `Artifacts/DeepBumpF9/deepbump_f9_player_view.png` potwierdza renderowanie F9 w widoku gracza. Wariant ma mocny pomarańczowy kolor i powtarzalny detal. Audyt map źródłowych wykazał nieciągłości na krawędziach kafla; szwy mogą być widoczne. Nie wykonano pełnego przejścia trasy, szczegółowego testu sterowania myszą ani benchmarku na GTX 1650. Ostateczna ocena wizualna należy do użytkownika; mapy źródłowe pozostawiono bez zmian.
- Ścisły, końcowy preflight Gameplay, uruchomiony synchronicznie bez filtra poleceniem `Invoke-UnityPreflight.ps1 -Tier Gameplay -Json`: **zaliczony**, kod wyjścia `0`, 14/14 kroków `passed`, brak ostrzeżeń i brak pominiętych wymaganych kontroli. Raport: [summary.json](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20260927T195158Z-217f49c1/summary.json). EditMode: 54/54; PlayMode: 24/24; scenariusz: 1/1; test sieciowy: 1/1. Preflight nie zmienił śledzonych plików.

Pierwsze błędy sandboxu, kompilacji pomocniczych fragmentów i nieaktualnego zestawu testów usunięto lub zastąpiono podczas pracy; nie są błędami końcowego przebiegu projektu. Końcowe testy ukierunkowane, próba runtime i ścisły preflight zakończyły się powodzeniem.
