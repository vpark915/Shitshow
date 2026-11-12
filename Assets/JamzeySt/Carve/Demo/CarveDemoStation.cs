using System.Collections.Generic;
using UnityEngine;

namespace JamzeySt.Carve.Demo
{
    // A stop on the Showcase tour: the HUD shows its title and explanation while the player is within its radius.
    public class CarveDemoStation : MonoBehaviour
    {
        public string title = "Station";
        [TextArea(2, 6)] public string description = "";
        public float radius = 4f;

        public static readonly List<CarveDemoStation> All = new List<CarveDemoStation>();

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public static CarveDemoStation Nearest(Vector3 position)
        {
            CarveDemoStation best = null;
            float bestD = float.MaxValue;
            foreach (var s in All)
            {
                float d = Vector3.Distance(new Vector3(position.x, 0f, position.z), new Vector3(s.transform.position.x, 0f, s.transform.position.z));
                if (d < s.radius && d < bestD) { best = s; bestD = d; }
            }
            return best;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.91f, 0.63f, 0.31f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
