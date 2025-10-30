#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;

public class TerrainShaderStripper : IPreprocessShaders
{
    public int callbackOrder => 9999;

    // If you truly never use Terrain, keep this true.
    private const bool STRIP_URP_TERRAIN = true;

    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> compilerData)
    {
        if (!STRIP_URP_TERRAIN || shader == null) return;

        // Match URP terrain shaders by name; adjust if you have custom terrain shaders you want to keep.
        string n = shader.name;
        bool isURPTerrain =
               n.StartsWith("Universal Render Pipeline/Terrain")
            || n.Contains("/TerrainLit")
            || n.Contains("TerrainLitAdd")
            || n.Contains("TerrainLitBase");

        if (!isURPTerrain) return;

        // Strip all variants of this shader
        if (compilerData.Count > 0)
        {
            compilerData.Clear();
            // Optional: log once if you want to verify
            // Debug.Log($"[Strip] {shader.name}");
        }
    }
}
#endif
