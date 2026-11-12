using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using DominusMultipliers;
using FakeGame;

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
        cfg.MoneyMultiplier.Value = 3f;
        cfg.DamageMultiplier.Value = 2f;
        cfg.DefenseMultiplier.Value = 4f;
        cfg.TrainingMultiplier.Value = 5f;
        cfg.WriteDump.Value = false;
        cfg.MoneyRules.Value += ";Purse::set_Coins@delta";
        cfg.DamageRules.Value += ";Unit::ReceiveHit@info.amount";
        cfg.TrainingRules.Value += ";TrainingFormula::GetSessionGain@return";
        cfg.PlayerSideMembers.Value += ";owner.isPlayer";

        Mod.Init(config, log, new List<Assembly> { typeof(Ludus).Assembly });

        var ludus = new Ludus();
        ludus.AddGold(10);   Check("AddGold 10 x3", ludus.Gold, 30);
        ludus.AddMoney(10);  Check("AddMoney->AddGold scales once", ludus.Gold, 60);
        ludus.SpendGold(5);  Check("SpendGold untouched", ludus.Gold, 55);
        ludus.AddGold(-5);   Check("negative AddGold untouched (MoneyOnlyGains)", ludus.Gold, 50);
        var ui = new GoldUI(); ui.AddGold(10); Check("GoldUI excluded", ui.Shown, 10);

        var purse = new Purse { Coins = 0 };  // first set: 0 -> 0, no gain
        purse.Coins += 10;   Check("set_Coins delta x3", purse.Coins, 30L);
        purse.Coins -= 5;    Check("set_Coins decrease untouched", purse.Coins, 25L);

        var mine = new Fighter { isPlayer = true }; mine.TakeDamage(20); Check("player takes 20/4", mine.hp, 95f);
        var foe = new Fighter { isPlayer = false }; foe.TakeDamage(20); Check("enemy takes 20*2", foe.hp, 60f);
        var heavy = new HeavyFighter { isPlayer = false }; heavy.TakeDamage(10); Check("override+base scales once", heavy.hp, 80f);
        var beast = new Beast(); beast.TakeDamage(10); Check("unknown side untouched", beast.hp, 90f);
        var foe2 = new Fighter(); Combat.ApplyDamage(foe2, 10); Check("static ApplyDamage receiver=target", foe2.hp, 80f);

        var u1 = new Unit { team = Team.Player }; u1.ReceiveHit(new DamageInfo { amount = 40 }); Check("struct path, team=Player", u1.hp, 90);
        var u2 = new Unit { team = Team.Enemy };  u2.ReceiveHit(new DamageInfo { amount = 40 }); Check("struct path, enemy", u2.hp, 20);
        var pawn = new Pawn(); pawn.owner.isPlayer = true; pawn.Hurt(8); Check("dotted side owner.isPlayer", pawn.hp, 98);

        var g = new Gladiator();
        g.AddExperience(7);   Check("AddExperience x5", g.Exp, 35);
        g.IncreaseStat(2, 3); Check("IncreaseStat scales amount", g.Stats[2], 15);
        Check("IncreaseStat leaves index alone", g.Stats[0], 0);
        Check("@return x5", TrainingFormula.GetSessionGain(), 50f);

        cfg = Mod.Cfg;
        cfg.MoneyMultiplier.Value = 1f; ludus.Gold = 0; ludus.AddGold(10); Check("live config change to x1", ludus.Gold, 10);
        cfg.Enabled.Value = false; g.Exp = 0; g.AddExperience(7); Check("Enabled=false", g.Exp, 7);

        Dumper.Write(new List<Assembly> { typeof(Ludus).Assembly }, Path.GetTempPath());
        Console.WriteLine(_failures == 0 ? "\nALL PASSED" : $"\n{_failures} FAILED");
        return _failures == 0 ? 0 : 1;
    }

    private sealed class ConsoleListener : ILogListener
    {
        public void LogEvent(object sender, LogEventArgs e) { Console.WriteLine($"  [{e.Level}] {e.Data}"); }
        public LogLevel LogLevelFilter => LogLevel.All;
        public void Dispose() { }
    }
}
