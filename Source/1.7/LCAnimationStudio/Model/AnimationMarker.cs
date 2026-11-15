namespace LCAnimationStudio.Model
{
    public enum AnimationMarkerType
    {
        Hit,
        Sound,
        Code,
        Note
    }

    public sealed class AnimationMarker
    {
        public int Tick;
        public AnimationMarkerType Type;
        public string Label;

        public AnimationMarker Clone()
        {
            return (AnimationMarker)MemberwiseClone();
        }
    }
}
