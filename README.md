# Dominus Multipliers — a BepInEx mod for *Dominus Gladiatus*

Configurable multipliers for:

| Setting | Default | What it does |
|---|---|---|
| `MoneyMultiplier` | `2` | Money (denarii/gold) you **gain** is multiplied. Spending is untouched. |
| `DamageMultiplier` | `1.5` | Damage taken by **enemy** combatants is multiplied, so your gladiators hit harder. |
| `DefenseMultiplier` | `1.5` | Damage taken by **your** gladiators is divided by this (`2` = half damage). |
| `TrainingMultiplier` | `2` | Training / experience / stat-progress gains are multiplied. |

All four can be changed while the game is running (edit the config, or use
[BepInEx.ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager) with **F1**). Set a multiplier to `1` to turn it off.

> **Important:** I don't have access to the game's code, so the mod doesn't hard-code the game's method names.
> It looks for common names (`AddGold`, `TakeDamage`, `AddExperience`, …) and hooks them. If the game uses
> different names, the mod writes a **dump file** listing the game's real money/combat/training methods, and you
> add the right name to the config in one line (see [Making it fit your game version](#making-it-fit-your-game-version)).
> The hooking logic is tested against a fake game (`tests/`), but it hasn't been run against Dominus Gladiatus itself.

---

## 1. Install

1. **Find out which build the game is.** Open the game folder (Steam → right-click the game → *Manage → Browse local files*).
   - If there's a `GameAssembly.dll` → it's **IL2CPP**.
   - If there's a `<GameName>_Data/Managed/Assembly-CSharp.dll` and no `GameAssembly.dll` → it's **Mono**.
2. **Install BepInEx** into the game folder (the folder with the game's `.exe`):
   - Mono: [BepInEx 5.4.23.x, win x64](https://github.com/BepInEx/BepInEx/releases) — unzip into the game folder.
   - IL2CPP: [BepInEx 6 (Unity IL2CPP, win x64)](https://github.com/BepInEx/BepInEx/releases) — unzip into the game folder.
3. Start the game once and quit. BepInEx creates `BepInEx/plugins` and `BepInEx/config`. (IL2CPP's first start takes a while.)
4. **Copy the mod DLL** into `BepInEx/plugins`:
   - Mono: `dist/Mono/DominusMultipliers.dll`
   - IL2CPP: `dist/IL2CPP/DominusMultipliers.IL2CPP.dll`
5. Start the game. The config appears at `BepInEx/config/dominus.multipliers.cfg`.

Tip: in `BepInEx/config/BepInEx.cfg` set `[Logging.Console] Enabled = true` to see the mod's messages in a console window.
They're also in `BepInEx/LogOutput.log`.

## 2. Check it worked

In the log you should see lines like:

```
[Info: Dominus Multipliers] Hooked [Money] Void Ludus::AddGold(Int32 amount)  scales: amount  (rule *::AddGold)
[Info: Dominus Multipliers] Hooked [Damage] Void Gladiator::TakeDamage(Single damage)  scales: damage, receiver=this  (rule *::TakeDamage)
...
[Info: Dominus Multipliers] Dominus Multipliers 1.0.0: hooked 7 method(s). Money x2, Damage x1.5, Defense x1.5, Training x2
```

If a category says `No Money methods were hooked` (or Damage/Training), go to the next section.
Set `LogScaledCalls = true` to log the first few changes per method (e.g. `AddGold: 100 -> 200 (x2)`), which shows it's working.

## Making it fit your game version

At startup the mod writes **`BepInEx/DominusMultipliers-dump.txt`**. It contains:

- **HOOKED METHODS** — what the mod hooked and which value it scales.
- **CANDIDATE METHODS** for Money, Damage, and Training — every game method whose name suggests that area.
- **POSSIBLE SIDE MEMBERS** — the bool/enum fields on combatant types, which tell the mod whose gladiator is whose.

Pick the method that does the job and add it to the matching rule list in `dominus.multipliers.cfg`
(append with `;`), then restart the game. Examples:

```ini
# Money is added by  Void LudusManager::ChangeDenarii(Int32 delta, String reason)
MoneyRules = ...existing...;LudusManager::ChangeDenarii

# Damage arrives as a struct:  Void Combatant::OnHit(HitData hit)  and HitData has a field "finalDamage"
DamageRules = ...existing...;Combatant::OnHit@hit.finalDamage

# Training gain is a calculated value:  Single TrainingSystem::CalculateGain(Gladiator g, StatType stat)
TrainingRules = ...existing...;TrainingSystem::CalculateGain@return

# Money is a property, and the game just does  Denarii += reward
MoneyRules = ...existing...;PlayerData::set_Denarii@delta
```

### Rule syntax

`TypeGlob::MethodGlob[@target][!receiver]`

- `TypeGlob` / `MethodGlob`: names with `*` and `?` wildcards, not case-sensitive. The type part can be the short name (`Ludus`) or the full name (`Game.Economy.Ludus`). Leaving out `Type::` means any type.
- `@target` — what gets scaled:
  - *(nothing)* / `@first`: the first numeric parameter that looks like an amount. Parameters named like `index`, `slot`, `type`, or `level` are skipped.
  - `@all`: every numeric parameter.
  - `@amount` or `@1`: the parameter with that name or position (0-based).
  - `@hit.finalDamage`: a field or property inside a parameter (works for classes and structs, and dotted paths can go deeper).
  - `@return`: the method's return value (good for `Calculate…`/`Get…Gain` methods).
  - `@delta`: for a property setter `set_X`, scales only the increase (new − current).
    ⚠ If the game also uses that setter when loading a save, loading would multiply your money. Prefer a real "add" method when one exists.
- `!receiver` (damage rules only): the parameter holding the combatant that **takes** the damage, e.g. `Combat::Resolve!defender`.
  By default this is the object the method is called on, or the first object parameter for static methods.

`ExcludeRules` uses the same syntax and keeps UI/preview methods from being scaled. If something UI-related got hooked, add it there.

### Telling your gladiators from enemies (damage/defense)

For every hooked damage call, the mod looks at the receiving combatant and checks `PlayerSideMembers` in order:

- `isPlayer` → the member is `true` → it's yours.
- `team=Player` → the member's value (enum name or number) is `Player` → it's yours; any other value → enemy.
- Dotted paths work: `owner.isPlayer`, `gladiator.ludus.isPlayer`.

The first member that exists on the object decides. If none exist, the mod logs a warning listing that type's
bool/enum members and their current values, then leaves the damage alone (see `UnknownSide`). Add the right one, for example
`PlayerSideMembers = isPlayerTeam;...` or `PlayerSideMembers = side=Left;...`.

## Notes and limits

- **Double scaling is prevented.** If one hooked method calls another (`AddMoney` → `AddGold`, or an override calling `base.TakeDamage`), the value is scaled once.
- **Only plain number types** (`int`, `long`, `float`, `double`, `decimal`, …) are scaled. If the game stores values in an anti-cheat type such as `ObscuredInt`, those methods are skipped. Use `@return` or `@delta` on a method that does use plain numbers.
- **`Assemblies = auto`** scans every game DLL (Unity, System, and common third-party libraries are skipped). You can list them instead, e.g. `Assembly-CSharp`.
- **Game updates** can rename methods. If a category stops working, check the dump again.
- **This is a single-player cheat mod.** Back up your saves before trying new rules.

## Building from source

Requires the .NET SDK (6 or newer).

```bash
./tools/fetch-bepinex-libs.sh                                  # downloads BepInEx 5 + 6 reference DLLs into lib/
dotnet build build/Mono/DominusMultipliers.Mono.csproj -c Release      # -> bin/Mono/DominusMultipliers.dll
dotnet build build/IL2CPP/DominusMultipliers.IL2CPP.csproj -c Release  # -> bin/IL2CPP/DominusMultipliers.IL2CPP.dll
dotnet run --project tests/DominusMultipliers.Tests -c Release         # runs the hook logic against a fake game
```

To compile against your own BepInEx install, pass `-p:BepInExCore="<game>/BepInEx/core"`.

| File | Purpose |
|---|---|
| `src/DominusMultipliers/Plugin.cs` | BepInEx entry point (BepInEx 5 `BaseUnityPlugin` / BepInEx 6 `BasePlugin`) |
| `Mod.cs`, `ModConfig.cs` | Startup and all config settings |
| `Rule.cs`, `Patcher.cs` | Parses rules, finds matching game methods, hooks them with Harmony |
| `Hooks.cs` | The Harmony prefix/postfix/finalizer that does the scaling |
| `SideResolver.cs` | Decides whether a damage receiver is yours |
| `Dumper.cs` | Writes the dump file |
