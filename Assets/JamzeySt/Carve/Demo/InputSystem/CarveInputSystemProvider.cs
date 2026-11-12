#if ENABLE_INPUT_SYSTEM
using UnityEngine;
using UnityEngine.InputSystem;

namespace JamzeySt.Carve.Demo
{
    // Compiled only when com.unity.inputsystem is installed; replaces the legacy demo input.
    class CarveInputSystemProvider : ICarveDemoInput
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register() => CarveDemoInput.Provider = new CarveInputSystemProvider();

        public Vector2 Move
        {
            get
            {
                var k = Keyboard.current;
                if (k == null) return Vector2.zero;
                float x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1 : 0) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1 : 0);
                float y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1 : 0) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1 : 0);
                return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
            }
        }

        public float Rise
        {
            get
            {
                var k = Keyboard.current;
                if (k == null) return 0f;
                return (k.eKey.isPressed || k.spaceKey.isPressed ? 1f : 0f) - (k.qKey.isPressed || k.leftCtrlKey.isPressed ? 1f : 0f);
            }
        }

        public bool Run => Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
        public bool Orbiting => Mouse.current != null && Mouse.current.rightButton.isPressed;
        public Vector2 Look => Mouse.current != null ? Mouse.current.delta.ReadValue() * 0.1f : Vector2.zero;
        public float Zoom => Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;

        public bool TogglePressed(KeyCode key)
        {
            var k = Keyboard.current;
            if (k == null) return false;
            switch (key)
            {
                case KeyCode.F1: return k.f1Key.wasPressedThisFrame;
                case KeyCode.C: return k.cKey.wasPressedThisFrame;
                case KeyCode.V: return k.vKey.wasPressedThisFrame;
                case KeyCode.L: return k.lKey.wasPressedThisFrame;
                case KeyCode.G: return k.gKey.wasPressedThisFrame;
                case KeyCode.F2: return k.f2Key.wasPressedThisFrame;
                case KeyCode.F3: return k.f3Key.wasPressedThisFrame;
                case KeyCode.T: return k.tKey.wasPressedThisFrame;
                default: return false;
            }
        }
    }
}
#endif
