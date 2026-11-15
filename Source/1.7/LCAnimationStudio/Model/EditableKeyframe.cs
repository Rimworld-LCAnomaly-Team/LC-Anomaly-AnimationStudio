using UnityEngine;

namespace LCAnimationStudio.Model
{
    public sealed class EditableKeyframe
    {
        public int Tick;
        public Vector3 Offset;
        public float Angle;
        public Vector3 Scale = Vector3.one;
        public string GraphicStateDefName;

        public EditableKeyframe Clone()
        {
            return (EditableKeyframe)MemberwiseClone();
        }
    }
}
