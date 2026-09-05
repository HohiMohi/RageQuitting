using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RageQuitting.Editor.AgentHarness
{
    internal sealed class ValidationHarnessWindow : EditorWindow
    {
        private const string LastReportPreferenceKey = "RageQuitting.Validation.LastReport";
        private const double ContextRefreshIntervalSeconds = 1.0;
        private const double OutputRefreshIntervalSeconds = 0.25;

        private ValidationEditorContext context;
        private Process ownedProcess;
        private string runningLabel = string.Empty;
        private string processStdOutPath = string.Empty;
        private string processStdErrPath = string.Empty;
        private string liveOutput = "Ready.";
        private Vector2 outputScroll;
        private double nextContextRefresh;
        private double nextOutputRefresh;

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string HarnessRoot => Path.Combine(ProjectRoot, "Tools", "AgentHarness");
        private static string PreflightScriptPath => Path.Combine(HarnessRoot, "Invoke-UnityPreflight.ps1");
        private static string AnalyzerScriptPath => Path.Combine(HarnessRoot, "Invoke-AnalyzerCheck.ps1");
        private static string AnalyzerBaselineScriptPath => Path.Combine(HarnessRoot, "Update-AnalyzerBaseline.ps1");
        private static string CompileScriptPath => Path.Combine(HarnessRoot, "Invoke-UnityCompileCheck.ps1");
        private static string ConsoleScriptPath => Path.Combine(HarnessRoot, "Invoke-UnityConsoleCheck.ps1");
        private static string ConsoleBaselineScriptPath => Path.Combine(HarnessRoot, "Update-UnityConsoleBaseline.ps1");
        private static string EditModeTestsScriptPath => Path.Combine(HarnessRoot, "Invoke-UnityEditModeTests.ps1");
        private static string PlayModeTestsScriptPath => Path.Combine(HarnessRoot, "Invoke-UnityPlayModeTests.ps1");
        private static string QuickValidatorsScriptPath => Path.Combine(HarnessRoot, "Invoke-UnityQuickValidators.ps1");
        private static string DocumentationScriptPath => Path.Combine(HarnessRoot, "Invoke-DocumentationCheck.ps1");
        private static string ArtifactRoot => Path.Combine(ProjectRoot, "Artifacts", "Validation");

        [MenuItem("Tools/RageQuitting/Validation")]
        private static void OpenWindow()
        {
            var window = GetWindow<ValidationHarnessWindow>();
            window.titleContent = new GUIContent("Validation");
            window.minSize = new Vector2(480f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshContext();
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
            AssemblyReloadEvents.beforeAssemblyReload -= DetachOwnedProcess;
            AssemblyReloadEvents.beforeAssemblyReload += DetachOwnedProcess;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Poll;
            AssemblyReloadEvents.beforeAssemblyReload -= DetachOwnedProcess;
            DetachOwnedProcess();
        }

        private void OnGUI()
        {
            DrawContext();
            EditorGUILayout.Space(8f);
            DrawRunControls();
            EditorGUILayout.Space(8f);
            DrawUtilityControls();
            EditorGUILayout.Space(8f);
            DrawOutput();
        }

        private void DrawContext()
        {
            EditorGUILayout.LabelField("Editor Context", EditorStyles.boldLabel);
            if (context == null)
            {
                EditorGUILayout.HelpBox("Editor context is unavailable.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Unity", context.unityVersion);
            EditorGUILayout.LabelField("Play Mode", context.playModeState);
            EditorGUILayout.LabelField("Compiling / Updating", $"{context.isCompiling} / {context.isUpdating}");
            EditorGUILayout.LabelField("Active Scene", string.IsNullOrEmpty(context.activeScenePath) ? context.activeSceneName : context.activeScenePath);
            EditorGUILayout.LabelField("Dirty Open Scenes", context.openScenes.Count(scene => scene.isDirty).ToString());
            EditorGUILayout.LabelField("Pipeline", context.pipelinePackagePresent ? context.pipelinePackageVersion : "Unavailable");
        }

        private void DrawRunControls()
        {
            EditorGUILayout.LabelField("Preflight", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(IsHarnessRunning || !File.Exists(PreflightScriptPath)))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Run Auto")) StartPreflight("Auto");
                if (GUILayout.Button("Run Fast")) StartPreflight("Fast");
                if (GUILayout.Button("Run Gameplay")) StartPreflight("Gameplay");
                if (GUILayout.Button("Run Full")) StartPreflight("Full");
                EditorGUILayout.EndHorizontal();
            }

            if (!File.Exists(PreflightScriptPath))
            {
                EditorGUILayout.HelpBox("Invoke-UnityPreflight.ps1 is missing.", MessageType.Error);
            }
        }

        private void DrawUtilityControls()
        {
            EditorGUILayout.LabelField("Utilities", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(IsHarnessRunning || !File.Exists(AnalyzerScriptPath)))
            {
                if (GUILayout.Button("Analyzer Check")) StartScript("Analyzer Check", AnalyzerScriptPath, "-Json");
            }
            using (new EditorGUI.DisabledScope(IsHarnessRunning || !File.Exists(CompileScriptPath)))
            {
                if (GUILayout.Button("Compile Check")) StartScript("Compile Check", CompileScriptPath, "-Json");
            }
            using (new EditorGUI.DisabledScope(IsHarnessRunning || !File.Exists(ConsoleScriptPath)))
            {
                if (GUILayout.Button("Console Check")) StartScript("Console Check", ConsoleScriptPath, "-Json");
            }
            using (new EditorGUI.DisabledScope(IsHarnessRunning || !File.Exists(EditModeTestsScriptPath)))
            {
                if (GUILayout.Button("EditMode Tests")) StartScript("EditMode Tests", EditModeTestsScriptPath, "-Json");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(IsHarnessRunning || !File.Exists(PlayModeTestsScriptPath)))
            {
                if (GUILayout.Button("PlayMode Tests")) StartScript("PlayMode Tests", PlayModeTestsScriptPath, "-Json");
            }
            using (new EditorGUI.DisabledScope(IsHarnessRunning || !File.Exists(QuickValidatorsScriptPath)))
            {
                if (GUILayout.Button("Quick Validators")) StartScript("Quick Validators", QuickValidatorsScriptPath, "-Json");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(IsHarnessRunning || !File.Exists(AnalyzerBaselineScriptPath)))
            {
                if (GUILayout.Button("Prune Analyzer Baseline"))
                {
                    StartScript("Prune Analyzer Baseline", AnalyzerBaselineScriptPath, "-PruneResolved -Json");
                }

                if (GUILayout.Button("Accept New Analyzer Warnings") &&
                    EditorUtility.DisplayDialog(
                        "Accept New Analyzer Warnings",
                        "Add current new or increased Correctness and Type Safety diagnostics to the analyzer baseline? Resolved entries will be retained.",
                        "Accept Warnings",
                        "Cancel"))
                {
                    StartScript("Accept New Analyzer Warnings", AnalyzerBaselineScriptPath, "-AcceptNew -Json");
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(IsHarnessRunning || !File.Exists(ConsoleBaselineScriptPath)))
            {
                if (GUILayout.Button("Prune Console Baseline"))
                {
                    StartScript("Prune Console Baseline", ConsoleBaselineScriptPath, "-PruneResolved -Json");
                }

                if (GUILayout.Button("Accept Current Console Errors") &&
                    EditorUtility.DisplayDialog(
                        "Accept Current Console Errors",
                        "Add current or increased Unity Console errors to the baseline? Resolved entries will be retained. This does not clear the Console.",
                        "Accept Errors",
                        "Cancel"))
                {
                    StartScript("Accept Current Console Errors", ConsoleBaselineScriptPath, "-AcceptCurrent -Json");
                }
            }
            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(IsHarnessRunning || !File.Exists(DocumentationScriptPath)))
            {
                if (GUILayout.Button("Documentation Check/Sync")) StartScript("Documentation Check/Sync", DocumentationScriptPath, "-Json");
            }

            string[] missingScripts =
            {
                File.Exists(AnalyzerScriptPath) ? null : "Invoke-AnalyzerCheck.ps1",
                File.Exists(AnalyzerBaselineScriptPath) ? null : "Update-AnalyzerBaseline.ps1",
                File.Exists(CompileScriptPath) ? null : "Invoke-UnityCompileCheck.ps1",
                File.Exists(ConsoleScriptPath) ? null : "Invoke-UnityConsoleCheck.ps1",
                File.Exists(ConsoleBaselineScriptPath) ? null : "Update-UnityConsoleBaseline.ps1",
                File.Exists(EditModeTestsScriptPath) ? null : "Invoke-UnityEditModeTests.ps1",
                File.Exists(PlayModeTestsScriptPath) ? null : "Invoke-UnityPlayModeTests.ps1",
                File.Exists(QuickValidatorsScriptPath) ? null : "Invoke-UnityQuickValidators.ps1",
                File.Exists(DocumentationScriptPath) ? null : "Invoke-DocumentationCheck.ps1"
            };
            string missingText = string.Join(", ", missingScripts.Where(path => !string.IsNullOrEmpty(path)));
            if (!string.IsNullOrEmpty(missingText))
            {
                EditorGUILayout.HelpBox("Unavailable because these scripts are missing: " + missingText + ".", MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Last Report")) OpenLastReport();
            if (GUILayout.Button("Open Artifacts")) OpenArtifacts();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawOutput()
        {
            string heading = IsHarnessRunning ? $"Running: {runningLabel}" : "Result";
            EditorGUILayout.LabelField(heading, EditorStyles.boldLabel);
            outputScroll = EditorGUILayout.BeginScrollView(outputScroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.SelectableLabel(liveOutput, EditorStyles.textArea, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private bool IsHarnessRunning
        {
            get
            {
                if (ownedProcess == null)
                {
                    return false;
                }

                try
                {
                    return !ownedProcess.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }

        private void StartPreflight(string tier)
        {
            StartScript($"Preflight {tier}", PreflightScriptPath, $"-Tier {tier} -Json");
        }

        private void StartScript(string label, string scriptPath, string scriptArguments)
        {
            if (IsHarnessRunning || !File.Exists(scriptPath))
            {
                return;
            }

            string launchDirectory = Path.Combine(ArtifactRoot, "EditorLaunches");
            Directory.CreateDirectory(launchDirectory);
            string launchId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
            processStdOutPath = Path.Combine(launchDirectory, launchId + ".stdout.txt");
            processStdErrPath = Path.Combine(launchDirectory, launchId + ".stderr.txt");

            string command = $"& '{EscapePowerShellLiteral(scriptPath)}' {scriptArguments} 1> '{EscapePowerShellLiteral(processStdOutPath)}' 2> '{EscapePowerShellLiteral(processStdErrPath)}'; exit $LASTEXITCODE";
            string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            var startInfo = new ProcessStartInfo
            {
                FileName = ResolvePowerShellPath(),
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encodedCommand,
                WorkingDirectory = ProjectRoot,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                ownedProcess = Process.Start(startInfo);
                if (ownedProcess == null)
                {
                    throw new InvalidOperationException("PowerShell process did not start.");
                }

                runningLabel = label;
                liveOutput = $"Started {label}.";
                nextOutputRefresh = 0d;
            }
            catch (Exception exception)
            {
                liveOutput = $"Failed to start {label}: {exception.Message}";
                DisposeOwnedProcess();
                Debug.LogException(exception);
            }
        }

        private void Poll()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now >= nextContextRefresh)
            {
                RefreshContext();
                nextContextRefresh = now + ContextRefreshIntervalSeconds;
            }

            if (ownedProcess == null || now < nextOutputRefresh)
            {
                return;
            }

            nextOutputRefresh = now + OutputRefreshIntervalSeconds;
            RefreshLiveOutput();
            bool exited;
            try
            {
                exited = ownedProcess.HasExited;
            }
            catch (InvalidOperationException)
            {
                exited = true;
            }

            if (!exited)
            {
                Repaint();
                return;
            }

            int exitCode = ownedProcess.ExitCode;
            RefreshLiveOutput();
            RecordLastReportFromOutputOrArtifacts();
            liveOutput = $"{runningLabel} finished with exit code {exitCode}.\n\n{liveOutput}";
            DisposeOwnedProcess();
            Repaint();
        }

        private void RefreshContext()
        {
            context = ValidationEditorContextService.Capture();
            Repaint();
        }

        private void RefreshLiveOutput()
        {
            string stdout = ReadSharedText(processStdOutPath);
            string stderr = ReadSharedText(processStdErrPath);
            string combined = string.IsNullOrWhiteSpace(stderr) ? stdout : stdout + Environment.NewLine + stderr;
            if (!string.IsNullOrWhiteSpace(combined))
            {
                const int maximumCharacters = 6000;
                liveOutput = combined.Length <= maximumCharacters ? combined : combined.Substring(combined.Length - maximumCharacters);
            }
        }

        private void RecordLastReportFromOutputOrArtifacts()
        {
            string reportPath = string.Empty;
            string stdout = ReadSharedText(processStdOutPath).Trim();
            if (!string.IsNullOrEmpty(stdout))
            {
                try
                {
                    ValidationHarnessLaunchResult result = JsonUtility.FromJson<ValidationHarnessLaunchResult>(stdout);
                    if (result != null)
                    {
                        string resultPath = !string.IsNullOrEmpty(result.reportPath) ? result.reportPath : result.summaryPath;
                        if (!string.IsNullOrEmpty(resultPath) && File.Exists(resultPath))
                        {
                            reportPath = resultPath;
                        }
                    }
                }
                catch (ArgumentException)
                {
                    // Fall back to newest summary below.
                }
            }

            if (string.IsNullOrEmpty(reportPath))
            {
                reportPath = FindNewestReport();
            }

            if (!string.IsNullOrEmpty(reportPath))
            {
                EditorPrefs.SetString(LastReportPreferenceKey, reportPath);
            }
        }

        private static string FindNewestReport()
        {
            if (!Directory.Exists(ArtifactRoot))
            {
                return string.Empty;
            }

            string[] reportNames =
            {
                "summary.json",
                "analyzer-report.json",
                "baseline-update.json",
                "unity-compile-report.json",
                "unity-console-report.json",
                "console-baseline-update.json",
                "unity-editmode-tests-report.json",
                "unity-playmode-tests-report.json",
                "unity-quick-validators-report.json"
            };
            return reportNames
                .SelectMany(name => Directory.GetFiles(ArtifactRoot, name, SearchOption.AllDirectories))
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault() ?? string.Empty;
        }

        private static void OpenLastReport()
        {
            string reportPath = EditorPrefs.GetString(LastReportPreferenceKey, string.Empty);
            if (string.IsNullOrEmpty(reportPath) || !File.Exists(reportPath))
            {
                reportPath = FindNewestReport();
            }

            if (!string.IsNullOrEmpty(reportPath) && File.Exists(reportPath))
            {
                EditorPrefs.SetString(LastReportPreferenceKey, reportPath);
                EditorUtility.OpenWithDefaultApp(reportPath);
            }
            else
            {
                Debug.LogWarning("No validation report was found.");
            }
        }

        private static void OpenArtifacts()
        {
            if (Directory.Exists(ArtifactRoot))
            {
                EditorUtility.RevealInFinder(ArtifactRoot);
            }
            else
            {
                Debug.LogWarning("Validation artifacts directory does not exist yet.");
            }
        }

        private void DetachOwnedProcess()
        {
            EditorApplication.update -= Poll;
            if (ownedProcess == null)
            {
                return;
            }

            // Process.Dispose releases this Editor's handle and never terminates the child process.
            DisposeOwnedProcess();
        }

        private void DisposeOwnedProcess()
        {
            if (ownedProcess != null)
            {
                ownedProcess.Dispose();
                ownedProcess = null;
            }

            runningLabel = string.Empty;
        }

        private static string ResolvePowerShellPath()
        {
            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string systemPowerShell = Path.Combine(windowsDirectory, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            return File.Exists(systemPowerShell) ? systemPowerShell : "powershell.exe";
        }

        private static string EscapePowerShellLiteral(string value)
        {
            return value.Replace("'", "''");
        }

        private static string ReadSharedText(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return string.Empty;
            }

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, true))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (IOException)
            {
                return string.Empty;
            }
        }
    }
}
