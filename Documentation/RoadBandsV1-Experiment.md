# RoadBandsV1 — eksperyment generowania pasów drogi

**Stan:** działający eksperyment offline w Blenderze 5.1.2; wyniki techniczne sprawdzone. Ostateczna akceptacja wizualna pozostaje po stronie użytkownika. To narzędzie nie integruje się z Unity i nie stanowi jeszcze docelowego materiału gry.

## Otwieranie

Pliki eksperymentu znajdują się w [RoadBandsV1](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1). Główny plik sceny to [RoadBandsV1.blend](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1/RoadBandsV1.blend). Uruchom [Launch.cmd](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1/Launch.cmd), który wywołuje [Launch.ps1](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1/Launch.ps1) i domyślny program `D:\Programy\Blender\blender.exe`. Dla innej instalacji Blendera można uruchomić skrypt PowerShell z parametrem `-BlenderExe <ścieżka-do-blendera.exe>`.

Launcher otwiera scenę i rejestruje panel `RoadBands` w bocznym pasku 3D View. Rejestracja dotyczy bieżącej sesji. Samo otworzenie pliku `.blend` w późniejszej sesji nie rejestruje panelu: najpierw uruchom launcher, a dopiero potem wybierz **File > Open** i otwórz wcześniejszy plik próby w tej samej sesji. Mapy próby są spakowane w pliku sceny; parametry i ścieżki do cache pozostają zapisane.

## Praca z panelem

1. Zmień parametry w zakładce **RoadBands** panelu bocznego 3D View (skrót N).
2. Naciśnij **Generuj**, aby synchronicznie przeliczyć mapy i utworzyć nowy katalog generacji.
3. Wybierz wariant: **Płynny**, **128** albo **64**. Przełączanie używa wygenerowanych map i nie zmienia kamery ani oświetlenia.
4. Ustaw kadr: **Z góry**, **Wysokość gracza**, **Zbliżenie** lub **Kafel 3×3**. Preset kadru zmienia kamerę. Podgląd samych normalnych pokazuje je na neutralnym szarym materiale ze strength 1.
5. **Renderuj porównanie** zapisuje 15 PNG (5 ujęć × 3 warianty) w `previews` bieżącej generacji.
6. **Zapisz próbę** tworzy nowy, niezmienny katalog w `Trials` z mapami, konfiguracją, raportem walidacyjnym, plikiem `.blend` i istniejącymi podglądami. Starsze próby nie są nadpisywane.

Zmiana parametrów po generowaniu blokuje zapis i porównanie do czasu ponownego naciśnięcia **Generuj**. **Przywróć ustawienia startowe** również wymaga późniejszego generowania. Przycisk modelu wysokości pokazuje lub ukrywa pomocniczą siatkę.
## Domyślna konfiguracja i formaty

Eksperyment generuje okresowy kafel 4 × 4 m w układzie Blendera: X w poprzek drogi, Y wzdłuż drogi. Ustawienia początkowe: seed `20260927`, 24 pasy, długość grupy około 0,6 m, relief 3 cm, szerokość przejścia 3 cm, falistość 1 cm, deformacja konturów 1,8 cm, zanikanie granic 0,33 oraz docelowy udział szarości 0,35. Rozdzielczość źródłowa wynosi 2048 × 2048.

Każdy zestaw zawiera trzy mapy albedo PNG oraz trzy mapy normalnych PNG: Płynny, 128 i 64. Warianty 128/64 powstają przez blokowe uśrednienie albedo w liniowym RGB i wektorów normalnych, ponowną normalizację wektorów oraz najbliższe powiększenie do rozmiaru źródłowego. Mapa wysokości `height_m.exr` jest wspólna dla wariantów, zapisana jako metryczny FLOAT32. Nie jest generowana mapa AO. Podgląd wykorzystuje płaską płaszczyznę; pomocnicza siatka wysokości jest ukryta.

## Zalecana próba i podglądy

Zalecana próba do oceny to [Trial_20260927_131810_seed20260927](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1/Trials/Trial_20260927_131810_seed20260927). Zawiera neutralny, wspólny zestaw kadrów:

- [Podgląd z wysokości gracza — Płynny](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1/Trials/Trial_20260927_131810_seed20260927/previews/player_smooth.png)
- [Podgląd z wysokości gracza — 128](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1/Trials/Trial_20260927_131810_seed20260927/previews/player_128.png)
- [Podgląd z wysokości gracza — 64](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1/Trials/Trial_20260927_131810_seed20260927/previews/player_64.png)
- [Zbliżenie — Płynny](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1/Trials/Trial_20260927_131810_seed20260927/previews/close_smooth.png)
- [Kafel 3×3 — Płynny](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1/Trials/Trial_20260927_131810_seed20260927/previews/tile3x3_smooth.png)
- [Podgląd samych normalnych — Płynny](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1/Trials/Trial_20260927_131810_seed20260927/previews/normal_smooth.png)

Pozostałe ujęcia z każdej próby są w jej katalogu `previews`. Podglądy renderowane są na stałym, neutralnym rigu: miękkie światło kluczowe 150 W o rozmiarze 3 m, światło świata 0,25, ekspozycja 0, transformacja Standard / Look None, bez AO. Warianty kwantyzowane są próbkowane metodą najbliższego sąsiada. Porównanie ma izolować różnice map; nie jest pomiarem wyglądu w silniku gry.

## Zmierzone wyniki

Raport próby: [validation_report.json](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1/Trials/Trial_20260927_131810_seed20260927/validation_report.json). Raport odnotowuje:

- udział szarości całego kafla `0,34209` (około 34,21%) przy celu `0,35`;
- wysokość od `−0,015578 m` do `+0,015666 m`;
- 95. percentyl różnicy wysokości między punktami oddalonymi o 10 cm wzdłuż osi Y: `0,008457 m` (to nie jest maksymalny relief w całym oknie 10 cm);
- maksymalny błąd odczytu EXR FLOAT32: `0`;
- długość wektora normalnej przed zapisem PNG w zakresie `0,99999988–1,00000012`.

Parametr relief `0,03 m` jest ustawieniem generatora, a nie gwarancją 3 cm różnicy wysokości w każdym miejscu. Wartość `0,32069` w raporcie to udział wybranych granic fragmentów potomnych objętych mieszaniem/zanikaniem w liczbie takich granic; nie jest to udział całkowitej długości wszystkich pasów ani potwierdzenie, że właśnie taki odsetek długości pasa znika wizualnie. Implementacja obecnie poszerza i miesza wybrane granice. To ograniczenie należy uwzględnić przy dalszym strojeniu.

## Weryfikacja i ograniczenia

Siedem testów rdzenia zakończyło się powodzeniem (`core_tests_final3.log`): okresowość wartości i pochodnych, powtarzalność hashy, udział szarości, poprawne uśrednianie koloru liniowego, orientacja i normalizacja normalnych, konwencja kanału zielonego oraz dokładnie stałe bloki wariantów. Testy operatorów i cyklu życia uruchomione w tle Blendera zakończyły się powodzeniem (`ui_lifecycle_final3.log`, [ui_lifecycle_report.json](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1/generated/ui_lifecycle_report.json)): 15 podglądów, sześć spakowanych map wariantów, blokady nieaktualnych ustawień, zapis dwóch odrębnych prób i ponowne otwarcie starszej próby po wygenerowaniu nowszej. Osobno sprawdzono przez API działający panel zarejestrowany w sesji GUI, powiązania kontrolek i presety. Nie oznacza to, że cały test cyklu życia wykonano ręcznie w GUI. Główne obiekty generowania i budowania sceny to [roadbands.py](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1/roadbands.py) i [build_experiment.py](C:/Users/dunia/Documents/RageQuitting/Experiments/RoadBandsV1/build_experiment.py).

Końcowy, ścisły preflight Unity `Fast` przeszedł: status `passed`, kod wyjścia 0, 11/11 kroków `passed`, bez ostrzeżeń. Raport: [summary.json](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20260927T112052Z-42560831/summary.json). Wcześniejszy przebieg w ograniczonym środowisku zakończył się kodem 1: analizatory zgłosiły błąd dostępu do Microsoft SDK, a kompilacja, odczyt konsoli, testy EditMode i szybkie walidatory były `not_available`, bo nie wykryto dostępnego edytora. Wcześniejszy raport: [summary.json — przebieg nieudany](D:/Programy/UnityProjects/RageQuitting/Artifacts/Validation/20260927T111934Z-540b5e40/summary.json). Po zmianie środowiska wykonano końcowy preflight poprawnie; do głównego raportu należy odnosić się jako do wyniku końcowego. Dokładna komenda: `.\Tools\AgentHarness\Invoke-UnityPreflight.ps1 -Tier Fast -Json`, uruchomiona w katalogu `D:\Programy\UnityProjects\RageQuitting`.

Preflight Unity nie weryfikuje generatora Blendera ani artystycznej akceptacji. Nie wykonano porównania rendererów ani wydajności w Unity, nie potwierdzono działania na GTX 1650 i nie należy twierdzić, że wygląd spełnia referencję lub uzyskał akceptację użytkownika. Kierunek pasów jest czytelny, ale rytm pasów i wzór bloków około 4 cm nadal wyglądają regularnie i powtarzalnie. Redukcja rozdzielczości może usuwać drobne formy. Przechwycony zrzut ekranu interfejsu Blendera był czarny; przyczyna nie została ustalona, dlatego ocenę oparto na zapisanych renderach. Podczas prac poprawiono wykryte problemy z nagłówkiem EXR, zachowaniem nieużywanych materiałów w zapisanej scenie, opóźnioną konfiguracją widoku przy starcie oraz nadmierną jasnością podglądów; końcowe testy powyżej przeszły po tych poprawkach. Starsze generacje, próby i logi pozostają w katalogach eksperymentu jako materiał diagnostyczny.