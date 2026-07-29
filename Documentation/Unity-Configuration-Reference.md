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
| `BaseResourceSO` | identity/prefab/icon, durability, carryability, carrier counts, speed/stamina penalties, anchors, rotation offset, physics profile, fuel, destruction recipes |
| `MountableBridgeComponentSO` | identity/prefab, recipe, bridge type, carry, furnace i carpenter dimensions |
| `ProductionRecipeSO` | identity/icon, składniki, typ i ilość wyjścia oraz opcjonalne parametry pieca |
| `BridgeComponentSO` | identity/final prefab, category, simple assembly i sześć opcjonalnych workflow |
| `EquippableItemSO` | identity/prefab, slots, range, cooldown, damage, work power, movement penalty, repeatability i enum |
| `CarryPhysicsProfileSO` | Rigidbody, movement, yaw, horizontal constraint i vertical support |
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
| `PlayerActionController` | fallback action values, server tolerance i action holder |
| `PlayerInventory` | dwa sloty i pełny catalog enum -> SO |
| `PlayerHealth` | HP, regen, delays |
| `PlayerExternalImpulseController` | referencja controller/interaction/health, jeśli widoczna w Inspectorze |
| `PlayerNetworkSetup` | camera target, local/remote visual, Canvas i owner-only components |
| `PlayerFirstPersonArms` | references, render layer, pose, locomotion, action, turn lag i tool visual |
| feedback components | controller/composer/input/health oraz amplitudy/smoothing |

### Player UI

| Komponent | Pola |
|---|---|
| `PlayerHealthUI` | `playerHealth`, fill image, value text, colors |
| `PlayerStaminaUI` | controller, fill image, value text, normal/warning colors, blink speed |
| `PlayerInventoryUI` | inventory oraz dwa zestawy slot references |
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
- scenowe `GoatPushZone` z approach, direction i impulse profile.

### Beaver-only

- `BeaverScoutBehaviorSO`;
- `BeaverDefenderBehaviorSO`;
- interest/destruction profiles;
- spawner memory, jeśli ma pamiętać znane storage.

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
| `playerFaction` | Rozpoznanie graczy jako kandydatów do walki |
| `idleDecisionDelay` | Zwłoka przed wyborem skauta albo pozostaniem w idle |
| `followDistance`, `followStopDistance` | Dystans formacji i próg zatrzymania |
| `followRepathInterval` | Częstotliwość aktualizacji celu NavMesh |
| `maxDefendersPerScout` | Maksymalna liczba globalnych rezerwacji jednego skauta |
| `attackRepathInterval` | Częstotliwość aktualizacji pościgu w `AttackMode` |
| `targetLostTimeout` | Czas tolerowania utraconego lub nieosiągalnego celu |
| `familyAlertRadius` | Maksymalna odległość od pozycji alarmu rodzinnego |

Asset `BeaverDefenderDefinition` wskazuje `NPC_BeaverDefender`, visual
`BeaverDefenderVisual`, `BeaversFaction` oraz behavior obrońcy. Prefab musi
pozostać wpisany w `DefaultNetworkPrefabs`.

## Managery sceny

| Komponent | Konfiguracja |
|---|---|
| `GameplayManager` | bridge, component state data, ordered stages |
| `GameTimerManager` | duration i wait signal |
| `PlayerSpawnManager` | player prefab i spawn points |
| `BridgeStageInfoManager` | stage info entries |
| `ResourcePopulationZone` | resource, minimum, box, cooldown, masks i clearance |
| `NPCSpawner` | prefab, legacy definitions, spawn groups, points, intervals i global limit |

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
3. Utwórz sieciowy prefab świata.
4. Dodaj visual builder/FPP representation.
5. Dodaj do inventory catalog wszystkich graczy.
6. Zarejestruj prefab i umieść source w scenie/storage.

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
