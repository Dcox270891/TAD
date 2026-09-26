using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// A composable farmhand look. Future cosmetic randomization can pick body, hair, and outfit
    /// assets independently, then assign the assembled profile to a FarmHandVisualController.
    /// </summary>
    [CreateAssetMenu(menuName = "TAD/Defenders/Visuals/Farmhand Appearance", fileName = "FarmHandAppearance")]
    public sealed class FarmHandAppearanceDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "Farmhand";
        [SerializeField] private FarmHandBodyVisualDefinition body;
        [SerializeField] private FarmHandHairVisualDefinition hair;
        [SerializeField] private FarmHandOutfitVisualDefinition outfit;

        public string DisplayName => displayName;
        public FarmHandBodyVisualDefinition Body => body;
        public FarmHandHairVisualDefinition Hair => hair;
        public FarmHandOutfitVisualDefinition Outfit => outfit;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = name;
            }
        }
#endif
    }
}
