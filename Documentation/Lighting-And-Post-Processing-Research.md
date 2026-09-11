# Lighting i post-processing — research i rekomendowany kierunek

> Stan: 2026-09-11, po wdrożeniu i walidacji pilota w `Tutorial_scene`. Dalsza część zachowuje pierwotny research i uzasadnienie; sekcja poniżej jest autorytatywnym opisem stanu faktycznie wdrożonego.

## Stan powdrożeniowy

### Zatwierdzona konfiguracja renderingu

- Globalny URP korzysta z `Assets/Settings/Rendering/ShowcaseURPAsset.asset` oraz `Assets/Settings/Rendering/ShowcaseUniversalRenderer.asset`. Rendering działa w trybie Forward, z HDR, render scale 1, wyłączonym MSAA, włączoną depth texture i wyłączoną opaque texture.
- Główne cienie mają atlas 2048, 2 kaskady i dystans 75 m. Additional lights są per-pixel i mają włączone cienie. Reflection probes używają blendingu i box projection. Color grading działa w HDR z LUT 32.
- APV Scenarios są włączone, a scenario blending pozostaje wyłączony.
- SSAO używa źródła Blue Noise, Depth Normals i pełnej rozdzielczości: intensity 1.2, direct lighting strength 0.15, radius 0.035, Medium samples oraz High blur.
- Unity automatycznie dopisało w `QualitySettings` wpis `Nintendo Switch 2: 5`. Wszystkie 6 override'ów render pipeline pozostaje `null`, aktywnym poziomem jakości jest `Ultra`; osobne tiery renderingu nie zostały utworzone.

### Volume i runtime

- Profil `Assets/Settings/Lighting/FairyAfternoonVolumeProfile.asset` zawiera 9 trwałych komponentów. Aktywna korekcja obrazu: Neutral tonemapping, White Balance temperature 12, post-exposure 0.2, contrast 8, saturation 12, color filter `(1, 0.94, 0.86)` oraz Bloom intensity 0.22, threshold 1.05, scatter 0.55.
- Vignette, Motion Blur, Depth of Field, Chromatic Aberration i Lens Distortion są jawnie nieaktywne.
- Runtime controller wykonuje deep clone każdego `VolumeComponent` oraz materiału sky. Cleanup przywraca poprzednie ustawienia, zamiast pozostawiać zmiany globalne po zmianie sceny lub zniszczeniu obiektu.
- `EnvironmentLightingProfile`, `EnvironmentLightingState`, `IEnvironmentTimeSource` i `EnvironmentLightingController` znajdują się w `Assets/Scripts/NewScripts/Lighting`.
- Zatwierdzony wariant czasu to ręcznie ustawione, stałe popołudnie `0.6875`. Poza popołudniem krzywe są stałymi placeholderami. Weather seam istnieje przed etapem apply, lecz nie ma jeszcze implementacji pogody. Nie dodano singletona ani adaptera NGO. `DynamicGI.UpdateEnvironment` jest ograniczone do wywołań nie częstszych niż co 0.25 s.

### Camera stack

- W `PlayerFirstPersonArms` kamera bazowa ma post-processing wyłączony i AA ustawione na None. Końcowa kamera arms overlay ma post-processing włączony, SMAA High oraz maskę warstwy `PostProcessing`, dzięki czemu post-processing wykonywany jest na końcu stosu.
- Kod odtwarza poprzednie ustawienia kamer po respawnie, zmianie konfiguracji oraz zniszczeniu komponentu.

### `Tutorial_scene`, assety i bake

- Scena zawiera `EnvironmentLightingRoot` oraz Mixed Afternoon Sun w trybie Baked Indirect, procedural sky i fog.
- Dodano 5 lokalnych Mixed accent lights. Po korekcie ostrzeżenia o redukcji atlasu cieni nie rzucają one realtime shadows.
- Dodano 5 nowych focal reflection probes; zachowano 3 istniejące baked outputs.
- APV Probe Volume ma rozmiar `90 × 24 × 90`, a aktywnym scenariuszem jest `Afternoon`.
- Assety konfiguracji i wyniki bake'u znajdują się w `Assets/Settings/Lighting` oraz `Assets/Scenes/Tutorial_scene`.
- Korekcyjny bake `16b26b1ce24c432a871e718d2eabd29b` zakończył się sukcesem po 101542 ms. Wygenerował 2 lightmapy 1024 oraz prawidłowe dane scenariusza APV: 8 cells i niepuste `CellData`, `Optional`, `Shared`, `Bricks` oraz `Support`.
- Pierwszy bake został świadomie zastąpiony po wyłączeniu starego `Directional Light`, aby finalne dane GI odpowiadały zatwierdzonej konfiguracji sceny.

## Wyniki walidacji

- EnvironmentLighting EditMode: 10/10 passed.
- Camera stack PlayMode: 1/1 passed.
- TutorialBoot: 1/1 passed.
- Smoke tests `FPP_scene`, `MainMenu`, `MultiplayerStart` i NGO: clean. `FPP_scene` nadal raportuje 29 wcześniej istniejących pustych slotów materiałów; nie są regresją tej zmiany.
- Finalny ścisły Gameplay preflight: **PASSED**, exit code 0. Raport: `D:\Programy\UnityProjects\RageQuitting\Artifacts\Validation\20260911T171041Z-f07ec8d2\summary.json`.
- Poprzednia finalna próba `20260911T170314Z-845df947` wykryła nowe ostrzeżenia analyzerów oraz redukcję shadow atlas; oba problemy poprawiono przed finalnym runem. Wcześniejszy run `20260911T163240Z-cb11c9b4` był ograniczony sandboxem i nie stanowi finalnego dowodu akceptacji.
- Zestawy porównawcze znajdują się w `Artifacts/LightingShowcase/Before` i `Artifacts/LightingShowcase/After`, po 4 kadry każdy.

## Odbiór wizualny, ograniczenia i dług

- Końcowy art sign-off należy do użytkownika. Obecny proceduralny horyzont jest mocno limonkowo-żółty i wymaga oceny oraz ewentualnego tuningu.
- Kadry porównawcze są szerokie i pochodzą ze Scene View, dlatego nie zastępują przeglądu FPP ani inspekcji w Frame Debuggerze.
- Nie wdrożono dynamicznego dnia/nocy, pogody, TAA ani adaptera NGO. Tylko wariant `Afternoon` jest przygotowany do odbioru artystycznego.
- Przyszły kierunek GI — APV Scenarios kontra Sky Occlusion — pozostaje nierozstrzygnięty.
- Legacy PPSv2 pozostawiono bez zmian jako jawny dług techniczny.
- Ręcznej weryfikacji nadal wymagają Frame Debugger, Rendering Debugger oraz zbliżenia pod kątem shadow acne i peter-panning. Profil GPU 1080p nie jest gate'em akceptacji tego pilota.
- Istnieje niskiego wpływu rozbieżność: zapisany `Tutorial_scene` Scene View ma `fogMode = ExponentialSquared`, natomiast runtime controller stosuje `Exponential`. Nie wpływa to na baked GI.

## Rekomendacja wykonawcza

Pozostać przy URP i zbudować własną, kontrolowaną konfigurację renderingu zamiast migrować do HDRP. Najpierw wykonać pilot w `Tutorial_scene`, następnie przenieść sprawdzony wariant do `FPP_scene`, a na końcu do menu i lobby. Docelowy obraz powinien wspierać ciepły, przytulny, stylizowany low-poly fantasy: czytelne kolory, delikatne światło pośrednie, subtelny bloom i mocne ugruntowanie obiektów bez ciężkich efektów filmowych.

Kluczowe ograniczenie techniczne: gameplay korzysta ze stosu kamer world + arms. Post-processing powinien działać raz, na ostatniej kamerze stosu. TAA jest niezgodne z camera stacking, dlatego bazą powinno być SMAA, a na najniższym tierze FXAA.

## Historyczny baseline przed wdrożeniem

- Unity `6000.3.18f1`; URP i Shader Graph `17.3.0`; liniowa przestrzeń barw i SDR.
- Domyślny pipeline to `Assets/StarterAssets/Environment/RenderPipelineProfiles/StarterAssetsURPAsset.asset`. Sześć poziomów jakości nie ma override'u pipeline, więc wszystkie używają tego samego assetu, mimo obecności sześciu assetów Starter Assets.
- Pipeline: HDR włączone, render scale 1, MSAA wyłączone, Forward, główne cienie 2048, 1 kaskada, dystans 50, soft shadows wyłączone, cienie additional lights wyłączone, depth/opaque texture wyłączone.
- Renderer nie ma Renderer Features, więc SSAO nie jest aktywne.
- Aktywny poziom jakości to `Ultra`, VSync = 1. `QualitySettings.antiAliasing` = 0.
- `Assets/DefaultVolumeProfile.asset` jest domyślnym profilem URP, lecz efekty są neutralne: brak tonemappingu, bloom/color adjustments/vignette = 0, a motion blur/DoF/chromatic aberration = 0.
- `Assets/StarterAssets/FirstPersonController/Prefabs/MainCamera.prefab` ma post-processing i AA wyłączone. `PlayerFirstPersonArms.cs` tworzy końcową kamerę overlay i również wyłącza na niej post-processing.
- Nie znaleziono Global ani Local Volume w scenach/prefabach gry. Kamera `MultiplayerStartScene` ma post-processing wyłączone.
- `FPP_scene` ma jedno Directional Light wyglądające na Mixed, ciepły skybox, baked lighting data, 3 reflection probes i 1 legacy Light Probe Group. Lighting Settings włączają baked GI i realtime environment, bake resolution 16 oraz 6 bounces.
- `FPP_scene` używa też niebieskawej mgły liniowej (koniec około 300), ciepłego sky ambient i własnego `SkyboxLiteWarm`; w `MainMenuScene` i `MultiplayerStartScene` fog jest wyłączony. To istniejący element stylu FPP, nie obowiązkowa rekomendacja dla wszystkich scen.
- Dane oświetlenia `FPP_scene` wskazują katalog ThirdPersonController, a Lighting Settings — FirstPersonController. Własność i aktualność tych danych są niepewne.
- `Tutorial_scene` nie ma bezpośrednio serializowanych świateł, Volumes ani probes. Niejawne zależności prefabów sceny zawierają tylko `MainCamera.prefab` z komponentem Camera; nie znaleziono Light, Volume ani ReflectionProbe. Głęboko runtime-created lighting nie zaobserwowano w wyszukiwaniu kodu.
- Legacy `Assets/Post-processing Profile.asset` istnieje, ale nie znaleziono referencji do jego GUID. Pakiet PPSv2 `3.5.4` jest nadal zainstalowany.

## Historyczne luki i ryzyka baseline'u

- Brak realnych tierów jakości renderingu: nazwy jakości nie zmieniają URP assetu.
- Brak jawnego Volume i wyłączony post-processing na ścieżkach kamer.
- Niewłaściwe włączenie post-processingu na kamerze bazowej mogłoby wykonać efekt więcej niż raz lub nie objąć końcowego obrazu rąk.
- TAA nie może być użyte w obecnym stacku kamer.
- Brak SSAO ogranicza kontaktowe cienie i czytelność styku stylizowanych obiektów z podłożem.
- Dynamiczne elementy mostu, gracze, NPC i zasoby potrzebują spójnego oświetlenia pośredniego z probe'ów.
- Dane baked lighting `FPP_scene` mogą być odziedziczone lub nieaktualne; preferowany jest scenowy, projektowy re-bake.
- PPSv2 jest niezgodne z URP. Usunięcie pakietu/profilu wymaga wcześniejszego audytu i osobnej zgody.

## Pierwotnie proponowana architektura

> Rekomendacje z tej sekcji pochodzą z fazy researchu; część została zrealizowana w opisanym wyżej pilocie, a część świadomie odłożona.

1. Utworzyć projektowe URP Asset i Renderer poza `StarterAssets` oraz trzy znaczące tiery: Low, Medium, High.
2. Jawnie przypisać pipeline assets do poziomów Quality; istniejące nazwy Very Low–Ultra można zmapować do tych trzech konfiguracji.
3. Dodać SSAO jako Renderer Feature tylko dla Medium/High. SSAO jest niezależne od Volume post-processing.
4. Utworzyć współdzielone, projektowe Volume Profiles: `Gameplay Base`, `Menu/Lobby` i opcjonalne profile lokalnych stref. Nie edytować generowanego `DefaultVolumeProfile`.
5. W gameplayu uruchamiać post-processing na ostatniej kamerze arms overlay; kamery pojedyncze menu/lobby skonfigurować osobno.
6. Zbudować światło wokół jednego Mixed Directional Light, baked indirect dla statycznego środowiska oraz probe'ów dla obiektów ruchomych.

## Pierwotna macierz startowa tierów jakości

Poniższe wartości są hipotezami startowymi do profilowania, nie ustawieniami finalnymi.

| Obszar | Low | Medium | High |
|---|---|---|---|
| Rendering path | Forward | Forward | Forward; Forward+ tylko gdy pomiar wykaże korzyść przy wielu światłach |
| AA | FXAA | SMAA | SMAA |
| Main shadows | hard/low | soft low | soft medium |
| Cascades | 1–2 | 2 | 2–4 |
| Shadow distance | ok. 50 m | 50–70 m | 60–80 m |
| SSAO | wyłączone | half-resolution, mały radius | włączone, nadal umiarkowane |
| Reflection probes | podstawowe | blending | blending + box projection, gdzie pomaga |

Dla map około 90 × 90 m nie należy automatycznie podnosić cieni do 150 m / 4096. Najpierw zmierzyć koszt i jakość w docelowym kadrze.

## Bazowy post-processing

- Tonemapping: zacząć od `Neutral`, porównać A/B z `ACES` pod kątem zachowania żywych kolorów.
- Color Adjustments / White Balance: umiarkowana korekta spójności palety, bez zastępowania dobrego oświetlenia.
- Bloom: bardzo subtelny, z kontrolą clippingu jasnych elementów.
- Vignette: minimalna albo wyłączona.
- Motion Blur, Depth of Field, Chromatic Aberration i Lens Distortion: domyślnie wyłączone w FPP; ewentualne opcje komfortu dopiero po decyzji użytkownika.
- Volume Mask musi wskazywać właściwą warstwę `PostProcessing`.
- Nie łączyć domyślnie kilku kosztownych metod AA; 2× MSAA rozważyć dopiero po profilowaniu.

## Strategia światła i probe'ów

- Jedno Mixed realtime Directional Light zapewnia kierunek i dynamiczne cienie; statyczne środowisko otrzymuje baked indirect.
- Ruchome mosty, gracze, NPC i zasoby nie powinny współtworzyć lightmap. Muszą odbierać GI przez probes.
- Dodać baked reflection probes wokół wizualnie odmiennych obszarów: camp, workshops, cave i river; dobrać bounds i box projection do lokalnej geometrii.
- APV jest mocnym kandydatem w Unity 6 dla licznych dużych/ruchomych obiektów i per-pixel probe lighting. Najpierw porównać mały pilot APV z legacy Light Probe Groups, bo APV zmienia workflow i zwiększa koszt bake'u, pamięci oraz storage.
- Screen-space shadows potraktować jako opcjonalny eksperyment profilujący, nie pierwszy krok.

## Pierwotnie rekomendowane etapy implementacji

1. Fundament: własne URP/Renderer assets, mapowanie jakości i bezpieczne wartości bazowe.
2. Integracja camera stack + Volume dla gameplayu oraz osobne kamery menu/lobby.
3. Pilot oświetlenia, probe'ów i SSAO w `Tutorial_scene`.
4. A/B wizualne i profil GPU; korekta parametrów.
5. Rollout do `FPP_scene`, potem lobby/menu; scenowy re-bake i audyt danych lighting.
6. Audyt pozostałości PPSv2; usunięcie pakietu/profilu tylko po potwierdzeniu braku zależności.

## Historyczny plan walidacji

- Wykonać stałe kadry before/after z identyczną kamerą i ekspozycją.
- Użyć Rendering Debugger i Frame Debugger do potwierdzenia liczby passów, SSAO, cieni i post-processingu.
- Profilować GPU w buildzie Windows 1080p/60 przy reprezentatywnym obciążeniu host + klient + NPC + fizyka.
- Porównać Low/Medium/High oraz sprawdzić zgodność rąk z world camera, skybox/reflections, shadow acne i peter-panning, probe leaks, bloom clipping oraz czytelność interakcji.
- Osobno przejść ścieżki kamer menu/lobby.
- Finalny odbiór wizualny należy do użytkownika. W pierwotnej fazie researchu nie uruchamiano Play Mode, testów ani buildów; nie opisuje to późniejszej implementacji i jej wyników walidacji podanych wyżej. Konsola nie stanowiła wtedy czystego baseline'u: dwa błędy pochodziły z timeoutów dwóch zbyt szerokich, read-only zapytań Pipeline; zawężone skany zakończyły się powodzeniem i nie zmieniły stanu projektu.

## Decyzje oczekujące w fazie researchu

> Poniższe pytania były otwarte podczas researchu; dla wdrożonego pilota zostały rozstrzygnięte przez decyzje i ograniczenia opisane w sekcji powdrożeniowej.

1. Czy bazowym targetem jest ciepły, słoneczny cozy look, czy inna pora dnia/paleta?
2. Czy oświetlenie pozostaje statyczne, czy wymagane są dynamiczne dzień/noc lub pogoda?
3. Jaki jest minimalny GPU oraz docelowa rozdzielczość i oczekiwany tier 60 FPS?
4. Czy pilot ma porównać APV z legacy Light Probe Groups?
5. Czy Motion Blur, DoF i inne efekty mają być dostępne jako opcjonalne ustawienia?

## Źródła

- [URP: integrated post-processing i niezgodność PPSv2](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/integration-with-post-processing.html)
- [Dodawanie post-processingu: kamera, Volume i maska](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/add-post-processing.html)
- [Camera stacking: post-processing na ostatniej kamerze](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/cameras/camera-stacking-concepts.html)
- [Anti-aliasing i ograniczenia TAA](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/anti-aliasing.html)
- [Aktywny Render Pipeline Asset i override'y jakości](https://docs.unity3d.com/6000.1/Documentation/Manual/srp-setting-render-pipeline-asset.html)
- [Lighting Modes](https://docs.unity3d.com/6000.0/Documentation/Manual/LightModes-introduction.html)
- [APV: koncepcje](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/probevolumes-concept.html) i [użycie](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/probevolumes-use.html)
- [SSAO: dodanie Renderer Feature](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/add-ssao-renderer-feature-to-renderer.html) i [referencja](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/ssao-renderer-feature-reference.html)
- [Optymalizacja cieni](https://docs.unity3d.com/6000.0/Documentation/Manual/shadows-optimization.html)
- [Ustawienia URP Asset](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/universalrp-asset.html)
- [Tonemapping](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/post-processing-tonemapping.html)
- [Forward i Forward+](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/rendering/forward-rendering-paths.html)
- [Reflection Probes](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/lighting/reflection-probes-introduction.html)
