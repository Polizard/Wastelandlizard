using System.Collections.Generic;
using System.Linq;
using Verse;

namespace WastelandLizard
{
    // 用于存储单个物品限制的辅助类（支持Scribe）
    public class ItemLimit : IExposable
    {
        public ThingDef def;
        public int min = -1;
        public int max = -1;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref min, "min", -1);
            Scribe_Values.Look(ref max, "max", -1);
        }
    }

    public class MapComponent_MEStorage : MapComponent
    {
        private object lockObj = new object();
        private Dictionary<ThingDef, int> networkContents = new Dictionary<ThingDef, int>();
        private int totalItemCount = 0;
        private int totalCapacity = 0;

        // 物品限制相关
        private List<ItemLimit> itemLimitsList = new List<ItemLimit>();
        private Dictionary<ThingDef, ItemLimit> itemLimitsDict = new Dictionary<ThingDef, ItemLimit>();

        public MapComponent_MEStorage(Map map) : base(map) { }

        // ---------- 容量管理 ----------
        public void AddCapacity(int amount)
        {
            if (amount <= 0) return;
            lock (lockObj)
            {
                totalCapacity += amount;
            }
        }

        public void RemoveCapacity(int amount)
        {
            if (amount <= 0) return;
            lock (lockObj)
            {
                totalCapacity -= amount;
                if (totalCapacity < 0) totalCapacity = 0;
            }
        }

        public int CapacityLimit
        {
            get
            {
                lock (lockObj)
                {
                    return totalCapacity;
                }
            }
        }

        // ---------- 物品管理 ----------
        public int GetCount(ThingDef def)
        {
            lock (lockObj)
            {
                return networkContents.TryGetValue(def, out int count) ? count : 0;
            }
        }

        public bool Has(ThingDef def, int count)
        {
            lock (lockObj)
            {
                return networkContents.TryGetValue(def, out int existing) && existing >= count;
            }
        }

        public bool TryConsume(ThingDef def, int count)
        {
            lock (lockObj)
            {
                if (!networkContents.TryGetValue(def, out int existing) || existing < count)
                    return false;
                if (existing == count)
                    networkContents.Remove(def);
                else
                    networkContents[def] = existing - count;
                totalItemCount -= count;
                return true;
            }
        }

        public bool CanAdd(int count)
        {
            lock (lockObj)
            {
                return totalItemCount + count <= totalCapacity;
            }
        }

        public void Add(ThingDef def, int count)
        {
            if (!CanAdd(count))
                return;

            lock (lockObj)
            {
                if (networkContents.ContainsKey(def))
                    networkContents[def] += count;
                else
                    networkContents[def] = count;
                totalItemCount += count;
            }
        }

        public Dictionary<ThingDef, int> GetAllContents()
        {
            lock (lockObj)
            {
                return new Dictionary<ThingDef, int>(networkContents);
            }
        }

        public int GetTotalItemCount() => totalItemCount;

        // ---------- 限制管理 ----------
        public void SetItemLimit(ThingDef def, int min, int max)
        {
            lock (lockObj)
            {
                if (min < 0 && max < 0)
                {
                    // 移除限制
                    if (itemLimitsDict.TryGetValue(def, out var existing))
                    {
                        itemLimitsList.Remove(existing);
                        itemLimitsDict.Remove(def);
                    }
                }
                else
                {
                    if (itemLimitsDict.TryGetValue(def, out var existing))
                    {
                        existing.min = min;
                        existing.max = max;
                    }
                    else
                    {
                        var newLimit = new ItemLimit { def = def, min = min, max = max };
                        itemLimitsList.Add(newLimit);
                        itemLimitsDict[def] = newLimit;
                    }
                }
            }
        }

        public (int min, int max) GetItemLimit(ThingDef def)
        {
            lock (lockObj)
            {
                if (itemLimitsDict.TryGetValue(def, out var limit))
                    return (limit.min, limit.max);
                return (-1, -1);
            }
        }

        public Dictionary<ThingDef, (int min, int max)> GetAllLimits()
        {
            lock (lockObj)
            {
                var result = new Dictionary<ThingDef, (int, int)>();
                foreach (var kv in itemLimitsDict)
                    result[kv.Key] = (kv.Value.min, kv.Value.max);
                return result;
            }
        }

        // ---------- 存档 ----------
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref networkContents, "networkContents", LookMode.Def, LookMode.Value);
            Scribe_Values.Look(ref totalItemCount, "totalItemCount", 0);
            Scribe_Values.Look(ref totalCapacity, "totalCapacity", 0);
            Scribe_Collections.Look(ref itemLimitsList, "itemLimits", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                itemLimitsDict.Clear();
                foreach (var limit in itemLimitsList)
                    itemLimitsDict[limit.def] = limit;
            }
        }
    }
}