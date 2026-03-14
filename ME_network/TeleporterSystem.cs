using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WastelandLizard
{
    public class TeleporterNetworkManager : MapComponent
    {
        public Dictionary<int, Building_Teleporter> channelOutputs = new Dictionary<int, Building_Teleporter>();
        public Dictionary<int, List<Building_Teleporter>> channelInputs = new Dictionary<int, List<Building_Teleporter>>();
        public Dictionary<Building_Teleporter, (int channel, int slot)> inputSlots = new Dictionary<Building_Teleporter, (int, int)>();
        public TeleporterNetworkManager(Map map) : base(map) { }

        private Building_Teleporter GetOutputOrDefault(int channel)
        {
            channelOutputs.TryGetValue(channel, out Building_Teleporter result);
            return result;
        }

        public void OnTeleporterAdded(Building_Teleporter tp)
        {
            if (!tp.IsInput && tp.Channel > 0){TrySetOutput(tp.Channel, tp);}
            else if (tp.IsInput && tp.Channel > 0){AssignSlot(tp);}
        }

        public void OnTeleporterRemoved(Building_Teleporter tp)
        {
            if (!tp.IsInput && tp.Channel > 0)
            {
                if (channelOutputs.TryGetValue(tp.Channel, out Building_Teleporter existing) && existing == tp)
                {
                    channelOutputs.Remove(tp.Channel);
                    NotifyInputsOutputGone(tp.Channel);
                }
            }
            else if (tp.IsInput && tp.Channel > 0){RecycleSlot(tp);}
        }

        public void OnModeChanged(Building_Teleporter tp, bool oldIsInput)
        {
            if (!oldIsInput && tp.IsInput)
            {
                if (tp.Channel > 0 && channelOutputs.TryGetValue(tp.Channel, out Building_Teleporter existing) && existing == tp)
                {
                    channelOutputs.Remove(tp.Channel);
                    NotifyInputsOutputGone(tp.Channel);
                }
                AssignSlot(tp);
            }
            else if (oldIsInput && !tp.IsInput)
            {
                RecycleSlot(tp);
                if (tp.Channel > 0) TrySetOutput(tp.Channel, tp);
            }
        }

        public void OnChannelChanged(Building_Teleporter tp, int oldChannel)
        {
            int newChannel = tp.Channel;

            if (!tp.IsInput)
            {
                if (oldChannel > 0 && channelOutputs.TryGetValue(oldChannel, out Building_Teleporter existing) && existing == tp)
                {
                    channelOutputs.Remove(oldChannel);
                    NotifyInputsOutputGone(oldChannel);
                }
                if (newChannel > 0) TrySetOutput(newChannel, tp);
            }
            else
            {
                if (oldChannel > 0) RecycleSlot(tp, oldChannel);
                if (newChannel > 0) AssignSlot(tp);
                else tp.SetSlotInfo(0, null);
            }
        }

        private void AssignSlot(Building_Teleporter input)
        {
            int channel = input.Channel;

            if (!channelInputs.ContainsKey(channel))
                channelInputs[channel] = new List<Building_Teleporter>();

            var list = channelInputs[channel];

            int newSlot = 1;
            for (int i = 1; i <= 60; i++)
            {
                if (i > list.Count || list[i - 1] == null)
                {
                    newSlot = i;
                    break;
                }
            }

            if (newSlot > 60)
            {
                input.SetSlotInfo(0, null);
                Messages.Message(string.Format("频道 {0} 已有60个输入，该传送器被禁用", channel), input, MessageTypeDefOf.NegativeEvent);
                return;
            }

            if (newSlot <= list.Count)
                list[newSlot - 1] = input;
            else
                list.Add(input);

            inputSlots[input] = (channel, newSlot);

            var output = GetOutputOrDefault(channel);
            input.SetSlotInfo(newSlot, output);
        }

        private void RecycleSlot(Building_Teleporter input, int? specificChannel = null)
        {
            int ch = specificChannel ?? input.Channel;

            if (inputSlots.TryGetValue(input, out var info))
            {
                int slot = info.slot;

                if (channelInputs.TryGetValue(ch, out var list) && slot > 0 && slot <= list.Count)
                {
                    list[slot - 1] = null;

                    while (list.Count > 0 && list[list.Count - 1] == null)
                        list.RemoveAt(list.Count - 1);
                }

                inputSlots.Remove(input);
            }

            input.SetSlotInfo(0, null);
        }

        private void TrySetOutput(int channel, Building_Teleporter tp)
        {
            if (channelOutputs.ContainsKey(channel))
            {
                tp.SetChannel(0);
                Messages.Message(string.Format("频道 {0} 已有一个输出", channel), tp, MessageTypeDefOf.NegativeEvent);
                return;
            }

            channelOutputs[channel] = tp;

            if (channelInputs.TryGetValue(channel, out var inputs))
            {
                foreach (var input in inputs.Where(i => i != null))
                    input.SetTargetOutput(tp);
            }
        }

        private void NotifyInputsOutputGone(int channel)
        {
            if (channelInputs.TryGetValue(channel, out var inputs))
            {
                foreach (var input in inputs.Where(i => i != null))
                    input.SetTargetOutput(null);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // 如果需要存档，请在此处添加序列化逻辑
        }
    }
    public class Building_Teleporter : Building_Storage
    {
        public bool IsInput = true;
        public int Channel = 0;
        public int TimeSlot = 0;
        public Building_Teleporter TargetOutput;

        public bool IsOutput => !IsInput;

        private bool cachedIsInput;
        private int cachedChannel;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                var manager = map.GetComponent<TeleporterNetworkManager>();
                if (manager == null)
                {
                    manager = new TeleporterNetworkManager(map);
                    map.components.Add(manager);
                }
                manager.OnTeleporterAdded(this);
            });
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Map.GetComponent<TeleporterNetworkManager>()?.OnTeleporterRemoved(this);
            base.DeSpawn(mode);
        }

        protected override void Tick()
        {
            base.Tick();

            if (IsInput || TimeSlot <= 0 || TargetOutput == null || TargetOutput.Destroyed)
                return;

            int currentTickInSecond = (Find.TickManager.TicksGame % 60) + 1;

            if (currentTickInSecond == TimeSlot)
            {
                TryTransfer();
            }
        }
        private void TryTransfer()
        {
            var slotGroup = GetSlotGroup();
            if (slotGroup == null) return;

            foreach (var cell in slotGroup.CellsList)
            {
                var things = cell.GetThingList(Map);
                foreach (var thing in things.ToList())
                {
                    if (thing.def.category != ThingCategory.Item) continue;

                    if (TryTeleport(thing, TargetOutput))
                    {
                        return;
                    }
                }
            }
        }

        private bool TryTeleport(Thing thing, Building_Teleporter target)
        {
            var targetGroup = target.GetSlotGroup();
            if (targetGroup == null) return false;

            foreach (var cell in targetGroup.CellsList)
            {
                int space = thing.def.stackLimit;
                var existing = cell.GetFirstItem(Map);

                if (existing != null)
                {
                    if (existing.def != thing.def) continue;
                    space -= existing.stackCount;
                }

                int count = Mathf.Min(thing.stackCount, space);
                if (count <= 0) continue;

                var moved = count == thing.stackCount ? thing : thing.SplitOff(count);

                if (existing != null && existing.def == moved.def)
                {
                    existing.TryAbsorbStack(moved, true);
                }
                else
                {
                    moved.DeSpawn(DestroyMode.Vanish);
                    GenPlace.TryPlaceThing(moved, cell, Map, ThingPlaceMode.Direct);
                }

                FleckMaker.ThrowLightningGlow(thing.Position.ToVector3Shifted(), Map, 0.3f);
                FleckMaker.ThrowLightningGlow(cell.ToVector3Shifted(), Map, 0.3f);

                return true;
            }
            return false;
        }

        public void SetSlotInfo(int slot, Building_Teleporter output)
        {
            TimeSlot = slot;
            TargetOutput = output;
        }

        public void SetTargetOutput(Building_Teleporter output)
        {
            TargetOutput = output;
        }

        public void SetChannel(int newChannel)
        {
            if (Channel == newChannel) return;
            int old = Channel;
            Channel = newChannel;
            cachedChannel = newChannel;
            Map.GetComponent<TeleporterNetworkManager>()?.OnChannelChanged(this, old);
        }

        public void ToggleMode()
        {
            bool old = IsInput;
            IsInput = !IsInput;
            cachedIsInput = IsInput;
            Map.GetComponent<TeleporterNetworkManager>()?.OnModeChanged(this, old);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos()) yield return g;

            yield return new Command_Action
            {
                defaultLabel = IsInput ? "模式: 输入" : "模式: 输出",
                defaultDesc = "点击切换输入/输出模式",
                icon = IsInput
                    ? ContentFinder<Texture2D>.Get("UI/Designators/ZoneCreate_Stockpile")
                    : ContentFinder<Texture2D>.Get("UI/Designators/Tame"),
                action = ToggleMode
            };

            yield return new Command_Action
            {
                defaultLabel = string.Format("频道: {0}", Channel),
                defaultDesc = "点击输入频道编号",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/LaunchReport"),
                action = () => Find.WindowStack.Add(new Dialog_SetChannel(this))
            };

            if (IsInput)
            {
                string slotLabel = TimeSlot > 0
                    ? string.Format("时隙: {0}/60", TimeSlot)
                    : "无时隙";

                string targetDesc = TargetOutput != null
                    ? string.Format("目标: {0}", TargetOutput.Position)
                    : "无目标";

                yield return new Command_Action
                {
                    defaultLabel = slotLabel,
                    defaultDesc = targetDesc,
                    action = () => { }
                };
            }
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref IsInput, "isInput", true);
            Scribe_Values.Look(ref Channel, "channel", 0);
            Scribe_Values.Look(ref TimeSlot, "timeSlot", 0);
        }
    }
}