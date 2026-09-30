using NUnit.Framework;
using TAD.Environment;

namespace TAD.Tests.Editor
{
    public sealed class FoodStoreVisualControllerEditModeTests
    {
        [TestCase(100f, 100f, 5, 5)]
        [TestCase(80f, 100f, 5, 4)]
        [TestCase(60f, 100f, 5, 3)]
        [TestCase(20f, 100f, 5, 1)]
        [TestCase(0f, 100f, 5, 0)]
        [TestCase(25f, 0f, 5, 0)]
        [TestCase(25f, 100f, 0, 0)]
        public void VisibleFoodBagPilesFollowRemainingFood(float currentFood, float startingFood, int pileCount, int expectedVisiblePiles)
        {
            Assert.That(
                FoodStoreVisualController.GetVisibleBagPileCount(currentFood, startingFood, pileCount),
                Is.EqualTo(expectedVisiblePiles));
        }
    }
}
