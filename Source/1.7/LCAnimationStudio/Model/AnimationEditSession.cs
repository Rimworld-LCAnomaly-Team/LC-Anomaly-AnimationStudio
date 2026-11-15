using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace LCAnimationStudio.Model
{
    public sealed class AnimationEditSession
    {
        private readonly UndoRedoStack<Snapshot> history = new UndoRedoStack<Snapshot>();

        public string DefName;
        public int DurationTicks;
        public bool StartOnRandomTick;
        public bool PlayWhenDowned;
        public LoopMode LoopMode;
        public float PosRotScale;
        public List<EditableNodeTrack> Tracks = new List<EditableNodeTrack>();
        public List<AnimationMarker> Markers = new List<AnimationMarker>();
        public bool IsDirty;
        public bool MetadataDirty;

        public bool CanUndo => history.CanUndo;
        public bool CanRedo => history.CanRedo;

        public static AnimationEditSession FromDef(AnimationDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (def.curveParts != null && def.curveParts.Count > 0)
                throw new NotSupportedException("Curve animation parts are not editable in Phase 2; the source Def was left unchanged.");
            var session = new AnimationEditSession
            {
                DefName = def.defName,
                DurationTicks = def.durationTicks,
                StartOnRandomTick = def.startOnRandomTick,
                PlayWhenDowned = def.playWhenDowned,
                LoopMode = def.loopMode,
                PosRotScale = def.posRotScale
            };
            if (def.keyframeParts != null)
            {
                foreach (KeyValuePair<PawnRenderNodeTagDef, KeyframeAnimationPart> pair in def.keyframeParts)
                {
                    KeyframeAnimationPart part = pair.Value;
                    var track = new EditableNodeTrack
                    {
                        TagDefName = pair.Key.defName,
                        Pivot = part.pivot,
                        RotationMode = part.rotationMode,
                        WorkerTypeName = part.workerType?.FullName ?? typeof(AnimationWorker_Keyframes).FullName
                    };
                    if (part.keyframes != null)
                    {
                        track.Keyframes.AddRange(part.keyframes.Select(frame => new EditableKeyframe
                        {
                            Tick = frame.tick,
                            Offset = frame.offset,
                            Angle = frame.angle,
                            Scale = frame.scale,
                            GraphicStateDefName = frame.graphicState?.defName
                        }));
                    }
                    track.Keyframes.Sort((left, right) => left.Tick.CompareTo(right.Tick));
                    session.Tracks.Add(track);
                }
            }
            return session;
        }

        public EditableNodeTrack FindTrack(string tagDefName)
        {
            return Tracks.FirstOrDefault(track => string.Equals(track.TagDefName, tagDefName, StringComparison.Ordinal));
        }

        public void UpsertKeyframe(string tagDefName, EditableKeyframe frame, Vector2 pivot, RotationMode rotationMode)
        {
            if (string.IsNullOrWhiteSpace(tagDefName)) throw new ArgumentException("A tagged render node is required.", nameof(tagDefName));
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (frame.Tick < 0 || frame.Tick > DurationTicks) throw new ArgumentOutOfRangeException(nameof(frame.Tick));
            RecordChange();
            EditableNodeTrack track = FindTrack(tagDefName);
            if (track == null)
            {
                track = new EditableNodeTrack { TagDefName = tagDefName, Pivot = pivot, RotationMode = rotationMode };
                Tracks.Add(track);
            }
            int index = track.Keyframes.FindIndex(existing => existing.Tick == frame.Tick);
            if (index >= 0) track.Keyframes[index] = frame.Clone();
            else track.Keyframes.Add(frame.Clone());
            track.Keyframes.Sort((left, right) => left.Tick.CompareTo(right.Tick));
            IsDirty = true;
        }

        public bool DeleteKeyframe(string tagDefName, int tick)
        {
            EditableNodeTrack track = FindTrack(tagDefName);
            int index = track?.Keyframes.FindIndex(frame => frame.Tick == tick) ?? -1;
            if (index < 0) return false;
            RecordChange();
            track.Keyframes.RemoveAt(index);
            if (track.Keyframes.Count == 0) Tracks.Remove(track);
            IsDirty = true;
            return true;
        }

        public Dictionary<string, EditableKeyframe> CapturePose(IEnumerable<string> tagDefNames, int tick)
        {
            var result = new Dictionary<string, EditableKeyframe>(StringComparer.Ordinal);
            foreach (string tag in tagDefNames.Distinct(StringComparer.Ordinal))
            {
                EditableKeyframe frame = FindTrack(tag)?.Keyframes.FirstOrDefault(item => item.Tick == tick);
                if (frame != null) result[tag] = frame.Clone();
            }
            return result;
        }

        public bool PastePose(IDictionary<string, EditableKeyframe> pose, int tick)
        {
            if (pose == null || pose.Count == 0) return false;
            if (tick < 0 || tick > DurationTicks) throw new ArgumentOutOfRangeException(nameof(tick));
            RecordChange();
            foreach (KeyValuePair<string, EditableKeyframe> pair in pose)
            {
                EditableNodeTrack track = FindTrack(pair.Key);
                if (track == null) continue;
                EditableKeyframe frame = pair.Value.Clone();
                frame.Tick = tick;
                int index = track.Keyframes.FindIndex(existing => existing.Tick == tick);
                if (index >= 0) track.Keyframes[index] = frame;
                else track.Keyframes.Add(frame);
                track.Keyframes.Sort((left, right) => left.Tick.CompareTo(right.Tick));
            }
            IsDirty = true;
            return true;
        }

        public void AddMarker(AnimationMarker marker)
        {
            if (marker == null) throw new ArgumentNullException(nameof(marker));
            if (marker.Tick < 0 || marker.Tick > DurationTicks) throw new ArgumentOutOfRangeException(nameof(marker.Tick));
            Markers.Add(marker.Clone());
            Markers.Sort((left, right) => left.Tick.CompareTo(right.Tick));
            MetadataDirty = true;
        }

        public bool RemoveMarker(AnimationMarker marker)
        {
            bool removed = Markers.Remove(marker);
            if (removed) MetadataDirty = true;
            return removed;
        }

        public bool Undo()
        {
            Snapshot state;
            if (!history.TryUndo(Capture(), out state)) return false;
            Restore(state);
            return true;
        }

        public bool Redo()
        {
            Snapshot state;
            if (!history.TryRedo(Capture(), out state)) return false;
            Restore(state);
            return true;
        }

        public void MarkClean()
        {
            IsDirty = false;
            history.Clear();
        }
        public void MarkMetadataClean() => MetadataDirty = false;
        private void RecordChange() => history.Record(Capture());

        private Snapshot Capture()
        {
            return new Snapshot
            {
                Tracks = Tracks.Select(track => track.Clone()).ToList(),
                IsDirty = IsDirty
            };
        }

        private void Restore(Snapshot snapshot)
        {
            Tracks = snapshot.Tracks.Select(track => track.Clone()).ToList();
            IsDirty = snapshot.IsDirty;
        }

        private sealed class Snapshot
        {
            public List<EditableNodeTrack> Tracks;
            public bool IsDirty;
        }
    }
}
