using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace LCAnimationStudio.Serialization
{
    public sealed class AnimationSchemaValidator
    {
        private static readonly string[] VectorFields = { "offset", "scale" };

        public IReadOnlyList<ValidationIssue> Validate(XDocument document, string filePath = null, ISet<string> knownTagDefs = null)
        {
            var issues = new List<ValidationIssue>();
            if (document?.Root == null)
            {
                AddError(issues, filePath, null, null, null, "XML document has no root element.");
                return issues;
            }

            foreach (XElement legacy in document.Descendants("animationParts"))
                AddError(issues, filePath, FindDefName(legacy), null, null, "RimWorld 1.5 field 'animationParts' is forbidden; use 'keyframeParts'.");
            foreach (XElement legacy in document.Descendants("workerClass"))
                AddError(issues, filePath, FindDefName(legacy), null, null, "RimWorld 1.5 field 'workerClass' is forbidden; use 'workerType'.");

            XElement[] defs = document.Descendants("AnimationDef").ToArray();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (XElement def in defs)
                ValidateDef(def, filePath, knownTagDefs, names, issues);
            return issues;
        }

        private static void ValidateDef(XElement def, string filePath, ISet<string> knownTagDefs, ISet<string> names, IList<ValidationIssue> issues)
        {
            string defName = Value(def.Element("defName"));
            if (string.IsNullOrWhiteSpace(defName))
                AddError(issues, filePath, null, null, null, "AnimationDef requires a non-empty defName.");
            else if (!names.Add(defName))
                AddError(issues, filePath, defName, null, null, "defName is duplicated in this file.");

            int duration;
            if (!TryInt(def.Element("durationTicks"), out duration) || duration <= 0)
            {
                AddError(issues, filePath, defName, null, null, "durationTicks must be an integer greater than zero.");
                duration = -1;
            }

            XElement keyframeParts = def.Element("keyframeParts");
            XElement curveParts = def.Element("curveParts");
            if (keyframeParts == null && curveParts == null)
                AddError(issues, filePath, defName, null, null, "AnimationDef requires keyframeParts or curveParts.");

            if (keyframeParts != null)
            {
                foreach (XElement entry in keyframeParts.Elements("li"))
                    ValidateTrack(entry, filePath, defName, duration, knownTagDefs, issues);
            }

            float posRotScale;
            XElement posRotScaleElement = def.Element("posRotScale");
            if (posRotScaleElement != null && !TryFiniteFloat(posRotScaleElement.Value, out posRotScale))
                AddError(issues, filePath, defName, null, null, "posRotScale must be a finite number.");

            string loopMode = Value(def.Element("loopMode"));
            if (loopMode == "End" || loopMode == "Clamp")
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, filePath, defName, null, null, "loopMode=" + loopMode + " does not continuously loop; verify playback intent."));
        }

        private static void ValidateTrack(XElement entry, string filePath, string defName, int duration, ISet<string> knownTagDefs, IList<ValidationIssue> issues)
        {
            string tag = Value(entry.Element("key"));
            XElement value = entry.Element("value");
            if (string.IsNullOrWhiteSpace(tag))
                AddError(issues, filePath, defName, null, null, "keyframeParts entry requires a tag Def name in 'key'.");
            else if (knownTagDefs != null && !knownTagDefs.Contains(tag))
                AddError(issues, filePath, defName, tag, null, "PawnRenderNodeTagDef does not exist in the validation set.");
            if (value == null)
            {
                AddError(issues, filePath, defName, tag, null, "keyframeParts entry requires a 'value' element.");
                return;
            }
            if (value.Element("workerClass") != null)
                AddError(issues, filePath, defName, tag, null, "Use workerType instead of the RimWorld 1.5 workerClass field.");
            if (string.IsNullOrWhiteSpace(Value(value.Element("workerType"))))
                AddError(issues, filePath, defName, tag, null, "Keyframe tracks require a workerType.");
            float[] pivot;
            XElement pivotElement = value.Element("pivot");
            if (pivotElement != null && !TryVector(pivotElement.Value, 2, out pivot))
                AddError(issues, filePath, defName, tag, null, "pivot must contain two finite numbers.");

            XElement keyframes = value.Element("keyframes");
            XElement[] frames = keyframes?.Elements("li").ToArray() ?? new XElement[0];
            if (frames.Length == 0)
            {
                AddError(issues, filePath, defName, tag, null, "Track must contain at least one keyframe.");
                return;
            }

            int previousTick = -1;
            foreach (XElement frame in frames)
            {
                int tick;
                if (!TryInt(frame.Element("tick"), out tick))
                {
                    AddError(issues, filePath, defName, tag, null, "Keyframe tick must be an integer.");
                    continue;
                }
                if (tick <= previousTick)
                    AddError(issues, filePath, defName, tag, tick, "Keyframe ticks must be strictly increasing and unique.");
                if (tick < 0 || (duration >= 0 && tick > duration))
                    AddError(issues, filePath, defName, tag, tick, "Keyframe tick must be within 0..durationTicks.");
                previousTick = tick;

                foreach (string field in VectorFields)
                {
                    float[] vector = null;
                    XElement element = frame.Element(field);
                    if (element != null && !TryVector3(element.Value, out vector))
                        AddError(issues, filePath, defName, tag, tick, field + " must contain three finite numbers.");
                }

                float angle;
                XElement angleElement = frame.Element("angle");
                if (angleElement != null && !TryFiniteFloat(angleElement.Value, out angle))
                    AddError(issues, filePath, defName, tag, tick, "angle must be a finite number.");
            }
        }

        private static string FindDefName(XElement element) => Value(element.Ancestors("AnimationDef").FirstOrDefault()?.Element("defName"));
        private static string Value(XElement element) => element?.Value?.Trim();
        private static bool TryInt(XElement element, out int result) => int.TryParse(Value(element), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

        private static bool TryVector3(string text, out float[] result)
        {
            return TryVector(text, 3, out result);
        }

        private static bool TryVector(string text, int count, out float[] result)
        {
            result = null;
            if (text == null) return false;
            string[] parts = text.Trim().Trim('(', ')').Split(',');
            if (parts.Length != count) return false;
            var values = new float[count];
            for (int i = 0; i < parts.Length; i++)
                if (!TryFiniteFloat(parts[i], out values[i])) return false;
            result = values;
            return true;
        }

        private static bool TryFiniteFloat(string text, out float result)
        {
            return float.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                && !float.IsNaN(result) && !float.IsInfinity(result);
        }

        private static void AddError(IList<ValidationIssue> issues, string filePath, string defName, string track, int? tick, string message)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, filePath, defName, track, tick, message));
        }
    }
}
