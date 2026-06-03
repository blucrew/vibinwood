# Vibinwood

A [BepInEx 6](https://github.com/BepInEx/BepInEx) plugin that drives haptic devices from
in-game events in **Robin Morningwood Adventure — The Whellcum's Secret (TWS)**.
Fight, jerk off, win a hot scene, climax — and feel it.

Two outputs fire in parallel for every event:

- **Intiface / Buttplug** — local USB/Bluetooth toys via [Intiface Central](https://intiface.com/central/).
- **XToys** — cloud toys via an [xtoys.app](https://xtoys.app) Private Webhook.

> ⚠️ **Adult content. 18+.** Integrates with adult haptic hardware.

---

## Engine facts (for modders)

| | |
|---|---|
| Game | Robin Morningwood Adventure — TWS, by GrizzlyGamerStudio |
| Unity | **2022.3.20f1**, **IL2CPP** (`GameAssembly.dll` + `il2cpp_data/`) |
| Loader | **BepInEx 6 (Unity.IL2CPP, win-x64)** — *not* BepInEx 5 (that's Mono only) |
| Plugin runtime | .NET 6 CoreCLR (BepInEx's bundled `dotnet/`) |
| Anti-cheat | none |

This is the IL2CPP sibling of the 7 Days to Die (Mono/BepInEx 5) plugin. The output
layer (`ButtplugManager`, `XToysManager`, `HapticsLogger`) is shared almost verbatim;
only the bootstrap + Harmony patch layer differ.

## Mapped events (`EVENT_MAP.md`)

All targets are in `Assembly-CSharp`, global namespace. 11 hooks bind live:

- **Combat** (`BattleScreenController`): `Attack`, `ExecuteEnemyAttack`,
  `DisplayRobinsLife(int damage)` (intensity scales with the hit), `JerkOff`,
  `ForceGameOverRobinCums`, `GameOver(EndType)` → RobinCums / EnemyCums / DoubleCumshot.
- **Hot scenes**: `HotSceneController.StartDisplay` / `DisplayNextScene`,
  `PlayerSetItemController.WinHotScene` / `LoseHotScene`.
- **Brawl**: `BrawlCumshotAttackPanel.Play`.

> `BattleScreenController.EnemyStartsCumming()` is **intentionally not hooked** — the
> title-screen attract loop calls it every frame (~1200/min), which would machine-gun the
> toy. The enemy-climax moment is already covered by `GameOver(EnemyCums)` + the brawl panel.
> A per-event debounce (`max(150ms, DurationMs)`) guards against any other rapid re-fires.

---

## Install (players)

1. **Install BepInEx 6 IL2CPP (win-x64):** download
   `BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2.zip` from the
   [BepInEx releases](https://github.com/BepInEx/BepInEx/releases) and extract it into the
   game folder (next to `Robin Morningwood Adventure.exe`). Launch once and quit — this
   generates `BepInEx/interop`, `plugins`, `config`.
2. **Runtime dependency fix (one-time):** Buttplug's websocket connector references
   `System.Threading.Channels 7.0.0.0`, but BepInEx's bundled .NET 6 runtime pins 6.0.0.0
   in its TPA list, so CoreCLR hard-fails the load. Copy the `System.Threading.Channels.dll`
   that ships with this plugin over `…\dotnet\System.Threading.Channels.dll` (a `.net6bak`
   backup is kept). The 7.0 file is the net6-targeted build (binds `System.Runtime 6.0.0.0`),
   so it's safe. *Intiface won't work without this step.*
3. Copy this plugin's `Vibinwood/` folder into `BepInEx/plugins/`.
4. Start **Intiface Central** ("Start Server") and/or set up XToys (below).
5. Launch the game. Settings live in `BepInEx/config/com.vibinwood.haptics.cfg`.

## XToys setup (optional — cloud devices)

You need a running XToys script with a **Private Webhook** + **Generic Output** block and a
`setIntensity` Global Trigger. The published **[7dtvibe](https://xtoys.app/scripts/7dtvibe)**
script is a ready-made one that works as-is:

1. Open **[xtoys.app/scripts/7dtvibe](https://xtoys.app/scripts/7dtvibe)**, connect your toy
   under **Generic Output**, press **▶**, keep the tab open.
2. In `com.vibinwood.haptics.cfg` (`[XToys]`): `Enabled = true`, `WebhookId = <your-id>`.

The plugin POSTs `https://webhook.xtoys.app/<id>` with `{"action":"setIntensity","intensity":0-100}`.

---

## Modes & configuration

Everything is tuned in `BepInEx/config/com.vibinwood.haptics.cfg` (the running game reads it on launch). There is **no in-game panel** — see the note below.

> 🎛️ **Config editor:** open [`ConfigEditor.html`](ConfigEditor.html) in any browser, **Load** your `com.vibinwood.haptics.cfg`, tweak everything with sliders/dropdowns (modes, per-event tuning, toy routing), then **Download** it back into `BepInEx/config/`. It edits values in place and preserves the file structure, so the plugin reads it cleanly.

**`[General] Mode`** — the master behaviour:
- `Discrete` — event pulses only (hits, climaxes, hot scenes, brawl)
- `Continuous` — a steady buzz during battles that tracks the live arousal meter
- `Both` — continuous buzz with discrete pulses layered on top (default)
- `Off`

**Continuous (battle-meter) mode.** The battle life bar in this game *is* an arousal/edging meter — it drains toward climax. While a battle is active the plugin reads both meters every frame and drives your toys:
- `[Continuous] Invert = true` — vibration **swells as the meter drains toward climax** (`arousal = 1 − life`). `false` tracks remaining life directly.
- `Multiplier`, `MinIntensity`, `MaxIntensity` — shape the curve.
- `[Continuous] ToyRouting` — per-toy meter routing, e.g. `Lovense Gush 2=Robin;Some Other Toy=Enemy`. Unlisted toys default to **slot 0 → Robin, others → Enemy**. So with two toys, one reacts to your character and one to the enemy.
- `[Continuous] XToysSource` — which meter the XToys output follows (`Robin` / `Enemy` / `Both` / `Off`).

> **No in-game GUI.** A custom IMGUI overlay would need an injected `OnGUI` MonoBehaviour, but `Il2CppInterop`'s ClassInjector hits an uncatchable native `RewriteType` AccessViolation on this game's runtime (confirmed across `Load()`, deferred injection, a field-free type, and the latest BepInEx with a fresh cache). So config is file-driven. An external config editor is the planned GUI.

## Build (developers)

Requires the **.NET SDK** and a BepInEx-6-initialised copy of the game (for the interop
reference assemblies). Edit `<GameDir>` in `Vibinwood.csproj`, then:

```sh
dotnet build -c Release
```

The plugin + its managed deps are copied to `<GameDir>\BepInEx\plugins\Vibinwood\`.

| File | What |
|---|---|
| `Plugin.cs` | BepInEx 6 `BasePlugin` entry; AssemblyResolve shim; per-hook resilient patching. |
| `HapticsConfig.cs` | Master/XToys config + the per-event table + dispatch (with debounce). |
| `Patches/Hooks.cs` | Harmony postfix bodies, wired one-by-one in `Plugin.ApplyPatches`. |
| `ButtplugManager.cs` / `XToysManager.cs` / `HapticsLogger.cs` | Shared output layer (ported from the 7DTD plugin). |

### Reversing workflow

`tools/Il2CppDumper` was run against `GameAssembly.dll` + `global-metadata.dat` to produce
`dump/dump.cs` (the source of `EVENT_MAP.md`). Re-run it after a game update to re-map
renamed methods; the per-hook patcher logs a `✗` for any target that no longer resolves.
