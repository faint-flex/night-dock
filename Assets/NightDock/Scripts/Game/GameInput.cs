using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NightDock.Game
{
    public enum GameKey { W, A, S, D, Q, E, Up, Down, Left, Right, Space, F, X, Escape, Alpha0, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6, Alpha7, Enter, Shift }

    /// <summary>Works with either Unity input backend, so the project builds whichever one is active.</summary>
    public static class GameInput
    {
#if ENABLE_INPUT_SYSTEM
        static Key Map(GameKey k)
        {
            switch (k)
            {
                case GameKey.W: return Key.W;
                case GameKey.A: return Key.A;
                case GameKey.S: return Key.S;
                case GameKey.D: return Key.D;
                case GameKey.Q: return Key.Q;
                case GameKey.E: return Key.E;
                case GameKey.Up: return Key.UpArrow;
                case GameKey.Down: return Key.DownArrow;
                case GameKey.Left: return Key.LeftArrow;
                case GameKey.Right: return Key.RightArrow;
                case GameKey.Space: return Key.Space;
                case GameKey.F: return Key.F;
                case GameKey.X: return Key.X;
                case GameKey.Escape: return Key.Escape;
                case GameKey.Alpha0: return Key.Digit0;
                case GameKey.Alpha1: return Key.Digit1;
                case GameKey.Alpha2: return Key.Digit2;
                case GameKey.Alpha3: return Key.Digit3;
                case GameKey.Alpha4: return Key.Digit4;
                case GameKey.Alpha5: return Key.Digit5;
                case GameKey.Alpha6: return Key.Digit6;
                case GameKey.Alpha7: return Key.Digit7;
                case GameKey.Enter: return Key.Enter;
                default: return Key.LeftShift;
            }
        }

        public static bool Held(GameKey k) => Keyboard.current != null && Keyboard.current[Map(k)].isPressed;
        public static bool Down(GameKey k) => Keyboard.current != null && Keyboard.current[Map(k)].wasPressedThisFrame;
        public static Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        public static Vector2 MouseDelta => Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
        public static float Scroll => Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
        public static bool LeftDown => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        public static bool RightDown => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        public static bool MiddleHeld => Mouse.current != null && Mouse.current.middleButton.isPressed;
        public static bool RightHeld => Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
        static KeyCode Map(GameKey k)
        {
            switch (k)
            {
                case GameKey.W: return KeyCode.W;
                case GameKey.A: return KeyCode.A;
                case GameKey.S: return KeyCode.S;
                case GameKey.D: return KeyCode.D;
                case GameKey.Q: return KeyCode.Q;
                case GameKey.E: return KeyCode.E;
                case GameKey.Up: return KeyCode.UpArrow;
                case GameKey.Down: return KeyCode.DownArrow;
                case GameKey.Left: return KeyCode.LeftArrow;
                case GameKey.Right: return KeyCode.RightArrow;
                case GameKey.Space: return KeyCode.Space;
                case GameKey.F: return KeyCode.F;
                case GameKey.X: return KeyCode.X;
                case GameKey.Escape: return KeyCode.Escape;
                case GameKey.Alpha0: return KeyCode.Alpha0;
                case GameKey.Alpha1: return KeyCode.Alpha1;
                case GameKey.Alpha2: return KeyCode.Alpha2;
                case GameKey.Alpha3: return KeyCode.Alpha3;
                case GameKey.Alpha4: return KeyCode.Alpha4;
                case GameKey.Alpha5: return KeyCode.Alpha5;
                case GameKey.Alpha6: return KeyCode.Alpha6;
                case GameKey.Alpha7: return KeyCode.Alpha7;
                case GameKey.Enter: return KeyCode.Return;
                default: return KeyCode.LeftShift;
            }
        }

        public static bool Held(GameKey k) => Input.GetKey(Map(k));
        public static bool Down(GameKey k) => Input.GetKeyDown(Map(k));
        public static Vector2 MousePosition => Input.mousePosition;
        public static Vector2 MouseDelta => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
        public static float Scroll => Input.mouseScrollDelta.y;
        public static bool LeftDown => Input.GetMouseButtonDown(0);
        public static bool RightDown => Input.GetMouseButtonDown(1);
        public static bool MiddleHeld => Input.GetMouseButton(2);
        public static bool RightHeld => Input.GetMouseButton(1);
#endif
    }
}
