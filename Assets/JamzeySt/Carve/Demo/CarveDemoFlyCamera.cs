using UnityEngine;

namespace JamzeySt.Carve.Demo
{
    // Free-fly demo camera: WASD fly, E / Q (Space / Ctrl) up and down, Shift faster, hold right mouse to look, scroll changes
    // speed. T plays the flyover tour; any movement takes control back. Stays above the colliders below it.
    [DisallowMultipleComponent]
    public class CarveDemoFlyCamera : MonoBehaviour
    {
        [Min(0.1f)] public float speed = 6f;
        [Min(1f)] public float fastMultiplier = 3f;
        public float sensitivity = 3f;
        [Tooltip("How quickly movement eases in and out.")]
        [Min(0.1f)] public float smoothing = 8f;
        [Tooltip("Keeps the camera at least this high above the colliders below it.")]
        [Min(0f)] public float minHeight = 0.8f;
        public LayerMask groundMask = ~0;

        [Header("Tour")]
        public CarveDemoFlyover tour;
        public bool playTourOnStart;

        float m_Yaw, m_Pitch, m_TourTime;
        Vector3 m_Velocity;
        bool m_Touring;
        Vector3 m_ProbeFrom, m_ProbeTo, m_ProbeHit;
        bool m_ProbeDidHit, m_Probed;

        public bool Touring => m_Touring;

        void Start()
        {
            SyncAngles();
            if (playTourOnStart && tour != null) StartTour();
        }

        public void StartTour()
        {
            if (tour == null) return;
            m_Touring = true;
            m_TourTime = 0f;
        }

        public void StopTour()
        {
            m_Touring = false;
            m_Velocity = Vector3.zero;
            SyncAngles();
        }

        void SyncAngles()
        {
            var e = transform.eulerAngles;
            m_Yaw = e.y;
            m_Pitch = e.x > 180f ? e.x - 360f : e.x;
        }

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var move = CarveDemoInput.Move;
            float rise = CarveDemoInput.Rise;
            if (CarveDemoInput.TogglePressed(KeyCode.T) && tour != null)
            {
                if (m_Touring) StopTour();
                else StartTour();
            }
            if (m_Touring)
            {
                if (move.sqrMagnitude > 0.01f || Mathf.Abs(rise) > 0.01f || CarveDemoInput.Orbiting) StopTour();
                else
                {
                    m_TourTime += dt;
                    tour.Evaluate(m_TourTime, out var tp, out var tr);
                    transform.SetPositionAndRotation(tp, tr);
                    return;
                }
            }

            speed = Mathf.Clamp(speed * Mathf.Pow(1.15f, CarveDemoInput.Zoom), 0.5f, 60f);
            if (CarveDemoInput.Orbiting)
            {
                var look = CarveDemoInput.Look;
                m_Yaw += look.x * sensitivity;
                m_Pitch = Mathf.Clamp(m_Pitch - look.y * sensitivity, -89f, 89f);
            }
            var rot = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
            Vector3 wish = (rot * new Vector3(move.x, 0f, move.y) + Vector3.up * rise) * speed * (CarveDemoInput.Run ? fastMultiplier : 1f);
            m_Velocity = Vector3.Lerp(m_Velocity, wish, 1f - Mathf.Exp(-smoothing * dt));
            transform.SetPositionAndRotation(KeepAboveGround(transform.position + m_Velocity * dt), rot);
        }

        Vector3 KeepAboveGround(Vector3 p)
        {
            m_Probed = true;
            m_ProbeFrom = p + Vector3.up * 2f;
            m_ProbeTo = p + Vector3.down * minHeight;
            m_ProbeDidHit = Physics.Raycast(m_ProbeFrom, Vector3.down, out var hit, 2f + minHeight, groundMask, QueryTriggerInteraction.Ignore);
            if (!m_ProbeDidHit) return p;
            m_ProbeHit = hit.point;
            if (p.y < hit.point.y + minHeight) p.y = hit.point.y + minHeight;
            return p;
        }

        void OnDrawGizmos()
        {
            if (!m_Probed) return;
            Gizmos.color = m_ProbeDidHit ? Color.green : Color.red;
            Gizmos.DrawLine(m_ProbeFrom, m_ProbeDidHit ? m_ProbeHit : m_ProbeTo);
            if (m_ProbeDidHit) Gizmos.DrawWireSphere(m_ProbeHit, 0.1f);
        }
    }
}
