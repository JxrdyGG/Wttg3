using BepInEx.Configuration;

namespace DominusMultipliers
{
    internal enum Category { Money = 0, Damage = 1, Training = 2 }

    public enum UnknownSideMode { Skip, TreatAsEnemy, TreatAsPlayer }

    internal sealed class ModConfig
    {
        // Multipliers (read live on every call, so editing them in-game via ConfigurationManager works).
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<float> MoneyMultiplier;
        public readonly ConfigEntry<float> DamageMultiplier;
        public readonly ConfigEntry<float> DefenseMultiplier;
        public readonly ConfigEntry<float> TrainingMultiplier;
        public readonly ConfigEntry<bool> MoneyOnlyGains;
        public readonly ConfigEntry<bool> TrainingOnlyGains;

        // Hook rules (read once at startup).
        public readonly ConfigEntry<string> MoneyRules;
        public readonly ConfigEntry<string> DamageRules;
        public readonly ConfigEntry<string> TrainingRules;
        public readonly ConfigEntry<string> ExcludeRules;
        public readonly ConfigEntry<string> PlayerSideMembers;
        public readonly ConfigEntry<UnknownSideMode> UnknownSide;
        public readonly ConfigEntry<string> Assemblies;

        // Diagnostics.
        public readonly ConfigEntry<bool> WriteDump;
        public readonly ConfigEntry<bool> LogScaledCalls;

        public ModConfig(ConfigFile cfg)
        {
            const string M = "1. Multipliers";
            Enabled = cfg.Bind(M, "Enabled", true, "Master switch. When false every hook passes values through unchanged.");
            MoneyMultiplier = cfg.Bind(M, "MoneyMultiplier", 2f,
                new ConfigDescription("Multiplies money (denarii/gold) you gain.", new AcceptableValueRange<float>(0f, 1000f)));
            DamageMultiplier = cfg.Bind(M, "DamageMultiplier", 1.5f,
                new ConfigDescription("Multiplies damage taken by ENEMY combatants (i.e. damage your gladiators deal).",
                    new AcceptableValueRange<float>(0f, 1000f)));
            DefenseMultiplier = cfg.Bind(M, "DefenseMultiplier", 1.5f,
                new ConfigDescription("Damage taken by YOUR gladiators is divided by this (2 = half damage).",
                    new AcceptableValueRange<float>(0.01f, 1000f)));
            TrainingMultiplier = cfg.Bind(M, "TrainingMultiplier", 2f,
                new ConfigDescription("Multiplies training / experience / stat progress gains.", new AcceptableValueRange<float>(0f, 1000f)));
            MoneyOnlyGains = cfg.Bind(M, "MoneyOnlyGains", true,
                "Only scale positive amounts (income). Spending/costs pass through unchanged.");
            TrainingOnlyGains = cfg.Bind(M, "TrainingOnlyGains", true,
                "Only scale positive amounts (gains). Stat loss passes through unchanged.");

            const string R = "2. Hook rules (restart required)";
            const string ruleHelp =
                "Semicolon-separated rules: TypeGlob::MethodGlob[@target][!side].  Globs use * and ?, case-insensitive; " +
                "the type part matches the short or full type name. @target picks what gets scaled: first (default, first numeric " +
                "parameter), all, return, delta (property setter: scales new-minus-current), a parameter name or index, optionally " +
                "followed by .field.path into that parameter (e.g. @info.amount). See README.md.";

            MoneyRules = cfg.Bind(R, "MoneyRules",
                "*::AddGold;*::AddMoney;*::AddDenarii;*::AddDenarius;*::AddDenari;*::AddCoins;*::AddCoin;*::AddCurrency;*::AddCash;" +
                "*::GainGold;*::GainMoney;*::EarnGold;*::EarnMoney;*::ReceiveGold;*::ReceiveMoney;*::GiveGold;*::GiveMoney;" +
                "*::IncreaseGold;*::IncreaseMoney;*::ChangeGold;*::ChangeMoney;*::ModifyGold;*::ModifyMoney;" +
                "*Gold*::Add;*Money*::Add;*Wallet*::Add;*Currency*::Add;*Treasury*::Add;*Denari*::Add",
                ruleHelp);
            DamageRules = cfg.Bind(R, "DamageRules",
                "*::TakeDamage;*::ReceiveDamage;*::ApplyDamage;*::GetHit;*::Hurt;*::SufferDamage;*::DealDamageToSelf;" +
                "*::OnTakeDamage;*::OnReceiveDamage;*::TakeHit;*::ReceiveHit;*Health*::Damage;*Health*::Reduce;*Health*::Decrease",
                ruleHelp + "  Damage rules are receiver-side: the mod checks whose side the object TAKING the damage is on " +
                "(the method's instance by default; use !paramName or !index to name the receiver parameter instead).");
            TrainingRules = cfg.Bind(R, "TrainingRules",
                "*::AddExperience;*::AddExp;*::AddXP;*::GainExperience;*::GainExp;*::GainXP;*::GiveExperience;*::GiveExp;*::GiveXP;" +
                "*::AddTrainingProgress;*::AddTrainingPoints;*::AddTrainingXP;*::AddTrainingExp;*::AddSkillProgress;" +
                "*::AddSkillExp;*::AddSkillXP;*::AddStatProgress;*::AddStatExp;*::AddStatXP;*::IncreaseStat;*::ImproveStat;" +
                "*::TrainStat;*::IncreaseSkill;*::ImproveSkill;*Train*::AddExp*;*Train*::AddXP*;*Train*::AddProgress*;*Train*::AddPoints*;*Train*::Gain*",
                ruleHelp);
            ExcludeRules = cfg.Bind(R, "ExcludeRules",
                "UI*::*;*UI::*;*View::*;*Panel::*;*Window::*;*Popup::*;*Tooltip::*;*Display::*;*Text::*;*Label::*;" +
                "*Tutorial*::*;*Debug*::*;*Cheat*::*;*Achievement*::*;*Stats*Tracker*::*;*::*Preview*;*::*Simulate*;*::*Estimate*",
                "Methods matching any of these are never patched (same TypeGlob::MethodGlob syntax). Keeps UI/preview code from being scaled.");
            PlayerSideMembers = cfg.Bind(R, "PlayerSideMembers",
                "isPlayer;isPlayerUnit;isPlayerGladiator;isPlayerTeam;isPlayerOwned;isOwnedByPlayer;isPlayerSide;isPlayerControlled;" +
                "belongsToPlayer;isAlly;isFriendly;isMine;team=Player;team=Ally;team=Allies;side=Player;side=Ally;faction=Player;" +
                "owner=Player;teamType=Player;combatTeam=Player",
                "How to tell if a damage receiver is yours. Semicolon-separated member paths on the receiver (fields, properties or " +
                "parameterless methods, case-insensitive, dotted paths allowed e.g. gladiator.isPlayer). A bare name means 'is true'; " +
                "name=Value compares the member's text (enum name or number). The first member that exists on the object decides.");
            UnknownSide = cfg.Bind(R, "UnknownSide", UnknownSideMode.Skip,
                "What to do with damage when none of PlayerSideMembers exists on the receiver. Skip = leave damage unchanged.");
            Assemblies = cfg.Bind(R, "Assemblies", "auto",
                "Game assemblies to scan. 'auto' = every non-engine/non-library DLL in the game's Managed (Mono) or interop (IL2CPP) folder. " +
                "Or a semicolon-separated list of assembly names, e.g. Assembly-CSharp.");

            const string D = "3. Diagnostics";
            WriteDump = cfg.Bind(D, "WriteDump", true,
                "Write BepInEx/DominusMultipliers-dump.txt at startup: what got patched plus candidate methods/members to use in rules.");
            LogScaledCalls = cfg.Bind(D, "LogScaledCalls", false,
                "Log the first few scaled calls of each hooked method (old -> new value) to the BepInEx console/LogOutput.log.");
        }
    }
}
