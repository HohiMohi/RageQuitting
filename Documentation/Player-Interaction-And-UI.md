# Gracz, interakcje i UI

## Status

**Gotowe, aktywnie rozwijane.** Gracz korzysta z `CharacterController`,
lokalnej kamery Cinemachine oraz server-validated interakcji. HUD i feedback
FPP są owner-only.

## Ruch

`FirstPersonController` przechowuje pełny poziomy wektor prędkości. Gracz nie
osiąga prędkości maksymalnej natychmiast; dochodzi do niej przez akcelerację,
a kolizje korygują zapamiętany wektor na podstawie rzeczywistego ruchu
`CharacterController`.

| Pole | Jednostka | Znaczenie |
|---|---:|---|
| `MoveSpeed` | m/s | Maksymalna prędkość chodu przed karami |
| `SprintSpeed` | m/s | Maksymalna prędkość sprintu |
| `RotationSpeed` | mnożnik | Czułość bazowego obrotu |
| `groundAcceleration` | m/s² | Rozpędzanie na ziemi |
| `groundDeceleration` | m/s² | Hamowanie bez inputu |
| `reverseAcceleration` | m/s² | Zmiana na przeciwny kierunek |
| `airAcceleration` | m/s² | Sterowanie poziome w powietrzu |
| `strafeSpeedMultiplier` | 0-1 | Kara ruchu bokiem |
| `backwardSpeedMultiplier` | 0-1 | Kara ruchu wstecz |
| `walkStepDistance` | m | Dystans pomiędzy eventami kroków w chodzie |
| `sprintStepDistance` | m | Dystans pomiędzy eventami sprintu |
| `JumpHeight` | m | Docelowa wysokość skoku |
| `Gravity` | m/s² | Bazowa grawitacja kontrolera |
| `JumpTimeout` | s | Minimalny odstęp skoku |
| `FallTimeout` | s | Opóźnienie uznania stanu spadania |
| `GroundedOffset` | m | Pionowy offset testu podłoża |
| `GroundedRadius` | m | Promień sfery grounded |
| `GroundLayers` | maska | Warstwy uznawane za podłoże |

`Grounded` i `currentMovementSpeed` są polami diagnostycznymi/runtime. Nie
powinny być używane do trwałej konfiguracji.

### Kary ruchu

Kary pochodzą z inventory, single-carry, shared-carry i niesienia gracza.
System składa mnożniki, zamiast nadpisywać `MoveSpeed`. Shared-carry może
dodatkowo sterować staminą, jeśli liczba player-holderów jest mniejsza od
wymaganej.

## Kamera i obrót ciała

Mysz natychmiast aktualizuje aim yaw/pitch. Root postaci dochodzi do yaw przez
`SmoothDampAngle`, a kamera może chwilowo wyprzedzić ciało.

| Pole | Jednostka | Znaczenie |
|---|---:|---|
| `CinemachineCameraTarget` | referencja | Bazowy target kamery |
| `TopClamp` / `BottomClamp` | stopnie | Ograniczenia pitch |
| `maximumCameraBodyYawOffset` | stopnie | Maksymalne wyprzedzenie kamery |
| `bodyYawSmoothTime` | s | Czas nadążania ciała |
| `bodyMaximumYawSpeed` | °/s | Limit prędkości obrotu rootu |

`CameraMotionSettings.RotationMotionIntensity` skaluje tylko nowy pakiet
obrotu: separację kamera/ciało, turn feedback i lag rąk. Singleton jest lokalny
i przechodzi pomiędzy scenami, ale nie zapisuje się po zamknięciu aplikacji.

### `PlayerMovementFeedback`

| Pole | Znaczenie |
|---|---|
| `controller`, `cameraFeedbackComposer`, `playerInput`, `playerHealth` | Wymagane referencje ownera |
| `walkBobAmplitude`, `sprintBobAmplitude` | Pionowa amplituda bobu |
| `bobCyclesPerMeter` | Częstotliwość zależna od faktycznego dystansu |
| `horizontalBobRatio` | Udział ruchu bocznego |
| `positionSmoothing` | Wygładzenie pozycji |
| `maximumStrafeRoll` | Limit roll przy strafowaniu |
| `accelerationSway` | Reakcja na zmianę prędkości |
| `maximumAccelerationOffset` | Clamp przesunięcia |
| `rotationSmoothing` | Wygładzenie roll |
| `jumpPositionImpulse` | Impuls kamery przy starcie skoku |
| `landingPositionImpulse` | Bazowy impuls lądowania |
| `landingReferenceSpeed` | Prędkość spadania dająca pełny efekt |
| `impulseRecoverySpeed` | Powrót do neutralnej pozycji |
| `sprintFovBonus` | Dodatkowe FOV sprintu |
| `fovChangeSpeed` | Szybkość blendu FOV |

### `PlayerTurnFeedback`

| Pole | Znaczenie |
|---|---|
| cztery referencje | Controller, composer, input i health |
| `maximumTurnRoll` | Maksymalny roll przy obrocie |
| `maximumLateralOffset` | Maksymalne przesunięcie boczne kamery |
| `responseSpring` | Szybkość reakcji |
| `responseDamping` | Tłumienie oscylacji |
| `maximumResponse` | Clamp overshootu |

`PlayerCameraFeedbackComposer` jest jedynym miejscem składającym movement,
turn i damage offsets. Inne komponenty nie powinny bezpośrednio nadpisywać
transformu feedback targetu.

## Stamina

| Pole | Jednostka | Znaczenie |
|---|---:|---|
| `MaxStamina` | punkty | Pojemność staminy |
| `StaminaRegenerationTimeout` | s | Opóźnienie regeneracji po zużyciu |
| `sharedCarryExhaustionWarningDuration` | s | Czas ostrzeżenia przed downed |
| `sharedCarryInputSendInterval` | s | Częstotliwość wysyłania intencji carry |
| `sharedCarryInputChangeThreshold` | wartość inputu | Minimalna zmiana wymuszająca update |
| `sharedCarryAttachCorrectionSpeed` | m/s | Miękka korekta holdera |
| `sharedCarryAttachSnapDistance` | m | Maksymalna odległość korekty |

Przy niedoborze player-holderów stamina spada według wartości niesionego SO.
Po osiągnięciu zera UI pulsuje czerwono. Jeśli niedobór trwa przez cały czas
ostrzeżenia, serwer wymusza drop i downed. Drop albo uzupełnienie obsady
anuluje warning. Po revive/respawn stamina wraca do maksimum.

## Health, downed i revive

`PlayerHealth` jest `NetworkBehaviour` i implementuje `IDamageable`.

| Pole | Znaczenie |
|---|---|
| `maxHealth` | Maksymalne HP |
| `healthRegenerationPerSecond` | Regeneracja HP na sekundę |
| `regenerationDelayAfterDamage` | Opóźnienie regeneracji |
| `respawnAvailableDelay` | Czas do samodzielnego respawnu po downed |

Zero HP ustawia downed, ale nie usuwa player objectu. Inny gracz może wykonać
`ActionAlt` revive. Downed player może być również niesiony przez
`DownedPlayerCarryable`.

`DownedPlayerCarryable` obsługuje carrierów-graczy oraz `NPCCarrier`. NPC jest
identyfikowany przez `NetworkObjectId`, dzięki czemu wiele server-owned NPC nie
koliduje ze sobą w mapowaniu holderów. Podczas NPC carry `CanBeRevived` i
`CanRespawn` zwracają `false`, a prompt pokazuje `Carried by enemy`.

`PlayerHealth` przechowuje synchronizowany timestamp
`npcCarryRespawnPauseStartedAtNetwork`. `GetRespawnTimeRemaining()` zamraża czas
na wartości z chwili pickupu. Przy dropie `downedAtTime` jest przesuwany o czas
transportu, więc odliczanie kontynuuje się bez utraty lub dodania sekund.
`IsRespawnTimerPausedByNpcCarry` udostępnia stan read-only. Carry przez innego
gracza nie zatrzymuje timera.

## Input

`PlayerInputNew` opakowuje Unity Input System i publikuje eventy ruchu,
patrzenia, skoku, sprintu, interakcji, action/alt action, inventory, menu Tab,
UI Back i zamykania overlayu informacyjnego.

`SetGameplayUiOpen(true)`:

- blokuje ruch, kamerę i gameplay actions;
- odblokowuje kursor;
- pozostawia aktywny skrót, który zamyka dane UI.

Informacyjne okno tutorialu nie ustawia gameplay UI open i nie zatrzymuje
sterowania.

## Targeting i interakcje

`PlayerInteractionNew` używa promienia z
`Camera.ViewportPointToRay(0.5, 0.5)`, zgodnego ze środkiem crosshaira.
Najpierw wykonuje precyzyjny raycast, potem mały spherecast tolerancji.
Solidna przeszkoda blokuje cel za sobą.

| Pole | Znaczenie |
|---|---|
| `interactionOrigin` | Fallback, gdy lokalna kamera nie jest przypięta |
| `interactDistance` | Maksymalny zasięg wykrywania |
| `aimAssistRadius` | Minimalna tolerancja dla małych work pointów |
| `pickUpHoldPositionHolder` | Anchor zwykłego carry |
| `carryBodyAnchor` | Anchor fizycznego shared-carry |
| `defaultCarryBodyAnchorLocalPosition` | Offset body anchora |
| `carriedPlayerAnchor` | Anchor powalonego gracza |
| `defaultCarriedPlayerAnchorLocalPosition` | Offset niesionej postaci |
| `pickedUpObject`, `_pickedUpGameObject` | Stan runtime; nie konfigurować |
| `temp` | Pole tymczasowe; nie jest stabilnym API |

`CurrentTarget` jest wspólnym źródłem prawdy dla:

- promptu;
- outline'u;
- `Interact`;
- `Action`;
- `ActionAlt`.

Informacyjny prompt nie aktywuje outline'u. Outline pojawia się wyłącznie, gdy
akcja jest obecnie wykonalna.

## Akcje narzędzi

`PlayerActionController` działa na jednym `CurrentTarget`, nie na wszystkich
obiektach w overlapie.

| Pole | Znaczenie |
|---|---|
| `baseActionRange` | Fallback zasięgu bez poprawnego SO |
| `baseActionCooldown` | Fallback cooldownu |
| `baseRepeatAction` | Czy akcję można powtarzać przy trzymaniu |
| `baseActionDamage` | Fallback damage/work |
| `serverActionRangeTolerance` | Mała tolerancja wyłącznie serwera |
| `actionTransformHolder` | Środek i orientacja obszaru akcji |

Wybrany `EquippableItemSO` zastępuje bazowe parametry. Serwer ponownie
sprawdza cel, narzędzie, obszar i etap construction workflow.

## Inventory i narzędzia

`PlayerInventory` ma dwa sloty. Slot `0` jest aktywny, slot `1` jest
przedmiotem na plecach; swap zamienia zawartość.

| Pole | Znaczenie |
|---|---|
| `inventoryItems` | Lokalna tablica zawartości |
| `equippableItemCatalog` | Mapowanie enum -> SO używane przez synchronizację |
| `_selectedItemIndex` | Aktywny slot |
| `_inventorySlots` | Pojemność |
| `_currentInventoryOccupiedSlots` | Stan runtime |

### `EquippableItemSO`

| Pole | Znaczenie |
|---|---|
| `itemName`, `uiSprite` | Nazwa i ikona HUD |
| `equippableItemPrefab` | Sieciowy obiekt świata |
| `inventorySlotsRequired` | Zajmowane sloty |
| `actionRange` | Zasięg działania narzędzia |
| `actionCooldown` | Odstęp akcji |
| `damage` | Obrażenia dla `IDamageable` |
| `constructionWorkPower` | Progress montażu; zero używa `damage` |
| `movementSpeedPenalty` | Kara noszenia/equip |
| `actionRepeatability` | Powtarzanie przy przytrzymaniu |
| `itemType` | Axe, Saw, Pickaxe, Hammer, Weapon, IndustrialHammer, Shovel, None lub Wrench |

## FPP arms

`PlayerFirstPersonArms` tworzy owner-only placeholderowe ręce i visual
narzędzia na osobnej warstwie renderowania.

| Grupa | Pola i znaczenie |
|---|---|
| Referencje | kamera, controller, input, action, interaction, health, inventory, turn feedback |
| Rendering | `firstPersonRenderLayer`, `firstPersonNearClipPlane` zapobiegają chowaniu rąk w ścianach |
| Pose | pozycja/rotacja rootu, rozstaw i kolory |
| Locomotion | amplitudy, cycles/meter, smoothing i limit teleportu |
| Action | czas i kąt swing, hit reaction, pose lerp |
| Turn lag | maksymalne przesunięcie i rotacja |
| Tool visual | materiały oraz lokalna pozycja, rotacja, skala i swing offset |

Faza ruchu zależy od faktycznie przebytego dystansu. Input przy ścianie nie
powinien napędzać animacji.

## HUD

Owner-only `PlayerHUD` zawiera:

- `PlayerHealthUI`: fill oraz `current / max`;
- `PlayerStaminaUI`: fill, liczba i puls exhaustion;
- `PlayerInventoryUI`: active/back;
- `PlayerHeldObjectUI`: nazwa, ikona i licznik shared-carry;
- `LookingAtComponentUI`: prompt i progres celu;
- `PlayerCrosshairUI`: kropka na środku;
- `PlayerBridgeStageInfoUI`: tekst tutorialu;
- `RestartLevelUI`: start timera i restart hosta.

Referencje `Image`, TMP, panel roots i CanvasGroup są techniczne. Muszą
wskazywać elementy tej samej lokalnej hierarchii Canvas. `raycastTarget`
crosshaira i elementów dekoracyjnych powinien być wyłączony.

## Ograniczenia

- `PlayerInteractionNew.temp` jest pozostałością roboczą.
- Część wyglądu HUD powstaje programowo lub przez rozbudowany prefab, więc
  zmiana hierarchii wymaga sprawdzenia referencji.
- Interfejs `IDamageable` obsługuje zarówno damage, jak i construction work.
- Feedback proceduralny nie ma jeszcze pełnego menu dostępności.
