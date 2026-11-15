using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using LCAnimationStudio.Model;

namespace LCAnimationStudio.Serialization
{
    public sealed class AnimationProjectMetadataSerializer
    {
        public string SidecarPath(string animationXmlPath) => Path.GetFullPath(animationXmlPath) + ".lcas.xml";

        public void Load(AnimationEditSession session, string animationXmlPath)
        {
            session.Markers.Clear();
            string path = SidecarPath(animationXmlPath);
            if (!File.Exists(path)) { session.MarkMetadataClean(); return; }
            XDocument document = XDocument.Load(path);
            XElement root = document.Root;
            if (root == null || root.Name != "LCAnimationStudioMetadata")
                throw new InvalidDataException("Invalid LC Animation Studio metadata root.");
            string defName = (string)root.Attribute("defName");
            if (!string.Equals(defName, session.DefName, StringComparison.Ordinal))
                throw new InvalidDataException("Metadata belongs to " + defName + ", not " + session.DefName + ".");
            foreach (XElement element in root.Element("markers")?.Elements("marker") ?? Enumerable.Empty<XElement>())
            {
                int tick = int.Parse((string)element.Attribute("tick"), CultureInfo.InvariantCulture);
                AnimationMarkerType type;
                if (!Enum.TryParse((string)element.Attribute("type"), true, out type)) type = AnimationMarkerType.Note;
                if (tick < 0 || tick > session.DurationTicks) continue;
                session.Markers.Add(new AnimationMarker { Tick = tick, Type = type, Label = (string)element.Attribute("label") ?? string.Empty });
            }
            session.Markers.Sort((left, right) => left.Tick.CompareTo(right.Tick));
            session.MarkMetadataClean();
        }

        public void SaveAtomic(AnimationEditSession session, string animationXmlPath)
        {
            string path = SidecarPath(animationXmlPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var document = new XDocument(new XDeclaration("1.0", "utf-8", null),
                new XElement("LCAnimationStudioMetadata",
                    new XAttribute("defName", session.DefName),
                    new XElement("markers", session.Markers.OrderBy(marker => marker.Tick).Select(marker =>
                        new XElement("marker",
                            new XAttribute("tick", marker.Tick),
                            new XAttribute("type", marker.Type),
                            new XAttribute("label", marker.Label ?? string.Empty))))));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, NewLineChars = "\n" };
                using (XmlWriter writer = XmlWriter.Create(temporary, settings)) document.Save(writer);
                XDocument.Load(temporary);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                session.MarkMetadataClean();
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
