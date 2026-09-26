# Git LFS — konfiguracja projektu

Projekt przechowuje duże pliki binarne w Git LFS dla rozszerzeń `.blend`, `.fbx` i `.exr`. Reguły znajdują się w `.gitattributes`.

## Konfiguracja na komputerze zespołu

1. Zainstaluj Git LFS: <https://git-lfs.com/>.
2. W katalogu repozytorium uruchom `git lfs install`.
3. Przy istniejącym klonie pobierz obiekty poleceniem `git lfs pull`.

Dotychczas śledzone pliki FBX i EXR zostały przygotowane jako wskaźniki LFS; ich bajty i historia Git pozostają bez zmian. Nie było dotąd śledzonych plików Blend. Nowe pliki `.blend`, `.fbx` i `.exr` będą obsługiwane przez LFS po dodaniu ich do indeksu poleceniem `git add`.

Zmiany `.gitattributes` oraz przygotowane konwersje plików wymagają zatwierdzenia w commicie. W tej konfiguracji nie wykonano commita, push ani wysyłania obiektów do zdalnego serwera. Uprawnienia serwera i dostępny limit miejsca nie zostały zweryfikowane.