using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class BridgeDeckPanelStage3EditModeTests
{
    [Test]
    public void ResetDeltaVelocityDefaultsAndClampsAreProfileOwned()
    {
        System.Type workflowType = System.Type.GetType("BridgeDeckPanelConstructionWorkflowSO, Assembly-CSharp");
        Assert.That(workflowType, Is.Not.Null);
        ScriptableObject workflow = ScriptableObject.CreateInstance(workflowType);
        try
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            BindingFlags publicFlags = BindingFlags.Instance | BindingFlags.Public;
            Assert.That((float)workflowType.GetProperty("PanelResetUpwardDeltaVelocity", publicFlags).GetValue(workflow), Is.EqualTo(2f));
            Assert.That((float)workflowType.GetProperty("PanelResetAwayDeltaVelocity", publicFlags).GetValue(workflow), Is.EqualTo(0.5f));
            workflowType.GetField("panelResetUpwardDeltaVelocity", flags).SetValue(workflow, -1f);
            workflowType.GetField("panelResetAwayDeltaVelocity", flags).SetValue(workflow, -1f);
            Assert.That((float)workflowType.GetProperty("PanelResetUpwardDeltaVelocity", publicFlags).GetValue(workflow), Is.Zero);
            Assert.That((float)workflowType.GetProperty("PanelResetAwayDeltaVelocity", publicFlags).GetValue(workflow), Is.Zero);
        }
        finally
        {
            Object.DestroyImmediate(workflow);
        }
    }
}
