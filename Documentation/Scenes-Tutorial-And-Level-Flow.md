# Sceny, tutorial i przepływ poziomu

## Status

**Gotowe dla dwóch scen gameplayowych.** `Tutorial_scene` jest blockoutem
pełnego nowego mostu i zawiera dodatkowe systemy edukacyjne oraz testowe.

## `MultiplayerStartScene`

Zawiera NetworkManager/lobby, lokalny limit FPS i Camera Motion. Host po
utworzeniu pokoju wybiera:

- `FPP_scene` - wcześniejszy poziom;
- `Tutorial` - nowy blockout.

Klient widzi stan lobby, ale nie może rozpocząć ładowania.

## `FPP_scene`

- podstawowy gameplay multiplayer/singleplayer;
- scenowy fallback `PlayerNew`;
- kamera i Cinemachine;
- spawn points;
- level manager, timer i stary katalog mostu/fabryk;
- NavMesh oraz sieciowe obiekty scenowe.

Scena nie powinna przejmować tutorialowych katalogów i tekstów przez zmiany
prefabów bazowych.

## `Tutorial_scene`

Główne strefy:

- obóz i cztery spawn pointy;
- zagajnik oraz `ResourcePopulationZone`;
- Carpenter Table;
- stojak Axe, Pickaxe, Shovel, Industrial Hammer i Wrench;
- kopalnia Iron/Coal;
- Blast Furnace;
- północny i południowy spawner Beaver;
- testowa koza oraz GoatPushZone;
- budowa mostu i sekcja siedmiu paneli;
- dekoracyjna rzeka oraz marker mety.

Rzeka i meta nie stanowią samodzielnego warunku zakończenia. Victory wynika z
ukończenia etapów mostu.

### Łańcuch produkcji metalu

Tutorialowy Blast Furnace ma cztery receptury zasobów: paczki gwoździ, zestawy
śrub i nakrętek, zestawy płyt łączących oraz zestawy kotew fundamentowych.
Produkty trzeba fizycznie przenieść do Carpenter Table, gdzie są składnikami
sześciu aktywnych rodzajów części mostu.

W kopalni znajduje się `IronResourcePopulationZone` skonfigurowana dla
`Iron Vein`: minimum `1`, kontrola co `2 s`, cooldown `15 s` i box około
`12 x 4 x 12 m`. Dwie początkowe żyły dają sześć samorodków; po ich wydobyciu
strefa odtwarza kolejną żyłę, aby brak żelaza nie powodował softlocka.

## Managery wymagane w scenie gameplayowej

| Obiekt/komponent | Rola |
|---|---|
| Main Camera + Cinemachine | Widok lokalnego ownera |
| `PlayerNew` scene fallback | Bezpośredni singleplayer |
| `PlayerSpawnManager` | Dynamiczni gracze NGO |
| `GameplayManager` | Etapy mostu |
| `GameTimerManager` | Waiting/Running/Victory/Defeat |
| `Bridge` | Kontener holderów |
| `EventSystem` | UGUI |
| `NavMeshSurface` | AI |
| level-specific managers | Restart, spawners, stage info |

## Timer tutorialu

Tutorial ma `waitForStartSignal = true`, dlatego:

- stan początkowy to `Waiting`;
- HUD timera jest ukryty;
- tylko host może użyć `Start timer` w menu Tab;
- klient widzi nieaktywny przycisk;
- wielokrotne wywołanie nie resetuje czasu;
- restart sceny przywraca pełny czas i Waiting.

### `GameTimerUI`

| Pole | Znaczenie |
|---|---|
| `timerText` | Tekst czasu |
| `timerProgressBar` | Image z poprawnym Fill Method |
| `timerVisualRoot` | Cały HUD ukrywany w Waiting |
| `targetCanvas` | Lokalny Canvas playera |
| `anchoredPosition`, `hudSize` | Runtime layout |
| `normalColor`, `warningColor` | Kolory |
| `warningThreshold` | Sekundy do trybu ostrzegawczego |
| `pulseSpeed` | Częstotliwość pulsowania |

### `RestartLevelUI`

| Pole | Znaczenie |
|---|---|
| `playerInput` | Toggle menu i blokada lokalnego gameplayu |
| `restartController` | Request restartu |
| `panelRoot` | Level Controls |
| `restartButton` | Host-only restart |
| `startTimerButton` | Host-only start |
| `availabilityText` | Stan host/client/timer |
| `panelSize` | Stabilny rozmiar panelu |

## Tekstowe instrukcje etapów

`BridgeStageInfoManager` istnieje tylko w tutorialu i subskrybuje lokalny event
zmiany zsynchronizowanego etapu.

Każdy `BridgeStageInfoEntry` ma:

| Pole | Znaczenie |
|---|---|
| `componentType` | `BridgeComponentSO` |
| `stage` | Docelowy etap |
| `title` | Nagłówek |
| `message` | Tekst wieloliniowy |

Wiadomość pokazuje się raz na typ części i stage podczas jednego uruchomienia
sceny. Late join nie odtwarza historycznych wiadomości. Nowa wiadomość
zastępuje poprzednią. Escape zamyka ją tylko lokalnie i nie blokuje gameplayu.

## NavMesh

- Każda scena z aktywnym NPC musi mieć wypieczony NavMesh.
- Agent parameters muszą odpowiadać rozmiarowi prefaba.
- Mountable i dynamiczne zasoby nie powinny przypadkowo wejść do bake jako
  stała geometria.
- Rampy gotowego mostu muszą mieć collider umożliwiający wejście i NavMesh
  odpowiedni do zamierzonego ruchu NPC.
- Goat charge dodatkowo sprawdza krawędzie NavMesh.

## Scene overrides

W tutorialu override'ami są między innymi:

- katalog Carpenter Table;
- katalog Blast Furnace z czterema metalowymi półproduktami;
- lista i kolejność etapów mostu;
- component IDs;
- prerequisites workflow;
- BridgeDeckSection;
- dwa grupowe spawnery bobrów oraz testowa koza;
- population zone;
- stage info entries;
- timer waiting.

Nie używaj `Apply All` bez sprawdzenia `FPP_scene`, ponieważ część różnic jest
celowym ustawieniem poziomu.

### Bobry w tutorialu

`BeaverSpawner_North` i `BeaverSpawner_South` używają dwóch kumulujących się
grup. `Beaver Scouts` jest dostępna od początku, a `Beaver Defenders` odblokowuje
się po przekroczeniu progu dwóch historycznych spawnów grupy skautów, czyli po
trzecim udanym spawnie skauta przez dany spawner.

Każda grupa ma limit jednej aktywnej instancji, a spawner globalny limit dwóch
NPC. Historia i odblokowanie są lokalne dla instancji spawnera oraz resetują się
po restarcie sceny. Obrońca używa tej samej `BeaversFaction`, więc może reagować
na zaatakowanie skauta także pochodzącego z drugiego spawnera, jeśli zdarzenie
znajduje się w jego zasięgu alarmu.

Każdy spawner ma `NPCDownedPlayerDropPoint` na NavMesh przy denie. Po powaleniu
własnego celu obrońca może odnieść go do tego punktu albo wybrać jedną z dwóch
testowych `GoatPushZone`. Strefy posiadają osobne `CarrierThrowPoint` ustawione
lokalnie około `(0, -0.75, -0.5)`, czyli `1.5 m` bliżej krawędzi niż zachowane
`ApproachPoint`. Punkt wyrzutu używa progu dotarcia `0.15 m`; den `0.65 m`.

Podczas transportu licznik respawnu gracza jest zatrzymany. Wznawia się przy
odłożeniu lub wyrzuceniu, przed aplikacją external impulse. Bezpieczny drop przy
denie jest ponawiany maksymalnie przez `1 s`, po czym używany jest fallback obok
bobra.

## Restart i late join

Restart:

- usuwa dynamiczne obiekty sceny;
- przeładowuje aktualną gameplay scene;
- tworzy świeżych graczy bez rozłączenia;
- resetuje timer, stage info memory, NPC, zasoby i fabryki.

Late join:

- otrzymuje aktualny stan timer/bridge/fabryk;
- dostaje nowy player object w obozie;
- nie otrzymuje historycznych tutorial messages.

## Kontrola przed zapisaniem sceny

1. Sprawdź unikalność `componentID`.
2. Sprawdź NetworkObjects i prefab registration.
3. Sprawdź wszystkie spawn pointy.
4. Sprawdź katalogi fabryk i storage whitelist.
5. Sprawdź ghost/final/work point visuals w stanie początkowym.
6. Wypiecz i przetestuj NavMesh.
7. Uruchom hosta oraz klienta.

## Ograniczenia

- Tutorial nie ma jeszcze pełnego systemu kroków wymuszających kolejność
  edukacyjną; stage info jest informacyjne.
- Meta i rzeka są głównie elementami blockoutu.
- World-space markery/szyldy wymagają ręcznej kontroli wysokości i orientacji.
- Część nowych rodzajów mostu ma SO, ale nie pełną integrację scenową.
