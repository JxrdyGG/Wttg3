using System.Collections.Generic;
using BepInEx.Configuration;

namespace DominusMultipliers
{
    /// <summary>Order matters: when a method matches rules of several categories, the first category in this list wins.</summary>
    internal enum Category
    {
        Damage, MaxHealth, CombatChance, Healing, StaminaCost, Stamina, Fatigue, Injury,
        Training, Fame, Morale, Duration, SellPrice, Cost, Money
    }

    /// <summary>How a category decides whose value it is looking at.</summary>
    internal enum SideMode
    {
        /// <summary>Damage: enemy receivers get DamageMultiplier, your receivers get 1/DefenseMultiplier.</summary>
        Combat,
        /// <summary>Only your side is boosted; if the side is unknown, <see cref="ModConfig.UnknownSide"/> decides (default: skip).</summary>
        PlayerStrict,
        /// <summary>Only your side is boosted; if the side is unknown, <see cref="ModConfig.UnknownSideOther"/> decides (default: apply).</summary>
        PlayerSupport,
    }

    public enum UnknownSideMode { Skip, TreatAsEnemy, TreatAsPlayer }

    internal sealed class CategoryDef
    {
        public Category Id;
        public SideMode Side;
        public int Group;                       // categories in the same group never scale the same value twice
        public ConfigEntry<float> Multiplier;
        public ConfigEntry<string> Rules;
        public ConfigEntry<bool> OnlyGains;     // null = only positive values are ever scaled
        public string BuiltInRules = "";        // Dominus Gladiatus-specific rules (see ApplyGameRules)
        public CategoryDef Via;                 // this multiplier is applied through another category's hooks (e.g. Cost via Money)
        public string Name => Id.ToString();
    }

    internal sealed class ModConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<float> DefenseMultiplier;
        public readonly ConfigEntry<string> ExcludeRules;
        public readonly ConfigEntry<string> PlayerSideMembers;
        public readonly ConfigEntry<UnknownSideMode> UnknownSide;
        public readonly ConfigEntry<UnknownSideMode> UnknownSideOther;
        public readonly ConfigEntry<string> Assemblies;
        public readonly ConfigEntry<bool> WriteDump;
        public readonly ConfigEntry<bool> LogScaledCalls;
        public readonly ConfigEntry<string> MenuKey;
        public readonly ConfigEntry<float> MenuScale;
        public readonly ConfigEntry<bool> ShowStartupHint;
        public readonly ConfigEntry<bool> UseBuiltInGameRules;
        public readonly ConfigEntry<string> PlayerTeam;
        public readonly ConfigEntry<string> DumpTypeDetails;

        public readonly List<CategoryDef> Categories = new List<CategoryDef>();
        private readonly CategoryDef[] _byId = new CategoryDef[System.Enum.GetValues(typeof(Category)).Length];
        public CategoryDef this[Category c] => _byId[(int)c];
        public int GroupCount;

        private const string MenuSection = "0. In-game menu";
        private const string Economy = "1. Economy";
        private const string Combat = "2. Combat";
        private const string Care = "3. Gladiator care";
        private const string Progress = "4. Progression";
        private const string RulesSection = "5. Hook rules (restart required)";
        private const string Diag = "6. Diagnostics";

        private const string RuleHelp =
            "Semicolon-separated rules: TypeGlob::MethodGlob[@target][!subject]. Globs use * and ?, case-insensitive; the type part " +
            "matches the short or full type name. @target: first (default: first numeric parameter that looks like an amount), all, " +
            "return, delta (property setter: scales new-minus-current), a parameter name or index, optionally followed by " +
            ".field.path (e.g. @info.amount). !subject: the parameter holding the gladiator/ludus the value belongs to. See README.md.";

        public ModConfig(ConfigFile cfg)
        {
            MenuKey = cfg.Bind(MenuSection, "MenuKey", "F8",
                "Key that opens/closes the in-game menu (a Unity KeyCode name: F1-F12, Insert, Home, BackQuote, Keypad0, ...).");
            MenuScale = cfg.Bind(MenuSection, "MenuScale", 0f,
                new ConfigDescription("Menu size. 0 = automatic (bigger on 1440p/4K screens), otherwise e.g. 1.5.",
                    new AcceptableValueRange<float>(0f, 4f)));
            ShowStartupHint = cfg.Bind(MenuSection, "ShowStartupHint", true,
                "Show a short 'press F8 for the menu' note in the corner after the game starts.");

            Enabled = cfg.Bind(Economy, "Enabled", true, "Master switch. When false every hook passes values through unchanged.");

            // ---------------- Economy ----------------
            Add(cfg, Category.Money, Economy, "MoneyMultiplier", 2f, SideMode.PlayerSupport, 1,
                "Multiplies money (denarii/gold) you GAIN: fight purses, sales, bets, rewards.",
                "*::AddGold;*::AddMoney;*::AddDenarii;*::AddDenarius;*::AddDenari;*::AddCoins;*::AddCoin;*::AddCurrency;*::AddCash;" +
                "*::GainGold;*::GainMoney;*::EarnGold;*::EarnMoney;*::ReceiveGold;*::ReceiveMoney;*::GiveGold;*::GiveMoney;" +
                "*::IncreaseGold;*::IncreaseMoney;*::ChangeGold;*::ChangeMoney;*::ModifyGold;*::ModifyMoney;" +
                "*Gold*::Add;*Money*::Add;*Wallet*::Add;*Currency*::Add;*Treasury*::Add;*Denari*::Add");
            Add(cfg, Category.Cost, Economy, "CostMultiplier", 0.5f, SideMode.PlayerSupport, 1,
                "Multiplies money you SPEND: buying gladiators/gear, upkeep, wages, construction (0.5 = half price). " +
                "Also applies to negative amounts passed to the money-gain methods.",
                "*::SpendGold;*::SpendMoney;*::SpendDenarii;*::SpendCoins;*::SpendCurrency;*::TrySpendGold;*::TrySpendMoney;" +
                "*::TrySpendDenarii;*::RemoveGold;*::RemoveMoney;*::RemoveDenarii;*::RemoveCoins;*::DeductGold;*::DeductMoney;" +
                "*::SubtractGold;*::SubtractMoney;*::PayGold;*::PayMoney;*::TakeGold;*::TakeMoney;*::ChargeGold;*::ChargeMoney;" +
                "*::UseGold;*::UseMoney;*::DecreaseGold;*::DecreaseMoney;*::PayUpkeep;*::PayWages;*::PaySalary;" +
                "*Wallet*::Remove;*Wallet*::Spend;*Wallet*::TrySpend;*Currency*::Remove;*Currency*::Spend;*Currency*::TrySpend;" +
                "*Gold*::Remove;*Gold*::Spend;*Gold*::TrySpend;*Money*::Remove;*Money*::Spend;*Money*::TrySpend;*Treasury*::Spend;*Treasury*::Remove");
            Add(cfg, Category.SellPrice, Economy, "SellPriceMultiplier", 1.5f, SideMode.PlayerSupport, 2,
                "Multiplies the sale value of gladiators/items you sell. In Dominus Gladiatus sales are already boosted by MoneyMultiplier, so this isn't needed.",
                "*::GetSellPrice@return;*::GetSellValue@return;*::GetSalePrice@return;*::GetSellingPrice@return;*::CalculateSellPrice@return;" +
                "*::CalculateSellValue@return;*::GetResalePrice@return;*::GetResaleValue@return;*::GetSellCost@return");

            // ---------------- Combat ----------------
            Add(cfg, Category.Damage, Combat, "DamageMultiplier", 1.5f, SideMode.Combat, 3,
                "Multiplies damage taken by ENEMY combatants (i.e. damage your gladiators deal).",
                "*::TakeDamage;*::ReceiveDamage;*::ApplyDamage;*::GetHit;*::Hurt;*::SufferDamage;*::DealDamageToSelf;" +
                "*::OnTakeDamage;*::OnReceiveDamage;*::TakeHit;*::ReceiveHit;*Health*::Damage;*Health*::Reduce;*Health*::Decrease",
                ruleExtra: "  Damage rules are receiver-side: the subject is the object TAKING the damage (the instance by default).");
            DefenseMultiplier = cfg.Bind(Combat, "DefenseMultiplier", 1.5f,
                new ConfigDescription("Damage taken by YOUR gladiators is divided by this (2 = half damage).",
                    new AcceptableValueRange<float>(0.01f, 1000f)));
            Add(cfg, Category.MaxHealth, Combat, "MaxHealthMultiplier", 1.5f, SideMode.PlayerStrict, 4,
                "Multiplies the maximum health of YOUR gladiators. Not available in Dominus Gladiatus yet; DefenseMultiplier has the same effect (x2 defense = twice the effective health).",
                "*::GetMaxHealth@return;*::GetMaxHP@return;*::GetMaxHitPoints@return;*::GetMaximumHealth@return;" +
                "*::CalculateMaxHealth@return;*::CalculateMaxHP@return;*::CalcMaxHealth@return;*::ComputeMaxHealth@return");
            Add(cfg, Category.CombatChance, Combat, "CombatChanceMultiplier", 1.25f, SideMode.PlayerStrict, 5,
                "Multiplies YOUR gladiators' hit, critical, dodge, block and parry chances (and critical damage multiplier).",
                "*::GetCritChance@return;*::GetCriticalChance@return;*::CalculateCritChance@return;*::CalculateCriticalChance@return;" +
                "*::GetCritMultiplier@return;*::GetCriticalMultiplier@return;*::GetCritDamage@return;*::GetHitChance@return;" +
                "*::CalculateHitChance@return;*::GetAccuracy@return;*::GetDodgeChance@return;*::CalculateDodgeChance@return;" +
                "*::GetEvadeChance@return;*::GetEvasion@return;*::GetBlockChance@return;*::CalculateBlockChance@return;" +
                "*::GetParryChance@return;*::CalculateParryChance@return",
                ruleExtra: "  The subject is the instance by default; for calculator methods like GetHitChance(attacker, defender) add !attacker.");

            // ---------------- Gladiator care ----------------
            Add(cfg, Category.Healing, Care, "HealingMultiplier", 2f, SideMode.PlayerSupport, 6,
                "Multiplies healing / health regeneration of your gladiators.",
                "*::Heal;*::HealDamage;*::AddHealth;*::RestoreHealth;*::RecoverHealth;*::GainHealth;*::IncreaseHealth;*::Regenerate;" +
                "*::RegenHealth;*::RegenerateHealth;*::AddHP;*::RestoreHP;*::RecoverHP;*::Recover;" +
                "*Health*::Add;*Health*::Restore;*Health*::Increase;*Health*::Heal");
            Add(cfg, Category.Stamina, Care, "StaminaRegenMultiplier", 2f, SideMode.PlayerSupport, 7,
                "Multiplies stamina/energy your gladiators regain. In Dominus Gladiatus: how fast fatigue goes away.",
                "*::AddStamina;*::RestoreStamina;*::RecoverStamina;*::GainStamina;*::RegenStamina;*::RegenerateStamina;*::IncreaseStamina;" +
                "*::AddEnergy;*::RestoreEnergy;*::RecoverEnergy;*::GainEnergy;*::RegenEnergy;" +
                "*Stamina*::Add;*Stamina*::Restore;*Stamina*::Recover;*Stamina*::Regen*;*Energy*::Add;*Energy*::Restore;*Energy*::Recover");
            Add(cfg, Category.StaminaCost, Care, "StaminaCostMultiplier", 0.5f, SideMode.PlayerSupport, 7,
                "Multiplies stamina/energy your gladiators use (0.5 = half cost).",
                "*::UseStamina;*::ConsumeStamina;*::SpendStamina;*::ReduceStamina;*::DrainStamina;*::RemoveStamina;*::DecreaseStamina;" +
                "*::LoseStamina;*::UseEnergy;*::ConsumeEnergy;*::SpendEnergy;*::ReduceEnergy;*::DrainEnergy;*::DecreaseEnergy;" +
                "*Stamina*::Use;*Stamina*::Consume;*Stamina*::Spend;*Stamina*::Reduce;*Stamina*::Remove;*Stamina*::Decrease;*Stamina*::Drain;" +
                "*Energy*::Use;*Energy*::Consume;*Energy*::Spend;*Energy*::Reduce;*Energy*::Remove");
            Add(cfg, Category.Fatigue, Care, "FatigueMultiplier", 0.5f, SideMode.PlayerSupport, 8,
                "Multiplies fatigue / exhaustion / stress your gladiators build up (0.5 = half).",
                "*::AddFatigue;*::IncreaseFatigue;*::GainFatigue;*::ApplyFatigue;*::AddExhaustion;*::IncreaseExhaustion;*::AddTiredness;" +
                "*::IncreaseTiredness;*::AddStress;*::IncreaseStress;*Fatigue*::Add;*Fatigue*::Increase;*Exhaustion*::Add");
            Add(cfg, Category.Injury, Care, "InjuryChanceMultiplier", 0.5f, SideMode.PlayerSupport, 9,
                "Multiplies the chance (and severity/duration) of injuries and death for your gladiators (0.5 = half).",
                "*::GetInjuryChance@return;*::CalculateInjuryChance@return;*::CalcInjuryChance@return;*::GetInjuryProbability@return;" +
                "*::GetDeathChance@return;*::CalculateDeathChance@return;*::CalcDeathChance@return;*::GetDeathProbability@return;" +
                "*::GetMaimChance@return;*::GetWoundChance@return;*::CalculateWoundChance@return;*::GetInjurySeverity@return;" +
                "*::GetInjuryDuration@return;*::CalculateInjurySeverity@return;*::CalculateInjuryDuration@return");

            // ---------------- Progression ----------------
            Add(cfg, Category.Training, Progress, "TrainingMultiplier", 2f, SideMode.PlayerSupport, 10,
                "Multiplies training / experience / stat progress gains.",
                "*::AddExperience;*::AddExp;*::AddXP;*::GainExperience;*::GainExp;*::GainXP;*::GiveExperience;*::GiveExp;*::GiveXP;" +
                "*::AddTrainingProgress;*::AddTrainingPoints;*::AddTrainingXP;*::AddTrainingExp;*::AddSkillProgress;" +
                "*::AddSkillExp;*::AddSkillXP;*::AddSkillPoints;*::AddStatProgress;*::AddStatExp;*::AddStatXP;*::AddAttributePoints;" +
                "*::IncreaseStat;*::ImproveStat;*::TrainStat;*::IncreaseSkill;*::ImproveSkill;*::IncreaseAttribute;*::ImproveAttribute;" +
                "*Train*::AddExp*;*Train*::AddXP*;*Train*::AddProgress*;*Train*::AddPoints*;*Train*::Gain*",
                onlyGainsKey: "TrainingOnlyGains");
            Add(cfg, Category.Fame, Progress, "FameMultiplier", 2f, SideMode.PlayerSupport, 11,
                "Multiplies fame, reputation, popularity, prestige, glory and crowd favor you gain.",
                "*::AddFame;*::GainFame;*::IncreaseFame;*::AddReputation;*::GainReputation;*::IncreaseReputation;*::AddPopularity;" +
                "*::GainPopularity;*::IncreasePopularity;*::AddRenown;*::GainRenown;*::AddPrestige;*::GainPrestige;*::AddInfluence;" +
                "*::GainInfluence;*::AddCrowdFavor;*::AddCrowdFavour;*::IncreaseCrowdFavor;*::IncreaseCrowdFavour;*::GainCrowdFavor;" +
                "*::AddFavor;*::AddFavour;*::AddHype;*::AddGlory;*::GainGlory;*::AddHonor;*::AddHonour;*::AddAppeal;*::AddSpectacle;" +
                "*Fame*::Add;*Reputation*::Add;*Popularity*::Add;*Favor*::Add;*Favour*::Add;*Prestige*::Add");
            Add(cfg, Category.Morale, Progress, "MoraleMultiplier", 2f, SideMode.PlayerSupport, 12,
                "Multiplies morale, loyalty, happiness and motivation your gladiators gain. Built-in game rule: morale losses are divided by it.",
                "*::AddMorale;*::IncreaseMorale;*::GainMorale;*::AddLoyalty;*::IncreaseLoyalty;*::GainLoyalty;*::AddHappiness;" +
                "*::IncreaseHappiness;*::AddMotivation;*::IncreaseMotivation;*::AddMood;*::IncreaseMood;*::AddSatisfaction;" +
                "*::AddObedience;*::IncreaseObedience;*::AddDiscipline;*::AddConfidence;*::IncreaseConfidence;*::AddTrust;" +
                "*Morale*::Add;*Loyalty*::Add;*Happiness*::Add");
            Add(cfg, Category.Duration, Progress, "DurationMultiplier", 0.5f, SideMode.PlayerSupport, 13,
                "Multiplies how long training, injury recovery, construction, upgrades and crafting take (0.5 = twice as fast).",
                "*::GetTrainingDuration@return;*::GetTrainingTime@return;*::GetTrainingDays@return;*::GetRecoveryTime@return;" +
                "*::GetRecoveryDays@return;*::GetRecoveryDuration@return;*::GetHealingTime@return;*::GetHealingDays@return;" +
                "*::GetHealingDuration@return;*::GetHealTime@return;*::GetBuildTime@return;*::GetBuildDuration@return;" +
                "*::GetBuildDays@return;*::GetConstructionTime@return;*::GetConstructionDays@return;*::GetConstructionDuration@return;" +
                "*::GetUpgradeTime@return;*::GetUpgradeDuration@return;*::GetUpgradeDays@return;*::GetResearchTime@return;" +
                "*::GetCraftTime@return;*::GetCraftingTime@return;*::GetCraftDuration@return");

            // ---------------- Shared rule settings ----------------
            ExcludeRules = cfg.Bind(RulesSection, "ExcludeRules",
                "UI*::*;*UI::*;*View::*;*Panel::*;*Window::*;*Popup::*;*Tooltip::*;*Display::*;*Text::*;*Label::*;" +
                "*Tutorial*::*;*Debug*::*;*Cheat*::*;*Achievement*::*;*Stats*Tracker*::*;*::*Preview*;*::*Simulate*;*::*Estimate*",
                "Methods matching any of these are never hooked (same TypeGlob::MethodGlob syntax). Keeps UI/preview code from being scaled.");
            PlayerSideMembers = cfg.Bind(RulesSection, "PlayerSideMembers",
                "isPlayer;isPlayerUnit;isPlayerGladiator;isPlayerTeam;isPlayerOwned;isOwnedByPlayer;isPlayerSide;isPlayerControlled;" +
                "isPlayerLudus;belongsToPlayer;isAlly;isFriendly;isMine;isAI=false;isEnemy=false;isRival=false;team=Player;team=Ally;" +
                "team=Allies;side=Player;side=Ally;faction=Player;owner=Player;teamType=Player;combatTeam=Player;" +
                "owner.isPlayer;ludus.isPlayer;ludus.isPlayerLudus;gladiator.isPlayer;fighter.isPlayer;unit.isPlayer",
                "How to tell whether a gladiator/ludus is yours. Semicolon-separated member paths on the subject (fields, properties or " +
                "parameterless methods, case-insensitive, dotted paths allowed). A bare name means 'is true'; name=Value compares the " +
                "member's text (enum name, number, true/false). The first member that exists with a simple value decides.");
            UnknownSide = cfg.Bind(RulesSection, "UnknownSide", UnknownSideMode.Skip,
                "Damage, Defense, MaxHealth and CombatChance: what to do when the side can't be determined. Skip = leave the value unchanged " +
                "(boosting an unknown side might boost enemies).");
            UnknownSideOther = cfg.Bind(RulesSection, "UnknownSideOther", UnknownSideMode.TreatAsPlayer,
                "All other categories: what to do when the side can't be determined. Most of these values (ludus money, training, " +
                "durations) belong to you, so the default applies the multiplier. Values identified as a rival's are never boosted.");
            Assemblies = cfg.Bind(RulesSection, "Assemblies", "auto",
                "Game assemblies to scan. 'auto' = every non-engine/non-library DLL in the game's Managed (Mono) or interop (IL2CPP) folder. " +
                "Or a semicolon-separated list of assembly names, e.g. Assembly-CSharp.");

            WriteDump = cfg.Bind(Diag, "WriteDump", true,
                "Write BepInEx/DominusMultipliers-dump.txt at startup: what got hooked plus candidate methods/members to use in rules.");
            UseBuiltInGameRules = cfg.Bind(RulesSection, "UseBuiltInGameRules", true,
                "Use the rules built into the mod for Dominus Gladiatus (PlayerProfile.ModifyCoins, Health.Reduce, HelperGladiator.ModifyMorale, ...) " +
                "in addition to the *Rules lists above. They are listed in the dump file under HOOKED METHODS.");
            PlayerTeam = cfg.Bind(RulesSection, "PlayerTeam", "TeamA",
                "Which arena team is yours (the game's Team component: TeamA, TeamB, ...). If your gladiators take MORE damage instead of less, " +
                "switch this to TeamB (also on the menu's Combat tab).");
            DumpTypeDetails = cfg.Bind(Diag, "DumpTypeDetails",
                "Health;Creature;Team;Damage;Healing;GladiatorTemplateData;GladiatorStatsData;PlayerProfile;HelperGladiator;ArenaCrowndManager;" +
                "LudusRoomBase;TrainGladiatorsSystem;BasePower;Shield_GladiatorItem;GladiatorArmor;DamageSourceEventArgs",
                "Types whose full member list (all fields, properties and methods) is written to the dump, to help add rules.");
            LogScaledCalls = cfg.Bind(Diag, "LogScaledCalls", false,
                "Log the first few scaled calls of each hooked method (old -> new value) to the BepInEx console/LogOutput.log.");

            ApplyGameRules();
        }

        /// <summary>
        /// Rules for Dominus Gladiatus itself, taken from the game's code (a dump of the Steam release). They are kept out of
        /// the editable *Rules settings so that existing config files pick them up, and can be switched off with UseBuiltInGameRules.
        /// </summary>
        private void ApplyGameRules()
        {
            // All coin changes go through PlayerProfile.ModifyCoins(delta): gains get MoneyMultiplier, spending (negative) CostMultiplier.
            this[Category.Money].BuiltInRules = "PlayerProfile::ModifyCoins";
            this[Category.Cost].Via = this[Category.Money];
            // Arena combat: Health is the component that takes damage / healing; the side comes from the Team component (PlayerTeam).
            this[Category.Damage].BuiltInRules = "Health::Reduce";
            this[Category.Healing].BuiltInRules = "Health::Increase;HelperGladiator::ModifyCurrentHealth";
            // Ludus: fatigue gains get FatigueMultiplier, fatigue recovery (negative) gets StaminaRegenMultiplier (the game has no stamina).
            this[Category.Fatigue].BuiltInRules = "HelperGladiator::ModifyFatiguePoints";
            this[Category.Stamina].Via = this[Category.Fatigue];
            this[Category.Injury].BuiltInRules =
                "HelperGladiator::AddPendingInjury@levelsToAdd;HelperGladiator::GetSuicideChanceFromInjuries@return;" +
                "HelperGladiator::GetSuicideChanceFromMorale@return";
            this[Category.Training].BuiltInRules =
                "HelperGladiator::CalculateTrainingValue@return;UISelectStartingConditions::GetStartingTrainingDays@return";
            // Morale gains get MoraleMultiplier, morale losses are divided by it.
            this[Category.Morale].BuiltInRules = "HelperGladiator::ModifyMorale";
            // Days spent in the infirmary/quarters/temple/pleasure house, and employee ability cooldowns.
            this[Category.Duration].BuiltInRules =
                "LudusRoom*::CalculateStayDurationInDaysForValidGladiator@return;EmployeeAbility::CreateAbilityChargeCooldown@cooldownInDays";
        }

        private void Add(ConfigFile cfg, Category id, string section, string key, float def, SideMode side, int group,
                         string description, string rules, string ruleExtra = "", string onlyGainsKey = null)
        {
            var c = new CategoryDef
            {
                Id = id, Side = side, Group = group,
                Multiplier = cfg.Bind(section, key, def,
                    new ConfigDescription(description + " 1 = off.", new AcceptableValueRange<float>(0f, 1000f))),
                Rules = cfg.Bind(RulesSection, id + "Rules", rules, RuleHelp + ruleExtra),
            };
            if (onlyGainsKey != null)
                c.OnlyGains = cfg.Bind(section, onlyGainsKey, true, "Only scale positive amounts. Losses pass through unchanged.");
            _byId[(int)id] = c;
            if (group + 1 > GroupCount) GroupCount = group + 1;

            // Keep Categories sorted by enum order so rule matching precedence follows the Category enum.
            int i = 0;
            while (i < Categories.Count && Categories[i].Id < id) i++;
            Categories.Insert(i, c);
        }
    }
}
