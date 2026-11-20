using System;
using UnityEditor;
using UnityEngine;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor;
using Unity.Pipeline.Models;
using Unity.Pipeline.Editor.Testing;
using Newtonsoft.Json.Linq;

namespace RageQuitting.AgentHarness
{
    /// <summary>Explicit, non-disruptive repair for a missing or stale CLI descriptor.</summary>
    public static class UnityPipelineRecovery
    {
        [MenuItem("Tools/Agent Harness/Repair Unity CLI Connection")]
        private static void RepairFromMenu()
        {
            var result = RepairDescriptor();
            if (result.StartsWith("REPAIRED", StringComparison.Ordinal)) Debug.Log(result);
            else Debug.LogWarning(result);
        }

        [CliCommand("rq_repair_unity_connection", "Republish the authenticated descriptor for an already-running Pipeline server", MainThreadRequired = true)]
        public static string RepairFromCli()
        {
            var result = RepairDescriptor();
            if (result.StartsWith("REFUSED:", StringComparison.Ordinal))
                throw new InvalidOperationException(result);
            return result;
        }

        private static string RepairDescriptor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return "REFUSED: Editor is playing, compiling, or updating.";

            try
            {
                var status = PipelineTestRunner.GetTestStatus();
                if (!string.IsNullOrEmpty(status) && string.Equals((string)JObject.Parse(status)["status"], "running", StringComparison.OrdinalIgnoreCase))
                    return "REFUSED: Pipeline tests are running.";
            }
            catch { return "REFUSED: Could not verify Pipeline test state."; }

            var server = PipelineServerStartup.Server;
            if (server == null || !server.IsRunning || server.Port <= 0)
                return "REFUSED: Pipeline server is stopped; this repair only republishes an already-running server descriptor.";

            var descriptor = InstanceDescriptor.CreateCurrent(server.Port);
            InstanceDescriptor.WriteToProjectRoot(descriptor);
            return "REPAIRED: Authenticated descriptor republished for the existing Pipeline server.";
        }
    }
}
