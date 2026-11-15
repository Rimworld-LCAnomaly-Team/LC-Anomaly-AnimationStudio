using System;
using System.Reflection;
using Verse;

namespace LCAnimationStudio.Runtime
{
    public sealed class PreviewTargetResolver
    {
        public bool TryResolveSelected(out Pawn pawn, out string source)
        {
            pawn = null;
            source = null;
            if (Find.Selector == null) return false;

            Thing selected = Find.Selector.SingleSelectedThing;
            Pawn selectedPawn = selected as Pawn;
            if (selectedPawn != null)
            {
                pawn = selectedPawn;
                source = "Selected pawn";
                return true;
            }

            if (selected == null) return false;
            PropertyInfo heldPawnProperty = selected.GetType().GetProperty(
                "HeldPawn",
                BindingFlags.Instance | BindingFlags.Public);
            if (heldPawnProperty == null || !typeof(Pawn).IsAssignableFrom(heldPawnProperty.PropertyType))
                return false;

            try
            {
                pawn = heldPawnProperty.GetValue(selected, null) as Pawn;
            }
            catch (TargetInvocationException exception)
            {
                Log.Warning("[LC Animation Studio] Failed to read HeldPawn from " + selected.GetType().FullName + ": " + exception.InnerException?.Message);
                return false;
            }

            if (pawn == null) return false;
            source = selected.LabelCap + " / HeldPawn";
            return true;
        }
    }
}
