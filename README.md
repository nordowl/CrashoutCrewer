<img width="1149" height="752" alt="grafik" src="https://github.com/user-attachments/assets/23602d2b-1303-4b50-9a67-756b8ab35adc" />

> [!CAUTION]
> This mod has not yet been tested with real players. But _should_ work fine.
> Will update README once I can confirm everything works.

# Crashout Crew — 6 Players

A [BepInEx](https://github.com/BepInEx/BepInEx) mod for **Crashout Crew** that raises the
co-op player limit from **4 to 6**.

It extends everything that was built for 4 players so the game keeps working with 6:

- Steam lobby size and the network connection limit
- The lobby itself — 2 extra parking spots (karts + painted bays) and the customization camera
- Proximity voice chat (works automatically once more players can connect)
- The end-of-contract report and per-shift report (extra stat rows)
- In-round UI — modifier voting icons, "ready / proceed" indicators, player spawns

> [!IMPORTANT]
> **Everyone in the lobby must have the mod installed** (host *and* all clients). It changes
> networked behaviour, so a vanilla player can't join a 6-player lobby and vice-versa.

---

## Install (players)

1. Download **BepInEx 5 (x64)** from the [releases page](https://github.com/BepInEx/BepInEx/releases),
   extract it into the game folder (next to `CrashoutCrew.exe`), and run the game once to generate the folders.

2. Download the latest release of this mod from the [releases page](https://github.com/nordowl/CrashoutCrewer/releases)
   and extract the contents (the `CrashoutCrew6` folder containing `CrashoutCrew6.dll`) into:
   ```…\steamapps\common\CrashoutCrew\BepInEx\plugins\```
   so the file ends up at `BepInEx\plugins\CrashoutCrew6\CrashoutCrew6.dll`.

3. Launch the game. On first run it creates a config file at
   `BepInEx\config\com.flux.crashoutcrew6.cfg`.

To uninstall, just delete the `CrashoutCrew6` folder from `BepInEx\plugins`.

---

## Development / Build from source

**Prerequisites**
- [.NET SDK](https://dotnet.microsoft.com/download) (6.0 or newer; developed with .NET 10)
- The game installed, with BepInEx and its `CrashoutCrew_Data\Managed` folder present — the
  project references the game/Unity/BepInEx DLLs directly from there (nothing is bundled).

**Build**
```sh
cd CrashoutCrew6
dotnet build -c Release
```
This compiles `CrashoutCrew6.dll` and automatically copies it into the game's
`BepInEx\plugins\CrashoutCrew6\` folder, so you can just launch the game to test.

If your game isn't at the default path, point the build at it (or edit the `GameDir` line near
the top of `CrashoutCrew6/CrashoutCrew6.csproj`):
```sh
dotnet build -c Release -p:GameDir="D:\Path\To\steamapps\common\CrashoutCrew"
```

### Decompiling the game (optional)
You **don't** need this to build the mod — it's only for browsing the game's code while
developing. The `decomp/` folder is git-ignored (it contains machine-specific paths), so
regenerate it locally if you want it:

```sh
# one-time: install the decompiler
dotnet tool install -g ilspycmd

# decompile the relevant assemblies (run from the repo root)
ilspycmd "<GameDir>\CrashoutCrew_Data\Managed\febjam.dll"          -o decomp/febjam          -p
ilspycmd "<GameDir>\CrashoutCrew_Data\Managed\aggro.core.dll"      -o decomp/aggro.core      -p
ilspycmd "<GameDir>\CrashoutCrew_Data\Managed\Assembly-CSharp.dll" -o decomp/Assembly-CSharp -p
```
`febjam` is the main game code and `aggro.core` is its engine/framework — that's where almost
everything this mod patches lives.
