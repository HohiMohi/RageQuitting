# Referencja konfiguracji Unity

## Jak czytać tę referencję

Pola są pogrupowane według komponentu. Tabele systemowe w pozostałych
dokumentach zawierają szczegółowe znaczenie gameplayowe. Ten plik służy jako
checklista podczas konfiguracji prefaba, SO lub sceny.

## Typy pól

| Typ | Zasada |
|---|---|
| SO/data | Konfiguruje rodzaj contentu i powinien być współdzielony |
| Prefab reference | Musi wskazywać child/component tego prefaba |
| Scene reference | Zależność konkretnego poziomu |
| Runtime/debug | Odczyt diagnostyczny; nie ustawiać jako content |
| LayerMask | Musi odpowiadać Project Settings/Physics |
| Network prefab | Wymaga `NetworkObject` i registry |

## ScriptableObjecty

| SO | Pola |
|---|---|
| `BaseResourceSO` | identity/prefab/icon, impact surface, durability, carryability, carrier counts, speed/stamina penalties, anchors, rotation offset, physics profile, fuel, destruction recipes |
| `MountableBridgeComponentSO` | identity/prefab, recipe, bridge type, carrier limits, jawne anchory, profil fizyki, furnace i carpenter dimensions |
| `ProductionRecipeSO` | identity/icon, składniki, typ i ilość wyjścia oraz opcjonalne parametry pieca |
| `BridgeComponentSO` | identity/final prefab, category, simple assembly i sześć opcjonalnych workflow |
| `EquippableItemSO` | identity/prefab, slots/two-handed, range, cooldown, combat/resource damage, work power, action profile, impact impulse, movement penalty, repeatability i enum |
| `EquippableActionProfileSO` | fazy akcji, pozy narzędzia/rąk, movement multiplier, camera kick, feedback strength i swing audio |
| `CarryPhysicsProfileSO` | Tryb `DirectYaw`/`PhysicalPointGrip`, Rigidbody, point grip, tether, limity udźwigu oraz fully-staffed load distribution i leveling |
| `ExternalImpulseProfileSO` | initial velocity, decay, gravity, control, clamps i forced drop |
| `NPCDefinitionSO` | identity, faction, behavior, prefab/visual, stats i AI ranges |
| `NPCSpawnGroupSO` | nazwa, waga, limit, ważone definicje, tryb `All/Any` i warunki |
| `NPCSpawnUnlockConditionSO` | Always, timer, globalny etap, etap części, manual signal albo historyczny spawn count |
| `NPCSpawnSignalSO` | Typowany identyfikator ręcznego odblokowania grupy |
| `NPCInterestProfileSO` | allow-any oraz whitelist zasobów/części |
| `NPCDestructionProfileSO` | resource-to-tool rules |
| `NPCFactionSO` | stabilny ID i nazwa |
| `NPCFactionRelationshipMatrixSO` | default relation oraz directed entries |
| `GoatStandingTargetProfileSO` | resources, allow-all mountable i whitelist mountable |
| workflow SO | narzędzia, zakresy kroków oraz wymagany progress |

SO powinien zawierać dane rodzaju, nie referencje do konkretnej instancji
scenowej.

## Player prefab - wymagane komponenty

| Komponent | Pola do przypięcia/strojenia |
|---|---|
| `FirstPersonController` | speed, acceleration, jump/ground, Cinemachine target, look feel, stamina i shared-carry |
| `PlayerInputNew` | Input Action Asset i event routing; większość stanu jest runtime |
| `PlayerInteractionNew` | camera fallback, distance, assist, hold/body/player anchors |
| `PlayerActionController` | fallback action values, tolerancja zasięgu/czasu RPC i action holder; faza profilowana jest runtime-only |
| `PlayerInventory` | dwa sloty, stany `Empty/Occupied/Reserved` i pełny catalog enum -> SO |
| `PlayerHealth` | HP, regen, delays |
| `PlayerExternalImpulseController` | referencja controller/interaction/health, jeśli widoczna w Inspectorze |
| `PlayerNetworkSetup` | camera target, local/remote visual, Canvas i owner-only components |
| `PlayerFirstPersonArms` | references, render layer, pose, locomotion, legacy action, turn lag, tool visual, two-handed grip oraz opcjonalny composer/audio source |
| feedback components | controller/composer/input/health oraz amplitudy/smoothing |

`PlayerCameraFeedbackComposer` sumuje obecnie kanały movement, turn, damage i
action. Nie należy bezpośrednio nadpisywać transformu jego output targetu z
nowego systemu feedbacku.

### Impact feedback

| Komponent/SO | Pola |
|---|---|
| `ActionImpactEffectSpawner` | default ParticleSystem, lifetime, `surfaceFeedback`, fallback volume |
| `ActionImpactFeedbackEntry` | surface type, opcjonalny ParticleSystem, klip i volume |
| `BaseResourceSO` | `impactSurfaceType` |

Brak wpisu lub assetu jest poprawną konfiguracją i uruchamia proceduralny
fallback. Docelowe klipy/prefaby przypisuje się per powierzchnia na prefabie
gracza.

### Player UI

| Komponent | Pola |
|---|---|
| `PlayerHealthUI` | `playerHealth`, fill image, value text, colors |
| `PlayerStaminaUI` | controller, fill image, value text, normal/warning colors, blink speed |
| `PlayerInventoryUI` | inventory, dwa zestawy slot references, `reservedSlotLabel` i kolory stanu |
| `PlayerHeldObjectUI` | interaction, root, icon/name/carry count i kolory |
| `LookingAtComponentUI` | interaction/action, panel root, prompt prefabs/text i progress UI |
| `PlayerCrosshairUI` | dot graphic, input, health, diameter/colors/outline |
| `CrosshairDotGraphic` | fill color, outline color i grubość |
| `PlayerTargetHighlightController` | interaction, materiał outline, biały kolor, width oraz fade in/out |
| `PlayerBridgeStageInfoUI` | panel, title/message/hint i input |
| `RestartLevelUI` | input, restart controller, root, dwa przyciski, status i panel size |
| `PlayerRespawnPromptUI` | health/input oraz panel/text |
| `PlayerDamageFeedback` | health, composer, flash image, audio i parametry shake |

Wszystkie elementy lokalnego HUD muszą znajdować się pod Canvasem wyłączanym
dla remote playerów.

## Prefab zasobu

Wymagane:

- `NetworkObject`;
- `BaseResourceNew`;
- `Rigidbody`;
- co najmniej jeden collider;
- server-authoritative transform/sync;
- opcjonalny `SharedCarryPhysicsBody`;
- visual i durability UI.

| Pole komponentu | Setup |
|---|---|
| `baseResourceSO` | Właściwy asset |
| `resourceDurability` | Runtime; startuje z SO |
| ground mask/offset/distance | Podłoże i maksymalna sonda |
| clearance/follow speed | Stabilność wysokości |
| max vertical placement | Zakaz teleportu między piętrami |

Jeśli `canBeCarried=false`, `BaseResourceNew` ustawia Rigidbody jako kinematic
i wyłącza gravity. Nie trzeba ręcznie utrzymywać tego na każdym prefabie.

## Prefab mountable

Wymagane komponenty analogiczne do zasobu, ale źródłem danych jest
`MountableBridgeComponentSO`. Collider powinien odpowiadać visualowi produktu,
a attach points uwzględniać jego rzeczywistą orientację shared-carry.

Aktualne Foundation, Abutment, Main Girder, Cross Beam, Diagonal Bracing i Deck
Panel używają osobnych profili `PhysicalPointGrip`. Nie przypisuj im wspólnego
profilu: masa, damping, limit przechyłu i udźwig na carriera są dostrojone do
konkretnej geometrii.

W tych sześciu SO `minAmountOfPlayersNeeded = 1` jest tymczasowym ustawieniem
testowym pozwalającym rozpocząć carry solo. Nie ustawiaj z tego powodu
`recommendedCarriers` na `1`: ta wartość steruje fizyką i wynosi `3` dla
Foundation, Abutment i Main Girder oraz `2` dla Cross Beam, Diagonal Bracing i
Deck Panel. Stabilizacja fully-staffed uruchamia się dopiero po osiągnięciu tej
docelowej obsady.

Każdy z tych SO ma osiem `carryAttachLocalPoints` w kolejności: cztery narożniki,
przód, prawa strona, tył, lewa strona. `maxCarriers` nadal wynosi `2` albo `3`;
osiem punktów oznacza pulę możliwych pozycji, nie ośmiu jednoczesnych holderów.
Standardowy margines logicznego anchora wynosi około `0.60 m` poza colliderem.
Rampa Abutment używa `0.90 m` oraz `Y = 0.30 m`, aby placement gracza nie
przecinał pochyłego decku i bocznych belek.

W profilach części pozostaw `projectGripForcesToColliderSurface=true`: preview i
placement korzystają z logicznego punktu poza modelem, ale siła jest przykładana
na colliderze. `limitPointGripLiftByCarrierCapacity` musi pozostać aktywne, aby
pojedynczy holder nie przejmował całego ciężaru wieloosobowej części.

`PhysicalPointGrip` używa stałej wysokości każdego `carryAttachLocalPoint`.
Ruch kamery nie reguluje już wysokości chwytu, a pole
`maximumGripHeightOffset` nie istnieje. Przy pełnej obsadzie części mostu
`stabilizeWhenFullyStaffed` uruchamia serwerowy solver rozkładu pionowego
podparcia. Solver kompensuje residualny moment pełnej pasywnej siły chwytu po
wcześniejszym, jednokrotnym odjęciu kompensacji rollu. Ustawienia tej sekcji
profilu:

| Pole | Typ/jednostka | Znaczenie |
|---|---|---|
| `fullyStaffedLoadDistributionRegularization` | współczynnik | Preferuje równy rozkład, gdy kilka rozwiązań podobnie redukuje moment |
| `fullyStaffedLevelingTorque` | moment | Sprężyna przywracająca pitch/roll zapisany przy rozpoczęciu carry |
| `fullyStaffedLevelingDeadZone` | stopnie | Tolerancja przechyłu bez korekty |
| `fullyStaffedTiltDamping` | damping | Tłumienie prędkości pitch/roll |
| `fullyStaffedMaximumTorque` | moment | Limit kompensacji pozostałego momentu i poziomowania |
| `fullyStaffedStabilizationBlendDuration` | sekundy | Czas płynnej aktywacji po dołączeniu ostatniego holdera i wygaszenia po jego utracie |

Solver respektuje `pointGripLiftCapacityPerCarrier`. Jeśli suma limitów
holderów nie może pokryć ciężaru, obiekt zachowuje fizyczne opadanie. Dla sześciu
części mostu profile mają tę stabilizację włączoną; profil Wooden Log pozostawia
ją wyłączoną. Nie przywracaj starej kompensacji `fullyStaffedPassiveTorqueCompensation`,
ponieważ została zastąpiona rozkładem udźwigu i residual torque compensation.

## Fabryki i magazyny

| Prefab | Wymagane referencje |
|---|---|
| Base storage | whitelist i withdraw point |
| Carpenter Table | `productionRecipeSOArray`, BaseFactory refs, switch, dwie korby, dial UI, minigame, zakresy wymiarów |
| Blast Furnace | `productionRecipeSOArray`, BaseFactory refs, FurnaceStorage, temperatura/fuel UI, bellows/switch |
| Main Storage | wymagane typy części i UI |

Katalog fabryki jest tablicą `ProductionRecipeSO`. `productType` ogranicza typ
wyjścia: Carpenter Table oczekuje części mostu, a Blast Furnace zasobu.
Tutorialowe katalogi pozostają scene override'ami. Pola
`mountableBridgeComponentSOArray` są obecnie wyłącznie warstwą kompatybilności.

### `ProductionRecipeSO`

| Pole | Typ/jednostka | Znaczenie |
|---|---|---|
| `recipeName` | string | Nazwa w UI |
| `recipeIcon` | Sprite | Ikona receptury |
| `requiredResources` | `RequiredResource[]` | Składniki pobierane z magazynu |
| `productType` | enum | `MountableBridgeComponent` albo `BaseResource` |
| `mountableBridgeComponentOutput` | SO | Wyjście Carpenter Table |
| `baseResourceOutput` | SO | Wyjście Blast Furnace |
| `outputAmount` | sztuki | Wielkość partii, minimum runtime `1` |
| `meltingPoint` | temperatura | Próg naliczania produkcji w piecu |
| `combustionTemperature` | temperatura | Próg naliczania przegrzania |
| `neededProgress` | progress | Ilość prawidłowej obróbki |
| `neededCombustionProgress` | progress | Ilość przegrzania niszcząca wsad |

## Bridge holder

Wymagane:

- `BridgeComponent` z unikalnym ID i SO;
- ghost root z triggerem;
- mounted visual root;
- jawne physical colliders;
- opcjonalny subclass `BridgeConstructionSite`;
- work point children właściwego typu;
- komplet prerequisites.

### `BridgeMountSocket`

Dodawaj ten komponent do holderów używających automatycznego, precyzyjnego
montażu. `targetPose` musi być osobnym childem. Jeśli referencja jest pusta,
runtime używa roota jako fallbacku, ale nie należy wtedy programowo obracać
targetu, ponieważ zmieniłoby to orientację całego holdera.

| Pole | Typ/jednostka | Konfiguracja |
|---|---|---|
| `bridgeComponent` | prefab reference | `BridgeComponent` na tym samym holderze |
| `targetPose` | Transform | Dokładny środek i bazowa rotacja finalnej części |
| `componentCaptureVolume` | BoxCollider trigger | Szeroka strefa kandydata; dla sześciu części około `125%` wcześniejszego rozmiaru |
| `carrierStagingVolume` | BoxCollider trigger | Strefa holderów; około `120%` w X/Z, bez zwiększania wysokości |
| `ghostVisualRoot` | GameObject | Visual docelowej części używany również do bounds feedbacku |
| `positionTolerance` | metry per lokalna oś | Aktualny baseline sześciu części: `(0.40, 0.40, 0.40)` |
| `rotationToleranceDegrees` | stopnie per lokalna oś | Aktualny baseline: `(18, 18, 18)` |
| `maximumLinearVelocity` | m/s | `0.35` |
| `maximumAngularVelocityDegrees` | °/s | `15` |
| `settleDuration` | sekundy | `1` |
| `requireRecommendedCarrierCount` | bool | Wymaga `recommendedCarriers`, nie tylko `minAmountOfPlayersNeeded` |
| `allowedOrientationOffsetsEuler` | Vector3[] | Alternatywne poprawne obroty, zwykle `0°`, opcjonalnie `180°` |
| `positionSpring`, `positionDamping` | assist | Bazowo `12` i `7` |
| `maximumPositionAcceleration` | m/s² | Bazowo `6` |
| `rotationSpring`, `rotationDamping` | assist | Bazowo `8` i `4` |
| `maximumAngularAcceleration` | rad/s² | Bazowo `4` |
| `mountingCollisionClearancePadding` | metry | Tymczasowy margines ignorowania kolizji podczas końcowego ustawiania |
| `feedbackVisibilityDistance` | metry | Bazowo `12` |
| `feedbackBoundsScale` | mnożnik | `1.12` |
| `feedbackBoundsPadding` | metry | Minimum `0.25` poza bounds ghosta |
| `maximumPositionIndicatorLength` | metry | `2.5` |
| `minimumRotationIndicatorRadius` | metry | Minimalny promień łuków, obecnie `0.65` |
| `maximumRotationIndicatorRadius` | metry | Maksymalny promień łuków, obecnie `2.5` |
| `invalidColor`, `positioningColor`, `settlingColor` | Color | Czerwony, żółty i zielony stan lokalnego feedbacku |

Przy nakładających się capture volume aktywny jest tylko kompatybilny socket o
najmniejszym znormalizowanym błędzie pozycji i rotacji. Nie próbuj rozwiązywać
tego przez ręczne wyłączanie sąsiednich triggerów.

W `BridgeDiagonalBracingConstructionSite` orientacja scenowa steruje bazową
rotacją targetu: `ForwardSlash = +45°`, `BackSlash = -45°`. Te same kąty muszą
mieć `MountTargetPose` i ghost. `allowedOrientationOffsetsEuler` zachowuje
wariant `0/180°`. Późniejszy `alignmentStep` obraca wyłącznie zamontowany visual
i nie może być zapisywany w target pose.

### Work point

Każdy work point ma:

- ID z dedykowanego enuma;
- referencję construction site;
- collider wykrywania;
- visual/renderer;
- prompt zależny od stage;
- opcjonalny highlight/pulse.

Nieaktywny punkt musi wyłączyć collider i visual, aby nie przejął targetingu.

## BridgeDeckSection

1. Utwórz wymaganą liczbę panel slotów w edytorze.
2. Uporządkuj `panelSlots` od początku do końca sekcji.
3. Nadaj unikalne IDs.
4. Przypisz prerequisites.
5. Ustaw `sectionLength`, `panelGap` i `nominalPanelLength`.
6. Wywołaj/zweryfikuj layout w edytorze.
7. Sprawdź pierwszy/ostatni panel jako full fastening.

## NPC prefab

Wymagane:

- `NetworkObject` i server network transform;
- `NPCBrain`;
- `NavMeshAgent`;
- `NPCHealth`;
- `NPCFactionMember`;
- `NPCAttackController`;
- `NPCCarrier`;
- `NPCStorageInteractor`;
- `NPCAnimationController`;
- `NPCExternalImpulseController`;
- collidery i visual root.

| Komponent | Kluczowe pola |
|---|---|
| Brain | definition, matrix, visual root |
| Agent | radius/height/speed zgodne z modelem i bake |
| Attack | origin, damage/range/angle/delay/layers/LOS |
| Carrier | anchors, distances, path/stuck tuning i collision radius |
| Animation | animator/visual/agent/carrier/health i smoothing |
| Impulse | recovery radius, death/air time, skin i ground probe |

### Goat-only

- `GoatChargeController.visualRoot`;
- `GoatBehaviorSO`;
- opcjonalne `GoatStandingSurface` na celach;
- scenowe `GoatPushZone` z `approachPoint`, `carrierThrowPoint`, direction i
  impulse profile.

### Beaver-only

- `BeaverScoutBehaviorSO`;
- `BeaverDefenderBehaviorSO`;
- interest/destruction profiles;
- `BeaverSpawnerStorageMemory`, jeśli skauci mają współdzielić znane storage i
  `ResourcePopulationZone` po odwiedzeniu bazy;
- `NPCCarrier.downedPlayerCarryAnchor` na grzbiecie;
- `NPCDownedPlayerDropPoint` przy spawnerze/denie.

Prefab `NPC_BeaverDefender` używa osobnego `BeaverDefenderVisual`, ale tego
samego rigu i Animatora co skaut. Root pozostaje w skali `1`; `ModelRoot` ma
skalę `0.4`. `NavMeshAgent` i `CapsuleCollider` należy stroić jawnie, obecnie
mają radius `0.5625` i height `1.5`.

W `BeaverDefenderBehaviorSO` obowiązkowo przypisz `scoutDefinition`.
`maxDefendersPerScout` kontroluje współdzieloną rezerwację eskorty, a
`familyAlertRadius` odległość odbioru serverowego alarmu frakcji.

| Pole `BeaverDefenderBehaviorSO` | Znaczenie |
|---|---|
| `scoutDefinition` | Jedyny typ NPC, który może zostać celem `FollowingScout` |
| `idleDecisionDelay` | Zwłoka przed wyborem skauta albo pozostaniem w idle |
| `followSearchRadius`, `followStoppingDistance` | Zasięg wyboru i dystans formacji |
| `followDestinationRefreshInterval` | Częstotliwość aktualizacji celu NavMesh |
| `maxDefendersPerScout` | Maksymalna liczba globalnych rezerwacji jednego skauta |
| `attackPrepareDuration`, `attackRecoveryDuration` | Timing zwykłego ataku |
| `attackApproachRefreshInterval` | Częstotliwość aktualizacji pościgu |
| `unreachableTargetTimeout` | Czas tolerowania nieosiągalnego celu |
| `familyAlertRadius` | Maksymalna odległość od pozycji alarmu rodzinnego |
| `pushZoneSearchRadius` | Zasięg wyboru GoatPushZone przed pickupem |
| `downedPlayerApproachRefreshInterval` | Repath podczas podejścia i transportu |
| `carryingMoveSpeedMultiplier` | Prędkość z niesionym graczem |
| `dropArrivalDistance` | Próg dotarcia do dena |
| `pushZoneArrivalDistance` | Próg dotarcia do `CarrierThrowPoint` |
| `dropRetryInterval`, `dropAttemptTimeout` | Próby i timeout bezpiecznego dropu |

Asset `BeaverDefenderDefinition` wskazuje `NPC_BeaverDefender`, visual
`BeaverDefenderVisual`, `BeaversFaction` oraz behavior obrońcy. Prefab musi
pozostać wpisany w `DefaultNetworkPrefabs`.

`GoatPushZone.approachPoint` pozostaje punktem dla zachowania kozy.
`carrierThrowPoint` jest opcjonalnym, bliższym krawędzi punktem dla NPC
transportującego gracza; bez niego system używa `approachPoint`. Jawny punkt
musi dać się dopasować do NavMesh i posiadać kompletną ścieżkę.

## Managery sceny

| Komponent | Konfiguracja |
|---|---|
| `GameplayManager` | bridge, component state data, ordered stages |
| `GameTimerManager` | duration i wait signal |
| `PlayerSpawnManager` | player prefab i spawn points |
| `BridgeStageInfoManager` | stage info entries |
| `ResourcePopulationZone` | resource, minimum, box, opcjonalny visit point, cooldown, masks i clearance |
| `NPCSpawner` | prefab, legacy definitions, spawn groups, points, intervals i global limit |

### Strefy odnawiania i Beaver Scout

| Pole/komponent | Setup |
|---|---|
| `ResourcePopulationZone.zoneSize` | Obejmuje wyłącznie właściwy obszar, szczególnie pionowy zakres jaskini |
| `ResourcePopulationZone.visitPoint` | Opcjonalny punkt NavMesh dla wizyty skauta; zostaw pusty dla automatycznego wyboru wewnątrz boxa |
| `BeaverScoutBehaviorSO.resourceZoneSweepArrivalDistance` | Tolerancja dotarcia do punktu wizyty, domyślnie `1.4 m` |
| `BeaverSpawnerStorageMemory` | Komponent na spawnerze bobrów; przechowuje wspólną pamięć magazynów i stref |

Strefa nie potrzebuje collidera do wykrywania przez skauta. NPC porównuje swój
zasięg detekcji z geometrycznym boxem strefy. Lokalna wiedza trafia do pamięci
spawnera dopiero, gdy skaut wróci do bazy lub dostarczy zasób.

### NPC spawn groups

| Asset/pole | Setup |
|---|---|
| `NPCSpawnGroupSO.spawnWeight` | Wartość dodatnia; jest względna wobec innych odblokowanych grup |
| `maxActiveNPCs` | Limit grupy, dodatkowy względem globalnego limitu spawnera |
| `entries` | Każdy wpis wymaga definicji i dodatniej wagi |
| `conditionMode` | `All` albo `Any` |
| `unlockConditions` | Pusta lista odblokowuje grupę od początku |
| timer condition | Czas w sekundach od stanu `Running` |
| global bridge condition | Indeks w `GameplayManager.bridgeBuildingStages` |
| component stage condition | `BridgeComponentSO`, właściwy stage i opcja wszystkich instancji |
| signal condition | Referencja do `NPCSpawnSignalSO` |
| spawn count condition | Zakres globalny/grupowy, ścisły próg `count > threshold` i opcjonalna grupa |

Nie należy używać `NPCFactionSO` jako grupy spawnu. Frakcja definiuje relacje,
a `NPCSpawnGroupSO` skład i progresję populacji.

`NPCSpawner.ActiveNPCCount` jest licznikiem bieżącym.
`TotalSpawnedNPCCount` oraz `GetTotalSpawnedCountForGroup()` są licznikami
historycznymi, resetowanymi przy ponownym uruchomieniu sceny. Pole
`totalSpawnedNPCCount` jest widoczne w Inspectorze wyłącznie diagnostycznie i
nie powinno być konfigurowane jako wartość startowa.

W tutorialu grupa obrońców używa `NPCSpawnCountConditionSO` wskazującego grupę
skautów. Ponieważ porównanie jest ścisłe (`count > threshold`), wartość `2`
odblokowuje grupę po trzecim spawnie skauta.

Konfiguracja używa assetów `BeaverScoutSpawnGroup`,
`BeaverDefenderSpawnGroup` i `BeaverDefenderAfterThreeScouts`. Obie instancje
spawnera mają osobne liczniki historyczne; nie należy oczekiwać, że spawny
północnego spawnera odblokują grupę południowego.

## Lokalne ustawienia

`FrameRateSettings` tworzy singleton przed sceną. Toggle ustawia
`Application.targetFrameRate` na 60 albo brak limitu. Nie zmienia VSync.

`CameraMotionSettings` przechowuje `RotationMotionIntensity` 0-1. Slider lobby
skaluje nowe efekty obrotu. Oba ustawienia są lokalne i nie używają
PlayerPrefs.

## Warstwy i fizyka

Przy zmianie warstw zweryfikuj równocześnie:

- raycast targetingu;
- action target layers;
- resource population masks;
- shared-carry ground mask;
- NPC attack i charge sweep;
- FPP arms render layer;
- kamera/culling mask.

## Procedury tworzenia contentu

### Nowy zasób

1. Utwórz `BaseResourceSO`.
2. Ustaw carryability, durability i receptury.
3. Utwórz wariant `BaseResource_prefab`.
4. Dopasuj visual, collider i Rigidbody.
5. Przypisz prefab do SO.
6. Dodaj NetworkPrefab.
7. Dodaj do storage/fabryki/population profile według potrzeb.

### Nowa receptura fabryki

1. Utwórz `ProductionRecipeSO`.
2. Ustaw nazwę, ikonę i składniki.
3. Wybierz `productType` i przypisz wyłącznie odpowiadające mu wyjście.
4. Ustaw wielkość partii.
5. Dla pieca ustaw temperatury i wymagane progresy.
6. Dodaj asset do `productionRecipeSOArray` instancji fabryki.
7. Zweryfikuj prefab wyjścia, NetworkPrefab i spawn point.
8. Sprawdź wygenerowaną whitelistę magazynu.

### Nowe narzędzie

1. Dodaj wartość enuma na końcu, aby nie przesunąć serializacji.
2. Utwórz `EquippableItemSO`.
3. Ustaw osobno `damage`, `resourceDamage` i `constructionWorkPower`.
4. Utwórz lub przypisz `EquippableActionProfileSO`; bez niego działa legacy flow.
5. Opcjonalnie przypisz `impactImpulseProfile`; działa wyłącznie dla combat targetów.
6. Dla narzędzia dwuręcznego ustaw `inventorySlotsRequired = 2` i dodaj
   `SecondaryGrip` do visuala.
7. Ustaw `actionRepeatability` oraz dodatnią `movementSpeedPenalty`.
8. Utwórz sieciowy prefab świata.
9. Dodaj visual builder/FPP representation.
10. Dodaj do inventory catalog wszystkich graczy.
11. Zarejestruj prefab i umieść source w scenie/storage.
12. Sprawdź slot reservation, chwyt obu dłoni, trafienie, impuls, pudło,
    hold/click, cancel, surface feedback i host/client.

### Nowy NPC

1. Utwórz faction/relacje, jeśli potrzebne.
2. Utwórz behavior SO oraz definition.
3. Utwórz prefab na bazie wspólnego NPC.
4. Dopasuj agent, collider i animację.
5. Dodaj NetworkPrefab.
6. Dodaj definition do spawnera albo instancję testową.
7. Wypiecz/sprawdź NavMesh.
8. Jeśli NPC reaguje na eventy frakcji, zweryfikuj subskrypcję tylko po stronie
   serwera i jej czyszczenie przy `Exit`/despawnie.

### Nowa grupa spawnów NPC

1. Utwórz warunki przez `Create > Scriptable Objects > NPC > Spawn Conditions`.
2. Utwórz `NPCSpawnGroupSO`.
3. Dodaj ważone `NPCDefinitionSO`.
4. Ustaw limit grupy oraz tryb `All/Any`.
5. Przypisz grupę do `NPCSpawner.spawnGroups`.
6. Dla manualnego odblokowania wywołaj na serwerze
   `NotifySpawnSignal(NPCSpawnSignalSO)`.
7. Uruchom `Validate Spawn Configuration`.

## Pola runtime, których nie należy stroić

- `isPickedUp`, holder dictionaries i network input caches;
- current health/stamina/durability;
- current bridge stage/progress;
- current storage amounts;
- factory production state;
- population zone runtime debug;
- current NPC target/state/reservations;
- aktualne UI preview i local drag state.

## Konfiguracja systemu wody

### `WaterBodyProfileSO`

| Pole | Jednostka | Znaczenie |
|---|---:|---|
| `maximumSafeWadingDepth` | m | Maksymalna glebokosc uznawana za bezpieczne brodzenie. |
| `staminaDrainPerSecond` | stamina/s | Staly koszt pobytu w wodzie. |
| `exhaustionWarningDuration` | s | Czas od zera staminy do downed. |
| `unsupportedGraceDuration` | s | Czas bez bezpiecznego podloza do downed. |
| `downedFloatDepth` | m | Zanurzenie roota powalonego gracza ponizej powierzchni. |
| `groundMask` | LayerMask | Warstwy uznawane za podloze przy sondzie brodzenia. |
| `waterNavMeshAreaName` | nazwa | Obszar NavMesh powierzchni wody; w tutorialu `WaterSurface`. |
| `waterNavMeshAreaCost` | mnoznik | Koszt trasy wodnej wzgledem ladu. |
| `surfaceSwimSpeedMultiplier` | 0-1 | Predkosc `SurfaceSwimmer` w wodzie. |

### Komponenty scenowe i prefabowe

| Komponent | Gdzie | Najwazniejsza konfiguracja |
|---|---|---|
| `WaterBody` | root akwenu | Profil, trigger volume, transform powierzchni i lista wyjsc na brzegi. |
| `PlayerStaminaController` | `PlayerNew.prefab` | Serwerowy stan staminy i zrodla drain; zwykle konfigurowany automatycznie przez FPP. |
| `PlayerWaterExposureController` | `PlayerNew.prefab` | Referencje health/stamina/FPP i interwal serwerowej sondy podloza. |
| `RiverBedCleanupZone` | dno akwenu | Trigger obejmujacy dno; usuwa tylko `BaseResourceNew`. |
| `NPCAquaticLocomotionController` | prefab NPC wodnego | Brain, agent, carrier i visual root; ruch oraz bobbing pobiera z definicji NPC. |
| `NPCDefinitionSO` | definicja NPC | `waterTraversalMode`, mnoznik predkosci, `WaterEntry/WaterSurface` oraz `surfaceSwimVisualBobbingAmplitude/Frequency`. |
| `GameplayManager` | manager sceny | `EnableRiverBedResourceRemoval` i `EnableUnsupportedWaterDowning`; obie flagi domyslnie wlaczone. |

Przy tworzeniu nowego akwenu nalezy przygotowac trigger `WaterBody`, osobny cleanup dna, shoreline segments oraz ciagla powierzchnie NavMesh z `WaterEntry` i `WaterSurface`. Dla nowego `SurfaceSwimmer` wymagane sa zgodny agent type, dostep do obu obszarow oraz `NPCAquaticLocomotionController` na prefabie. Tryby `BottomWalker` i `VolumeSwimmer` nie maja jeszcze solvera ruchu.
# Kopanie fundamentów i substancje

| Komponent / asset | Pole | Znaczenie |
|---|---|---|
| `BridgeConstructionWorkflowSO` | `diggingCycleCount` | Liczba cykli rozdrabniania i wybierania ziemi |
| `BridgeConstructionWorkflowSO` | `looseningProgressPerCycle` | Progres łopatą wymagany przed wybieraniem |
| `BridgeConstructionWorkflowSO` | `soilUnitsPerCycle` | Porcje usuwane netto w cyklu |
| `BridgeConstructionWorkflowSO` | `finalExcavationDepth` | Maksymalne pionowe przesunięcie powierzchni w metrach |
| `BridgeConstructionWorkflowSO` | `loosenedSoilHardeningDuration` | Czas aktywnego `SoilRemoval` przed ponownym stwardnieniem; tutorial: `15 s` |
| `BridgeConstructionSite` | `excavationVolume` | Referencja do geometrii i triggera konkretnego wykopu |
| `FoundationExcavationVolume` | `soilSurface` | Transform ruchomej powierzchni wraz z colliderem |
| `FoundationExcavationVolume` | `compact/loosenedSoilMaterial` | Materiały blendowane podczas pracy łopatą |
| `PortableSubstanceContainer` | `capacity` | Maksymalna liczba porcji; obecnie `3` |
| `PortableSubstanceContainer` | `supportedSubstances` | Typy możliwe do przechowywania bez mieszania |
| `PortableSubstanceContainer` | `loosePilePrefab` | Sieciowy prefab tworzony przy wysypaniu |
| `LooseSubstancePile` | `initialUnits` | Startowa liczba porcji dla instancji scenowej; runtime dump ją nadpisuje |
| `BucketRespawnPoint` | `bucketIndex` | Stabilna kolejność wyboru punktów powrotu |

# Przygotowanie betonu

| Komponent / asset | Pole | Znaczenie |
|---|---|---|
| `ContainerSubstanceSO` | `substanceKind` | `Soil`, `Water`, `Gravel` albo `Concrete` |
| `SubstanceExtractionZone` | `sourceId` | Stabilny, zgodny na wszystkich peerach identyfikator niewyczerpywalnego źródła |
| `ConcreteMixerProfileSO` | `drumCapacity` | Pojemność bębna; tutorial: `15` |
| `ConcreteMixerProfileSO` | `requiredWaterUnits/requiredGravelUnits` | Po `6` porcji |
| `ConcreteMixerProfileSO` | `requiredCementBags` | Jeden worek zajmujący `3` jednostki objętości |
| `ConcreteMixerProfileSO` | `requiredRotations` | Pełne obroty korby; tutorial: `6` |
| `ConcreteMixerProfileSO` | `minimumLoadedVolumeToStartMixing` | Minimalna objętość przed naliczaniem progresu; tutorial: `6` |
| `ConcreteMixerProfileSO` | `maximumCrankAngularSpeed` | Serwerowy limit wejścia; tutorial: `240 deg/s` |
| `ConcreteMixerProfileSO` | `crankResponseTime` | Opór wskaźnika UI; tutorial: `0.12 s` |
| `ConcreteMixerController` | `water/gravel/cement` | Referencje katalogowe receptury |
| `ConcreteMixerController` | `drumPivot/drumSpinVisual` | Przechył trybu i wizualny obrót mieszania |
| `ConcreteMixerModeLever` | `mixing/pouringLocalEuler` | Pozycje dźwigni dla obu trybów |
