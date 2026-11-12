// Stand-ins with the same type/method signatures as Dominus Gladiatus (from the user's dump), to test the built-in rules.
using System;
using System.Collections.Generic;
using System.Linq;

namespace DominusFake
{
    public enum TeamId { None, TeamA, TeamB, TeamC, TeamD, Count }

    public class GameObjectLike { public List<object> Components = new List<object>(); }

    /// <summary>Mimics UnityEngine.Component.GetComponent(Type).</summary>
    public class FakeComponent
    {
        public GameObjectLike gameObject = new GameObjectLike();
        public object GetComponent(Type t) => gameObject.Components.FirstOrDefault(t.IsInstanceOfType);
    }

    public class Team : FakeComponent { public TeamId Id; }

    public class Health : FakeComponent
    {
        public float Amount = 100;
        public bool IsDead;
        public void Reduce(float amount, object whoChangedIt, object powerUsed) { Amount -= amount; }
        public void Increase(int amount, object whoChangedIt, bool raiseEvents) { Amount += amount; }

        public static Health OnTeam(TeamId? id)
        {
            var h = new Health();
            h.gameObject.Components.Add(h);
            if (id != null) h.gameObject.Components.Add(new Team { Id = id.Value, gameObject = h.gameObject });
            return h;
        }
    }

    public class GladiatorTemplateData { public int Health = 50, Fatigue = 20, Morale = 10, InjuryLevels; }

    public static class PlayerProfile
    {
        public static int Coins;
        public static void ModifyCoins(int delta, bool forceMute) { Coins += delta; }
    }

    public static class HelperGladiator
    {
        public static int ModifyCurrentHealth(GladiatorTemplateData template, int delta) => template.Health += delta;
        public static bool ModifyFatiguePoints(GladiatorTemplateData template, int delta) { template.Fatigue += delta; return true; }
        public static bool ModifyMorale(GladiatorTemplateData template, int delta) { template.Morale += delta; return true; }
        public static List<int> AddPendingInjury(GladiatorTemplateData template, int levelsToAdd) { template.InjuryLevels += levelsToAdd; return new List<int>(); }
        public static float GetSuicideChanceFromInjuries(GladiatorTemplateData template) => 0.2f;
        public static float CalculateTrainingValue(GladiatorTemplateData template) => 3f;
    }

    public class LudusRoomBase { public virtual float CalculateStayDurationInDaysForValidGladiator(GladiatorTemplateData template) => 4f; }
    public class LudusRoom_Infirmary : LudusRoomBase
    {
        public override float CalculateStayDurationInDaysForValidGladiator(GladiatorTemplateData template) =>
            base.CalculateStayDurationInDaysForValidGladiator(template) + 2f;
    }

    public class EmployeeAbility { public int Cooldown; public void CreateAbilityChargeCooldown(int cooldownInDays) { Cooldown = cooldownInDays; } }

    public class UISelectStartingConditions { public static int GetStartingTrainingDays(int quality, int difficultyDay) => 10; }
}
