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

### Statusy czasowe gracza i `CrushedHands`

`PlayerTemporaryStatusController` utrzymuje wspólny rejestr statusów autorytatywny dla serwera w grze sieciowej i lokalnie w trybie offline. Rejestr przechowuje osobną instancję każdego statusu danego typu. Ponowne nałożenie odświeża instancję, a różne typy działają równocześnie. Status może wygasnąć po czasie, po zdarzeniu albo po spełnieniu pierwszego z obu warunków. Odświeżenie zmienia identyfikator instancji, więc spóźnione zdarzenie nie usuwa nowszego statusu. Mnożniki statusów składają się, także dla prędkości chodzenia, wkładu gracza we wspólne noszenie i limitów prędkości taczki.

Etap 1 wprowadza produkcyjne statusy `PlacementLock` i `CrushedHands`. Zatwierdzone umieszczenie F→E tworzy `PlacementLock` domyślnie na `1 s`. Blokuje ruch, sprint, skok, kamerę oraz akcje w świecie. Grawitacja i fizyczne impulsy nadal działają, a menu UI pozostaje dostępne. Zwykłe przenoszenie bez zatwierdzonego placementu nie uruchamia blokady. Osobny identyfikator placementu, niezależny od wersji snapshotu statusów, wiąże odpowiedź z właściwym umieszczeniem.

Obrażenie wymaga skutecznego osadzenia świeżo przygotowanego gwoździa ściśle przed upływem sekundowej blokady placementu. Termin ten jest niezależny od istniejącego terminu `Prepared`. Każde zaakceptowane umieszczenie może spowodować najwyżej jedno zranienie. Trafienie kończy `PlacementLock` i nakłada `CrushedHands` domyślnie na `15 s`. Może je spowodować dowolny atakujący. Kopnięcie luźnego gwoździa i późne trafienie nadal podlegają zwykłym regułom gwoździa, ale nie powodują statusu. `CrushedHands` pozostawia ruch i rozglądanie, lecz blokuje akcje w świecie. Przerywa też trwające czynności trzymania i obsługi narzędzi. Przy wejściu wykonuje jedno upuszczenie narzędzi z obu zajętych slotów oraz niesionego obiektu, bez rzutu. Dwu-slotowe narzędzie jest upuszczane tylko raz. Ręce FPP pozostają spłaszczone do przywrócenia stanu.

Wymuszone zakończenie prowadzenia taczki po stronie serwera odzyskuje kontrolę nad jej fizyką i zachowuje pęd taczki. Gracz trafia najpierw do pobliskiego bezpiecznego miejsca wyjścia, a jeśli go nie ma, do istniejącego technicznego punktu spawnu. Obie drogi zachowują status. Rzeczywisty respawn usuwa statusy Etapu 1 przez `ResetTemporaryStatuses`. Ożywienie przez sojusznika zachowuje pozostały czas. Klient otrzymuje potwierdzone przez serwer podniesienie, zamianę slotów i upuszczenie. Oba sloty ekwipunku replikują się razem. `PlayerNew` konfiguruje wartości w Inspectorze.

Etap 2 używa tego samego rejestru dla statusów wody, uwięzienia w betonie i uwięzienia przez tumbleweed. Szczegóły ich timerów i reguł wejścia/wyjścia opisują poniższe sekcje. Geometria wody i stamina nadal należą do kontrolerów wody i staminy.

### Uwięzienie przez tumbleweed

Uwięzienie trwa od złapania do bezpiecznego uwolnienia po upływie czasu lub usunięcia źródła. Status jest związany z identyfikatorem epizodu i usuwa zagrożenie wodne. Blokuje ruch, rozglądanie i akcje. Pasażerowie są wyłączeni z przechwycenia. Przed złapaniem kierowcy serwer kończy prowadzenie i odzyskuje kontrolę nad fizyką taczki. Klient gracza ustawia pozycję uwolnienia przed przywróceniem sterowania. Ochrona przed ponownym złapaniem zaczyna odliczać przy uwolnieniu. Zachowuje pozostały czas przez ożywienie sojusznika i rzeczywisty respawn, a kończy się przy despawnie.


### Obłocenie

Obłocenie ma wartość od 0 do 100%. Każdy metr faktycznie pokonany w aktywnej luźnej ziemi wykopu dodaje 5 p.p. Każda porcja ziemi faktycznie pobrana do wiadra dodaje 15 p.p. Ułamkowe porcje liczą się proporcjonalnie. Ruch pieszy w wodzie zmywa 10 p.p. na sekundę zarówno podczas bezpiecznego brodzenia, jak i w niebezpiecznej wodzie. Wspólne noszenie również liczy się jako ruch pieszy. Postój, jazda taczką i bierny transport zachowują poziom obłocenia. Gdy woda i luźna ziemia pokrywają się, ruch zmywa błoto. Jednoczesny zatwierdzony pobór do wiadra nadal dodaje obłocenie.

Spowolnienie rośnie liniowo do 30% i obejmuje chód, sprint, wkład gracza we wspólne noszenie oraz prowadzenie taczki. Jego mnożnik składa się z karą za zagłębienie w ziemi przez mnożenie. Ożywienie przez gracza zachowuje obłocenie, a rzeczywisty respawn je zeruje.

Lokalny HUD FPP wyświetla nakładkę błota pod celownikiem i menu. Nakładka nie przechwytuje wejścia. Plamy płynnie rozrastają się lub cofają wraz ze zmianą poziomu obłocenia. Przy 100% trzy nieregularne prześwity zajmują około 20% ekranu. Największy obejmuje pole wokół celownika. Od 35% obłocenia pojawia się pojedyncza kropla z krótką smugą. Odstęp między startami wynosi 7–12 sekund czasu gry, a spływanie trwa 4–6 sekund. Krople mogą przeciąć każdy prześwit. Spadek poniżej 35% wygasza aktywną kroplę. Przy 0% obłocenia lub rzeczywistym respawnie cała nakładka znika od razu.\n\nDomyślny jest styl malowany. Styl komiksowy `MudComicStyle` można ustawić w polu `Style` komponentu `Player Mud Overlay` na obiekcie `MudScreenOverlay` w prefabie `PlayerNew`. Oba zestawy znajdują się w `Assets/Sprites/PlayerMudOverlay/`. Parametry obłocenia są w `Assets/ScriptableObjectAssets/New/PlayerStatusCatalog.asset`. Maksymalna alfa nakładki wynosi 100%, a pokrycie jest sterowane niezależnie.

Obłocenie widać też z perspektywy trzeciej osoby jako plamy, które narastają od nóg przez tułów i przedramiona, a przy wysokim poziomie pojawiają się również na twarzy. Efekt korzysta z maski UV0 dopasowanej do modelu Goblina, gdzie kanał R określa pokrycie, a G próg plamy. Replikowany poziom obłocenia steruje wyglądem postaci widocznym dla pozostałych graczy. Każda postać ma własną instancję materiału. Kolor, gładkość i miękkość krawędzi można dostroić w `Assets/Meshes/Goblin_AI2_Assets/Materials/GoblinPlayerMud.mat`. Każdy kolejny model wymaga pasującej maski UV.

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

### Uwięzienie w betonie taczki

`PlayerConcreteTrapController` stosuje status zdarzeniowy od chwili, gdy beton w taczce stwardnieje. Pozostaje aktywny przez wyrzucenie gracza i zawalenie betonu, a czyści się dopiero po zakończeniu zawalenia lub sprzątnięciu pułapki. Uwięzienie blokuje ruch, sprint, skok i akcje w świecie, lecz kamera pozostaje dostępna. Gracz nadal przyjmuje obrażenia. Woda zachowuje niezależne zasady zagrożenia i downed.

Jeżeli uwięziony gracz jest downed, nie może zostać ożywiony ani użyć respawnu, dopóki beton nie zostanie usunięty. Countdown respawnu pauzuje z osobnym powodem i po uwolnieniu kontynuuje od zachowanej wartości; nie koliduje to z pauzą podczas carry przez NPC. Uwolnienie nie wykonuje revive: gracz pozostaje downed. Po rozbiciu betonu w ustawionej taczce gracz pozostaje pasażerem i może wysiąść standardową akcją.


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

## UI księgi konstrukcyjnej

Przy celowaniu w księgę kontekst przejmuje `IContextActionConsumer`: LPM cofa,
a PPM przechodzi do następnej strony, zamiast uruchamiać akcję narzędzia.
`E` wywołuje zwykłą interakcję i otwiera lokalny ekran księgi. W otwartym UI
`A`/`D` oraz strzałki lewo/prawo nawigują przez akcje UI Input System, natomiast
`Escape` i `X` zamykają ekran wyłącznie lokalnie. Przyciski ekranowe realizują
te same operacje. Callbacki są nazwane `HandleUiLeft`, `HandleUiRight` i
`HandleUiBack`, aby nie kolidowały z dispatchingiem `PlayerInput` w trybie
`SendMessages`.

`PlayerConstructionBookUI` ustawia `PlayerInputNew.IsGameplayUiOpen`, dzięki
czemu blokuje gameplay input i zwalnia kursor; zamknięcie przywraca poprzedni
tryb sterowania. Zmiana strony używa krótkiego slide/fade w ekranowym UI oraz
obrotu kartki w obiekcie świata. Próba wyjścia poza pierwszą lub ostatnią
stronę daje tylko małe wychylenie krawędzi — indeks nie zawija się. Callback
przypisania jest związany z aktualnie renderowanym krokiem, a kliknięcia są
blokowane w czasie przejścia. `SetIsOnWithoutNotify` odświeża stan z serwera,
więc odrzucone żądanie nie zmienia lokalnie oznaczenia.

Ekran jest owner-only: powstaje i reaguje wyłącznie dla lokalnie posiadanego
gracza. Nie otwiera się w stanie downed, zamyka się po wejściu w downed oraz w
`OnDisable`/`OnDestroy`, odpinając eventy i resetując niedokończoną animację.
World-space i screen-space korzystają ze wspólnego `ConstructionBookView`,
więc pokazują ten sam tytuł, materiały, kroki, roster, przypisania i stan strony. Oba widoki
używają układu: `Steps` 28, instrukcja 22, ikona narzędzia `22x22`, nazwa 18,
checkbox `18x18`, etykieta 12, komórka `64x40`, wyśrodkowany wiersz wysokości
42 oraz karta z paddingiem 6, spacingiem 3 i gapem 6. Toggle ma jawne
target/check graphic i outline, a zaznaczenie oznacza przypisanie. Wyłącznie
world-space oznaczenia są nieinteraktywne; przypisania zmienia się na ekranie
`E`. Zmiany autorytatywnego, sortowanego rosteru `Host`/`Player N`
odświeżają oba widoki bez uruchamiania animacji strony. Na ekranie `E` każdy
gracz może przypisać dowolny krok jednemu lub wielu graczom, także planować
dla przyszłych etapów. Księga w świecie pokazuje zsynchronizowane
oznaczenia jako read-only. Klucz przypisania to typ rozkładówki, oryginalny
indeks kroku i `ClientId`, nie etykieta gracza.
Oznaczenie opisuje odpowiedzialność, nie ukończenie kroku. Serwer waliduje
nadawcę, połączenie, zdrowie, odległość `3.5 m`, stronę i krok oraz stosuje
idempotentny stan docelowy, przechowywany w serwerowym replikowanym
`NetworkList` rekordów przypisań. Disconnect usuwa zadania gracza, rejoin zaczyna
czysto, a restart poziomu czyści listę; pozostałe przypisania przechodzą przez
zmiany etapów i powrót typu części.

Panel wymagań `N` działa niezależnie od otwarcia księgi i pokazuje własne
przypisane kroki tylko dla typów bieżącego globalnego etapu. Zachowuje
kolejność księgi i oryginalną numerację, instrukcje i nazwy wymagań; wiele
instancji typu nie dubluje pozycji. Panel zachowuje położenie i rozmiar, ma
stały nagłówek `Bridge Requirements` nad przewijaną treścią, której sekcje są
uporządkowane jako `Current stage`, `My tasks` i `Remaining stages`.
Wyświetla jawne stany braku/niedostępności danych oraz ukończenia.
`Game/BridgeRequirementsScroll` przekazuje `Vector2` z `<Mouse>/scroll` przez
`PlayerInputNew`; działa tylko dla widocznego lokalnego panelu N i nie
przechwytuje scrolla innych menu. Panel zachowuje ruch, patrzenie i blokadę
kursora. Otwarcie i zmiana etapu przewijają do góry, zwykłe odświeżenia
zachowują pozycję z ograniczeniem do zakresu.

Testy PlayMode pokrywają presenter, wejście/kursor, animację, granice, kolejkę
zmian stron i lifecycle owner-only. Automaty potwierdzają sześć kroków,
czterech graczy, 24 checkboxy, geometrię rectów, nawigację A/D i host smoke.
Walidacja dwuprocesowa localhost objęła late join, przypisania w obu
kierunkach, widoki world/screen/N, odrzucanie niepoprawnych żądań, zmianę i
powrót etapów, scroll, disconnect/rejoin, ukończenie oraz restart poziomu.
Część callbacków wywoływano programowo, a etap/ukończenie wstrzykiwano; nie
wykonano pełnej ręcznej rozgrywki wizualnej. Ukierunkowany strict Gameplay
zakończył się kodem 0; ukierunkowane suite ConstructionBook: EditMode 12/12,
PlayMode 13/13:
`D:\Programy\UnityProjects\RageQuitting\Artifacts\Validation\20260927T164837Z-21a6ddd7\summary.json`.
Dowody sesji:
`D:\Programy\UnityProjects\RageQuitting\Artifacts\Validation\ConstructionBookMultiplayer`.
Rejoin z nadal załadowanego Tutorial miał błąd duplikatu `GlobalObjectIdHash`
`NPC_BeaverScout`; rejoin przez lobby działał. Ostateczna ocena wizualna należy
do użytkownika.

## Ograniczenia

- `PlayerInteractionNew.temp` jest pozostałością roboczą.
- Część wyglądu HUD powstaje programowo lub przez rozbudowany prefab, więc
  zmiana hierarchii wymaga sprawdzenia referencji.
- Interfejs `IDamageable` obsługuje zarówno damage, jak i construction work.
- Feedback proceduralny nie ma jeszcze pełnego menu dostępności.

## Woda, stamina i utoniecie

**Status:** zaimplementowane w `Tutorial_scene`; serwer autorytatywnie rozstrzyga zagrożenie i staminę w sieci. `PlayerWaterExposureController` obsługuje geometrię ekspozycji, a `PlayerStaminaController` pozostaje właścicielem stanu staminy. Koszty aktywnych źródeł sumują się, a `FirstPersonController` zachowuje kompatybilne właściwości delegujące do kontrolera staminy.

Woda zużywa `1 stamina/s` w tutorialu. Wartość pochodzi z `staminaDrainPerSecond` w `TutorialRiverProfile`. Zerowa stamina uruchamia status z ostrzeżeniem domyślnie przez `3 s`. Pozostanie w wodzie kończy się stanem downed. Brak bezpiecznego podłoża albo przekroczenie bezpiecznej głębokości brodzenia uruchamia osobny status z domyślnym terminem `2 s`. Oba zegary działają niezależnie.

| Stan | Zachowanie |
|---|---|
| `Wading` | Gracz ma bezpieczne podłoże, ale nadal płaci koszt staminy wody. |
| `Unsafe` | Brak bezpiecznego podłoża lub przekroczona głębokość; aktywny zegar zależy od flagi `GameplayManager`. |
| Zero staminy | HUD pokazuje powód `Water`; po upływie profilu serwer ustawia downed. |
| Downed w wodzie | Ciało unosi się przy powierzchni, revive jest zablokowany, pickup i respawn pozostają dostępne. |

Przekroczenie progu unsafe i wyczerpanie staminy uruchamiają osobne timery. Zmiana profilu wody zachowuje dotychczasowy czas ekspozycji dla każdego aktywnego zegara i przelicza jego termin. Wyłączenie flagi unsafe zatrzymuje i czyści tylko timer unsafe; pozostały timer działa dalej. Ponowne włączenie flagi rozpoczyna timer unsafe od początku. Ustanie jednego warunku czyści tylko jego timer. Wyjście na suchy ląd lub uwięzienie czyści oba, a uwięzienie natychmiast zatrzymuje wodny drain. Przejście w downed zatrzymuje drain oraz miganie HUD, ale pozostawia unoszenie ciała. Carry powalonego gracza przez innego gracza pozostaje dozwolone, a drain carry sumuje się z kosztem wody.

Rzeczywisty respawn usuwa statusy Etapu 1 i wpisy wody, więc ocena zagrożenia rozpoczyna się od nowa.

## HUD poradnika Stage02

Stage02 pokazuje lokalny HUD poradnika przez skrót `N` (domyślnie włączony). Preferencja jest lokalna i per poziom, przetrwa respawn, a resetuje się po ponownym załadowaniu sceny. Gdy panel wymagań `N` jest otwarty, `Tab` przełącza widoczność kompaktowego HUD; HUD nie zasłania zarezerwowanego miejsca timera i znajduje się w prawym górnym obszarze. Widok podaje szczegóły aktualnej porady, właściwe narzędzie, progres i aktywne przypisania.

Wskazówki są kontekstowe: aktywna istotna praca lub transport ma pierwszeństwo, potem cel użyty ostatnio (do 6 m), wykonalne przypisanie z bieżącego etapu i pozostała porada etapu. Istotny stan awarii przebija zwykłą poradę. Dane pochodzą z autorytatywnego snapshotu tylko do odczytu, więc aktualny stan jest dostępny także po late join. Zintegrowane przypisania z księgi nie zmieniają semantyki checkboxów: oznaczają odpowiedzialność, nie ukończenie.

HUD nie zmienia inputu ani gameplayu. Katalog Stage02 ma 28 kluczowanych angielskich kroków dla 6 etapów/13 części; nie dodaje network state, markerów ani ograniczeń kolejności. Authoring/additive, idempotentne ponowne otwarcie i read-only zweryfikowano w raportach `Artifacts/GuidanceFormatting/stage02-authoring.txt` i `Artifacts/GuidanceFormatting/stage02-reopen-validation.txt`. Runtime build, analizatory i kompilacja obu zestawów testów przeszły; testy ukierunkowane w izolowanej kopii trwają. Nie zweryfikowano wizualnie ani w bezpośredniej interakcji host-client; użyty istniejący NGO wire fixture był regresją. Strict live Gameplay oczekuje z powodu niepowiązanego dirty stanu lobby.
