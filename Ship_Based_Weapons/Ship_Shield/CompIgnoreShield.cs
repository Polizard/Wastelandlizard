using Verse;

namespace WastelandLizard
{
    public class CompIgnoreShield : ThingComp { }
    public class CompProperties_IgnoreShield : CompProperties
    {
        public CompProperties_IgnoreShield() { this.compClass = typeof(CompIgnoreShield); }
    }
}