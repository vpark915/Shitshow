using System;
using System.Collections.Generic;
using UnityEngine;

namespace JamzeySt.Carve.Demo
{
    // A looping camera flyover: a smooth path through the points, looking along a smooth path through their targets.
    public class CarveDemoFlyover : MonoBehaviour
    {
        [Serializable]
        public struct Point
        {
            public Vector3 position;
            public Vector3 target;
        }

        public List<Point> points = new List<Point>();
        [Tooltip("Meters per second along the path.")]
        [Min(0.1f)] public float speed = 5f;

        float[] m_Times;

        public float Duration
        {
            get
            {
                Build();
                return m_Times != null && m_Times.Length > 0 ? m_Times[m_Times.Length - 1] : 0f;
            }
        }

        void OnValidate() => m_Times = null;

        // Time at each point (segment time from its length), the last entry closing the loop.
        void Build()
        {
            if (m_Times != null && m_Times.Length == points.Count + 1) return;
            m_Times = new float[points.Count + 1];
            for (int i = 0; i < points.Count; i++)
                m_Times[i + 1] = m_Times[i] + Vector3.Distance(points[i].position, points[(i + 1) % points.Count].position) / speed;
        }

        public void Evaluate(float time, out Vector3 position, out Quaternion rotation)
        {
            position = transform.position;
            rotation = transform.rotation;
            if (points.Count < 2) return;
            Build();
            float t = Mathf.Repeat(time, m_Times[points.Count]);
            int i = 0;
            while (i < points.Count - 1 && t >= m_Times[i + 1]) i++;
            float u = Mathf.InverseLerp(m_Times[i], m_Times[i + 1], t);
            u = u * u * (3f - 2f * u) * 0.35f + u * 0.65f;
            int n = points.Count;
            Point p0 = points[(i - 1 + n) % n], p1 = points[i], p2 = points[(i + 1) % n], p3 = points[(i + 2) % n];
            position = CatmullRom(p0.position, p1.position, p2.position, p3.position, u);
            Vector3 target = CatmullRom(p0.target, p1.target, p2.target, p3.target, u);
            var dir = target - position;
            if (dir.sqrMagnitude > 1e-6f) rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        static Vector3 CatmullRom(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * b + (c - a) * t + (2f * a - 5f * b + 4f * c - d) * t2 + (3f * b - a - 3f * c + d) * t3);
        }

        void OnDrawGizmosSelected()
        {
            if (points.Count < 2) return;
            float total = Duration;
            Gizmos.color = new Color(0.91f, 0.63f, 0.31f, 0.9f);
            Evaluate(0f, out var prev, out _);
            for (int k = 1; k <= 200; k++)
            {
                Evaluate(total * k / 200f, out var p, out _);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
            Gizmos.color = new Color(0.3f, 0.65f, 1f, 0.8f);
            foreach (var p in points)
            {
                Gizmos.DrawWireSphere(p.position, 0.4f);
                Gizmos.DrawLine(p.position, p.target);
            }
        }
    }
}
