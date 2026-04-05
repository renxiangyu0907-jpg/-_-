// ============================================================================
//  MotorState.cs
//  Immutable snapshot of the motor's output each frame.
//  Upper layers (PlayerController, SpineAnimBridge) read this; nobody writes.
// ============================================================================
using UnityEngine;

namespace PlatformerKit.Physics
{
    public readonly struct MotorState
    {
        // ---- Ground ----
        public readonly bool    IsGrounded;
        public readonly bool    WasGroundedLastFrame;
        public readonly bool    IsOnSlope;          // slopeAngle > ~1 deg
        public readonly float   SlopeAngle;         // 0 on flat
        public readonly Vector2 GroundNormal;        // (0,1) on flat

        // ---- Wall (reserved for future wall-jump) ----
        public readonly bool    IsOnWall;
        public readonly int     WallDirection;       // -1 left, +1 right, 0 none

        // ---- Platform ----
        public readonly bool    IsOnMovingPlatform;

        // ---- Velocity ----
        /// <summary>
        /// The actual displacement this frame divided by fixedDeltaTime.
        /// Useful for animation blend trees and effect triggers.
        /// </summary>
        public readonly Vector2 Velocity;

        public MotorState(
            bool    isGrounded,
            bool    wasGroundedLastFrame,
            bool    isOnSlope,
            float   slopeAngle,
            Vector2 groundNormal,
            bool    isOnWall,
            int     wallDirection,
            bool    isOnMovingPlatform,
            Vector2 velocity)
        {
            IsGrounded            = isGrounded;
            WasGroundedLastFrame  = wasGroundedLastFrame;
            IsOnSlope             = isOnSlope;
            SlopeAngle            = slopeAngle;
            GroundNormal          = groundNormal;
            IsOnWall              = isOnWall;
            WallDirection         = wallDirection;
            IsOnMovingPlatform    = isOnMovingPlatform;
            Velocity              = velocity;
        }
    }
}
