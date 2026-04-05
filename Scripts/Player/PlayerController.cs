// ============================================================================
//  PlayerController.cs
//  The "brain" — reads input, computes velocity (gravity, jump, accel/decel),
//  then feeds the result to KinematicMotor2D each FixedUpdate.
//
//  This script does NOT know about:
//    - Collision detection, shape casts, slope normals
//    - Animation, Spine, sprites
//
//  It DOES know about:
//    - Player input (horizontal axis, jump button)
//    - Physics "feel" (gravity, jump arc, coyote time, jump buffer)
//    - Motor's read-only state (IsGrounded, IsOnWall, etc.)
// ============================================================================
using System;
using UnityEngine;
using PlatformerKit.Physics;

namespace PlatformerKit.Player
{
    [RequireComponent(typeof(KinematicMotor2D))]
    public class PlayerController : MonoBehaviour
    {
        // ================================================================
        //  SERIALIZED
        // ================================================================

        [SerializeField] private PlayerConfig config;

        // ================================================================
        //  EVENTS (Spine anim bridge / VFX / audio subscribe to these)
        // ================================================================

        /// <summary>Fires on the frame a jump begins.</summary>
        public event Action OnJump;

        /// <summary>Fires when landing on ground from air.</summary>
        public event Action OnLand;

        /// <summary>Fires every frame with the current facing direction (-1 or +1).</summary>
        public event Action<int> OnFacingChanged;

        // ================================================================
        //  REFERENCES
        // ================================================================

        private KinematicMotor2D motor;

        // ================================================================
        //  INTERNAL STATE
        // ================================================================

        private Vector2 velocity;

        // Input (read in Update, consumed in FixedUpdate)
        private float inputX;
        private bool  jumpPressed;
        private bool  jumpHeld;

        // Jump state machine
        private bool  isJumping;        // true from liftoff until apex or release
        private float coyoteTimer;      // counts down from coyoteTime
        private float jumpBufferTimer;  // counts down from jumpBufferTime

        // Facing
        private int  facing = 1;       // +1 right, -1 left
        private int  prevFacing = 1;

        // Landing detection
        private bool wasGrounded;

        // ================================================================
        //  UNITY LIFECYCLE
        // ================================================================

        private void Awake()
        {
            motor = GetComponent<KinematicMotor2D>();

            if (config == null)
            {
                Debug.LogError($"[PlayerController] PlayerConfig is not assigned on {gameObject.name}! " +
                    "Create one via Assets > Create > PlatformerKit > Player Config, then drag it into the Inspector.");
                enabled = false;
                return;
            }
        }

        /// <summary>
        /// Input is read in Update() for maximum responsiveness.
        /// Frame-perfect input (jumpPressed) uses GetButtonDown which
        /// must be called in Update, not FixedUpdate.
        /// </summary>
        private void Update()
        {
            // ---- Read raw input ----
            inputX = Input.GetAxisRaw("Horizontal");

            if (Input.GetButtonDown("Jump"))
                jumpPressed = true; // consumed in FixedUpdate

            jumpHeld = Input.GetButton("Jump");

            // ---- Tick timers (real-time, not physics-time) ----
            if (jumpBufferTimer > 0f) jumpBufferTimer -= Time.deltaTime;
            if (coyoteTimer     > 0f) coyoteTimer     -= Time.deltaTime;

            // ---- Jump buffer: remember the press for a short window ----
            if (jumpPressed)
                jumpBufferTimer = config.jumpBufferTime;
        }

        /// <summary>
        /// All physics calculations happen in FixedUpdate to stay in
        /// lockstep with Motor's rb.MovePosition.
        /// </summary>
        private void FixedUpdate()
        {
            float dt    = Time.fixedDeltaTime;
            MotorState state = motor.State;

            // ================ Coyote Time Management ================
            if (state.IsGrounded)
            {
                coyoteTimer = config.coyoteTime;
            }
            // (coyoteTimer is ticked down in Update)

            // ================ Landing Detection ================
            // Uses state from PREVIOUS Move() — this is correct because
            // wasGrounded is set at the end of last FixedUpdate.
            if (!wasGrounded && state.IsGrounded)
            {
                velocity.y = 0f;
                isJumping  = false;
                OnLand?.Invoke();
            }

            // ================ Horizontal ================
            float targetVx = inputX * config.maxRunSpeed;

            // Choose accel or decel rate
            bool accelerating = Mathf.Abs(targetVx) >= Mathf.Abs(velocity.x)
                             || Mathf.Sign(targetVx) != Mathf.Sign(velocity.x);
            float baseRate = accelerating ? config.GroundAccelRate : config.GroundDecelRate;
            float rate     = state.IsGrounded ? baseRate : baseRate * config.airControlFactor;

            velocity.x = Mathf.MoveTowards(velocity.x, targetVx, rate * dt);

            // ================ Facing ================
            if (Mathf.Abs(inputX) > 0.01f)
            {
                facing = (inputX > 0f) ? 1 : -1;
                if (facing != prevFacing)
                {
                    OnFacingChanged?.Invoke(facing);
                    prevFacing = facing;
                }
            }

            // ================ Gravity ================
            // Use lighter gravity while rising during a jump (held button),
            // heavier gravity when falling or when jump is released.
            float gravity;
            if (velocity.y > 0f && isJumping && jumpHeld)
                gravity = config.JumpGravity;
            else
                gravity = config.FallGravity;

            // Only apply gravity when airborne
            if (!state.IsGrounded || velocity.y > 0f)
            {
                velocity.y -= gravity * dt;
                velocity.y  = Mathf.Max(velocity.y, -config.maxFallSpeed);
            }
            else
            {
                // Grounded and not jumping upward: clamp vertical velocity
                // A small negative value helps ground probe stick.
                velocity.y = -0.5f;
            }

            // ================ Jump ================
            bool canJump = coyoteTimer > 0f;
            bool wantJump = jumpBufferTimer > 0f;

            if (wantJump && canJump)
            {
                velocity.y      = config.JumpVelocity;
                isJumping       = true;
                coyoteTimer     = 0f;     // consume coyote
                jumpBufferTimer = 0f;     // consume buffer
                OnJump?.Invoke();
            }

            // ---- Variable jump height: cut velocity on early release ----
            if (isJumping && !jumpHeld && velocity.y > 0f)
            {
                velocity.y *= config.jumpCutMultiplier;
                isJumping   = false;
            }

            // ================ Feed Motor ================
            motor.Move(velocity * dt);

            // ================ Ceiling bonk (post-move) ================
            // Read the UPDATED state AFTER Move() to detect ceiling collision.
            // If we intended to go up but motor couldn't move us up, we hit ceiling.
            MotorState postState = motor.State;
            if (velocity.y > 0f && postState.Velocity.y <= 0.01f && !postState.IsGrounded)
            {
                velocity.y = 0f;
                isJumping  = false;
            }

            // ================ Post-move bookkeeping ================
            wasGrounded = postState.IsGrounded;
            jumpPressed = false; // consumed
        }

        // ================================================================
        //  PUBLIC QUERIES (for external systems, e.g. dash ability)
        // ================================================================

        /// <summary>Current facing direction: +1 right, -1 left.</summary>
        public int Facing => facing;

        /// <summary>The velocity the player intends, before motor collision.</summary>
        public Vector2 Velocity => velocity;

        /// <summary>Externally set velocity (e.g., knockback, dash, spring pad).</summary>
        public void SetVelocity(Vector2 newVelocity)
        {
            velocity = newVelocity;

            // If launched upward, treat as a jump for gravity purposes
            if (velocity.y > 0f)
                isJumping = true;
        }

        /// <summary>Add an impulse to current velocity (e.g., bouncy mushroom).</summary>
        public void AddImpulse(Vector2 impulse)
        {
            SetVelocity(velocity + impulse);
        }
    }
}
