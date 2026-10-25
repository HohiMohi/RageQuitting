using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public sealed class SawCuttingPlayModeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private static readonly System.Type InputType = System.Type.GetType("PlayerInputNew, Assembly-CSharp");
    private static readonly System.Type InventoryType = System.Type.GetType("PlayerInventory, Assembly-CSharp");
    private static readonly System.Type InteractionType = System.Type.GetType("PlayerInteractionNew, Assembly-CSharp");
    private static readonly System.Type ActionType = System.Type.GetType("PlayerActionController, Assembly-CSharp");
    private static readonly System.Type PanelType = System.Type.GetType("BridgeDeckAreaPanel, Assembly-CSharp");
    private static readonly System.Type ItemType = System.Type.GetType("EquippableItemSO, Assembly-CSharp");
    private static readonly System.Type ProfileType = System.Type.GetType("SawCutProfileSO, Assembly-CSharp");
    private readonly List<GameObject> ownedObjects = new();
    private GameObject player;
    private GameObject cameraObject;
    private GameObject source;
    private GameObject floor;
    private ScriptableObject sawItem;
    private ScriptableObject profile;
    private ScriptableObject canonicalProfile;
    private Mouse mouse;
    private InputAction leftAction;
    private Keyboard keyboard;
    private readonly CrowbarInputSettingsScope inputSettings = new();
    private int primaryActionEvents;

    [UnitySetUp]
    public IEnumerator Setup()
    {
        inputSettings.UseFocusedGameViewRouting();
        Assert.That(Unity.Netcode.NetworkManager.Singleton == null || !Unity.Netcode.NetworkManager.Singleton.IsListening, Is.True);
        mouse = InputSystem.AddDevice<Mouse>();
        keyboard = InputSystem.AddDevice<Keyboard>();
        player = Own(new GameObject("Saw B2 input actor"));
        Component input = player.AddComponent(InputType);
        Component inventory = player.AddComponent(InventoryType);
        Component interaction = player.AddComponent(InteractionType);
        player.AddComponent(System.Type.GetType("PlayerHealth, Assembly-CSharp"));
        player.AddComponent(ActionType);
        FieldInfo actionEvent = InputType.GetField("OnAction", PublicInstance);
        actionEvent.SetValue(input, (System.EventHandler)actionEvent.GetValue(input) + (System.EventHandler)((_, _) => primaryActionEvents++));
        cameraObject = Own(new GameObject("Saw B2 aim camera"));
        cameraObject.transform.position = new Vector3(0f, 1.2f, -.1f);
        Camera camera = cameraObject.AddComponent<Camera>();
        cameraObject.tag = "MainCamera";
        InteractionType.GetMethod("SetInteractionOrigin", PublicInstance).Invoke(interaction, new object[] { cameraObject.transform });
        InteractionType.GetMethod("SetAimCamera", PublicInstance).Invoke(interaction, new object[] { camera });

        profile = ScriptableObject.CreateInstance(ProfileType);
        ProfileType.GetField("activeCutSeconds", PublicInstance).SetValue(profile, .20f);
        ProfileType.GetField("stableSeconds", PublicInstance).SetValue(profile, .05f);
        canonicalProfile = ScriptableObject.CreateInstance(System.Type.GetType("BridgeDeckAreaProfile, Assembly-CSharp"));
        ProfileType.GetField("geometryProfile", PublicInstance).SetValue(profile, canonicalProfile);
        sawItem = ScriptableObject.CreateInstance(ItemType);
        ItemType.GetField("itemName", PublicInstance).SetValue(sawItem, "Saw B2 test item");
        ItemType.GetField("itemType", PublicInstance).SetValue(sawItem, System.Enum.Parse(System.Type.GetType("EquippableItemType, Assembly-CSharp"), "Saw"));
        ItemType.GetField("inventorySlotsRequired", PublicInstance).SetValue(sawItem, 2);
        ItemType.GetField("actionRange", PublicInstance).SetValue(sawItem, 2.5f);
        ItemType.GetField("sawCutProfile", PublicInstance).SetValue(sawItem, profile);
        Assert.That((bool)InventoryType.GetMethod("AddItem", PublicInstance).Invoke(inventory, new object[] { sawItem }), Is.True);

        floor = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
        floor.name = "Saw B2 static support";
        floor.transform.position = new Vector3(0f, .1f, 1.2f);
        floor.transform.localScale = new Vector3(5f, .2f, 5f);
        source = Own(new GameObject("Saw B2 source"));
        source.transform.position = new Vector3(0f, .2f, 1.2f);
        source.AddComponent<Unity.Netcode.NetworkObject>();
        source.AddComponent<MeshFilter>();
        source.AddComponent<MeshRenderer>();
        source.AddComponent<Rigidbody>().useGravity = true;
        source.AddComponent<BoxCollider>();
        Component sourcePanel = source.AddComponent(PanelType);
        PanelType.GetMethod("ApplyMaterialProfile", PublicInstance).Invoke(sourcePanel, new[] { canonicalProfile });
        yield return null;
        yield return null;
        leftAction = ConfigureInputDevices(input);
        Assert.That((bool)InputType.GetProperty("IsLocalGameplayInputActive", PublicInstance).GetValue(input), Is.True);
        Assert.That(InventoryType.GetMethod("GetCurrentSelectedItem", PublicInstance).Invoke(inventory, null), Is.SameAs(sawItem));
        cameraObject.transform.rotation = Quaternion.LookRotation(new Vector3(0f, .3f, 1.2f) - cameraObject.transform.position);
        Physics.SyncTransforms();
        yield return new WaitForSeconds(.45f);
    }

    [UnityTest]
    public IEnumerator ActualLeftMouseHoldPausesAndResumesImmutableLineThenCreatesTwoFragments()
    {
        yield return new WaitForSeconds(.08f);
        Component panel = source.GetComponent(PanelType);
        Component action = player.GetComponent(ActionType);
        Assert.That((bool)PanelType.GetMethod("IsEligible", PublicInstance).Invoke(panel, new object[] { profile, true }), Is.True,
            "The source must be released, stable, nail-free, and supported before the real input begins.");
        Assert.That(leftAction.enabled, Is.True, "The generated Action action must be enabled.");
        Assert.That(Physics.Raycast(cameraObject.transform.position, cameraObject.transform.forward, out RaycastHit aimedHit, 2.5f), Is.True,
            "The production aim ray must hit the test stock.");
        Assert.That(aimedHit.collider.GetComponent(PanelType), Is.SameAs(panel));
        QueueLeft(true);
        Assert.That(leftAction.IsPressed(), Is.True, "The real left-mouse binding must report held state.");
        yield return null;
        Assert.That(primaryActionEvents, Is.EqualTo(1), "The generated LMB action must invoke PlayerInputNew.OnAction exactly once.");
        yield return new WaitForSeconds(.08f);
        Assert.That((bool)PanelType.GetProperty("CutStarted", PublicInstance).GetValue(panel), Is.True,
            "A real generated LMB action starts the saw cut through PlayerActionController.");
        bool promptActive;
        string sawPrompt = ReadSawPresentation(action, panel, out promptActive);
        Assert.That(promptActive, Is.True);
        StringAssert.Contains("hold LMB to continue", sawPrompt,
            "The current operator should see the cut's existing progress as an active hold.");
        Assert.That((bool)PanelType.GetMethod("ResumeCutOffline", PublicInstance).Invoke(panel,
            new object[] { 0x1234UL, profile }), Is.False,
            "A competing operator cannot take a panel while its current attempt is active.");
        int frozenOffset = (int)PanelType.GetProperty("CutOffsetMillimeters", PublicInstance).GetValue(panel);
        Assert.That(frozenOffset, Is.EqualTo(1250));
        QueueLeft(false);
        yield return null;
        int progressAfterPause = (int)PanelType.GetProperty("ProgressMilliseconds", PublicInstance).GetValue(panel);
        object state = PanelType.GetProperty("State", PublicInstance).GetValue(panel);
        Assert.That((ulong)state.GetType().GetField("OperatorClientId").GetValue(state), Is.EqualTo(ulong.MaxValue));
        Assert.That(progressAfterPause, Is.GreaterThan(0));
        sawPrompt = ReadSawPresentation(action, panel, out promptActive);
        Assert.That(promptActive, Is.True, "A paused cut must keep its completed work visible.");
        StringAssert.Contains("hold LMB to resume", sawPrompt,
            "CutStarted alone does not mean an operator currently holds the reservation.");
        Assert.That((bool)PanelType.GetMethod("ResumeCutOffline", PublicInstance).Invoke(panel,
            new object[] { 0x1234UL, profile }), Is.True,
            "A different eligible operator may resume the same immutable cut after the first operator releases it.");
        state = PanelType.GetProperty("State", PublicInstance).GetValue(panel);
        Assert.That((ulong)state.GetType().GetField("OperatorClientId").GetValue(state), Is.EqualTo(0x1234UL));
        PanelType.GetMethod("PauseCutOffline", PublicInstance).Invoke(panel, new object[] { 0x1234UL });

        floor.GetComponent<Collider>().enabled = false;
        sawPrompt = ReadSawPresentation(action, panel, out promptActive);
        Assert.That(promptActive, Is.True, "Unavailable paused work must still report retained progress.");
        StringAssert.StartsWith("Saw: cut unavailable", sawPrompt,
            "A paused cut that lost its support must show an information prompt, not an LMB action hint.");
        StringAssert.Contains("support from below", sawPrompt);
        StringAssert.Contains("Cut at 1250 mm", sawPrompt,
            "The unavailable information must preserve the original immutable cut offset.");
        StringAssert.Contains("A ", sawPrompt);
        StringAssert.Contains(" + B ", sawPrompt,
            "The unavailable information must preserve the cut's A/B dimensions.");
        floor.GetComponent<Collider>().enabled = true;

        cameraObject.transform.rotation = Quaternion.LookRotation(new Vector3(.3f, .3f, 1.2f) - cameraObject.transform.position);
        Physics.SyncTransforms();
        QueueLeft(true);
        yield return null;
        Assert.That((int)PanelType.GetProperty("CutOffsetMillimeters", PublicInstance).GetValue(panel), Is.EqualTo(frozenOffset),
            "Resume anywhere on the panel cannot move the original cut line.");
        yield return new WaitForSeconds(.28f);
        Assert.That(source == null, Is.True, "Completion must replace the source after both output fragments are prepared.");
        Component[] fragments = FindPanels();
        Assert.That(fragments.Length, Is.EqualTo(2));
        System.Array.Sort(fragments, (a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
        Assert.That((int)PanelType.GetProperty("LengthMillimeters", PublicInstance).GetValue(fragments[0])
            + (int)PanelType.GetProperty("LengthMillimeters", PublicInstance).GetValue(fragments[1]) + 5, Is.EqualTo(2500));
        Assert.That((int)PanelType.GetProperty("WidthMillimeters", PublicInstance).GetValue(fragments[0]), Is.EqualTo(2400));
        Assert.That((int)PanelType.GetProperty("WidthMillimeters", PublicInstance).GetValue(fragments[1]), Is.EqualTo(2400));
        Rigidbody left = ((Component)PanelType.GetProperty("PhysicsBody", PublicInstance).GetValue(fragments[0])).GetComponent<Rigidbody>();
        Rigidbody right = ((Component)PanelType.GetProperty("PhysicsBody", PublicInstance).GetValue(fragments[1])).GetComponent<Rigidbody>();
        Assert.That(left.mass + right.mass, Is.EqualTo(.998f).Within(.001f));
        Assert.That(fragments[0].transform.position.x, Is.EqualTo(-.625f).Within(.002f));
        Assert.That(fragments[1].transform.position.x, Is.EqualTo(.6275f).Within(.002f));
        AssertClosedOutwardBoxMesh(fragments[0].gameObject);
        AssertClosedOutwardBoxMesh(fragments[1].gameObject);

        QueueLeft(false);
        yield return new WaitForSeconds(.45f);
        Component recutSource = fragments[0];
        Vector3 recutPoint = recutSource.transform.position
            + recutSource.transform.rotation * new Vector3(0f, .2f, 0f);
        cameraObject.transform.rotation = Quaternion.LookRotation(recutPoint - cameraObject.transform.position);
        Physics.SyncTransforms();
        int recutSourceLength = (int)PanelType.GetProperty("LengthMillimeters", PublicInstance).GetValue(recutSource);
        float recutSourceMass = recutSource.GetComponent<Rigidbody>().mass;
        QueueLeft(true);
        yield return Until(() => (bool)PanelType.GetProperty("CutStarted", PublicInstance).GetValue(recutSource),
            "a cut on a previously cut fragment");
        yield return new WaitForSeconds(.3f);
        Assert.That(recutSource == null, Is.True, "A fragment remains eligible for another independent cut.");
        Component[] afterRecut = FindPanels();
        Assert.That(afterRecut.Length, Is.EqualTo(3));
        int recutLengthTotal = 0;
        float recutMassTotal = 0f;
        int recutPieceCount = 0;
        foreach (Component piece in afterRecut)
        {
            int pieceLength = (int)PanelType.GetProperty("LengthMillimeters", PublicInstance).GetValue(piece);
            if (pieceLength < 1000)
            {
                recutPieceCount++;
                recutLengthTotal += pieceLength;
                recutMassTotal += piece.GetComponent<Rigidbody>().mass;
                AssertClosedOutwardBoxMesh(piece.gameObject);
            }
        }
        Assert.That(recutPieceCount, Is.EqualTo(2));
        Assert.That(recutLengthTotal + 5, Is.EqualTo(recutSourceLength));
        Assert.That(recutMassTotal, Is.EqualTo(recutSourceMass * (recutSourceLength - 5f) / recutSourceLength).Within(.001f));
    }

    [UnityTest]
    public IEnumerator FourSecondOfflineInputTimerKeepsElapsedTimeAcrossPause()
    {
        ScriptableObject defaultProfile = ScriptableObject.CreateInstance(ProfileType);
        Assert.That((float)ProfileType.GetField("activeCutSeconds", PublicInstance).GetValue(defaultProfile), Is.EqualTo(4f));
        Object.Destroy(defaultProfile);
        ProfileType.GetField("activeCutSeconds", PublicInstance).SetValue(profile, 4f);
        Component panel = source.GetComponent(PanelType);
        Assert.That((bool)PanelType.GetMethod("IsEligible", PublicInstance).Invoke(panel, new object[] { profile, true }), Is.True);
        QueueLeft(true);
        yield return null;
        Assert.That((bool)PanelType.GetProperty("CutStarted", PublicInstance).GetValue(panel), Is.True);
        yield return new WaitForSecondsRealtime(1.95f);
        QueueLeft(false);
        yield return null;
        int pausedMilliseconds = (int)PanelType.GetProperty("ProgressMilliseconds", PublicInstance).GetValue(panel);
        Assert.That(pausedMilliseconds, Is.InRange(1850, 2000));
        Assert.That(source != null, Is.True, "A paused cut cannot complete while inactive.");

        cameraObject.transform.rotation = Quaternion.LookRotation(new Vector3(.3f, .3f, 1.2f) - cameraObject.transform.position);
        Physics.SyncTransforms();
        QueueLeft(true);
        yield return null;
        Assert.That((ulong)PanelType.GetField("activeOperatorNetworkObjectId", PrivateInstance).GetValue(panel),
            Is.Not.EqualTo(ulong.MaxValue));
        yield return new WaitForSecondsRealtime(2.25f);
        Assert.That(source == null, Is.True, "The configured four active seconds should finish across the pause.");
    }

    [UnityTest]
    public IEnumerator NonDefaultCanonicalProfileDrivesPanelCutDimensionsKerfAndCarryMass()
    {
        System.Type canonicalType = canonicalProfile.GetType();
        canonicalType.GetField("stockLengthMeters").SetValue(canonicalProfile, 2.5f);
        canonicalType.GetField("stockWidthMeters").SetValue(canonicalProfile, 2.4f);
        canonicalType.GetField("stockThicknessMeters").SetValue(canonicalProfile, .2f);
        canonicalType.GetField("minimumFragmentLengthMeters").SetValue(canonicalProfile, .45f);
        canonicalType.GetField("sawKerfMeters").SetValue(canonicalProfile, .009f);
        canonicalType.GetField("fullStockMassKilograms").SetValue(canonicalProfile, 1.6f);
        ProfileType.GetField("geometryProfile", PublicInstance).SetValue(profile, null);

        Component panel = source.GetComponent(PanelType);
        PanelType.GetMethod("ApplyMaterialProfile", PublicInstance).Invoke(panel, new[] { canonicalProfile });
        Assert.That((int)PanelType.GetProperty("LengthMillimeters", PublicInstance).GetValue(panel), Is.EqualTo(2500));
        Assert.That((int)PanelType.GetProperty("WidthMillimeters", PublicInstance).GetValue(panel), Is.EqualTo(2400));
        Assert.That(source.GetComponent<Rigidbody>().mass, Is.EqualTo(1.6f).Within(.001f),
            "The canonical profile initializes full-stock density before any area-assessment refresh.");
        Assert.That((int)PanelType.GetMethod("MinimumFragmentLengthMillimeters", PublicInstance)
            .Invoke(panel, new[] { profile }), Is.EqualTo(450), "The panel canonical profile is the fallback when the Saw has no canonical reference.");

        Assert.That((bool)PanelType.GetMethod("TryBeginCutOffline", PublicInstance)
            .Invoke(panel, new object[] { 1250, 0x88UL, profile }), Is.True);
        PanelType.GetMethod("AdvanceCutOffline", PublicInstance).Invoke(panel, new object[] { 0x88UL, .2f, profile });
        Assert.That((bool)PanelType.GetMethod("TryCompleteOfflineCut", PublicInstance).Invoke(panel, new[] { profile }), Is.True);
        yield return null;

        Component[] fragments = FindPanels();
        Assert.That(source == null, Is.True);
        Assert.That(fragments.Length, Is.EqualTo(2));
        System.Array.Sort(fragments, (a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
        Assert.That((int)PanelType.GetProperty("LengthMillimeters", PublicInstance).GetValue(fragments[0]), Is.EqualTo(1250));
        Assert.That((int)PanelType.GetProperty("LengthMillimeters", PublicInstance).GetValue(fragments[1]), Is.EqualTo(1241),
            "The configured 9mm kerf is removed from the integer-millimeter stock split.");
        Assert.That(fragments[0].transform.position.x, Is.EqualTo(-.625f).Within(.001f));
        Assert.That(fragments[1].transform.position.x, Is.EqualTo(.6295f).Within(.001f));
        Assert.That(fragments[0].GetComponent<Rigidbody>().mass, Is.EqualTo(.8f).Within(.001f));
        Assert.That(fragments[1].GetComponent<Rigidbody>().mass, Is.EqualTo(.79424f).Within(.001f));

        Component carry = fragments[0].gameObject.AddComponent(System.Type.GetType("SharedCarryPhysicsBody, Assembly-CSharp"));
        carry.GetType().GetMethod("BeginSharedCarry", PublicInstance).Invoke(carry, new object[] { true });
        Assert.That(fragments[0].GetComponent<Rigidbody>().mass, Is.EqualTo(.8f).Within(.001f));
        carry.GetType().GetMethod("EndSharedCarry", PublicInstance).Invoke(carry, null);
        Assert.That(fragments[0].GetComponent<Rigidbody>().mass, Is.EqualTo(.8f).Within(.001f),
            "Carry and drop preserve density-derived fragment mass under nondefault tuning.");
    }

    [UnityTest]
    public IEnumerator RepeatedOfflineCutsCreateConfiguredV1FragmentInstances()
    {
        GameObject fragmentPrefab = null;
        UnityEngine.Object productionGeometryProfile = null;
#if UNITY_EDITOR
        fragmentPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Experiments/BridgeDeckAreaV1/Prefabs/WoodenDeckAreaCutFragment.prefab");
        productionGeometryProfile = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
            "Assets/Experiments/BridgeDeckAreaV1/Profiles/BridgeDeckAreaV1.asset");
#else
        Assert.Fail("The production fragment-prefab fixture requires Unity Editor AssetDatabase access.");
#endif
        Assert.That(fragmentPrefab, Is.Not.Null);
        ProfileType.GetField("activeCutSeconds", PublicInstance).SetValue(profile, .1f);
        ProfileType.GetField("fragmentNetworkPrefab", PublicInstance).SetValue(profile, fragmentPrefab);
        ProfileType.GetField("geometryProfile", PublicInstance).SetValue(profile, productionGeometryProfile);
        Component panel = source.GetComponent(PanelType);
        PanelType.GetField("geometryProfile", PrivateInstance).SetValue(panel, productionGeometryProfile);
        var originalPanelInstanceIds = new HashSet<int>();
        foreach (Component existingPanel in FindPanels()) originalPanelInstanceIds.Add(existingPanel.GetInstanceID());
        Assert.That((bool)PanelType.GetMethod("TryBeginCutOffline", PublicInstance)
            .Invoke(panel, new object[] { 1250, 0xA11UL, profile }), Is.True);
        PanelType.GetMethod("AdvanceCutOffline", PublicInstance).Invoke(panel, new object[] { 0xA11UL, .2f, profile });
        bool firstCutCompleted = false;
        try
        {
            firstCutCompleted = (bool)PanelType.GetMethod("TryCompleteOfflineCut", PublicInstance).Invoke(panel, new[] { profile });
        }
        finally
        {
            TrackGeneratedCutPanels(originalPanelInstanceIds);
        }
        yield return null;
        TrackGeneratedCutPanels(originalPanelInstanceIds);
        Assert.That(firstCutCompleted, Is.True);

        Component[] firstCut = FindPanels();
        Assert.That(firstCut.Length, Is.EqualTo(2));
        foreach (Component fragment in firstCut)
            AssertConfiguredV1Fragment(fragment);

        Component recutSource = firstCut[0];
        int recutLength = (int)PanelType.GetProperty("LengthMillimeters", PublicInstance).GetValue(recutSource);
        int minimumFragmentLength = Mathf.CeilToInt((float)productionGeometryProfile.GetType()
            .GetField("minimumFragmentLengthMeters", PublicInstance).GetValue(productionGeometryProfile) * 1000f);
        int kerf = Mathf.RoundToInt((float)productionGeometryProfile.GetType()
            .GetField("sawKerfMeters", PublicInstance).GetValue(productionGeometryProfile) * 1000f);
        int recutOffset = recutLength / 2;
        Assert.That(recutOffset, Is.GreaterThanOrEqualTo(minimumFragmentLength));
        Assert.That(recutLength - recutOffset - kerf, Is.GreaterThanOrEqualTo(minimumFragmentLength));
        float eligibilityDeadline = Time.realtimeSinceStartup + 2f;
        bool eligible = false;
        while (!eligible && Time.realtimeSinceStartup < eligibilityDeadline)
        {
            eligible = (bool)PanelType.GetMethod("IsEligible", PublicInstance)
                .Invoke(recutSource, new object[] { profile, true });
            if (!eligible) yield return null;
        }
        Assert.That(eligible, Is.True,
            "The generated fragment must satisfy production recut eligibility: "
            + PanelType.GetMethod("GetSawCutUnavailableReason", PublicInstance).Invoke(recutSource, new object[] { profile }));
        object[] cutArguments = { recutOffset, profile, null };
        bool cutIsValid = (bool)PanelType.GetMethod("TryGetCut", PublicInstance).Invoke(recutSource, cutArguments);
        Assert.That(cutIsValid, Is.True, $"Invalid recut geometry: fragment={recutLength}mm, offset={recutOffset}mm, "
            + $"minimum={minimumFragmentLength}mm, kerf={kerf}mm, sourceProfile={PanelType.GetField("geometryProfile", PrivateInstance).GetValue(recutSource)}, "
            + $"sawProfile={ProfileType.GetField("geometryProfile", PublicInstance).GetValue(profile)}.");
        object recutState = PanelType.GetProperty("State", PublicInstance).GetValue(recutSource);
        Unity.Netcode.NetworkObject recutNetworkObject = recutSource.GetComponent<Unity.Netcode.NetworkObject>();
        Assert.That((recutNetworkObject == null || !recutNetworkObject.IsSpawned)
            && !(bool)PanelType.GetProperty("CutStarted", PublicInstance).GetValue(recutSource)
            && (ulong)recutState.GetType().GetField("OperatorClientId").GetValue(recutState) == ulong.MaxValue
            && !(bool)PanelType.GetProperty("IsLockedForCompletedArea", PublicInstance).GetValue(recutSource), Is.True,
            $"Unexpected recut state: spawned={recutNetworkObject != null && recutNetworkObject.IsSpawned}, "
            + $"cutStarted={PanelType.GetProperty("CutStarted", PublicInstance).GetValue(recutSource)}, "
            + $"operator={recutState.GetType().GetField("OperatorClientId").GetValue(recutState)}, "
            + $"locked={PanelType.GetProperty("IsLockedForCompletedArea", PublicInstance).GetValue(recutSource)}.");
        bool recutBegan = (bool)PanelType.GetMethod("TryBeginCutOffline", PublicInstance)
            .Invoke(recutSource, new object[] { recutOffset, 0xA12UL, profile });
        Assert.That(recutBegan, Is.True, $"TryBeginCutOffline rejected valid recut: eligible={PanelType.GetMethod("IsEligible", PublicInstance).Invoke(recutSource, new object[] { profile, true })}, "
            + $"reason={PanelType.GetMethod("GetSawCutUnavailableReason", PublicInstance).Invoke(recutSource, new object[] { profile })}, cutValid={cutIsValid}, "
            + $"started={PanelType.GetProperty("CutStarted", PublicInstance).GetValue(recutSource)}, operator={recutState.GetType().GetField("OperatorClientId").GetValue(recutState)}, "
            + $"netSpawned={recutNetworkObject != null && recutNetworkObject.IsSpawned}, locked={PanelType.GetProperty("IsLockedForCompletedArea", PublicInstance).GetValue(recutSource)}.");
        PanelType.GetMethod("AdvanceCutOffline", PublicInstance).Invoke(recutSource, new object[] { 0xA12UL, .2f, profile });
        bool secondCutCompleted = false;
        try
        {
            secondCutCompleted = (bool)PanelType.GetMethod("TryCompleteOfflineCut", PublicInstance).Invoke(recutSource, new[] { profile });
        }
        finally
        {
            TrackGeneratedCutPanels(originalPanelInstanceIds);
        }
        yield return null;
        TrackGeneratedCutPanels(originalPanelInstanceIds);
        Assert.That(secondCutCompleted, Is.True);

        Component[] secondCut = FindPanels();
        Assert.That(secondCut.Length, Is.EqualTo(3), "The second cut replaces one prior fragment with two new prefab instances.");
        foreach (Component fragment in secondCut)
            AssertConfiguredV1Fragment(fragment);
    }

    private static void AssertConfiguredV1Fragment(Component fragment)
    {
        System.Type orientationType = System.Type.GetType("CarryPlacementOrientation, Assembly-CSharp", true);
        Component orientation = fragment.GetComponent(orientationType);
        Assert.That(orientation, Is.Not.Null, "Offline cutting should instantiate the configured V1 fragment prefab.");
        Vector3 offset = (Vector3)orientationType.GetField("offsetEulerDegrees", PrivateInstance).GetValue(orientation);
        Assert.That(offset, Is.EqualTo(new Vector3(0f, 90f, 0f)));
    }

    private void TrackGeneratedCutPanels(HashSet<int> originalPanelInstanceIds)
    {
        foreach (Component fragment in FindPanels())
        {
            if (originalPanelInstanceIds.Contains(fragment.GetInstanceID()) || ownedObjects.Contains(fragment.gameObject)) continue;
            ownedObjects.Add(fragment.gameObject);
        }
    }

    [UnityTest]
    public IEnumerator PartialCutAreaPlateCannotBeReducedToLegacyStorageCount()
    {
        Component panel = source.GetComponent(PanelType);
        object snapshot = PanelType.GetProperty("State", PublicInstance).GetValue(panel);
        snapshot.GetType().GetField("CutStarted", BindingFlags.Instance | BindingFlags.Public).SetValue(snapshot, true);
        PanelType.GetMethod("ApplySnapshot", PrivateInstance).Invoke(panel, new[] { snapshot });
        FieldInfo heldObject = InteractionType.GetField("_pickedUpGameObject", PrivateInstance);
        Assert.That(heldObject, Is.Not.Null);
        heldObject.SetValue(player.GetComponent(InteractionType), source);
        MethodInfo store = InteractionType.GetMethod("TryStoreObject", PublicInstance, null,
            new[] { System.Type.GetType("MainStorageNew, Assembly-CSharp") }, null);
        Assert.That(store, Is.Not.Null);
        Assert.That((bool)store.Invoke(player.GetComponent(InteractionType), new object[] { null }), Is.False);
        Assert.That(source != null, Is.True, "The physical partial cut remains intact in the actor's hands.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator EligibilityRejectsMotionAndTriggerColliderThenAllowsStableStock()
    {
        Component panel = source.GetComponent(PanelType);
        Rigidbody body = source.GetComponent<Rigidbody>();
        BoxCollider collider = source.GetComponent<BoxCollider>();
        yield return new WaitForSecondsRealtime(.05f);
        Assert.That((bool)PanelType.GetMethod("IsEligible", PublicInstance).Invoke(panel, new object[] { profile, true }), Is.True);
        body.linearVelocity = Vector3.right * .051f;
        Assert.That((bool)PanelType.GetMethod("IsEligible", PublicInstance).Invoke(panel, new object[] { profile, true }), Is.False,
            "Motion above the configured threshold rejects a cut.");
        body.linearVelocity = Vector3.zero;
        collider.isTrigger = true;
        Assert.That((bool)PanelType.GetMethod("IsEligible", PublicInstance).Invoke(panel, new object[] { profile, true }), Is.False,
            "A trigger cannot stand in for the physical stock collider.");
        collider.isTrigger = false;
        yield return new WaitForSecondsRealtime(.05f);
        Assert.That((bool)PanelType.GetMethod("IsEligible", PublicInstance).Invoke(panel, new object[] { profile, true }), Is.True);
    }

    [UnityTest]
    public IEnumerator DistributedStaticUndersideSupportOnUnevenGroundRemainsEligible()
    {
        Component panel = source.GetComponent(PanelType);
        floor.GetComponent<Collider>().enabled = false;
        var supportPads = new List<GameObject>();
        float[] zSamples = { -.6f, 0f, .6f };
        float[] xSamples = { 0f, .625f };
        foreach (float z in zSamples)
        foreach (float x in xSamples)
        {
            GameObject pad = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
            pad.name = "Saw B2 uneven static underside support";
            pad.transform.position = source.transform.TransformPoint(new Vector3(x, -.1f, z));
            pad.transform.localScale = new Vector3(.35f, .2f, .35f);
            supportPads.Add(pad);
        }
        Physics.SyncTransforms();
        yield return new WaitForSecondsRealtime(.2f);

        MethodInfo support = PanelType.GetMethod("IsSupportedFromBelow", PublicInstance);
        Assert.That((bool)support.Invoke(panel, new object[] { profile }), Is.True,
            "Six static patches distributed across the underside must qualify even though all four extreme corners are unsupported.");
        Assert.That((bool)PanelType.GetMethod("IsEligible", PublicInstance).Invoke(panel,
            new object[] { profile, true }), Is.True,
            "The shared support result must allow a stable, released, unnailled plate to be cut.");
        Assert.That(PanelType.GetMethod("GetSawCutUnavailableReason", PublicInstance)
            .Invoke(panel, new object[] { profile }), Is.Null,
            "The HUD-facing availability explanation must agree with the cut eligibility gate.");

        foreach (GameObject pad in supportPads) pad.GetComponent<Collider>().enabled = false;
        Vector3[] cornerPositions =
        {
            new(-1.25f, -.1f, -1.2f), new(1.25f, -.1f, -1.2f), new(-1.25f, -.1f, 1.2f)
        };
        var cornerPads = new List<GameObject>();
        foreach (Vector3 localPosition in cornerPositions)
        {
            GameObject pad = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
            pad.name = "Saw B2 three-corner static underside support";
            pad.transform.position = source.transform.TransformPoint(localPosition);
            pad.transform.localScale = new Vector3(.22f, .2f, .22f);
            cornerPads.Add(pad);
        }
        Physics.SyncTransforms();
        Assert.That((bool)support.Invoke(panel, new object[] { profile }), Is.True,
            "Three genuine static underside corner supports must keep the previously accepted layout eligible.");

        foreach (GameObject pad in cornerPads) pad.GetComponent<Collider>().enabled = false;
        GameObject singlePad = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
        singlePad.name = "Saw B2 single-point static underside support";
        singlePad.transform.position = source.transform.TransformPoint(new Vector3(0f, -.1f, 0f));
        singlePad.transform.localScale = new Vector3(.22f, .2f, .22f);
        Physics.SyncTransforms();
        Assert.That((bool)support.Invoke(panel, new object[] { profile }), Is.False,
            "A single underside support point must not satisfy the distributed support gate.");
        string unavailable = (string)PanelType.GetMethod("GetSawCutUnavailableReason", PublicInstance)
            .Invoke(panel, new object[] { profile });
        StringAssert.Contains("support from below", unavailable,
            "The availability prompt must report the same failing support predicate as cut eligibility.");
        StringAssert.Contains("support from below", (string)PanelType.GetMethod(
                "GetSawCutUnavailableReasonForClientReplica", PublicInstance)
            .Invoke(panel, new object[] { profile }),
            "The remote-client HUD support explanation must use the same static underside support gate.");
        singlePad.GetComponent<Collider>().enabled = false;
        Assert.That((bool)support.Invoke(panel, new object[] { profile }), Is.False,
            "Airborne stock must not qualify without underside support.");
        GameObject sideWall = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
        sideWall.name = "Saw B2 side-jammed but unsupported stock";
        sideWall.transform.position = source.transform.TransformPoint(new Vector3(1.4f, .25f, 0f));
        sideWall.transform.localScale = new Vector3(.3f, .7f, 2.2f);
        Physics.SyncTransforms();
        Assert.That((bool)support.Invoke(panel, new object[] { profile }), Is.False,
            "A vertical side contact must not count as support from below.");

        foreach (GameObject pad in supportPads)
        {
            pad.GetComponent<Collider>().enabled = true;
            Rigidbody dynamicSupport = pad.AddComponent<Rigidbody>();
            dynamicSupport.useGravity = false;
        }
        Physics.SyncTransforms();
        Assert.That((bool)support.Invoke(panel, new object[] { profile }), Is.False,
            "Dynamic bodies cannot substitute for static ground support.");
        foreach (GameObject pad in supportPads)
        {
            pad.GetComponent<Rigidbody>().isKinematic = true;
            pad.GetComponent<Collider>().isTrigger = true;
        }
        Physics.SyncTransforms();
        Assert.That((bool)support.Invoke(panel, new object[] { profile }), Is.False,
            "Trigger colliders cannot substitute for physical underside support.");
        foreach (GameObject pad in supportPads)
        {
            pad.GetComponent<Collider>().isTrigger = false;
            pad.AddComponent(System.Type.GetType("PlayerHealth, Assembly-CSharp"));
        }
        Physics.SyncTransforms();
        Assert.That((bool)support.Invoke(panel, new object[] { profile }), Is.False,
            "Player colliders cannot be treated as static ground support.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator RealPlayerNewShowsEquippedProductionDeckSawInFirstPersonHand()
    {
        GameObject playerPrefab = LoadEditorAsset<GameObject>("Assets/Prefabs/PlayerNew.prefab");
        ScriptableObject productionSawItem = LoadEditorAsset<ScriptableObject>(
            "Assets/Experiments/BridgeDeckAreaV1/Profiles/BridgeDeckSaw.asset");
        Assert.That(playerPrefab, Is.Not.Null);
        Assert.That(productionSawItem, Is.Not.Null);
        GameObject actualPlayer = Own(Object.Instantiate(playerPrefab));
        Component actualInventory = actualPlayer.GetComponent(InventoryType);
        Assert.That(actualInventory, Is.Not.Null);
        Assert.That((bool)InventoryType.GetMethod("AddItem", PublicInstance).Invoke(actualInventory,
            new[] { productionSawItem }), Is.True);
        yield return null;
        yield return null;

        Component arms = actualPlayer.GetComponent(System.Type.GetType("PlayerFirstPersonArms, Assembly-CSharp"));
        Assert.That(arms, Is.Not.Null);
        GameObject handVisual = (GameObject)arms.GetType().GetField("rightToolVisual", PrivateInstance).GetValue(arms);
        Assert.That(handVisual, Is.Not.Null,
            "Equipping the real DeckSaw asset on PlayerNew must build a first-person hand visual.");
        Assert.That(handVisual.activeInHierarchy, Is.True);
        Assert.That(handVisual.GetComponentsInChildren<Renderer>(true), Is.Not.Empty,
            "The equipped hand visual must contain renderable geometry.");
    }

    [UnityTest]
    public IEnumerator WallAndOutOfRangeAimCannotStartAnOfflineCut()
    {
        Component panel = source.GetComponent(PanelType);
        GameObject wall = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
        wall.name = "Saw B2 aim blocker";
        wall.transform.position = new Vector3(0f, .78f, .56f);
        wall.transform.localScale = new Vector3(.8f, .8f, .12f);
        cameraObject.transform.rotation = Quaternion.LookRotation(new Vector3(0f, .3f, 1.2f) - cameraObject.transform.position);
        Physics.SyncTransforms();
        Assert.That(Physics.Raycast(cameraObject.transform.position, cameraObject.transform.forward, out RaycastHit blockedHit, 2.5f), Is.True);
        Assert.That(blockedHit.collider.gameObject, Is.SameAs(wall));
        QueueLeft(true);
        yield return new WaitForSeconds(.1f);
        QueueLeft(false);
        Assert.That((bool)PanelType.GetProperty("CutStarted", PublicInstance).GetValue(panel), Is.False,
            "A wall between the operator and board must block the cut attempt.");

        wall.SetActive(false);
        cameraObject.transform.position = new Vector3(0f, 1.2f, -1.55f);
        cameraObject.transform.rotation = Quaternion.LookRotation(new Vector3(0f, .3f, 1.2f) - cameraObject.transform.position);
        Physics.SyncTransforms();
        Assert.That(Physics.Raycast(cameraObject.transform.position, cameraObject.transform.forward, out RaycastHit distantHit, 3f), Is.True);
        Assert.That(distantHit.collider.GetComponent(PanelType), Is.SameAs(panel));
        Assert.That(distantHit.distance, Is.GreaterThan(2.5f));
        QueueLeft(true);
        yield return new WaitForSeconds(.1f);
        QueueLeft(false);
        Assert.That((bool)PanelType.GetProperty("CutStarted", PublicInstance).GetValue(panel), Is.False,
            "The 2.5m maximum range must be enforced by the actual aim path.");
    }

    [UnityTest]
    public IEnumerator FailedOfflineFragmentCreationRollsBackAndRetainsTheSource()
    {
        Component panel = source.GetComponent(PanelType);
        GameObject malformedPrefab = Own(new GameObject("Saw B2 malformed fragment prefab"));
        ProfileType.GetField("fragmentNetworkPrefab", PublicInstance).SetValue(profile, malformedPrefab);
        Assert.That((bool)PanelType.GetMethod("TryBeginCutOffline", PublicInstance)
            .Invoke(panel, new object[] { 1250, 0x2222UL, profile }), Is.True);
        PanelType.GetMethod("AdvanceCutOffline", PublicInstance)
            .Invoke(panel, new object[] { 0x2222UL, .25f, profile });
        Assert.That((bool)PanelType.GetMethod("IsCutComplete", PublicInstance).Invoke(panel, new object[] { profile }), Is.True);
        var rollbackLogHandler = new ExpectedRollbackLogHandler(Debug.unityLogger.logHandler);
        ILogHandler previousLogHandler = Debug.unityLogger.logHandler;
        bool completed;
        Debug.unityLogger.logHandler = rollbackLogHandler;
        try
        {
            completed = (bool)PanelType.GetMethod("TryCompleteOfflineCut", PublicInstance)
                .Invoke(panel, new object[] { profile });
        }
        finally
        {
            Debug.unityLogger.logHandler = previousLogHandler;
        }
        Assert.That(completed, Is.False);
        Assert.That(rollbackLogHandler.CapturedErrors, Is.EqualTo(new[]
        {
            "Offline deck plate cut transaction rolled back: Fragment source requires BridgeDeckAreaPanel."
        }), "The test should capture exactly the expected rollback diagnostic and forward all other log events.");
        Assert.That(source, Is.Not.Null, "If either fragment cannot be created, the original panel must remain intact.");
        Assert.That(FindPanels().Length, Is.EqualTo(1), "A failed transaction must clean up partial output fragments.");
        Assert.That((bool)PanelType.GetProperty("CutStarted", PublicInstance).GetValue(panel), Is.True);
        Assert.That((int)PanelType.GetProperty("ProgressMilliseconds", PublicInstance).GetValue(panel), Is.GreaterThanOrEqualTo(200));
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator Teardown()
    {
        foreach (Component panel in FindPanels())
            if (panel != null && panel.name.StartsWith("Saw B2", System.StringComparison.Ordinal)) Object.Destroy(panel.gameObject);
        foreach (GameObject item in ownedObjects) if (item != null) Object.Destroy(item);
        if (profile != null) Object.Destroy(profile);
        if (canonicalProfile != null) Object.Destroy(canonicalProfile);
        if (canonicalProfile != null) Object.Destroy(canonicalProfile);
        if (sawItem != null) Object.Destroy(sawItem);
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        inputSettings.Restore();
        yield return null;
    }

    private GameObject Own(GameObject value) { ownedObjects.Add(value); return value; }

    private string ReadSawPresentation(Component action, Component panel, out bool active)
    {
        object[] args = { panel, null, 0f, false };
        Assert.That((bool)ActionType.GetMethod("TryGetSawCutPresentation", PublicInstance).Invoke(action, args), Is.True,
            "The equipped Saw should keep presenting the aimed panel.");
        active = (bool)args[3];
        return (string)args[1];
    }

    private static T LoadEditorAsset<T>(string path) where T : Object
    {
        System.Type assetDatabase = System.Type.GetType("UnityEditor.AssetDatabase, UnityEditor.CoreModule")
            ?? System.Type.GetType("UnityEditor.AssetDatabase, UnityEditor");
        Assert.That(assetDatabase, Is.Not.Null, "The production asset load is only available in the Editor test runner.");
        MethodInfo load = null;
        foreach (MethodInfo candidate in assetDatabase.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (candidate.Name == "LoadAssetAtPath" && candidate.IsGenericMethodDefinition
                && candidate.GetParameters().Length == 1 && candidate.GetParameters()[0].ParameterType == typeof(string))
            {
                load = candidate;
                break;
            }
        }
        Assert.That(load, Is.Not.Null);
        return (T)load.MakeGenericMethod(typeof(T)).Invoke(null, new object[] { path });
    }

    private static Component[] FindPanels()
    {
        Object[] objects = Object.FindObjectsByType(PanelType, FindObjectsSortMode.None);
        var panels = new List<Component>();
        foreach (Object item in objects) if (item is Component panel) panels.Add(panel);
        return panels.ToArray();
    }

    private InputAction ConfigureInputDevices(Component input)
    {
        object generated = InputType.GetField("playerGameInputActions", PrivateInstance).GetValue(input);
        InputActionAsset asset = (InputActionAsset)generated.GetType().GetProperty("asset").GetValue(generated);
        asset.devices = new InputDevice[] { keyboard, mouse };
        InputSystem.Update();
        object game = generated.GetType().GetProperty("Game", PublicInstance).GetValue(generated);
        return (InputAction)game.GetType().GetProperty("Action", PublicInstance).GetValue(game);
    }

    private void QueueLeft(bool pressed)
    {
        MouseState state = default;
        state = state.WithButton(MouseButton.Left, pressed);
        InputSystem.QueueStateEvent(mouse, state);
        InputSystem.Update();
    }

    private static void AssertClosedOutwardBoxMesh(GameObject fragment)
    {
        Mesh mesh = fragment.GetComponent<MeshFilter>().sharedMesh;
        Assert.That(mesh, Is.Not.Null);
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector2[] uv = mesh.uv;
        int[] triangles = mesh.triangles;
        Assert.That(vertices.Length, Is.EqualTo(24));
        Assert.That(triangles.Length, Is.EqualTo(36));
        var edgeCounts = new Dictionary<string, int>();
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
            Assert.That(Vector3.Cross(b - a, c - a).sqrMagnitude, Is.GreaterThan(1e-8f), "No degenerate triangle may leave a hole.");
            Vector3 center = (a + b + c) / 3f;
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            Assert.That(Vector3.Dot(normal, center - mesh.bounds.center),
                Is.GreaterThan(0f), "Every face normal must point away from the box interior.");
            AddEdge(edgeCounts, a, b); AddEdge(edgeCounts, b, c); AddEdge(edgeCounts, c, a);
        }
        foreach (KeyValuePair<string, int> edge in edgeCounts)
            Assert.That(edge.Value, Is.EqualTo(2), $"Each welded geometric edge must occur exactly twice: {edge.Key}");
        Assert.That(normals.Length, Is.EqualTo(vertices.Length));
        for (int face = 0; face < 6; face++)
        {
            int first = face * 4;
            float worldU = Vector3.Distance(vertices[first], vertices[first + 1]);
            float worldV = Vector3.Distance(vertices[first], vertices[first + 3]);
            float textureU = Vector2.Distance(uv[first], uv[first + 1]);
            float textureV = Vector2.Distance(uv[first], uv[first + 3]);
            Assert.That(textureU, Is.EqualTo(worldU).Within(.00001f), "U scale must remain one tile per meter on every face.");
            Assert.That(textureV, Is.EqualTo(worldV).Within(.00001f), "V scale must remain one tile per meter on every face.");
        }
    }

    private static void AddEdge(Dictionary<string, int> counts, Vector3 a, Vector3 b)
    {
        string first = PointKey(a), second = PointKey(b);
        string key = string.CompareOrdinal(first, second) <= 0 ? first + "|" + second : second + "|" + first;
        counts.TryGetValue(key, out int count);
        counts[key] = count + 1;
    }

    private static IEnumerator Until(System.Func<bool> condition, string message)
    {
        float deadline = Time.realtimeSinceStartup + 12f;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, $"Timed out waiting for {message}.");
    }

    private static string PointKey(Vector3 point) => $"{Mathf.RoundToInt(point.x * 100000)},{Mathf.RoundToInt(point.y * 100000)},{Mathf.RoundToInt(point.z * 100000)}";

    private sealed class ExpectedRollbackLogHandler : ILogHandler
    {
        private const string ExpectedError = "Offline deck plate cut transaction rolled back: Fragment source requires BridgeDeckAreaPanel.";
        private readonly ILogHandler inner;
        public readonly List<string> CapturedErrors = new();

        public ExpectedRollbackLogHandler(ILogHandler inner) => this.inner = inner;

        public void LogFormat(LogType logType, Object context, string format, params object[] args)
        {
            string message = string.Format(format, args);
            if (logType == LogType.Error && message == ExpectedError)
            {
                CapturedErrors.Add(message);
                return;
            }
            inner.LogFormat(logType, context, format, args);
        }

        public void LogException(System.Exception exception, Object context) => inner.LogException(exception, context);
    }
}
