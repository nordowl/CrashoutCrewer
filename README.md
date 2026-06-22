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

2. Download the latest release of this mod from the releases page
   and extract the contents (the `CrashoutCrew6` folder containing `CrashoutCrew6.dll`) into:
   ```…\steamapps\common\CrashoutCrew\BepInEx\plugins\```
   so the file ends up at `BepInEx\plugins\CrashoutCrew6\CrashoutCrew6.dll`.

3. Launch the game. On first run it creates a config file at
   `BepInEx\config\com.flux.crashoutcrew6.cfg`.

To uninstall, just delete the `CrashoutCrew6` folder from `BepInEx\plugins`.

---

## Development / Build from source

Ran out of Claude usage so this will follow soon (if I dont forget lol)

---

yes, this is vibecoded
