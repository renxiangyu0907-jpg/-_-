// ============================================================================
//  SimpleMovingPlatform.cs
//  Ping-pong platform that implements IMovingPlatform.
//
//  CRITICAL: Script Execution Order must be set to -100 (before player)
//  so that FrameDisplacement is calculated BEFORE the motor reads it.
//
//  Unity Editor: Edit > Project Settings > Script Execution Order
//    SimpleMovingPlatform  →  -100
//    PlayerController      →    0   (default)
// ============================================================================
using UnityEngine;
using PlatformerKit.Physics;

namespace PlatformerKit.Platform
{
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Rigidbody2D))]
    public class SimpleMovingPlatform : MonoBehaviour, IMovingPlatform
    {
        // ================================================================
        //  CONFIGURATION
        // ================================================================

        [Header("Waypoints (Local Space)")]
        [Tooltip("Offsets from starting position. First element is typically (0,0).")]
        [SerializeField] private Vector2[] waypoints = new Vector2[]
        {
            Vector2.zero,
            new Vector2(5f, 0f)
        };

        [Header("Movement")]
        [Tooltip("Travel speed in world units per second.")]
        [SerializeField] private float speed = 3f;

        [Tooltip("Seconds to pause at each waypoint.")]
        [SerializeField] private float waitTime = 0.5f;

        // ================================================================
        //  RUNTIME STATE
        // ================================================================

        private Rigidbody2D rb;
        private Vector2     startPosition;
        private Vector2     previousPosition;
        private Vector2     frameDisplacement;

        private int   currentWaypointIndex;
        private float waitTimer;

        // ================================================================
        //  IMovingPlatform
        // ================================================================

        public Vector2 GetFrameDisplacement() => frameDisplacement;

        // ================================================================
        //  LIFECYCLE
        // ================================================================

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();

            // Platform RB must be Kinematic
            rb.bodyType = RigidbodyType2D.Kinematic;

            startPosition    = rb.position;
            previousPosition = rb.position;
        }

        private void FixedUpdate()
        {
            // ---- Calculate displacement FIRST (before moving) ----
            // This ensures the motor sees the displacement from the
            // PREVIOUS frame's move, which is the standard convention.
            frameDisplacement = rb.position - previousPosition;
            previousPosition  = rb.position;

            // ---- Wait at waypoint ----
            if (waitTimer > 0f)
            {
                waitTimer -= Time.fixedDeltaTime;
                return;
            }

            // ---- Move toward current waypoint ----
            Vector2 target = startPosition + waypoints[currentWaypointIndex];
            Vector2 newPos = Vector2.MoveTowards(
                rb.position,
                target,
                speed * Time.fixedDeltaTime
            );

            rb.MovePosition(newPos);

            // ---- Arrived at waypoint? ----
            if (Vector2.Distance(newPos, target) < 0.01f)
            {
                currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
                waitTimer = waitTime;
            }
        }

        // ================================================================
        //  EDITOR VISUALIZATION
        // ================================================================

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector2 origin = Application.isPlaying ? startPosition : (Vector2)transform.position;

            Gizmos.color = Color.yellow;
            for (int i = 0; i < waypoints.Length; i++)
            {
                Vector2 wp = origin + waypoints[i];
                Gizmos.DrawWireSphere(wp, 0.15f);

                // Draw line to next waypoint
                Vector2 next = origin + waypoints[(i + 1) % waypoints.Length];
                Gizmos.DrawLine(wp, next);
            }
        }
#endif
    }
}
