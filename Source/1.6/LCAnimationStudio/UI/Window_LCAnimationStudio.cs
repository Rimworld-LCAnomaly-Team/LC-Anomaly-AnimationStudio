using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LCAnimationStudio.Bootstrap;
using LCAnimationStudio.Model;
using LCAnimationStudio.Runtime;
using LCAnimationStudio.Serialization;
using RimWorld;
using UnityEngine;
using Verse;

namespace LCAnimationStudio.UI
{
    public sealed class Window_LCAnimationStudio : MainTabWindow
    {
        private readonly PreviewTargetResolver targetResolver = new PreviewTargetResolver();
        private readonly RuntimePreviewController preview = new RuntimePreviewController();
        private readonly PreviewAnimationFactory previewFactory = new PreviewAnimationFactory();
        private readonly AnimationXmlSerializer xmlSerializer = new AnimationXmlSerializer();
        private readonly List<NodeRow> nodes = new List<NodeRow>();
        private Vector2 nodeScroll;
        private PawnRenderNode selectedNode;
        private string targetSource;
        private AnimationEditSession session;
        private string status;
        private string xmlPath;

        public override Vector2 RequestedTabSize => new Vector2(1120f, 720f);
        public override MainTabWindowAnchor Anchor => MainTabWindowAnchor.Left;

        public override void PostOpen()
        {
            base.PostOpen();
            ResolveTarget();
        }

        public override void PostClose()
        {
            preview.Dispose();
            selectedNode = null;
            session = null;
            nodes.Clear();
            base.PostClose();
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            ResolveTarget();
            preview.Update();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Rect header = new Rect(inRect.x, inRect.y, inRect.width, 96f);
            DrawHeader(header);

            Rect body = new Rect(inRect.x, header.yMax + 8f, inRect.width, inRect.height - header.height - 76f);
            float leftWidth = body.width * 0.36f;
            Rect treeRect = new Rect(body.x, body.y, leftWidth, body.height);
            Rect inspectorRect = new Rect(treeRect.xMax + 8f, body.y, body.width - leftWidth - 8f, body.height);
            DrawNodeTree(treeRect);
            DrawInspector(inspectorRect);

            Rect timeline = new Rect(inRect.x, body.yMax + 8f, inRect.width, inRect.yMax - body.yMax - 8f);
            DrawTimeline(timeline);
        }

        private void ResolveTarget()
        {
            Pawn pawn;
            string source;
            if (!targetResolver.TryResolveSelected(out pawn, out source))
            {
                if (preview.Target != null)
                {
                    preview.BindTarget(null);
                    nodes.Clear();
                    selectedNode = null;
                }
                targetSource = null;
                return;
            }
            if (ReferenceEquals(preview.Target, pawn)) return;

            preview.BindTarget(pawn);
            targetSource = source;
            selectedNode = null;
            RefreshNodes();
            if (session != null) ApplySessionPreview();
        }

        private void RefreshNodes()
        {
            nodes.Clear();
            Pawn pawn = preview.Target;
            if (pawn == null) return;
            pawn.Drawer.renderer.EnsureGraphicsInitialized();
            PawnRenderNode root = pawn.Drawer.renderer.renderTree.rootNode;
            if (root != null) AddNode(root, 0);
        }

        private void AddNode(PawnRenderNode node, int depth)
        {
            nodes.Add(new NodeRow(node, depth));
            if (node.children == null) return;
            foreach (PawnRenderNode child in node.children)
                AddNode(child, depth + 1);
        }

        private void DrawHeader(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);
            if (preview.Target == null)
            {
                Widgets.Label(inner, "Select a Pawn or an LC holding platform containing a Pawn.");
                return;
            }

            string renderTree = preview.Target.RaceProps.renderTree?.defName ?? "<none>";
            Widgets.Label(new Rect(inner.x, inner.y, inner.width * 0.52f, Text.LineHeight),
                preview.Target.LabelShortCap + " | " + preview.Target.def.defName + " | " + renderTree + " | " + targetSource);

            Rect animationButton = new Rect(inner.x, inner.y + 28f, 330f, 26f);
            string animationLabel = session == null ? "Choose AnimationDef..." : session.DefName + (session.IsDirty ? " *" : string.Empty);
            if (Widgets.ButtonText(animationButton, animationLabel))
                OpenAnimationMenu();
            if (Widgets.ButtonText(new Rect(animationButton.xMax + 8f, animationButton.y, 100f, 26f), "Refresh tree"))
                RefreshNodes();
            if (session != null && Widgets.ButtonText(new Rect(animationButton.xMax + 116f, animationButton.y, 92f, 26f), "Validate"))
                ValidateSession();
            if (session != null && Widgets.ButtonText(new Rect(animationButton.xMax + 216f, animationButton.y, 92f, 26f), "Export XML"))
                ExportSession();
            if (!string.IsNullOrEmpty(status))
                Widgets.Label(new Rect(animationButton.xMax + 316f, animationButton.y, inner.xMax - animationButton.xMax - 316f, 26f), status);

            if (session != null)
            {
                Rect pathRect = new Rect(inner.x, inner.y + 58f, inner.width - 220f, 24f);
                xmlPath = Widgets.TextField(pathRect, xmlPath ?? DefaultExportPath());
                if (Widgets.ButtonText(new Rect(pathRect.xMax + 8f, pathRect.y, 96f, 24f), "Import XML")) ImportSession();
                if (Widgets.ButtonText(new Rect(pathRect.xMax + 112f, pathRect.y, 100f, 24f), "Use default")) xmlPath = DefaultExportPath();
            }
        }

        private void OpenAnimationMenu()
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("None", () => { session = null; status = null; preview.SetAnimation(null); })
            };
            foreach (AnimationDef animation in DefDatabase<AnimationDef>.AllDefsListForReading.OrderBy(def => def.defName))
            {
                AnimationDef local = animation;
                options.Add(new FloatMenuOption(local.defName, () => BeginEdit(local)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DrawNodeTree(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(6f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 24f), "PawnRenderTree nodes (" + nodes.Count + ")");
            Rect outRect = new Rect(inner.x, inner.y + 28f, inner.width, inner.height - 28f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, nodes.Count * 25f);
            Widgets.BeginScrollView(outRect, ref nodeScroll, viewRect);
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeRow row = nodes[i];
                Rect rowRect = new Rect(row.Depth * 12f, i * 25f, viewRect.width - row.Depth * 12f, 24f);
                if (ReferenceEquals(row.Node, selectedNode)) Widgets.DrawHighlightSelected(rowRect);
                else Widgets.DrawHighlightIfMouseover(rowRect);
                string tag = row.Node.Props?.tagDef?.defName;
                Widgets.Label(rowRect.ContractedBy(3f, 0f), string.IsNullOrEmpty(tag) ? row.Node.ToString() : tag + " | " + row.Node);
                if (Widgets.ButtonInvisible(rowRect)) selectedNode = row.Node;
            }
            Widgets.EndScrollView();
        }

        private void DrawInspector(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(10f);
            if (selectedNode == null)
            {
                Widgets.Label(inner, "Select a render node to inspect its live debug transform.");
                return;
            }

            float y = inner.y;
            Widgets.Label(new Rect(inner.x, y, inner.width, 24f), selectedNode.ToString());
            y += 32f;
            Vector3 oldOffset = selectedNode.debugOffset;
            float oldAngle = selectedNode.debugAngleOffset;
            float oldScale = selectedNode.debugScale;
            Vector2 oldPivot = selectedNode.debugPivotOffset;
            DrawSlider(inner, ref y, ref selectedNode.debugOffset.x, new FloatRange(-10f, 10f), "Offset X", 0.01f);
            DrawSlider(inner, ref y, ref selectedNode.debugOffset.z, new FloatRange(-10f, 10f), "Offset Z", 0.01f);
            DrawSlider(inner, ref y, ref selectedNode.debugAngleOffset, new FloatRange(-180f, 180f), "Angle", 0.5f);
            DrawSlider(inner, ref y, ref selectedNode.debugScale, new FloatRange(0.01f, 10f), "Scale", 0.01f);
            DrawSlider(inner, ref y, ref selectedNode.debugPivotOffset.x, FloatRange.ZeroToOne, "Pivot X", 0.01f);
            DrawSlider(inner, ref y, ref selectedNode.debugPivotOffset.y, FloatRange.ZeroToOne, "Pivot Y", 0.01f);
            if (oldOffset != selectedNode.debugOffset || oldAngle != selectedNode.debugAngleOffset ||
                oldScale != selectedNode.debugScale || oldPivot != selectedNode.debugPivotOffset)
                selectedNode.requestRecache = true;

            y += 8f;
            string tag = selectedNode.Props?.tagDef?.defName;
            if (session == null)
            {
                Widgets.Label(new Rect(inner.x, y, inner.width, 26f), "Choose an animation before editing keyframes.");
                return;
            }
            if (string.IsNullOrEmpty(tag))
            {
                Widgets.Label(new Rect(inner.x, y, inner.width, 42f), "This node has no tagDef and cannot own an AnimationDef track.");
                return;
            }

            if (Widgets.ButtonText(new Rect(inner.x, y, 154f, 28f), "Record / update tick"))
                RecordCurrentKeyframe(tag);
            if (Widgets.ButtonText(new Rect(inner.x + 162f, y, 130f, 28f), "Delete at tick"))
                DeleteCurrentKeyframe(tag);
            y += 36f;
            if (Widgets.ButtonText(new Rect(inner.x, y, 80f, 28f), "Undo", active: session.CanUndo))
            {
                if (session.Undo()) ApplySessionPreview();
            }
            if (Widgets.ButtonText(new Rect(inner.x + 88f, y, 80f, 28f), "Redo", active: session.CanRedo))
            {
                if (session.Redo()) ApplySessionPreview();
            }
            EditableNodeTrack track = session.FindTrack(tag);
            int frameCount = track?.Keyframes.Count ?? 0;
            bool hasCurrent = track?.Keyframes.Any(frame => frame.Tick == preview.RequestedTick) == true;
            Widgets.Label(new Rect(inner.x + 180f, y, inner.width - 180f, 28f), frameCount + " frame(s)" + (hasCurrent ? " | current tick exists" : string.Empty));
        }

        private static void DrawSlider(Rect rect, ref float y, ref float value, FloatRange range, string label, float roundTo)
        {
            Widgets.HorizontalSlider(new Rect(rect.x, y, rect.width, 28f), ref value, range, label + ": " + value.ToString("0.##"), roundTo);
            y += 34f;
        }

        private void DrawTimeline(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(6f);
            if (session == null || preview.Animation == null)
            {
                Widgets.Label(inner, "Choose an AnimationDef to enable playback and scrubbing.");
                return;
            }

            float x = inner.x;
            if (Widgets.ButtonText(new Rect(x, inner.y, 58f, 26f), preview.IsPlaying ? "Pause" : "Play"))
            {
                if (preview.IsPlaying) preview.Pause(); else preview.Play();
            }
            x += 64f;
            if (Widgets.ButtonText(new Rect(x, inner.y, 48f, 26f), "Stop")) preview.Stop();
            x += 54f;
            if (Widgets.ButtonText(new Rect(x, inner.y, 32f, 26f), "<")) preview.Step(-1);
            x += 38f;
            if (Widgets.ButtonText(new Rect(x, inner.y, 32f, 26f), ">")) preview.Step(1);
            x += 42f;
            bool loop = preview.Loop;
            Widgets.CheckboxLabeled(new Rect(x, inner.y, 85f, 26f), "Loop", ref loop);
            preview.Loop = loop;
            x += 92f;
            Widgets.Label(new Rect(x, inner.y, 150f, 26f), preview.RequestedTick + " / " + session.DurationTicks + " ticks");

            float tick = preview.RequestedTick;
            Rect slider = new Rect(inner.x, inner.y + 30f, inner.width, 26f);
            Widgets.HorizontalSlider(slider, ref tick, new FloatRange(0f, session.DurationTicks), null, 1f);
            int roundedTick = Mathf.RoundToInt(tick);
            if (roundedTick != preview.RequestedTick) preview.SetTick(roundedTick);
        }

        private void BeginEdit(AnimationDef animation)
        {
            try
            {
                session = AnimationEditSession.FromDef(animation);
                xmlPath = DefaultExportPath();
                status = session.Tracks.Count + " track(s) loaded";
                ApplySessionPreview();
            }
            catch (Exception exception)
            {
                status = "Animation is not editable; see log";
                Log.Error("[LC Animation Studio] Could not create edit session: " + exception);
            }
        }

        private void RecordCurrentKeyframe(string tag)
        {
            EditableNodeTrack existing = session.FindTrack(tag);
            session.UpsertKeyframe(tag, new EditableKeyframe
            {
                Tick = preview.RequestedTick,
                Offset = selectedNode.debugOffset,
                Angle = selectedNode.debugAngleOffset,
                Scale = Vector3.one * selectedNode.debugScale
            }, existing?.Pivot ?? new Vector2(0.5f, 0.5f), existing?.RotationMode ?? RotationMode.None);
            status = "Recorded " + tag + " @ " + preview.RequestedTick;
            ApplySessionPreview();
        }

        private void DeleteCurrentKeyframe(string tag)
        {
            if (!session.DeleteKeyframe(tag, preview.RequestedTick))
            {
                status = "No keyframe at current tick";
                return;
            }
            status = "Deleted " + tag + " @ " + preview.RequestedTick;
            ApplySessionPreview();
        }

        private void ApplySessionPreview()
        {
            int tick = preview.RequestedTick;
            preview.SetAnimation(previewFactory.Create(session));
            preview.SetTick(Mathf.Min(tick, session.DurationTicks));
        }

        private void ValidateSession()
        {
            IReadOnlyList<ValidationIssue> issues = xmlSerializer.Validate(session);
            int errors = issues.Count(issue => issue.Severity == ValidationSeverity.Error);
            int warnings = issues.Count - errors;
            status = errors + " error(s), " + warnings + " warning(s)";
            if (issues.Count > 0) Log.Warning("[LC Animation Studio] " + string.Join(Environment.NewLine, issues.Select(issue => issue.ToString())));
        }

        private void ExportSession()
        {
            try
            {
                string path = string.IsNullOrWhiteSpace(xmlPath) ? DefaultExportPath() : xmlPath;
                xmlSerializer.ExportAtomic(session, path);
                xmlPath = Path.GetFullPath(path);
                status = "Exported " + Path.GetFileName(path);
                Messages.Message("LC Animation Studio exported " + path, MessageTypeDefOf.TaskCompletion, false);
            }
            catch (Exception exception)
            {
                status = "Export failed; see log";
                Log.Error("[LC Animation Studio] Export failed: " + exception);
            }
        }

        private void ImportSession()
        {
            try
            {
                session = xmlSerializer.Import(xmlPath, session?.DefName);
                status = session.Tracks.Count + " track(s) imported";
                ApplySessionPreview();
            }
            catch (Exception exception)
            {
                status = "Import failed; see log";
                Log.Error("[LC Animation Studio] Import failed: " + exception);
            }
        }

        private string DefaultExportPath()
        {
            string defName = session?.DefName ?? "Animation";
            return Path.Combine(LCAnimationStudioMod.RootDir, "1.6", "Defs", "AnimationDefs", defName + ".xml");
        }

        private sealed class NodeRow
        {
            public NodeRow(PawnRenderNode node, int depth)
            {
                Node = node;
                Depth = depth;
            }

            public PawnRenderNode Node { get; }
            public int Depth { get; }
        }
    }
}
