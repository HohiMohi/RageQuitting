using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
        T("ConstructionBookView").GetMethod("Render").Invoke(view,new[]{entry,(object)2,0,6,materials,true,roster,(object)(ulong)7});Canvas.ForceUpdateCanvases();yield return null;Canvas.ForceUpdateCanvases();
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
        GameObject root=Track(new GameObject("Book"));root.SetActive(false);root.AddComponent<BoxCollider>();root.AddComponent<NetworkObject>();Component controller=root.AddComponent(T("ConstructionBookController"));ScriptableObject catalog=Track(ScriptableObject.CreateInstance(T("ConstructionBookCatalogSO")));Type entryType=T("ConstructionBookCatalogSO+Entry");Array entries=Array.CreateInstance(entryType,titles.Length);IList spreads=(IList)F(T("ConstructionBookController"),"spreads").GetValue(controller);Type spreadType=T("OrderedBridgeComponentRequirement");
        for(int i=0;i<titles.Length;i++){ScriptableObject component=Track(ScriptableObject.CreateInstance(T("BridgeComponentSO")));object entry=Entry(titles[i],1);entryType.GetField("component").SetValue(entry,component);entries.SetValue(entry,i);spreads.Add(Activator.CreateInstance(spreadType,new object[]{component,1}));}
        F(T("ConstructionBookCatalogSO"),"entries").SetValue(catalog,entries);F(T("ConstructionBookController"),"catalog").SetValue(controller,catalog);F(T("ConstructionBookController"),"initialized").SetValue(controller,true);root.SetActive(true);return controller;
    }
    private Component CreatePlayerUi(out Component input,out Component health){GameObject player=Track(new GameObject("LocalPlayer"));player.SetActive(false);player.AddComponent<NetworkObject>();input=player.AddComponent(T("PlayerInputNew"));health=player.AddComponent(T("PlayerHealth"));Component ui=player.AddComponent(T("PlayerConstructionBookUI"));player.SetActive(true);return ui;}
    private object Entry(string title,int stepCount){Type entryType=T("ConstructionBookCatalogSO+Entry"),stepType=T("ConstructionBookCatalogSO+Step"),requirementType=T("ConstructionBookCatalogSO+Requirement");object entry=Activator.CreateInstance(entryType);entryType.GetField("title").SetValue(entry,title);Array steps=Array.CreateInstance(stepType,stepCount);for(int i=0;i<stepCount;i++){object step=Activator.CreateInstance(stepType);stepType.GetField("instruction").SetValue(step,"Do the work");object requirement=Activator.CreateInstance(requirementType);requirementType.GetField("displayName").SetValue(requirement,"Hammer");Array requirements=Array.CreateInstance(requirementType,1);requirements.SetValue(requirement,0);stepType.GetField("requirements").SetValue(step,requirements);steps.SetValue(step,i);}entryType.GetField("steps").SetValue(entry,steps);return entry;}
    private TObj Track<TObj>(TObj value) where TObj:UnityEngine.Object{created.Add(value);return value;}
}
