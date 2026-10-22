using System;
using System.Collections;
using System.Collections.Generic;
using TAD.Defenders;
using TAD.Match;
using TAD.UI;
using TAD.Waves;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TAD.Tutorials
{
    /// <summary>The observable action required before a Level 1 tutorial page can continue.</summary>
    public enum LevelOneTutorialRequirement
    {
        Continue,
        QueueAttackers,
        OpenAttackerInfo,
        DispatchWave,
        ConfirmFarmHandPlacement,
        OpenDefenderInfo,
        ObserveAttackPhase
    }

    /// <summary>Immutable copy and completion rule for one guided Level 1 tutorial page.</summary>
    public sealed class LevelOneTutorialStep
    {
        public LevelOneTutorialStep(
            string heading,
            string body,
            string requirementText,
            LevelOneTutorialRequirement requirement,
            bool holdsDayCycle)
        {
            Heading = heading;
            Body = body;
            RequirementText = requirementText;
            Requirement = requirement;
            HoldsDayCycle = holdsDayCycle;
        }

        public string Heading { get; }
        public string Body { get; }
        public string RequirementText { get; }
        public LevelOneTutorialRequirement Requirement { get; }
        public bool HoldsDayCycle { get; }
    }

    /// <summary>
    /// Defines the shared explanation and role-specific, hands-on portion of Level 1. A single
    /// match has one human side, so the opposite role is explained here and practised when the
    /// player reruns Level 1 as that role.
    /// </summary>
    public static class LevelOneTutorialFlow
    {
        private static readonly IReadOnlyList<LevelOneTutorialStep> AttackerSteps =
            new[]
            {
                ContinueStep(
                    "Welcome to the farm",
                    "Level 1 introduces both roles. Attackers send animals from the woodland spawn at the top left to empty the Food Store. Defenders place Farmhands to protect the barn.\n\nYou selected ATTACKER, so this run teaches you how to plan and send a wave."),
                ContinueStep(
                    "Know the opposing side",
                    "The Defender spends gold on Farmhands. A Farmhand attacks animals in range, earns gold and experience from damage and kills, then chooses stronger weapons as it levels.\n\nAs the Attacker, use the enemy's position, cover and terrain to choose a better animal group."),
                ContinueStep(
                    "Routes, cover and terrain",
                    "Ground animals follow the fastest cached route around solid walls to the Food Store. Full cover blocks attacks across it; ranged Farmhands can attack through half cover, while melee cannot.\n\nMud slows ground animals, Ice speeds them up, and flying animals ignore terrain and half cover."),
                ContinueStep(
                    "Attacker economy",
                    "Your gold and persistent income are shown at the top left. Each animal card costs gold once, queues its Send Size as one group, and immediately adds its listed income. The first animal in a group to reach the Food Store refunds that card cost once; every animal that reaches it adds the listed income again. Income is paid after sent groups finish and keeps building across days.\n\nThe UP NEXT cue shows your planned groups."),
                ActionStep(
                    "Queue your first group",
                    "Choose any affordable animal card in the attacker panel. Its cost is removed immediately, and its group appears in UP NEXT.",
                    "Required: queue one animal group.",
                    LevelOneTutorialRequirement.QueueAttackers,
                    holdsDayCycle: true),
                ActionStep(
                    "Inspect the queued group",
                    "Click the group's circle in UP NEXT. Its information card shows health, speed, size, combat rating, cost, income, remaining animals and the highlighted route it will take.",
                    "Required: open the queued attacker information card.",
                    LevelOneTutorialRequirement.OpenAttackerInfo,
                    holdsDayCycle: true),
                ActionStep(
                    "Send your wave",
                    "Close the information card, then press > above the Food Store to deploy the queued group. The preparation clock is held for this lesson, so you can choose when to launch.",
                    "Required: start the queued wave.",
                    LevelOneTutorialRequirement.DispatchWave,
                    holdsDayCycle: false),
                ActionStep(
                    "The day / night rhythm",
                    "TAD currently represents its day/night rhythm as gameplay phases rather than a lighting change. Every day starts with Preparation; queued groups deploy in FIFO order, then the day completes once every animal is defeated or reaches the Food Store.\n\nIf no animals are queued, the day still lasts at least 10 seconds. Defenders win after Day 10; Attackers win the moment food reaches zero.",
                    "Required: watch the attack phase begin.",
                    LevelOneTutorialRequirement.ObserveAttackPhase,
                    holdsDayCycle: false),
                ContinueStep(
                    "Attacker tutorial complete",
                    "You can continue ordering groups while a wave is active; they wait for the next preparation phase. Use routes, terrain, animal archetypes and the Defender layout to press the Food Store.\n\nReplay Level 1 as DEFENDER to complete that side's hands-on tutorial.")
            };

        private static readonly IReadOnlyList<LevelOneTutorialStep> DefenderSteps =
            new[]
            {
                ContinueStep(
                    "Welcome to the farm",
                    "Level 1 introduces both roles. Attackers send animals from the woodland spawn at the top left to empty the Food Store. Defenders place Farmhands to protect the barn.\n\nYou selected DEFENDER, so this run teaches you how to set up a defense."),
                ContinueStep(
                    "Know the opposing side",
                    "The Attacker spends gold on animal cards. Each card queues its Send Size as one group, immediately adds persistent income, and shows its route in UP NEXT. Animals that reach the Food Store add more income, while the first one refunds that group cost once.\n\nAs the Defender, place Farmhands where they can cover the routes before animals reach the Food Store."),
                ContinueStep(
                    "Routes, cover and terrain",
                    "Ground animals follow the fastest cached route around solid walls to the Food Store. Full cover blocks attacks across it; ranged Farmhands can attack through half cover, while melee cannot.\n\nMud slows ground animals, Ice speeds them up, and flying animals ignore terrain and half cover."),
                ContinueStep(
                    "Defender economy and growth",
                    "Your gold is shown at the top right. Farmhands cost gold to place and earn gold from kills. They gain experience from hits and kills, grow in damage and range, and later choose weapons and targeting priorities.\n\nUse their range display to cover the route rather than placing them directly on it."),
                ActionStep(
                    "Place a Farmhand",
                    "Press BUY FARMHAND at the bottom right, choose a clear spot beside the attacker route, then press the tick to confirm. The preview's circle shows the Farmhand's current targetable range.",
                    "Required: buy and confirm one Farmhand placement.",
                    LevelOneTutorialRequirement.ConfirmFarmHandPlacement,
                    holdsDayCycle: true),
                ActionStep(
                    "Inspect your Farmhand",
                    "Click the Farmhand you placed. Its information card shows damage, range, attack speed, projectile type, experience, level, upgrades, targeting method and move cost.\n\nThe targeting dropdown lets a Defender choose First, Highest Health, Fastest, Last or Highest Resources.",
                    "Required: open a Farmhand information card.",
                    LevelOneTutorialRequirement.OpenDefenderInfo,
                    holdsDayCycle: true),
                ActionStep(
                    "The day / night rhythm",
                    "TAD currently represents its day/night rhythm as gameplay phases rather than a lighting change. Every day starts with Preparation; the attacking AI queues groups, then they deploy and the day completes once every animal resolves.\n\nIf no animals are queued, the day still lasts at least 10 seconds. Defenders win after Day 10; Attackers win the moment food reaches zero.",
                    "Required: watch the attack phase begin.",
                    LevelOneTutorialRequirement.ObserveAttackPhase,
                    holdsDayCycle: false),
                ContinueStep(
                    "Defender tutorial complete",
                    "Keep Farmhands beside routes, use Mud to increase their attack time, and use cover to protect their practical range. Earn experience to unlock weapon choices and adapt the targeting mode to the animals being sent.\n\nReplay Level 1 as ATTACKER to complete that side's hands-on tutorial.")
            };

        public static IReadOnlyList<LevelOneTutorialStep> GetSteps(PlayerRole role)
        {
            return role == PlayerRole.Defender ? DefenderSteps : AttackerSteps;
        }

        private static LevelOneTutorialStep ContinueStep(string heading, string body)
        {
            return new LevelOneTutorialStep(
                heading,
                body,
                "Read this page, then continue.",
                LevelOneTutorialRequirement.Continue,
                holdsDayCycle: true);
        }

        private static LevelOneTutorialStep ActionStep(
            string heading,
            string body,
            string requirementText,
            LevelOneTutorialRequirement requirement,
            bool holdsDayCycle)
        {
            return new LevelOneTutorialStep(heading, body, requirementText, requirement, holdsDayCycle);
        }
    }

    /// <summary>
    /// Level 1-only, non-modal tutorial presentation. It observes existing authoritative systems
    /// rather than simulating placement, information panels, waves or the day cycle itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LevelOneTutorialController : MonoBehaviour
    {
        // Sit above read-only HUD badges (100-105), but below all normal player controls and
        // information cards (110+) so the tutorial never hides the very action it requests.
        private const int TutorialCanvasOrder = 109;
        private const float CardWidth = 700f;
        private const float CardHeight = 370f;
        private const float CardTopOffset = 136f;

        private readonly List<LevelOneTutorialStep> steps = new();

        private PlayerRole playerRole;
        private DayCycleController dayCycle;
        private WaveController waveController;
        private AttackerWaveQueue attackerWaveQueue;
        private FarmHandPlacementController placementController;
        private AttackerDetailsPanel attackerDetailsPanel;
        private DefenderDetailsPanel defenderDetailsPanel;
        private Canvas tutorialCanvas;
        private UnityEngine.UI.Text headingText;
        private UnityEngine.UI.Text bodyText;
        private UnityEngine.UI.Text requirementText;
        private UnityEngine.UI.Button continueButton;
        private UnityEngine.UI.Text continueLabel;
        private int currentStepIndex;
        private bool requirementComplete;
        private bool observedWaveDispatch;
        private bool observedAttackPhase;
        private bool hasConfirmedFarmHand;
        private bool isSubscribedToDayCycle;
        private bool isSubscribedToQueue;
        private bool isSubscribedToWaveController;
        private bool isSubscribedToPlacement;
        private bool isFinished;

        /// <summary>Current zero-based tutorial page, useful for automated validation.</summary>
        public int CurrentStepIndex => currentStepIndex;

        /// <summary>Current configured tutorial requirement, or Continue after the tutorial ends.</summary>
        public LevelOneTutorialRequirement CurrentRequirement =>
            isFinished || currentStepIndex < 0 || currentStepIndex >= steps.Count
                ? LevelOneTutorialRequirement.Continue
                : steps[currentStepIndex].Requirement;

        private void Awake()
        {
            if (!string.Equals(SceneManager.GetActiveScene().name, "Level1", StringComparison.Ordinal))
            {
                enabled = false;
                return;
            }

            playerRole = MatchRoleSelection.SelectedRole;
            steps.AddRange(LevelOneTutorialFlow.GetSteps(playerRole));
            ResolveDependencies();
            Subscribe();
            ApplyCurrentDayCycleHold();
        }

        private IEnumerator Start()
        {
            // The shared level core creates several UI presenters in Start. Waiting two frames
            // keeps this scene-only controller independent of script execution order.
            yield return null;
            yield return null;

            if (!enabled || isFinished)
            {
                yield break;
            }

            ResolveDependencies();
            Subscribe();
            CreateViewIfNeeded();
            ShowCurrentStep();
        }

        private void Update()
        {
            if (isFinished || currentStepIndex < 0 || currentStepIndex >= steps.Count)
            {
                return;
            }

            ResolveDependencies();
            Subscribe();
            ApplyCurrentDayCycleHold();

            if (!requirementComplete && IsCurrentRequirementComplete())
            {
                requirementComplete = true;
                RefreshView();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            if (dayCycle != null)
            {
                dayCycle.SetTutorialHeld(false);
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (dayCycle != null)
            {
                dayCycle.SetTutorialHeld(false);
            }

            if (tutorialCanvas != null)
            {
                Destroy(tutorialCanvas.gameObject);
            }
        }

        private void ResolveDependencies()
        {
            dayCycle ??= UnityEngine.Object.FindAnyObjectByType<DayCycleController>();
            waveController ??= dayCycle != null
                ? dayCycle.GetComponent<WaveController>()
                : UnityEngine.Object.FindAnyObjectByType<WaveController>();
            attackerWaveQueue ??= dayCycle != null
                ? dayCycle.GetComponent<AttackerWaveQueue>()
                : UnityEngine.Object.FindAnyObjectByType<AttackerWaveQueue>();
            placementController ??= UnityEngine.Object.FindAnyObjectByType<FarmHandPlacementController>();
            attackerDetailsPanel ??= UnityEngine.Object.FindAnyObjectByType<AttackerDetailsPanel>(
                FindObjectsInactive.Include);
            defenderDetailsPanel ??= UnityEngine.Object.FindAnyObjectByType<DefenderDetailsPanel>(
                FindObjectsInactive.Include);
        }

        private void Subscribe()
        {
            if (!isSubscribedToDayCycle && dayCycle != null)
            {
                dayCycle.CycleChanged += HandleCycleChanged;
                isSubscribedToDayCycle = true;
            }

            if (!isSubscribedToQueue && attackerWaveQueue != null)
            {
                attackerWaveQueue.AttackerDispatched += HandleAttackerDispatched;
                isSubscribedToQueue = true;
            }

            if (!isSubscribedToWaveController && waveController != null)
            {
                waveController.WaveStarted += HandleWaveStarted;
                isSubscribedToWaveController = true;
            }

            if (!isSubscribedToPlacement && placementController != null)
            {
                placementController.FarmHandPlacementConfirmed += HandleFarmHandPlacementConfirmed;
                isSubscribedToPlacement = true;
            }
        }

        private void Unsubscribe()
        {
            if (isSubscribedToDayCycle && dayCycle != null)
            {
                dayCycle.CycleChanged -= HandleCycleChanged;
            }

            if (isSubscribedToQueue && attackerWaveQueue != null)
            {
                attackerWaveQueue.AttackerDispatched -= HandleAttackerDispatched;
            }

            if (isSubscribedToWaveController && waveController != null)
            {
                waveController.WaveStarted -= HandleWaveStarted;
            }

            if (isSubscribedToPlacement && placementController != null)
            {
                placementController.FarmHandPlacementConfirmed -= HandleFarmHandPlacementConfirmed;
            }

            isSubscribedToDayCycle = false;
            isSubscribedToQueue = false;
            isSubscribedToWaveController = false;
            isSubscribedToPlacement = false;
        }

        private void HandleCycleChanged()
        {
            if (dayCycle != null && dayCycle.IsAttackerWavesRunning)
            {
                observedAttackPhase = true;
            }
        }

        private void HandleAttackerDispatched(TAD.Attackers.AttackerDefinition _)
        {
            observedWaveDispatch = true;
            observedAttackPhase = true;
        }

        private void HandleWaveStarted(int _, int __)
        {
            observedWaveDispatch = true;
            observedAttackPhase = true;
        }

        private void HandleFarmHandPlacementConfirmed(FarmHand _)
        {
            // Keep this fact for the rest of the tutorial. A player may place a Farmhand while
            // reading an earlier explanation, and should not be forced to buy a second one just
            // because they reach the corresponding page afterward.
            hasConfirmedFarmHand = true;
            if (CurrentRequirement == LevelOneTutorialRequirement.ConfirmFarmHandPlacement)
            {
                requirementComplete = true;
                RefreshView();
            }
        }

        private bool IsCurrentRequirementComplete()
        {
            return CurrentRequirement switch
            {
                LevelOneTutorialRequirement.Continue => true,
                LevelOneTutorialRequirement.QueueAttackers =>
                    attackerWaveQueue != null && attackerWaveQueue.HasQueuedAttackers,
                LevelOneTutorialRequirement.OpenAttackerInfo =>
                    attackerDetailsPanel != null && attackerDetailsPanel.IsShowing,
                LevelOneTutorialRequirement.DispatchWave =>
                    observedWaveDispatch || (dayCycle != null && dayCycle.IsAttackerWavesRunning),
                LevelOneTutorialRequirement.ConfirmFarmHandPlacement =>
                    hasConfirmedFarmHand,
                LevelOneTutorialRequirement.OpenDefenderInfo =>
                    defenderDetailsPanel != null && defenderDetailsPanel.IsShowing,
                LevelOneTutorialRequirement.ObserveAttackPhase =>
                    observedAttackPhase || (dayCycle != null && dayCycle.IsAttackerWavesRunning),
                _ => false
            };
        }

        private void ApplyCurrentDayCycleHold()
        {
            if (dayCycle == null)
            {
                return;
            }

            var shouldHold = !isFinished &&
                             currentStepIndex >= 0 &&
                             currentStepIndex < steps.Count &&
                             steps[currentStepIndex].HoldsDayCycle;
            dayCycle.SetTutorialHeld(shouldHold);
        }

        private void CreateViewIfNeeded()
        {
            if (tutorialCanvas != null)
            {
                return;
            }

            var uiLayer = LayerMask.NameToLayer("UI");
            var root = new GameObject(
                "Level 1 Tutorial Overlay",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler),
                typeof(UnityEngine.UI.GraphicRaycaster));
            root.layer = uiLayer >= 0 ? uiLayer : gameObject.layer;

            tutorialCanvas = root.GetComponent<Canvas>();
            tutorialCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            tutorialCanvas.sortingOrder = TutorialCanvasOrder;

            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var card = CreateUiObject(
                "TutorialCard",
                root.transform,
                typeof(UnityEngine.UI.Image)).GetComponent<RectTransform>();
            var cardImage = card.GetComponent<UnityEngine.UI.Image>();
            cardImage.color = new Color(0.94f, 0.86f, 0.66f, 0.97f);
            cardImage.raycastTarget = false;
            card.anchorMin = new Vector2(0.5f, 1f);
            card.anchorMax = new Vector2(0.5f, 1f);
            card.pivot = new Vector2(0.5f, 1f);
            card.sizeDelta = new Vector2(CardWidth, CardHeight);
            card.anchoredPosition = new Vector2(0f, -CardTopOffset);

            headingText = CreateText("TutorialHeading", card, 24, TextAnchor.UpperLeft);
            SetTopStretch(headingText.rectTransform, 16f, 58f, 22f);

            bodyText = CreateText("TutorialBody", card, 20, TextAnchor.UpperLeft);
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.verticalOverflow = VerticalWrapMode.Overflow;
            SetTopStretch(bodyText.rectTransform, 80f, 178f, 22f);

            requirementText = CreateText("TutorialRequirement", card, 18, TextAnchor.UpperLeft);
            SetBottomStretch(requirementText.rectTransform, 62f, 34f, 22f);

            continueButton = CreateButton("ContinueButton", card, out continueLabel);
            var buttonRect = continueButton.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0f);
            buttonRect.anchorMax = new Vector2(1f, 0f);
            buttonRect.pivot = new Vector2(1f, 0f);
            buttonRect.sizeDelta = new Vector2(192f, 44f);
            buttonRect.anchoredPosition = new Vector2(-22f, 15f);
            continueButton.onClick.AddListener(Advance);

            var skin = UnityEngine.Object.FindAnyObjectByType<FarmUiSkin>(FindObjectsInactive.Include);
            if (skin != null)
            {
                skin.ApplyThemeTo(root.transform);
            }
        }

        private void ShowCurrentStep()
        {
            if (isFinished || currentStepIndex < 0 || currentStepIndex >= steps.Count)
            {
                FinishTutorial();
                return;
            }

            observedWaveDispatch = false;
            observedAttackPhase = dayCycle != null && dayCycle.IsAttackerWavesRunning;
            requirementComplete = IsCurrentRequirementComplete();
            ApplyCurrentDayCycleHold();
            RefreshView();
        }

        private void RefreshView()
        {
            if (tutorialCanvas == null || isFinished || currentStepIndex < 0 || currentStepIndex >= steps.Count)
            {
                return;
            }

            var step = steps[currentStepIndex];
            headingText.text = $"LEVEL 1 TUTORIAL  •  {currentStepIndex + 1}/{steps.Count}\n{step.Heading}";
            bodyText.text = step.Body;
            requirementText.text = requirementComplete
                ? "✓ Action complete — you can continue."
                : step.RequirementText;
            requirementText.color = requirementComplete
                ? new Color(0.12f, 0.43f, 0.16f, 1f)
                : new Color(0.24f, 0.105f, 0.03f, 1f);

            continueButton.interactable = requirementComplete;
            continueLabel.text = currentStepIndex == steps.Count - 1
                ? "FINISH"
                : requirementComplete
                    ? "CONTINUE"
                    : "COMPLETE STEP";
        }

        private void Advance()
        {
            if (isFinished || !requirementComplete)
            {
                return;
            }

            currentStepIndex++;
            if (currentStepIndex >= steps.Count)
            {
                FinishTutorial();
                return;
            }

            ShowCurrentStep();
        }

        private void FinishTutorial()
        {
            if (isFinished)
            {
                return;
            }

            isFinished = true;
            if (dayCycle != null)
            {
                dayCycle.SetTutorialHeld(false);
            }

            if (tutorialCanvas != null)
            {
                tutorialCanvas.gameObject.SetActive(false);
            }
        }

        private static GameObject CreateUiObject(string name, Transform parent, params Type[] components)
        {
            var uiObject = new GameObject(name, components);
            uiObject.layer = parent.gameObject.layer;
            uiObject.transform.SetParent(parent, worldPositionStays: false);
            return uiObject;
        }

        private static UnityEngine.UI.Text CreateText(
            string name,
            Transform parent,
            int fontSize,
            TextAnchor alignment)
        {
            var textObject = CreateUiObject(
                name,
                parent,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Text));
            var text = textObject.GetComponent<UnityEngine.UI.Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = alignment;
            text.color = new Color(0.24f, 0.105f, 0.03f, 1f);
            text.raycastTarget = false;
            return text;
        }

        private static UnityEngine.UI.Button CreateButton(
            string name,
            Transform parent,
            out UnityEngine.UI.Text label)
        {
            var buttonObject = CreateUiObject(
                name,
                parent,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Image),
                typeof(UnityEngine.UI.Button));
            var image = buttonObject.GetComponent<UnityEngine.UI.Image>();
            image.color = new Color(0.94f, 0.86f, 0.66f, 0.98f);
            var button = buttonObject.GetComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;

            label = CreateText("Label", buttonObject.transform, 16, TextAnchor.MiddleCenter);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            return button;
        }

        private static void SetTopStretch(RectTransform rectTransform, float top, float height, float horizontalPadding)
        {
            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(1f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.sizeDelta = new Vector2(-(horizontalPadding * 2f), height);
            rectTransform.anchoredPosition = new Vector2(0f, -top);
        }

        private static void SetBottomStretch(
            RectTransform rectTransform,
            float bottom,
            float height,
            float horizontalPadding)
        {
            rectTransform.anchorMin = new Vector2(0f, 0f);
            rectTransform.anchorMax = new Vector2(1f, 0f);
            rectTransform.pivot = new Vector2(0.5f, 0f);
            rectTransform.sizeDelta = new Vector2(-(horizontalPadding * 2f), height);
            rectTransform.anchoredPosition = new Vector2(0f, bottom);
        }
    }
}
