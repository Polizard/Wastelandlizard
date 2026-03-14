using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WastelandLizard
{
    public class CompProperties_MELimit : CompProperties
    {
        public CompProperties_MELimit(){compClass = typeof(Comp_MElimit);}
    }
    public class Comp_MElimit : ThingComp
    {
        private MapComponent_MEStorage Network => parent.Map?.GetComponent<MapComponent_MEStorage>();
        private ThingDef selectedDef;
        private int minLimit = -1;
        private int maxLimit = -1;
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (parent.Map.GetComponent<MapComponent_MEStorage>() == null)
                parent.Map.components.Add(new MapComponent_MEStorage(parent.Map));
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra())
                yield return g;
            // 选择物品
            yield return new Command_Action
            {
                defaultLabel = "选择限制物品",
                defaultDesc = "选择要设置限制的物品类型",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", true),
                action = () =>
                {
                    var network = parent.Map.GetComponent<MapComponent_MEStorage>();
                    if (network == null) return;
                    Find.WindowStack.Add(new Window_MEChoose(parent.Map, (def) =>
                    {
                        selectedDef = def;
                        var limit = network.GetItemLimit(def);
                        minLimit = limit.min;
                        maxLimit = limit.max;
                    }));
                }
            };

            if (selectedDef != null)
            {
                // 显示当前物品
                yield return new Command_Action
                {
                    defaultLabel = string.Format("当前物品: {0}", selectedDef.LabelCap),
                    defaultDesc = "",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", true),
                    action = null
                };

                // 设置下限
                yield return new Command_Action
                {
                    defaultLabel = string.Format("下限: {0}", minLimit >= 0 ? minLimit.ToString() : "无"),
                    defaultDesc = "点击设置库存下限（低于此值不输出）",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", true),
                    action = () =>
                    {
                        Find.WindowStack.Add(new Dialog_SetInteger("设置下限", "输入下限（-1表示无限制）", minLimit, (val) =>
                        {
                            minLimit = val;
                            ApplyLimit();
                        }));
                    }
                };

                // 设置上限
                yield return new Command_Action
                {
                    defaultLabel = string.Format("上限: {0}", maxLimit >= 0 ? maxLimit.ToString() : "无"),
                    defaultDesc = "点击设置库存上限（超过此值不存入）",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", true),
                    action = () =>
                    {
                        Find.WindowStack.Add(new Dialog_SetInteger("设置上限", "输入上限（-1表示无限制）", maxLimit, (val) =>
                        {
                            maxLimit = val;
                            ApplyLimit();
                        }));
                    }
                };

                // 清除限制
                yield return new Command_Action
                {
                    defaultLabel = "清除限制",
                    defaultDesc = "移除该物品的所有限制",
                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel", true),
                    action = () =>
                    {
                        minLimit = -1;
                        maxLimit = -1;
                        ApplyLimit();
                        selectedDef = null;
                    }
                };
            }
        }

        private void ApplyLimit()
        {
            var network = parent.Map.GetComponent<MapComponent_MEStorage>();
            if (network != null && selectedDef != null)
            {
                network.SetItemLimit(selectedDef, minLimit, maxLimit);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Defs.Look(ref selectedDef, "selectedDef");
            Scribe_Values.Look(ref minLimit, "minLimit", -1);
            Scribe_Values.Look(ref maxLimit, "maxLimit", -1);
        }
    }

    // 通用整数输入对话框
    public class Dialog_SetInteger : Window
    {
        private string title;
        private string prompt;
        private int currentValue;
        private Action<int> onConfirm;
        private string editBuffer;

        public Dialog_SetInteger(string title, string prompt, int initialValue, Action<int> onConfirm)
        {
            this.title = title;
            this.prompt = prompt;
            this.currentValue = initialValue;
            this.onConfirm = onConfirm;
            editBuffer = initialValue.ToString();
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(300, 150);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 30), title);

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0, 30, inRect.width, 30), prompt);

            GUI.SetNextControlName("IntField");
            editBuffer = Widgets.TextField(new Rect(0, 60, inRect.width, 30), editBuffer);

            if (Widgets.ButtonText(new Rect(0, 100, inRect.width / 2 - 5, 30), "确认"))
            {
                TryConfirm();
            }
            if (Widgets.ButtonText(new Rect(inRect.width / 2 + 5, 100, inRect.width / 2 - 5, 30), "取消"))
            {
                Close();
            }
        }

        private void TryConfirm()
        {
            if (int.TryParse(editBuffer, out int value))
            {
                onConfirm(value);
                Close();
            }
            else
            {
                Messages.Message("无效数字", MessageTypeDefOf.RejectInput);
            }
        }

        public override void OnAcceptKeyPressed()
        {
            TryConfirm();
        }
    }
}