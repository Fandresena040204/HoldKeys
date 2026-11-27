# HoldKeys

Petit utilitaire Windows pour taper les caractères spéciaux en **maintenant une touche**, sans connaître les codes Alt.

![logo](HoldKeys.ico)

## Fonctionnalités

- **Maintenir une lettre** (ex. `e`) : un popup propose `é è ê ë ē ę €`.
  - Espace / → : suivant, ← : précédent, **1-9** : choix direct, ou **clic / survol à la souris**.
  - Relâcher la lettre insère le caractère choisi ; Échap annule.
  - Maj / Verr. Maj donnent les majuscules.
- **Derniers caractères utilisés en premier** (historique conservé, effaçable).
- **Recherche de symboles par nom** avec **Win+Alt+Espace** : tapez « flèche », « euro », « cœur », « e aigu »… puis Entrée pour insérer (Ctrl+Entrée pour copier). Plus de 400 symboles : lettres accentuées, typographie, monnaies, maths, grec, flèches, formes, emoji.
- **Fenêtre Paramètres** (double-clic sur l'icône) : délais, raccourci de recherche, caractères par touche, démarrage avec Windows.
- Icône dans la zone de notification, lancement automatique à l'ouverture de session.

## Installation

Aucun prérequis : le compilateur C# de .NET Framework 4 est inclus dans Windows.

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

L'application est compilée puis installée dans `%LOCALAPPDATA%\Programs\HoldKeys` (sans droits administrateur), avec un raccourci dans le menu Démarrer et le démarrage automatique.

Désinstallation :

```powershell
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\HoldKeys\uninstall.ps1"
```

## Configuration

Les réglages sont dans `HoldKeys.txt` à côté de l'exécutable (modifiable via la fenêtre Paramètres) :

```
delay = 250              # ms de maintien avant qu'Espace ouvre le popup
autoshow = 400           # ouverture automatique après X ms (0 = désactivé)
recent = 1               # derniers caractères utilisés en premier
searchkey = Win+Alt+Space
e = é è ê ë ē ę €        # une ligne par touche (a-z, 0-9)
E = É È Ê Ë              # majuscules définies à part (optionnel)
```

## Développement

| Fichier | Rôle |
|---|---|
| `HoldKeys.cs` | Hook clavier, popup, icône de notification, historique |
| `Settings.cs` | Lecture/écriture de la config, fenêtre Paramètres |
| `Search.cs` | Base de symboles, fenêtre de recherche, raccourci global |
| `make-icon.ps1` | Génère `HoldKeys.ico` |
| `build.ps1` | Compile `HoldKeys.exe` |

Limite : les applications lancées en administrateur ne reçoivent pas les caractères (protection de Windows).
