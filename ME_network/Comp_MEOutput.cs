using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;
using UnityEngine;

namespace WastelandLizard
{
    public class CompProperties_MEOutput : CompProperties
    {
        public int workInterval = 60;
        public Vector3 dropOffset = new Vector3(1, 0, 1); // 默认偏移，可在XML中覆盖

        public CompProperties_MEOutput()
        {
            compClass = typeof(Comp_MEOutput);
        }
    }

    public class Comp_MEOutput : ThingComp
    {
        private CompProperties_MEOutput Props => (CompProperties_MEOutput)props;

        private ThingDef selectedDef;
        private int lastWorkTick = -1;

        private IntVec3 GetDropPosition() => MEUtility.GetRotatedDropPosition(parent, Props.dropOffset);

        private void DoOutput()
        {
            if (selectedDef == null) return;
            if (selectedDef.MadeFromStuff) return; // 需要材料的物品无法直接输出

            var network = parent.Map.GetComponent<MapComponent_MEStorage>();
            if (network == null) return;

            int networkCount = network.GetCount(selectedDef);
            if (networkCount <= 0) return;

            IntVec3 dropPos = GetDropPosition();
            Map map = parent.Map;

            // 检查目标位置是否有同类型且未满堆的物品
            Thing existingStack = null;
            int space = 0;
            var thingsAtPos = dropPos.GetThingList(map);
            foreach (var thing in thingsAtPos)
            {
                if (thing.def == selectedDef && thing.stackCount < thing.def.stackLimit)
                {
                    existingStack = thing;
                    space = thing.def.stackLimit - thing.stackCount;
                    break;
                }
            }

            var limit = network.GetItemLimit(selectedDef); // 获取下限限制

            if (existingStack != null)
            {
                // 补充已有堆
                int countToAdd = Mathf.Min(space, networkCount);
                if (countToAdd <= 0) return;

                // 检查下限：补充后剩余数量必须 ≥ 下限
                if (limit.min >= 0 && networkCount - countToAdd < limit.min)
                    return;

                if (network.TryConsume(selectedDef, countToAdd))
                {
                    Thing thing = ThingMaker.MakeThing(selectedDef);
                    thing.stackCount = countToAdd;
                    existingStack.TryAbsorbStack(thing, false);
                }
            }
            else
            {
                // 目标位置没有同类型堆：检查是否被其他物品占据
                bool hasOtherItems = false;
                foreach (var thing in thingsAtPos)
                {
                    if (thing.def.category == ThingCategory.Item)
                    {
                        hasOtherItems = true;
                        break;
                    }
                }
                if (hasOtherItems) return;

                // 位置空闲，尝试放置新堆
                int stackLimit = selectedDef.stackLimit;
                int countToPlace = Mathf.Min(networkCount, stackLimit);
                if (countToPlace <= 0) return;

                // 检查下限：放置后剩余数量必须 ≥ 下限
                if (limit.min >= 0 && networkCount - countToPlace < limit.min)
                    return;

                Thing newThing = ThingMaker.MakeThing(selectedDef);
                newThing.stackCount = countToPlace;

                bool placed = GenPlace.TryPlaceThing(newThing, dropPos, map, ThingPlaceMode.Direct, out Thing resultingThing);
                if (placed)
                {
                    // 成功放置后消耗网络物品；若消耗失败则回滚放置
                    if (!network.TryConsume(selectedDef, countToPlace))
                    {
                        resultingThing?.Destroy();
                    }
                }
                else
                {
                    newThing.Destroy();
                }
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (parent.Map.GetComponent<MapComponent_MEStorage>() == null)
                parent.Map.components.Add(new MapComponent_MEStorage(parent.Map));
        }

        public override void CompTick()
        {
            base.CompTick();

            if (!parent.Spawned) return;

            int currentTick = Find.TickManager.TicksGame;
            if (lastWorkTick < 0 || currentTick - lastWorkTick >= Props.workInterval)
            {
                DoOutput();
                lastWorkTick = currentTick;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Defs.Look(ref selectedDef, "selectedDef");
            Scribe_Values.Look(ref lastWorkTick, "lastWorkTick", -1);
        }

        public override string CompInspectStringExtra()
        {
            string text = base.CompInspectStringExtra();
            text += "\n输出物品: " + (selectedDef?.LabelCap ?? "未选择");
            return text.TrimStart('\n');
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra())
                yield return g;

            yield return new Command_Action
            {
                defaultLabel = "选择输出物品",
                defaultDesc = "从ME网络中选择要输出的物品类型",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", true),
                action = () =>
                {
                    var network = parent.Map.GetComponent<MapComponent_MEStorage>();
                    if (network == null) return;

                    Find.WindowStack.Add(new Window_MEChoose(parent.Map, (def) =>
                    {
                        selectedDef = def;
                        Messages.Message(string.Format("已选择输出物品: {0}", def.LabelCap), parent, MessageTypeDefOf.TaskCompletion);
                    }));
                }
            };

            if (selectedDef != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "立即输出一次",
                    defaultDesc = "尽可能输出一堆",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", true),
                    action = () => DoOutput()
                };
            }
        }
    }
}