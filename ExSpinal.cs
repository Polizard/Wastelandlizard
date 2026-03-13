using HarmonyLib;
using SaveOurShip2;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
//这个文件就用来放所有的harmony
namespace WastelandLizard
{
    //这个是Harmony的初始化，不要再重复了
    [StaticConstructorOnStartup]
    public static class HarmonyInit
    {
        static HarmonyInit()
        {
            var harmony = new Harmony("Wastelandlizard_harmony");
            harmony.PatchAll();
        }
    }

    //这是替代脊峰成型逻辑的
    [HarmonyPatch(typeof(Building_ShipTurret), "SpinalRecalc")]
    public static class Patch_SpinalRecalc
    {
        public static readonly FieldInfo ampsField = typeof(Building_ShipTurret).GetField("amps", BindingFlags.NonPublic | BindingFlags.Instance);
        public static readonly FieldInfo spinalCompField = typeof(Building_ShipTurret).GetField("spinalComp", BindingFlags.Public | BindingFlags.Instance);
        public static readonly FieldInfo amplifierCountField = typeof(Building_ShipTurret).GetField("AmplifierCount", BindingFlags.Public | BindingFlags.Instance)
            ?? typeof(Building_ShipTurret).GetField("AmplifierCount", BindingFlags.NonPublic | BindingFlags.Instance);
        public static readonly FieldInfo amplifierDamageBonusField = typeof(Building_ShipTurret).GetField("AmplifierDamageBonus", BindingFlags.Public | BindingFlags.Instance)
            ?? typeof(Building_ShipTurret).GetField("AmplifierDamageBonus", BindingFlags.NonPublic | BindingFlags.Instance);

        public static bool Prefix(Building_ShipTurret __instance)
        {
            if (__instance == null) return true;
            try
            {
                __instance.SpinalRecalc_New();
            }
            catch (System.Exception ex)
            {
                Log.Error("[WLxSpinalPatch] 异常: " + ex);
                return true;
            }

            return false;
        }
    }

    public static class SpinalRecalcExtension
    {
        public static void SpinalRecalc_New(this Building_ShipTurret __instance)
        {
            List<Thing> amps = (List<Thing>)Patch_SpinalRecalc.ampsField.GetValue(__instance);
            CompSpinalMount spinalComp = (CompSpinalMount)Patch_SpinalRecalc.spinalCompField.GetValue(__instance);

            int AmplifierCount = (int)(Patch_SpinalRecalc.amplifierCountField?.GetValue(__instance) ?? -1);
            //Log.Message("[WLxSpinalPatch] 当前 AmplifierCount=" + AmplifierCount);

            if (spinalComp == null) { return; }
            if (AmplifierCount != -1 && amps != null && amps.All((Thing a) => a != null && !a.Destroyed)){return;}
            if (amps == null){return;}

            amps.Clear();
            AmplifierCount = -1;
            float num = 0f;
            bool flag = false;
            Thing thing = __instance;

            int mtstep = (thing.def.size.x >= 10) ? 5 : 1;

            IntVec3 intVec_u = ((__instance.Rotation.AsByte == 0) ? new IntVec3(0, 0, -1) : ((__instance.Rotation.AsByte == 1) ? new IntVec3(-1, 0, 0) : ((__instance.Rotation.AsByte != 2) ? new IntVec3(1, 0, 0) : new IntVec3(0, 0, 1))));
            IntVec3 intVec = new IntVec3(intVec_u.x * mtstep, 0, intVec_u.z * mtstep);
            IntVec3 intVec2 = thing.Position + intVec;
            //修正一下
            if (mtstep == 5) { intVec2 = intVec2 - intVec_u; }

            int loopCount = 0;
            do
            {
                loopCount++;
                if (loopCount > 100){break;}

                intVec2 += intVec;
                thing = intVec2.GetFirstThingWithComp<CompSpinalMount>(__instance.Map);

                if (thing == null)
                {
                    AmplifierCount = -1;
                    break;
                }

                CompSpinalMount compSpinalMount = thing.TryGetComp<CompSpinalMount>();

                if (thing.Rotation != __instance.Rotation)
                {
                    AmplifierCount = -1;
                    break;
                }

                if (thing.def.size.x < mtstep)
                {
                    AmplifierCount = -1;
                    break;
                }

                if (thing.Position == intVec2)
                {
                    amps.Add(thing);
                    AmplifierCount++;
                    num += compSpinalMount.Props.ampAmount;
                    compSpinalMount.SetColor(spinalComp.Props.color);
                }
                else if (thing.Position == intVec2 + intVec && compSpinalMount.Props.stackEnd)
                {
                    amps.Add(thing);
                    AmplifierCount++;
                    flag = true;
                }
                else
                {
                    AmplifierCount = -1;
                    flag = true;
                }
            }
            while (!flag);
            Patch_SpinalRecalc.amplifierCountField?.SetValue(__instance, AmplifierCount);
            if (num > 0f)
            {
                Patch_SpinalRecalc.amplifierDamageBonusField?.SetValue(__instance, num);
            }
        }
    }
}