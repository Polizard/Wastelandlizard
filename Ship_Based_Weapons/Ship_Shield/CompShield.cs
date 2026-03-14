using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;
using SaveOurShip2;

namespace WastelandLizard
{
    public class CompProperties_ShipHeatShield_FluxGenerator : CompProperties
    {
        public float baseFluxHeat = 0f;
        public CompProperties_ShipHeatShield_FluxGenerator() { this.compClass = typeof(CompShipHeatShield_FluxGenerator); }
    }
    public class CompShipHeatShield_FluxGenerator : ThingComp
    {
        private CompProperties_ShipHeatShield_FluxGenerator Props => (CompProperties_ShipHeatShield_FluxGenerator)props;
        private CompShipHeatShield shieldComp;
        private CompShipHeat heatComp;

        private float currentFluxHeat = 0f;
        private int ticksUntilNextFlux = 0;

        public float CurrentFluxHeat => currentFluxHeat;
        public bool IsActive => currentFluxHeat > 0.01f;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            //shieldComp = parent.GetComp<CompShipHeatShield>();
            //heatComp = parent.GetComp<CompShipHeat>();
            //if (shieldComp == null)
            //    Log.Warning("[WL] CompShipHeatShield_FluxGenerator: " + parent + " 没有 CompShipHeatShield 组件！");
            //if (heatComp == null)
            //    Log.Warning("[WL] CompShipHeatShield_FluxGenerator: " + parent + " 没有 CompShipHeat 组件！");

            if (!respawningAfterLoad)
            {
                ticksUntilNextFlux = Rand.Range(0, 10);
                currentFluxHeat = Props.baseFluxHeat;
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (parent == null || !parent.Spawned || shieldComp?.shutDown != false)return;
            if (heatComp?.myNet == null)return;
            ticksUntilNextFlux--;
            if (ticksUntilNextFlux <= 0)
            {
                ticksUntilNextFlux = 10;
                if (currentFluxHeat > 0.01f && heatComp.myNet != null)
                {
                    float heatToAdd = currentFluxHeat;
                    if (!heatComp.AddHeatToNetwork(heatToAdd))
                    {
                        HandleNetworkFull(heatToAdd);
                    }
                }
            }
        }

        private void HandleNetworkFull(float attemptedHeat)
        {
            if (heatComp.myNet != null)
            {
                float remaining = heatComp.myNet.StorageCapacity - heatComp.myNet.StorageUsed;
                if (remaining > 0)
                {
                    heatComp.AddHeatToNetwork(remaining);
                }
            }
            if (shieldComp != null && Rand.Chance(0.1f))
            {
                shieldComp.shutDown = true;
                GenExplosion.DoExplosion(parent.Position, parent.Map, 1f, DamageDefOf.Flame, parent);
            }
        }

        public void AddFluxHeat(float amount)
        {
            float oldValue = currentFluxHeat;
            currentFluxHeat += amount;
            //if (Prefs.DevMode)
            //{
            //    Log.Message(string.Format("[SOS2] 护盾 {0} 产热量: {1:F1} -> {2:F1}",
            //        parent, oldValue, currentFluxHeat));
            //}
        }

        public void SetFluxHeat(float amount){currentFluxHeat = Mathf.Max(0, amount);}
        public void ReduceFluxHeat(float amount){currentFluxHeat = Mathf.Max(0, currentFluxHeat - amount);}
        public void ResetFluxHeat(){currentFluxHeat = 0f;}

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref currentFluxHeat, "currentFluxHeat", 0f);
            Scribe_Values.Look(ref ticksUntilNextFlux, "ticksUntilNextFlux", 0);
        }

        public override string CompInspectStringExtra()
        {
            if (currentFluxHeat > 0.01f){return "废热产生: " + currentFluxHeat.ToString("F1") + "/10tick";}
            return null;
        }
    }
}