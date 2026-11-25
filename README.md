# Space Defender

A turn-based 3D tactics game: aliens are pouring into the city through portals, and your squad has to hold out for 12 turns.

**Play it:** https://tsripata.github.io/spacedefender/

## How to play

- **Survive 12 turns** and keep the city's power grid above **50%**. Aliens damage buildings, and every collapsed floor drains the grid.
- Each soldier has **2 actions**: move + move, or move + shoot/throw. Shooting or throwing ends that soldier's turn.
- **Hit chance** is shown before every shot:
  - farther = lower (−5% per tile beyond the weapon's range)
  - **high ground** +10% per level (max +20%; Sniper +10% more)
  - **cover**: walls −40%, cars/crates/trees −20%; shooting from 2+ levels above halves cover
- **Climbing**: step up one level at a time, or use the **yellow ladders** to climb buildings. Rooftops see further.
- **Fog of war**: you only see what your soldiers (and Scout beacons) can see. Noises in the dark show as red **!** pings.
- **Telegraphed attacks**: visible aliens show what they'll do next turn. **Red tile** = attack target, **green area** = acid grenade, **purple** = where they'll move. Get out of the way.
- **Explosives**: grenades and rockets destroy cover, set off cars (chain reactions) and collapse building floors. Units standing on a floor that collapses fall and get hurt.

### Squad

| Soldier | Strength | Special (key 3) |
|---|---|---|
| Rifleman | Solid all-rounder, 2 grenades | — |
| Sniper | Long range, big high-ground bonus, poor up close | Steady Aim: +20% on the next shot |
| Heavy | Tough | Rocket: 4 dmg area blast, wrecks buildings (x2) |
| Scout | Fast, sees far, climbs anything | Sensor Beacon: reveals an area for 3 turns (x2) |

### Aliens

| Alien | Behaviour |
|---|---|
| Crawler | Fast melee, climbs walls, tears into buildings |
| Spitter | Ranged acid shots and acid grenades |
| Brood Pod | Stationary, hatches a Crawler every 3 turns. Kill it fast |

### Controls

Left click: select / move / target · **1** Shoot · **2** Grenade · **3** Special · **Esc** cancel · **Tab** next soldier · **Space** end turn · **WASD** / right-drag pan · **Q/E** rotate · scroll zoom

## Project

- Unity **6.3 LTS** (6000.3.29f1), URP 3D. All game code is in `Assets/Scripts/Game/`. The map, units and visuals are generated in code, so the scene stays empty.
- Web build: Unity menu **SpaceDefender → Build Web** builds to `WebBuild/` (gzip and decompression fallback, so it works on GitHub Pages). Copy it to `docs/` and push, and the GitHub Action publishes it.
- `Assets/Editor/ClaudeBridge/` is an editor helper that lets builds, compile checks and an automated playtest be triggered by request files in `ClaudeBridge/requests/`.
