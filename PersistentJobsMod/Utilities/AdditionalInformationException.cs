using System;
using JetBrains.Annotations;

namespace PersistentJobsMod.Utilities {
    public sealed class AdditionalInformationException : Exception {
        public AdditionalInformationException(string message, [NotNull] Exception innerException) : base(message, innerException) { }
    }
}