# Roadmap i audyt aktualnego stanu

## Jak interpretować ten dokument

Sekcje są rozdzielone na:

1. fakty potwierdzone w repo;
2. wcześniej uzgodnione kierunki rozwoju;
3. rekomendacje wynikające z audytu.

Rekomendacja nie oznacza istniejącej funkcjonalności.

## Potwierdzone ograniczenia repo

### Wysoki priorytet

- `BlastFurnaceMinigame` zawiera `NotImplementedException`.
- `CarpenterTableMinigame` zawiera `NotImplementedException`.
- `BridgeComponent` ma niezaimplementowaną ścieżkę z
  `NotImplementedException`.
- `NPCCarrier.IsSharedCarryEnabled` jest stałym feature gate ustawionym na
  `false`.
- Projekt ma wiele scenowych override'ów, których przypadkowe cofnięcie może
  usunąć katalog fabryki, IDs albo prerequisites.

### Content i prezentacja

- Większość nowych części mostu używa placeholderowych brył.
- Koza i część NPC visuals/animacji są techniczne, nie finalne.
- Tutorial jest blockoutem; rzeka, meta, markery i część szyldów nie mają
  finalnego artu.
- Stage info przekazuje tekst, ale nie wymusza kompletnej sekwencji tutorialu.
- Nie wszystkie przygotowane SO części mają prefab, holder i integrację.

### Architektura

- `GameplayManager` przechowuje polimorficzny construction state w ogólnych
  polach `constructionValue/Anchor/Aux`.
- `BridgeComponentType` nie odpowiada pełnemu katalogowi nowych elementów.
- Część klas zachowuje pola lub komentarze tymczasowe, np.
  `PlayerInteractionNew.temp` i `InteractionOutlineGameobject`.
- Interfejs `IDamageable` łączy HP, niszczenie surowca i pracę konstrukcyjną.
- Starsze dokumenty i plany nie odzwierciedlają obecnej architektury.

### Testy

- Repo nie ma kompletnego zestawu automatycznych testów gameplayu NGO.
- Najważniejsze przepływy są obecnie weryfikowane manualnie host/client.
- Fizyka shared-carry wymaga testów z symulowanym latency i różnym FPS.

## Uzgodniony roadmap

### Most

- Dokończyć filary, oczepy i łożyska.
- Zintegrować przygotowane bariery, connectory i pozostałe rodzaje części.
- Rozbudować katalog prefabów, holderów, receptur i kolejność aktywnego mostu.
- Zastąpić placeholdery finalnymi modelami oraz poprawić collidery/anchory.

### Tutorial

- Podłączyć stage info dla kolejnych typów części.
- Dodać właściwe kroki, warunki i ewentualne blokady kolejności nauki.
- Zastąpić tymczasowy ręczny `Start timer` docelową czynnością tutorialową.
- Dopracować oznaczenia stref, mapę i czytelność punktów montażowych.

### NPC

- Dostrajać Goat standing, charge, push i external impulse.
- Dodać finalne modele, telegraph, animacje, audio i VFX.
- Rozbudować neutralne NPC i świadomie zdecydować o ponownym włączeniu
  shared-carry NPC.
- Rozszerzyć profile zainteresowań/destruction wraz z nowym contentem.

### Gracz i feedback

- Dodać audio/material mapping do istniejących eventów kroków.
- Dodać ustawienia dostępności dla bobu, FOV, turn feedback, flashes i outline.
- Dopracować finalny HUD, ikony i modele FPP arms.

### Produkcja i zasoby

- Dokończyć minigry Carpenter/Furnace.
- Rozszerzyć receptury o wykorzystanie żelaza i kolejne materiały.
- Dodać finalne modele zasobów i narzędzi.

## Rekomendacje audytu

### 1. Usunąć jawne wyjątki z aktywnych ścieżek

Każde `NotImplementedException` powinno zostać zastąpione implementacją albo
bezpiecznym, logowanym wynikiem `false`. Wyjątek w gameplayu może zakończyć
całą sesję testową.

### 2. Rozdzielić typy pracy

Wprowadzić osobne kontrakty dla:

- health damage;
- resource harvesting;
- construction work.

Zmniejszy to ryzyko przypadkowego routingu promptu lub obrażeń.

### 3. Nadać construction state typowaną reprezentację

Rozważyć osobne serializowalne struktury per workflow albo jawny union/version.
Obecne pola ogólne są oszczędne, lecz trudne do utrzymania przy kolejnych
częściach.

### 4. Dodać walidatory edytorowe

Automatyczna walidacja powinna wykrywać:

- powtórzone component IDs;
- brakujące SO/prefaby/workflow;
- NetworkPrefab niezarejestrowany;
- nieaktywne lub widoczne w złym stage visuale;
- collider ghosta niebędący triggerem;
- katalog fabryki z niekompletnym produktem;
- brakujące inventory catalog entries.

### 5. Dodać testy

Priorytet:

1. serializacja `BridgeComponentNetworkState`;
2. transition tests każdego workflow;
3. server validation pickup/action/production;
4. forced drop i stamina recovery;
5. NPC state transitions;
6. restart i late join.

### 6. Ograniczyć scene overrides

Dane zależne od poziomu powinny pozostać override'ami, ale wspólne hierarchie
UI, narzędzi i workflow lepiej utrzymywać w prefabach/variantach. Do sceny
powinny trafiać głównie kolejność, pozycje, prerequisites i katalog.

### 7. Utrzymywać dokumentację razem ze zmianą

Definition of Done dla nowego systemu powinno zawierać:

- aktualizację odpowiedniego dokumentu systemowego;
- dopisanie pól do Configuration Reference;
- aktualizację statusu/roadmap;
- test host/client, jeśli zmiana dotyczy sieci.

## Deprecated appendix

Nie używać jako podstawy nowego contentu bez osobnego audytu:

- `Assets/Scripts/NewScripts/Deprecated`;
- starsze `BaseResourceSource`;
- stare przyciski `DimensionChangeSwitch`;
- historyczne sceny/testy spoza aktualnego flow;
- `Assets/Project_Overview.md` oraz plany opisujące dawną architekturę.

