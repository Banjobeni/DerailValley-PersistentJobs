using DV.Common;
using DV.UserManagement;
using DV.UserManagement.Data;
using DV.Utils;
using JetBrains.Annotations;
using MessageBox;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

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

        public static void AlertPlayerToExceptionAndCompileDataForBugReport(Exception e, string location)
        {
            try
            {
                var logMessage = $"Exception thrown at {location}:\n{e}";
                Debug.LogError(logMessage);
                Main.Pause = true;

                var dataDir = Application.persistentDataPath;

                var exceptionFileName = $"PersistentJobsMod_Exception_{DateTime.Now.ToString("O").Replace(':', '.')}";
                var exceptionOutFolder = Path.Combine(dataDir, exceptionFileName);
                Directory.CreateDirectory(exceptionOutFolder);
                File.WriteAllText(Path.Combine(exceptionOutFolder, Path.ChangeExtension(exceptionFileName, ".log")), logMessage);

                var currentSession = SingletonBehaviour<UserManager>.Instance.CurrentUser.CurrentSession as GameSession;
                string savesDir = Path.Combine(dataDir, Path.Combine(currentSession.BasePath, "Saves"));
                
                var dirInfo = new DirectoryInfo(savesDir);
                var lastSaveFile = (from f in dirInfo.GetFiles("*.save") orderby f.LastWriteTime descending select f).FirstOrDefault();
                var lastJsonFile = (from f in dirInfo.GetFiles("*.json") orderby f.LastWriteTime descending select f).FirstOrDefault();

                lastSaveFile?.CopyTo(Path.Combine(exceptionOutFolder, "beforeEx_" + lastSaveFile.Name));
                lastJsonFile?.CopyTo(Path.Combine(exceptionOutFolder, "beforeEx_" + lastJsonFile.Name));

                currentSession.Save();
                var sessionFPath = Path.Combine(currentSession.BasePath, "sessionData.json");
                if (File.Exists(sessionFPath)) File.Copy(sessionFPath, Path.Combine(exceptionOutFolder, "sessionData.json"));

                SingletonBehaviour<SaveGameManager>.Instance.Save(SaveType.Auto, null, true);
                var newSaveFile = (from f in dirInfo.GetFiles("*.save") orderby f.LastWriteTime descending select f).FirstOrDefault();
                var newJsonFile = (from f in dirInfo.GetFiles("*.json") orderby f.LastWriteTime descending select f).FirstOrDefault();

                newSaveFile?.CopyTo(Path.Combine(exceptionOutFolder, "afterEx_" + newSaveFile.Name));
                newJsonFile?.CopyTo(Path.Combine(exceptionOutFolder, "afterEx_" + newJsonFile.Name));

                var logFPath = Path.Combine(dataDir, "Player.log");
                var newLogPath = Path.Combine(exceptionOutFolder, "Player.log");
                if (File.Exists(logFPath)) File.Copy(logFPath, newLogPath, overwrite: true);
                if (File.Exists(newLogPath))
                {
                    var log = CleanLogFromUnityDebugNoise(File.ReadAllText(newLogPath, Encoding.UTF8));
                    File.WriteAllText(newLogPath, log);
                }

                string readmeStr = ($"If you see this, please go make a bug report concerning this as it helps enormously with the development of Persistent Jobs. Thank you in advance.\n\nThe best way is to create an issue on the Github repo at \"https://github.com/Banjobeni/DerailValley-PersistentJobs/issues\". You should include this .zip file which contains the relevant logs and saves and describe what you were doing in game at the moment the exception fired.\n\nAlternatively you can post the same info in the \"Altfuture\" discord server at \"https://discord.com/invite/altfuture\" under the \"#mods-support-and-bugs\" channel. Make sure you tag \"@8029\" or \"@Banjobeni\" in your post.");
                File.WriteAllText(Path.Combine(exceptionOutFolder, "ReadME.txt"), readmeStr);

                ZipFile.CreateFromDirectory(exceptionOutFolder, Path.ChangeExtension(exceptionOutFolder, ".zip"));
                Directory.Delete(exceptionOutFolder, true);

                PopupAPI.ShowOk($"Persistent Jobs mod encountered a critical failure. The mod will stay inactive until the game is restarted.\n\nSee {exceptionFileName} for details.", onClose: c => { PopupAPI.ShowOk($"The game should be restarted now, not doing so is going to result in problems (eg. cars and jobs disappearing). It might be beneficial to load an earlier save. \nIf you see this please go make a bug report concerning this. A zipped folder containing the required info has been created at {exceptionOutFolder}"); });
            }
            catch (Exception ex)
            {
                Main._modEntry.Logger.LogException("Failed to generate bug report: ", ex);
            }
        }

        public static string CleanLogFromUnityDebugNoise(string input) =>  Regex.Replace(input, @"\r?\n?\(Filename: .*? Line: \d+\)\r?\n?\r?\n?", "\n", RegexOptions.Compiled);
    }
}