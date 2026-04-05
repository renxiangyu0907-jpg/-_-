// ============================================================================
//  MotorConfig.cs
//  ScriptableObject — all tuning constants for KinematicMotor2D.
//  Create via: Assets > Create > PlatformerKit > Motor Config
//  Editable at runtime in Inspector; values persist after exiting Play Mode.
// ============================================================================
using UnityEngine;

namespace PlatformerKit.Physics
{
    [CreateAssetMenu(
        fileName  = "NewMotorConfig",
        menuName  = "PlatformerKit/Motor Config",
        order     = 0)]
    public class MotorConfig : ScriptableObject
    {
        [Header("Shape Cast")]
        [Tooltip("Thin shell kept between capsule surface and geometry. " +
                 "Prevents float-precision overlap deadlocks.")]
        [Range(0.005f, 0.05f)]
        public float skinWidth = 0.015f;

        [Header("Ground Detection")]
        [Tooltip("Extra distance below the capsule to probe for ground.")]
        [Range(0.02f, 0.2f)]
        public float groundProbeDistance = 0.08f;

        [Tooltip("Capsule width shrink ratio for ground probe. " +
                 "0.9 = 90% of original width. Prevents false grounding " +
                 "when toes barely overhang a ledge.")]
        [Range(0.7f, 1f)]
        public float groundProbeWidthRatio = 0.9f;

        [Tooltip("Maximum slope angle (degrees) the character can walk on. " +
                 "Slopes steeper than this are treated as walls.")]
        [Range(20f, 70f)]
        public float maxSlopeAngle = 55f;

        [Header("Ground Snap (Anti-Launch on Downhill)")]
        [Tooltip("When transitioning from grounded to airborne with downward " +
                 "intent, cast down this far to re-attach to ground.")]
        [Range(0.05f, 0.6f)]
        public float snapDownDistance = 0.25f;

        [Header("Step Offset (Ghost Collision Fix)")]
        [Tooltip("Max obstacle height (world units) the character auto-climbs. " +
                 "~0.12 ≈ 3 pixels at 48 PPU.")]
        [Range(0f, 0.3f)]
        public float stepOffset = 0.12f;

        [Header("Slide Iteration")]
        [Tooltip("Max bounce/slide iterations per axis per frame. " +
                 "3 handles concave corners; more is wasteful.")]
        [Range(1, 5)]
        public int maxCastBounces = 3;

        [Header("Wall Detection (Reserved for Wall-Jump)")]
        [Tooltip("If |hit.normal.x| exceeds this threshold during horizontal " +
                 "cast, the surface is classified as a wall (not a slope).")]
        [Range(0.8f, 1f)]
        public float wallNormalThreshold = 0.9f;
    }
}
