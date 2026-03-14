using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace WastelandLizard
{
    public static class MEUtility
    {
        public static List<Thing> ScanForItems(Thing parent, int radius)
        {
            var result = new List<Thing>();
            var center = parent.Position;
            var map = parent.Map;

            for (int x = -radius; x <= radius; x++)
            {
                for (int z = -radius; z <= radius; z++)
                {
                    IntVec3 cell = new IntVec3(center.x + x, 0, center.z + z);
                    if (!cell.InBounds(map)) continue;
                    if (cell == parent.Position) continue;

                    var things = cell.GetThingList(map)
                        .Where(t => t.def.category == ThingCategory.Item && !t.IsForbidden(Faction.OfPlayer))
                        .ToList();
                    result.AddRange(things);
                }
            }
            return result;
        }
        public static IntVec3 GetRotatedDropPosition(Thing parent, Vector3 localOffset)
        {
            Rot4 rot = parent.Rotation;
            Vector3 worldOffset;

            if (rot == Rot4.North)
                worldOffset = localOffset;
            else if (rot == Rot4.East)
                worldOffset = new Vector3(localOffset.z, localOffset.y, -localOffset.x);
            else if (rot == Rot4.South)
                worldOffset = new Vector3(-localOffset.x, localOffset.y, -localOffset.z);
            else if (rot == Rot4.West)
                worldOffset = new Vector3(-localOffset.z, localOffset.y, localOffset.x);
            else
                worldOffset = localOffset;
            return (parent.DrawPos + worldOffset).ToIntVec3();
        }
    }
}