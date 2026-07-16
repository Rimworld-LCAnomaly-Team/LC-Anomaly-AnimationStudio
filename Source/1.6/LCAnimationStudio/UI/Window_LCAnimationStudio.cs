using System.Collections.Generic;
using System.Linq;
using LCAnimationStudio.Runtime;
using RimWorld;
using UnityEngine;
using Verse;

namespace LCAnimationStudio.UI
{
    public sealed class Window_LCAnimationStudio : MainTabWindow
    {
        private readonly PreviewTargetResolver targetResolver = new PreviewTargetResolver();
        private readonly RuntimePreviewController preview = new RuntimePreviewController();
        private readonly List<NodeRow> nodes = new List<NodeRow>();
        private Vector2 nodeScroll;
        private PawnRenderNode selectedNode;
        private string targetSource;

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
            Rect header = new Rect(inRect.x, inRect.y, inRect.width, 64f);
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
            if (Widgets.ButtonText(animationButton, preview.Animation?.defName ?? "Choose AnimationDef..."))
                OpenAnimationMenu();
            if (Widgets.ButtonText(new Rect(animationButton.xMax + 8f, animationButton.y, 100f, 26f), "Refresh tree"))
                RefreshNodes();
        }

        private void OpenAnimationMenu()
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("None", () => preview.SetAnimation(null))
            };
            foreach (AnimationDef animation in DefDatabase<AnimationDef>.AllDefsListForReading.OrderBy(def => def.defName))
            {
                AnimationDef local = animation;
                options.Add(new FloatMenuOption(local.defName, () => preview.SetAnimation(local)));
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
            if (preview.Animation == null)
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
            Widgets.Label(new Rect(x, inner.y, 150f, 26f), preview.RequestedTick + " / " + preview.Animation.durationTicks + " ticks");

            float tick = preview.RequestedTick;
            Rect slider = new Rect(inner.x, inner.y + 30f, inner.width, 26f);
            Widgets.HorizontalSlider(slider, ref tick, new FloatRange(0f, preview.Animation.durationTicks), null, 1f);
            int roundedTick = Mathf.RoundToInt(tick);
            if (roundedTick != preview.RequestedTick) preview.SetTick(roundedTick);
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
