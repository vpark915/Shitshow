using UnityEngine;

namespace JamzeySt.Carve.Demo
{
    // Minimal playable character for the demos: WASD movement with a procedural two-foot gait. Each foot raycasts the
    // ground, so on a Carve relief collider the feet stand on the carved stones.
    [RequireComponent(typeof(CharacterController))]
    public class CarveDemoWalker : MonoBehaviour
    {
        public Transform cameraTransform;
        public Transform leftFoot;
        public Transform rightFoot;
        public float walkSpeed = 1.7f;
        public float runSpeed = 4.2f;
        public float stride = 0.7f;
        public float footSpacing = 0.14f;
        public float stepHeight = 0.25f;
        public LayerMask groundLayers = ~0;

        CharacterController m_Controller;
        readonly Vector3[] m_Plant = new Vector3[2];
        readonly Vector3[] m_From = new Vector3[2];
        int m_Moving = -1;
        float m_StepT;
        float m_StepDuration = 0.3f;
        float m_VerticalSpeed;
        Vector3 m_Heading = Vector3.forward;

        void Start()
        {
            m_Controller = GetComponent<CharacterController>();
            m_Heading = transform.forward;
            for (int i = 0; i < 2; i++) m_Plant[i] = FootTarget(i, Vector3.zero);
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector3 move = ReadMove();
            Vector3 velocity = move * (CarveDemoInput.Run ? runSpeed : walkSpeed);

            m_VerticalSpeed = m_Controller.isGrounded ? -1f : m_VerticalSpeed + Physics.gravity.y * dt;
            m_Controller.Move((velocity + Vector3.up * m_VerticalSpeed) * dt);

            if (move.sqrMagnitude > 0.01f)
            {
                m_Heading = Vector3.Slerp(m_Heading, move.normalized, 1f - Mathf.Exp(-10f * dt));
                transform.rotation = Quaternion.LookRotation(m_Heading, Vector3.up);
            }
            UpdateGait(velocity, dt);
        }

        Vector3 ReadMove()
        {
            Vector2 input = CarveDemoInput.Move;
            if (input.sqrMagnitude < 0.0001f) return Vector3.zero;
            Vector3 fwd = cameraTransform != null ? Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            return fwd * input.y + right * input.x;
        }

        Vector3 FootTarget(int foot, Vector3 velocity)
        {
            Vector3 side = transform.right * (foot == 0 ? -footSpacing : footSpacing);
            Vector3 lead = velocity.sqrMagnitude > 0.01f ? velocity.normalized * stride * 0.5f : Vector3.zero;
            Vector3 p = transform.position + side + lead + Vector3.up;
            return Physics.Raycast(p, Vector3.down, out var hit, 4f, groundLayers, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform)
                ? hit.point
                : new Vector3(p.x, transform.position.y, p.z);
        }

        void UpdateGait(Vector3 velocity, float dt)
        {
            if (m_Moving < 0)
            {
                // Step with whichever foot has drifted furthest from where it wants to be.
                float d0 = Vector3.Distance(m_Plant[0], FootTarget(0, velocity));
                float d1 = Vector3.Distance(m_Plant[1], FootTarget(1, velocity));
                int foot = d0 >= d1 ? 0 : 1;
                if (Mathf.Max(d0, d1) > stride * 0.55f)
                {
                    m_Moving = foot;
                    m_From[foot] = m_Plant[foot];
                    m_StepT = 0f;
                    m_StepDuration = Mathf.Clamp(stride / Mathf.Max(velocity.magnitude, 0.5f) * 0.5f, 0.12f, 0.35f);
                }
            }
            if (m_Moving >= 0)
            {
                m_StepT += dt / m_StepDuration;
                if (m_StepT >= 1f)
                {
                    m_Plant[m_Moving] = FootTarget(m_Moving, velocity);
                    m_Moving = -1;
                }
            }
            PlaceFoot(0, velocity);
            PlaceFoot(1, velocity);
        }

        void PlaceFoot(int foot, Vector3 velocity)
        {
            var t = foot == 0 ? leftFoot : rightFoot;
            if (t == null) return;
            Vector3 pos = m_Plant[foot];
            if (foot == m_Moving)
            {
                Vector3 to = FootTarget(foot, velocity);
                float s = Mathf.SmoothStep(0f, 1f, m_StepT);
                pos = Vector3.Lerp(m_From[foot], to, s) + Vector3.up * (stepHeight * Mathf.Sin(Mathf.PI * m_StepT));
            }
            t.SetPositionAndRotation(pos, Quaternion.LookRotation(m_Heading, Vector3.up));
        }
    }
}
