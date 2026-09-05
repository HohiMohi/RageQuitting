using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

namespace RageQuitting.Editor.AgentHarness
{
    internal sealed class UnityAnalyzerProjectPostprocessor : AssetPostprocessor
    {
        private const string AnalyzerFileName = "Microsoft.Unity.Analyzers.dll";

        private static readonly Regex AnalyzerLinePattern = new Regex(
            @"(?m)^[ \t]*<Analyzer\b(?=[^>\r\n]*\bInclude\s*=)[^>\r\n]*\bInclude\s*=\s*(?<quote>[""'])(?<include>[^""'\r\n]+)\k<quote>[^>\r\n]*/?>[ \t]*(?:\r?\n|$)",
            RegexOptions.CultureInvariant);

        public static string OnGeneratedCSProject(string path, string content)
        {
            string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(path));
            string localAnalyzerPath = Path.GetFullPath(Path.Combine(
                projectDirectory,
                "Assets",
                "Analyzers",
                AnalyzerFileName));
            bool localAnalyzerSeen = false;

            return AnalyzerLinePattern.Replace(content, match =>
            {
                string include = match.Groups["include"].Value;
                if (!HasAnalyzerFileName(include) ||
                    !TryResolvePath(projectDirectory, include, out string resolvedPath))
                {
                    return match.Value;
                }

                if (!string.Equals(resolvedPath, localAnalyzerPath, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                if (localAnalyzerSeen)
                {
                    return string.Empty;
                }

                localAnalyzerSeen = true;
                return match.Value;
            });
        }

        private static bool HasAnalyzerFileName(string include)
        {
            string normalized = include.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return string.Equals(Path.GetFileName(normalized), AnalyzerFileName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryResolvePath(string projectDirectory, string include, out string resolvedPath)
        {
            try
            {
                string normalized = include.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                resolvedPath = Path.GetFullPath(
                    Path.IsPathRooted(normalized)
                        ? normalized
                        : Path.Combine(projectDirectory, normalized));
                return true;
            }
            catch (Exception)
            {
                resolvedPath = string.Empty;
                return false;
            }
        }
    }
}
