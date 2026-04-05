// ============================================================================
//  MotorDebugHUD.cs
//  Runtime on-screen debug overlay showing motor state in real time.
//  Attach to any GameObject in the scene (TestSceneBuilder does this).
//
//  Displays:
//    - Grounded / Airborne state with color indicator
//    - Slope angle + ground normal
//    - Current velocity (X, Y)
//    - Wall contact direction
//    - Moving platform status
//    - Step offset triggers
//    - Frame-by-frame state transitions
//
//  Automatically finds the Player's Motor in scene.
// ============================================================================
using UnityEngine;
using PlatformerKit.Physics;
using PlatformerKit.Player;

namespace PlatformerKit.Debug
{
    public class MotorDebugHUD : MonoBehaviour
    {
        // ================================================================
        //  CONFIG
        // ================================================================

        [Header("Display")]
        [SerializeField] private bool showHUD = true;
        [SerializeField] private KeyCode toggleKey = KeyCode.F1;
        [SerializeField] private int fontSize = 14;

        // ================================================================
        //  RUNTIME REFS
        // ================================================================

        private KinematicMotor2D motor;
        private PlayerController player;

        // Event log (ring buffer)
        private string[] eventLog = new string[8];
        private int eventLogIndex;

        private GUIStyle boxStyle;
        private GUIStyle labelStyle;
        private GUIStyle headerStyle;

        // ================================================================
        //  LIFECYCLE
        // ================================================================

        private void Start()
        {
            // Auto-find player components
            var playerGO = GameObject.FindGameObjectWithTag("Player");
            if (playerGO != null)
            {
                motor  = playerGO.GetComponent<KinematicMotor2D>();
                player = playerGO.GetComponent<PlayerController>();
            }

            if (motor == null)
            {
                // FindObjectOfType works in Unity 2021+. For 2023.1+,
                // replace with Object.FindFirstObjectByType<T>().
                motor = FindObjectOfType<KinematicMotor2D>();
                if (motor != null)
                    player = motor.GetComponent<PlayerController>();
            }

            // Subscribe to events for logging
            if (motor != null)
            {
                motor.OnLanded     += () => LogEvent("<color=green>LANDED</color>");
                motor.OnLeftGround += () => LogEvent("<color=yellow>LEFT GROUND</color>");
                motor.OnWallContactChanged += dir =>
                {
                    string d = dir == 0 ? "NONE" : (dir < 0 ? "LEFT" : "RIGHT");
                    LogEvent($"<color=magenta>WALL: {d}</color>");
                };
            }

            if (player != null)
            {
                player.OnJump += () => LogEvent("<color=cyan>JUMP</color>");
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                showHUD = !showHUD;
        }

        // ================================================================
        //  GUI
        // ================================================================

        private void OnGUI()
        {
            if (!showHUD || motor == null) return;

            // Init styles once
            if (boxStyle == null) InitStyles();

            MotorState s = motor.State;

            float x = 10f;
            float y = 10f;
            float w = 320f;
            float lineH = fontSize + 4f;

            // Background box
            float totalH = lineH * 14 + 20f;
            GUI.Box(new Rect(x, y, w, totalH), "", boxStyle);

            x += 8f;
            y += 6f;

            // ---- Header ----
            GUI.Label(new Rect(x, y, w, lineH),
                "MOTOR DEBUG  <color=grey>[F1 toggle]</color>", headerStyle);
            y += lineH + 4f;

            // ---- Ground State ----
            string groundColor = s.IsGrounded ? "green" : "red";
            string groundText  = s.IsGrounded ? "GROUNDED" : "AIRBORNE";
            DrawLabel(ref y, x, w, lineH,
                $"State:  <color={groundColor}><b>{groundText}</b></color>");

            // ---- Slope ----
            if (s.IsGrounded)
            {
                string slopeColor = s.IsOnSlope ? "yellow" : "white";
                DrawLabel(ref y, x, w, lineH,
                    $"Slope:  <color={slopeColor}>{s.SlopeAngle:F1} deg</color>  " +
                    $"Normal: ({s.GroundNormal.x:F2}, {s.GroundNormal.y:F2})");
            }
            else
            {
                DrawLabel(ref y, x, w, lineH, "Slope:  <color=grey>N/A</color>");
            }

            // ---- Velocity ----
            DrawLabel(ref y, x, w, lineH,
                $"Vel X: <color=white>{s.Velocity.x:F2}</color>   " +
                $"Vel Y: <color=white>{s.Velocity.y:F2}</color>");

            // ---- Speed ----
            DrawLabel(ref y, x, w, lineH,
                $"Speed: <color=white>{s.Velocity.magnitude:F2}</color> u/s");

            // ---- Wall ----
            string wallText = s.IsOnWall
                ? $"<color=magenta>{(s.WallDirection < 0 ? "LEFT" : "RIGHT")}</color>"
                : "<color=grey>NONE</color>";
            DrawLabel(ref y, x, w, lineH, $"Wall:   {wallText}");

            // ---- Platform ----
            string platText = s.IsOnMovingPlatform
                ? "<color=yellow>ON PLATFORM</color>"
                : "<color=grey>NONE</color>";
            DrawLabel(ref y, x, w, lineH, $"Platform: {platText}");

            // ---- Facing ----
            if (player != null)
            {
                string faceText = player.Facing > 0 ? "RIGHT -->" : "<-- LEFT";
                DrawLabel(ref y, x, w, lineH, $"Facing: <color=white>{faceText}</color>");
            }

            // ---- Event Log ----
            y += 6f;
            DrawLabel(ref y, x, w, lineH, "<color=grey>--- Events ---</color>");
            for (int i = 0; i < eventLog.Length; i++)
            {
                int idx = (eventLogIndex - 1 - i + eventLog.Length) % eventLog.Length;
                if (eventLog[idx] != null)
                    DrawLabel(ref y, x, w, lineH, eventLog[idx]);
            }
        }

        // ================================================================
        //  HELPERS
        // ================================================================

        private void DrawLabel(ref float y, float x, float w, float h, string text)
        {
            GUI.Label(new Rect(x, y, w, h), text, labelStyle);
            y += h;
        }

        private void LogEvent(string msg)
        {
            eventLog[eventLogIndex] = $"{Time.time:F2}s  {msg}";
            eventLogIndex = (eventLogIndex + 1) % eventLog.Length;
        }

        private void InitStyles()
        {
            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = MakeTex(2, 2, new Color(0f, 0f, 0f, 0.8f));

            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize  = fontSize;
            labelStyle.richText  = true;
            labelStyle.normal.textColor = Color.white;

            headerStyle = new GUIStyle(labelStyle);
            headerStyle.fontSize  = fontSize + 2;
            headerStyle.fontStyle = FontStyle.Bold;
        }

        private static Texture2D MakeTex(int w, int h, Color color)
        {
            var tex = new Texture2D(w, h);
            var pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = color;
            tex.SetPixels(pix);
            tex.Apply();
            return tex;
        }
    }
}
