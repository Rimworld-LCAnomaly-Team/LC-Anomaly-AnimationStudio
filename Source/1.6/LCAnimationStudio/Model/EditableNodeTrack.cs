using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace LCAnimationStudio.Model
{
    public sealed class EditableNodeTrack
    {
        public string TagDefName;
        public Vector2 Pivot;
        public RotationMode RotationMode;
        public string WorkerTypeName = typeof(AnimationWorker_Keyframes).FullName;
        public List<EditableKeyframe> Keyframes = new List<EditableKeyframe>();

        public EditableNodeTrack Clone()
        {
            return new EditableNodeTrack
            {
                TagDefName = TagDefName,
                Pivot = Pivot,
                RotationMode = RotationMode,
                WorkerTypeName = WorkerTypeName,
                Keyframes = Keyframes.Select(frame => frame.Clone()).ToList()
            };
        }
    }
}
