using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;

// At build time: drops every variant of shaders the game never draws with but that end up in the build anyway.
// ProBuilder keeps its default material in a Resources folder inside its package, and everything under Resources
// ships, so its "Standard Vertex Color" shader went in with 6,000+ variants (over an hour of compiling) though no
// shipped scene uses a ProBuilder material. ProBuilder still works in the editor; only the build leaves it out.
// If a shipped scene ever does use one of these shaders, take its prefix out of the list.
class StripUnusedShaders : IPreprocessShaders
{
    static readonly string[] Unused = { "ProBuilder6/", "Hidden/ProBuilder/" };

    public int callbackOrder => 0;

    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
    {
        foreach (string prefix in Unused)
            if (shader.name.StartsWith(prefix)) { data.Clear(); return; }
    }
}
