using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using LCAnimationStudio.Model;
using UnityEngine;
using Verse;

namespace LCAnimationStudio.Serialization
{
    public sealed class AnimationXmlSerializer
    {
        private static readonly HashSet<string> SupportedDefFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "defName", "durationTicks", "startOnRandomTick", "playWhenDowned", "posRotScale", "loopMode", "keyframeParts"
        };
        private readonly AnimationSchemaValidator validator = new AnimationSchemaValidator();

        public AnimationEditSession Import(string path, string defName = null)
        {
            XDocument document = XDocument.Load(path, LoadOptions.SetLineInfo);
            IReadOnlyList<ValidationIssue> issues = validator.Validate(document, path, KnownTagDefs());
            ThrowIfInvalid(issues);
            XElement def = document.Descendants("AnimationDef")
                .FirstOrDefault(node => defName == null || Value(node.Element("defName")) == defName);
            if (def == null) throw new InvalidDataException("The XML file does not contain the requested AnimationDef.");
            return ParseDef(def);
        }

        public XDocument Serialize(AnimationEditSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            var def = new XElement("AnimationDef",
                new XElement("defName", session.DefName),
                new XElement("durationTicks", session.DurationTicks.ToString(CultureInfo.InvariantCulture)),
                new XElement("startOnRandomTick", Lower(session.StartOnRandomTick)),
                new XElement("playWhenDowned", Lower(session.PlayWhenDowned)),
                new XElement("posRotScale", Float(session.PosRotScale)),
                new XElement("loopMode", session.LoopMode.ToString()),
                new XElement("keyframeParts",
                    session.Tracks.Select(SerializeTrack)));
            return new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("Defs", def));
        }

        public IReadOnlyList<ValidationIssue> Validate(AnimationEditSession session, string path = null)
        {
            return validator.Validate(Serialize(session), path, KnownTagDefs());
        }

        public void ExportAtomic(AnimationEditSession session, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An export path is required.", nameof(path));
            string fullPath = Path.GetFullPath(path);
            RejectReadOnlyPath(fullPath);
            XDocument document = BuildExportDocument(session, fullPath);
            ThrowIfInvalid(validator.Validate(document, fullPath, KnownTagDefs()));

            string directory = Path.GetDirectoryName(fullPath);
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                var settings = new XmlWriterSettings
                {
                    Encoding = new UTF8Encoding(false),
                    Indent = true,
                    IndentChars = "  ",
                    NewLineChars = "\n",
                    NewLineHandling = NewLineHandling.Replace
                };
                using (XmlWriter writer = XmlWriter.Create(temporary, settings)) document.Save(writer);
                XDocument reparsed = XDocument.Load(temporary, LoadOptions.SetLineInfo);
                ThrowIfInvalid(validator.Validate(reparsed, fullPath, KnownTagDefs()));
                XElement exportedDef = reparsed.Descendants("AnimationDef")
                    .Single(node => Value(node.Element("defName")) == session.DefName);
                AnimationEditSession roundTrip = ParseDef(exportedDef);
                EnsureEquivalent(session, roundTrip);

                if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
                else File.Move(temporary, fullPath);
                session.MarkClean();
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static XElement SerializeTrack(EditableNodeTrack track)
        {
            return new XElement("li",
                new XElement("key", track.TagDefName),
                new XElement("value",
                    new XElement("workerType", ShortTypeName(track.WorkerTypeName)),
                    new XElement("pivot", Vector2Text(track.Pivot)),
                    new XElement("rotationMode", track.RotationMode.ToString()),
                    new XElement("keyframes", track.Keyframes.OrderBy(frame => frame.Tick).Select(SerializeFrame))));
        }

        private static XElement SerializeFrame(EditableKeyframe frame)
        {
            var element = new XElement("li",
                new XElement("tick", frame.Tick.ToString(CultureInfo.InvariantCulture)),
                new XElement("offset", Vector3Text(frame.Offset)),
                new XElement("angle", Float(frame.Angle)),
                new XElement("scale", Vector3Text(frame.Scale)));
            if (!string.IsNullOrEmpty(frame.GraphicStateDefName))
                element.Add(new XElement("graphicState", frame.GraphicStateDefName));
            return element;
        }

        private static AnimationEditSession ParseDef(XElement def)
        {
            EnsureSupportedDef(def);
            var session = new AnimationEditSession
            {
                DefName = Value(def.Element("defName")),
                DurationTicks = ParseInt(def.Element("durationTicks")),
                StartOnRandomTick = ParseBool(def.Element("startOnRandomTick")),
                PlayWhenDowned = ParseBool(def.Element("playWhenDowned")),
                PosRotScale = ParseFloat(def.Element("posRotScale"), 1f),
                LoopMode = ParseEnum(def.Element("loopMode"), LoopMode.Loop)
            };
            XElement parts = def.Element("keyframeParts");
            if (parts != null)
            {
                foreach (XElement entry in parts.Elements("li"))
                {
                    XElement value = entry.Element("value");
                    var track = new EditableNodeTrack
                    {
                        TagDefName = Value(entry.Element("key")),
                        WorkerTypeName = Value(value?.Element("workerType")) ?? typeof(AnimationWorker_Keyframes).FullName,
                        Pivot = ParseVector2(value?.Element("pivot"), new Vector2(0.5f, 0.5f)),
                        RotationMode = ParseEnum(value?.Element("rotationMode"), RotationMode.None)
                    };
                    XElement frames = value?.Element("keyframes");
                    if (frames != null)
                    {
                        foreach (XElement frame in frames.Elements("li"))
                        {
                            track.Keyframes.Add(new EditableKeyframe
                            {
                                Tick = ParseInt(frame.Element("tick")),
                                Offset = ParseVector3(frame.Element("offset"), Vector3.zero),
                                Angle = ParseFloat(frame.Element("angle"), 0f),
                                Scale = ParseVector3(frame.Element("scale"), Vector3.one),
                                GraphicStateDefName = Value(frame.Element("graphicState"))
                            });
                        }
                    }
                    track.Keyframes.Sort((left, right) => left.Tick.CompareTo(right.Tick));
                    session.Tracks.Add(track);
                }
            }
            return session;
        }

        private XDocument BuildExportDocument(AnimationEditSession session, string fullPath)
        {
            XDocument replacement = Serialize(session);
            if (!File.Exists(fullPath)) return replacement;

            XDocument existing = XDocument.Load(fullPath, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            ThrowIfInvalid(validator.Validate(existing, fullPath, KnownTagDefs()));
            if (existing.Root == null || existing.Root.Name.LocalName != "Defs")
                throw new InvalidDataException("The target XML must have a Defs root element.");
            XElement[] matches = existing.Descendants("AnimationDef")
                .Where(node => Value(node.Element("defName")) == session.DefName).ToArray();
            if (matches.Length > 1)
                throw new InvalidDataException("The target XML contains duplicate AnimationDef entries for " + session.DefName + ".");

            XElement newDef = replacement.Descendants("AnimationDef").Single();
            if (matches.Length == 1)
            {
                EnsureSupportedDef(matches[0]);
                matches[0].ReplaceWith(newDef);
            }
            else
            {
                existing.Root.Add(newDef);
            }
            return existing;
        }

        private static void EnsureSupportedDef(XElement def)
        {
            string[] unsupported = def.Elements()
                .Select(element => element.Name.LocalName)
                .Where(name => !SupportedDefFields.Contains(name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (unsupported.Length > 0)
                throw new NotSupportedException("AnimationDef contains fields not yet preserved by the editor: " + string.Join(", ", unsupported) + ". No file was changed.");
        }

        private static void EnsureEquivalent(AnimationEditSession expected, AnimationEditSession actual)
        {
            if (expected.DefName != actual.DefName || expected.DurationTicks != actual.DurationTicks || expected.Tracks.Count != actual.Tracks.Count)
                throw new InvalidDataException("Export round-trip changed the animation header or track count.");
            for (int i = 0; i < expected.Tracks.Count; i++)
            {
                EditableNodeTrack left = expected.Tracks[i];
                EditableNodeTrack right = actual.Tracks[i];
                if (left.TagDefName != right.TagDefName || left.Keyframes.Count != right.Keyframes.Count)
                    throw new InvalidDataException("Export round-trip changed track " + left.TagDefName + ".");
                for (int frame = 0; frame < left.Keyframes.Count; frame++)
                {
                    EditableKeyframe a = left.Keyframes[frame];
                    EditableKeyframe b = right.Keyframes[frame];
                    if (a.Tick != b.Tick || a.Offset != b.Offset || a.Angle != b.Angle || a.Scale != b.Scale || a.GraphicStateDefName != b.GraphicStateDefName)
                        throw new InvalidDataException("Export round-trip changed " + left.TagDefName + " tick " + a.Tick + ".");
                }
            }
        }

        private static HashSet<string> KnownTagDefs()
        {
            return new HashSet<string>(DefDatabase<PawnRenderNodeTagDef>.AllDefsListForReading.Select(def => def.defName), StringComparer.Ordinal);
        }

        private static void ThrowIfInvalid(IEnumerable<ValidationIssue> issues)
        {
            ValidationIssue[] errors = issues.Where(issue => issue.Severity == ValidationSeverity.Error).ToArray();
            if (errors.Length > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors.Select(issue => issue.ToString())));
        }

        private static void RejectReadOnlyPath(string path)
        {
            string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            foreach (string retiredVersion in new[] { "1.5", "1.6" })
            {
                string marker = Path.DirectorySeparatorChar + retiredVersion + Path.DirectorySeparatorChar;
                if (normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new UnauthorizedAccessException("Animation exports target RimWorld 1.7; retired version paths are not supported.");
            }
            if (!string.Equals(Path.GetExtension(path), ".xml", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Animation exports must use an .xml file.");
        }

        private static string Value(XElement element) => element?.Value?.Trim();
        private static int ParseInt(XElement element) => int.Parse(Value(element), NumberStyles.Integer, CultureInfo.InvariantCulture);
        private static bool ParseBool(XElement element) => element != null && bool.Parse(Value(element));
        private static float ParseFloat(XElement element, float fallback) => element == null ? fallback : float.Parse(Value(element), NumberStyles.Float, CultureInfo.InvariantCulture);
        private static T ParseEnum<T>(XElement element, T fallback) where T : struct
        {
            T result;
            return element != null && Enum.TryParse(Value(element), true, out result) ? result : fallback;
        }

        private static Vector2 ParseVector2(XElement element, Vector2 fallback)
        {
            float[] values = ParseVector(element, 2);
            return values == null ? fallback : new Vector2(values[0], values[1]);
        }

        private static Vector3 ParseVector3(XElement element, Vector3 fallback)
        {
            float[] values = ParseVector(element, 3);
            return values == null ? fallback : new Vector3(values[0], values[1], values[2]);
        }

        private static float[] ParseVector(XElement element, int count)
        {
            if (element == null) return null;
            string[] parts = Value(element).Trim('(', ')').Split(',');
            if (parts.Length != count) throw new FormatException("Invalid vector: " + element.Value);
            return parts.Select(part => float.Parse(part, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
        }

        private static string ShortTypeName(string name) => string.IsNullOrEmpty(name) ? nameof(AnimationWorker_Keyframes) : name.Split('.').Last();
        private static string Lower(bool value) => value ? "true" : "false";
        private static string Float(float value) => value.ToString("G9", CultureInfo.InvariantCulture);
        private static string Vector2Text(Vector2 value) => "(" + Float(value.x) + "," + Float(value.y) + ")";
        private static string Vector3Text(Vector3 value) => "(" + Float(value.x) + "," + Float(value.y) + "," + Float(value.z) + ")";
    }
}
