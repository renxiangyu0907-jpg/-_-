// ============================================================================
//  KinematicMotor2D.cs
//  Pure kinematic character motor using CapsuleCast shape-sweeping.
//
//  Responsibilities (and ONLY these):
//    - Shape cast (capsule sweep) for collision detection
//    - Iterative slide along surfaces (CastAndSlide)
//    - Slope tangent projection (anti-launch on downhill)
//    - Step offset / ghost collision tolerance
//    - Ground snap (anti-launch safety net)
//    - Moving platform displacement follow
//    - Wall detection (data only, no wall-jump logic)
//
//  This script does NOT know about:
//    - Player input, gravity formula, jump velocity, coyote time
//    - Animation, Spine, sprites
//
//  Usage:
//    Called once per FixedUpdate by a "brain" script (PlayerController / AIController).
//    brain computes desiredVelocity, then: motor.Move(desiredVelocity * dt);
// ============================================================================
using System;
using UnityEngine;

namespace PlatformerKit.Physics
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CapsuleCollider2D))]
    public class KinematicMotor2D : MonoBehaviour
    {
        // ================================================================
        //  SERIALIZED FIELDS
        // ================================================================

        [SerializeField] private MotorConfig config;

        [Tooltip("Layer mask for solid geometry. Assign in Inspector.")]
        [SerializeField] private LayerMask collisionMask;

        // ================================================================
        //  PUBLIC READ-ONLY OUTPUT
        // ================================================================

        /// <summary>Immutable snapshot updated at the end of each Move().</summary>
        public MotorState State { get; private set; }

        // ================================================================
        //  EVENTS (animation / audio / VFX subscribe to these)
        // ================================================================

        /// <summary>Fires on the frame the character touches ground.</summary>
        public event Action OnLanded;

        /// <summary>Fires on the frame the character leaves the ground.</summary>
        public event Action OnLeftGround;

        /// <summary>Fires when wall-touch state changes. Arg = wallDirection (-1/+1/0).</summary>
        public event Action<int> OnWallContactChanged;

        // ================================================================
        //  PRIVATE STATE
        // ================================================================

        private Rigidbody2D       rb;
        private CapsuleCollider2D capsule;
        private ContactFilter2D   contactFilter;

        // Ground probe results (written by GroundProbe, read by other phases)
        private bool    isGrounded;
        private bool    wasGrounded;
        private float   slopeAngle;
        private Vector2 groundNormal;
        private IMovingPlatform currentPlatform;

        // Wall detection (written by horizontal CastAndSlide)
        private bool isOnWall;
        private int  wallDirection;  // -1 left, +1 right, 0 none
        private int  prevWallDirection;

        // Reusable buffers (zero-alloc per frame)
        private readonly RaycastHit2D[] hitBuffer = new RaycastHit2D[16];

        // ================================================================
        //  UNITY LIFECYCLE
        // ================================================================

        private void Awake()
        {
            rb      = GetComponent<Rigidbody2D>();
            capsule = GetComponent<CapsuleCollider2D>();

            // ---- Config safety ----
            if (config == null)
            {
                Debug.LogError($"[KinematicMotor2D] MotorConfig is not assigned on {gameObject.name}! " +
                    "Create one via Assets > Create > PlatformerKit > Motor Config, then drag it into the Inspector.");
                enabled = false;
                return;
            }

            // ---- Rigidbody safety ----
            rb.bodyType    = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true; // needed for Cast queries
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            // ---- Contact filter (ignore triggers, use layer mask) ----
            contactFilter = new ContactFilter2D();
            contactFilter.useTriggers  = false;
            contactFilter.useLayerMask = true;
            contactFilter.layerMask    = collisionMask;

            groundNormal = Vector2.up;
        }

        // ================================================================
        //  PUBLIC API — the ONLY entry point called by the brain
        // ================================================================

        /// <summary>
        /// Move the character by <paramref name="desiredDelta"/> (world units, 
        /// already multiplied by deltaTime by the caller).
        /// </summary>
        public void Move(Vector2 desiredDelta)
        {
            float dt = Time.fixedDeltaTime;
            Vector2 startPos = rb.position;

            // Save previous frame state for transition detection
            wasGrounded       = isGrounded;
            prevWallDirection = wallDirection;

            // Reset per-frame wall state (will be set by horizontal pass)
            isOnWall      = false;
            wallDirection = 0;

            // ============ Phase 1: Ground Probe ============
            GroundProbe();

            // ============ Phase 2: Horizontal Move ============
            //   Project horizontal intent onto slope tangent,
            //   then iteratively cast-and-slide.
            Vector2 horizontalDelta = ProjectOntoSlope(desiredDelta);
            Vector2 hMoved = CastAndSlide(horizontalDelta, true);

            // Sync rb.position so subsequent vertical cast starts from
            // the post-horizontal position. Without this, vertical cast
            // would originate from the OLD position and miss geometry.
            rb.position = startPos + hMoved;

            // ============ Phase 3: Vertical Move ============
            Vector2 verticalDelta = new Vector2(0f, desiredDelta.y);
            // On ground, vertical component is absorbed by slope projection.
            // Only apply raw vertical when airborne or jumping upward.
            if (isGrounded && desiredDelta.y <= 0f)
                verticalDelta = Vector2.zero;
            Vector2 vMoved = CastAndSlide(verticalDelta, false);

            // Sync again after vertical pass for ground snap probe.
            rb.position = startPos + hMoved + vMoved;

            // ============ Phase 3.5: Ground Snap ============
            // If we WERE on ground last frame, are NOT grounded now,
            // and we weren't trying to jump (desiredDelta.y <= 0),
            // attempt to snap back down to prevent downhill launch.
            Vector2 snapDelta = Vector2.zero;
            if (wasGrounded && !isGrounded && desiredDelta.y <= 0f)
            {
                snapDelta = GroundSnap();
            }

            // ============ Phase 4: Moving Platform Follow ============
            Vector2 platformDelta = Vector2.zero;
            if (currentPlatform != null && isGrounded)
            {
                platformDelta = currentPlatform.GetFrameDisplacement();
            }

            // ============ Commit Final Position ============
            Vector2 totalDelta = hMoved + vMoved + snapDelta + platformDelta;
            Vector2 finalPos   = startPos + totalDelta;
            rb.MovePosition(finalPos);

            // ============ Build & Publish State ============
            Vector2 actualVelocity = (dt > 0f) ? totalDelta / dt : Vector2.zero;

            State = new MotorState(
                isGrounded:           isGrounded,
                wasGroundedLastFrame: wasGrounded,
                isOnSlope:            slopeAngle > 1f,
                slopeAngle:           slopeAngle,
                groundNormal:         groundNormal,
                isOnWall:             isOnWall,
                wallDirection:        wallDirection,
                isOnMovingPlatform:   currentPlatform != null && isGrounded,
                velocity:             actualVelocity
            );

            FireEvents();
        }

        // ================================================================
        //  PHASE 1 — GROUND PROBE
        //  Capsule cast downward with narrowed width.
        // ================================================================

        private void GroundProbe()
        {
            float castDistance = config.skinWidth + config.groundProbeDistance;

            // --- Narrowed capsule size for ground probe ---
            // Prevents false grounding when toes overhang a ledge.
            Vector2 originalSize = capsule.size;
            Vector2 probeSize    = new Vector2(
                originalSize.x * config.groundProbeWidthRatio,
                originalSize.y
            );

            int hitCount = Physics2D.CapsuleCast(
                origin:           rb.position + capsule.offset,
                size:             probeSize,
                capsuleDirection: capsule.direction,
                angle:            0f,
                direction:        Vector2.down,
                contactFilter:    contactFilter,
                results:          hitBuffer,
                distance:         castDistance
            );

            if (hitCount > 0)
            {
                RaycastHit2D closestHit = GetClosestHit(hitBuffer, hitCount);
                groundNormal = closestHit.normal;
                slopeAngle   = Vector2.Angle(groundNormal, Vector2.up);

                isGrounded = (slopeAngle <= config.maxSlopeAngle)
                          && (closestHit.distance <= castDistance);

                // Moving platform detection
                if (isGrounded)
                {
                    currentPlatform = closestHit.collider
                        .GetComponentInParent<IMovingPlatform>();
                }
                else
                {
                    currentPlatform = null;
                }
            }
            else
            {
                isGrounded      = false;
                slopeAngle      = 0f;
                groundNormal    = Vector2.up;
                currentPlatform = null;
            }
        }

        // ================================================================
        //  SLOPE PROJECTION
        //  Projects horizontal intent along the ground tangent.
        //  Uses the CORRECT general formula:
        //    v_slope = v - (v . n) * n
        //  which naturally handles all slope angles and directions.
        // ================================================================

        private Vector2 ProjectOntoSlope(Vector2 desiredDelta)
        {
            // Airborne: return only the horizontal component
            if (!isGrounded)
                return new Vector2(desiredDelta.x, 0f);

            // Flat ground (optimization: skip projection)
            if (slopeAngle < 1f)
                return new Vector2(desiredDelta.x, 0f);

            // -- General slope projection --
            // We want the horizontal speed preserved along the slope surface.
            // Construct the "intent" as a horizontal vector, then project
            // onto the plane defined by groundNormal.
            //
            //   v_projected = v - (v . n) * n
            //
            // This removes the component of v that points INTO the surface,
            // leaving only the component tangent to it.
            Vector2 horizontal = new Vector2(desiredDelta.x, 0f);
            Vector2 projected  = horizontal - Vector2.Dot(horizontal, groundNormal) * groundNormal;

            // Normalize then re-apply original speed magnitude along the surface.
            // This prevents characters from slowing down on steep slopes.
            if (projected.sqrMagnitude > 1e-6f)
            {
                projected = projected.normalized * Mathf.Abs(desiredDelta.x);
            }

            return projected;
        }

        // ================================================================
        //  CAST AND SLIDE — iterative sweep + depenetration
        //  Used for both horizontal and vertical passes.
        // ================================================================

        private Vector2 CastAndSlide(Vector2 delta, bool isHorizontalPass)
        {
            Vector2 totalMoved     = Vector2.zero;
            Vector2 remainingDelta = delta;

            for (int bounce = 0; bounce < config.maxCastBounces; bounce++)
            {
                float moveMag = remainingDelta.magnitude;
                if (moveMag < 1e-5f)
                    break;

                Vector2 moveDir = remainingDelta / moveMag; // normalized

                // Cast from CURRENT rb.position (which is kept in sync
                // via rb.position = ... between H/V passes).
                float castDist = moveMag + config.skinWidth;
                int hitCount = CapsuleCastFull(moveDir, castDist, hitBuffer);

                // ---- No hit: free to move the entire remaining distance ----
                if (hitCount == 0)
                {
                    totalMoved += remainingDelta;
                    break;
                }

                RaycastHit2D hit = GetClosestHit(hitBuffer, hitCount);

                // ---- Move up to the hit (minus skin width) ----
                float safeDistance = Mathf.Max(hit.distance - config.skinWidth, 0f);
                Vector2 safeDelta = moveDir * safeDistance;
                totalMoved += safeDelta;

                // ---- Wall detection (horizontal pass only) ----
                if (isHorizontalPass)
                {
                    float absNormalX = Mathf.Abs(hit.normal.x);
                    if (absNormalX >= config.wallNormalThreshold)
                    {
                        isOnWall      = true;
                        wallDirection = (hit.normal.x > 0f) ? -1 : 1;
                        // -1 = wall is to our LEFT, +1 = wall is to our RIGHT
                    }
                }

                // ---- Step Offset attempt (horizontal pass only) ----
                if (isHorizontalPass && config.stepOffset > 0f)
                {
                    if (TryStepUp(hit, moveDir, moveMag - safeDistance, out Vector2 stepDelta))
                    {
                        totalMoved += stepDelta;
                        break; // successfully climbed; stop sliding
                    }
                }

                // ---- Slide along surface ----
                // Remove the component of remaining movement that goes into the wall.
                float penetrating = Vector2.Dot(remainingDelta - safeDelta, hit.normal);
                remainingDelta = (remainingDelta - safeDelta)
                               - Mathf.Min(penetrating, 0f) * hit.normal;
                // Mathf.Min(...,0) ensures we only remove the INTO-surface component,
                // not the away-from-surface component (which would "pull" us to walls).
            }

            return totalMoved;
        }

        // ================================================================
        //  STEP OFFSET — three-phase: up → forward → down
        //  Allows auto-climbing bumps smaller than stepOffset height.
        // ================================================================

        private bool TryStepUp(
            RaycastHit2D wallHit,
            Vector2      moveDir,
            float        remainingDist,
            out Vector2  stepDelta)
        {
            stepDelta = Vector2.zero;

            // Only attempt for near-vertical surfaces (actual walls/steps)
            float absNx = Mathf.Abs(wallHit.normal.x);
            if (absNx < 0.7f) return false; // it's more of a slope, not a step

            float stepH = config.stepOffset;

            // ---- Step A: Cast UP ----
            int upHits = CapsuleCastFull(Vector2.up, stepH + config.skinWidth, hitBuffer);
            float actualUp = stepH;
            if (upHits > 0)
            {
                RaycastHit2D upHit = GetClosestHit(hitBuffer, upHits);
                actualUp = Mathf.Max(upHit.distance - config.skinWidth, 0f);
            }
            if (actualUp < 0.01f) return false; // ceiling too close, can't step

            // We need to temporarily offset the capsule for the next casts.
            // Instead of actually moving, we use Physics2D.CapsuleCast with
            // an offset origin.
            Vector2 elevatedOrigin = rb.position + capsule.offset + Vector2.up * actualUp;

            // ---- Step B: Cast FORWARD from elevated position ----
            float forwardDist = remainingDist + config.skinWidth;
            int fwdHits = Physics2D.CapsuleCast(
                elevatedOrigin,
                capsule.size,
                capsule.direction,
                0f,
                moveDir,
                contactFilter,
                hitBuffer,
                forwardDist
            );

            float actualFwd = remainingDist;
            if (fwdHits > 0)
            {
                RaycastHit2D fwdHit = GetClosestHit(hitBuffer, fwdHits);
                actualFwd = Mathf.Max(fwdHit.distance - config.skinWidth, 0f);
            }
            if (actualFwd < 0.01f) return false; // can't move forward even elevated

            Vector2 landingOrigin = elevatedOrigin + moveDir * actualFwd;

            // ---- Step C: Cast DOWN from landing position to find ground ----
            float downDist = actualUp + config.groundProbeDistance + config.skinWidth;
            int downHits = Physics2D.CapsuleCast(
                landingOrigin,
                capsule.size,
                capsule.direction,
                0f,
                Vector2.down,
                contactFilter,
                hitBuffer,
                downDist
            );

            if (downHits == 0) return false; // no ground found: it's a ledge, not a step

            RaycastHit2D downHit = GetClosestHit(hitBuffer, downHits);

            // Verify the landing surface is walkable
            float landAngle = Vector2.Angle(downHit.normal, Vector2.up);
            if (landAngle > config.maxSlopeAngle) return false;

            float actualDown = Mathf.Max(downHit.distance - config.skinWidth, 0f);

            // ---- Compose step delta ----
            stepDelta = (Vector2.up   * actualUp)
                      + (moveDir      * actualFwd)
                      + (Vector2.down * actualDown);

            return true;
        }

        // ================================================================
        //  GROUND SNAP — anti-launch safety net for downhill transitions
        // ================================================================

        private Vector2 GroundSnap()
        {
            float castDist = config.snapDownDistance + config.skinWidth;
            int hitCount = CapsuleCastFull(Vector2.down, castDist, hitBuffer);

            if (hitCount > 0)
            {
                RaycastHit2D hit = GetClosestHit(hitBuffer, hitCount);
                float snapAngle = Vector2.Angle(hit.normal, Vector2.up);

                if (snapAngle <= config.maxSlopeAngle)
                {
                    // Re-establish ground contact
                    isGrounded   = true;
                    groundNormal = hit.normal;
                    slopeAngle   = snapAngle;

                    // Check if we snapped onto a moving platform
                    currentPlatform = hit.collider
                        .GetComponentInParent<IMovingPlatform>();

                    float safeDist = Mathf.Max(hit.distance - config.skinWidth, 0f);
                    return Vector2.down * safeDist;
                }
            }

            return Vector2.zero; // genuinely left ground (ledge / jump)
        }

        // ================================================================
        //  EVENT FIRING
        // ================================================================

        private void FireEvents()
        {
            // Ground transition events
            if (isGrounded && !wasGrounded)
                OnLanded?.Invoke();
            else if (!isGrounded && wasGrounded)
                OnLeftGround?.Invoke();

            // Wall contact change event
            if (wallDirection != prevWallDirection)
                OnWallContactChanged?.Invoke(wallDirection);
        }

        // ================================================================
        //  HELPERS — capsule cast wrappers (zero-alloc)
        // ================================================================

        /// <summary>
        /// Full capsule cast from current rb.position + collider offset.
        /// Uses the actual collider size (not narrowed).
        /// </summary>
        private int CapsuleCastFull(Vector2 direction, float distance, RaycastHit2D[] results)
        {
            return Physics2D.CapsuleCast(
                origin:           rb.position + capsule.offset,
                size:             capsule.size,
                capsuleDirection: capsule.direction,
                angle:            0f,
                direction:        direction,
                contactFilter:    contactFilter,
                results:          results,
                distance:         distance
            );
        }

        /// <summary>
        /// Returns the hit with the smallest distance from the buffer.
        /// Assumes count > 0.
        /// </summary>
        private static RaycastHit2D GetClosestHit(RaycastHit2D[] buffer, int count)
        {
            RaycastHit2D closest = buffer[0];
            for (int i = 1; i < count; i++)
            {
                if (buffer[i].distance < closest.distance)
                    closest = buffer[i];
            }
            return closest;
        }

        // ================================================================
        //  DEBUG VISUALIZATION
        // ================================================================

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;

            // Ground normal
            Gizmos.color = isGrounded ? Color.green : Color.red;
            Vector2 feet = rb.position + capsule.offset + Vector2.down * (capsule.size.y * 0.5f);
            Gizmos.DrawLine(feet, feet + groundNormal * 0.5f);

            // Slope tangent
            if (isGrounded && slopeAngle > 1f)
            {
                Gizmos.color = Color.cyan;
                Vector2 tangent = new Vector2(groundNormal.y, -groundNormal.x);
                Gizmos.DrawLine(feet, feet + tangent * 0.4f);
            }

            // Step offset height
            if (config != null && config.stepOffset > 0f)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f); // orange
                Gizmos.DrawLine(feet, feet + Vector2.up * config.stepOffset);
            }

            // Wall contact
            if (isOnWall)
            {
                Gizmos.color = Color.magenta;
                Vector2 wallSide = rb.position + new Vector2(
                    wallDirection * capsule.size.x * 0.5f, 0f);
                Gizmos.DrawWireSphere(wallSide, 0.08f);
            }
        }
#endif
    }
}
