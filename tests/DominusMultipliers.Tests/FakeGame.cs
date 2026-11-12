// A stand-in "game" whose method names mimic what a gladiator manager might use.
namespace FakeGame
{
    public enum Side { Player, Enemy }

    public class Ludus
    {
        public int Gold;
        public void AddGold(int amount) { Gold += amount; }
        public void AddMoney(int amount) => AddGold(amount);     // nested hooked call: must scale once
        public void SpendGold(int amount) { Gold -= amount; }    // Cost hook
    }

    public class GoldUI { public int Shown; public void AddGold(int amount) { Shown += amount; } } // excluded (UI)

    public class Purse { public long Coins { get; set; } }    // hooked via Purse::set_Coins@delta

    public class Fighter
    {
        public bool isPlayer;
        public float hp = 100;
        public virtual void TakeDamage(float damage) { hp -= damage; }
    }

    public class HeavyFighter : Fighter
    {
        public override void TakeDamage(float damage) { base.TakeDamage(damage); } // override + base: scale once
    }

    public class Beast { public float hp = 100; public void TakeDamage(float damage) { hp -= damage; } } // no side member

    public struct DamageInfo { public int amount; public bool crit; }

    public class Unit
    {
        public Side team;
        public int hp = 100;
        public void ReceiveHit(DamageInfo info) { hp -= info.amount; }  // hooked via Unit::ReceiveHit@info.amount
    }

    public class Owner { public bool isPlayer; }
    public class Pawn { public Owner owner = new Owner(); public int hp = 100; public void Hurt(int amount) { hp -= amount; } } // owner.isPlayer

    public static class Combat
    {
        public static void ApplyDamage(Fighter target, float amount) { target.hp -= amount; } // static: receiver = target
    }

    public class Gladiator
    {
        public int Exp;
        public int[] Stats = new int[5];
        public void AddExperience(int amount) { Exp += amount; }
        public void IncreaseStat(int statIndex, int amount) { Stats[statIndex] += amount; } // must scale amount, not index
    }

    public static class TrainingFormula { public static float GetSessionGain() => 10f; } // hooked via @return
}

namespace FakeGame
{
    public class Shop
    {
        public Ludus Ludus = new Ludus();
        public void SpendGold(int amount) => Ludus.AddGold(-amount);   // Cost hook; inner AddGold(-x) must not discount twice
        public void BuyViaNegative(int price) => Ludus.AddGold(-price); // AddGold(-x) on its own = cost
        public int GetSellPrice() => 100;
    }

    public class Warrior
    {
        public bool isPlayer;
        public float hp, stamina, fatigue, morale, fame;
        public float GetMaxHealth() => 100f;
        public float GetCritChance() => 0.2f;
        public void Heal(float amount) { hp += amount; }
        public void AddStamina(float amount) { stamina += amount; }
        public void UseStamina(float amount) { stamina -= amount; }
        public void AddFatigue(float amount) { fatigue += amount; }
        public void AddMorale(float amount) { morale += amount; }
        public void AddFame(float amount) { fame += amount; }
        public float GetInjuryChance() => 0.4f;
        public void AddExperience(int amount) { }
    }

    public class RivalLudus { public bool isAI = true; public int Gold; public void AddGold(int amount) { Gold += amount; } }

    public static class Calendar
    {
        public static int GetRecoveryDays(Warrior w) => 10;   // static with object param: subject = w
        public static int GetBuildTime() => 6;                 // static, no subject: UnknownSideOther applies
    }
}
