# JPTV Streaming

Application de streaming vidéo japonaise multi-plateforme développée avec .NET MAUI Blazor.

![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4)
![MAUI](https://img.shields.io/badge/MAUI-Blazor-purple)
![Platform](https://img.shields.io/badge/Platform-Android%20%7C%20iOS%20%7C%20Windows%20%7C%20macOS-blue)

## Fonctionnalités

- Streaming de vidéos japonaises
- Navigation par liste avec chargement infini
- Traduction automatique des titres japonais (romanji + langue locale)
- Lecteur vidéo plein écran avec contrôles tactiles
- Configuration FlareSolverr pour bypass Cloudflare
- Support Android (mobile et TV)

## Prérequis

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Visual Studio 2022](https://visualstudio.microsoft.com/) avec les workloads MAUI
- Android SDK (API 28+)
- [FlareSolverr](https://github.com/FlareSolverr/FlareSolverr) pour le scraping

## Installation

### Cloner le repository

```bash
git clone https://github.com/Svenouu/jptvstreaming.git
cd jptvstreaming
```

### Restaurer les dépendances

```bash
dotnet restore
```

### Lancer en développement

```bash
# Android
dotnet build -f net9.0-android

# Windows
dotnet build -f net9.0-windows10.0.19041.0
```

## Publication

### APK Android (non signé)

```bash
dotnet publish -f net9.0-android -c Release
```

L'APK sera généré dans `bin/Release/net9.0-android/publish/`

### APK Android (signé)

Voir le guide détaillé : [PUBLISH-GUIDE.md](PUBLISH-GUIDE.md)

## Configuration

### FlareSolverr

L'application nécessite FlareSolverr pour contourner la protection Cloudflare.

```bash
# Lancer FlareSolverr avec Docker
docker run -d --name flaresolverr -p 8191:8191 ghcr.io/flaresolverr/flaresolverr:latest
```

Configurez l'URL dans les paramètres de l'application :
- **Réseau local** : `http://VOTRE_IP:8191/v1`
- **Émulateur Android** : `http://10.0.2.2:8191/v1`

## Structure du projet

```
jptvstreaming/
├── Components/
│   ├── Layout/           # Layouts Blazor
│   ├── Pages/            # Pages (Home, Settings)
│   ├── VideoCard.razor   # Carte vidéo
│   └── VideoPlayer.razor # Lecteur vidéo plein écran
├── Models/
│   └── VideoPost.cs      # Modèle de données vidéo
├── Platforms/
│   └── Android/          # Code spécifique Android
├── Services/
│   ├── ScrapingService   # Service de scraping
│   ├── TranslationService# Traduction des titres
│   └── BackButtonService # Gestion bouton retour Android
└── wwwroot/              # Assets statiques
```

## Utilisation

1. **Lancer l'application** sur votre appareil Android
2. **Configurer FlareSolverr** dans les paramètres
3. **Parcourir** la liste des vidéos
4. **Appuyer** sur une vidéo pour la lire
5. **Bouton retour** pour revenir à la liste

## Plateformes supportées

| Plateforme | Statut | Version minimale |
|------------|--------|------------------|
| Android    | Testé | Android 9 (API 28) |
| Android TV | Testé | Android TV 9 |
| Windows    | Compilable | Windows 10 19041 |
| iOS        | Compilable | iOS 15+ |
| macOS      | Compilable | macOS 13+ |

## Problèmes connus

- Le lecteur vidéo utilise une WebView, certains formats peuvent ne pas être supportés
- FlareSolverr est requis pour accéder au contenu

