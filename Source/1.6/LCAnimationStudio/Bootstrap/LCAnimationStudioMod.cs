using Verse;

namespace LCAnimationStudio.Bootstrap
{
    public sealed class LCAnimationStudioMod : Mod
    {
        public static string RootDir { get; private set; }

        public LCAnimationStudioMod(ModContentPack content) : base(content)
        {
            RootDir = content.RootDir;
            Log.Message("[LC Animation Studio] RimWorld 1.6 tools loaded.");
        }
    }
}
