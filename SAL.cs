using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;
using UnityEngine;

namespace AutonomousWorkbench
{
    public class CompProperties_AutonomousWorkbench : CompProperties
    {
        public int workInterval = 60;           // 每多少tick工作一次
        public int scanRadius = 4;               // 扫描原料的范围
        public bool requirePower = true;         // 是否需要电力
        public SoundDef workSound;               // 工作音效

        public CompProperties_AutonomousWorkbench()
        {
            compClass = typeof(Comp_AutonomousWorkbench);
        }
    }

    public class Comp_AutonomousWorkbench : ThingComp
    {
        private CompProperties_AutonomousWorkbench Props => (CompProperties_AutonomousWorkbench)props;

        // 当前工作状态
        private RecipeDef currentRecipe;
        private float workProgress;
        private List<Thing> reservedIngredients = new List<Thing>();

        // 缓存
        private CompPowerTrader powerComp;
        private Building_WorkTable workTable;
        private Sustainer sustainer;

        // 统计
        private int itemsProduced;
        private int ticksSpentWorking;

        // Tick监控
        private int lastTickCheck = 0;
        private bool tickWorking = false;

        // 公开属性
        public bool IsWorking => currentRecipe != null;
        public string CurrentRecipeLabel => currentRecipe?.label ?? "无";

        public float ProgressPercent
        {
            get
            {
                if (currentRecipe == null) return 0f;
                float totalWork = currentRecipe.WorkAmountTotal(null);
                if (totalWork <= 0f) return 0f;
                return Mathf.Clamp01(1f - (workProgress / totalWork));
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            workTable = parent as Building_WorkTable;
            powerComp = parent.GetComp<CompPowerTrader>();

            if (workTable == null)
            {
                Log.Error("[AutonomousWorkbench] 只能挂载在工作台上！");
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            // 标记 tick 正在工作
            tickWorking = true;
            lastTickCheck = Find.TickManager.TicksGame;

            if (!parent.Spawned) return;
            if (workTable == null) return;

            // 电力检查
            bool hasPower = !Props.requirePower || (powerComp?.PowerOn == true);
            if (!hasPower) return;

            // 如果没有当前工作，尝试开始新工作
            if (currentRecipe == null)
            {
                AutoStartWork();
            }

            // 如果有工作，推进进度
            if (currentRecipe != null)
            {
                // 检查原料是否还在
                if (!AllIngredientsStillValid())
                {
                    Log.Message("[AutoWorkbench] 原料失效，取消当前工作");
                    CancelCurrentWork();
                    return;
                }

                float workSpeed = GetWorkSpeed();
                workProgress -= workSpeed;
                ticksSpentWorking++;

                // 每60tick打印一次进度
                if (Find.TickManager.TicksGame % 60 == 0)
                {
                    float totalWork = currentRecipe.WorkAmountTotal(null);
                    float percent = 1f - (workProgress / totalWork);
                    Log.Message(string.Format("[AutoWorkbench] 进度: {0:P1}, 剩余: {1:F1}/{2:F1}",
                        percent, workProgress, totalWork));
                }

                // 工作完成
                if (workProgress <= 0f)
                {
                    Log.Message("[AutoWorkbench] 工作完成！");
                    CompleteCurrentWork();
                }
            }

            // 音效
            if (IsWorking && hasPower && Props.workSound != null)
            {
                if (sustainer == null || sustainer.Ended)
                {
                    sustainer = Props.workSound.TrySpawnSustainer(SoundInfo.InMap(parent));
                }
                sustainer?.Maintain();
            }
            else if (sustainer != null && !sustainer.Ended)
            {
                sustainer.End();
                sustainer = null;
            }
        }

        private void AutoStartWork()
        {
            if (workTable?.billStack == null) return;

            Log.Message("[AutoWorkbench] 自动检查工作...");

            foreach (var bill in workTable.billStack)
            {
                if (!bill.ShouldDoNow()) continue;
                if (!bill.recipe.AvailableNow) continue;

                var ingredients = FindIngredientsForRecipe(bill.recipe);
                if (ingredients == null || ingredients.Count == 0) continue;

                Log.Message("[AutoWorkbench] 自动开始工作: " + bill.recipe.label);
                StartWork(bill.recipe, ingredients);
                return;
            }
        }

        private List<Thing> FindIngredientsForRecipe(RecipeDef recipe)
        {
            var result = new List<Thing>();
            var availableThings = ScanForIngredients();

            if (availableThings.Count == 0)
            {
                Log.Message("[AutoWorkbench] 周围没有找到任何物品");
                return null;
            }

            foreach (var ingredient in recipe.ingredients)
            {
                float needed = ingredient.GetBaseCount();
                Log.Message("[AutoWorkbench] 需要 " + ingredient.filter.Summary + ": " + needed);

                foreach (var thing in availableThings.ToList())
                {
                    if (result.Contains(thing)) continue;
                    if (!ingredient.filter.Allows(thing.def)) continue;

                    float valuePerUnit = recipe.IngredientValueGetter.ValuePerUnitOf(thing.def);
                    if (valuePerUnit <= 0f) continue;

                    float canTake = needed / valuePerUnit;

                    if (canTake >= thing.stackCount)
                    {
                        result.Add(thing);
                        availableThings.Remove(thing);
                        needed -= thing.stackCount * valuePerUnit;
                        Log.Message("[AutoWorkbench] 取用整个 " + thing.def.label + " x" + thing.stackCount);
                    }
                    else
                    {
                        int takeCount = Mathf.RoundToInt(canTake);
                        if (takeCount <= 0) continue;
                        if (takeCount > thing.stackCount) takeCount = thing.stackCount;

                        var splitThing = thing.SplitOff(takeCount);
                        result.Add(splitThing);
                        needed -= takeCount * valuePerUnit;
                        Log.Message("[AutoWorkbench] 取用部分 " + thing.def.label + " x" + takeCount);
                    }

                    if (needed <= 0.001f) break;
                }

                if (needed > 0.001f)
                {
                    Log.Message("[AutoWorkbench] 原料不足，需要 " + ingredient.filter.Summary + " 还差 " + needed);
                    foreach (var t in result)
                    {
                        if (t != null && !t.Destroyed && !t.Spawned)
                        {
                            GenPlace.TryPlaceThing(t, parent.Position, parent.Map, ThingPlaceMode.Near);
                        }
                    }
                    return null;
                }
            }

            Log.Message("[AutoWorkbench] 原料收集完成，共 " + result.Count + " 个物品");
            return result;
        }

        private List<Thing> ScanForIngredients()
        {
            var result = new List<Thing>();
            var center = parent.Position;

            for (int x = -Props.scanRadius; x <= Props.scanRadius; x++)
            {
                for (int z = -Props.scanRadius; z <= Props.scanRadius; z++)
                {
                    IntVec3 cell = new IntVec3(center.x + x, 0, center.z + z);
                    if (!cell.InBounds(parent.Map)) continue;
                    if (cell == parent.Position) continue;

                    var things = cell.GetThingList(parent.Map)
                        .Where(t => t.def.category == ThingCategory.Item && !t.IsForbidden(Faction.OfPlayer))
                        .ToList();

                    result.AddRange(things);
                }
            }

            return result;
        }

        private bool AllIngredientsStillValid()
        {
            foreach (var thing in reservedIngredients)
            {
                if (thing == null || thing.Destroyed)
                {
                    Log.Message("[AutoWorkbench] 原料失效: null 或已销毁");
                    return false;
                }
                // 原料被我们借用，不需要检查是否在地图上
            }
            return true;
        }

        private float GetWorkSpeed()
        {
            float baseSpeed = 20f; // 基础速度

            float speedFactor = parent.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor);
            if (speedFactor <= 0f) speedFactor = 1f;

            // 电力加成
            if (powerComp != null && powerComp.PowerOn)
            {
                speedFactor *= 1.5f;
            }

            float finalSpeed = baseSpeed * speedFactor;

            // 确保速度至少为1
            if (finalSpeed < 1f) finalSpeed = 1f;

            return finalSpeed;
        }

        private void StartWork(RecipeDef recipe, List<Thing> ingredients)
        {
            currentRecipe = recipe;
            reservedIngredients = ingredients;
            workProgress = recipe.WorkAmountTotal(null);

            Log.Message(string.Format("[AutoWorkbench] 开始工作: {0}, 总工作量: {1:F1}",
                recipe.label, workProgress));

            MoteMaker.ThrowText(parent.DrawPos, parent.Map, "开始" + recipe.label, 3f);
        }

        private void CancelCurrentWork()
        {
            Log.Message("[AutoWorkbench] 取消工作，归还原料");

            foreach (var thing in reservedIngredients)
            {
                if (thing != null && !thing.Destroyed)
                {
                    // 被借用的原料不应该在地图上，直接放置
                    Log.Message("[AutoWorkbench] 归还原料: " + thing.def.label + " x" + thing.stackCount);
                    GenPlace.TryPlaceThing(thing, parent.Position, parent.Map, ThingPlaceMode.Near);
                }
            }

            currentRecipe = null;
            reservedIngredients.Clear();
        }

        private void CompleteCurrentWork()
        {
            if (currentRecipe == null) return;

            Log.Message("[AutoWorkbench] 完成工作: " + currentRecipe.label);

            // 生成产品
            foreach (var product in currentRecipe.products)
            {
                var stuff = DetermineStuffForProduct(currentRecipe, reservedIngredients);
                var thing = ThingMaker.MakeThing(product.thingDef, stuff);
                thing.stackCount = product.count;

                var compColorable = thing.TryGetComp<CompColorable>();
                if (compColorable != null && reservedIngredients.Count > 0)
                {
                    compColorable.SetColor(reservedIngredients[0].DrawColor);
                }

                GenPlace.TryPlaceThing(thing, parent.Position, parent.Map, ThingPlaceMode.Near);
                itemsProduced += product.count;

                Log.Message("[AutoWorkbench] 产出: " + thing.def.label + " x" + product.count);
            }

            // 找到对应的账单并更新
            Bill matchingBill = null;
            foreach (var bill in workTable.billStack)
            {
                if (bill.recipe == currentRecipe)
                {
                    matchingBill = bill;
                    break;
                }
            }

            if (matchingBill != null)
            {
                matchingBill.Notify_IterationCompleted(null, reservedIngredients);
            }

            // 消耗原料 - 直接销毁，它们本来就不在地图上
            foreach (var thing in reservedIngredients)
            {
                if (thing != null && !thing.Destroyed)
                {
                    Log.Message("[AutoWorkbench] 消耗原料: " + thing.def.label);
                    thing.Destroy();
                }
            }

            MoteMaker.ThrowText(parent.DrawPos, parent.Map, "完成", 3f);

            currentRecipe = null;
            reservedIngredients.Clear();
        }

        private ThingDef DetermineStuffForProduct(RecipeDef recipe, List<Thing> ingredients)
        {
            if (!recipe.products.Any(p => p.thingDef.MadeFromStuff))
                return null;

            var stuffIngredient = ingredients.FirstOrDefault(i => i.def.IsStuff);
            return stuffIngredient?.def;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Defs.Look(ref currentRecipe, "currentRecipe");
            Scribe_Values.Look(ref workProgress, "workProgress");
            Scribe_Collections.Look(ref reservedIngredients, "reservedIngredients", LookMode.Reference);
            Scribe_Values.Look(ref itemsProduced, "itemsProduced");
            Scribe_Values.Look(ref ticksSpentWorking, "ticksSpentWorking");
            Scribe_Values.Look(ref lastTickCheck, "lastTickCheck");
        }

        public override string CompInspectStringExtra()
        {
            string text = base.CompInspectStringExtra();

            // 添加 Tick 状态检查 - 用 + 连接，不用字符串插值
            int ticksSinceLast = Find.TickManager.TicksGame - lastTickCheck;
            if (ticksSinceLast > 120)
            {
                text = text + "\n⚠️ Tick 可能被暂停 (" + ticksSinceLast + " ticks 无响应)";
            }

            if (IsWorking)
            {
                text = text + "\n自动工作: " + currentRecipe.label + " (" + ProgressPercent.ToStringPercent() + ")";
            }
            else
            {
                text = text + "\n自动工作: 待机中";
            }

            if (itemsProduced > 0)
            {
                text = text + "\n已生产: " + itemsProduced + " 件";
            }

            return text.TrimStart('\n');
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra())
                yield return g;

            // Tick 状态检查
            yield return new Command_Action
            {
                defaultLabel = "检查 Tick 状态",
                defaultDesc = "显示 Tick 是否正常运行",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", true),
                action = () =>
                {
                    int ticksSinceLast = Find.TickManager.TicksGame - lastTickCheck;
                    if (tickWorking && ticksSinceLast < 120)
                    {
                        Messages.Message("✅ Tick 正常运行，上次检查: " + ticksSinceLast + " ticks前", parent, MessageTypeDefOf.TaskCompletion);
                    }
                    else
                    {
                        Messages.Message("❌ Tick 可能被暂停！上次检查: " + ticksSinceLast + " ticks前", parent, MessageTypeDefOf.RejectInput);
                    }

                    // 强制触发一次 Tick
                    CompTick();
                }
            };

            // 强制工作按钮
            yield return new Command_Action
            {
                defaultLabel = "强制工作",
                defaultDesc = "立即检查并开始工作",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", true),
                action = () =>
                {
                    AutoStartWork();
                    if (currentRecipe != null)
                    {
                        Messages.Message("开始: " + currentRecipe.label, parent, MessageTypeDefOf.TaskCompletion);
                    }
                    else
                    {
                        var items = ScanForIngredients();
                        if (items.Count == 0)
                        {
                            Messages.Message("周围没有原料！", parent, MessageTypeDefOf.RejectInput);
                        }
                        else
                        {
                            Messages.Message("找到原料，但没有合适的账单", parent, MessageTypeDefOf.RejectInput);
                        }
                    }
                }
            };

            // 取消工作按钮
            if (IsWorking)
            {
                yield return new Command_Action
                {
                    defaultLabel = "取消工作",
                    defaultDesc = "取消当前工作并归还原料",
                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel", true),
                    action = () =>
                    {
                        CancelCurrentWork();
                        Messages.Message("工作已取消", parent, MessageTypeDefOf.TaskCompletion);
                    }
                };
            }
        }
    }
}