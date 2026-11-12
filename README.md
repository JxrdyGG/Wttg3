# Dominus Multipliers — a BepInEx mod for *Dominus Gladiatus*

Configurable multipliers for everything that gives you an edge. Values below 1 reduce something (cheaper, faster, less).

| Group | Setting | Default | What it does |
|---|---|---|---|
| Economy | `MoneyMultiplier` | `2` | Money you **gain**: fight purses, sales, bets, rewards |
| | `CostMultiplier` | `0.5` | Money you **spend**: gladiators, gear, upkeep, wages, buildings |
| | `SellPriceMultiplier` | `1.5` | Sale value of gladiators/items you sell |
| Combat | `DamageMultiplier` | `1.5` | Damage **enemies** take (your gladiators hit harder) |
| | `DefenseMultiplier` | `1.5` | Damage **your** gladiators take is divided by this |
| | `MaxHealthMultiplier` | `1.5` | Max health of your gladiators |
| | `CombatChanceMultiplier` | `1.25` | Your hit / crit / dodge / block / parry chances and crit damage |
| Gladiator care | `HealingMultiplier` | `2` | Healing and health regeneration |
| | `StaminaRegenMultiplier` | `2` | Stamina/energy regained |
| | `StaminaCostMultiplier` | `0.5` | Stamina/energy used |
| | `FatigueMultiplier` | `0.5` | Fatigue / exhaustion / stress built up |
| | `InjuryChanceMultiplier` | `0.5` | Chance, severity, and duration of injuries and death |
| Progression | `TrainingMultiplier` | `2` | Training / experience / skill and stat gains |
| | `FameMultiplier` | `2` | Fame, reputation, popularity, prestige, crowd favor |
| | `MoraleMultiplier` | `2` | Morale, loyalty, happiness, motivation |
| | `DurationMultiplier` | `0.5` | Time for training, injury recovery, construction, upgrades, crafting |

Only **your** side is boosted. Rival gladiators and rival schools that the mod can identify (see
[Telling yours from theirs](#telling-yours-from-theirs)) are left alone, except that enemies take extra damage.

Every multiplier can be changed while the game is running (edit the config, or use
[BepInEx.ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager) with **F1**). Set a multiplier to `1` to turn it off.

> **Important:** I don't have access to the game's code, so the mod doesn't hard-code the game's method names.
> It looks for common names (`AddGold`, `SpendGold`, `TakeDamage`, `Heal`, `AddExperience`, `GetInjuryChance`, …) and hooks them.
> Some multipliers will probably match nothing in the real game at first. The mod tells you which ones in the log, and writes a
> **dump file** listing the game's real money/combat/health/training/fame/duration methods, so you
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
[Info: Dominus Multipliers] Dominus Multipliers 1.1.0: hooked 9 method(s). Damage x1.5, Defense x1.5, MaxHealth x1.5, ...
[Warning: Dominus Multipliers] Nothing was hooked for: CombatChance, Fatigue, Duration. Those multipliers do nothing until ...
```

Each multiplier listed under "Nothing was hooked" has no effect yet. Use the next section to connect it to the game.
Set `LogScaledCalls = true` to log the first few changes per method (e.g. `AddGold: 100 -> 200 (x2)`), which shows it's working.

## Making it fit your game version

At startup the mod writes **`BepInEx/DominusMultipliers-dump.txt`**. It contains:

- **HOOKED METHODS** — what the mod hooked and which value it scales.
- **CANDIDATE METHODS** for money/prices, combat, health/stamina/injuries, training, fame/morale, and durations — every game method whose name suggests that area.
- **POSSIBLE SIDE MEMBERS** — the bool/enum fields on gladiator/ludus types, which tell the mod what's yours.

Each multiplier has its own rule list in `dominus.multipliers.cfg` (section *5. Hook rules*): `MoneyRules`, `CostRules`,
`SellPriceRules`, `DamageRules`, `MaxHealthRules`, `CombatChanceRules`, `HealingRules`, `StaminaRules`, `StaminaCostRules`,
`FatigueRules`, `InjuryRules`, `TrainingRules`, `FameRules`, `MoraleRules`, `DurationRules`.
Pick the method that does the job and add it to the matching rule list
(append with `;`), then restart the game. Examples:

```ini
# Money is added by  Void LudusManager::ChangeDenarii(Int32 delta, String reason)
MoneyRules = ...existing...;LudusManager::ChangeDenarii

# Damage arrives as a struct:  Void Combatant::OnHit(HitData hit)  and HitData has a field "finalDamage"
DamageRules = ...existing...;Combatant::OnHit@hit.finalDamage

# Training gain is a calculated value:  Single TrainingSystem::CalculateGain(Gladiator g, StatType stat)
TrainingRules = ...existing...;TrainingSystem::CalculateGain@return

# Money is a property, and the game just does  Denarii += reward  /  Denarii -= price
# (an increase gets MoneyMultiplier, a decrease gets CostMultiplier)
MoneyRules = ...existing...;PlayerData::set_Denarii@delta

# Hit chance is computed by a calculator:  Single CombatMath::HitChance(Fighter attacker, Fighter defender)
CombatChanceRules = ...existing...;CombatMath::HitChance@return!attacker

# Recovery time is a property on the injury:  Int32 Injury::get_DaysToHeal()
DurationRules = ...existing...;Injury::get_DaysToHeal@return
```

### Rule syntax

`TypeGlob::MethodGlob[@target][!subject]`

- `TypeGlob` / `MethodGlob`: names with `*` and `?` wildcards, not case-sensitive. The type part can be the short name (`Ludus`) or the full name (`Game.Economy.Ludus`). Leaving out `Type::` means any type.
- `@target` — what gets scaled:
  - *(nothing)* / `@first`: the first numeric parameter that looks like an amount. Parameters named like `index`, `slot`, `type`, or `level` are skipped.
  - `@all`: every numeric parameter.
  - `@amount` or `@1`: the parameter with that name or position (0-based).
  - `@hit.finalDamage`: a field or property inside a parameter (works for classes and structs, and dotted paths can go deeper).
  - `@return`: the method's return value (good for `Calculate…`/`Get…Gain` methods).
  - `@delta`: for a property setter `set_X`, scales only the increase (new − current).
    ⚠ If the game also uses that setter when loading a save, loading would multiply your money. Prefer a real "add" method when one exists.
- `!subject`: the parameter holding the gladiator or ludus the value belongs to. For damage rules, this is whoever **takes** the
  damage, e.g. `Combat::Resolve!defender`. By default it's the object the method is called on, or the first object parameter for static methods.

`ExcludeRules` uses the same syntax and keeps UI/preview methods from being scaled. If something UI-related got hooked, add it there.

### Telling yours from theirs

For every hooked call, the mod looks at the subject (gladiator, ludus, …) and checks `PlayerSideMembers` in order:

- `isPlayer` → the member is `true` → it's yours.
- `team=Player` → the member's value (enum name or number) is `Player` → it's yours; any other value → enemy.
- `isAI=false` → the member is `false` → it's yours (so `true` means a rival).
- Dotted paths work: `owner.isPlayer`, `gladiator.ludus.isPlayer`.

The first member that exists with a simple value decides. If none exist:

- **Damage, defense, max health, combat chances** (`UnknownSide`, default `Skip`): the value is left alone, because boosting an unknown side could boost enemies.
  The mod logs a warning listing that type's bool/enum members and their current values. Add the right one, for example
  `PlayerSideMembers = isPlayerTeam;...` or `PlayerSideMembers = side=Left;...`.
- **Everything else** (`UnknownSideOther`, default `TreatAsPlayer`): the multiplier is applied. Values like ludus money, training,
  or build times are almost always yours. Anything positively identified as a rival's is still skipped.

## Notes and limits

- **Double scaling is prevented.** If one hooked method calls another (`AddMoney` → `AddGold`, `SpendGold` → `AddGold(-x)`,
  or an override calling `base.TakeDamage`), the value is scaled once.
- **Some multipliers stack on purpose.** A sale whose price comes from `GetSellPrice` (×1.5) and is then paid through `AddGold` (×2) gives ×3.
- **Hooking property getters with `@return` can compound.** For example, if the game levels up with `MaxHealth = MaxHealth + 10` and you
  hook `get_MaxHealth`, the boosted value is saved and grows every time. The defaults only hook `Get…()`/`Calculate…()` methods for this reason.
  Hook a property getter only when it's never written back.
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
| `ModConfig.cs` (categories) | Every multiplier: default value, default rules, side handling |
| `Hooks.cs` | The Harmony prefix/postfix/finalizer that does the scaling |
| `SideResolver.cs` | Decides whether a gladiator/ludus is yours |
| `Dumper.cs` | Writes the dump file |
