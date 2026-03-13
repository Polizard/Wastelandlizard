using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace WastelandLizard
{
    public class CompProperties_ConsciousnessHost : CompProperties
    {
        public bool allowResurrection = true;
        public bool restoreMissingParts = true;
        public int sicknessDuration = 180000;
        public bool requirePower = false;
        public List<ThingCountClass> resurrectionCost;

        public CompProperties_ConsciousnessHost()
        {
            compClass = typeof(CompConsciousnessHost);
        }
    }

    public class HediffCompProperties_ConsciousnessBound : HediffCompProperties
    {
        public HediffCompProperties_ConsciousnessBound()
        {
            compClass = typeof(HediffComp_ConsciousnessBound);
        }
    }

    public class HediffComp_ConsciousnessBound : HediffComp
    {
        public CompConsciousnessHost hostComp;
        private int buildingIDToResolve = -1;

        private static HediffDef BoundDef => HediffDef.Named("ConsciousnessBound");

        public override void CompExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                int buildingID = hostComp?.parent.thingIDNumber ?? -1;
                Scribe_Values.Look(ref buildingID, "hostBuildingID", -1);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Scribe_Values.Look(ref buildingIDToResolve, "hostBuildingID", -1);
            }
        }

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            if (buildingIDToResolve != -1) ResolveHostReference();
        }

        private void ResolveHostReference()
        {
            Thing thing = Find.CurrentMap?.listerThings.AllThings
                .FirstOrDefault(t => t.thingIDNumber == buildingIDToResolve);

            if (thing is ThingWithComps twc)
                hostComp = twc.GetComp<CompConsciousnessHost>();

            buildingIDToResolve = -1;
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            if (Find.TickManager.TicksGame % 250 != 0) return;
            if (hostComp?.parent.Destroyed == true) hostComp = null;
        }
    }

    public class CompConsciousnessHost : ThingComp
    {
        private static HediffDef BoundDef => HediffDef.Named("ConsciousnessBound");

        public CompProperties_ConsciousnessHost Props => (CompProperties_ConsciousnessHost)props;
        public Pawn boundPawn;

        private bool PowerOk => !Props.requirePower || (parent.GetComp<CompPowerTrader>()?.PowerOn ?? true);
        private bool IsFunctional => parent.Spawned && !parent.Destroyed && PowerOk;
        private ThingOwner InnerContainer => (parent as IThingHolder)?.GetDirectlyHeldThings();
        private bool IsOccupied => InnerContainer?.Contains(boundPawn) ?? false;

        public override void PostExposeData()
        {
            Scribe_References.Look(ref boundPawn, "boundPawn");
        }

        public void BindPawn(Pawn pawn)
        {
            if (!IsFunctional)
            {
                Messages.Message("主机不可用", MessageTypeDefOf.RejectInput);
                return;
            }

            if (boundPawn != null)
            {
                Messages.Message("该主机已绑定其他殖民者", MessageTypeDefOf.RejectInput);
                return;
            }

            if (IsBoundElsewhere(pawn))
            {
                Messages.Message(pawn.Name.ToStringShort + "已绑定其他主机", MessageTypeDefOf.RejectInput);
                return;
            }

            boundPawn = pawn;
            AddBoundHediff(pawn);
            Messages.Message(pawn.Name.ToStringShort + "的意识已绑定至" + parent.Label, MessageTypeDefOf.PositiveEvent);
        }

        private void AddBoundHediff(Pawn pawn)
        {
            Hediff hediff = HediffMaker.MakeHediff(BoundDef, pawn);
            var comp = hediff.TryGetComp<HediffComp_ConsciousnessBound>();
            if (comp != null) comp.hostComp = this;
            pawn.health.AddHediff(hediff);
        }

        public void ReleasePawn()
        {
            if (IsOccupied) ForceEjectPawn();

            if (boundPawn != null)
            {
                RemoveBoundHediff(boundPawn);
                Messages.Message(boundPawn.Name.ToStringShort + "已从" + parent.Label + "解绑", MessageTypeDefOf.NeutralEvent);
                boundPawn = null;
            }
        }

        private void RemoveBoundHediff(Pawn pawn)
        {
            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(BoundDef);
            if (hediff != null) pawn.health.RemoveHediff(hediff);
        }

        private static bool IsBoundElsewhere(Pawn pawn)
        {
            var comp = pawn.health.hediffSet.GetFirstHediffOfDef(BoundDef)?.TryGetComp<HediffComp_ConsciousnessBound>();
            return comp?.hostComp != null && !comp.hostComp.parent.Destroyed;
        }

        public bool ForceEnterPawn(Pawn pawn)
        {
            if (pawn != boundPawn || pawn?.Dead != false) return false;
            if (!IsFunctional)
            {
                Messages.Message(parent.Label + "不可用，传送失败", MessageTypeDefOf.NegativeEvent);
                return false;
            }

            var container = InnerContainer;
            if (container == null || container.Count > 0) return false;

            if (pawn.Spawned) pawn.DeSpawn();

            if (!container.TryAddOrTransfer(pawn))
            {
                GenPlace.TryPlaceThing(pawn, parent.Position, parent.Map, ThingPlaceMode.Near);
                return false;
            }

            FleckMaker.Static(parent.Position, parent.Map, FleckDefOf.PsycastAreaEffect, 2f);
            return true;
        }

        public void ForceEjectPawn()
        {
            if (!IsOccupied)
            {
                Messages.Message("容器为空", MessageTypeDefOf.RejectInput);
                return;
            }

            var pawn = InnerContainer.FirstOrDefault(t => t is Pawn) as Pawn;
            if (pawn == null) return;

            if (parent is Building_CryptosleepCasket casket)
            {
                // 冬眠舱：EjectContents 内部会生成 pawn
                casket.EjectContents();
            }
            else
            {
                // 其他容器：手动移除并生成
                InnerContainer.Remove(pawn);
                if (!pawn.Spawned)
                    GenPlace.TryPlaceThing(pawn, parent.Position, parent.Map, ThingPlaceMode.Near);
            }

            GameComponent_HiddenPawns.Instance?.SetPawnHidden(pawn, false);
        }

        public void DoResurrection(Pawn pawn)
        {
            if (pawn != boundPawn) return;

            Log.Message(pawn.Name.ToStringShort + "终端离线");
            pawn.Corpse?.Destroy(DestroyMode.Vanish);

            ResurrectionUtility.TryResurrect(pawn);
            CureAllInjuries(pawn);

            if (!pawn.health.hediffSet.HasHediff(BoundDef)) AddBoundHediff(pawn);

            ForceEnterPawn(pawn);
            GameComponent_HiddenPawns.Instance?.SetPawnHidden(pawn, true);

            Messages.Message(pawn.Name.ToStringShort + "已回归" + parent.Label, new TargetInfo(parent), MessageTypeDefOf.PositiveEvent);
        }

        private void CureAllInjuries(Pawn pawn)
        {
            var injuries = pawn.health.hediffSet.hediffs
                .Where(h => h is Hediff_Injury || (Props.restoreMissingParts && h is Hediff_MissingPart))
                .ToList();

            foreach (var injury in injuries) pawn.health.RemoveHediff(injury);
            pawn.health.Reset();
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            if (boundPawn?.Dead == false)
            {
                if (IsOccupied) ForceEjectPawn();
                ReleasePawn();
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (boundPawn == null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "绑定殖民者",
                    defaultDesc = "绑定后，该殖民者死亡时会被紧急传送回此保存。",
                    icon = TexButton.Add,
                    action = ShowBindPawnDialog,
                    Disabled = !IsFunctional,
                    disabledReason = "主机不可用"
                };
            }
            else
            {
                yield return new Command_Action
                {
                    defaultLabel = "已绑定: " + boundPawn.Name.ToStringShort,
                    defaultDesc = "点击解绑并释放此主机。",
                    action = ReleasePawn
                };

                if (!IsOccupied && boundPawn.Spawned && !boundPawn.Dead)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "强制召回",
                        defaultDesc = "立即将绑定者传送至此。",
                        action = () => ForceEnterPawn(boundPawn),
                        Disabled = !IsFunctional || InnerContainer == null,
                        disabledReason = !IsFunctional ? "主机不可用" : "此建筑无容器功能"
                    };
                }

                if (IsOccupied)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "强制弹出",
                        defaultDesc = "立即唤醒并弹出绑定者。",
                        action = ForceEjectPawn
                    };
                }
            }
        }

        private void ShowBindPawnDialog()
        {
            var candidates = Find.ColonistBar.GetColonistsInOrder()
                .Where(p => p.Faction == Faction.OfPlayer && !p.Dead && !IsBoundElsewhere(p))
                .ToList();

            if (!candidates.Any())
            {
                Messages.Message("没有可绑定的殖民者", MessageTypeDefOf.RejectInput);
                return;
            }

            var options = candidates.Select(p =>
                new FloatMenuOption(p.Name.ToStringShort, () => BindPawn(p))
            ).ToList();

            Find.WindowStack.Add(new FloatMenu(options));
        }

        public override string CompInspectStringExtra()
        {
            if (boundPawn == null) return null;
            return "绑定: " + boundPawn.Name.ToStringShort + (!PowerOk ? " [无电力]" : "");
        }
    }

    [HarmonyPatch(typeof(Pawn), "Kill")]
    public static class Pawn_Kill_Patch
    {
        public static void Postfix(Pawn __instance)
        {
            if (!__instance.Dead) return;

            var comp = __instance.health.hediffSet
                .GetFirstHediffOfDef(HediffDef.Named("ConsciousnessBound"))
                ?.TryGetComp<HediffComp_ConsciousnessBound>();

            if (comp?.hostComp != null && !comp.hostComp.parent.Destroyed)
                comp.hostComp.DoResurrection(__instance);
        }
    }
}