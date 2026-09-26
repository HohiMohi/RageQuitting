# Przygotowanie wspólnego push

Przygotowano jeden wspólny zestaw do przyszłego commitu obejmujący zmiany scen i animacje Goblina, nowe i poprawione zasoby Art, źródła ArtSource, narzędzia lookdev/testów, plany oraz dokumentację. Animacje Idle, Run i Sprint są ujęte razem ze zmianą `Tutorial_scene.unity` i sceną `TerrainRoad_Lookdev.unity`, wraz z wymaganymi plikami `.meta`.

Lokalnie pozostają: `.cursor/mcp.json` (konfiguracja zawiera dane dostępowe; usunięto ją wyłącznie z indeksu Git), dwa nieużywane assety Terrain i ich `.meta`, trzy nieużywane katalogi eksportów `GeneratedAssets`, kopie Blender `.blend1`/`.blend2`, podglądy, montaże i raporty walidacyjne w `ArtSource`, a także wyniki QA, logi i zrzuty w `Artifacts`. Dołączono dwa zatwierdzone wyjątki od ignorowania QA: `Artifacts/TerrainRoadReliefV2/batch3b_baseline.bin` oraz źródło `Artifacts/PainterlyRoadLookdev/process_texture.py`. Pozostałe QA wyniki są odtwarzalne i pozostają ignorowane; przed ich użyciem należy uruchomić odpowiadający im generator.

Po przeglądzie przygotowany indeks można zapisać jednym commitem obejmującym powyższe grupy, a następnie wypchnąć. Nie wykonano commitu ani pushu. Historia istniejących commitów nie została przepisana: wcześniejsze wersje lokalnej konfiguracji mogły pozostać w historii; ten zestaw jedynie przestaje śledzić bieżący plik.

Manifest przygotowania: `Artifacts/Validation/push-preparation-manifest.txt`.
