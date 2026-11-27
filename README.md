# HoldKeys

Petit utilitaire Windows pour taper les caractères spéciaux en **maintenant une touche**, sans connaître les codes Alt.

![logo](HoldKeys.ico)

## Fonctionnalités

- **Maintenir une lettre** (ex. `e`) : un popup propose `é è ê ë ē ę €`.
  - Espace / → : suivant, ← : précédent, **1-9** : choix direct, ou **clic / survol à la souris**.
  - Relâcher la lettre insère le caractère choisi ; Échap annule.
  - Maj / Verr. Maj donnent les majuscules.
- **Derniers caractères utilisés en premier** (historique conservé, effaçable).
- **Lanceur universel** avec **Win+Alt+Espace** (voir ci-dessous).
- **Collage avancé** avec **Win+Shift+V** (voir ci-dessous).
- **Texte rapide** : tapez `;mail`, `;sig`, `;date`… n'importe où, l'abréviation est remplacée par votre texte.
- **Historique du presse-papiers** chiffré avec votre compte Windows, avec épinglage (les mots de passe des gestionnaires sont ignorés).
- **Fenêtre Paramètres** en onglets (double-clic sur l'icône) : délais, raccourci, caractères par touche, texte rapide, presse-papiers, démarrage avec Windows.
- Icône dans la zone de notification, lancement automatique à l'ouverture de session.

## Lanceur

| Tapez | Pour |
|---|---|
| *(rien)* | applications (les plus utilisées en premier), fenêtres, calculs, paramètres Windows, commandes système, fichiers, symboles |
| `=` | calculatrice et conversions : `2*(3+4)`, `sqrt(2)`, `15%`, `10 km en mi`, `30 c en f`, `1 go en mo`, `255 en hex` |
| `<` | basculer vers une fenêtre ouverte (Ctrl+Entrée : la fermer) |
| `?` | fichiers et dossiers de l'index Windows (Ctrl+Entrée : ouvrir l'emplacement) |
| `>` | commande dans une invite (Ctrl+Entrée : en administrateur) |
| `!` | recherche web : `!g`, `!yt`, `!w`, `!tr`, `!maps`, `!gh`, `!img`, `!ddg` |
| `:` | symboles et emoji (plus de 400) : `:flèche`, `:euro`, `:e aigu` |
| `;` | texte rapide |
| `v ` | historique du presse-papiers (Ctrl+Entrée : épingler, Maj+Suppr : effacer) |
| `%` | actions sur le texte sélectionné : majuscules, sans accents, trier, dédoublonner, encoder URL, traduire, compter |
| `C:\…` `~` `%APPDATA%` | parcourir les dossiers (Tab : compléter) |
| `mdp 20` `guid` `date` `sha256 …` `lorem 30` `nombre 1 100` | générateurs |
| `verrouiller` `veille` `éteindre` `vider la corbeille`… | commandes système (confirmation pour les actions risquées) |

Entrée : action principale (lancer, insérer, coller…) · Ctrl+Entrée : action secondaire · Tab : compléter · Échap : effacer puis fermer.

## Collage avancé

**Win+Shift+V** (ou `+` dans le lanceur) propose de coller ce que vous avez copié sous une autre forme. Le presse-papiers d'origine est restauré après le collage.

| Contenu copié | Actions |
|---|---|
| Texte | texte brut · JSON (indenté, minifié, CSV/TSV → JSON, « clé : valeur » → JSON) · fichier `.txt` · MAJUSCULES, sans accents, lignes triées, sans doublons, URL encodée… |
| Texte mis en forme (page web, Word…) | Markdown (titres, gras, liens, listes, tableaux) · fichier `.html` ou `.md` |
| Image | texte de l'image (OCR de Windows, hors ligne) · fichier `.png` · Base64 (data URI) |
| Fichiers | chemins · noms · contenu d'un fichier texte |
| IA (optionnel) | actions personnalisables (corriger, traduire, résumer…) et instruction libre tapée dans la barre, via l'API Claude |

Ctrl+Entrée : copier le résultat au lieu de le coller (ou, pour un fichier, l'enregistrer dans Téléchargements).

Les actions IA utilisent votre propre clé API Claude (Paramètres > Collage avancé, stockée chiffrée avec votre compte Windows). Le contenu copié n'est envoyé à l'API Anthropic que lorsque vous choisissez une action IA. Sans clé, tout le reste fonctionne hors ligne.

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
searchkey = Win+Alt+Space  # raccourci du lanceur
clipboard = 1            # historique du presse-papiers
e = é è ê ë ē ę €        # une ligne par touche (a-z, 0-9)
E = É È Ê Ë              # majuscules définies à part (optionnel)
```

## Développement

| Fichier | Rôle |
|---|---|
| `HoldKeys.cs` | Hook clavier, popup, texte rapide, icône de notification, historique |
| `Settings.cs` | Lecture/écriture de la config, fenêtre Paramètres |
| `Search.cs` | Base de symboles, raccourci global, écoute du presse-papiers |
| `Launcher.cs` | Fenêtre du lanceur, modèle de résultats, icônes, usage |
| `Providers.cs` | Modules : applications, fenêtres, calcul, unités, chemins, URL, fichiers, web, système, commandes |
| `Extras.cs` | Presse-papiers, texte rapide, générateurs, actions sur le texte, symboles |
| `Paste.cs` | Collage avancé : conversions, fichiers, OCR (API Windows), IA (API Claude) |
| `make-icon.ps1` | Génère `HoldKeys.ico` |
| `build.ps1` | Compile `HoldKeys.exe` |

Limite : les applications lancées en administrateur ne reçoivent pas les caractères (protection de Windows).
