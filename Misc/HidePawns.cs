// 1. GameComponent 保持不变
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace WastelandLizard
{
    public class GameComponent_HiddenPawns : GameComponent
    {
        private HashSet<int> hiddenPawnIDs = new HashSet<int>();
        private static GameComponent_HiddenPawns instance;

        public GameComponent_HiddenPawns(Game game) { instance = this; }

        public static GameComponent_HiddenPawns Instance => instance ??
            (Current.Game != null ? Current.Game.GetComponent<GameComponent_HiddenPawns>() : null);

        public void SetPawnHidden(Pawn pawn, bool hide)
        {
            if (hide)
                hiddenPawnIDs.Add(pawn.thingIDNumber);
            else
                hiddenPawnIDs.Remove(pawn.thingIDNumber);
            Find.ColonistBar?.MarkColonistsDirty();  // 关键：标记脏，强制重算
        }

        public bool IsPawnHidden(Pawn pawn) =>
            pawn != null && hiddenPawnIDs.Contains(pawn.thingIDNumber);

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref hiddenPawnIDs, "hiddenPawnIDs", LookMode.Value);
        }
    }

    // 2. 修正补丁：拦截 Entries 的获取（这才是核心！）
    [HarmonyPatch(typeof(ColonistBar), "get_Entries")]
    public static class Patch_ColonistBar_get_Entries
    {
        public static void Postfix(ref List<ColonistBar.Entry> __result)
        {
            var comp = GameComponent_HiddenPawns.Instance;
            if (comp == null || __result == null || __result.Count == 0) return;

            // 从 Entries 中移除隐藏的 pawn
            __result.RemoveAll(entry => comp.IsPawnHidden(entry.pawn));
        }
    }
}
