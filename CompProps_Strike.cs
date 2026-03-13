using RimWorld;
using System;
using Verse;

namespace WastelandLizard
{
    public class CompProps_Strike : CompProperties
    {
        //我应该补一个伤害类型的
        //先鸽着
        public float EnergyCost = 10000;
        //打击一次耗电
        public float HeatCost = 10000;
        //打击一次产热
        public int BeamRadius = 10;
        //打击的半径
        public int BeamDamage = 10000;
        //打击伤害
        public DamageDef damageDef = DamageDefOf.Flame;
        //伤害类型

        public CompProps_Strike(){this.compClass = typeof(CompStrike);}
    }
}
