// Assets/Editor/StripListedShaders.cs
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;

namespace BuildTools
{
    /// <summary>
    /// Strips specific shaders (by exact name or prefix) from player builds.
    /// Default behavior: only active for WebGL builds (configurable via ONLY_FOR_WEBGL).
    /// </summary>
    public sealed class StripListedShaders :
        IPreprocessShaders,
        IPreprocessBuildWithReport,
        IPostprocessBuildWithReport
    {
        // -----------------------------
        // CONFIG
        // -----------------------------

        // Toggle this off if you want stripping on ALL platforms.
        private const bool ONLY_FOR_WEBGL = true;

        // Exact shader names to remove entirely.
        private static readonly HashSet<string> ExactShaderNames = new HashSet<string>
        {
            // From your log:
            "Hidden/TerrainEngine/Details/UniversalPipeline/BillboardWavingDoublePass",
            "Hidden/CoreSRP/CoreCopy",
            "Hidden/Universal/HDRDebugView",
            "Hidden/TerrainEngine/Details/UniversalPipeline/WavingDoublePass",
        };

        // Any shader whose name starts with one of these prefixes will be removed.
        private static readonly string[] ShaderNamePrefixes =
        {
            // Remove all TerrainEngine detail shaders under URP (optional but handy):
            "Hidden/TerrainEngine/Details/UniversalPipeline/",
            // Add more prefixes here if needed
        };

        // -----------------------------
        // STATE
        // -----------------------------

        private static BuildTarget _currentTarget = BuildTarget.NoTarget;
        private static bool _activeForThisBuild = false;
        private static int _strippedCount = 0;

        // -----------------------------
        // Build hooks just to know which platform we’re building for
        // -----------------------------

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            _currentTarget = report.summary.platform;
            _strippedCount = 0;

            _activeForThisBuild = !ONLY_FOR_WEBGL || _currentTarget == BuildTarget.WebGL;
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (_activeForThisBuild)
            {
                Debug.Log($"[ShaderStripper] Finished build for {_currentTarget}. Total stripped shader variants: {_strippedCount}");
            }

            _currentTarget = BuildTarget.NoTarget;
            _activeForThisBuild = false;
        }

        // -----------------------------
        // Shader variant stripping
        // -----------------------------

        public void OnProcessShader(
            Shader shader,
            ShaderSnippetData snippet,
            IList<ShaderCompilerData> shaderCompilerData)
        {
            if (!_activeForThisBuild)
                return;

            if (shader == null)
                return;

            string name = shader.name;

            if (ShouldStrip(name))
            {
                _strippedCount += shaderCompilerData.Count;
                shaderCompilerData.Clear(); // remove all variants of this shader
#if UNITY_2021_2_OR_NEWER
                // Helpful, compact log:
                // Debug.Log($"[ShaderStripper] Stripped: \"{name}\" ({snippet.passName}/{snippet.shaderType})");
#else
                // Older Unity versions may not have snippet fields the same way
                // Debug.Log($"[ShaderStripper] Stripped: \"{name}\"");
#endif
            }
        }

        private static bool ShouldStrip(string shaderName)
        {
            if (ExactShaderNames.Contains(shaderName))
                return true;

            for (int i = 0; i < ShaderNamePrefixes.Length; i++)
            {
                if (!string.IsNullOrEmpty(ShaderNamePrefixes[i]) && shaderName.StartsWith(ShaderNamePrefixes[i]))
                    return true;
            }

            return false;
        }
    }
}
#endif
