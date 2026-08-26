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

Dla narzędzia z przypisanym `EquippableActionProfileSO` akcja przebiega jako:

`WindUp → Strike → ImpactFreeze → Recovery`.

Cel nie jest zapamiętywany przy naciśnięciu LPM. Kontroler ponownie odczytuje
`CurrentTarget` w chwili impactu oraz sprawdza zasięg i dostępność akcji.
Pozwala to skorygować cel podczas zamachu. Pudło nie zadaje obrażeń i nie
uruchamia impact feedbacku, ale nadal odtwarza pełny recovery.

Zmiana narzędzia, downed, modalne UI lub utrata lokalnej kontroli anulują
zamach. Rozpoczęcie akcji wyłącza sprint i stosuje profilowy mnożnik zwykłego
ruchu. Axe, Pickaxe, Shovel i Industrial Hammer rozpoczynają kolejny cykl przy
przytrzymaniu LPM. Wrench wymaga osobnego kliknięcia.

`SpiritLevel` i `Rope` przejmują LPM przed zwykłą akcją narzędzia. Poziomica
nie zadaje obrażeń ani work progressu. LPM na `SpiritLevelMeasurementPoint`
rozpoczyna autoryzowany pomiar osi długości lub szerokości, a puszczenie LPM,
utrata celu/zasięgu, zmiana narzędzia, downed albo modalne UI kończą pomiar.
Bez aktywnego pomiaru pęcherzyk reaguje lokalnie na orientację narzędzia wobec
grawitacji; podczas pomiaru płynnie pokazuje zsynchronizowany logiczny przechył.
Para wyraźnych kresek na rurce otacza idealną pozycję pęcherzyka przy odczycie
`0`. Cztery punkty pomiaru są oznaczane lokalnie na cyjanowo. W tym samym etapie
gracz z Industrial Hammerem widzi pomarańczowe cele na czterech punktach klinów.

| Pole | Znaczenie |
|---|---|
| `baseActionRange` | Fallback zasięgu bez poprawnego SO |
| `baseActionCooldown` | Fallback cooldownu |
| `baseRepeatAction` | Czy akcję można powtarzać przy trzymaniu |
| `baseActionDamage` | Fallback damage/work |
| `serverActionRangeTolerance` | Mała tolerancja wyłącznie serwera |
| `serverCombatImpactTimingTolerance` | Tolerancja czasu RPC względem pełnego cyklu zamachu |
| `actionTransformHolder` | Środek i orientacja obszaru akcji |

Wybrany `EquippableItemSO` zastępuje bazowe parametry. Serwer ponownie
sprawdza cel, narzędzie, obszar i etap construction workflow. Trafienia
`PlayerHealth` i `NPCHealth` przechodzą przez jeden `ServerRpc`, który stosuje
obrażenia i opcjonalny impuls dokładnie raz. Serwer ogranicza częstotliwość
zaakceptowanych trafień na podstawie czasu pełnego profilu akcji. Harvesting
zasobów i construction work zachowują własne wyspecjalizowane ścieżki.

Publiczny stan `IsActionInProgress`, `CurrentActionPhase`,
`CurrentActionPhaseNormalized` i `ActionMovementMultiplier` jest używany przez
movement oraz FPP arms. Eventy `OnToolActionStarted`, `OnToolActionImpact` i
`OnToolActionEnded` pozwalają dołączać feedback przyszłych broni bez zmiany
samego kontrolera.

## Inventory i narzędzia

`PlayerInventory` ma dwa sloty. Slot `0` jest aktywny, slot `1` jest
przedmiotem na plecach; swap zamienia zawartość. Każdy slot ma jawny stan
`Empty`, `Occupied` albo `Reserved`. Narzędzie wymagające dwóch slotów można
podnieść wyłącznie do pustego inventory: trafia do slotu `0`, a slot `1`
otrzymuje sieciowo synchronizowany stan `Reserved`. Nie jest w nim zapisywany
drugi egzemplarz SO, więc kara ruchu jest liczona tylko raz.

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
| `inventorySlotsRequired` | Zajmowane sloty; `>= 2` ustawia `IsTwoHanded` |
| `actionRange` | Zasięg działania narzędzia |
| `actionCooldown` | Odstęp akcji |
| `damage` | Obrażenia graczy i NPC |
| `resourceDamage` | Utrata durability zasobu; zero używa kompatybilnego `damage * 2` |
| `constructionWorkPower` | Progress montażu; zero używa `damage` |
| `movementSpeedPenalty` | Dodatnia kara noszenia; sumowana dla obu slotów |
| `actionRepeatability` | Powtarzanie przy przytrzymaniu |
| `itemType` | Axe, Saw, Pickaxe, Hammer, Weapon, IndustrialHammer, Shovel, None, Wrench, Rope lub SpiritLevel |
| `actionProfile` | Opcjonalny profil faz, pozy, ruchu, camera kicku i audio |
| `impactImpulseProfile` | Opcjonalny odrzut żywego gracza lub NPC po potwierdzonym trafieniu |
| `spiritLevelProfile` | Zasięg, poza pomiarowa, dynamika pęcherzyka, kreski środka oraz markery punktów poziomicy |

### `EquippableActionProfileSO`

| Grupa | Pola i znaczenie |
|---|---|
| Timing | `windUpDuration`, `strikeDuration`, `impactFreezeDuration`, `recoveryDuration` |
| Tool pose | offset pozycji i rotacji w wind-up/impact |
| Arms | offsety prawej ręki i `leftArmActionWeight` |
| Movement | `movementMultiplierDuringAction` w zakresie `0–1` |
| Camera | offset pozycji/rotacji, recovery i `impactFeedbackStrength` |
| Audio | opcjonalny `swingClip`, volume i pitch |

Brak profilu zachowuje legacy natychmiastową akcję opartą o
`actionCooldown`. Brak klipu zamachu uruchamia lokalny proceduralny fallback.

Aktualny tuning:

| Narzędzie | Range | Damage / Resource / Work | Cykl | Ruch | Hold |
|---|---:|---:|---:|---:|---|
| Axe | `0.95` | `10 / 20 / 10` | `0.57 s` | `90%` | tak |
| Pickaxe | `1.0` | `12 / 20 / 12` | `0.88 s` | `78%` | tak |
| Shovel | `1.25` | `5 / 5 / 20` | `0.74 s` | `85%` | tak |
| Industrial Hammer | `1.3` | `8 / 8 / 24` | `1.03 s` | `65%` | tak |
| Wrench | `1.15` | `5 / 5 / 20` | `0.36 s` | `95%` | nie |

Kary inventory wynoszą odpowiednio `0.02`, `0.04`, `0.03`, `0.06` i `0.01`.
Shovel i Industrial Hammer mają `inventorySlotsRequired = 2`; pozostałe
narzędzia z tabeli są jednoslotowe. HUD pokazuje w zarezerwowanym slocie BACK
tekst `TWO-HANDED`, a swap narzędzia dwuręcznego nie zmienia inventory.

Rope oraz Spirit Level również zajmują dwa sloty. W FPP poziomica spoczywa
poziomo między domyślnie ustawionymi dłońmi i podczas pomiaru jest unoszona ku
środkowi ekranu. `PlayerSpiritLevelController` synchronizuje jedynie rozpoczęcie
i zakończenie pomiaru, ID części oraz stabilne ID konkretnego punktu.
Odczyt pęcherzyka powstaje lokalnie z `constructionValueA/B`, więc nie wymaga
ciągłych RPC i jest poprawnie odtwarzany przez late join.

Industrial Hammer ma przypisany `IndustrialHammerImpactImpulse`: `6 m/s`
poziomo, `2 m/s` w górę, `1.5 s` maksymalnego czasu i `50%` zachowanej kontroli.
Profil działa tylko na graczy i NPC, wymusza drop i nie jest aplikowany do
zasobów, części mostu ani work pointów.

## FPP arms

`PlayerFirstPersonArms` tworzy owner-only placeholderowe ręce i visual
narzędzia na osobnej warstwie renderowania.

| Grupa | Pola i znaczenie |
|---|---|
| Referencje | kamera, controller, input, action, interaction, health, inventory, turn feedback |
| Rendering | `firstPersonRenderLayer`, `firstPersonNearClipPlane` zapobiegają chowaniu rąk w ścianach |
| Pose | pozycja/rotacja rootu, rozstaw i kolory |
| Locomotion | amplitudy, cycles/meter, smoothing i limit teleportu |
| Legacy action | `actionDuration`, `actionSwingAngle`, hit reaction i pose lerp |
| Profiled action | pozycje narzędzia i rąk odczytywane z bieżącej fazy profilu |
| Turn lag | maksymalne przesunięcie i rotacja |
| Tool visual | materiały, lokalna poza, swing offset oraz parametry blendu dwuręcznego chwytu |

Faza ruchu zależy od faktycznie przebytego dystansu. Input przy ścianie nie
powinien napędzać animacji.

Hit-stop zatrzymuje tylko lokalną pozę narzędzia w `ImpactFreeze`; nie zmienia
`Time.timeScale`. Trafienie dodaje osobny kanał action feedback do
`PlayerCameraFeedbackComposer`. Proceduralny camera kick jest owner-only.

Proceduralne visuale Shovel i Industrial Hammer zawierają child
`SecondaryGrip`. Przy aktywnym narzędziu dwuręcznym lewa dłoń jest przeliczana
do tego punktu po złożeniu bieżącej pozy zamachu, dzięki czemu podąża za
uchwytem przez `WindUp`, `Strike`, `ImpactFreeze` i `Recovery`. Carry pose,
downed albo brak narzędzia wyłączają chwyt i płynnie przywracają zwykłą pozę.

## Impact audio i VFX

`ActionImpactEffectSpawner` synchronizuje efekt trafienia, ale zamach bez
kontaktu pozostaje lokalny. Powierzchnie to `Default`, `Wood`, `Stone`,
`Metal`, `Soil` i `Flesh`.

`IActionImpactSurfaceProvider` może jawnie określić powierzchnię. Zasoby
korzystają z `BaseResourceSO.impactSurfaceType`, gracze i NPC z `Flesh`, a
punkty budowy mają fallback wynikający z używanego narzędzia. Wpisy
`surfaceFeedback` mogą przypisać prefab ParticleSystemu, klip i volume.
Brak assetu tworzy niewielki proceduralny VFX i przestrzenny dźwięk.

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

## Woda, stamina i utoniecie

**Status:** zaimplementowane w `Tutorial_scene`; autorytet zagrozen i staminy nalezy do serwera.

`PlayerStaminaController` jest wspolnym zrodlem stanu staminy dla sprintu, carry, niedoborowego shared-carry i wody. Koszty aktywnych zrodel sumuja sie, a `FirstPersonController` zachowuje kompatybilne wlasciwosci delegujace do nowego kontrolera.

`PlayerWaterExposureController` rozroznia bezpieczne brodzenie i niebezpieczna wode. W tutorialu woda zuzywa `1 stamina/s`; wartosc konfiguruje pole `staminaDrainPerSecond` w `TutorialRiverProfile`. Przy zerowej staminie rozpoczyna sie ostrzezenie trwajace `3 s`; pozostanie w wodzie konczy sie stanem downed. Brak bezpiecznego podloza albo przekroczenie bezpiecznej glebokosci brodzenia uruchamia niezalezny timer `2 s`.

| Stan | Zachowanie |
|---|---|
| `Wading` | Gracz ma bezpieczne podloze, ale nadal placi koszt staminy wody. |
| `Unsafe` | Brak bezpiecznego podloza lub przekroczona glebokosc; przy wlaczonej fladze `GameplayManager` odliczane sa `2 s`. |
| Zero staminy | HUD pokazuje powod `Water`; po `3 s` serwer ustawia downed. |
| Downed w wodzie | Cialo unosi sie przy powierzchni, revive jest zablokowany, pickup i respawn pozostaja dostepne. |

Wyjscie na bezpieczny brzeg resetuje oba timery. Przejscie w downed natychmiast zatrzymuje wodny drain i miganie HUD, ale pozostawia unoszenie ciala przy powierzchni. Carry powalonego gracza przez innego gracza pozostaje dozwolone, a drain carry sumuje sie z kosztem wody.

`GameplayManager.EnableUnsupportedWaterDowning` pozwala scenowo wylaczyc tylko timer `Unsafe`. Nie wylacza kosztu staminy ani powalenia po jej wyczerpaniu. Ponowne wlaczenie flagi rozpoczyna timer od poczatku.
