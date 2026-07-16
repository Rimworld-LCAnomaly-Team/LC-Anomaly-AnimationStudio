using System;
using UnityEngine;
using Verse;

namespace LCAnimationStudio.Runtime
{
    public sealed class RuntimePreviewController : IDisposable
    {
        private Pawn target;
        private AnimationDef originalAnimation;
        private int originalAnimationStartTick;
        private float lastRealtime;
        private float fractionalTicks;

        public Pawn Target => target;
        public AnimationDef Animation { get; private set; }
        public int RequestedTick { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool Loop { get; set; } = true;

        public void BindTarget(Pawn pawn)
        {
            if (ReferenceEquals(target, pawn)) return;
            RestoreTarget();
            target = pawn;
            if (target == null) return;

            target.Drawer.renderer.EnsureGraphicsInitialized();
            originalAnimation = target.Drawer.renderer.CurAnimation;
            originalAnimationStartTick = target.Drawer.renderer.renderTree.animationStartTick;
            Animation = null;
            RequestedTick = 0;
            IsPlaying = false;
        }

        public void SetAnimation(AnimationDef animation)
        {
            Animation = animation;
            RequestedTick = 0;
            fractionalTicks = 0f;
            IsPlaying = false;
            if (target == null) return;
            target.Drawer.renderer.SetAnimation(animation);
            SetTick(0);
        }

        public void Play()
        {
            if (target == null || Animation == null || Animation.durationTicks <= 0) return;
            Find.TickManager.Pause();
            lastRealtime = Time.realtimeSinceStartup;
            fractionalTicks = 0f;
            IsPlaying = true;
        }

        public void Pause()
        {
            Update();
            IsPlaying = false;
        }

        public void Stop()
        {
            IsPlaying = false;
            SetTick(0);
        }

        public void Step(int delta)
        {
            IsPlaying = false;
            SetTick(RequestedTick + delta);
        }

        public void SetTick(int tick)
        {
            int duration = Animation?.durationTicks ?? 0;
            RequestedTick = Mathf.Clamp(tick, 0, Math.Max(0, duration));
            if (target?.Drawer?.renderer?.renderTree == null || Animation == null) return;
            target.Drawer.renderer.renderTree.animationStartTick = GenTicks.TicksGame - RequestedTick;
        }

        public void Update()
        {
            if (!IsPlaying || target == null || Animation == null) return;
            float now = Time.realtimeSinceStartup;
            fractionalTicks += Math.Max(0f, now - lastRealtime) * GenTicks.TicksPerRealSecond;
            lastRealtime = now;
            int wholeTicks = Mathf.FloorToInt(fractionalTicks);
            if (wholeTicks <= 0) return;
            fractionalTicks -= wholeTicks;

            int next = RequestedTick + wholeTicks;
            if (next > Animation.durationTicks)
            {
                if (Loop && Animation.durationTicks > 0)
                    next %= Animation.durationTicks;
                else
                {
                    next = Animation.durationTicks;
                    IsPlaying = false;
                }
            }
            SetTick(next);
        }

        public void Dispose()
        {
            RestoreTarget();
            target = null;
        }

        private void RestoreTarget()
        {
            if (target?.Drawer?.renderer == null) return;
            target.Drawer.renderer.SetAnimation(originalAnimation);
            target.Drawer.renderer.renderTree.animationStartTick = originalAnimationStartTick;
            originalAnimation = null;
            Animation = null;
            IsPlaying = false;
        }
    }
}
