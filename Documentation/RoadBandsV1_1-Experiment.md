# RoadBandsV1.1 — eksperyment nieregularnych granic pasów

**Stan:** eksperyment generatora w Blenderze 5.1.2 oraz ręcznie zaimportowana, wybrana przez użytkownika generacja materiału w Unity F8. Generator nadal działa offline i nie synchronizuje przyszłych generacji automatycznie. Wyniki techniczne sprawdzono; ostateczna akceptacja wizualna pozostaje po stronie użytkownika. Wersja źródłowa generatora: `RoadBandsV1.1-field-0.1`.

## Otwieranie

Pliki eksperymentu znajdują się w [RoadBandsV1_1](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1). Główny plik sceny to [RoadBandsV1_1.blend](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1/RoadBandsV1_1.blend). Uruchom [Launch.cmd](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1/Launch.cmd) albo alternatywny [Launch.ps1](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1/Launch.ps1); oba bezpośrednio uruchamiają `D:\Programy\Blender\blender.exe` z plikiem sceny i skryptem `register_panel.py`. Launcher rejestruje panel `RoadBands` tylko na czas bieżącej sesji; nie zmienia globalnych preferencji Blendera. Samo otwarcie pliku `.blend` w nowej sesji nie rejestruje panelu, dlatego rozpocznij od jednego z launcherów. Mapy próby są spakowane w pliku sceny.

## Praca z panelem

1. W panelu bocznym 3D View (skrót N), w zakładce **RoadBands**, ustaw **Nieregularność granic (cm)** od 0 do 3 cm (domyślnie 1,5 cm).
2. Naciśnij **Generuj**, aby synchronicznie przeliczyć mapy. Zmiana parametru nie przelicza już wygenerowanych map; najpierw użyj **Generuj**.
3. Przełącz wariant **Płynny**, **128** albo **64**. Zmiana wariantu nie zmienia kamery ani oświetlenia.
4. Wybierz kadr **Z góry**, **Wysokość gracza**, **Zbliżenie** lub **Kafel 3×3**. Możesz też pokazać same normalne na neutralnym szarym materiale.
5. **Renderuj porównanie** zapisuje pięć ujęć dla trzech wariantów w katalogu `previews` bieżącej generacji.
6. **Zapisz próbę** zapisuje niezmienny katalog próby w `Trials`; **Przywróć ustawienia startowe** przywraca wartości domyślne i wymaga ponownego generowania. Pomocnicza siatka wysokości jest domyślnie ukryta.

Pełne generowanie w rozdzielczości 2048 zajmuje kilka minut i synchroniczny operator może na ten czas blokować interfejs. Nie dodano szybkiego podglądu.

## Konfiguracja i zakres zmiany

Pierwotne ustawienia eksperymentu offline (seed `20260927`, 24 pasy, długość grupy około 0,6 m, relief 3 cm, przejście 3 cm, falistość 1 cm, deformacja konturów 1,8 cm, zanik granic 0,33 i docelowy udział szarości 0,35) opisują historyczne próby w Blenderze, a nie generację zaimportowaną do Unity. Aktualna importowana generacja i jej parametry są wymienione w sekcji integracji. Generator tworzy okresowy kafel 4 × 4 m, 2048 × 2048 pikseli; w układzie Blendera X biegnie w poprzek drogi, a Y wzdłuż niej.

W V1.1 granice segmentów pasów są wspólnie odkształcane: jeden wspólny przebieg tworzy wypukłość po jednej stronie oraz odpowiadające jej wcięcie po drugiej. Ogranicznik zachowuje odstęp między sąsiednimi granicami i pilnuje prześwitu punktów kontrolnych. Losowość nowej deformacji jest oddzielona od dotychczasowych pól koloru w mikroskali, które pozostały bez zmian. Ustawienie 0 cm przy tych samych pozostałych parametrach odtwarza V1 dokładnie.

Każda próba zawiera 15 PNG (pięć ujęć × trzy warianty), komplet map albedo i normalnych dla Płynny/128/64, wspólną mapę wysokości `FLOAT32` EXR w metrach, konfigurację, raport walidacji i plik `.blend`. Warianty 128 i 64 powstają przez blokowe uśrednianie albedo w liniowym RGB i normalnych, ponowną normalizację normalnych oraz powiększenie metodą najbliższego sąsiada bez interpolacji. Mapa wysokości jest wspólna. Nie jest generowana mapa AO; podgląd korzysta z płaskiej geometrii.

## Zapisane próby i rendery

Wszystkie trzy próby mają osobne konfiguracje i raporty; nie osadzono kompletu 45 obrazów w tej dokumentacji.

| Nieregularność | Próba | Przykładowe rendery |
|---|---|---|
| 0 cm | [Trial_20260927_144748_irregularity00.0cm_seed20260927](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1/Trials/Trial_20260927_144748_irregularity00.0cm_seed20260927) | [Z góry, Płynny](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1/Trials/Trial_20260927_144748_irregularity00.0cm_seed20260927/previews/top_smooth.png), [Wysokość gracza, 128](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1/Trials/Trial_20260927_144748_irregularity00.0cm_seed20260927/previews/player_128.png), [Zbliżenie, Płynny](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1/Trials/Trial_20260927_144748_irregularity00.0cm_seed20260927/previews/close_smooth.png) |
| 1,5 cm | [Trial_20260927_145520_irregularity01.5cm_seed20260927](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1/Trials/Trial_20260927_145520_irregularity01.5cm_seed20260927) | [Z góry, Płynny](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1/Trials/Trial_20260927_145520_irregularity01.5cm_seed20260927/previews/top_smooth.png), [Wysokość gracza, 128](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1/Trials/Trial_20260927_145520_irregularity01.5cm_seed20260927/previews/player_128.png), [Zbliżenie, Płynny](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1/Trials/Trial_20260927_145520_irregularity01.5cm_seed20260927/previews/close_smooth.png) |
| 3 cm | [Trial_20260927_145834_irregularity03.0cm_seed20260927](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1/Trials/Trial_20260927_145834_irregularity03.0cm_seed20260927) | [Z góry, Płynny](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1/Trials/Trial_20260927_145834_irregularity03.0cm_seed20260927/previews/top_smooth.png), [Wysokość gracza, 128](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1/Trials/Trial_20260927_145834_irregularity03.0cm_seed20260927/previews/player_128.png), [Zbliżenie, Płynny](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1/Trials/Trial_20260927_145834_irregularity03.0cm_seed20260927/previews/close_smooth.png) |

Pozostałe kadry i warianty są w `previews` każdej próby. Raport zbiorczy to [trial_set_validation.json](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1/Trials/trial_set_validation.json), a sprawdzenie ponownego otwarcia i cyklu życia to [lifecycle_reopen_report.json](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1/Trials/lifecycle_reopen_report.json).

## Zmierzone wyniki

Raporty każdej próby podają maksymalną zastosowaną deformację granicy oraz hash map. Dla 0 cm wartość rzeczywista wyniosła 0 m; dla 1,5 cm wyniosła 0,0149899 m, a dla 3 cm 0,0299799 m. Licznik ograniczonych wartości kontrolnych w próbkowanych krzywych granic i boków wyniósł odpowiednio 0, 5 i 7; nie jest to liczba granic ani fizycznych zdarzeń. Dodatkowo ograniczane są próbkowane położenia boków, a próbkowany minimalny prześwit fragmentu, czyli odstęp między granicami, wyniósł 0,0193716 m we wszystkich trzech przypadkach.

Niezależne wygenerowanie wariantu 0 cm dało te same tablice, hashe i sześć plików PNG, co zaakceptowana próba V1. Powtórne generowanie wariantu 1,5 cm odtworzyło hashe zapisane w cache. Ponowne otwarcie zapisanej próby 0 cm po późniejszych generacjach przywróciło mapy i ustawienia. W każdym wariancie udział szarości całego kafla wyniósł około 34,17–34,21% przy celu 35%; błąd odczytu wysokości FLOAT32 EXR wyniósł 0. Wektory źródłowe normalnych miały długości w zakresie około `0,99999988–1,00000012`; po kodowaniu PNG audyt długości wykazał błąd poniżej 0,007 wynikający z 8-bitowej reprezentacji.

Wszystkie trzy warianty przeszły audyt pełnych map 2048 px: wymiary, dokładnie stałe bloki 128/64, kierunek normalnych ku górze i odczyt EXR. Raport obejmuje 45 renderów łącznie. Testy numeryczne rdzenia: 10/10 zaliczonych, kod wyjścia 0. Testy sprawdziły również niezmienność kamery i świateł przy przełączaniu cache, presety kamer, podgląd samych normalnych, blokadę zapisu i porównania dla nieaktualnych ustawień, reset oraz ponowne otwarcie prób. Przez API potwierdzono działanie panelu w aktywnej sesji GUI i zmianę jego kontrolek, ale nie wykonano pełnego ręcznego przeklikania całego interfejsu. W historycznym pliku głównym próby offline zapisany domyślny stan to próba 1,5 cm, wariant 128 i kadr wysokości gracza; ten stan nie opisuje generacji Smooth zaimportowanej do Unity.


## Przebieg i znane ograniczenia

Podczas prac naprawiono szew normalnych w początkowej wersji deformacji granic. Pierwszy przebieg wsadowy przerwano po ukończeniu i zapisaniu próby 0 cm; zapis ten zachowano, a kontynuacja dokończyła pozostałe próby i komplet 45 renderów. Wykonano cztery pełne generowania: 0 cm, 1,5 cm, ponowną generację 1,5 cm do sprawdzenia deterministyczności oraz 3 cm. Blender wyświetlił ostrzeżenie o domyślnej ścieżce pędzla; było ono niegroźne. Logi: [batch2_blender.log](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1/batch2_blender.log) (przerwany przebieg), [continue_blender.log](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1/continue_blender.log) (kontynuacja i walidacja), [lifecycle_blender.log](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1_1/lifecycle_blender.log) (cykl życia).

Audyt wcześniejszej fazy offline obejmował 428 plików: 360 plików V1 oraz 68 plików Unity z modyfikacjami sprzed tamtego zadania; ówczesny raport stwierdzał brak integracji. Późniejsza, osobna integracja F8 jest opisana niżej.
## Integracja Unity F8 — 2026-09-27

Zaimportowano wybraną przez użytkownika gotową generację Smooth `gen_1f973d776e8f_20260927_160826_381552` z seeda `20260916`: 12 pasów, nieregularność `0.02`, parametr reliefu `0.06 m` (nie jest to wysokość literalnie wszystkich form). Nie regenerowano map ani nie wykonywano poprawek artystycznych. Źródła, EXR, konfiguracja, raport i snapshoty SHA-256 znajdują się w `ArtSource/RoadBandsV1_1/GeneratedSmooth`; nowe assety są w `Assets/Art/Environment/TerrainRoadLookdev/SurfaceRoadBandsV1_1` i `TerrainLayers/RoadBandsV1_1`.

**Warianty przełącznika podłoża:** F6 = V3A; F7 = V3B (domyślny); F8 = RoadBandsV1.1Płynny. Trzy nowe Terrain Layers (road/stone/darkearth) dotyczą wyłącznie F8. F6 i F7 nadal używają istniejących warstw V3. Warstwy F8 wskazują te same tekstury albedo i normalną oraz stałą maskę 4 × 4 piksele: R0, G1, B128/255, A0, bez mapy AO. Albedo i normalna mają 2048 × 2048 pikseli. Wszystkie mają Terrain Layer tile size 4 m i offset 0; siła normalnej 1, flip wyłączony. Import: albedo sRGB/color, normalna jako linear Normal Map, maska jako linear; Repeat, mipmapy, Trilinear, aniso 8, uncompressed. Shader TerrainLit używa próbkowania Trilinear dla tekstur warstw Terrain; wygląd może różnić się od renderu Blendera z próbkowaniem Closest.

W scenie zmieniono wyłącznie trzy GUID-y `v4TemplateLayers`; zmiany kodu ograniczyły się do etykiety i szerokości. TerrainData: wysokości, wagi, collider, trawa i pobocza oraz oświetlenie pozostały bez zmian. Wszystkie 196 zależności innych niż scena pozostały bez zmian (197 zależności łącznie). PNG importowane do Unity są bajtowo identyczne ze źródłami. Audyt normalnych względem EXR wykazał maksymalny błąd `0.003921807` (zaokrąglenie 8-bitowe); audyt okresowości potwierdził identyczność granic na 512 próbkach na oś.

**Użycie:** otwórz scenę `TerrainRoad_Lookdev`, wejdź w Play Mode, ustaw fokus na Game View i naciśnij **F8**, aby wybrać RoadBands V1.1 — Płynny. **F6** wybiera V3 A, a **F7** wybiera V3 B, który jest też domyślny przy rozpoczęciu sesji. Generator Blendera pozostaje oddzielny: kolejne generacje nie trafiają automatycznie do Unity.

Testy PlayMode: 4/4 zaliczone. Rzeczywista sekwencja V3B→V4→V3A→V3B dwukrotnie, wyłączenie/ponowne włączenie komponentu, druga sesja Play i czyszczenie kopii runtime przeszły asercje parowania Terrain/Collider. Krótki test wejścia: 2.467239 m i 76 klatek z uziemieniem, reakcja yaw/pitch kamery i usunięcie tymczasowych urządzeń wejściowych. To nie był pełny spacer wizualny ani benchmark wydajności.

Końcowy ścisły, niefiltrowany preflight `Gameplay` wykonano po ostatniej poprawce z katalogu `D:\Programy\UnityProjects\RageQuitting`: status `passed`, kod 0, 14/14 kroków zaliczonych, brak kroków z innym statusem i brak ostrzeżeń. EditMode 54/54 i PlayMode 17/17, bez filtrów; czas około 90,8 s. Raport: [summary.json](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20260927T145533Z-1bce4f2f/summary.json). Wcześniejszy, ścisły preflight `Fast` z fazy offline również zakończył się powodzeniem (0, 11/11); raport: [summary.json](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20260927T130230Z-eab8c2b3/summary.json). Raport integracji i szczegółowe audyty: [validation_summary.json](D:/Programy/UnityProjects/RageQuitting/Artifacts/RoadBandsV1_1/validation_summary.json), [targeted_playmode_tests.json](D:/Programy/UnityProjects/RageQuitting/Artifacts/RoadBandsV1_1/targeted_playmode_tests.json), [player_input_smoke.json](D:/Programy/UnityProjects/RageQuitting/Artifacts/RoadBandsV1_1/player_input_smoke.json), [independent_normal_audit.json](D:/Programy/UnityProjects/RageQuitting/Artifacts/RoadBandsV1_1/independent_normal_audit.json), [independent_periodicity_audit.json](D:/Programy/UnityProjects/RageQuitting/Artifacts/RoadBandsV1_1/independent_periodicity_audit.json). Początkowe błędy diagnostyczne importu i uruchomienia prób zostały naprawione; szczegóły są w raporcie integracji.

Nie potwierdzono artystycznej akceptacji użytkownika ani wydajności na GTX 1650 przy 1080p/60 FPS.

## Preflight i ograniczenia

Rendery pokazują wyraźniejsze wcięcia przy 3 cm, szczególnie w widoku Płynny. Regularne zygzaki po bokach i równoległy rytm pasów nadal są widoczne; kafel 4 m pozostaje rozpoznawalny przy powtórzeniu 3×3. Blokowe uśrednianie wariantów 128 i 64 częściowo, a 64 w większym stopniu, usuwa widoczność drobnych wcięć; powiększenie wyjściowej mapy odbywa się metodą najbliższego sąsiada bez interpolacji. To opis zaobserwowanych renderów, nie deklaracja akceptacji artystycznej. W pierwotnej fazie offline nie walidowano materiału w Unity; integrację F8 i jej techniczne walidacje wykonano później, jak opisano wyżej. Nie przeprowadzono pełnego spaceru wizualnego ani pomiaru wydajności na GTX 1650 przy 1080p/60 FPS; akceptacja artystyczna należy do użytkownika.







