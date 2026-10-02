using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TAD.Attackers;
using TAD.UI;
using TAD.Waves;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TAD.Tests.PlayMode
{
    [TestFixture]
    [Category("GameBasics")]
    public sealed class AttackerRosterPresentationPlayModeTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return GameBasicsTestSupport.DestroyAllTestObjects();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return GameBasicsTestSupport.DestroyAllTestObjects();
        }

        [Test]
        public void ArchetypeBadge_UsesClearLabelsForEverySupportedArchetype()
        {
            Assert.That(AttackerArchetypeBadge.GetDisplayLabel(AttackerArchetype.Basic), Is.EqualTo("BASIC"));
            Assert.That(AttackerArchetypeBadge.GetDisplayLabel(AttackerArchetype.Fast), Is.EqualTo("FAST"));
            Assert.That(AttackerArchetypeBadge.GetDisplayLabel(AttackerArchetype.Strong), Is.EqualTo("STRONG"));
            Assert.That(AttackerArchetypeBadge.GetDisplayLabel(AttackerArchetype.Flying), Is.EqualTo("FLYING"));
        }

        [UnityTest]
        public IEnumerator ArchetypeBadge_AppearsOnSelectionQueueAndDetailsPortraits()
        {
            var definition = Resources.Load<AttackerDefinition>("TADTests/EagleDefinition");
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.Archetype, Is.EqualTo(AttackerArchetype.Flying));

            var selectionObject = CreateButtonRoot("TAD Test Archetype Selection");
            var selectionImage = CreatePortrait(selectionObject.transform, new Vector2(100f, 90f));
            var selectionButton = selectionObject.AddComponent<AttackerSelectionButton>();
            selectionButton.Bind(definition, null, null, null);

            var queueObject = CreateButtonRoot("TAD Test Archetype Queue");
            var queueImage = CreatePortrait(queueObject.transform, new Vector2(64f, 64f));
            CreateText(queueObject.transform, "Count");
            CreateText(queueObject.transform, "State");
            var queueButton = queueObject.AddComponent<AttackerQueueGroupButton>();
            var displayGroup = new AttackerQueuedGroupInfo(
                id: 1,
                definition,
                definition.SendSize,
                definition.SendSize,
                isDispatching: false,
                isCurrentGroup: false);
            queueButton.Bind(displayGroup, null, null, false);

            var fixture = GameBasicsTestSupport.CreateQueueFixture();
            yield return null;
            Assert.That(fixture.SpawnPool.RouteMapper.RebuildRoutes(), Is.True);
            Assert.That(fixture.WaveQueue.TryQueueAttackers(definition), Is.True);
            var groups = new List<AttackerQueuedGroupInfo>();
            fixture.WaveQueue.CopyDisplayGroups(groups);
            var detailsPanel = GameBasicsTestSupport.CreateAttackerDetailsPanel(
                out _,
                out _,
                out _,
                out _);
            detailsPanel.Show(fixture.WaveQueue, groups[0], null);

            yield return null;

            AssertArchetypeBadge(selectionImage, definition);
            AssertArchetypeBadge(queueImage, definition);
            var detailsImage = detailsPanel.transform.Find("Card/AttackerImage")?.GetComponent<Image>();
            Assert.That(detailsImage, Is.Not.Null);
            AssertArchetypeBadge(detailsImage, definition);
        }

        private static GameObject CreateButtonRoot(string name)
        {
            return new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
        }

        private static Image CreatePortrait(Transform parent, Vector2 size)
        {
            var imageObject = new GameObject(
                "AttackerImage",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            imageObject.transform.SetParent(parent, worldPositionStays: false);
            imageObject.GetComponent<RectTransform>().sizeDelta = size;
            return imageObject.GetComponent<Image>();
        }

        private static void CreateText(Transform parent, string name)
        {
            var textObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            textObject.transform.SetParent(parent, worldPositionStays: false);
        }

        private static void AssertArchetypeBadge(Image attackerImage, AttackerDefinition definition)
        {
            var badge = attackerImage.transform.Find("ArchetypeBadge")
                ?.GetComponent<AttackerArchetypeBadge>();
            Assert.That(badge, Is.Not.Null);
            Assert.That(badge.Label, Is.EqualTo(AttackerArchetypeBadge.GetDisplayLabel(definition.Archetype)));
        }
    }
}
