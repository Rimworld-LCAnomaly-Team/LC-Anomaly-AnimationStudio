using System.Collections.Generic;
using Verse;

namespace LCAnimationStudio.Bootstrap
{
    public sealed class AnimationStudioSettings : ModSettings
    {
        public string ProjectRoot;
        public Dictionary<string, string> DefFileMappings = new Dictionary<string, string>();
        public bool WatchExternalChanges = true;
        public int WatchDebounceMilliseconds = 400;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref ProjectRoot, "projectRoot");
            Scribe_Values.Look(ref WatchExternalChanges, "watchExternalChanges", true);
            Scribe_Values.Look(ref WatchDebounceMilliseconds, "watchDebounceMilliseconds", 400);
            Scribe_Collections.Look(ref DefFileMappings, "defFileMappings", LookMode.Value, LookMode.Value);
            DefFileMappings = DefFileMappings ?? new Dictionary<string, string>();
        }
    }
}
