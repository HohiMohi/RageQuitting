using System;
using System.Diagnostics;
using System.Linq;
using UnityEditor;

namespace RageQuitting.Editor.AgentHarness
{
    [Serializable]
    internal sealed class QuickValidatorResult
    {
        public string name;
        public string status;
        public string message;
        public double durationMs;
    }

    [Serializable]
    internal sealed class QuickValidatorsResult
    {
        public int schemaVersion = 1;
        public string status;
        public QuickValidatorResult[] checks;
        public int total;
        public int passed;
        public int failed;
        public int blocked;
        public double durationMs;
    }

    internal static class QuickValidatorsSuite
    {
        // Explicit registration: no menu discovery, Console parsing, or optional checks.
        public static QuickValidatorsResult Validate()
        {
            var timer = Stopwatch.StartNew();
            var checks = new[]
            {
                Run("FoundationConcreteFailureProbe", FoundationConcreteFailureProbe.Validate),
                Run("PlayerConcreteTrapProbe", PlayerConcreteTrapProbe.Validate)
            };
            return new QuickValidatorsResult
            {
                status = checks.Any(check => check.status == "failed") ? "failed" :
                    checks.Any(check => check.status == "blocked") ? "blocked" : "passed",
                checks = checks,
                total = checks.Length,
                passed = checks.Count(check => check.status == "passed"),
                failed = checks.Count(check => check.status == "failed"),
                blocked = checks.Count(check => check.status == "blocked"),
                durationMs = timer.Elapsed.TotalMilliseconds
            };
        }

        private static QuickValidatorResult Run(string name, Func<string> validate)
        {
            var timer = Stopwatch.StartNew();
            var result = new QuickValidatorResult { name = name };
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                    EditorApplication.isUpdating)
                    throw new InvalidOperationException("BLOCKED: validation requires an idle EditMode Editor.");
                result.message = validate();
                result.status = "passed";
            }
            catch (Exception exception)
            {
                // BLOCKED is the explicit exception contract of the probes, not a log heuristic.
                result.status = exception is InvalidOperationException &&
                    exception.Message.StartsWith("BLOCKED:", StringComparison.Ordinal)
                    ? "blocked" : "failed";
                result.message = exception.ToString();
            }
            finally
            {
                result.durationMs = timer.Elapsed.TotalMilliseconds;
            }
            return result;
        }
    }
}
