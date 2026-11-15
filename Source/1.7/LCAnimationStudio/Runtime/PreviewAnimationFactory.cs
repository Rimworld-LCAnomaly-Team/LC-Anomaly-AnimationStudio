using System;
using System.Collections.Generic;
using LCAnimationStudio.Model;
using Verse;

namespace LCAnimationStudio.Runtime
{
    public sealed class PreviewAnimationFactory
    {
        public AnimationDef Create(AnimationEditSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            var def = new AnimationDef
            {
                defName = session.DefName + "_LCAS_Preview",
                durationTicks = session.DurationTicks,
                startOnRandomTick = false,
                playWhenDowned = session.PlayWhenDowned,
                loopMode = session.LoopMode,
                posRotScale = session.PosRotScale,
                keyframeParts = new Dictionary<PawnRenderNodeTagDef, KeyframeAnimationPart>()
            };
            foreach (EditableNodeTrack track in session.Tracks)
            {
                PawnRenderNodeTagDef tag = DefDatabase<PawnRenderNodeTagDef>.GetNamedSilentFail(track.TagDefName);
                if (tag == null || track.Keyframes.Count == 0) continue;
                var part = new KeyframeAnimationPart
                {
                    pivot = track.Pivot,
                    rotationMode = track.RotationMode,
                    workerType = ResolveWorkerType(track.WorkerTypeName),
                    keyframes = new List<Keyframe>()
                };
                foreach (EditableKeyframe editable in track.Keyframes)
                {
                    part.keyframes.Add(new Keyframe
                    {
                        tick = editable.Tick,
                        offset = editable.Offset,
                        angle = editable.Angle,
                        scale = editable.Scale,
                        graphicState = string.IsNullOrEmpty(editable.GraphicStateDefName)
                            ? null
                            : DefDatabase<GraphicStateDef>.GetNamedSilentFail(editable.GraphicStateDefName)
                    });
                }
                def.keyframeParts.Add(tag, part);
            }
            return def;
        }

        private static Type ResolveWorkerType(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                Type type = GenTypes.GetTypeInAnyAssembly(name);
                if (type != null && typeof(BaseAnimationWorker).IsAssignableFrom(type)) return type;
            }
            return typeof(AnimationWorker_Keyframes);
        }
    }
}
