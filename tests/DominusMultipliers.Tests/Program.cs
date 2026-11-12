using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using DominusMultipliers;
using FakeGame;
using DF = DominusFake;

internal static class Program
{
    private static int _failures;

    private static void Check(string what, object actual, object expected)
    {
        bool ok = Equals(actual, expected);
        if (!ok) _failures++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {what}: got {actual}, expected {expected}");
    }

    private static int Main()
    {
        var cfgPath = Path.Combine(Path.GetTempPath(), "dominus-multipliers-test.cfg");
        File.Delete(cfgPath);
        var config = new ConfigFile(cfgPath, true);
        var log = new ManualLogSource("Test");
        Logger.Sources.Add(log);
        Logger.Listeners.Add(new ConsoleListener());

        // Defaults plus the explicit rules a user would add after reading the dump.
        var cfg = new ModConfig(config);
        void Set(Category c, float v) => cfg[c].Multiplier.Value = v;
        Set(Category.Money, 3f); Set(Category.Damage, 2f); cfg.DefenseMultiplier.Value = 4f; Set(Category.Training, 5f);
        Set(Category.Cost, 0.5f); Set(Category.SellPrice, 1.5f); Set(Category.MaxHealth, 1.5f); Set(Category.CombatChance, 2f);
        Set(Category.Healing, 2f); Set(Category.Stamina, 3f); Set(Category.StaminaCost, 0.5f); Set(Category.Fatigue, 0.25f);
        Set(Category.Injury, 0.5f); Set(Category.Fame, 4f); Set(Category.Morale, 2f); Set(Category.Duration, 0.5f);
        cfg.WriteDump.Value = false;
        cfg[Category.Money].Rules.Value += ";Purse::set_Coins@delta";
        cfg[Category.Damage].Rules.Value += ";Unit::ReceiveHit@info.amount";
        cfg[Category.Training].Rules.Value += ";TrainingFormula::GetSessionGain@return";

        Mod.Init(config, log, new List<Assembly> { typeof(Ludus).Assembly });

        var ludus = new Ludus();
        ludus.AddGold(10);   Check("AddGold 10 x3", ludus.Gold, 30);
        ludus.AddMoney(10);  Check("AddMoney->AddGold scales once", ludus.Gold, 60);
        ludus.SpendGold(6);  Check("Ludus.SpendGold x0.5 cost", ludus.Gold, 57);
        ludus.AddGold(-10);  Check("negative AddGold = cost x0.5", ludus.Gold, 52);
        var ui = new GoldUI(); ui.AddGold(10); Check("GoldUI excluded", ui.Shown, 10);

        var purse = new Purse { Coins = 0 };  // first set: 0 -> 0, no gain
        purse.Coins += 10;   Check("set_Coins delta x3", purse.Coins, 30L);
        purse.Coins -= 10;   Check("set_Coins decrease = cost x0.5", purse.Coins, 25L);

        var mine = new Fighter { isPlayer = true }; mine.TakeDamage(20); Check("player takes 20/4", mine.hp, 95f);
        var foe = new Fighter { isPlayer = false }; foe.TakeDamage(20); Check("enemy takes 20*2", foe.hp, 60f);
        var heavy = new HeavyFighter { isPlayer = false }; heavy.TakeDamage(10); Check("override+base scales once", heavy.hp, 80f);
        var beast = new Beast(); beast.TakeDamage(10); Check("unknown side untouched", beast.hp, 90f);
        var foe2 = new Fighter(); Combat.ApplyDamage(foe2, 10); Check("static ApplyDamage receiver=target", foe2.hp, 80f);

        var u1 = new Unit { team = Side.Player }; u1.ReceiveHit(new DamageInfo { amount = 40 }); Check("struct path, team=Player", u1.hp, 90);
        var u2 = new Unit { team = Side.Enemy };  u2.ReceiveHit(new DamageInfo { amount = 40 }); Check("struct path, enemy", u2.hp, 20);
        var pawn = new Pawn(); pawn.owner.isPlayer = true; pawn.Hurt(8); Check("dotted side owner.isPlayer", pawn.hp, 98);

        var g = new Gladiator();
        g.AddExperience(7);   Check("AddExperience x5", g.Exp, 35);
        g.IncreaseStat(2, 3); Check("IncreaseStat scales amount", g.Stats[2], 15);
        Check("IncreaseStat leaves index alone", g.Stats[0], 0);
        Check("@return x5", TrainingFormula.GetSessionGain(), 50f);

        var shop = new Shop(); shop.Ludus.Gold = 100;
        shop.SpendGold(40);       Check("SpendGold->AddGold(-x) discounted once", shop.Ludus.Gold, 80);
        shop.BuyViaNegative(40);  Check("AddGold(-40) discounted", shop.Ludus.Gold, 60);
        Check("sell price x1.5", shop.GetSellPrice(), 150);

        var w = new Warrior { isPlayer = true };
        var e = new Warrior { isPlayer = false };
        Check("max health mine x1.5", w.GetMaxHealth(), 150f);
        Check("max health enemy unchanged", e.GetMaxHealth(), 100f);
        Check("crit chance mine x2", w.GetCritChance(), 0.4f);
        Check("crit chance enemy unchanged", e.GetCritChance(), 0.2f);
        w.Heal(10); e.Heal(10);   Check("heal mine x2", w.hp, 20f); Check("heal enemy unchanged", e.hp, 10f);
        w.AddStamina(10);         Check("stamina regen x3", w.stamina, 30f);
        w.UseStamina(10);         Check("stamina cost x0.5", w.stamina, 25f);
        w.AddFatigue(8);          Check("fatigue x0.25", w.fatigue, 2f);
        w.AddMorale(5);           Check("morale x2", w.morale, 10f);
        w.AddFame(5); e.AddFame(5); Check("fame x4", w.fame, 20f); Check("enemy fame unchanged", e.fame, 5f);
        Check("injury chance x0.5", w.GetInjuryChance(), 0.2f);
        Check("static recovery days(subject=mine) x0.5", Calendar.GetRecoveryDays(w), 5);
        Check("static recovery days(subject=enemy) unchanged", Calendar.GetRecoveryDays(e), 10);
        Check("static build time (no subject) x0.5", Calendar.GetBuildTime(), 3);
        var rival = new RivalLudus(); rival.AddGold(10); Check("rival (isAI=true) gold unchanged", rival.Gold, 10);

        cfg = Mod.Cfg;
        cfg[Category.Money].Multiplier.Value = 1f; ludus.Gold = 0; ludus.AddGold(10); Check("live config change to x1", ludus.Gold, 10);
        cfg.Enabled.Value = false; g.Exp = 0; g.AddExperience(7); Check("Enabled=false", g.Exp, 7);

        DominusTests(cfg);
        MenuTests(cfg);

        Dumper.Write(new List<Assembly> { typeof(Ludus).Assembly }, Path.GetTempPath());
        Console.WriteLine(_failures == 0 ? "\nALL PASSED" : $"\n{_failures} FAILED");
        return _failures == 0 ? 0 : 1;
    }

    private static void DominusTests(ModConfig cfg)
    {
        Console.WriteLine("-- Dominus Gladiatus built-in rules --");
        cfg.Enabled.Value = true;
        cfg[Category.Money].Multiplier.Value = 3f;
        DF.PlayerProfile.Coins = 0;
        DF.PlayerProfile.ModifyCoins(100, false); Check("ModifyCoins +100 x3", DF.PlayerProfile.Coins, 300);
        DF.PlayerProfile.ModifyCoins(-40, false); Check("ModifyCoins -40 x0.5 (cost)", DF.PlayerProfile.Coins, 280);

        var mine = DF.Health.OnTeam(DF.TeamId.TeamA);
        var foe = DF.Health.OnTeam(DF.TeamId.TeamB);
        var noTeam = DF.Health.OnTeam(null);
        mine.Reduce(20, null, null);   Check("TeamA (yours) takes 20/4", mine.Amount, 95f);
        foe.Reduce(20, null, null);    Check("TeamB (enemy) takes 20*2", foe.Amount, 60f);
        noTeam.Reduce(20, null, null); Check("no Team component: unchanged", noTeam.Amount, 80f);
        mine.Increase(10, null, true); Check("TeamA healing x2", mine.Amount, 115f);
        foe.Increase(10, null, true);  Check("TeamB healing unchanged", foe.Amount, 70f);

        var t = new DF.GladiatorTemplateData();
        DF.HelperGladiator.ModifyCurrentHealth(t, 5);  Check("ludus heal +5 x2", t.Health, 60);
        DF.HelperGladiator.ModifyCurrentHealth(t, -5); Check("ludus health loss unchanged", t.Health, 55);
        DF.HelperGladiator.ModifyFatiguePoints(t, 8);  Check("fatigue +8 x0.25", t.Fatigue, 22);
        DF.HelperGladiator.ModifyFatiguePoints(t, -4); Check("fatigue recovery -4 x3 (StaminaRegen)", t.Fatigue, 10);
        DF.HelperGladiator.ModifyMorale(t, 5);         Check("morale +5 x2", t.Morale, 20);
        DF.HelperGladiator.ModifyMorale(t, -6);        Check("morale -6 /2", t.Morale, 17);
        Check("suicide chance from injuries x0.5", DF.HelperGladiator.GetSuicideChanceFromInjuries(t), 0.1f);
        Check("training value x5", DF.HelperGladiator.CalculateTrainingValue(t), 15f);
        Check("starting training days x5 (UI type, built-in ignores excludes)", DF.UISelectStartingConditions.GetStartingTrainingDays(0, 0), 50);
        Check("room stay x0.5", new DF.LudusRoomBase().CalculateStayDurationInDaysForValidGladiator(t), 2f);
        Check("infirmary override+base scaled once", new DF.LudusRoom_Infirmary().CalculateStayDurationInDaysForValidGladiator(t), 3f);
        var ab = new DF.EmployeeAbility(); ab.CreateAbilityChargeCooldown(4); Check("ability cooldown x0.5", ab.Cooldown, 2);

        // 1 injury level x0.5 must come out as 0 or 1, about half the time each (plain rounding would always give 1).
        t.InjuryLevels = 0;
        for (int i = 0; i < 2000; i++) DF.HelperGladiator.AddPendingInjury(t, 1);
        Check("injury levels x0.5 on average (900-1100 of 2000)", t.InjuryLevels >= 900 && t.InjuryLevels <= 1100, true);

        // Switching the player team flips the sides.
        cfg.PlayerTeam.Value = "TeamB";
        var a = DF.Health.OnTeam(DF.TeamId.TeamA); a.Reduce(20, null, null);
        Check("after switching to TeamB, TeamA is the enemy", a.Amount, 60f);
        cfg.PlayerTeam.Value = "TeamA";
    }

    private static void MenuTests(ModConfig cfg)
    {
        Console.WriteLine("-- menu --");
        cfg.Enabled.Value = true;
        var gui = new FakeGui();
        var menu = new DominusMultipliers.Menu.ModMenu(gui, Mod.File);

        menu.OnGUI();
        Check("startup hint shown while closed", gui.Drawn.Exists(d => d.Contains("press F8")), true);

        gui.EventType = "KeyDown"; gui.KeyCode = "F8"; menu.OnGUI();
        Check("F8 opens menu", menu.IsOpen, true);
        Check("F8 event consumed", gui.Used, true);

        gui.EventType = "Repaint"; gui.KeyCode = "None"; menu.OnGUI();
        Check("economy tab shows Money row", gui.Drawn.Contains("Money gained"), true);
        Check("font size pushed/popped", gui.FontDepth, 0);

        gui.EventType = "MouseDown"; gui.ClickButton = "Strong"; menu.OnGUI();
        Check("Strong preset: money", cfg[Category.Money].Multiplier.Value, 5f);
        Check("Strong preset: defense", cfg.DefenseMultiplier.Value, 3f);
        Check("Strong preset: duration", cfg[Category.Duration].Multiplier.Value, 0.25f);

        gui.ClickButton = "+"; menu.OnGUI();          // first + is Money
        Check("+ button adds 0.25", cfg[Category.Money].Multiplier.Value, 5.25f);
        gui.SliderIndex = 1; gui.SliderValue = 0.37f; menu.OnGUI(); gui.SliderIndex = -1; // 2nd slider = Cost, snaps to 0.05
        Check("cost slider snaps to 0.35", cfg[Category.Cost].Multiplier.Value, 0.35f);

        gui.ClickButton = "Combat"; menu.OnGUI();
        gui.EventType = "Repaint"; menu.OnGUI();
        Check("combat tab shows Defense row", gui.Drawn.Contains("Defense (dmg taken /)"), true);
        gui.EventType = "MouseDown"; gui.ClickButton = "-"; menu.OnGUI();   // first - on Combat tab is Damage
        Check("- button lowers damage", cfg[Category.Damage].Multiplier.Value, 2.75f);

        gui.ClickButton = "Info"; menu.OnGUI();
        gui.EventType = "Repaint"; menu.OnGUI();
        Check("info tab lists hook counts", gui.Drawn.Exists(d => d.Contains("Money: 5")), true);

        gui.EventType = "MouseDown"; gui.ClickButton = "All 1x"; menu.OnGUI();
        Check("All 1x preset", cfg[Category.Training].Multiplier.Value, 1f);
        gui.ClickButton = "Defaults"; menu.OnGUI();
        Check("Defaults preset", cfg[Category.Money].Multiplier.Value, 2f);

        // Title-bar drag moves the window (window starts at 40,40; title bar is the top 28px).
        gui.EventType = "MouseDown"; gui.MouseX = 100; gui.MouseY = 50; menu.OnGUI();
        gui.EventType = "MouseDrag"; gui.DeltaX = 30; gui.DeltaY = 20; menu.OnGUI();
        gui.EventType = "MouseUp"; menu.OnGUI();
        gui.EventType = "Repaint"; gui.MouseX = 100 + 30; gui.MouseY = 50 + 20; menu.OnGUI();
        gui.EventType = "MouseDown"; gui.ClickButton = null; gui.MouseX = 75; gui.MouseY = 65; menu.OnGUI();
        Check("dragged window: new title bar position grabs", gui.Used, true);
        gui.EventType = "MouseUp"; menu.OnGUI();

        gui.EventType = "KeyDown"; gui.KeyCode = "F8"; menu.OnGUI();
        Check("F8 closes menu", menu.IsOpen, false);
        var saved = new ConfigFile(Mod.File.ConfigFilePath, false);
        Check("settings saved to file", saved.Bind("1. Economy", "MoneyMultiplier", 0f).Value, 2f);
    }

    private sealed class ConsoleListener : ILogListener
    {
        public void LogEvent(object sender, LogEventArgs e) { Console.WriteLine($"  [{e.Level}] {e.Data}"); }
        public LogLevel LogLevelFilter => LogLevel.All;
        public void Dispose() { }
    }
}
