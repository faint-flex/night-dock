using UnityEngine;

namespace NightDock.Game
{
    /// <summary>Tycoon camera: WASD / arrows / screen-edge pan, scroll zoom, Q/E rotate, middle-drag orbit.</summary>
    public class CameraRig : MonoBehaviour
    {
        public Camera Cam;
        public bool InputEnabled = true;
        public bool EdgePan = true;

        Vector3 _pivot = new Vector3(0f, 0f, -2f);
        float _yaw;
        float _pitch = 58f;
        float _distance = 62f;

        public static CameraRig Create()
        {
            var go = new GameObject("CameraRig");
            var rig = go.AddComponent<CameraRig>();
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(go.transform, false);
            rig.Cam = camGo.AddComponent<Camera>();
            rig.Cam.clearFlags = CameraClearFlags.SolidColor;
            rig.Cam.backgroundColor = new Color(0.03f, 0.04f, 0.07f);
            rig.Cam.fieldOfView = 45f;
            rig.Cam.nearClipPlane = 0.3f;
            rig.Cam.farClipPlane = 600f;
            camGo.AddComponent<AudioListener>();
            rig.Apply();
            return rig;
        }

        public void Frame(Vector3 pivot, float yaw, float pitch, float distance)
        {
            _pivot = pivot; _yaw = yaw; _pitch = pitch; _distance = distance;
            Apply();
        }

        void Update()
        {
            if (!InputEnabled) return;
            float dt = Time.unscaledDeltaTime;

            Vector2 move = Vector2.zero;
            if (GameInput.Held(GameKey.W) || GameInput.Held(GameKey.Up)) move.y += 1f;
            if (GameInput.Held(GameKey.S) || GameInput.Held(GameKey.Down)) move.y -= 1f;
            if (GameInput.Held(GameKey.A) || GameInput.Held(GameKey.Left)) move.x -= 1f;
            if (GameInput.Held(GameKey.D) || GameInput.Held(GameKey.Right)) move.x += 1f;

            if (EdgePan && Application.isFocused)
            {
                Vector2 m = GameInput.MousePosition;
                const float edge = 6f;
                if (m.x >= 0 && m.x <= Screen.width && m.y >= 0 && m.y <= Screen.height)
                {
                    if (m.x < edge) move.x -= 1f;
                    if (m.x > Screen.width - edge) move.x += 1f;
                    if (m.y < edge) move.y -= 1f;
                    if (m.y > Screen.height - edge) move.y += 1f;
                }
            }

            float speed = Mathf.Lerp(12f, 55f, Mathf.InverseLerp(20f, 120f, _distance));
            var rot = Quaternion.Euler(0f, _yaw, 0f);
            _pivot += rot * new Vector3(move.x, 0f, move.y) * speed * dt;
            _pivot.x = Mathf.Clamp(_pivot.x, -70f, 70f);
            _pivot.z = Mathf.Clamp(_pivot.z, -60f, 45f);

            if (GameInput.Held(GameKey.Q)) _yaw += 70f * dt;
            if (GameInput.Held(GameKey.E)) _yaw -= 70f * dt;
            if (GameInput.MiddleHeld)
            {
                var d = GameInput.MouseDelta;
                _yaw += d.x * 0.25f;
                _pitch = Mathf.Clamp(_pitch - d.y * 0.2f, 25f, 85f);
            }

            float scroll = GameInput.Scroll;
            if (Mathf.Abs(scroll) > 0.001f) _distance = Mathf.Clamp(_distance * (1f - scroll * 0.12f), 12f, 130f);

            Apply();
        }

        void Apply()
        {
            if (Cam == null) return;
            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Cam.transform.position = _pivot + rot * new Vector3(0f, 0f, -_distance);
            Cam.transform.rotation = rot;
        }
    }
}
