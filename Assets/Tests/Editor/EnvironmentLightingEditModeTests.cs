using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RageQuitting.Tests.Editor
{
    public sealed class EnvironmentLightingEditModeTests
    {
        private Type profileType;
        private Type controllerType;
        private ScriptableObject profile;

        [SetUp]
        public void SetUp()
        {
            Assembly runtime = AppDomain.CurrentDomain.GetAssemblies()
                .Single(assembly => assembly.GetName().Name == "Assembly-CSharp");
            profileType = runtime.GetType("EnvironmentLightingProfile", true);
            controllerType = runtime.GetType("EnvironmentLightingController", true);
            profile = ScriptableObject.CreateInstance(profileType);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(profile);
        }

        [Test]
        public void Evaluate_WrapsNegativeTime()
        {
            object negative = Evaluate(-0.1f);
            object positive = Evaluate(0.9f);
            AssertStateEqual(negative, positive);
            Assert.That(Read<float>(negative, "normalizedTime"), Is.EqualTo(0.9f).Within(0.00001f));
        }

        [Test]
        public void Evaluate_WrapsOneToZero()
        {
            object one = Evaluate(1f);
            object zero = Evaluate(0f);
            AssertStateEqual(one, zero);
            Assert.That(Read<float>(one, "normalizedTime"), Is.Zero.Within(0.00001f));
        }

        [Test]
        public void Evaluate_RepeatedCallsAreDeterministic()
        {
            AssertStateEqual(Evaluate(0.6875f), Evaluate(0.6875f));
        }

        [Test]
        public void Evaluate_NonFiniteTimeFallsBackToAfternoon()
        {
            AssertStateEqual(Evaluate(0.6875f), Evaluate(float.NaN));
            AssertStateEqual(Evaluate(0.6875f), Evaluate(float.PositiveInfinity));
        }

        [Test]
        public void Evaluate_DefaultAfternoonMatchesApprovedValues()
        {
            object state = Evaluate(0.6875f);
            Assert.That(Read<Vector3>(state, "sunEulerAngles"), Is.EqualTo(new Vector3(28f, 215f, 0f)));
            Assert.That(Read<Color>(state, "sunColor"), Is.EqualTo(new Color(1f, 0.78f, 0.55f, 1f)));
            Assert.That(Read<float>(state, "sunIntensity"), Is.EqualTo(1.15f).Within(0.00001f));
            Assert.That(Read<float>(state, "sunShadowStrength"), Is.EqualTo(0.82f).Within(0.00001f));
            Assert.That(Read<Color>(state, "skyTint"), Is.EqualTo(new Color(0.55f, 0.72f, 1f, 1f)));
            Assert.That(Read<Color>(state, "groundColor"), Is.EqualTo(new Color(0.42f, 0.30f, 0.24f, 1f)));
            Assert.That(Read<float>(state, "skyExposure"), Is.EqualTo(1.1f).Within(0.00001f));
            Assert.That(Read<float>(state, "atmosphereThickness"), Is.EqualTo(0.85f).Within(0.00001f));
            Assert.That(Read<float>(state, "ambientIntensity"), Is.EqualTo(1.05f).Within(0.00001f));
            Assert.That(Read<bool>(state, "fogEnabled"), Is.True);
            Assert.That(Read<Color>(state, "fogColor"), Is.EqualTo(new Color(0.72f, 0.78f, 0.72f, 1f)));
            Assert.That(Read<float>(state, "fogDensity"), Is.EqualTo(0.0035f).Within(0.00001f));
            Assert.That(Read<float>(state, "postExposure"), Is.EqualTo(0.2f).Within(0.00001f));
            Assert.That(Read<float>(state, "postContrast"), Is.EqualTo(8f).Within(0.00001f));
            Assert.That(Read<float>(state, "postSaturation"), Is.EqualTo(12f).Within(0.00001f));
            Assert.That(Read<Color>(state, "postColorFilter"), Is.EqualTo(new Color(1f, 0.94f, 0.86f, 1f)));
            Assert.That(Read<float>(state, "whiteBalanceTemperature"), Is.EqualTo(12f).Within(0.00001f));
            Assert.That(Read<float>(state, "bloomIntensity"), Is.EqualTo(0.22f).Within(0.00001f));
            Assert.That(Read<float>(state, "localLightMultiplier"), Is.EqualTo(0.9f).Within(0.00001f));
        }

        [Test]
        public void Evaluate_DoesNotMutateSourceProfile()
        {
            string before = JsonUtility.ToJson(profile);
            Evaluate(0.6875f);
            Evaluate(-0.1f);
            Assert.That(JsonUtility.ToJson(profile), Is.EqualTo(before));
        }

        [Test]
        public void FairyAfternoonVolumeProfile_HasPersistentUniqueApprovedComponents()
        {
            const string assetPath = "Assets/Settings/Lighting/FairyAfternoonVolumeProfile.asset";
            ScriptableObject asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            Assert.That(asset, Is.Not.Null);
            IList components = GetVolumeComponents(asset);
            Assert.That(components, Has.Count.EqualTo(9));
            Assert.That(components.Cast<UnityEngine.Object>(), Has.None.Null);
            Assert.That(components.Cast<UnityEngine.Object>().All(EditorUtility.IsPersistent), Is.True);
            Assert.That(components.Cast<UnityEngine.Object>().All(component => AssetDatabase.GetAssetPath(component) == assetPath), Is.True);

            Type[] expectedTypes =
            {
                FindLoadedType("UnityEngine.Rendering.Universal.Tonemapping"),
                FindLoadedType("UnityEngine.Rendering.Universal.WhiteBalance"),
                FindLoadedType("UnityEngine.Rendering.Universal.ColorAdjustments"),
                FindLoadedType("UnityEngine.Rendering.Universal.Bloom"),
                FindLoadedType("UnityEngine.Rendering.Universal.Vignette"),
                FindLoadedType("UnityEngine.Rendering.Universal.MotionBlur"),
                FindLoadedType("UnityEngine.Rendering.Universal.DepthOfField"),
                FindLoadedType("UnityEngine.Rendering.Universal.ChromaticAberration"),
                FindLoadedType("UnityEngine.Rendering.Universal.LensDistortion")
            };
            CollectionAssert.AreEquivalent(expectedTypes, components.Cast<object>().Select(component => component.GetType()));
            foreach (Type expectedType in expectedTypes)
            {
                Assert.That(components.Cast<object>().Count(component => component.GetType() == expectedType), Is.EqualTo(1), expectedType.Name);
            }

            object tonemapping = FindVolumeComponent(asset, expectedTypes[0]);
            Assert.That(ReadActive(tonemapping), Is.True);
            AssertParameterOverride(tonemapping, "mode", "Neutral");

            object whiteBalance = FindVolumeComponent(asset, expectedTypes[1]);
            Assert.That(ReadActive(whiteBalance), Is.True);
            AssertParameterOverride(whiteBalance, "temperature", 12f);

            object colorAdjustments = FindVolumeComponent(asset, expectedTypes[2]);
            Assert.That(ReadActive(colorAdjustments), Is.True);
            AssertParameterOverride(colorAdjustments, "postExposure", 0.2f);
            AssertParameterOverride(colorAdjustments, "contrast", 8f);
            AssertParameterOverride(colorAdjustments, "saturation", 12f);
            Assert.That(ReadParameterValue<Color>(colorAdjustments, "colorFilter"), Is.EqualTo(new Color(1f, 0.94f, 0.86f, 1f)));

            object bloom = FindVolumeComponent(asset, expectedTypes[3]);
            Assert.That(ReadActive(bloom), Is.True);
            AssertParameterOverride(bloom, "intensity", 0.22f);
            AssertParameterOverride(bloom, "threshold", 1.05f);
            AssertParameterOverride(bloom, "scatter", 0.55f);

            AssertInactiveZero(asset, expectedTypes[4], "intensity");
            AssertInactiveZero(asset, expectedTypes[5], "intensity");
            Assert.That(ReadActive(FindVolumeComponent(asset, expectedTypes[6])), Is.False);
            AssertInactiveZero(asset, expectedTypes[7], "intensity");
            AssertInactiveZero(asset, expectedTypes[8], "intensity");
        }

        [Test]
        public void Controller_ManualTimeDefaultsToAfternoonAndSetWraps()
        {
            var gameObject = new GameObject("EnvironmentLightingControllerTest");
            gameObject.SetActive(false);
            try
            {
                Component controller = gameObject.AddComponent(controllerType);
                PropertyInfo normalizedTime = controllerType.GetProperty("NormalizedTime");
                MethodInfo setNormalizedTime = controllerType.GetMethod("SetNormalizedTime");
                Assert.That((float)normalizedTime.GetValue(controller), Is.EqualTo(0.6875f).Within(0.00001f));
                setNormalizedTime.Invoke(controller, new object[] { -0.1f });
                Assert.That((float)normalizedTime.GetValue(controller), Is.EqualTo(0.9f).Within(0.00001f));
                setNormalizedTime.Invoke(controller, new object[] { 1f });
                Assert.That((float)normalizedTime.GetValue(controller), Is.Zero.Within(0.00001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Controller_ApplyAndCleanup_RestoresRenderSettingsAndDoesNotMutateTemplates()
        {
            Material savedSkybox = RenderSettings.skybox;
            Light savedSun = RenderSettings.sun;
            AmbientMode savedAmbientMode = RenderSettings.ambientMode;
            float savedAmbientIntensity = RenderSettings.ambientIntensity;
            bool savedFog = RenderSettings.fog;
            Color savedFogColor = RenderSettings.fogColor;
            FogMode savedFogMode = RenderSettings.fogMode;
            float savedFogDensity = RenderSettings.fogDensity;
            float savedFogStart = RenderSettings.fogStartDistance;
            float savedFogEnd = RenderSettings.fogEndDistance;

            GameObject controllerObject = null;
            GameObject previousSunObject = null;
            GameObject controlledSunObject = null;
            Light previousSun = null;
            Light controlledSun = null;
            Component volume = null;
            ScriptableObject sourceVolume = null;
            UnityEngine.Object sourceColorAdjustments = null;
            UnityEngine.Object sourceBloom = null;
            UnityEngine.Object runtimeColorAdjustments = null;
            UnityEngine.Object runtimeBloom = null;
            FieldInfo sharedProfile = null;
            Material sourceSkybox = null;
            Component controller = null;

            try
            {
                controllerObject = new GameObject("EnvironmentLightingControllerLifecycleTest");
                previousSunObject = new GameObject("PreviousRenderSettingsSun");
                controlledSunObject = new GameObject("ControlledRenderSettingsSun");
                controllerObject.SetActive(false);
                previousSun = previousSunObject.AddComponent<Light>();
                controlledSun = controlledSunObject.AddComponent<Light>();
                previousSun.type = LightType.Directional;
                controlledSun.type = LightType.Directional;
                Type volumeType = FindLoadedType("UnityEngine.Rendering.Volume");
                volume = controllerObject.AddComponent(volumeType);
                Type volumeProfileType = FindLoadedType("UnityEngine.Rendering.VolumeProfile");
                Type colorAdjustmentsType = FindLoadedType("UnityEngine.Rendering.Universal.ColorAdjustments");
                Type bloomType = FindLoadedType("UnityEngine.Rendering.Universal.Bloom");
                sourceVolume = ScriptableObject.CreateInstance(volumeProfileType);
                MethodInfo addComponent = volumeProfileType.GetMethod("Add", new[] { typeof(Type), typeof(bool) });
                sourceColorAdjustments = (UnityEngine.Object)addComponent.Invoke(sourceVolume, new object[] { colorAdjustmentsType, true });
                sourceBloom = (UnityEngine.Object)addComponent.Invoke(sourceVolume, new object[] { bloomType, true });
                SetParameterValue(sourceColorAdjustments, "postExposure", 7f);
                SetParameterValue(sourceBloom, "intensity", 0.77f);
                sharedProfile = volumeType.GetField("sharedProfile");
                sharedProfile.SetValue(volume, sourceVolume);

                Shader skyShader = Shader.Find("Skybox/Procedural");
                Assert.That(skyShader, Is.Not.Null, "Built-in procedural sky shader is required for this test.");
                sourceSkybox = new Material(skyShader);
                RenderSettings.sun = previousSun;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientIntensity = 0.37f;
                RenderSettings.fog = false;
                RenderSettings.fogColor = Color.magenta;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogDensity = 0.019f;
                RenderSettings.fogStartDistance = 12f;
                RenderSettings.fogEndDistance = 34f;
                Material expectedPreviousSkybox = RenderSettings.skybox;

                controller = controllerObject.AddComponent(controllerType);
                SetPrivateField(controller, "profile", profile);
                SetPrivateField(controller, "mainDirectionalLight", controlledSun);
                SetPrivateField(controller, "globalVolume", volume);
                SetPrivateField(controller, "volumeProfileTemplate", sourceVolume);
                SetPrivateField(controller, "proceduralSkyboxTemplate", sourceSkybox);

                string volumeBefore = EditorJsonUtility.ToJson(sourceVolume);
                string skyboxBefore = EditorJsonUtility.ToJson(sourceSkybox);
                controllerObject.SetActive(true);
                InvokePrivateMethod(controller, "OnEnable");

                Assert.That(RenderSettings.sun, Is.SameAs(controlledSun));
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Skybox));
                Assert.That(RenderSettings.skybox, Is.Not.SameAs(sourceSkybox));
                Assert.That(sharedProfile.GetValue(volume), Is.SameAs(sourceVolume));
                var runtimeProfile = (ScriptableObject)ReadPrivateField(controller, "runtimeVolumeProfile");
                Assert.That(runtimeProfile, Is.Not.Null.And.Not.SameAs(sourceVolume));
                runtimeColorAdjustments = (UnityEngine.Object)FindVolumeComponent(runtimeProfile, colorAdjustmentsType);
                runtimeBloom = (UnityEngine.Object)FindVolumeComponent(runtimeProfile, bloomType);
                Assert.That(runtimeColorAdjustments, Is.Not.SameAs(sourceColorAdjustments));
                Assert.That(runtimeBloom, Is.Not.SameAs(sourceBloom));
                Assert.That(ReadParameterValue<float>(sourceColorAdjustments, "postExposure"), Is.EqualTo(7f));
                Assert.That(ReadParameterValue<float>(sourceBloom, "intensity"), Is.EqualTo(0.77f));
                Assert.That(EditorJsonUtility.ToJson(sourceVolume), Is.EqualTo(volumeBefore));
                Assert.That(EditorJsonUtility.ToJson(sourceSkybox), Is.EqualTo(skyboxBefore));

                InvokePrivateMethod(controller, "OnDisable");
                controllerObject?.SetActive(false);

                Assert.That(RenderSettings.sun, Is.SameAs(previousSun));
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Flat));
                Assert.That(RenderSettings.ambientIntensity, Is.EqualTo(0.37f));
                Assert.That(RenderSettings.skybox, Is.SameAs(expectedPreviousSkybox));
                Assert.That(RenderSettings.fog, Is.False);
                Assert.That(RenderSettings.fogColor, Is.EqualTo(Color.magenta));
                Assert.That(RenderSettings.fogMode, Is.EqualTo(FogMode.Linear));
                Assert.That(RenderSettings.fogDensity, Is.EqualTo(0.019f));
                Assert.That(RenderSettings.fogStartDistance, Is.EqualTo(12f));
                Assert.That(RenderSettings.fogEndDistance, Is.EqualTo(34f));
                Assert.That(sharedProfile.GetValue(volume), Is.SameAs(sourceVolume));
                Assert.That(runtimeColorAdjustments == null, Is.True);
                Assert.That(runtimeBloom == null, Is.True);
                Assert.That(ReadPrivateField(controller, "runtimeSkybox"), Is.Null);
                Assert.That(ReadPrivateField(controller, "runtimeVolumeProfile"), Is.Null);
            }
            finally
            {
                if (controller != null)
                {
                    InvokePrivateMethod(controller, "OnDisable");
                }
                controllerObject.SetActive(false);
                RenderSettings.skybox = savedSkybox;
                RenderSettings.sun = savedSun;
                RenderSettings.ambientMode = savedAmbientMode;
                RenderSettings.ambientIntensity = savedAmbientIntensity;
                RenderSettings.fog = savedFog;
                RenderSettings.fogColor = savedFogColor;
                RenderSettings.fogMode = savedFogMode;
                RenderSettings.fogDensity = savedFogDensity;
                RenderSettings.fogStartDistance = savedFogStart;
                RenderSettings.fogEndDistance = savedFogEnd;
                DynamicGI.UpdateEnvironment();
                if (controllerObject != null) UnityEngine.Object.DestroyImmediate(controllerObject);
                if (previousSunObject != null) UnityEngine.Object.DestroyImmediate(previousSunObject);
                if (controlledSunObject != null) UnityEngine.Object.DestroyImmediate(controlledSunObject);
                if (sourceSkybox != null) UnityEngine.Object.DestroyImmediate(sourceSkybox);
                if (sourceColorAdjustments != null) UnityEngine.Object.DestroyImmediate(sourceColorAdjustments);
                if (sourceBloom != null) UnityEngine.Object.DestroyImmediate(sourceBloom);
                if (sourceVolume != null) UnityEngine.Object.DestroyImmediate(sourceVolume);
            }
        }

        [Test]
        public void Controller_MissingOptionalReferences_AppliesTimeAndRestoresRenderSettings()
        {
            Material savedSkybox = RenderSettings.skybox;
            Light savedSun = RenderSettings.sun;
            AmbientMode savedAmbientMode = RenderSettings.ambientMode;
            float savedAmbientIntensity = RenderSettings.ambientIntensity;
            bool savedFog = RenderSettings.fog;
            Color savedFogColor = RenderSettings.fogColor;
            FogMode savedFogMode = RenderSettings.fogMode;
            float savedFogDensity = RenderSettings.fogDensity;
            GameObject controllerObject = null;
            GameObject sunObject = null;
            GameObject previousSunObject = null;
            Component controller = null;

            try
            {
                controllerObject = new GameObject("EnvironmentLightingControllerOptionalReferencesTest");
                sunObject = new GameObject("OptionalReferencesSun");
                previousSunObject = new GameObject("OptionalReferencesPreviousSun");
                controllerObject.SetActive(false);
                Light sun = sunObject.AddComponent<Light>();
                sun.type = LightType.Directional;
                Light previousSun = previousSunObject.AddComponent<Light>();
                previousSun.type = LightType.Directional;

                RenderSettings.sun = previousSun;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientIntensity = 0.42f;
                RenderSettings.fog = false;
                RenderSettings.fogColor = Color.cyan;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogDensity = 0.017f;

                controller = controllerObject.AddComponent(controllerType);
                SetPrivateField(controller, "profile", profile);
                SetPrivateField(controller, "mainDirectionalLight", sun);

                Assert.DoesNotThrow(() => InvokePrivateMethod(controller, "OnEnable"));
                Assert.DoesNotThrow(() => controllerType.GetMethod("SetNormalizedTime").Invoke(controller, new object[] { -0.1f }));
                Assert.That((float)controllerType.GetProperty("NormalizedTime").GetValue(controller), Is.EqualTo(0.9f).Within(0.00001f));
                Assert.DoesNotThrow(() => InvokePrivateMethod(controller, "OnDisable"));

                Assert.That(RenderSettings.sun, Is.SameAs(previousSun));
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Flat));
                Assert.That(RenderSettings.ambientIntensity, Is.EqualTo(0.42f));
                Assert.That(RenderSettings.fog, Is.False);
                Assert.That(RenderSettings.fogColor, Is.EqualTo(Color.cyan));
                Assert.That(RenderSettings.fogMode, Is.EqualTo(FogMode.Linear));
                Assert.That(RenderSettings.fogDensity, Is.EqualTo(0.017f));
            }
            finally
            {
                if (controller != null) InvokePrivateMethod(controller, "OnDisable");
                RenderSettings.skybox = savedSkybox;
                RenderSettings.sun = savedSun;
                RenderSettings.ambientMode = savedAmbientMode;
                RenderSettings.ambientIntensity = savedAmbientIntensity;
                RenderSettings.fog = savedFog;
                RenderSettings.fogColor = savedFogColor;
                RenderSettings.fogMode = savedFogMode;
                RenderSettings.fogDensity = savedFogDensity;
                DynamicGI.UpdateEnvironment();
                if (controllerObject != null) UnityEngine.Object.DestroyImmediate(controllerObject);
                if (sunObject != null) UnityEngine.Object.DestroyImmediate(sunObject);
                if (previousSunObject != null) UnityEngine.Object.DestroyImmediate(previousSunObject);
            }
        }

        private object Evaluate(float time)
        {
            return profileType.GetMethod("Evaluate").Invoke(profile, new object[] { time });
        }

        private static T Read<T>(object state, string fieldName)
        {
            return (T)state.GetType().GetField(fieldName).GetValue(state);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static object ReadPrivateField(object target, string fieldName)
        {
            return target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private static void InvokePrivateMethod(object target, string methodName)
        {
            target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
        }

        private static Type FindLoadedType(string fullName)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(candidate => candidate != null);
            Assert.That(type, Is.Not.Null, $"Required loaded type was not found: {fullName}");
            return type;
        }

        private static IList GetVolumeComponents(ScriptableObject volumeProfile)
        {
            return (IList)volumeProfile.GetType().GetField("components").GetValue(volumeProfile);
        }

        private static object FindVolumeComponent(ScriptableObject volumeProfile, Type componentType)
        {
            object component = GetVolumeComponents(volumeProfile).Cast<object>().SingleOrDefault(candidate => candidate.GetType() == componentType);
            Assert.That(component, Is.Not.Null, componentType.Name);
            return component;
        }

        private static bool ReadActive(object component)
        {
            return (bool)component.GetType().GetField("active").GetValue(component);
        }

        private static T ReadParameterValue<T>(object component, string fieldName)
        {
            object parameter = component.GetType().GetField(fieldName).GetValue(component);
            return (T)parameter.GetType().GetProperty("value").GetValue(parameter);
        }

        private static void SetParameterValue(object component, string fieldName, object value)
        {
            object parameter = component.GetType().GetField(fieldName).GetValue(component);
            parameter.GetType().GetProperty("value").SetValue(parameter, value);
            parameter.GetType().GetProperty("overrideState").SetValue(parameter, true);
        }

        private static void AssertParameterOverride(object component, string fieldName, object expected)
        {
            object parameter = component.GetType().GetField(fieldName).GetValue(component);
            Assert.That((bool)parameter.GetType().GetProperty("overrideState").GetValue(parameter), Is.True, fieldName);
            object value = parameter.GetType().GetProperty("value").GetValue(parameter);
            if (expected is float expectedFloat)
            {
                Assert.That((float)value, Is.EqualTo(expectedFloat).Within(0.00001f), fieldName);
            }
            else
            {
                Assert.That(value.ToString(), Is.EqualTo(expected.ToString()), fieldName);
            }
        }

        private static void AssertInactiveZero(ScriptableObject asset, Type componentType, string fieldName)
        {
            object component = FindVolumeComponent(asset, componentType);
            Assert.That(ReadActive(component), Is.False);
            Assert.That(ReadParameterValue<float>(component, fieldName), Is.Zero.Within(0.00001f));
        }

        private static void AssertStateEqual(object expected, object actual)
        {
            foreach (FieldInfo field in expected.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                Assert.That(field.GetValue(actual), Is.EqualTo(field.GetValue(expected)), field.Name);
            }
        }
    }
}
