using UnityEngine;
using Verse;

namespace LCAnimationStudio.Bootstrap
{
    public sealed class LCAnimationStudioMod : Mod
    {
        public static string RootDir { get; private set; }
        public static LCAnimationStudioMod Instance { get; private set; }
        public static AnimationStudioSettings Settings { get; private set; }

        public LCAnimationStudioMod(ModContentPack content) : base(content)
        {
            RootDir = content.RootDir;
            Instance = this;
            Settings = GetSettings<AnimationStudioSettings>();
            if (string.IsNullOrWhiteSpace(Settings.ProjectRoot)) Settings.ProjectRoot = RootDir;
            Log.Message("[LC Animation Studio] RimWorld 1.6 tools loaded.");
        }

        public override string SettingsCategory() => "LC Animation Studio";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.Label("Authorized project root (exports are restricted to its 1.6/Defs/AnimationDefs folder):");
            Settings.ProjectRoot = listing.TextEntry(Settings.ProjectRoot ?? RootDir);
            listing.Gap();
            listing.CheckboxLabeled("Watch mapped XML files for external changes", ref Settings.WatchExternalChanges);
            Settings.WatchDebounceMilliseconds = (int)listing.SliderLabeled(
                "File watcher debounce: " + Settings.WatchDebounceMilliseconds + " ms",
                Settings.WatchDebounceMilliseconds, 300f, 1000f);
            listing.Label("Def-to-file mappings are recorded after a successful import or export.");
            if (listing.ButtonText("Reset project root to this Studio mod")) Settings.ProjectRoot = RootDir;
            listing.End();
        }
    }
}
