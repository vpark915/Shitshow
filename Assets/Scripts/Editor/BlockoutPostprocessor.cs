using System.Linq;
using UnityEditor;
using UnityEngine;

// Runs every time the Washington Square blockout is (re)imported from Blender, so the clean-up and colliders survive re-exports.
class BlockoutPostprocessor : AssetPostprocessor
{
    // name prefixes of flat, hollow or walk-through pieces, which need their real shape (WSP_Park also covers the park's
    // surfaces and fences); everything else is a building or a small prop and gets a box
    static readonly string[] MeshShaped = { "WSP_Streets", "WSP_Park", "WSP_Fountain", "WSP_Arch", "WSP_Prop_LongBenches", "WSP_Prop_Mound" };

    const float PavingTile = 3f; // metres covered by one repeat of the paving texture

    // bump to force a reimport after changing this file
    public override uint GetVersion() => 5;

    void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.EndsWith("washingtonSquareBlockout.blend")) return;

        foreach (Transform child in root.transform.Cast<Transform>().ToArray())
        {
            // Blender's default cube and the empty camera / light nodes
            if (!child.GetComponent<MeshFilter>() || !child.name.StartsWith("WSP_"))
            {
                Object.DestroyImmediate(child.gameObject);
                continue;
            }

            child.gameObject.isStatic = true;
            // the paving slab and the streets come without usable UVs: project their textures in world metres,
            // from above for the ground and from the side for kerb faces
            if (child.name == "WSP_Park" || child.name == "WSP_Streets")
            {
                Mesh mesh = child.GetComponent<MeshFilter>().sharedMesh;
                Vector3[] normals = mesh.normals;
                mesh.uv = mesh.vertices.Select((v, i) =>
                {
                    Vector3 w = child.TransformPoint(v), n = child.TransformDirection(normals[i]);
                    if (Mathf.Abs(n.y) > 0.5f) return new Vector2(w.x, w.z) / PavingTile;
                    return new Vector2(Mathf.Abs(n.x) > Mathf.Abs(n.z) ? w.z : w.x, w.y) / PavingTile;
                }).ToArray();
            }
            if (MeshShaped.Any(child.name.StartsWith)) child.gameObject.AddComponent<MeshCollider>();
            else child.gameObject.AddComponent<BoxCollider>();
        }
    }
}
