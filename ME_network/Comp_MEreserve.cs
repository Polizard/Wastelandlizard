using RimWorld;
using Verse;

namespace WastelandLizard
{
    public class CompProperties_MEreserve : CompProperties
    {
        public int capacity = 100;

        public CompProperties_MEreserve()
        {
            compClass = typeof(Comp_MEreserve);
        }
    }

    public class Comp_MEreserve : ThingComp
    {
        private CompProperties_MEreserve Props => (CompProperties_MEreserve)props;

        private int contributedAmount = 0;

        private MapComponent_MEStorage Network => parent.Map?.GetComponent<MapComponent_MEStorage>();

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            // 确保网络组件存在
            if (parent.Map.GetComponent<MapComponent_MEStorage>() == null)
                parent.Map.components.Add(new MapComponent_MEStorage(parent.Map));

            // 新建建筑时添加容量，从存档加载时不重复添加
            if (!respawningAfterLoad)
            {
                contributedAmount = Props.capacity;
                Network?.AddCapacity(contributedAmount);
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode)
        {
            base.PostDeSpawn(map, mode);

            // 建筑移除时撤回容量
            if (contributedAmount > 0)
            {
                Network?.RemoveCapacity(contributedAmount);
                contributedAmount = 0;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref contributedAmount, "contributedAmount", 0);
        }

        public override string CompInspectStringExtra()
        {
            string text = base.CompInspectStringExtra();
            text += "\n网络容量贡献: " + contributedAmount;
            return text.TrimStart('\n');
        }
    }
}