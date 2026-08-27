# Znane problemy odłożone na później

## Cel dokumentu

Ten dokument rejestruje potwierdzone problemy, które nie blokują obecnego
etapu produkcji i nie wymagają natychmiastowej poprawki. Wpis pozostaje tutaj
do czasu rozpoczęcia prac nad wskazanym systemem albo podniesienia jego
priorytetu.

Każdy problem powinien zawierać kroki reprodukcji, rezultat obecny,
oczekiwane zachowanie oraz aktualny status.

## KI-001: Cofanie animacji przechyłu taczki po utracie synchronizacji

- **Status:** odłożony
- **Priorytet:** niski
- **System:** minigra wylewania betonu
- **Tryb:** multiplayer, dwóch graczy
- **Data zgłoszenia:** 2026-08-26

### Opis

Po rozpoczęciu przechylania taczki zbyt duża różnica między pozycjami
kursorów graczy cofa visual taczki do początkowej pozycji transportowej.
Problem nie kończy minigry natychmiast, jeśli różnica nie utrzyma się przez
czas wymagany do krytycznej porażki.

### Kroki reprodukcji

1. Wejdź dwoma graczami do minigry wylewania betonu.
2. Zwiększ progres obu graczy tak, aby taczka zmieniła początkowe położenie.
3. Zwiększ progres jednego gracza tak, aby różnica kursorów przekroczyła
   akceptowaną tolerancję.
4. Obserwuj położenie taczki.

### Rezultat obecny

Taczka wraca do początkowej pozycji transportowej.

### Rezultat oczekiwany

Po przekroczeniu tolerancji dalsze przechylanie powinno zostać zatrzymane,
ale taczka powinna zachować ostatni osiągnięty kąt do czasu ponownego
zsynchronizowania kursorów albo rozstrzygnięcia krytycznej porażki.

### Obejście

Utrzymywać różnicę kursorów w akceptowanej tolerancji podczas całej minigry.

## KI-002: Katapultowanie narzędzi przy uruchomieniu sceny

- **Status:** odłożony
- **Priorytet:** niski
- **System:** narzędzia i fizyka świata
- **Tryb:** uruchamianie sceny
- **Data zgłoszenia:** 2026-08-26

### Opis

Podczas inicjalizacji sceny narzędzia otrzymują niepożądany impuls fizyczny i
zostają rozrzucone wokół stojaka zamiast pozostać w przygotowanych pozycjach.

### Kroki reprodukcji

1. Uruchom scenę zawierającą narzędzia, na przykład `Tutorial_scene`.
2. Obserwuj narzędzia znajdujące się przy stojaku bez wykonywania interakcji.

### Rezultat obecny

Narzędzia zostają katapultowane i rozrzucone wokół stojaka.

### Rezultat oczekiwany

Narzędzia pozostają stabilnie na swoich skonfigurowanych miejscach do czasu
interakcji gracza albo zadziałania uzasadnionej siły zewnętrznej.

### Obejście

Ręcznie zebrać rozrzucone narzędzia albo ponownie umieścić je przy stojaku.
