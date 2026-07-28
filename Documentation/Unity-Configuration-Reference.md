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
- interest/destruction profiles;
- spawner memory, jeśli ma pamiętać znane storage.

## Managery sceny

| Komponent | Konfiguracja |
|---|---|
| `GameplayManager` | bridge, component state data, ordered stages |
| `GameTimerManager` | duration i wait signal |
| `PlayerSpawnManager` | player prefab i spawn points |
| `BridgeStageInfoManager` | stage info entries |
| `ResourcePopulationZone` | resource, minimum, box, cooldown, masks i clearance |
| `NPCSpawner` | prefab, definitions, spawn points, intervals i limit |

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

## Pola runtime, których nie należy stroić

- `isPickedUp`, holder dictionaries i network input caches;
- current health/stamina/durability;
- current bridge stage/progress;
- current storage amounts;
- factory production state;
- population zone runtime debug;
- current NPC target/state/reservations;
- aktualne UI preview i local drag state.
