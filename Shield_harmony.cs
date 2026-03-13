using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;
using HarmonyLib;
using SaveOurShip2;

// 命名空间
namespace SaveOurShip2.HarmonyPatches
{
    // 修补类 - 必须是静态类
    [HarmonyPatch(typeof(CompShipHeatShield))]
    public static class CompShipHeatShield_FluxPatch
    {
        // 修补 CompTick 方法
        [HarmonyPatch("CompTick")]
        [HarmonyPostfix]
        public static void CompTick_Postfix(CompShipHeatShield __instance)
        {
            var fluxGen = __instance.parent.GetComp<CompShipHeatShield_FluxGenerator>();
            if (fluxGen == null)
                return;

            if (__instance.shutDown && fluxGen.CurrentFluxHeat > 0)
            {
                fluxGen.ReduceFluxHeat(0.1f);
            }
        }

        // 修补 HitShield 方法（可选）
        [HarmonyPatch("HitShield")]
        [HarmonyPostfix]
        public static void HitShield_Postfix(CompShipHeatShield __instance, Projectile proj)
        {
            var fluxGen = __instance.parent.GetComp<CompShipHeatShield_FluxGenerator>();
            if (fluxGen == null)
                return;

            float damage = proj.DamageAmount;
            float fluxIncrease = damage * 0.3f;
            fluxGen.AddFluxHeat(fluxIncrease);
        }
    }
}