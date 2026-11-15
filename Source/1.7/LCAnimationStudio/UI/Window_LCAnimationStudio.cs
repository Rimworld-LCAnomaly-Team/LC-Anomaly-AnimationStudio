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
        private readonly AnimationProjectMetadataSerializer metadataSerializer = new AnimationProjectMetadataSerializer();
        private readonly AnimationProjectValidator projectValidator = new AnimationProjectValidator();
        private readonly ContactSheetExporter contactSheetExporter = new ContactSheetExporter();
        private readonly AnimationFileWatcher fileWatcher = new AnimationFileWatcher();
        private readonly List<NodeRow> nodes = new List<NodeRow>();
        private readonly HashSet<string> poseSelection = new HashSet<string>(StringComparer.Ordinal);
        private Vector2 nodeScroll;
        private PawnRenderNode selectedNode;
        private string targetSource;
        private AnimationEditSession session;
        private string status;
        private string xmlPath;
        private string nodeSearch = string.Empty;
        private bool taggedOnly;
        private string tickBuffer;
        private ProjectPathResolver pathResolver;
        private Dictionary<string, EditableKeyframe> copiedPose;
        private AnimationMarkerType markerType = AnimationMarkerType.Hit;
        private string markerLabel = string.Empty;

        public override Vector2 RequestedTabSize => new Vector2(1120f, 720f);
        public override MainTabWindowAnchor Anchor => MainTabWindowAnchor.Left;

        public override void PostOpen()
        {
            base.PostOpen();
            pathResolver = new ProjectPathResolver(LCAnimationStudioMod.Settings);
            ResolveTarget();
        }

        public override void PostClose()
        {
            preview.Dispose();
            fileWatcher.Dispose();
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
            PollFileWatcher();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Rect header = new Rect(inRect.x, inRect.y, inRect.width, 124f);
            DrawHeader(header);

            Rect body = new Rect(inRect.x, header.yMax + 8f, inRect.width, inRect.height - header.height - 150f);
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
            string animationLabel = session == null ? "Choose AnimationDef..." : session.DefName + (session.IsDirty || session.MetadataDirty ? " *" : string.Empty);
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

                float toolsY = inner.y + 86f;
                if (Widgets.ButtonText(new Rect(inner.x, toolsY, 122f, 24f), "Validate project")) ValidateProject();
                if (Widgets.ButtonText(new Rect(inner.x + 130f, toolsY, 138f, 24f), "PNG contact sheet")) ExportContactSheet();
                if (Widgets.ButtonText(new Rect(inner.x + 276f, toolsY, 110f, 24f), "Animated GIF")) ExportAnimatedGif();
                if (fileWatcher.State == WatchedFileState.Conflict)
                {
                    Widgets.Label(new Rect(inner.x + 394f, toolsY, 230f, 24f), "External change conflicts with edits");
                    if (Widgets.ButtonText(new Rect(inner.x + 628f, toolsY, 92f, 24f), "Reload disk")) ImportSession(true);
                    if (Widgets.ButtonText(new Rect(inner.x + 728f, toolsY, 70f, 24f), "Ignore")) fileWatcher.Acknowledge();
                }
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
            Widgets.Label(new Rect(inner.x, inner.y, 170f, 24f), "PawnRenderTree nodes (" + nodes.Count + ")");
            Rect searchRect = new Rect(inner.x + 174f, inner.y, inner.width - 174f, 24f);
            nodeSearch = Widgets.TextField(searchRect, nodeSearch ?? string.Empty);
            Widgets.CheckboxLabeled(new Rect(inner.x, inner.y + 26f, inner.width, 22f), "Only nodes with tagDef", ref taggedOnly);
            List<NodeRow> visible = nodes.Where(row =>
            {
                string tag = row.Node.Props?.tagDef?.defName;
                if (taggedOnly && string.IsNullOrEmpty(tag)) return false;
                return string.IsNullOrWhiteSpace(nodeSearch) || (tag ?? row.Node.ToString()).IndexOf(nodeSearch, StringComparison.OrdinalIgnoreCase) >= 0;
            }).ToList();
            Rect outRect = new Rect(inner.x, inner.y + 50f, inner.width, inner.height - 50f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, visible.Count * 25f);
            Widgets.BeginScrollView(outRect, ref nodeScroll, viewRect);
            for (int i = 0; i < visible.Count; i++)
            {
                NodeRow row = visible[i];
                Rect rowRect = new Rect(row.Depth * 12f, i * 25f, viewRect.width - row.Depth * 12f, 24f);
                if (ReferenceEquals(row.Node, selectedNode)) Widgets.DrawHighlightSelected(rowRect);
                else Widgets.DrawHighlightIfMouseover(rowRect);
                string tag = row.Node.Props?.tagDef?.defName;
                Rect labelRect = rowRect.ContractedBy(3f, 0f);
                if (!string.IsNullOrEmpty(tag)) labelRect.xMax -= 28f;
                Widgets.Label(labelRect, string.IsNullOrEmpty(tag) ? row.Node.ToString() : tag + " | " + row.Node);
                if (!string.IsNullOrEmpty(tag))
                {
                    Rect selectRect = new Rect(rowRect.xMax - 25f, rowRect.y + 2f, 23f, 20f);
                    if (Widgets.ButtonText(selectRect, poseSelection.Contains(tag) ? "✓" : "+"))
                    {
                        if (!poseSelection.Add(tag)) poseSelection.Remove(tag);
                    }
                }
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
            y += 36f;
            if (Widgets.ButtonText(new Rect(inner.x, y, 92f, 26f), "Gizmo move")) BeginSceneGizmo(SceneGizmoMode.Move);
            if (Widgets.ButtonText(new Rect(inner.x + 100f, y, 92f, 26f), "Gizmo rotate")) BeginSceneGizmo(SceneGizmoMode.Rotate);
            if (Widgets.ButtonText(new Rect(inner.x + 200f, y, 92f, 26f), "Gizmo scale")) BeginSceneGizmo(SceneGizmoMode.Scale);
            y += 34f;
            if (Widgets.ButtonText(new Rect(inner.x, y, 100f, 26f), "Copy pose")) CopySelectedPose(tag);
            if (Widgets.ButtonText(new Rect(inner.x + 108f, y, 100f, 26f), "Paste pose", active: copiedPose != null && copiedPose.Count > 0)) PasteSelectedPose();
            Widgets.Label(new Rect(inner.x + 218f, y, inner.width - 218f, 26f), poseSelection.Count + " selected track(s)");
            y += 34f;
            if (Widgets.ButtonText(new Rect(inner.x, y, 104f, 24f), "Hold before")) InsertHoldBefore(tag);
            if (Widgets.ButtonText(new Rect(inner.x + 112f, y, 104f, 24f), "Hold after")) InsertHoldAfter(tag);
            if (Widgets.ButtonText(new Rect(inner.x + 224f, y, 112f, 24f), "One-tick snap")) MakeOneTickSnap(tag);
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
            tickBuffer = tickBuffer ?? preview.RequestedTick.ToString();
            string editedTick = Widgets.TextField(new Rect(x + 158f, inner.y, 70f, 24f), tickBuffer);
            if (editedTick != tickBuffer)
            {
                tickBuffer = editedTick;
                int entered;
                if (int.TryParse(tickBuffer, out entered)) preview.SetTick(entered);
            }

            float tick = preview.RequestedTick;
            Rect slider = new Rect(inner.x, inner.y + 30f, inner.width, 26f);
            Widgets.HorizontalSlider(slider, ref tick, new FloatRange(0f, session.DurationTicks), null, 1f);
            int roundedTick = Mathf.RoundToInt(tick);
            if (roundedTick != preview.RequestedTick)
            {
                preview.SetTick(roundedTick);
                tickBuffer = preview.RequestedTick.ToString();
            }

            Rect markerLine = new Rect(inner.x, inner.y + 58f, inner.width, 22f);
            Widgets.DrawBoxSolid(markerLine, new Color(0.12f, 0.12f, 0.12f, 1f));
            foreach (AnimationMarker marker in session.Markers.ToArray())
            {
                float markerX = markerLine.x + markerLine.width * marker.Tick / Math.Max(1f, session.DurationTicks);
                Rect markerRect = new Rect(markerX - 4f, markerLine.y + 3f, 8f, 16f);
                Widgets.DrawBoxSolid(markerRect, MarkerColor(marker.Type));
                TooltipHandler.TipRegion(markerRect, marker.Type + " @ " + marker.Tick + ": " + marker.Label);
                if (Widgets.ButtonInvisible(markerRect)) preview.SetTick(marker.Tick);
            }

            float controlsY = inner.y + 84f;
            if (Widgets.ButtonText(new Rect(inner.x, controlsY, 112f, 24f), "Marker: " + markerType)) OpenMarkerTypeMenu();
            if (Widgets.ButtonText(new Rect(inner.x + 120f, controlsY, 86f, 24f), "Add marker")) AddMarker();
            if (Widgets.ButtonText(new Rect(inner.x + 214f, controlsY, 112f, 24f), "Remove here")) RemoveMarkersAtTick();
            bool range = preview.LoopRangeEnabled;
            Widgets.CheckboxLabeled(new Rect(inner.x + 340f, controlsY, 118f, 24f), "Loop range", ref range);
            preview.LoopRangeEnabled = range;
            if (Widgets.ButtonText(new Rect(inner.x + 466f, controlsY, 92f, 24f), "Set start")) preview.LoopStartTick = preview.RequestedTick;
            if (Widgets.ButtonText(new Rect(inner.x + 566f, controlsY, 92f, 24f), "Set end")) preview.LoopEndTick = preview.RequestedTick;
            Widgets.Label(new Rect(inner.x + 668f, controlsY, 150f, 24f), preview.LoopStartTick + ".." + preview.LoopEndTick);
            markerLabel = Widgets.TextField(new Rect(inner.x + 820f, controlsY, inner.width - 820f, 24f), markerLabel ?? string.Empty);
        }

        private void BeginEdit(AnimationDef animation)
        {
            try
            {
                session = AnimationEditSession.FromDef(animation);
                poseSelection.Clear();
                copiedPose = null;
                xmlPath = DefaultExportPath();
                LoadMetadataIfPresent();
                StartWatching();
                preview.LoopStartTick = 0;
                preview.LoopEndTick = session.DurationTicks;
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
            var issues = xmlSerializer.Validate(session).ToList();
            var targetTags = new HashSet<string>(nodes.Select(row => row.Node.Props?.tagDef?.defName).Where(tag => !string.IsNullOrEmpty(tag)), StringComparer.Ordinal);
            foreach (EditableNodeTrack track in session.Tracks.Where(track => !targetTags.Contains(track.TagDefName)))
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, xmlPath, session.DefName, track.TagDefName, null, "Track tag is not present in the current target RenderTree."));
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
                path = pathResolver.Authorize(path);
                xmlSerializer.ExportAtomic(session, path);
                xmlPath = Path.GetFullPath(path);
                metadataSerializer.SaveAtomic(session, xmlPath);
                pathResolver.Remember(session.DefName, xmlPath);
                StartWatching();
                status = "Exported " + Path.GetFileName(path);
                Messages.Message("LC Animation Studio exported " + path, MessageTypeDefOf.TaskCompletion, false);
            }
            catch (Exception exception)
            {
                status = "Export failed; see log";
                Log.Error("[LC Animation Studio] Export failed: " + exception);
            }
        }

        private void ImportSession(bool discardDirty = false)
        {
            try
            {
                if (!discardDirty && session != null && (session.IsDirty || session.MetadataDirty))
                    throw new InvalidOperationException("The edit session has unsaved changes. Export, reload explicitly, or ignore the external change.");
                xmlPath = pathResolver.Authorize(xmlPath);
                session = xmlSerializer.Import(xmlPath, session?.DefName);
                metadataSerializer.Load(session, xmlPath);
                pathResolver.Remember(session.DefName, xmlPath);
                StartWatching();
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
            return pathResolver?.Resolve(defName) ?? Path.Combine(LCAnimationStudioMod.RootDir, "1.6", "Defs", "AnimationDefs", defName + ".xml");
        }

        private void CopySelectedPose(string currentTag)
        {
            IEnumerable<string> tags = poseSelection.Count > 0 ? (IEnumerable<string>)poseSelection : new[] { currentTag };
            copiedPose = session.CapturePose(tags, preview.RequestedTick);
            status = "Copied " + copiedPose.Count + " pose track(s) @ " + preview.RequestedTick;
        }

        private void PasteSelectedPose()
        {
            if (session.PastePose(copiedPose, preview.RequestedTick))
            {
                status = "Pasted pose @ " + preview.RequestedTick;
                ApplySessionPreview();
            }
        }

        private void InsertHoldBefore(string tag)
        {
            EditableNodeTrack track = session.FindTrack(tag);
            EditableKeyframe previous = track?.Keyframes.LastOrDefault(frame => frame.Tick < preview.RequestedTick);
            int targetTick = preview.RequestedTick - 1;
            if (previous == null || targetTick < 0) { status = "No previous pose to hold"; return; }
            EditableKeyframe copy = previous.Clone();
            copy.Tick = targetTick;
            session.UpsertKeyframe(tag, copy, track.Pivot, track.RotationMode);
            status = "Hold-before keyframe @ " + targetTick;
            ApplySessionPreview();
        }

        private void InsertHoldAfter(string tag)
        {
            EditableNodeTrack track = session.FindTrack(tag);
            EditableKeyframe current = track?.Keyframes.FirstOrDefault(frame => frame.Tick == preview.RequestedTick);
            if (current == null) { status = "Record the current pose first"; return; }
            EditableKeyframe next = track.Keyframes.FirstOrDefault(frame => frame.Tick > current.Tick);
            int targetTick = next == null ? session.DurationTicks : next.Tick - 1;
            if (targetTick <= current.Tick) { status = "No room for a hold-after keyframe"; return; }
            EditableKeyframe copy = current.Clone();
            copy.Tick = targetTick;
            session.UpsertKeyframe(tag, copy, track.Pivot, track.RotationMode);
            status = "Hold-after keyframe @ " + targetTick;
            ApplySessionPreview();
        }

        private void MakeOneTickSnap(string tag)
        {
            InsertHoldBefore(tag);
            if (session.FindTrack(tag)?.Keyframes.Any(frame => frame.Tick == preview.RequestedTick) == true) InsertHoldAfter(tag);
        }

        private void BeginSceneGizmo(SceneGizmoMode mode)
        {
            if (preview.Target == null || !preview.Target.SpawnedOrAnyParentSpawned)
            {
                status = "Scene gizmo requires a Pawn on the current map or in a spawned holder";
                return;
            }
            Find.Targeter.BeginTargeting(TargetingParameters.ForCell(),
                target => ApplySceneGizmo(target, mode),
                target => DrawSceneGizmo(target, mode));
        }

        private void ApplySceneGizmo(LocalTargetInfo target, SceneGizmoMode mode)
        {
            Vector3 origin = preview.Target.Spawned ? preview.Target.DrawPos : preview.Target.PositionHeld.ToVector3Shifted();
            Vector3 delta = target.Cell.ToVector3Shifted() - origin;
            if (mode == SceneGizmoMode.Move)
            {
                selectedNode.debugOffset.x = delta.x;
                selectedNode.debugOffset.z = delta.z;
            }
            else if (mode == SceneGizmoMode.Rotate)
            {
                selectedNode.debugAngleOffset = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            }
            else
            {
                selectedNode.debugScale = Mathf.Max(0.01f, Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z));
            }
            selectedNode.requestRecache = true;
            RecordCurrentKeyframe(selectedNode.Props.tagDef.defName);
        }

        private void DrawSceneGizmo(LocalTargetInfo target, SceneGizmoMode mode)
        {
            SimpleColor color = mode == SceneGizmoMode.Move ? SimpleColor.Blue : mode == SceneGizmoMode.Rotate ? SimpleColor.Yellow : SimpleColor.Green;
            Vector3 origin = preview.Target.Spawned ? preview.Target.DrawPos : preview.Target.PositionHeld.ToVector3Shifted();
            GenDraw.DrawLineBetween(origin, target.Cell.ToVector3Shifted(), color);
        }

        private void OpenMarkerTypeMenu()
        {
            Find.WindowStack.Add(new FloatMenu(Enum.GetValues(typeof(AnimationMarkerType)).Cast<AnimationMarkerType>()
                .Select(type => new FloatMenuOption(type.ToString(), () => markerType = type)).ToList()));
        }

        private void AddMarker()
        {
            string label = string.IsNullOrWhiteSpace(markerLabel) ? markerType + " @ " + preview.RequestedTick : markerLabel.Trim();
            session.AddMarker(new AnimationMarker { Tick = preview.RequestedTick, Type = markerType, Label = label });
            markerLabel = string.Empty;
            status = "Marker added @ " + preview.RequestedTick;
        }

        private void RemoveMarkersAtTick()
        {
            AnimationMarker[] matches = session.Markers.Where(marker => marker.Tick == preview.RequestedTick).ToArray();
            foreach (AnimationMarker marker in matches) session.RemoveMarker(marker);
            status = "Removed " + matches.Length + " marker(s)";
        }

        private static Color MarkerColor(AnimationMarkerType type)
        {
            switch (type)
            {
                case AnimationMarkerType.Hit: return Color.red;
                case AnimationMarkerType.Sound: return Color.yellow;
                case AnimationMarkerType.Code: return Color.cyan;
                default: return Color.white;
            }
        }

        private void LoadMetadataIfPresent()
        {
            if (File.Exists(xmlPath)) metadataSerializer.Load(session, xmlPath);
        }

        private void StartWatching()
        {
            fileWatcher.Acknowledge();
            if (LCAnimationStudioMod.Settings.WatchExternalChanges) fileWatcher.Watch(xmlPath);
            else fileWatcher.Watch(null);
        }

        private void PollFileWatcher()
        {
            if (session == null || !LCAnimationStudioMod.Settings.WatchExternalChanges) return;
            WatchedFileState state = fileWatcher.Poll(session.IsDirty || session.MetadataDirty, LCAnimationStudioMod.Settings.WatchDebounceMilliseconds);
            if (state == WatchedFileState.Changed) ImportSession(true);
            else if (state == WatchedFileState.Conflict) status = "External XML change conflicts with unsaved edits";
        }

        private void ValidateProject()
        {
            IReadOnlyList<ValidationIssue> issues = projectValidator.ValidateSiblingProjects(pathResolver.ProjectRoot);
            int errors = issues.Count(issue => issue.Severity == ValidationSeverity.Error);
            status = "Project: " + errors + " error(s), " + (issues.Count - errors) + " warning(s)";
            if (issues.Count > 0) Log.Warning("[LC Animation Studio] Project validation:\n" + string.Join(Environment.NewLine, issues.Select(issue => issue.ToString())));
        }

        private void ExportContactSheet()
        {
            try
            {
                IEnumerable<int> ticks = session.Markers.Count > 0
                    ? session.Markers.Select(marker => marker.Tick)
                    : Enumerable.Range(0, 8).Select(index => Mathf.RoundToInt(session.DurationTicks * index / 7f));
                string path = Path.Combine(pathResolver.ProjectRoot, "1.6", "Preview", session.DefName + "_contact-sheet.png");
                string exported = contactSheetExporter.Export(preview.Target, preview, ticks, path);
                status = "Contact sheet: " + Path.GetFileName(exported);
            }
            catch (Exception exception)
            {
                status = "Contact sheet failed; see log";
                Log.Error("[LC Animation Studio] Contact sheet failed: " + exception);
            }
        }

        private void ExportAnimatedGif()
        {
            try
            {
                IEnumerable<int> ticks = ExportTicks();
                string path = Path.Combine(pathResolver.ProjectRoot, "1.6", "Preview", session.DefName + "_preview.gif");
                string exported = contactSheetExporter.ExportGif(preview.Target, preview, ticks, path);
                status = "Animated GIF: " + Path.GetFileName(exported);
            }
            catch (Exception exception)
            {
                status = "GIF export failed; see log";
                Log.Error("[LC Animation Studio] GIF export failed: " + exception);
            }
        }

        private IEnumerable<int> ExportTicks()
        {
            return session.Markers.Count > 0
                ? session.Markers.Select(marker => marker.Tick)
                : Enumerable.Range(0, 8).Select(index => Mathf.RoundToInt(session.DurationTicks * index / 7f));
        }

        private enum SceneGizmoMode
        {
            Move,
            Rotate,
            Scale
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
