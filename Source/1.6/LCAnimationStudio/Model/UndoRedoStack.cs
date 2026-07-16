using System.Collections.Generic;

namespace LCAnimationStudio.Model
{
    public sealed class UndoRedoStack<T>
    {
        private readonly Stack<T> undo = new Stack<T>();
        private readonly Stack<T> redo = new Stack<T>();

        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;

        public void Record(T state)
        {
            undo.Push(state);
            redo.Clear();
        }

        public bool TryUndo(T current, out T previous)
        {
            if (undo.Count == 0) { previous = default(T); return false; }
            redo.Push(current);
            previous = undo.Pop();
            return true;
        }

        public bool TryRedo(T current, out T next)
        {
            if (redo.Count == 0) { next = default(T); return false; }
            undo.Push(current);
            next = redo.Pop();
            return true;
        }
    }
}
