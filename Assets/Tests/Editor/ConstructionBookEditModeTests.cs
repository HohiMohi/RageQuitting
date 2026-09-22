using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class ConstructionBookEditModeTests
{
    private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
    [TearDown] public void TearDown() { foreach (UnityEngine.Object item in created) UnityEngine.Object.DestroyImmediate(item); created.Clear(); }
    private static Type T(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private ScriptableObject SO(string name) { ScriptableObject value=ScriptableObject.CreateInstance(T(name)); created.Add(value); return value; }
    [Test] public void RecipeResolutionCalculatesPerUnitAndTotal()
    {
        ScriptableObject component=SO("BridgeComponentSO"),output=SO("MountableBridgeComponentSO"),resource=SO("BaseResourceSO"),recipe=SO("ProductionRecipeSO");
        T("MountableBridgeComponentSO").GetField("bridgeComponentSO").SetValue(output,component);T("BaseResourceSO").GetField("resourceName").SetValue(resource,"Log");Set(recipe,"productType",Enum.Parse(T("FactoryProductType"),"MountableBridgeComponent"));Set(recipe,"mountableBridgeComponentOutput",output);
        Type rr=T("RequiredResource");object item=Activator.CreateInstance(rr);rr.GetField("resourceType").SetValue(item,resource);rr.GetField("amount").SetValue(item,4);Array rra=Array.CreateInstance(rr,1);rra.SetValue(item,0);Set(recipe,"requiredResources",rra);
        Array recipes=Array.CreateInstance(T("ProductionRecipeSO"),1);recipes.SetValue(recipe,0);object[] args={component,2,recipes,null,null};Assert.IsTrue((bool)T("ConstructionBookData").GetMethod("TryResolveRecipe").Invoke(null,args));IList lines=(IList)args[3];object line=lines[0];Assert.AreEqual(4,line.GetType().GetField("PerUnit").GetValue(line));Assert.AreEqual(8,line.GetType().GetField("Total").GetValue(line));
    }
    [Test] public void MissingRecipeReportsExactIssue(){ScriptableObject component=SO("BridgeComponentSO");Array recipes=Array.CreateInstance(T("ProductionRecipeSO"),0);object[] args={component,1,recipes,null,null};Assert.IsFalse((bool)T("ConstructionBookData").GetMethod("TryResolveRecipe").Invoke(null,args));StringAssert.Contains("missing direct Carpenter recipe",(string)args[4]);}
    [Test] public void DuplicateRecipeReportsExactIssue(){ScriptableObject component=SO("BridgeComponentSO"),output=SO("MountableBridgeComponentSO");T("MountableBridgeComponentSO").GetField("bridgeComponentSO").SetValue(output,component);Array recipes=Array.CreateInstance(T("ProductionRecipeSO"),2);for(int i=0;i<2;i++){ScriptableObject recipe=SO("ProductionRecipeSO");Set(recipe,"productType",Enum.Parse(T("FactoryProductType"),"MountableBridgeComponent"));Set(recipe,"mountableBridgeComponentOutput",output);recipes.SetValue(recipe,i);}object[] args={component,1,recipes,null,null};Assert.IsFalse((bool)T("ConstructionBookData").GetMethod("TryResolveRecipe").Invoke(null,args));StringAssert.Contains("2 direct Carpenter recipes",(string)args[4]);}
    [Test] public void NonDirectRecipeDoesNotMatch(){ScriptableObject component=SO("BridgeComponentSO"),output=SO("MountableBridgeComponentSO"),recipe=SO("ProductionRecipeSO");T("MountableBridgeComponentSO").GetField("bridgeComponentSO").SetValue(output,component);Set(recipe,"productType",Enum.Parse(T("FactoryProductType"),"BaseResource"));Set(recipe,"mountableBridgeComponentOutput",output);Array recipes=Array.CreateInstance(T("ProductionRecipeSO"),2);recipes.SetValue(null,0);recipes.SetValue(recipe,1);object[] args={component,1,recipes,null,null};Assert.IsFalse((bool)T("ConstructionBookData").GetMethod("TryResolveRecipe").Invoke(null,args));StringAssert.Contains("missing direct Carpenter recipe",(string)args[4]);}
    [Test] public void RosterSortsAndLabelsHostThenPlayers(){IList list=(IList)T("ConstructionBookRoster").GetMethod("Build").Invoke(null,new object[]{new List<ulong>{8,0,3},true,(ulong)0});Assert.AreEqual((ulong)0,list[0].GetType().GetField("ClientId").GetValue(list[0]));Assert.AreEqual("Host",list[0].GetType().GetField("Label").GetValue(list[0]));Assert.AreEqual("Player 2",list[1].GetType().GetField("Label").GetValue(list[1]));Assert.AreEqual("Player 3",list[2].GetType().GetField("Label").GetValue(list[2]));}
    [Test] public void RosterPlacesHostFirstAndUsesConnectionIdsWithoutPlayerObjects(){IList list=(IList)T("ConstructionBookRoster").GetMethod("Build").Invoke(null,new object[]{new List<ulong>{2,9,7},true,(ulong)7});Assert.AreEqual((ulong)7,list[0].GetType().GetField("ClientId").GetValue(list[0]));Assert.AreEqual("Host",list[0].GetType().GetField("Label").GetValue(list[0]));Assert.AreEqual((ulong)2,list[1].GetType().GetField("ClientId").GetValue(list[1]));Assert.AreEqual("Player 2",list[1].GetType().GetField("Label").GetValue(list[1]));Assert.AreEqual((ulong)9,list[2].GetType().GetField("ClientId").GetValue(list[2]));Assert.AreEqual("Player 3",list[2].GetType().GetField("Label").GetValue(list[2]));}
    [Test] public void RosterWithoutHostStartsAtPlayerOne(){IList list=(IList)T("ConstructionBookRoster").GetMethod("Build").Invoke(null,new object[]{new List<ulong>{9,2},false,(ulong)0});Assert.AreEqual((ulong)2,list[0].GetType().GetField("ClientId").GetValue(list[0]));Assert.AreEqual("Player 1",list[0].GetType().GetField("Label").GetValue(list[0]));}
    [Test] public void PrefabGeometryColliderAndPageSurfaceAnchorAreExact()
    {
        GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/New/ConstructionBook.prefab");Assert.IsNotNull(prefab);
        Transform post=prefab.transform.Find("StandPost"),left=prefab.transform.Find("BookLeft"),right=prefab.transform.Find("BookRight"),anchor=prefab.transform.Find("PageSurfaceAnchor");
        Assert.IsNotNull(post);Assert.AreEqual(new Vector3(0f,.415f,0f),post.localPosition);Assert.AreEqual(new Vector3(.16f,.55f,.16f),post.localScale);
        foreach(Transform page in new[]{left,right}){Assert.IsNotNull(page);Assert.AreEqual(.70f,page.localPosition.y,.0001f);Assert.AreEqual(35f,page.localEulerAngles.x,.001f);Assert.AreEqual(new Vector3(.56f,.04f,.72f),page.localScale);}
        BoxCollider collider=prefab.GetComponent<BoxCollider>();Assert.AreEqual(new Vector3(0f,.46f,0f),collider.center);Assert.AreEqual(new Vector3(1.35f,.92f,.75f),collider.size);
        Assert.IsNotNull(anchor);Quaternion pageRotation=Quaternion.Euler(35f,0f,0f);Vector3 physicalNormal=pageRotation*Vector3.up;Vector3 pageTop=pageRotation*Vector3.forward;
        Assert.AreEqual(new Vector3(0f,.70f,0f)+physicalNormal*.044f,anchor.localPosition);Assert.Greater(Vector3.Dot(anchor.forward,prefab.transform.TransformDirection(physicalNormal)),.999f);Assert.Greater(Vector3.Dot(anchor.up,prefab.transform.TransformDirection(pageTop)),.999f);
        Component controller=prefab.GetComponent(T("ConstructionBookController"));Assert.AreSame(anchor,controller.GetType().GetField("pageSurfaceAnchor",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller));
    }
    [Test] public void CatalogValidationReportsAllMissingAuthoringData()
    {
        ScriptableObject catalog=SO("ConstructionBookCatalogSO"),component=SO("BridgeComponentSO");Type et=T("ConstructionBookCatalogSO+Entry"),st=T("ConstructionBookCatalogSO+Step"),rt=T("ConstructionBookCatalogSO+Requirement");Array entries=Array.CreateInstance(et,2);
        object first=Activator.CreateInstance(et);et.GetField("component").SetValue(first,component);object step=Activator.CreateInstance(st);st.GetField("instruction").SetValue(step," ");Array requirements=Array.CreateInstance(rt,2);requirements.SetValue(null,0);requirements.SetValue(Activator.CreateInstance(rt),1);st.GetField("requirements").SetValue(step,requirements);Array steps=Array.CreateInstance(st,1);steps.SetValue(step,0);et.GetField("steps").SetValue(first,steps);entries.SetValue(first,0);
        object duplicate=Activator.CreateInstance(et);et.GetField("component").SetValue(duplicate,component);entries.SetValue(duplicate,1);Set(catalog,"entries",entries);
        IEnumerable values=(IEnumerable)T("ConstructionBookCatalogSO").GetMethod("ValidateCatalog").Invoke(catalog,null);string text="";foreach(object value in values)text+=value+"\n";
        StringAssert.Contains("missing part sprite",text);StringAssert.Contains("missing instruction",text);StringAssert.Contains("null requirement",text);StringAssert.Contains("missing display name",text);StringAssert.Contains("missing icon",text);StringAssert.Contains("duplicate component",text);StringAssert.Contains("missing authored steps",text);
    }
    [Test] public void TurnQueueIsFifoBoundedPerSenderAndGlobally()
    {
        Type qt=T("ConstructionBookTurnQueue");object queue=Activator.CreateInstance(qt,new object[]{3,2});var enqueue=qt.GetMethod("TryEnqueue");
        Assert.IsTrue((bool)enqueue.Invoke(queue,new object[]{(ulong)8,1}));Assert.IsTrue((bool)enqueue.Invoke(queue,new object[]{(ulong)2,-1}));Assert.IsTrue((bool)enqueue.Invoke(queue,new object[]{(ulong)8,1}));Assert.IsFalse((bool)enqueue.Invoke(queue,new object[]{(ulong)8,1}));Assert.IsFalse((bool)enqueue.Invoke(queue,new object[]{(ulong)3,1}));
        object[] take={0d,null};Assert.IsTrue((bool)qt.GetMethod("TryDequeue").Invoke(queue,take));Assert.AreEqual((ulong)8,take[1].GetType().GetField("SenderClientId").GetValue(take[1]));Assert.AreEqual(1,take[1].GetType().GetField("Delta").GetValue(take[1]));
        qt.GetMethod("MarkApplied").Invoke(queue,new object[]{0d,.35d});take=new object[]{.34d,null};Assert.IsFalse((bool)qt.GetMethod("TryDequeue").Invoke(queue,take));take=new object[]{.35d,null};Assert.IsTrue((bool)qt.GetMethod("TryDequeue").Invoke(queue,take));Assert.AreEqual((ulong)2,take[1].GetType().GetField("SenderClientId").GetValue(take[1]));
        qt.GetMethod("RemoveSender").Invoke(queue,new object[]{(ulong)8});Assert.AreEqual(0,qt.GetProperty("Count").GetValue(queue));
    }
    [Test] public void TutorialCarpenterRecipesMatchAllSixExpectedMaterialTotals()
    {
        string root="Assets/ScriptableObjectAssets/New/ProductionRecipes/Carpenter";string[] guids=AssetDatabase.FindAssets("t:ProductionRecipeSO",new[]{root});Array recipes=Array.CreateInstance(T("ProductionRecipeSO"),guids.Length);for(int i=0;i<guids.Length;i++)recipes.SetValue(AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[i]),T("ProductionRecipeSO")),i);
        string componentRoot="Assets/ScriptableObjectAssets/New/BridgeComponentSO/";var cases=new[]{
            new{Path="WoodenFoundation.asset",Count=2,Expected=new[]{"Wooden Log:4:8","Wooden Board:3:6","Foundation Anchor Kit:1:2"}},
            new{Path="WoodenAbutment.asset",Count=2,Expected=new[]{"Wooden Log:4:8","Wooden Board:3:6","Connector Plate Set:1:2"}},
            new{Path="WoodenMainGirder.asset",Count=2,Expected=new[]{"Wooden Log:2:4","Wooden Board:1:2","Connector Plate Set:1:2"}},
            new{Path="WoodenCrossBeam.asset",Count=3,Expected=new[]{"Wooden Log:1:3","Wooden Board:1:3","Bolt & Nut Set:1:3"}},
            new{Path="WoodenDiagonalBracing.asset",Count=2,Expected=new[]{"Wooden Log:1:2","Wooden Board:1:2","Bolt & Nut Set:1:2"}},
            new{Path="WoodenDeckPanel.asset",Count=7,Expected=new[]{"Wooden Board:3:21","Forged Nail Bundle:1:7"}}
        };
        foreach(var item in cases)
        {
            UnityEngine.Object component=AssetDatabase.LoadAssetAtPath(componentRoot+item.Path,T("BridgeComponentSO"));object[] args={component,item.Count,recipes,null,null};Assert.IsTrue((bool)T("ConstructionBookData").GetMethod("TryResolveRecipe").Invoke(null,args),(string)args[4]);IList lines=(IList)args[3];List<string> actual=new List<string>();foreach(object line in lines){object resource=line.GetType().GetField("Resource").GetValue(line);string name=(string)T("BaseResourceSO").GetField("resourceName").GetValue(resource);actual.Add($"{name}:{line.GetType().GetField("PerUnit").GetValue(line)}:{line.GetType().GetField("Total").GetValue(line)}");}CollectionAssert.AreEquivalent(item.Expected,actual,item.Path);
        }
    }
    [Test] public void GameplayManagerGroupsInFirstStageOccurrenceOrder(){GameObject go=new GameObject("GM");created.Add(go);Component gm=go.AddComponent(T("GameplayManager"));ScriptableObject a=SO("BridgeComponentSO"),b=SO("BridgeComponentSO");Type dataType=T("BridgeComponentData");Array data=Array.CreateInstance(dataType,3);foreach((int index,ScriptableObject component) in new[]{(0,a),(1,b),(2,a)}){object item=Activator.CreateInstance(dataType);dataType.GetField("bridgeComponentSO").SetValue(item,component);data.SetValue(item,index);}Type stageType=T("BridgeBuildingStage");Array stages=Array.CreateInstance(stageType,2);foreach((int index,int[] ids) in new[]{(0,new[]{1}),(1,new[]{0,2})}){object stage=Activator.CreateInstance(stageType);stageType.GetField("bridgeComponentDataIndexes").SetValue(stage,ids);stages.SetValue(stage,index);}Set(gm,"bridgeComponentDataArray",data);Set(gm,"bridgeBuildingStages",stages);IList result=(IList)T("GameplayManager").GetMethod("GetOrderedBridgeComponentRequirements").Invoke(gm,null);Assert.AreSame(b,result[0].GetType().GetField("Component").GetValue(result[0]));Assert.AreEqual(2,result[1].GetType().GetField("RequiredCount").GetValue(result[1]));}
    private static void Set(object target,string field,object value)=>target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
}
