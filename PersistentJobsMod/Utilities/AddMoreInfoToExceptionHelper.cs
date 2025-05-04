using System;
using JetBrains.Annotations;

namespace PersistentJobsMod.Utilities {
    public static class AddMoreInfoToExceptionHelper {
        public static TResult Run<TResult>([NotNull] [InstantHandle] Func<TResult> action, [NotNull] Func<string> getAdditionalInformation) {
            try {
                return action();
            } catch (Exception exception) {
                string additionalInformation = null;
                try {
                    additionalInformation = getAdditionalInformation();
                } catch (Exception) {
                    // failed to get additional information. throw the original exception.
                }
                if (additionalInformation == null) {
                    throw;
                }
                throw new AdditionalInformationException(additionalInformation, exception);
            }
        }

        public static void Run([NotNull] [InstantHandle] Action action, [NotNull] Func<string> getAdditionalInformation) {
            try {
                action();
            } catch (Exception exception) {
                string additionalInformation = null;
                try {
                    additionalInformation = getAdditionalInformation();
                } catch (Exception) {
                    // failed to get additional information. throw the original exception.
                }
                if (additionalInformation == null) {
                    throw;
                }
                throw new AdditionalInformationException(additionalInformation, exception);
            }
        }
    }
}