using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RageQuitting.Tests.PlayMode
{
    public sealed class AgentHarnessBasicPlayModeTests
    {
        [UnityTest]
        public IEnumerator PlayMode_AdvancesFrameAndFixedUpdate()
        {
            GameObject temporary = null;
            try
            {
                Assert.That(Application.isPlaying, Is.True);
                temporary = new GameObject("AgentHarnessPlayModeProbe");
                int initialFrame = Time.frameCount;
                yield return null;
                Assert.That(Time.frameCount, Is.GreaterThan(initialFrame));

                double initialFixedTime = Time.fixedTimeAsDouble;
                yield return new WaitForFixedUpdate();
                Assert.That(Time.fixedTimeAsDouble, Is.GreaterThan(initialFixedTime));
                Assert.That(Application.isPlaying, Is.True);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                // Immediate cleanup also works when an assertion aborts the coroutine.
                if (temporary != null)
                    Object.DestroyImmediate(temporary);
            }
        }
    }
}
