using UnityEngine;

namespace TAD.Levels
{
    /// <summary>
    /// Describes one playable map that can be displayed in the Main Menu and loaded by scene name.
    /// </summary>
    [CreateAssetMenu(fileName = "LevelDefinition", menuName = "TAD/Levels/Level Definition")]
    public sealed class LevelDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private string sceneName;
        [SerializeField, TextArea] private string description;

        public string Id => id;
        public string DisplayName => displayName;
        public string SceneName => sceneName;
        public string Description => description;
    }
}