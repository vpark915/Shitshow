using UnityEngine;

namespace JamzeySt.Carve.Demo
{
    // Third-person orbit camera for the demos: hold right mouse to orbit, scroll to zoom.
    public class CarveDemoCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 targetOffset = new Vector3(0f, 1.3f, 0f);
        public float distance = 4f;
        public float minDistance = 1f;
        public float maxDistance = 14f;
        public float yaw = 20f;
        public float pitch = 24f;
        public float sensitivity = 3f;

        void LateUpdate()
        {
            if (target == null) return;
            if (CarveDemoInput.Orbiting)
            {
                var look = CarveDemoInput.Look;
                yaw += look.x * sensitivity;
                pitch = Mathf.Clamp(pitch - look.y * sensitivity, -10f, 85f);
            }
            distance = Mathf.Clamp(distance - CarveDemoInput.Zoom * 0.5f, minDistance, maxDistance);

            var rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focus = target.position + targetOffset;
            transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
        }
    }
}
