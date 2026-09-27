using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class ConstructionBookPlayModeTests
{
    private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
    private static Type T(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static FieldInfo F(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static MethodInfo M(Type type, string name) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    [UnityTearDown] public IEnumerator TearDown(){foreach(UnityEngine.Object item in created)if(item!=null)UnityEngine.Object.Destroy(item);created.Clear();yield return null;}

    [UnityTest] public IEnumerator ViewBuildsFourPlayerLayoutWithDisabledCheckboxesHighlightAndSpinePivot()
    {
        GameObject root=Track(new GameObject("BookView",typeof(RectTransform)));RectTransform rootRect=root.GetComponent<RectTransform>();rootRect.sizeDelta=new Vector2(2048f,1400f);Component view=root.AddComponent(T("ConstructionBookView"));T("ConstructionBookView").GetMethod("Build").Invoke(view,new object[]{new Color(.8f,.7f,.5f),Color.black});
        object entry=Entry("Foundation",6);IList roster=(IList)T("ConstructionBookRoster").GetMethod("Build").Invoke(null,new object[]{new List<ulong>{9,0,7,3},true,(ulong)0});object materials=Activator.CreateInstance(typeof(List<>).MakeGenericType(T("ConstructionBookMaterialLine")));
        T("ConstructionBookView").GetMethod("Render").Invoke(view,new object[]{entry,2,0,6,materials,true,roster,(ulong)7,null,null,null,true});Canvas.ForceUpdateCanvases();yield return null;Canvas.ForceUpdateCanvases();
        RectTransform pivot=(RectTransform)T("ConstructionBookView").GetProperty("TurningPagePivot").GetValue(view);Assert.AreEqual(0f,pivot.pivot.x,.001f);Assert.AreEqual(new Vector2(.5f,0f),pivot.anchorMin);Assert.AreEqual(Vector2.one,pivot.anchorMax);Assert.AreEqual(Vector2.zero,pivot.offsetMin);Assert.AreEqual(Vector2.zero,pivot.offsetMax);Assert.IsNotNull(pivot.GetComponent<Image>());
        Toggle[] toggles=root.GetComponentsInChildren<Toggle>(true);Assert.AreEqual(24,toggles.Length);foreach(Toggle toggle in toggles){Assert.IsFalse(toggle.interactable);Assert.IsFalse(toggle.isOn);Assert.AreEqual(Selectable.Transition.None,toggle.transition);Assert.IsNotNull(toggle.targetGraphic);Assert.IsNotNull(toggle.graphic);Assert.AreEqual(Color.clear,toggle.graphic.color);Assert.AreEqual(new Vector2(18f,18f),((RectTransform)toggle.transform).rect.size);}
        Transform[] playerRows=Array.FindAll(root.GetComponentsInChildren<Transform>(true),item=>item.name=="Players");Assert.AreEqual(6,playerRows.Length);foreach(Transform row in playerRows){Assert.AreEqual(4,row.childCount);foreach(Transform cell in row){Assert.AreEqual("Player",cell.name);Assert.AreEqual(2,cell.childCount);Assert.AreEqual(new Vector2(64f,40f),((RectTransform)cell).rect.size);Assert.Greater(cell.GetChild(0).localPosition.y,cell.GetChild(1).localPosition.y);}}
        RectTransform[] icons=Array.FindAll(root.GetComponentsInChildren<RectTransform>(true),item=>item.name=="Icon"&&item.IsChildOf(root.transform.Find("Right/Steps")));Assert.AreEqual(6,icons.Length);foreach(RectTransform icon in icons)Assert.AreEqual(new Vector2(22f,22f),icon.rect.size);
        RectTransform steps=(RectTransform)root.transform.Find("Right/Steps");foreach(RectTransform child in steps.GetComponentsInChildren<RectTransform>(true)){if(child==steps)continue;Vector3[] corners=new Vector3[4];child.GetWorldCorners(corners);for(int c=0;c<4;c++){Vector3 localPoint=steps.InverseTransformPoint(corners[c]);Assert.That(localPoint.x,Is.InRange(steps.rect.xMin-.01f,steps.rect.xMax+.01f),child.name+" x bounds");Assert.That(localPoint.y,Is.InRange(steps.rect.yMin-.01f,steps.rect.yMax+.01f),child.name+" y bounds");}}
        Type textType=Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro",true);Component[] texts=root.GetComponentsInChildren(textType,true);Component local=Array.Find(texts,text=>(string)textType.GetProperty("text").GetValue(text)=="Player 3"),remote=Array.Find(texts,text=>(string)textType.GetProperty("text").GetValue(text)=="Player 2");Assert.IsNotNull(local);Assert.IsNotNull(remote);Assert.AreNotEqual(textType.GetProperty("color").GetValue(local),textType.GetProperty("color").GetValue(remote));
    }

    [UnityTest] public IEnumerator UiLeftAndRightHandlersTurnExactlyOnceWithoutSendMessageCollisionMethods()
    {
        LogAssert.Expect(LogType.Warning,"ConstructionBook: missing direct Carpenter recipe for ''; spread retained with Data unavailable.");Component book=CreateBook("First","Second","Third"),ui=CreatePlayerUi(out Component input,out _);yield return null;T("PlayerConstructionBookUI").GetMethod("Open").Invoke(ui,new object[]{book});
        Assert.IsNull(M(T("PlayerConstructionBookUI"),"OnLeft"));Assert.IsNull(M(T("PlayerConstructionBookUI"),"OnRight"));int changes=0;T("ConstructionBookController").GetEvent("PageChanged").AddEventHandler(book,new Action<int,bool>((_,snap)=>{if(!snap)changes++;}));
        ((EventHandler)F(T("PlayerInputNew"),"OnUI_Right").GetValue(input))?.Invoke(input,EventArgs.Empty);yield return new WaitForSecondsRealtime(.4f);Assert.AreEqual(1,T("ConstructionBookController").GetProperty("CurrentIndex").GetValue(book));Assert.AreEqual(1,changes);
        ((EventHandler)F(T("PlayerInputNew"),"OnUI_Left").GetValue(input))?.Invoke(input,EventArgs.Empty);yield return new WaitForSecondsRealtime(.4f);Assert.AreEqual(0,T("ConstructionBookController").GetProperty("CurrentIndex").GetValue(book));Assert.AreEqual(2,changes);LogAssert.NoUnexpectedReceived();
    }

    [UnityTest] public IEnumerator PrefabWorldCanvasIsParallelAndReadableFromAnchorLocalNegativeZ()
    {
        GameObject prefab=Resources.Load<GameObject>("__not_used__");
#if UNITY_EDITOR
        prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/ConstructionBook.prefab");
#endif
        Assert.IsNotNull(prefab);GameObject instance=Track(UnityEngine.Object.Instantiate(prefab));yield return null;
        Transform anchor=instance.transform.Find("PageSurfaceAnchor"),pages=anchor!=null?anchor.Find("WorldPages"):null;Assert.IsNotNull(anchor);Assert.IsNotNull(pages);
        Assert.AreEqual(Vector3.zero,pages.localPosition);Assert.Less(Quaternion.Angle(Quaternion.identity,pages.localRotation),.001f);Assert.AreEqual(new Vector3(.00048f,-.00048f,.00048f),pages.localScale);
        Quaternion pageRotation=Quaternion.Euler(35f,0f,0f);Vector3 physicalNormal=instance.transform.TransformDirection(pageRotation*Vector3.up),physicalTop=instance.transform.TransformDirection(pageRotation*Vector3.forward);
        Assert.Greater(Vector3.Dot(anchor.forward,physicalNormal),.999f);Assert.Greater(Vector3.Dot(anchor.up,physicalTop),.999f);Assert.Greater(Vector3.Dot(pages.TransformVector(Vector3.up).normalized,-physicalTop),.999f);Assert.Greater(Vector3.Dot(pages.TransformVector(Vector3.right).normalized,anchor.right),.999f);Assert.Greater(Vector3.Dot((pages.position-pages.forward)-pages.position,-pages.forward),0f);
        RectTransform left=(RectTransform)pages.Find("Left"),right=(RectTransform)pages.Find("Right");Assert.IsNotNull(left);Assert.IsNotNull(right);Vector3 contentRight=pages.TransformVector(Vector3.right).normalized;Vector3 canvasCenter=((RectTransform)pages).TransformPoint(((RectTransform)pages).rect.center);Assert.Less(Vector3.Dot(left.TransformPoint(left.rect.center)-canvasCenter,contentRight),0f);Assert.Greater(Vector3.Dot(right.TransformPoint(right.rect.center)-canvasCenter,contentRight),0f);Component worldView=pages.GetComponent(T("ConstructionBookView"));Component title=(Component)F(T("ConstructionBookView"),"title").GetValue(worldView);RectTransform stepsRoot=(RectTransform)F(T("ConstructionBookView"),"stepsRoot").GetValue(worldView);Assert.AreSame(left,title.transform.parent);Assert.AreSame(right,stepsRoot.parent);
        RectTransform rect=pages.GetComponent<RectTransform>();Assert.AreEqual(new Vector2(2048f,1400f),rect.sizeDelta);
    }

    [UnityTest] public IEnumerator OpenCloseTogglesGameplayUiCursorAndResetsTransition()
    {
        Component book=CreateBook("A","B"),ui=CreatePlayerUi(out Component input,out _);yield return null;T("PlayerConstructionBookUI").GetMethod("Open").Invoke(ui,new object[]{book});
        Assert.IsTrue((bool)T("PlayerInputNew").GetProperty("IsGameplayUiOpen").GetValue(input));Assert.AreEqual(CursorLockMode.None,Cursor.lockState);M(T("PlayerConstructionBookUI"),"OnPageChanged").Invoke(ui,new object[]{1,false});yield return null;T("PlayerConstructionBookUI").GetMethod("Close").Invoke(ui,null);
        Assert.IsFalse((bool)T("PlayerInputNew").GetProperty("IsGameplayUiOpen").GetValue(input));Assert.IsNull(F(T("PlayerConstructionBookUI"),"transition").GetValue(ui));CanvasGroup group=(CanvasGroup)F(T("PlayerConstructionBookUI"),"group").GetValue(ui);RectTransform panel=(RectTransform)F(T("PlayerConstructionBookUI"),"panel").GetValue(ui);Assert.AreEqual(1f,group.alpha);Assert.AreEqual(Vector2.zero,panel.anchoredPosition);Assert.IsFalse(panel.gameObject.activeSelf);
    }

    [UnityTest] public IEnumerator NavigationClampsAndSwapsContentAfterAnimation()
    {
        Component book=CreateBook("First","Second");yield return null;T("ConstructionBookController").GetMethod("RequestTurn").Invoke(book,new object[]{-1,null});yield return new WaitForSecondsRealtime(.15f);Assert.AreEqual(0,T("ConstructionBookController").GetProperty("CurrentIndex").GetValue(book));
        T("ConstructionBookController").GetMethod("RequestTurn").Invoke(book,new object[]{1,null});yield return new WaitForSecondsRealtime(.4f);Assert.AreEqual(1,T("ConstructionBookController").GetProperty("CurrentIndex").GetValue(book));StringAssert.Contains("Second",(string)T("ConstructionBookController").GetMethod("GetLeftPageText").Invoke(book,null));
        T("ConstructionBookController").GetMethod("RequestTurn").Invoke(book,new object[]{1,null});yield return new WaitForSecondsRealtime(.15f);Assert.AreEqual(1,T("ConstructionBookController").GetProperty("CurrentIndex").GetValue(book));
    }

    [UnityTest] public IEnumerator DesiredAssignmentsAreIdempotentMultiPlayerAndRefreshMarksWithoutTurningPage()
    {
        LogAssert.Expect(LogType.Warning,"ConstructionBook: missing direct Carpenter recipe for ''; spread retained with Data unavailable.");
        Component book=CreateBook("First","Second");yield return null;
        Type rosterType=T("ConstructionBookRoster");IList ids=(IList)F(T("ConstructionBookController"),"rosterIds").GetValue(book);ids.Add((ulong)0);ids.Add((ulong)7);
        F(T("ConstructionBookController"),"roster").SetValue(book,rosterType.GetMethod("Build").Invoke(null,new object[]{new List<ulong>{0,7},true,(ulong)0}));
        int pageChanges=0;T("ConstructionBookController").GetEvent("PageChanged").AddEventHandler(book,new Action<int,bool>((_,__)=>pageChanges++));
        MethodInfo request=T("ConstructionBookController").GetMethod("RequestAssignment");
        request.Invoke(book,new object[]{0,0,(ulong)0,true});request.Invoke(book,new object[]{0,0,(ulong)0,true});request.Invoke(book,new object[]{0,0,(ulong)7,true});
        Assert.IsTrue((bool)T("ConstructionBookController").GetMethod("IsAssigned").Invoke(book,new object[]{0,0,(ulong)0}));
        Assert.IsTrue((bool)T("ConstructionBookController").GetMethod("IsAssigned").Invoke(book,new object[]{0,0,(ulong)7}));
        object assignments=F(T("ConstructionBookController"),"assignments").GetValue(book);Assert.AreEqual(2,(int)assignments.GetType().GetProperty("Count").GetValue(assignments));
        StringAssert.Contains("☑ Host",(string)T("ConstructionBookController").GetMethod("GetRightPageText").Invoke(book,null));
        request.Invoke(book,new object[]{0,4,(ulong)0,true});request.Invoke(book,new object[]{0,0,(ulong)99,true});Assert.AreEqual(2,(int)assignments.GetType().GetProperty("Count").GetValue(assignments));
        Assert.AreEqual(0,pageChanges,"Assignment refresh must not issue a page turn.");
        yield return null;
        Toggle[] worldToggles=book.GetComponentsInChildren<Toggle>(true);Assert.AreEqual(2,worldToggles.Length);Assert.IsFalse(worldToggles[0].interactable);Assert.AreNotEqual(Color.clear,worldToggles[0].graphic.color);
        Component ui=CreatePlayerUi(out _,out _);T("PlayerConstructionBookUI").GetMethod("Open").Invoke(ui,new object[]{book});Toggle[] screenToggles=ui.GetComponentsInChildren<Toggle>(true);Assert.AreEqual(2,screenToggles.Length);Assert.IsTrue(screenToggles[0].interactable);
        screenToggles[1].isOn=false;Assert.IsFalse((bool)T("ConstructionBookController").GetMethod("IsAssigned").Invoke(book,new object[]{0,0,(ulong)7}));yield return null;
        screenToggles=ui.GetComponentsInChildren<Toggle>(true);T("ConstructionBookController").GetMethod("RequestTurn").Invoke(book,new object[]{1,ui.transform});screenToggles[1].isOn=true;Assert.IsFalse((bool)T("ConstructionBookController").GetMethod("IsAssigned").Invoke(book,new object[]{0,0,(ulong)7}));
        T("PlayerConstructionBookUI").GetMethod("Close").Invoke(ui,null);
        T("ConstructionBookController").GetMethod("SnapTo",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(book,new object[]{0});object catalog=F(T("ConstructionBookController"),"catalog").GetValue(book);Array entries=(Array)F(T("ConstructionBookCatalogSO"),"entries").GetValue(catalog);object secondEntry=entries.GetValue(1);Array nullStep=Array.CreateInstance(T("ConstructionBookCatalogSO+Step"),1);F(T("ConstructionBookCatalogSO+Entry"),"steps").SetValue(secondEntry,nullStep);request.Invoke(book,new object[]{1,0,(ulong)0,true});Assert.AreEqual(1,(int)assignments.GetType().GetProperty("Count").GetValue(assignments));
        request.Invoke(book,new object[]{0,0,(ulong)0,false});Assert.AreEqual(0,(int)assignments.GetType().GetProperty("Count").GetValue(assignments));
    }

    [UnityTest]
    public IEnumerator MyTaskQueryUsesComponentIdentityLocalClientAndOriginalStepOrder()
    {
        Component book = CreateBook("Same part", "Same part");
        yield return null;

        Type controllerType = T("ConstructionBookController");
        Type componentType = T("BridgeComponentSO");
        IList spreadList = (IList)F(controllerType, "spreads").GetValue(book);
        object firstSpread = spreadList[0];
        object secondSpread = spreadList[1];
        object firstComponent = firstSpread.GetType().GetField("Component").GetValue(firstSpread);
        object secondComponent = secondSpread.GetType().GetField("Component").GetValue(secondSpread);
        Assert.AreNotSame(firstComponent, secondComponent);

        Type catalogType = T("ConstructionBookCatalogSO");
        Type entryType = T("ConstructionBookCatalogSO+Entry");
        Array entries = (Array)F(catalogType, "entries").GetValue(F(controllerType, "catalog").GetValue(book));
        F(entryType, "title").SetValue(entries.GetValue(0), "Same part");
        F(entryType, "title").SetValue(entries.GetValue(1), "Same part");
        for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
        {
            object entry = entries.GetValue(entryIndex);
            Type stepType = T("ConstructionBookCatalogSO+Step");
            Type requirementType = T("ConstructionBookCatalogSO+Requirement");
            Array steps = Array.CreateInstance(stepType, 3);
            for (int stepIndex = 0; stepIndex < steps.Length; stepIndex++)
            {
                object step = steps.GetValue(stepIndex);
                if (step == null)
                {
                    step = Activator.CreateInstance(stepType);
                }
                F(stepType, "instruction").SetValue(step, $"Instruction {stepIndex + 1}");
                object requirement = Activator.CreateInstance(requirementType);
                F(requirementType, "displayName").SetValue(requirement, $"Requirement {stepIndex + 1}");
                Array requirements = Array.CreateInstance(requirementType, 1);
                requirements.SetValue(requirement, 0);
                F(stepType, "requirements").SetValue(step, requirements);
                steps.SetValue(step, stepIndex);
            }
            F(entryType, "steps").SetValue(entry, steps);
        }

        IList rosterIds = (IList)F(controllerType, "rosterIds").GetValue(book);
        rosterIds.Add((ulong)0);
        rosterIds.Add((ulong)7);
        MethodInfo request = M(controllerType, "RequestAssignment");
        request.Invoke(book, new object[] { 0, 0, (ulong)0, true });
        request.Invoke(book, new object[] { 0, 2, (ulong)0, true });
        request.Invoke(book, new object[] { 0, 1, (ulong)7, true });
        request.Invoke(book, new object[] { 1, 1, (ulong)0, true });

        Type setType = typeof(HashSet<>).MakeGenericType(componentType);
        object currentTypes = Activator.CreateInstance(setType);
        setType.GetMethod("Add").Invoke(currentTypes, new[] { firstComponent });
        StringBuilder output = new StringBuilder();
        M(controllerType, "AppendAssignedTasks").Invoke(book, new object[] { (ulong)0, currentTypes, output });

        StringAssert.Contains("Same part\n  Step 1: Instruction 1 [Requirement 1]", output.ToString());
        StringAssert.Contains("  Step 3: Instruction 3 [Requirement 3]", output.ToString());
        StringAssert.DoesNotContain("Instruction 2", output.ToString(), "The other player's assignment must stay private.");
        Assert.AreEqual(2, output.ToString().Split(new[] { "Step " }, StringSplitOptions.None).Length - 1);

        setType.GetMethod("Clear").Invoke(currentTypes, null);
        setType.GetMethod("Add").Invoke(currentTypes, new[] { secondComponent });
        output.Length = 0;
        M(controllerType, "AppendAssignedTasks").Invoke(book, new object[] { (ulong)0, currentTypes, output });
        StringAssert.Contains("Same part\n  Step 2: Instruction 2 [Requirement 2]", output.ToString());
        Assert.AreEqual(1, output.ToString().Split(new[] { "Step " }, StringSplitOptions.None).Length - 1);
    }

    [UnityTest]
    public IEnumerator BridgeRequirementsWheelScrollIsVisibleLocalAndSubscriptionSafe()
    {
        GameObject root = Track(new GameObject("RequirementsCanvas", typeof(RectTransform), typeof(Canvas)));
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(1000f, 800f);
        Component input = root.AddComponent(T("PlayerInputNew"));
        Component requirementsUi = root.AddComponent(T("BridgeRequirementsUI"));
        yield return null;

        Type uiType = T("BridgeRequirementsUI");
        M(uiType, "SetVisible").Invoke(requirementsUi, new object[] { true });
        RectTransform panel = (RectTransform)((GameObject)F(uiType, "panelRoot").GetValue(requirementsUi)).transform;
        Assert.AreEqual(new Vector2(-24f, -120f), panel.anchoredPosition);
        Assert.AreEqual(new Vector2(380f, 430f), panel.sizeDelta);
        Component tasksText = (Component)F(uiType, "myTasksText").GetValue(requirementsUi);
        Type textType = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro", true);
        textType.GetProperty("text").SetValue(tasksText, string.Join("\n", new string[90].Select((_, index) => $"Long task line {index}")));
        Canvas.ForceUpdateCanvases();
        RectTransform content = (RectTransform)F(uiType, "contentRoot").GetValue(requirementsUi);
        ScrollRect scrollRect = (ScrollRect)F(uiType, "scrollRect").GetValue(requirementsUi);
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        Canvas.ForceUpdateCanvases();
        Assert.Greater(content.rect.height, scrollRect.viewport.rect.height, "The panel body should scroll when the authored task list is long.");

        MethodInfo routeScroll = M(T("PlayerInputNew"), "RouteBridgeRequirementsScroll");
        routeScroll.Invoke(input, new object[] { new Vector2(0f, -1f) });
        float firstPosition = scrollRect.verticalNormalizedPosition;
        Assert.That(firstPosition, Is.EqualTo(0.92f).Within(0.02f));

        ((Behaviour)requirementsUi).enabled = false;
        ((Behaviour)requirementsUi).enabled = true;
        M(uiType, "SetVisible").Invoke(requirementsUi, new object[] { true });
        textType.GetProperty("text").SetValue(tasksText, string.Join("\n", Enumerable.Range(0, 90).Select(index => $"Long task line {index}")));
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        Canvas.ForceUpdateCanvases();
        routeScroll.Invoke(input, new object[] { new Vector2(0f, -1f) });
        float afterReenable = scrollRect.verticalNormalizedPosition;
        Assert.That(afterReenable, Is.EqualTo(0.92f).Within(0.02f), "Repeated enable must not duplicate wheel subscriptions.");

        F(T("PlayerInputNew"), "IsUIOpened").SetValue(input, true);
        routeScroll.Invoke(input, new object[] { new Vector2(0f, -1f) });
        Assert.That(scrollRect.verticalNormalizedPosition, Is.EqualTo(afterReenable).Within(0.001f), "Other gameplay UI must own the wheel input.");
        F(T("PlayerInputNew"), "IsUIOpened").SetValue(input, false);

        M(uiType, "SetVisible").Invoke(requirementsUi, new object[] { false });
        routeScroll.Invoke(input, new object[] { new Vector2(0f, -1f) });
        Assert.That(scrollRect.verticalNormalizedPosition, Is.EqualTo(afterReenable).Within(0.001f), "A hidden panel must not consume wheel input.");

        M(uiType, "Refresh").Invoke(requirementsUi, new object[] { true });
        Assert.That(content.anchoredPosition.y, Is.EqualTo(0f).Within(0.5f), "Opening or explicitly resetting the panel returns to the top.");
    }

    [UnityTest]
    public IEnumerator LiveTaskRefreshPreservesPixelOffsetAndStageChangeResetsToTop()
    {
        Component manager = CreateConfiguredGameplayManager();
        string[] titles = Enumerable.Range(0, 20).Select(index => $"Part {index + 1:00}").ToArray();
        LogAssert.Expect(LogType.Warning, "ConstructionBook: missing direct Carpenter recipe for ''; spread retained with Data unavailable.");
        Component book = CreateBookWithStepCount(6, titles);
        ConfigureGameplayManagerForBook(manager, book, titles.Length);

        Type controllerType = T("ConstructionBookController");
        Type assignmentType = T("ConstructionBookAssignment");
        object networkAssignments = F(controllerType, "assignments").GetValue(book);
        MethodInfo addAssignment = networkAssignments.GetType().GetMethod("Add");
        int authoredTaskCount = titles.Length * 6;
        for (int index = 0; index < authoredTaskCount - 1; index++)
        {
            int spreadIndex = index / 6;
            int stepIndex = index % 6;
            addAssignment.Invoke(networkAssignments, new[] { Activator.CreateInstance(assignmentType, spreadIndex, stepIndex, (ulong)0) });
        }

        GameObject root = Track(new GameObject("TaskPanelCanvas", typeof(RectTransform), typeof(Canvas)));
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(1000f, 800f);
        root.AddComponent(T("PlayerInputNew"));
        Component requirementsUi = root.AddComponent(T("BridgeRequirementsUI"));
        yield return null;

        Type uiType = T("BridgeRequirementsUI");
        M(uiType, "SetVisible").Invoke(requirementsUi, new object[] { true });
        Component tasksText = (Component)F(uiType, "myTasksText").GetValue(requirementsUi);
        Type textType = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro", true);
        StringAssert.Contains("Part 01", (string)textType.GetProperty("text").GetValue(tasksText));
        StringAssert.Contains("Step 6", (string)textType.GetProperty("text").GetValue(tasksText));
        ScrollRect scrollRect = (ScrollRect)F(uiType, "scrollRect").GetValue(requirementsUi);
        RectTransform content = (RectTransform)F(uiType, "contentRoot").GetValue(requirementsUi);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        Canvas.ForceUpdateCanvases();
        Assert.Greater(content.rect.height, scrollRect.viewport.rect.height);

        ((Component)requirementsUi).GetType().GetMethod("ApplyWheelScroll").Invoke(requirementsUi, new object[] { new Vector2(0f, -3f) });
        float scrolledOffset = content.anchoredPosition.y;
        Assert.Greater(scrolledOffset, 0f);
        M(controllerType, "RequestAssignment").Invoke(book, new object[] { titles.Length - 1, 5, (ulong)0, true });
        Assert.That(content.anchoredPosition.y, Is.EqualTo(scrolledOffset).Within(0.5f), "Assignment refresh should retain the same pixel offset.");
        Assert.IsTrue((bool)controllerType.GetMethod("IsAssigned").Invoke(book, new object[] { titles.Length - 1, 5, (ulong)0 }));

        F(T("GameplayManager"), "currentBridgeBuildingStageIndex").SetValue(manager, 1);
        M(T("GameplayManager"), "NotifyBridgeRequirementsChanged").Invoke(manager, null);
        Assert.That(content.anchoredPosition.y, Is.EqualTo(0f).Within(0.5f), "Changing the global stage returns the task panel to the top.");

        F(T("GameplayManager"), "isFullyAsembled").SetValue(manager, true);
        M(T("GameplayManager"), "NotifyBridgeRequirementsChanged").Invoke(manager, null);
        string completeMessage = (string)textType.GetProperty("text").GetValue(tasksText);
        StringAssert.Contains("No current stage tasks", completeMessage);
        Assert.That(content.anchoredPosition.y, Is.EqualTo(0f).Within(0.5f));
    }

    [Test]
    public void BridgeStateSnapshotCarriesCurrentStageAndCompletionAlongsideComponentState()
    {
        Component manager = CreateConfiguredGameplayManager();
        Type managerType = T("GameplayManager");
        Type componentType = T("BridgeComponentSO");
        Type dataType = T("BridgeComponentData");
        Type stageType = T("BridgeBuildingStage");
        UnityEngine.Object firstPart = Track(ScriptableObject.CreateInstance(componentType));
        UnityEngine.Object secondPart = Track(ScriptableObject.CreateInstance(componentType));
        F(componentType, "componentName").SetValue(firstPart, "Foundation");
        F(componentType, "componentName").SetValue(secondPart, "Deck");

        Array componentData = Array.CreateInstance(dataType, 2);
        for (int index = 0; index < componentData.Length; index++)
        {
            object row = Activator.CreateInstance(dataType);
            F(dataType, "bridgeComponentSO").SetValue(row, index == 0 ? firstPart : secondPart);
            componentData.SetValue(row, index);
        }
        Array stages = Array.CreateInstance(stageType, 2);
        for (int index = 0; index < stages.Length; index++)
        {
            object stage = Activator.CreateInstance(stageType);
            F(stageType, "bridgeComponentDataIndexes").SetValue(stage, new[] { index });
            stages.SetValue(stage, index);
        }
        F(managerType, "bridgeComponentDataArray").SetValue(manager, componentData);
        F(managerType, "bridgeBuildingStages").SetValue(manager, stages);

        IList states = (IList)F(managerType, "bridgeComponentStates").GetValue(manager);
        Type stateType = T("BridgeComponentNetworkState");
        object initialState = Activator.CreateInstance(stateType, 0);
        F(stateType, "isMounted").SetValue(initialState, true);
        F(stateType, "canBeMounted").SetValue(initialState, true);
        states.Add(initialState);
        int requirementsChanged = 0;
        int completedEvents = 0;
        managerType.GetEvent("OnBridgeRequirementsChanged").AddEventHandler(manager, new EventHandler((_, __) => requirementsChanged++));
        managerType.GetEvent("OnBridgeFullyAssembled").AddEventHandler(manager, new EventHandler((_, __) => completedEvents++));
        MethodInfo createWriter = M(managerType, "CreateBridgeStateWriter");
        MethodInfo handleSnapshot = M(managerType, "HandleBridgeStateMessage");
        IList queriedTypes = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(componentType));

        F(managerType, "currentBridgeBuildingStageIndex").SetValue(manager, 1);
        F(managerType, "isFullyAsembled").SetValue(manager, false);
        using (FastBufferWriter writer = (FastBufferWriter)createWriter.Invoke(manager, null))
        using (FastBufferReader reader = new FastBufferReader(writer, Allocator.Temp))
        {
            F(managerType, "currentBridgeBuildingStageIndex").SetValue(manager, 0);
            handleSnapshot.Invoke(manager, new object[] { NetworkManager.ServerClientId, reader });
        }

        Assert.AreEqual(1, managerType.GetProperty("CurrentBridgeStageIndex").GetValue(manager));
        Assert.IsFalse((bool)managerType.GetProperty("IsFullyAssembled").GetValue(manager));
        managerType.GetMethod("GetCurrentStageComponentTypes").Invoke(manager, new object[] { queriedTypes });
        CollectionAssert.AreEqual(new[] { secondPart }, queriedTypes);
        Assert.AreEqual(1, states.Count);
        Assert.IsTrue((bool)F(stateType, "isMounted").GetValue(states[0]), "The existing component snapshot must remain part of the message.");
        Assert.AreEqual(1, requirementsChanged, "A received stage change refreshes dependent task views once.");
        Assert.AreEqual(0, completedEvents);

        F(managerType, "currentBridgeBuildingStageIndex").SetValue(manager, 2);
        F(managerType, "isFullyAsembled").SetValue(manager, true);
        using (FastBufferWriter writer = (FastBufferWriter)createWriter.Invoke(manager, null))
        using (FastBufferReader reader = new FastBufferReader(writer, Allocator.Temp))
        {
            F(managerType, "currentBridgeBuildingStageIndex").SetValue(manager, 1);
            F(managerType, "isFullyAsembled").SetValue(manager, false);
            handleSnapshot.Invoke(manager, new object[] { NetworkManager.ServerClientId, reader });
        }

        Assert.AreEqual(2, managerType.GetProperty("CurrentBridgeStageIndex").GetValue(manager));
        Assert.IsTrue((bool)managerType.GetProperty("IsFullyAssembled").GetValue(manager));
        managerType.GetMethod("GetCurrentStageComponentTypes").Invoke(manager, new object[] { queriedTypes });
        Assert.IsEmpty(queriedTypes);
        object summary = managerType.GetMethod("GetBridgeRequirementsSnapshot").Invoke(manager, null);
        Assert.IsTrue((bool)summary.GetType().GetField("IsBridgeComplete").GetValue(summary));
        Assert.AreEqual(2, requirementsChanged);
        Assert.AreEqual(1, completedEvents, "A completed snapshot raises the completion event once.");
    }

    [UnityTest] public IEnumerator ReceivedIndicesAnimateInOrderWithoutInterruptingActiveTurn()
    {
        Component book=CreateBook("First","Second","Third");yield return null;List<int> shown=new List<int>();T("ConstructionBookController").GetEvent("PageChanged").AddEventHandler(book,new Action<int,bool>((index,snap)=>{if(!snap)shown.Add(index);}));
        M(T("ConstructionBookController"),"AnimateTo").Invoke(book,new object[]{1});M(T("ConstructionBookController"),"AnimateTo").Invoke(book,new object[]{2});yield return new WaitForSecondsRealtime(.8f);
        CollectionAssert.AreEqual(new[]{1,2},shown);Assert.AreEqual(2,T("ConstructionBookController").GetProperty("CurrentIndex").GetValue(book));StringAssert.Contains("Third",(string)T("ConstructionBookController").GetMethod("GetLeftPageText").Invoke(book,null));
    }

    [UnityTest] public IEnumerator ContextPrimaryAndSecondaryAreConsumedWithoutOrdinaryActions()
    {
        Component book=CreateBook("First","Second");GameObject player=Track(new GameObject("Player"));player.SetActive(false);player.AddComponent<NetworkObject>();Component input=player.AddComponent(T("PlayerInputNew"));player.AddComponent(T("PlayerInventory"));player.AddComponent(T("PlayerHealth"));Component interaction=player.AddComponent(T("PlayerInteractionNew"));Component actions=player.AddComponent(T("PlayerActionController"));F(T("PlayerInteractionNew"),"_currentTarget").SetValue(interaction,book);
        int normalPrimary=0,normalSecondary=0,pageChanges=0;T("PlayerActionController").GetEvent("OnActionPerformed").AddEventHandler(actions,new EventHandler((_,__)=>normalPrimary++));T("PlayerActionController").GetEvent("OnActionAltPerformed").AddEventHandler(actions,new EventHandler((_,__)=>normalSecondary++));player.SetActive(true);yield return null;T("ConstructionBookController").GetEvent("PageChanged").AddEventHandler(book,new Action<int,bool>((_,__)=>pageChanges++));F(T("PlayerInteractionNew"),"_currentTarget").SetValue(interaction,book);
        M(T("PlayerActionController"),"HandleActionAlt").Invoke(actions,new object[]{input,EventArgs.Empty});M(T("PlayerInteractionNew"),"HandleActionAlt").Invoke(interaction,new object[]{input,EventArgs.Empty});yield return new WaitForSecondsRealtime(.4f);Assert.AreEqual(1,T("ConstructionBookController").GetProperty("CurrentIndex").GetValue(book));Assert.AreEqual(0,normalSecondary);Assert.AreEqual(1,pageChanges);
        F(T("PlayerInteractionNew"),"_currentTarget").SetValue(interaction,book);M(T("PlayerActionController"),"HandleAction").Invoke(actions,new object[]{input,EventArgs.Empty});yield return new WaitForSecondsRealtime(.4f);Assert.AreEqual(0,T("ConstructionBookController").GetProperty("CurrentIndex").GetValue(book));Assert.AreEqual(0,normalPrimary);Assert.AreEqual(2,pageChanges);
    }

    [UnityTest] public IEnumerator DownedAndDisableCloseSafely()
    {
        Component book=CreateBook("A","B"),ui=CreatePlayerUi(out Component input,out Component health);yield return null;T("PlayerConstructionBookUI").GetMethod("Open").Invoke(ui,new object[]{book});F(T("PlayerHealth"),"isDownedLocal").SetValue(health,true);yield return null;Assert.IsFalse((bool)T("PlayerInputNew").GetProperty("IsGameplayUiOpen").GetValue(input));
        F(T("PlayerHealth"),"isDownedLocal").SetValue(health,false);T("PlayerConstructionBookUI").GetMethod("Open").Invoke(ui,new object[]{book});ui.gameObject.SetActive(false);Assert.IsFalse((bool)T("PlayerInputNew").GetProperty("IsGameplayUiOpen").GetValue(input));Assert.IsNull(F(T("PlayerConstructionBookUI"),"transition").GetValue(ui));
    }

    private Component CreateBook(params string[] titles)
    {
        return CreateBookWithStepCount(1, titles);
    }

    private Component CreateBookWithStepCount(int stepCount, params string[] titles)
    {
        GameObject root=Track(new GameObject("Book"));root.SetActive(false);root.AddComponent<BoxCollider>();root.AddComponent<NetworkObject>();Component controller=root.AddComponent(T("ConstructionBookController"));ScriptableObject catalog=Track(ScriptableObject.CreateInstance(T("ConstructionBookCatalogSO")));Type entryType=T("ConstructionBookCatalogSO+Entry");Array entries=Array.CreateInstance(entryType,titles.Length);IList spreads=(IList)F(T("ConstructionBookController"),"spreads").GetValue(controller);Type spreadType=T("OrderedBridgeComponentRequirement");
        for(int i=0;i<titles.Length;i++){ScriptableObject component=Track(ScriptableObject.CreateInstance(T("BridgeComponentSO")));object entry=Entry(titles[i],stepCount);entryType.GetField("component").SetValue(entry,component);entries.SetValue(entry,i);spreads.Add(Activator.CreateInstance(spreadType,new object[]{component,1}));}
        F(T("ConstructionBookCatalogSO"),"entries").SetValue(catalog,entries);F(T("ConstructionBookController"),"catalog").SetValue(controller,catalog);F(T("ConstructionBookController"),"initialized").SetValue(controller,true);root.SetActive(true);return controller;
    }

    private Component CreateConfiguredGameplayManager()
    {
        GameObject root = Track(new GameObject("GameplayManager"));
        Component manager = root.AddComponent(T("GameplayManager"));
        ((Behaviour)manager).enabled = false;
        T("GameplayManager").GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, manager);
        return manager;
    }

    private static void ConfigureGameplayManagerForBook(Component manager, Component book, int count)
    {
        Type componentType = T("BridgeComponentSO");
        Type dataType = T("BridgeComponentData");
        Type stageType = T("BridgeBuildingStage");
        Type controllerType = T("ConstructionBookController");
        IList spreads = (IList)F(controllerType, "spreads").GetValue(book);
        Array data = Array.CreateInstance(dataType, count);
        int[] componentIndexes = Enumerable.Range(0, data.Length).ToArray();
        Array stages = Array.CreateInstance(stageType, 2);
        for (int stageIndex = 0; stageIndex < stages.Length; stageIndex++)
        {
            object stage = Activator.CreateInstance(stageType);
            F(stageType, "bridgeComponentDataIndexes").SetValue(stage, componentIndexes);
            stages.SetValue(stage, stageIndex);
        }

        for (int index = 0; index < data.Length; index++)
        {
            object spread = spreads[index];
            UnityEngine.Object component = (UnityEngine.Object)spread.GetType().GetField("Component").GetValue(spread);
            F(componentType, "componentName").SetValue(component, $"Part {index + 1:00}");
            object item = Activator.CreateInstance(dataType);
            F(dataType, "bridgeComponentSO").SetValue(item, component);
            data.SetValue(item, index);
        }

        F(T("GameplayManager"), "bridgeComponentDataArray").SetValue(manager, data);
        F(T("GameplayManager"), "bridgeBuildingStages").SetValue(manager, stages);
        F(T("GameplayManager"), "currentBridgeBuildingStageIndex").SetValue(manager, 0);
    }
    private Component CreatePlayerUi(out Component input,out Component health){GameObject player=Track(new GameObject("LocalPlayer"));player.SetActive(false);player.AddComponent<NetworkObject>();input=player.AddComponent(T("PlayerInputNew"));health=player.AddComponent(T("PlayerHealth"));Component ui=player.AddComponent(T("PlayerConstructionBookUI"));player.SetActive(true);return ui;}
    private object Entry(string title,int stepCount){Type entryType=T("ConstructionBookCatalogSO+Entry"),stepType=T("ConstructionBookCatalogSO+Step"),requirementType=T("ConstructionBookCatalogSO+Requirement");object entry=Activator.CreateInstance(entryType);entryType.GetField("title").SetValue(entry,title);Array steps=Array.CreateInstance(stepType,stepCount);for(int i=0;i<stepCount;i++){object step=Activator.CreateInstance(stepType);stepType.GetField("instruction").SetValue(step,"Do the work");object requirement=Activator.CreateInstance(requirementType);requirementType.GetField("displayName").SetValue(requirement,"Hammer");Array requirements=Array.CreateInstance(requirementType,1);requirements.SetValue(requirement,0);stepType.GetField("requirements").SetValue(step,requirements);steps.SetValue(step,i);}entryType.GetField("steps").SetValue(entry,steps);return entry;}
    private TObj Track<TObj>(TObj value) where TObj:UnityEngine.Object{created.Add(value);return value;}
}
