using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace TAD.Editor
{
    /// <summary>
    /// Removes the unused Unity AI Assistant package through UPM so its subscription failures
    /// cannot continue filling the Console. It intentionally leaves Pipeline and AI Inference intact.
    /// </summary>
    [InitializeOnLoad]
    internal static class UnityAiAssistantPackageRemoval
    {
        private const string PackageName = "com.unity.ai.assistant";
        private const double TimeoutSeconds = 600d;

        private static RemoveRequest removeRequest;
        private static double deadline;
        private static bool removalRequested;

        static UnityAiAssistantPackageRemoval()
        {
            EditorApplication.delayCall += RemoveIfPresent;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        [MenuItem("TAD/Maintenance/Remove Unity AI Assistant Package")]
        private static void RemoveFromMenu()
        {
            RemoveIfPresent();
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.delayCall += RemoveIfPresent;
            }
        }

        private static void RemoveIfPresent()
        {
            if (removalRequested || EditorApplication.isPlayingOrWillChangePlaymode || !ManifestContainsAssistant())
            {
                return;
            }

            removalRequested = true;
            removeRequest = Client.Remove(PackageName);
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += PollRemoval;
            Debug.Log("Removing the unused Unity AI Assistant package.");
        }

        private static void PollRemoval()
        {
            if (removeRequest == null)
            {
                return;
            }

            if (!removeRequest.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup <= deadline)
                {
                    return;
                }

                EditorApplication.update -= PollRemoval;
                Debug.LogError("Timed out while removing the Unity AI Assistant package.");
                return;
            }

            EditorApplication.update -= PollRemoval;
            if (removeRequest.Status == StatusCode.Success)
            {
                Debug.Log("Removed the Unity AI Assistant package. The Editor will recompile without its subscription warnings.");
                return;
            }

            Debug.LogError($"Could not remove the Unity AI Assistant package: {removeRequest.Error?.message}");
        }

        private static bool ManifestContainsAssistant()
        {
            var manifestPath = Path.GetFullPath("Packages/manifest.json");
            return File.Exists(manifestPath) && File.ReadAllText(manifestPath).Contains($"\"{PackageName}\"");
        }
    }
}
