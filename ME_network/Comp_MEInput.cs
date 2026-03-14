using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using UnityEngine;

namespace WastelandLizard
{
    public class CompProperties_MEInput : CompProperties
    {
        public int scanRadius = 1;
        public int workInterval = 60;
        public bool autoModeDefault = true;
        public CompProperties_MEInput(){compClass = typeof(Comp_MEInput);}
    }
    public class Comp_MEInput : ThingComp
    {
        private CompProperties_MEInput Props => (CompProperties_MEInput)props;
        private bool autoMode;
        private int lastWorkTick = -1;
        private List<Thing> ScanForIngredients() => MEUtility.ScanForItems(parent, Props.scanRadius);
        private void DoInput()
        {
            var network = parent.Map.GetComponent<MapComponent_MEStorage>();
            //if (network == null)
            //{
            //    if (Prefs.DevMode)
            //        Log.Message("[MEInput] Network component not found.");
            //    return;
            //}
            var items = ScanForIngredients();
            //if (items.Count == 0)
            //{
            //    if (Prefs.DevMode)
            //        Log.Message("[MEInput] No items found to input.");
            //    return;
            //}
            // 过滤尸体和需要指定stuff的物品
            var validItems = items.Where(t => !(t is Corpse) && !t.def.MadeFromStuff).ToList();
            //if (validItems.Count == 0)
            //{
            //    if (Prefs.DevMode)
            //        Log.Message("[MEInput] All items filtered out (corpse or MadeFromStuff).");
            //    return;
            //}
            //int deposited = 0;
            int skippedDueToLimit = 0;
            int skippedDueToCapacity = 0;
            foreach (var thing in validItems)
            {
                // 检查该物品的上限
                var limit = network.GetItemLimit(thing.def);
                if (limit.max >= 0)
                {
                    int current = network.GetCount(thing.def);
                    if (current + thing.stackCount > limit.max)
                    {
                        skippedDueToLimit++;
                        //if (Prefs.DevMode)Messages.Message(string.Format("物品 {0} 达到上限，跳过存入", thing.def.LabelCap), parent, MessageTypeDefOf.RejectInput);
                        continue; // 跳过此物品
                    }
                }
                // 检查总容量是否允许加入这个物品
                if (!network.CanAdd(thing.stackCount))
                {
                    skippedDueToCapacity++;
                    //if (Prefs.DevMode)Messages.Message("网络容量不足，停止存入后续物品", parent, MessageTypeDefOf.RejectInput);
                    break; // 容量不足，停止整个输入操作
                }
                // 存入网络
                network.Add(thing.def, thing.stackCount);
                thing.Destroy();
                //deposited++;
            }
            //if (deposited > 0)
            //{
            //    string message = string.Format("已存入 {0} 组物品到网络", deposited);
            //    if (skippedDueToLimit > 0)
            //        message += string.Format("，{0} 组因达到上限跳过", skippedDueToLimit);
            //    if (skippedDueToCapacity > 0)
            //        message += string.Format("，{0} 组因容量不足跳过", skippedDueToCapacity);
            //    Messages.Message(message, parent, MessageTypeDefOf.TaskCompletion);
            //}
            //else if (Prefs.DevMode)
            //{
            //    Log.Message("[MEInput] No items deposited.");
            //}
        }
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            autoMode = Props.autoModeDefault;

            if (parent.Map.GetComponent<MapComponent_MEStorage>() == null)
                parent.Map.components.Add(new MapComponent_MEStorage(parent.Map));
        }
        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned) return;
            if (autoMode)
            {
                int currentTick = Find.TickManager.TicksGame;
                if (lastWorkTick < 0 || currentTick - lastWorkTick >= Props.workInterval)
                {
                    DoInput();
                    lastWorkTick = currentTick;
                }
            }
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref autoMode, "autoMode");
            Scribe_Values.Look(ref lastWorkTick, "lastWorkTick", -1);
        }

        public override string CompInspectStringExtra()
        {
            string text = base.CompInspectStringExtra();
            text += "\n输入模式: " + (autoMode ? "自动" : "手动");
            return text.TrimStart('\n');
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra())
                yield return g;
            yield return new Command_Toggle
            {
                defaultLabel = "模式: " + (autoMode ? "自动" : "手动"),
                defaultDesc = "切换输入模式",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", true),
                isActive = () => autoMode,
                toggleAction = () => autoMode = !autoMode
            };
            if (!autoMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "手动输入",
                    defaultDesc = "立即将周围物品存入网络",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", true),
                    action = () => DoInput()
                };
            }
        }
    }
}