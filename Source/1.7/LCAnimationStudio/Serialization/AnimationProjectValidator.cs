using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Verse;

namespace LCAnimationStudio.Serialization
{
    public sealed class AnimationProjectValidator
    {
        private readonly AnimationSchemaValidator validator = new AnimationSchemaValidator();

        public IReadOnlyList<ValidationIssue> ValidateProject(string projectRoot)
        {
            var issues = new List<ValidationIssue>();
            string directory = Path.Combine(Path.GetFullPath(projectRoot), "1.6", "Defs", "AnimationDefs");
            if (!Directory.Exists(directory))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, directory, null, null, null, "AnimationDefs directory does not exist."));
                return issues;
            }
            var tags = new HashSet<string>(DefDatabase<PawnRenderNodeTagDef>.AllDefsListForReading.Select(def => def.defName), StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(directory, "*.xml", SearchOption.AllDirectories)
                .Where(item => !item.EndsWith(".lcas.xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                try { issues.AddRange(validator.Validate(XDocument.Load(path, LoadOptions.SetLineInfo), path, tags)); }
                catch (Exception exception) { issues.Add(new ValidationIssue(ValidationSeverity.Error, path, null, null, null, "XML parse failed: " + exception.Message)); }
            }
            return issues;
        }

        public IReadOnlyList<ValidationIssue> ValidateSiblingProjects(string projectRoot)
        {
            string root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string modsDirectory = Directory.GetParent(root)?.FullName;
            if (modsDirectory == null || !Directory.Exists(modsDirectory)) return ValidateProject(root);
            var issues = new List<ValidationIssue>();
            foreach (string directory in Directory.GetDirectories(modsDirectory).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                string animationDirectory = Path.Combine(directory, "1.6", "Defs", "AnimationDefs");
                if (Directory.Exists(animationDirectory)) issues.AddRange(ValidateProject(directory));
            }
            return issues;
        }
    }
}
