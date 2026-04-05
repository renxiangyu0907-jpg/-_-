// ============================================================================
//  PlayerConfig.cs
//  ScriptableObject — all player-specific tuning constants.
//  Create via: Assets > Create > PlatformerKit > Player Config
//  Separate from MotorConfig because Motor is reusable for NPCs/enemies.
// ============================================================================
using UnityEngine;

namespace PlatformerKit.Player
{
    [CreateAssetMenu(
        fileName = "NewPlayerConfig",
        menuName = "PlatformerKit/Player Config",
        order    = 1)]
    public class PlayerConfig : ScriptableObject
    {
        // ================================================================
        //  HORIZONTAL MOVEMENT
        // ================================================================

        [Header("Horizontal Movement")]
        [Tooltip("Maximum horizontal speed (world units/second).")]
        public float maxRunSpeed = 8f;

        [Tooltip("Time in seconds to accelerate from 0 to maxRunSpeed on ground.")]
        [Range(0.01f, 0.5f)]
        public float groundAccelTime = 0.08f;

        [Tooltip("Time in seconds to decelerate from maxRunSpeed to 0 on ground.")]
        [Range(0.01f, 0.5f)]
        public float groundDecelTime = 0.05f;

        [Tooltip("Multiplier for aerial acceleration/deceleration (vs ground). " +
                 "0.65 = 65% of ground responsiveness while airborne.")]
        [Range(0.1f, 1f)]
        public float airControlFactor = 0.65f;

        // ================================================================
        //  JUMP
        // ================================================================

        [Header("Jump — Defining Arc by Height & Time")]
        [Tooltip("Desired maximum jump height in world units.")]
        public float jumpHeight = 3.2f;

        [Tooltip("Time in seconds from jump start to reaching apex. " +
                 "Together with jumpHeight, this fully determines gravity & initial velocity.")]
        public float timeToApex = 0.4f;

        [Tooltip("Multiplier applied to gravity during the falling phase. " +
                 "1.5~2.0 gives Celeste-style snappy landings.")]
        [Range(1f, 4f)]
        public float fallGravityMultiplier = 1.6f;

        [Tooltip("Maximum falling speed (positive value, applied as downward cap).")]
        public float maxFallSpeed = 18f;

        [Tooltip("When the player releases jump early, remaining upward velocity " +
                 "is multiplied by this value (0.5 = cut in half).")]
        [Range(0f, 1f)]
        public float jumpCutMultiplier = 0.5f;

        // ================================================================
        //  ASSIST TIMERS
        // ================================================================

        [Header("Assist Timers")]
        [Tooltip("Seconds after leaving ground where jump is still allowed (coyote time).")]
        [Range(0f, 0.2f)]
        public float coyoteTime = 0.08f;

        [Tooltip("Seconds before landing where jump input is remembered (jump buffer).")]
        [Range(0f, 0.2f)]
        public float jumpBufferTime = 0.10f;

        // ================================================================
        //  DERIVED CONSTANTS (computed at runtime)
        // ================================================================

        /// <summary>Gravity during rising phase: 2h / t^2</summary>
        public float JumpGravity   => 2f * jumpHeight / (timeToApex * timeToApex);

        /// <summary>Gravity during falling phase: jumpGravity * multiplier</summary>
        public float FallGravity   => JumpGravity * fallGravityMultiplier;

        /// <summary>Initial upward velocity for jump: 2h / t</summary>
        public float JumpVelocity  => 2f * jumpHeight / timeToApex;

        /// <summary>Ground acceleration rate: maxSpeed / accelTime</summary>
        public float GroundAccelRate =>
            (groundAccelTime > 0f) ? maxRunSpeed / groundAccelTime : float.MaxValue;

        /// <summary>Ground deceleration rate: maxSpeed / decelTime</summary>
        public float GroundDecelRate =>
            (groundDecelTime > 0f) ? maxRunSpeed / groundDecelTime : float.MaxValue;
    }
}
