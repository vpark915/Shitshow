using System.Collections.Generic;
using UnityEngine;

// On a traffic signal post or walk signal post (Blender/Street): runs its lights. Every post in the scene keeps the same
// clock, so traffic along Z gets green, then amber, a beat of all red, then traffic along X does. A head controls the
// traffic coming toward it (the way it faces); a walk head lets people cross toward it while the traffic going the same
// way has green: WALK for the first part, then the hand flashes, then a steady hand.
// Parts by name, from the model: Lens_Red / Lens_Yellow / Lens_Green under each head, Face under each ped head.
public class TrafficSignal : MonoBehaviour
{
    public const float Green = 14, Amber = 3, AllRed = 1.5f, Walk = 8;
    const float Half = Green + Amber + AllRed;

    enum Light { Green, Amber, Red }
    enum Ped { Walk, Flash, Hand }

    class Head { public Renderer red, amber, green; public bool alongZ; public Light shown = (Light)(-1); }
    class PedHead { public Renderer face; public bool alongZ; public int shown = -1; }

    readonly List<Head> heads = new();
    readonly List<PedHead> peds = new();

    static Material redOn, redOff, amberOn, amberOff, greenOn, greenOff, walk, hand, handOff;

    static Material Lens(Color color, bool lit)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Lens" };
        m.color = lit ? Color.Lerp(color, Color.white, 0.25f) : color * 0.22f;
        m.SetFloat("_Smoothness", 0.85f);
        if (lit)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * 3.5f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        return m;
    }

    static Material FaceMaterial(string art)
    {
        Texture2D tex = StreetFurniture.Art(art);
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Signal " + art, mainTexture = tex };
        m.SetTexture("_BaseMap", tex);
        return m;
    }

    static void Materials()
    {
        if (redOn) return;
        Color red = new Color(1f, 0.12f, 0.06f), amber = new Color(1f, 0.62f, 0.08f), green = new Color(0.15f, 1f, 0.55f);
        redOn = Lens(red, true); redOff = Lens(red, false);
        amberOn = Lens(amber, true); amberOff = Lens(amber, false);
        greenOn = Lens(green, true); greenOff = Lens(green, false);
        walk = FaceMaterial("signal_walk");
        hand = FaceMaterial("signal_dont_walk");
        handOff = FaceMaterial("signal_dont_walk_off");
    }

    void Awake()
    {
        Materials();
        var byHead = new Dictionary<Transform, Head>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            string n = r.name;
            if (n.StartsWith("Lens_"))
            {
                if (!byHead.TryGetValue(r.transform.parent, out Head head))
                {
                    byHead[r.transform.parent] = head = new Head();
                    heads.Add(head);
                }
                if (n.StartsWith("Lens_Red")) head.red = r;
                else if (n.StartsWith("Lens_Yellow")) head.amber = r;
                else if (n.StartsWith("Lens_Green")) head.green = r;
                head.alongZ = AlongZ(r.transform.forward); // the lenses look out the front of the head
            }
            else if (n.StartsWith("Face") && r.TryGetComponent(out MeshFilter mesh) && mesh.sharedMesh && mesh.sharedMesh.normals.Length > 0)
                peds.Add(new PedHead { face = r, alongZ = AlongZ(r.transform.TransformDirection(mesh.sharedMesh.normals[0])) });
        }
        heads.RemoveAll(h => !h.red || !h.amber || !h.green);
    }

    static bool AlongZ(Vector3 facing) => Mathf.Abs(facing.z) >= Mathf.Abs(facing.x);

    static Light LightFor(bool alongZ, float t)
    {
        float mine = Mathf.Repeat(t - (alongZ ? 0 : Half), Half * 2); // this direction's half of the cycle comes first
        return mine < Green ? Light.Green : mine < Green + Amber ? Light.Amber : Light.Red;
    }

    static Ped PedFor(bool alongZ, float t)
    {
        float mine = Mathf.Repeat(t - (alongZ ? 0 : Half), Half * 2);
        return mine < Walk ? Ped.Walk : mine < Green + Amber ? Ped.Flash : Ped.Hand;
    }

    void Update()
    {
        float t = Time.time;
        foreach (Head h in heads)
        {
            Light l = LightFor(h.alongZ, t);
            if (l == h.shown) continue;
            h.shown = l;
            h.red.sharedMaterial = l == Light.Red ? redOn : redOff;
            h.amber.sharedMaterial = l == Light.Amber ? amberOn : amberOff;
            h.green.sharedMaterial = l == Light.Green ? greenOn : greenOff;
        }
        bool flashOn = Mathf.Repeat(t, 1f) < 0.5f;
        foreach (PedHead p in peds)
        {
            Ped s = PedFor(p.alongZ, t);
            int show = s == Ped.Walk ? 0 : s == Ped.Hand || flashOn ? 1 : 2;
            if (show == p.shown) continue;
            p.shown = show;
            p.face.sharedMaterial = show == 0 ? walk : show == 1 ? hand : handOff;
        }
    }
}
