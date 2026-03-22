# NetLauncher 🚀

A custom, lightweight Minecraft launcher built from scratch in C# with WinForms. No premium account required.

## Features

- 🎮 Launch any Minecraft release version (1.0 through 1.21+)
- 📦 Automatic download of game files, libraries, and assets
- 💾 Offline support — play without internet once a version is downloaded
- ☕ Automatic Java version detection (supports Java 8, 16, 17, 21, 25)
- ⚙️ Settings panel with RAM slider, JVM arguments, and snapshot toggle
- 🔤 Saves last used version and username between sessions
- 📋 Visual download progress bar
- 🖊️ Downloaded versions shown in **bold** in the version selector

## Screenshots
<img width="568" height="473" alt="image" src="https://github.com/user-attachments/assets/e9229a68-a351-487d-9f46-ed0de10ac644" />

## Requirements

- Windows 7SP1-11
- .NET Framework 4.7.2 or later
- Java installed (version depends on Minecraft version):

| Minecraft Version | Required Java |
|---|---|
| 1.0 – 1.16 | Java 8 |
| 1.17 | Java 16 |
| 1.18 – 1.20 | Java 17 |
| 1.21 | Java 21 |
| Latest snapshots | Java 25 |

## Installation

1. Download the latest release from the [Releases](../../releases) page
2. Run `NetLauncher.exe`
3. Make sure you have the appropriate Java version installed

## Building from source

1. Clone the repository:
```
   git clone https://github.com/MeLlamoGamer/NetLauncher.git
```
2. Open `NetLauncher.sln` in Visual Studio
3. Build → Build Solution (`Ctrl+Shift+B`)
4. Run with `F5`

## How it works

NetLauncher communicates directly with Mojang's public API to fetch version metadata, download game files, libraries, and assets — all without using any third-party launcher libraries.
```
Launch flow
    ├── Fetch version manifest from Mojang API (cached locally for offline use)
    ├── Download client .jar + libraries → build classpath
    ├── Extract native .dll files
    ├── Download assets (textures, sounds)
    │   ├── Modern format (1.7+) → assets/objects/
    │   └── Legacy format (1.6 and below) → resources/
    ├── Generate offline session (UUID v3)
    └── Launch Java process with correct arguments
```

## Game files location

All game files are stored in:
```
%APPDATA%\.NetLauncher\
    versions\        ← game jars and version JSONs
    libraries\       ← dependency jars
    assets\          ← textures, sounds (modern)
    resources\       ← textures, sounds (legacy)
    launcher_settings.json
    launcher_debug.log
```

## Known limitations

- **No Microsoft/Mojang authentication** — offline mode only, cannot join online-mode servers
- **Few mod loader support** — only fabric is supported yet
- **Some 1.6.x versions may have missing sound** — work in progress
- **Windows only** — WinForms does not support Linux or macOS

## License

MIT License — see [LICENSE](LICENSE) for details.
