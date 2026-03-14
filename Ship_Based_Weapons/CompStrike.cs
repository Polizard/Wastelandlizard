using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;


namespace WastelandLizard
{
    [StaticConstructorOnStartup]
    public class CompStrike : ThingComp
    {
        //光束轰炸
        public CompProps_Strike Props
        {get { return props as CompProps_Strike; }}
        private float GetAmp()
        {
            if (this.parent is SaveOurShip2.Building_ShipTurret t && t.spinalComp != null)
            {return Math.Min(t.AmplifierDamageBonus+1,50);}
            else { return 1;}
        }
        //获取脊峰炮的放大倍数
        //也许我应该另外写一个大号的增幅器，到时候这里可能要改
        //总不能有能量颠佬叠50个顶级放大器吧
        private void RemoveEnergy(float EnergyToRemove)
        {
            CompPowerBattery Battery = parent.GetComp<CompPowerBattery>();
            Battery.DrawPower(EnergyToRemove);
        }
        //我为什么要把扣除电能单独拿出来写？？？

        //判断能不能开火
        private bool CanFire(out string reason)
        {
            reason = null;
            // 检查电力，先看看有没有电网，再看看有没有足够的电
            //激光脊峰的默认energytofire是720，我假设未来出现的脊峰炮energytofire都不大于1500
            //储电能力storedEnergyMax要够打一发50倍，也就是说我可能要写能存75000电的脊峰
            //太疯狂了
            if (this.parent != null && !((Building)this.parent).TryGetComp<CompPowerTrader>().PowerOn)
            {
                reason = "NoPower";// 或自定义翻译键
                return false;
            }
            if (this.parent.GetComp<CompPowerBattery>().StoredEnergy <= Props.EnergyCost)
            {
                reason = "InsufficientEnergy"; // 需要添加翻译
                return false;
            }
            //看看是不是口袋空间
            //买不起异象的作者完全没有想到这个
            if (this.parent.Map.IsPocketMap)
            {
                reason = "CannotUseInPocketMap"; // 需要添加翻译
                return false;
            }
            return true;
        }


        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) { yield return gizmo; }
            //通过遍历基类的 CompGetGizmosExtra 方法返回的所有 Gizmo 对象，并将它们逐个返回，确保了在扩展功能时保留基类的原有功能。
            string disableReason = null;
            bool canFire = CanFire(out disableReason);

            yield return new Command_ActionWithCooldown
            {
                //icon = TextureCache.iconBomb,
                //得想办法整个图标
                defaultLabel = "defaultLabel",
                defaultDesc = canFire ? "defaultDesc" : disableReason,
                // 如果不能发射，显示原因
                //得想办法编点描述
                Disabled = !canFire,
                disabledReason = disableReason,
                action = delegate
                {
                    if (!CanFire(out string failReason)){Messages.Message(failReason, MessageTypeDefOf.RejectInput); return; }
                    //在动作内部再次检查，防止通过快捷键触发
                    //if (this.coolDowntime <= 0 && Event.current.button == 0)
                    //如果冷却时间已经达到0 并且 此时按下左键
                    if (Event.current.button == 0)
                    //只需要按下左键
                    {
                        List<FloatMenuOption> list = new List<FloatMenuOption>();
                        //生成浮动菜单
                        using (List<Map>.Enumerator enumerator2 = Find.Maps.GetEnumerator())
                        {
                            while (enumerator2.MoveNext())
                            //MoveNext()是bool，如果有下一个就true
                            {
                                Map map = enumerator2.Current;
                                if (!map.IsPocketMap && map != this.parent.Map && !MapInVac(map))
                                //不能用于口袋空间 和 原地图 和 太空地图
                                {
                                    list.Add(new FloatMenuOption(map.info.parent.Label, delegate
                                    {
                                        //list
                                        Current.Game.CurrentMap = map;
                                        Targeter targeter = Find.Targeter;
                                        TargetingParameters targetingParameters = this.targetingParameters;
                                        //下面是轨道轰炸的参数
                                        Action<LocalTargetInfo> action = null;
                                        if (action == null)
                                        {
                                            action = (targetinfo) =>
                                            {
                                                if(parent.GetComp<SaveOurShip2.CompShipHeat>().AddHeatToNetwork(Props.HeatCost))
                                                {
                                                    int amp = (int)GetAmp();
                                                    //amp转换成int才能用
                                                    RemoveEnergy(Props.EnergyCost);
                                                    //扣电
                                                    PowerBeam powerBeam = (PowerBeam)GenSpawn.Spawn(ThingDefOf.PowerBeam, targetinfo.Cell, map, WipeMode.Vanish);
                                                    powerBeam.instigator = null;
                                                    powerBeam.duration = 600;
                                                    //持续时间10s
                                                    //其实考虑到伤害机制应该短一点的
                                                    //但是再短就不够玩家看着光束说“如今我成了死神,世界的毁灭者”了
                                                    powerBeam.StartStrike();
                                                    //Messages.Message("OrbitalStrikeMessage".Translate(powerBeam.LabelCap, map.info.parent.Label), MessageTypeDefOf.PositiveEvent, true);
                                                    GenExplosion.DoExplosion(targetinfo.Cell, map, Props.BeamRadius, DamageDefOf.Flame, null, Props.BeamDamage*amp, armorPenetration: -1f, chanceToStartFire: 0.3f, postExplosionGasAmount: 0, excludeRadius: 0f, doSoundEffects: true);
                                                    //靠光束本身造成不了多少伤害，制造爆炸补一下伤害
                                                    //我预料到会有能量颠佬叠50的amp制造一场毁天灭地的爆炸
                                                    //“如今我成了死神,世界的毁灭者”
                                                    //this.coolDowntime = 60;
                                                    //冷却复位
                                                    //Log.Message("The number is: " + GetAmp());
                                                    //读取amp
                                                }
                                            };
                                        }

                                        Action<LocalTargetInfo> action2 = null;
                                        if (action2 == null)
                                        {
                                            action2 = (targetInfo) =>
                                            {
                                                if (!map.fogGrid.IsFogged(targetInfo.Cell))
                                                //战争迷雾
                                                {
                                                    GenDraw.DrawTargetHighlight(targetInfo);
                                                    GenDraw.DrawRadiusRing(targetInfo.Cell, Props.BeamRadius);

                                                }
                                            };
                                        }

                                        Func<LocalTargetInfo, bool> func = null;
                                        if (func == null)
                                        {
                                            func = (targetInfo) =>
                                            {
                                                if (targetInfo.IsValid && targetInfo.Cell.InBounds(map) && !map.fogGrid.IsFogged(targetInfo.Cell))
                                                {
                                                    return true;
                                                }
                                                Messages.Message("LocationInvalid".Translate(), MessageTypeDefOf.RejectInput, false);
                                                return false;
                                            };
                                        }

                                        targeter.BeginTargeting(targetingParameters, action, action2, func, null, null, null, true, null, null);
                                    }, MenuOptionPriority.Default, null, null, 0f, null, null, true, 0));
                                    Find.WindowStack.Add(new FloatMenu(list));
                                }
                            }
                        }
                        if (list.Count <= 0)
                        {
                            list.Add(new FloatMenuOption("OrbitalStrikeNoOtherMap".Translate(), null, MenuOptionPriority.DisabledOption, null, null, 0f, null, null, true, 0));
                            Find.WindowStack.Add(new FloatMenu(list));
                            return;
                        }
                    }
                },
                //cooldownPercentGetter = () => Mathf.InverseLerp((float)(60), 0f, (float)this.coolDowntime),
                defaultIconColor = Color.white
            };
            yield break;
        }
        //接下来的东西没啥意思
        private bool MapInVac(Map map)
        { return (map.weatherManager.curWeather == WeatherDef.Named("OuterSpaceWeather"));}
        //太空地图判断方法
        //public override void CompTick()
        //{base.CompTick();if(this.coolDowntime>0){this.coolDowntime--;}}
        //冷却恢复
        //private int coolDowntime;
        private TargetingParameters targetingParameters = new TargetingParameters
        {canTargetLocations = true,canTargetBuildings = true,canTargetPawns = true};
        //可选目标类型
    }
}
