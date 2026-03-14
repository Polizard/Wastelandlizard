using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;
using UnityEngine;

namespace WastelandLizard
{
    // 枚举网络模式
    public enum NetworkMode{Offline,Online}
    // 联网模式下产物去向，Deposit存入ME网络，Drop放置
    public enum OutputMode{Deposit,Drop}
    // 用于记录联网模式下每个原料项选择的物品类型和数量
    public class RecipeIngredientSelection : IExposable
    {
        public IngredientCount ingredient;
        public ThingDef chosenDef;
        public int requiredCount;
        public void ExposeData()
        {
            Scribe_Defs.Look(ref chosenDef, "chosenDef");
            Scribe_Values.Look(ref requiredCount, "requiredCount", 0);
        }
    }
    //自动化工作台配置
    public class CompProperties_AutonomousWorkbench : CompProperties
    {
        public int workInterval = 60;//60tick工作一次
        public int scanRadius = 4;//离线模式搜索原材料半径。说真的我应该多写一个偏移量的，要不然超大机器就很浪费性能了。
        public bool requirePower = true;//万一真有不要电的神人自动化机器呢
        public SoundDef workSound;
        public float baseWorkSpeed = 20f;
        public float powerSpeedBoost = 1.5f;
        public Vector3 dropOffset = new Vector3(1, 0, 1);//输出位置相对于中心的偏移量
        public CompProperties_AutonomousWorkbench(){compClass = typeof(Comp_AutonomousWorkbench);}
    }

    // 自动工作台组件
    public class Comp_AutonomousWorkbench : ThingComp
    {
        private CompProperties_AutonomousWorkbench Props => (CompProperties_AutonomousWorkbench)props;
        // 核心状态
        private RecipeDef currentRecipe;
        private float workProgress;
        private NetworkMode networkMode = NetworkMode.Offline;
        private OutputMode outputMode = OutputMode.Deposit;
        // 断网模式专用：物理保留的原料
        private List<Thing> reservedIngredients = new List<Thing>();
        // 联网模式专用：记录的原料选择
        private List<RecipeIngredientSelection> selectedIngredients = new List<RecipeIngredientSelection>();
        // 品质偏移量，等后续更新介入
        private int qualityShift = 0;
        // 工作速度倍率，等后续更新介入
        private float speedMultiplier = 1f;
        // 缓存
        private CompPowerTrader powerComp;
        private Building_WorkTable workTable;
        private Sustainer sustainer;
        // 统计
        //private int itemsProduced;
        //private int ticksSpentWorking;
        // Tick监控
        //private int lastTickCheck = 0;
        //private bool tickWorking = false;

        // 公开属性
        public bool IsWorking => currentRecipe != null;
        public string CurrentRecipeLabel => currentRecipe?.label ?? "N/A";
        //进度条
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
        //质量偏移
        public int QualityShift{get => qualityShift;set => qualityShift = value;}
        //速度偏移
        public float SpeedMultiplier{get => speedMultiplier;set => speedMultiplier = Mathf.Max(0.1f, value);}
        // ---------- 生命周期 ----------
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            workTable = parent as Building_WorkTable;
            powerComp = parent.GetComp<CompPowerTrader>();
            if (workTable == null){Log.Error("[AutonomousWorkbench] 只能挂载在工作台上！");}
            // 确保ME网络存在
            if (parent.Map.GetComponent<MapComponent_MEStorage>() == null){parent.Map.components.Add(new MapComponent_MEStorage(parent.Map));}
        }
        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned || workTable == null) return;
            bool hasPower = !Props.requirePower || (powerComp?.PowerOn == true);
            if (!hasPower) return;
            // 如果工作已完成但等待输出，尝试完成
            if (currentRecipe != null && workProgress <= 0f)
            {TryOutput();}
            // 正常进行工作
            if (currentRecipe == null){AutoStartWork();}
            if (currentRecipe != null && workProgress > 0f)
            {
                //离线模式原料检定
                if (networkMode == NetworkMode.Offline && !AllIngredientsStillValid()){CancelCurrentWork();return;}
                float workSpeed = GetWorkSpeed();
                workProgress -= workSpeed;
                //if (Find.TickManager.TicksGame % 60 == 0 && Prefs.DevMode)
                //{
                //    float totalWork = currentRecipe.WorkAmountTotal(null);
                //    float percent = 1f - (workProgress / totalWork);
                //    Log.Message(string.Format("[AutoWorkbench] 进度: {0:P1}, 剩余: {1:F1}/{2:F1}",
                //        percent, workProgress, totalWork));
                //}
            }
            HandleSound(hasPower);
        }
        //deepseek给写的循环音效
        private void HandleSound(bool hasPower)
        {
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
        //自动启动工作
        private void AutoStartWork()
        {
            if (workTable?.billStack == null) return;
            foreach (var bill in workTable.billStack)
            {
                if (!bill.ShouldDoNow()) continue;
                if (!bill.recipe.AvailableNow) continue;
                if (networkMode == NetworkMode.Online)
                {
                    if (TryReserveIngredientsForRecipe(bill.recipe))
                    {
                        StartWork(bill.recipe);
                        return;
                    }
                }
                else
                {
                    var ingredients = FindIngredientsForRecipe(bill.recipe);
                    if (ingredients != null)
                    {
                        StartWork(bill.recipe, ingredients);
                        return;
                    }
                }
            }
        }

        //连接ME网络时从ME网络抽取原料
        private bool TryReserveIngredientsForRecipe(RecipeDef recipe)
        {
            var network = parent.Map.GetComponent<MapComponent_MEStorage>();
            if (network == null) return false;
            selectedIngredients.Clear();
            var networkContents = network.GetAllContents();
            foreach (var ingredient in recipe.ingredients)
            {
                float neededValue = ingredient.GetBaseCount();
                var possibleDefs = ingredient.filter.AllowedThingDefs
                    .Where(def => networkContents.ContainsKey(def) && networkContents[def] > 0)
                    .ToList();
                if (possibleDefs.Count == 0) return false;
                var bestDef = possibleDefs.OrderByDescending(def => networkContents[def]).First();
                float valuePerUnit = recipe.IngredientValueGetter.ValuePerUnitOf(bestDef);
                if (valuePerUnit <= 0f) continue;
                int neededUnits = Mathf.CeilToInt(neededValue / valuePerUnit);
                if (networkContents[bestDef] < neededUnits) return false;
                //执行配方后剩余数量必须不低于下限
                var limit = network.GetItemLimit(bestDef);
                if (limit.min >= 0)
                {
                    int remaining = networkContents[bestDef] - neededUnits;
                    if (remaining < limit.min){return false;}
                }
                selectedIngredients.Add(new RecipeIngredientSelection
                {
                    ingredient = ingredient,
                    chosenDef = bestDef,
                    requiredCount = neededUnits
                });
                networkContents[bestDef] -= neededUnits;
            }
            return true;
        }
        //离线模式从周围扒原料
        private List<Thing> FindIngredientsForRecipe(RecipeDef recipe)
        {
            var result = new List<Thing>();
            var availableThings = ScanForIngredients();
            if (availableThings.Count == 0) return null;
            foreach (var ingredient in recipe.ingredients)
            {
                float needed = ingredient.GetBaseCount();
                foreach (var thing in availableThings.ToList())
                {
                    if (result.Contains(thing)) continue;
                    if (!ingredient.filter.Allows(thing.def)) continue;
                    float valuePerUnit = recipe.IngredientValueGetter.ValuePerUnitOf(thing.def);
                    if (valuePerUnit <= 0f) continue;
                    float canTake = needed / valuePerUnit;
                    int takeCount = Mathf.RoundToInt(Mathf.Min(canTake, thing.stackCount));
                    if (takeCount <= 0) continue;
                    if (takeCount < thing.stackCount)
                    {
                        var splitThing = thing.SplitOff(takeCount);
                        result.Add(splitThing);
                    }
                    else
                    {
                        result.Add(thing);
                        availableThings.Remove(thing);
                    }
                    needed -= takeCount * valuePerUnit;
                    if (needed <= 0.001f) break;
                }
                if (needed > 0.001f)
                {
                    foreach (var t in result)
                    {
                        if (t != null && !t.Destroyed && !t.Spawned)
                            GenPlace.TryPlaceThing(t, parent.Position, parent.Map, ThingPlaceMode.Near);
                    }
                    return null;
                }
            }
            return result;
        }
        //搜索周围
        private List<Thing> ScanForIngredients() => MEUtility.ScanForItems(parent, Props.scanRadius);
        private bool AllIngredientsStillValid()
        {
            foreach (var thing in reservedIngredients)
            {if (thing == null || thing.Destroyed)return false;}
            return true;
        }
        private float GetWorkSpeed()
        {
            float speed = Props.baseWorkSpeed;
            speed *= parent.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor);
            if (Props.requirePower && powerComp?.PowerOn == true)
            {
                speed *= Props.powerSpeedBoost;
            }
            if (currentRecipe != null)
            {
                float totalWork = currentRecipe.WorkAmountTotal(null);
                float progress = 1f - (workProgress / totalWork);
                speed *= (1f + progress * 0.2f);
            }
            speed *= Rand.Range(0.9f, 1.1f);
            speed *= speedMultiplier;
            return Mathf.Max(1f, speed);
        }
        //启动配方，用到了重载
        private void StartWork(RecipeDef recipe, List<Thing> ingredients)
        {
            currentRecipe = recipe;
            reservedIngredients = ingredients;
            workProgress = recipe.WorkAmountTotal(null);
            MoteMaker.ThrowText(parent.DrawPos, parent.Map, string.Format("▶▶▶{0}", recipe.label), 3f);
        }
        private void StartWork(RecipeDef recipe)
        {
            currentRecipe = recipe;
            workProgress = recipe.WorkAmountTotal(null);
            MoteMaker.ThrowText(parent.DrawPos, parent.Map, string.Format("▶▶▶{0}", recipe.label), 3f);
        }
        //取消配方退材料。还是ME网络模式方便啊。
        private void CancelCurrentWork()
        {
            if (networkMode == NetworkMode.Online){selectedIngredients.Clear();}
            else
            {
                foreach (var thing in reservedIngredients)
                {
                    if (thing != null && !thing.Destroyed)
                        GenPlace.TryPlaceThing(thing, parent.Position, parent.Map, ThingPlaceMode.Near);
                }
                reservedIngredients.Clear();
            }
            currentRecipe = null;
            workProgress = 0f;
        }
        //产品输出位置
        private IntVec3 GetDropPosition() => MEUtility.GetRotatedDropPosition(parent, Props.dropOffset);
        // 检查堵塞
        private bool CanPlaceAt(ThingDef def, int count, IntVec3 pos)
        {
            Map map = parent.Map;
            var things = pos.GetThingList(map);
            // 检查是否有同类stack
            foreach (var thing in things)
            {
                if (thing.def == def && thing.stackCount < def.stackLimit)
                {
                    int space = def.stackLimit - thing.stackCount;
                    if (space >= count) return true;
                }
            }
            // 检查位置是否为空
            if (things.Any(t => t.def.category == ThingCategory.Item))return false; // 有其他物品占据
            return true; // 空位置
        }
        //尝试完成工作
        private void TryOutput()
        {
            if (currentRecipe == null) return;
            //连接ME检查材料还在不在
            if (networkMode == NetworkMode.Online)
            {
                var network = parent.Map.GetComponent<MapComponent_MEStorage>();
                if (network == null){CancelCurrentWork();return;}
                foreach (var sel in selectedIngredients)
                {
                    if (!network.Has(sel.chosenDef, sel.requiredCount)){CancelCurrentWork();return;}
                }
                // 如果是存入网络模式，检查产物上限
                if (outputMode == OutputMode.Deposit)
                {
                    bool canDepositAll = true;
                    foreach (var product in currentRecipe.products)
                    {
                        if (product.thingDef.MadeFromStuff)
                        {
                            // 需要指定stuff的产物会强制掉落，不检查网络上限
                            continue;
                        }
                        var limit = network.GetItemLimit(product.thingDef);
                        if (limit.max >= 0)
                        {
                            int current = network.GetCount(product.thingDef);
                            if (current + product.count > limit.max)
                            {
                                canDepositAll = false;
                                break;
                            }
                        }
                    }
                    if (!canDepositAll)
                    {
                        // 产物超限，等待（不取消工作，只是暂不完成）
                        return;
                    }
                }
            }
            // 检查输出位置是否可放置所有产物
            IntVec3 dropPos = GetDropPosition();
            bool canPlaceAll = true;
            foreach (var product in currentRecipe.products)
            {
                if (!CanPlaceAt(product.thingDef, product.count, dropPos))
                {
                    canPlaceAll = false;
                    break;
                }
            }
            if (!canPlaceAll)
            {
                return;
            }
            // 所有条件满足，完成工作
            CompleteCurrentWork();
        }
        // 完成工作（消耗原料并生成产物）
        private void CompleteCurrentWork()
        {
            if (currentRecipe == null) return;
            // 需要指定stuff的产物强制掉落
            bool anyProductNeedsStuff = currentRecipe.products.Any(p => p.thingDef.MadeFromStuff);
            if (networkMode == NetworkMode.Online && !anyProductNeedsStuff)
            {
                var network = parent.Map.GetComponent<MapComponent_MEStorage>();
                if (network == null) return;
                // 消耗网络原料
                foreach (var sel in selectedIngredients)
                {network.TryConsume(sel.chosenDef, sel.requiredCount);}
                // 根据输出模式处理产物
                if (outputMode == OutputMode.Deposit)
                {
                    int totalProduced = currentRecipe.products.Sum(p => p.count);
                    if (!network.CanAdd(totalProduced))
                    {
                        // 容量不足，转为掉落模式
                        OutputToDrop();
                        return;
                    }
                    // 存入ME网络
                    foreach (var product in currentRecipe.products)
                    {
                        int count = product.count;
                        ThingDef stuff = null;
                        if (product.thingDef.MadeFromStuff && selectedIngredients.Count > 0)
                        {
                            var first = selectedIngredients.First();
                            if (first.chosenDef.IsStuff)
                                stuff = first.chosenDef;
                        }
                        network.Add(product.thingDef, count);
                    }
                }
                else
                {OutputToDrop();}
                selectedIngredients.Clear();
            }
            else{OutputToDrop();}
            NotifyBillCompleted();
            MoteMaker.ThrowText(parent.DrawPos, parent.Map, "⚙️✔", 3f);
            currentRecipe = null;
            workProgress = 0f;
        }
        //随手一扔
        private void OutputToDrop()
        {
            foreach (var product in currentRecipe.products)
            {
                ThingDef stuff = null;
                if (product.thingDef.MadeFromStuff)
                {
                    if (networkMode == NetworkMode.Online && selectedIngredients.Count > 0)
                    {
                        var first = selectedIngredients.First();
                        if (first.chosenDef.IsStuff)
                            stuff = first.chosenDef;
                    }
                    else if (networkMode == NetworkMode.Offline && reservedIngredients.Count > 0)
                    {
                        var first = reservedIngredients.FirstOrDefault(i => i.def.IsStuff);
                        stuff = first?.def;
                    }
                }
                var thing = ThingMaker.MakeThing(product.thingDef, stuff);
                thing.stackCount = product.count;
                if (networkMode == NetworkMode.Offline)
                {
                    var compColorable = thing.TryGetComp<CompColorable>();
                    if (compColorable != null && reservedIngredients.Count > 0)
                        compColorable.SetColor(reservedIngredients[0].DrawColor);
                }
                ApplyQualityShift(thing);
                GenPlace.TryPlaceThing(thing, GetDropPosition(), parent.Map, ThingPlaceMode.Near);
            }
            if (networkMode == NetworkMode.Offline)
            {
                foreach (var thing in reservedIngredients)
                {
                    if (thing != null && !thing.Destroyed)
                        thing.Destroy();
                }
                reservedIngredients.Clear();
            }
        }
        // 应用品质偏移
        private void ApplyQualityShift(Thing thing)
        {
            var compQuality = thing.TryGetComp<CompQuality>();
            if (compQuality == null) return;

            QualityCategory baseQuality = GenerateRandomQuality();
            int newLevel = (int)baseQuality + qualityShift;
            newLevel = Mathf.Clamp(newLevel, (int)QualityCategory.Awful, (int)QualityCategory.Legendary);
            compQuality.SetQuality((QualityCategory)newLevel, ArtGenerationContext.Outsider);
        }
        //假装工况有起伏
        private QualityCategory GenerateRandomQuality()
        {
            float[] weights = { 5f, 20f, 50f, 15f, 7f, 2f, 1f };
            float total = weights.Sum();
            float rand = (float)Rand.Value * total;
            float accum = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                accum += weights[i];
                if (rand < accum)
                    return (QualityCategory)i;
            }
            return QualityCategory.Normal;
        }
        private void NotifyBillCompleted()
        {
            if (workTable?.billStack == null) return;
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
                matchingBill.Notify_IterationCompleted(null,
                    networkMode == NetworkMode.Offline ? reservedIngredients : null);
            }
        }
        //检查需要指定stuff的配方
        private ThingDef DetermineStuffForProduct(RecipeDef recipe, List<Thing> ingredients)
        {
            if (!recipe.products.Any(p => p.thingDef.MadeFromStuff))
                return null;
            var stuffIngredient = ingredients.FirstOrDefault(i => i.def.IsStuff);
            return stuffIngredient?.def;
        }
        // ---------- 界面方法 ----------
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Defs.Look(ref currentRecipe, "currentRecipe");
            Scribe_Values.Look(ref workProgress, "workProgress");
            Scribe_Collections.Look(ref reservedIngredients, "reservedIngredients", LookMode.Reference);
            Scribe_Values.Look(ref networkMode, "networkMode");
            Scribe_Values.Look(ref outputMode, "outputMode");
            Scribe_Values.Look(ref qualityShift, "qualityShift", 1);
            Scribe_Values.Look(ref speedMultiplier, "speedMultiplier", 1f);
            Scribe_Collections.Look(ref selectedIngredients, "selectedIngredients", LookMode.Deep);
        }
        public override string CompInspectStringExtra()
        {
            string text = base.CompInspectStringExtra();
            if (IsWorking)
                text += string.Format("\n自动工作: {0} ({1})", currentRecipe.label, ProgressPercent.ToStringPercent());
            else
                text += "\n自动工作: 待机中";
            text += string.Format("\nME Mode: {0}", networkMode == NetworkMode.Online ? "Online" : "Offline");
            if (networkMode == NetworkMode.Online)
                text += string.Format("\n产物去向: {0}", outputMode == OutputMode.Deposit ? "存入网络" : "掉落地面");
            if (speedMultiplier != 1f)
                text += string.Format("\n速度倍率: {0}", speedMultiplier.ToStringPercent());
            return text.TrimStart('\n');
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra())
                yield return g;
            // 网络模式切换
            yield return new Command_Toggle
            {
                defaultLabel = string.Format("ME网络模式: {0}", networkMode == NetworkMode.Online ? "在线" : "离线"),
                defaultDesc = "切换工作台的原料获取方式。\n在线：只从ME网络中取用原料。\n离线：扫描周围物品。",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", true),
                isActive = () => networkMode == NetworkMode.Online,
                toggleAction = () =>
                {
                    networkMode = networkMode == NetworkMode.Online ? NetworkMode.Offline : NetworkMode.Online;
                    if (IsWorking)CancelCurrentWork();
                }
            };
            if (networkMode == NetworkMode.Online)
            {
                // 产物去向切换
                yield return new Command_Toggle
                {
                    defaultLabel = string.Format("产物去向: {0}", outputMode == OutputMode.Deposit ? "存入网络" : "放置"),
                    defaultDesc = "选择在线模式下产物的去向。\n存入网络：产物直接加入ME网络。\n放置：产物放置于输出位置。",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", true),
                    isActive = () => outputMode == OutputMode.Deposit,
                    toggleAction = () =>
                    {
                        outputMode = outputMode == OutputMode.Deposit ? OutputMode.Drop : OutputMode.Deposit;
                    }
                };
                // 查看ME网络
                yield return new Command_Action
                {
                    defaultLabel = "查看ME网络内容",
                    defaultDesc = "显示ME网络中所有物品的清单",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", true),
                    action = () => Find.WindowStack.Add(new Window_MEChoose(parent.Map))
                };
            }
            // 品质偏移调节
            //yield return new Command_Action
            //{
            //    defaultLabel = "品质偏移 +1",
            //    defaultDesc = "提高产物的品质等级",
            //    icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", true),
            //    action = () => qualityShift++
            //};
            //yield return new Command_Action
            //{
            //    defaultLabel = "品质偏移 -1",
            //    defaultDesc = "降低产物的品质等级",
            //    icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", true),
            //    action = () => qualityShift--
            //};
            // 速度倍率调节
            //yield return new Command_Action
            //{
            //    defaultLabel = "速度倍率 +0.1",
            //    defaultDesc = "增加工作速度",
            //    icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", true),
            //    action = () => SpeedMultiplier += 0.1f
            //};
            //yield return new Command_Action
            //{
            //    defaultLabel = "速度倍率 -0.1",
            //    defaultDesc = "降低工作速度",
            //    icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", true),
            //    action = () => SpeedMultiplier -= 0.1f
            //};
            //yield return new Command_Action
            //{
            //    defaultLabel = "重置速度倍率",
            //    defaultDesc = "将速度倍率重置为1.0",
            //    icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", true),
            //    action = () => SpeedMultiplier = 1f
            //};
            // 开发调试
            //if (Prefs.DevMode)
            //{
            //    yield return new Command_Action
            //    {
            //        defaultLabel = "[调试] 添加100钢铁到网络",
            //        icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", true),
            //        action = DebugAddSteelToNetwork
            //    };
            //}
            // 强制工作，以防万一
            yield return new Command_Action
            {
                defaultLabel = "强制工作",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", true),
                action = () =>
                {
                    AutoStartWork();
                    if (currentRecipe != null)
                        Messages.Message(string.Format("开始: {0}", currentRecipe.label), parent, MessageTypeDefOf.TaskCompletion);
                    else
                    {
                        if (networkMode == NetworkMode.Offline && ScanForIngredients().Count == 0)
                            Messages.Message("周围没有原料！", parent, MessageTypeDefOf.RejectInput);
                        else
                            Messages.Message("配方或原料缺失", parent, MessageTypeDefOf.RejectInput);
                    }
                }
            };
            // 取消工作
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

        // 手动存入周围物品（仅调试或手动模式使用）
        //private void DepositNearbyItemsToNetwork()
        //{
        //    var network = parent.Map.GetComponent<MapComponent_MEStorage>();
        //    if (network == null) return;

        //    var items = ScanForIngredients();
        //    if (items.Count == 0)
        //    {
        //        Messages.Message("周围没有可存入的物品", parent, MessageTypeDefOf.RejectInput);
        //        return;
        //    }

        //    // 过滤掉需要材料的物品
        //    var validItems = items.Where(t => !t.def.MadeFromStuff).ToList();
        //    if (validItems.Count == 0)
        //    {
        //        Messages.Message("周围没有可存入的基础资源（需要材料的物品被过滤）", parent, MessageTypeDefOf.RejectInput);
        //        return;
        //    }

        //    int totalStack = validItems.Sum(t => t.stackCount);
        //    if (!network.CanAdd(totalStack))
        //    {
        //        Messages.Message("网络容量不足", parent, MessageTypeDefOf.RejectInput);
        //        return;
        //    }

        //    int deposited = 0;
        //    foreach (var thing in validItems)
        //    {
        //        network.Add(thing.def, thing.stackCount);
        //        thing.Destroy();
        //        deposited++;
        //    }
        //    Messages.Message(string.Format("已存入 {0} 组物品到网络", deposited), parent, MessageTypeDefOf.TaskCompletion);
        //}

        //private void DebugAddSteelToNetwork()
        //{
        //    var network = parent.Map.GetComponent<MapComponent_MEStorage>();
        //    if (network == null)
        //    {
        //        network = new MapComponent_MEStorage(parent.Map);
        //        parent.Map.components.Add(network);
        //    }
        //    network.Add(ThingDefOf.Steel, 100);
        //    Messages.Message("已添加100钢铁到ME网络", parent, MessageTypeDefOf.TaskCompletion);
        //}
    }
}