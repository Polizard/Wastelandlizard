using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MyMod
{
    // ========== 管理器 ==========
    public class TeleporterNetworkManager : MapComponent
    {
        public Dictionary<int, Building_Teleporter> channelOutputs = new Dictionary<int, Building_Teleporter>();
        public Dictionary<int, List<Building_Teleporter>> channelInputs = new Dictionary<int, List<Building_Teleporter>>();
        public Dictionary<Building_Teleporter, (int channel, int slot)> inputSlots = new Dictionary<Building_Teleporter, (int, int)>();

        public TeleporterNetworkManager(Map map) : base(map) { }

        private Building_Teleporter GetOutputOrDefault(int channel)
        {
            Building_Teleporter result;
            channelOutputs.TryGetValue(channel, out result);
            return result;
        }

        public void OnTeleporterAdded(Building_Teleporter tp)
        {
            if (!tp.IsInput && tp.Channel > 0)
            {
                TrySetOutput(tp.Channel, tp);
            }
            else if (tp.IsInput && tp.Channel > 0)
            {
                AssignSlot(tp);
            }
        }

        public void OnTeleporterRemoved(Building_Teleporter tp)
        {
            if (!tp.IsInput && tp.Channel > 0)
            {
                Building_Teleporter existing;
                if (channelOutputs.TryGetValue(tp.Channel, out existing) && existing == tp)
                {
                    channelOutputs.Remove(tp.Channel);
                    NotifyInputsOutputGone(tp.Channel);
                }
            }
            else if (tp.IsInput && tp.Channel > 0)
            {
                RecycleSlot(tp);
            }
        }

        public void OnModeChanged(Building_Teleporter tp, bool oldIsInput)
        {
            if (!oldIsInput && tp.IsInput)
            {
                if (tp.Channel > 0)
                {
                    Building_Teleporter existing;
                    if (channelOutputs.TryGetValue(tp.Channel, out existing) && existing == tp)
                    {
                        channelOutputs.Remove(tp.Channel);
                        NotifyInputsOutputGone(tp.Channel);
                    }
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
                if (oldChannel > 0)
                {
                    Building_Teleporter existing;
                    if (channelOutputs.TryGetValue(oldChannel, out existing) && existing == tp)
                    {
                        channelOutputs.Remove(oldChannel);
                        NotifyInputsOutputGone(oldChannel);
                    }
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

        void AssignSlot(Building_Teleporter input)
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
                Messages.Message(string.Format("Channel {0} has 60 inputs, this one disabled", channel), input, MessageTypeDefOf.NegativeEvent);
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

        void RecycleSlot(Building_Teleporter input, int? specificChannel = null)
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

        void TrySetOutput(int channel, Building_Teleporter tp)
        {
            if (channelOutputs.ContainsKey(channel))
            {
                tp.SetChannel(0);
                Messages.Message(string.Format("Channel {0} already has output", channel), tp, MessageTypeDefOf.NegativeEvent);
                return;
            }

            channelOutputs[channel] = tp;

            if (channelInputs.TryGetValue(channel, out var inputs))
            {
                foreach (var input in inputs.Where(i => i != null))
                    input.SetTargetOutput(tp);
            }
        }

        void NotifyInputsOutputGone(int channel)
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
        }
    }

    // ========== 数字输入对话框 ==========
    public class Dialog_SetChannel : Window
    {
        private Building_Teleporter teleporter;
        private string editBuffer;

        public Dialog_SetChannel(Building_Teleporter tp)
        {
            teleporter = tp;
            editBuffer = tp.Channel.ToString();
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 30), "Set Channel (0-99)");

            Text.Font = GameFont.Small;
            GUI.SetNextControlName("ChannelField");

            editBuffer = GUI.TextField(new Rect(0, 40, inRect.width, 30), editBuffer);

            if (GUI.Button(new Rect(0, 80, inRect.width / 2 - 5, 30), "Confirm"))
            {
                TrySetChannel();
            }

            if (GUI.Button(new Rect(inRect.width / 2 + 5, 80, inRect.width / 2 - 5, 30), "Cancel"))
            {
                Close();
            }
        }

        void TrySetChannel()
        {
            int value;
            if (int.TryParse(editBuffer, out value))
            {
                value = Mathf.Clamp(value, 0, 99);
                teleporter.SetChannel(value);
                Close();
            }
            else
            {
                Messages.Message("Invalid number", MessageTypeDefOf.RejectInput);
            }
        }

        public override void OnAcceptKeyPressed()
        {
            TrySetChannel();
        }

        public override Vector2 InitialSize => new Vector2(300, 150);
    }

    // ========== 建筑 ==========
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

            // 无论是否从存档加载，都要注册（延迟确保MapComponent已初始化）
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

        void TryTransfer()
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

        bool TryTeleport(Thing thing, Building_Teleporter target)
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
                defaultLabel = IsInput ? "Mode: Input" : "Mode: Output",
                defaultDesc = "Click to toggle Input/Output mode",
                icon = IsInput
                    ? ContentFinder<Texture2D>.Get("UI/Designators/ZoneCreate_Stockpile")
                    : ContentFinder<Texture2D>.Get("UI/Designators/Tame"),
                action = ToggleMode
            };

            yield return new Command_Action
            {
                defaultLabel = string.Format("Channel: {0}", Channel),
                defaultDesc = "Click to enter channel number",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/LaunchReport"),
                action = () =>
                {
                    Find.WindowStack.Add(new Dialog_SetChannel(this));
                }
            };

            if (IsInput)
            {
                string slotLabel = TimeSlot > 0
                    ? string.Format("Slot: {0}/60", TimeSlot)
                    : "No Slot";

                string targetDesc = TargetOutput != null
                    ? string.Format("Target: {0}", TargetOutput.Position)
                    : "No target";

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