// ============================================================================
//  SpineAnimBridge.cs
//  Decoupled bridge between physics and Spine 2D animation.
//
//  This script:
//    - Subscribes to events from Motor and PlayerController
//    - Reads MotorState each frame for continuous parameters
//    - Drives Spine SkeletonAnimation (or your specific Spine setup)
//
//  STUB IMPLEMENTATION: Spine API calls are commented out.
//  Uncomment and adapt to your Spine skeleton's animation names.
// ============================================================================
using UnityEngine;
using PlatformerKit.Physics;
using PlatformerKit.Player;

// Uncomment when Spine is imported:
// using Spine.Unity;

namespace PlatformerKit.Animation
{
    /// <summary>
    /// Reads physics state from Motor + Controller and drives Spine animations.
    /// Zero physics logic lives here — this is purely a data consumer.
    /// </summary>
    [DefaultExecutionOrder(100)] // runs AFTER physics
    public class SpineAnimBridge : MonoBehaviour
    {
        // ================================================================
        //  REFERENCES (assign in Inspector)
        // ================================================================

        [SerializeField] private KinematicMotor2D motor;
        [SerializeField] private PlayerController player;

        // Uncomment when Spine is imported:
        // [SerializeField] private SkeletonAnimation skeletonAnimation;

        // ================================================================
        //  SPINE ANIMATION NAMES (configure per skeleton)
        // ================================================================

        [Header("Animation Names")]
        [SerializeField] private string idleAnim  = "idle";
        [SerializeField] private string runAnim   = "run";
        [SerializeField] private string jumpAnim  = "jump";
        [SerializeField] private string fallAnim  = "fall";
        [SerializeField] private string landAnim  = "land";

        // ================================================================
        //  STATE
        // ================================================================

        private enum AnimState
        {
            Idle,
            Run,
            Jump,
            Fall,
            Land
        }

        private AnimState currentAnimState;

        // ================================================================
        //  LIFECYCLE
        // ================================================================

        private void OnEnable()
        {
            if (motor == null || player == null)
            {
                Debug.LogWarning($"[SpineAnimBridge] Missing references on {gameObject.name}. " +
                    "Assign Motor and Player in Inspector.");
                enabled = false;
                return;
            }

            // Subscribe to one-shot events
            motor.OnLanded       += HandleLanded;
            motor.OnLeftGround   += HandleLeftGround;
            player.OnJump        += HandleJump;
            player.OnFacingChanged += HandleFacingChanged;
        }

        private void OnDisable()
        {
            if (motor != null)
            {
                motor.OnLanded     -= HandleLanded;
                motor.OnLeftGround -= HandleLeftGround;
            }
            if (player != null)
            {
                player.OnJump          -= HandleJump;
                player.OnFacingChanged -= HandleFacingChanged;
            }
        }

        private void LateUpdate()
        {
            if (motor == null) return;

            // Continuous state evaluation (every frame)
            MotorState state = motor.State;

            AnimState desired = EvaluateDesiredState(state);

            if (desired != currentAnimState)
            {
                currentAnimState = desired;
                PlayAnimation(GetAnimationName(desired));
            }
        }

        // ================================================================
        //  STATE EVALUATION (pure logic, no Spine API)
        // ================================================================

        private AnimState EvaluateDesiredState(MotorState state)
        {
            // Priority order: Land > Jump > Fall > Run > Idle
            // (Land is set by event, cleared after animation finishes)

            if (currentAnimState == AnimState.Land)
                return AnimState.Land; // wait for land anim to finish

            if (!state.IsGrounded)
            {
                return (state.Velocity.y > 0.1f)
                    ? AnimState.Jump
                    : AnimState.Fall;
            }

            // Grounded
            return (Mathf.Abs(state.Velocity.x) > 0.5f)
                ? AnimState.Run
                : AnimState.Idle;
        }

        private string GetAnimationName(AnimState animState)
        {
            return animState switch
            {
                AnimState.Idle  => idleAnim,
                AnimState.Run   => runAnim,
                AnimState.Jump  => jumpAnim,
                AnimState.Fall  => fallAnim,
                AnimState.Land  => landAnim,
                _ => idleAnim
            };
        }

        // ================================================================
        //  ANIMATION PLAYBACK (Spine-specific, uncomment & adapt)
        // ================================================================

        private void PlayAnimation(string animName)
        {
            // Uncomment when Spine is imported:
            // skeletonAnimation.AnimationState.SetAnimation(0, animName, loop: animName != landAnim);

            Debug.Log($"[SpineAnimBridge] Play: {animName}");
        }

        // ================================================================
        //  EVENT HANDLERS
        // ================================================================

        private void HandleLanded()
        {
            currentAnimState = AnimState.Land;
            PlayAnimation(landAnim);

            // After land animation completes, transition to Idle/Run.
            // With Spine, use AnimationState.Complete callback:
            //
            // skeletonAnimation.AnimationState.Complete += entry =>
            // {
            //     if (entry.Animation.Name == landAnim)
            //         currentAnimState = AnimState.Idle; // will be re-evaluated next frame
            // };

            // Simple fallback: clear after a fixed duration
            Invoke(nameof(ClearLandState), 0.15f);
        }

        private void ClearLandState()
        {
            if (currentAnimState == AnimState.Land)
                currentAnimState = AnimState.Idle;
        }

        private void HandleLeftGround()
        {
            // Triggered by Motor. The next LateUpdate will pick up
            // Jump or Fall state from velocity direction.
        }

        private void HandleJump()
        {
            currentAnimState = AnimState.Jump;
            PlayAnimation(jumpAnim);
        }

        private void HandleFacingChanged(int direction)
        {
            // Flip the Spine skeleton horizontally.
            // Spine uses skeleton.ScaleX for flipping.

            // Uncomment when Spine is imported:
            // skeletonAnimation.Skeleton.ScaleX = direction;

            // Generic SpriteRenderer fallback:
            Vector3 scale = transform.localScale;
            scale.x = Mathf.Abs(scale.x) * direction;
            transform.localScale = scale;
        }
    }
}
