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

namespace WastelandLizard
{
    [HarmonyPatch(typeof(CompShipHeatShield))]
    public static class CompShipHeatShield_FluxPatch
    {
        [HarmonyPatch("CompTick")]
        [HarmonyPostfix]
        public static void CompTick_Postfix(CompShipHeatShield __instance)
        {
            var fluxGen = __instance.parent.GetComp<CompShipHeatShield_FluxGenerator>();
            if (fluxGen == null)
                return;
            if (__instance.shutDown && fluxGen.CurrentFluxHeat > 0)
            {
                //后面改用CompProperties
                fluxGen.ReduceFluxHeat(0.1f);
            }
        }

        [HarmonyPatch(typeof(CompShipHeatShield), "HitShield")]
        public static bool Prefix(Projectile proj)
        {
            var ignoreComp = proj.TryGetComp<CompIgnoreShield>();
            if (ignoreComp != null)return false;
            return true;
        }

        [HarmonyPatch("HitShield")]
        [HarmonyPostfix]
        public static void HitShield_Postfix(CompShipHeatShield __instance, Projectile proj)
        {
            var fluxGen = __instance.parent.GetComp<CompShipHeatShield_FluxGenerator>();
            if (fluxGen == null)return;
            //后面改用CompProperties
            float damage = proj.DamageAmount;
            float fluxIncrease = damage * 0.3f;
            fluxGen.AddFluxHeat(fluxIncrease);
        }
    }
}