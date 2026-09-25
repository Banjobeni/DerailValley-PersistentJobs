using System;
using System.Collections.Generic;

namespace PersistentJobsMod.Utilities
{
    public sealed class TokenizedFlagHolder<TFlag, TObject>(TObject heldObject, Func<TObject, TFlag, bool> onRaised, Func<TObject, TFlag, bool> onLowered) where TFlag : notnull
    {
        public readonly HashSet<TFlag> Callers = [];

        public readonly TObject HeldObject = heldObject;

        public bool IsSet => Callers.Count > 0;

        public bool Set(TFlag caller)
        {
            if (!Callers.Add(caller)) return false;

            if (Callers.Count == 1) return onRaised(HeldObject, caller);

            return false;
        }

        public bool Unset(TFlag caller)
        {
            if (!Callers.Remove(caller)) return false;

            if (Callers.Count == 0) return onLowered(HeldObject, caller);

            return false;
        }
    }
}
