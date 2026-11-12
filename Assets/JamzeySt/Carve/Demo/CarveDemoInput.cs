using UnityEngine;

namespace JamzeySt.Carve.Demo
{
    public interface ICarveDemoInput
    {
        Vector2 Move { get; }
        float Rise { get; }
        bool Run { get; }
        bool Orbiting { get; }
        Vector2 Look { get; }
        float Zoom { get; }
        bool TogglePressed(KeyCode key);
    }

    // Demo input. Uses the legacy Input Manager unless the optional Input System assembly
    // (Demo/InputSystem, compiled only when com.unity.inputsystem is installed) registers a provider.
    public static class CarveDemoInput
    {
        public static ICarveDemoInput Provider = new LegacyInput();

        public static Vector2 Move => Provider.Move;
        public static float Rise => Provider.Rise;
        public static bool Run => Provider.Run;
        public static bool Orbiting => Provider.Orbiting;
        public static Vector2 Look => Provider.Look;
        public static float Zoom => Provider.Zoom;
        public static bool TogglePressed(KeyCode key) => Provider.TogglePressed(key);

        class LegacyInput : ICarveDemoInput
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            public Vector2 Move => Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);
            public float Rise => (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl) ? 1f : 0f);
            public bool Run => Input.GetKey(KeyCode.LeftShift);
            public bool Orbiting => Input.GetMouseButton(1);
            public Vector2 Look => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 3f;
            public float Zoom => Input.mouseScrollDelta.y;
            public bool TogglePressed(KeyCode key) => Input.GetKeyDown(key);
#else
            public Vector2 Move => Vector2.zero;
            public float Rise => 0f;
            public bool Run => false;
            public bool Orbiting => false;
            public Vector2 Look => Vector2.zero;
            public float Zoom => 0f;
            public bool TogglePressed(KeyCode key) => false;
#endif
        }
    }
}
