using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RageQuitting.Tests.PlayMode
{
    public sealed class AgentHarnessExplicitPlayModeTests
    {
        [UnityTest]
        [Explicit("Run only when explicitly requested by the Agent Harness.")]
        public IEnumerator ExplicitPlayMode_AdvancesOneFrame()
        {
            Assert.That(Application.isPlaying, Is.True);
            int initialFrame = Time.frameCount;
            yield return null;
            Assert.That(Time.frameCount, Is.GreaterThan(initialFrame));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
