using System;
using System.IO;
using LCAnimationStudio.Bootstrap;

namespace LCAnimationStudio.Serialization
{
    public sealed class ProjectPathResolver
    {
        private readonly AnimationStudioSettings settings;

        public ProjectPathResolver(AnimationStudioSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public string ProjectRoot
        {
            get
            {
                string configured = string.IsNullOrWhiteSpace(settings.ProjectRoot) ? LCAnimationStudioMod.RootDir : settings.ProjectRoot;
                return Path.GetFullPath(configured);
            }
        }

        public string Resolve(string defName)
        {
            string mapped;
            if (settings.DefFileMappings.TryGetValue(defName, out mapped) && !string.IsNullOrWhiteSpace(mapped))
                return Authorize(mapped);
            return Authorize(Path.Combine(ProjectRoot, "1.7", "Defs", "AnimationDefs", defName + ".xml"));
        }

        public string Authorize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A project XML path is required.", nameof(path));
            string full = Path.GetFullPath(path);
            string root = ProjectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string allowed = Path.Combine(ProjectRoot, "1.7", "Defs", "AnimationDefs")
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("The path is outside the configured project root.");
            if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Animation XML must be under 1.7/Defs/AnimationDefs.");
            if (!string.Equals(Path.GetExtension(full), ".xml", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Animation exports must use an .xml file.");
            return full;
        }

        public void Remember(string defName, string path)
        {
            settings.DefFileMappings[defName] = Authorize(path);
            LCAnimationStudioMod.Instance.WriteSettings();
        }
    }
}
