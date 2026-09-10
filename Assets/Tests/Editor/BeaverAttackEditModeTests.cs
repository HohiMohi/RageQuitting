using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace RageQuitting.Tests.Editor
{
    public sealed class BeaverAttackEditModeTests
    {
        [TestCase(1f, 0.35f)]
        [TestCase(1.4f, 0.25f)]
        [TestCase(1.5f, 0.23333333f)]
        [TestCase(1.6f, 0.21875f)]
        [TestCase(-10f, 3.5f)]
        [TestCase(10f, 0.0875f)]
        public void ImpactDelay_UsesProductionFormulaAndPlaybackClamp(float speed, float expected)
        {
            Assert.That(CalculateImpactDelay(0.35f, speed), Is.EqualTo(expected).Within(0.00001f));
        }

        [Test]
        public void ScoutRetaliation_FirstImpactIsApproximatelyPointSevenEightThreeSeconds()
        {
            float firstImpact = 0.4f + 0.15f + CalculateImpactDelay(0.35f, 1.5f);
            Assert.That(firstImpact, Is.EqualTo(0.7833333f).Within(0.0001f));
        }

        [Test]
        public void OneShotBlendEnvelope_StartsAtZero_Rises_HoldsAndFadesToZero()
        {
            const float duration = 1f;
            const float blendIn = 0.08f;
            const float blendOut = 0.1f;
            float start = CalculateOneShotWeight(0f, duration, blendIn, blendOut, 0f);
            float early = CalculateOneShotWeight(0.02f, duration, blendIn, blendOut, 0f);
            float later = CalculateOneShotWeight(0.06f, duration, blendIn, blendOut, 0f);

            Assert.That(start, Is.EqualTo(0f));
            Assert.That(early, Is.GreaterThanOrEqualTo(start));
            Assert.That(later, Is.GreaterThanOrEqualTo(early));
            Assert.That(CalculateOneShotWeight(0.5f, duration, blendIn, blendOut, 0f), Is.EqualTo(1f));
            Assert.That(CalculateOneShotWeight(duration, duration, blendIn, blendOut, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void OneShotBlendEnvelope_RetriggerPreservesCurrentWeightAndDoesNotDrop()
        {
            const float currentWeight = 0.65f;
            float atRetrigger = CalculateOneShotWeight(0f, 1f, 0.08f, 0.1f, currentWeight);
            float nextSample = CalculateOneShotWeight(1f / 60f, 1f, 0.08f, 0.1f, currentWeight);

            Assert.That(atRetrigger, Is.EqualTo(currentWeight).Within(0.00001f));
            Assert.That(nextSample, Is.GreaterThanOrEqualTo(atRetrigger));
        }

        [Test]
        public void OneShotBlendEnvelope_ScalesBlendWindowsForShortClips()
        {
            Assert.That(CalculateOneShotWeight(0f, 0.05f, 0.08f, 0.1f, 0f), Is.EqualTo(0f));
            Assert.That(CalculateOneShotWeight(0.025f, 0.05f, 0.08f, 0.1f, 0f), Is.InRange(0f, 1f));
            Assert.That(CalculateOneShotWeight(0.05f, 0.05f, 0.08f, 0.1f, 0f), Is.EqualTo(0f));
            Assert.That(CalculateOneShotWeight(0f, 0.05f, -1f, -1f, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void DefenderPatterns_HaveEqualNominalDamage()
        {
            Array patterns = CreateDefenderPatterns();
            MethodInfo nominalDamage = patterns.GetType().GetElementType().GetMethod("GetNominalDamage");
            foreach (object pattern in patterns)
            {
                float damage = (float)nominalDamage.Invoke(pattern, new object[] { 15f });
                Assert.That(damage, Is.EqualTo(22.5f).Within(0.0001f));
            }
        }

        [Test]
        public void PatternSelection_AvoidsImmediateRepeatWhenAlternativeHasWeight()
        {
            Array patterns = CreateDefenderPatterns();
            for (int previous = 0; previous < patterns.Length; previous++)
            {
                Assert.That(SelectPattern(patterns, previous, 0f), Is.Not.EqualTo(previous));
                Assert.That(SelectPattern(patterns, previous, 0.999f), Is.Not.EqualTo(previous));
            }
        }

        [Test]
        public void PatternSelection_AllZeroWeightsFallsBackToHeavy()
        {
            Array patterns = CreateDefenderPatterns();
            FieldInfo weight = patterns.GetType().GetElementType().GetField("weight");
            for (int i = 0; i < patterns.Length; i++)
            {
                object pattern = patterns.GetValue(i);
                weight.SetValue(pattern, 0f);
                patterns.SetValue(pattern, i);
            }

            Assert.That(SelectPattern(patterns, 0, 0.5f), Is.EqualTo(0));
        }

        [Test]
        public void PatternHistory_ChangesOnlyForSuccessfullyStartedCloseRangePattern()
        {
            Assert.That(ResolveRecordedPattern(2, 1, false, false), Is.EqualTo(2), "failed start");
            Assert.That(ResolveRecordedPattern(2, 1, true, true), Is.EqualTo(2), "lunge start");
            Assert.That(ResolveRecordedPattern(2, 1, true, false), Is.EqualTo(1), "close-range start");
        }

        [Test]
        public void PrefabAndAssetWiring_IsComplete()
        {
            Type probe = GetTypeFromAssembly("Assembly-CSharp-Editor", "BeaverAttackFeatureProbe");
            string result = (string)probe.GetMethod("Validate", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
            Assert.That(result, Does.Contain("passed"));
        }

        [Test]
        public void LungeSerializedDefaultsAndWiring_AreComplete()
        {
            Type probe = GetTypeFromAssembly("Assembly-CSharp-Editor", "BeaverAttackFeatureProbe");
            string result = (string)probe.GetMethod(
                    "ValidateLungeConfiguration",
                    BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, null);
            Assert.That(result, Does.Contain("passed"));
        }

        private static float CalculateImpactDelay(float canonicalDelay, float speed)
        {
            Type attackController = GetTypeFromAssembly("Assembly-CSharp", "NPCAttackController");
            return (float)attackController.GetMethod(
                    "CalculateEffectiveImpactDelay",
                    BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { canonicalDelay, speed });
        }

        private static float CalculateOneShotWeight(
            float elapsed,
            float duration,
            float blendIn,
            float blendOut,
            float startWeight)
        {
            Type animationController = GetTypeFromAssembly("Assembly-CSharp", "NPCAnimationController");
            return (float)animationController.GetMethod(
                    "CalculateOneShotBlendWeight",
                    BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { elapsed, duration, blendIn, blendOut, startWeight });
        }

        private static int ResolveRecordedPattern(
            int previous,
            int selected,
            bool sequenceStarted,
            bool startedAsLunge)
        {
            Type controller = GetTypeFromAssembly("Assembly-CSharp", "BeaverAttackSequenceController");
            return (int)controller.GetMethod(
                    "ResolveRecordedPatternIndex",
                    BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { previous, selected, sequenceStarted, startedAsLunge });
        }

        private static int SelectPattern(Array patterns, int previous, float roll)
        {
            Type controller = GetTypeFromAssembly("Assembly-CSharp", "BeaverAttackSequenceController");
            return (int)controller.GetMethod("SelectPatternIndex", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { patterns, previous, roll });
        }

        private static Array CreateDefenderPatterns()
        {
            Type strikeType = GetTypeFromAssembly("Assembly-CSharp", "BeaverStrikeConfig");
            Type patternType = GetTypeFromAssembly("Assembly-CSharp", "BeaverAttackPatternConfig");
            ConstructorInfo strikeConstructor = strikeType.GetConstructor(new[] { typeof(float), typeof(float), typeof(float) });
            ConstructorInfo patternConstructor = patternType.GetConstructor(
                new[] { typeof(string), typeof(float), typeof(float), strikeType.MakeArrayType(), typeof(float) });

            object Pattern(string name, float prepare, float recovery, params float[] multipliers)
            {
                Array strikes = Array.CreateInstance(strikeType, multipliers.Length);
                for (int i = 0; i < multipliers.Length; i++)
                {
                    strikes.SetValue(strikeConstructor.Invoke(new object[] { multipliers[i], 1f, 0f }), i);
                }

                return patternConstructor.Invoke(new object[] { name, 1f, prepare, strikes, recovery });
            }

            Array patterns = Array.CreateInstance(patternType, 3);
            patterns.SetValue(Pattern("Heavy", 0.15f, 0.65f, 1.5f), 0);
            patterns.SetValue(Pattern("Double", 0.15f, 0.55f, 0.75f, 0.75f), 1);
            patterns.SetValue(Pattern("Triple", 0.2f, 0.6f, 0.5f, 0.5f, 0.5f), 2);
            return patterns;
        }

        private static Type GetTypeFromAssembly(string assemblyName, string typeName)
        {
            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
                .Single(candidate => candidate.GetName().Name == assemblyName);
            return assembly.GetType(typeName, true);
        }
    }
}
