using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;
using SaveOurShip2;

namespace SaveOurShip2
{
    /// <summary>
    /// 护盾动态产热组件 - 每10tick产生配置的废热
    /// </summary>
    public class CompShipHeatShield_FluxGenerator : ThingComp
    {
        // ===== 属性 =====
        private CompProperties_ShipHeatShield_FluxGenerator Props => (CompProperties_ShipHeatShield_FluxGenerator)props;

        // 引用的其他组件
        private CompShipHeatShield shieldComp;
        private CompShipHeat heatComp;

        // ===== 状态变量 =====
        private float currentFluxHeat = 0f;
        private int ticksUntilNextFlux = 0;

        // ===== 只读属性 =====
        public float CurrentFluxHeat => currentFluxHeat;
        public bool IsActive => currentFluxHeat > 0.01f;

        // ===== 初始化 =====
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            shieldComp = parent.GetComp<CompShipHeatShield>();
            heatComp = parent.GetComp<CompShipHeat>();

            if (shieldComp == null)
                Log.Warning("[SOS2] CompShipHeatShield_FluxGenerator: " + parent + " 没有 CompShipHeatShield 组件！");

            if (heatComp == null)
                Log.Warning("[SOS2] CompShipHeatShield_FluxGenerator: " + parent + " 没有 CompShipHeat 组件！");

            if (!respawningAfterLoad)
            {
                ticksUntilNextFlux = Rand.Range(0, 10);
                currentFluxHeat = Props.baseFluxHeat;
            }
        }

        // ===== 每tick更新 =====
        public override void CompTick()
        {
            base.CompTick();

            if (parent == null || !parent.Spawned || shieldComp?.shutDown != false)
                return;

            if (heatComp?.myNet == null)
                return;

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

        // ===== 处理网络已满 =====
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

        // ===== 公共方法 =====
        public void AddFluxHeat(float amount)
        {
            float oldValue = currentFluxHeat;
            currentFluxHeat += amount;
            if (Prefs.DevMode)
            {
                Log.Message(string.Format("[SOS2] 护盾 {0} 产热量: {1:F1} -> {2:F1}",
                    parent, oldValue, currentFluxHeat));
            }
        }

        public void SetFluxHeat(float amount){currentFluxHeat = Mathf.Max(0, amount);}
        public void ReduceFluxHeat(float amount){currentFluxHeat = Mathf.Max(0, currentFluxHeat - amount);}
        public void ResetFluxHeat(){currentFluxHeat = 0f;}

        // ===== 存档/读档 =====
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref currentFluxHeat, "currentFluxHeat", 0f);
            Scribe_Values.Look(ref ticksUntilNextFlux, "ticksUntilNextFlux", 0);
        }

        // ===== 鼠标提示 =====
        public override string CompInspectStringExtra()
        {
            if (currentFluxHeat > 0.01f)
            {
                // ✅ 修复：这里也是字符串拼接而不是插值
                return "废热产生: " + currentFluxHeat.ToString("F1") + "/10tick";
            }
            return null;
        }
    }

    /// <summary>
    /// 护盾动态产热组件的属性类
    /// </summary>
    public class CompProperties_ShipHeatShield_FluxGenerator : CompProperties
    {
        public float baseFluxHeat = 0f;

        public CompProperties_ShipHeatShield_FluxGenerator()
        {
            this.compClass = typeof(CompShipHeatShield_FluxGenerator);
        }
    }
}