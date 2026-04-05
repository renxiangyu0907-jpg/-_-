# Kinematic 2D Motor - Architecture Design Document

---

## 0. Overall Data Flow Diagram

```
 FixedUpdate Execution Order
 ===========================

 [PlayerController]                    [KinematicMotor2D]
       |                                      |
  Read Input                                  |
       |                                      |
  Apply Gravity to velocity.y                 |
  Apply Jump / CoyoteTime                     |
  Apply Horizontal Accel/Decel                |
       |                                      |
  Call motor.Move(velocity * dt) ------------>|
       |                               [Phase 1] GroundProbe()
       |                                 Capsule Cast downward
       |                                 -> isGrounded, groundNormal
       |                                 -> slopeAngle, platformDelta
       |                                      |
       |                               [Phase 2] HorizontalMove()
       |                                 Project onto slope tangent
       |                                 Capsule Cast in move dir
       |                                 -> StepOffset attempt
       |                                 -> Slide along wall
       |                                      |
       |                               [Phase 3] VerticalMove()
       |                                 Capsule Cast up/down
       |                                 -> Head bump ceiling
       |                                 -> Snap to ground (anti-launch)
       |                                      |
       |                               [Phase 4] PlatformFollow()
       |                                 Add platform delta to final pos
       |                                      |
       |                               rb.MovePosition(finalPos)
       |                                      |
  Read motor.IsGrounded  <--------------------|
  Read motor.Velocity    <--------------------|
  Broadcast events to Spine anim              |
```

---

## 1. Core Interfaces & Data Structs

```csharp
// ============================================================
//  IMovingPlatform - any moving platform implements this
// ============================================================
public interface IMovingPlatform
{
    /// <summary>
    /// The world-space displacement this platform moved during the CURRENT FixedUpdate.
    /// Calculated as: currentPos - previousPos, updated in platform's own FixedUpdate.
    /// </summary>
    Vector2 FrameDelta { get; }
}

// ============================================================
//  MotorState - read-only snapshot exposed to upper layers
// ============================================================
public readonly struct MotorState
{
    public readonly bool   IsGrounded;
    public readonly bool   IsOnSlope;        // slope angle > ~1 degree
    public readonly bool   IsOnPlatform;     // standing on IMovingPlatform
    public readonly float  SlopeAngle;       // 0 = flat, degrees
    public readonly Vector2 GroundNormal;    // (0,1) on flat ground
    public readonly Vector2 Velocity;        // final actual velocity this frame

    // constructor omitted for brevity
}

// ============================================================
//  MotorConfig - tunable constants, ScriptableObject or [Serializable]
// ============================================================
[System.Serializable]
public class MotorConfig
{
    public float SkinWidth       = 0.015f;   // prevent overlap deadlock
    public float GroundProbeLen  = 0.08f;    // how far below to probe
    public float MaxSlopeAngle   = 55f;      // walkable slope limit
    public float StepOffset      = 0.12f;    // auto-climb pixel bumps (world units)
    public float SnapDownForce   = 0.25f;    // downhill ground-snap distance
    public int   MaxCastBounces  = 3;        // slide-iteration cap
}
```

---

## 2. KinematicMotor2D - Pseudocode

```csharp
[RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D))]
public class KinematicMotor2D : MonoBehaviour
{
    // === Dependencies ===
    Rigidbody2D        rb;          // BodyType = Kinematic
    CapsuleCollider2D  capsule;     // used for ALL shape casts
    MotorConfig        config;
    ContactFilter2D    filter;      // layer mask, ignore triggers

    // === Output State (read-only for outside) ===
    public MotorState  State { get; private set; }

    // === Events (animation layer subscribes) ===
    public event Action OnLanded;          // air -> ground transition
    public event Action OnLeftGround;      // ground -> air transition
    public event Action<float> OnSlopeChanged;  // angle changed

    // === Internal working vars ===
    RaycastHit2D[]  hitBuffer = new RaycastHit2D[8];
    Vector2         groundNormal;
    IMovingPlatform currentPlatform;
    bool            wasGrounded;

    // ================================================================
    //  PUBLIC API - called by PlayerController each FixedUpdate
    // ================================================================

    /// <summary>
    /// The ONLY entry point. desiredDelta is already multiplied by dt.
    /// e.g. motor.Move(velocity * Time.fixedDeltaTime);
    /// </summary>
    public void Move(Vector2 desiredDelta)
    {
        Vector2 startPos = rb.position;

        // ---- Phase 1: Ground Probe ----
        GroundProbe();

        // ---- Phase 2: Horizontal ----
        Vector2 hDelta = ProjectOnSlope(desiredDelta);
        hDelta = CastAndSlide(hDelta, isHorizontalPass: true);

        // ---- Phase 3: Vertical ----
        Vector2 vDelta = new Vector2(0, desiredDelta.y);
        vDelta = CastAndSlide(vDelta, isHorizontalPass: false);

        // ---- Phase 3.5: Ground Snap (anti-launch on downhill) ----
        if (wasGrounded && !State.IsGrounded && desiredDelta.y <= 0)
            vDelta += GroundSnap();

        // ---- Phase 4: Moving Platform ----
        Vector2 platformDelta = Vector2.zero;
        if (currentPlatform != null)
            platformDelta = currentPlatform.FrameDelta;

        // ---- Commit ----
        Vector2 finalPos = startPos + hDelta + vDelta + platformDelta;
        rb.MovePosition(finalPos);

        // ---- Update Output State & Fire Events ----
        UpdateStateAndEvents(startPos, finalPos);
    }

    // ================================================================
    //  Phase 1 - GROUND PROBE
    // ================================================================

    void GroundProbe()
    {
        /*
         * Cast capsule downward by (SkinWidth + GroundProbeLen).
         * If hit:
         *   - Read hit.normal -> groundNormal
         *   - slopeAngle = Vector2.Angle(hit.normal, Vector2.up)
         *   - isGrounded = (slopeAngle <= MaxSlopeAngle)
         *   - Check hit.collider for IMovingPlatform
         *     (via GetComponent or cache by tag)
         * If no hit:
         *   - isGrounded = false
         *   - currentPlatform = null
         */

        float castDist = config.SkinWidth + config.GroundProbeLen;
        int count = CapsuleCast(Vector2.down, castDist, hitBuffer);

        if (count > 0)
        {
            RaycastHit2D hit = GetClosestHit(hitBuffer, count);
            groundNormal = hit.normal;
            float angle  = Vector2.Angle(groundNormal, Vector2.up);

            isGrounded = (angle <= config.MaxSlopeAngle);
            slopeAngle = angle;

            // Moving platform detection
            currentPlatform = hit.collider.GetComponentInParent<IMovingPlatform>();
        }
        else
        {
            isGrounded     = false;
            slopeAngle     = 0;
            groundNormal   = Vector2.up;
            currentPlatform = null;
        }
    }

    // ================================================================
    //  Phase 2/3 - CAST AND SLIDE (iterative depenetration)
    // ================================================================

    Vector2 CastAndSlide(Vector2 delta, bool isHorizontalPass)
    {
        /*
         * Iterative approach (max MaxCastBounces iterations):
         *
         * remainingDelta = delta
         * totalMoved = Vector2.zero
         *
         * for i in 0..MaxCastBounces:
         *     Cast capsule in direction of remainingDelta
         *     distance = remainingDelta.magnitude - SkinWidth
         *
         *     if no hit:
         *         totalMoved += remainingDelta
         *         break
         *
         *     // Move up to the hit point (minus skin)
         *     safeDist = hit.distance - SkinWidth
         *     totalMoved += remainingDelta.normalized * safeDist
         *
         *     // --- STEP OFFSET (horizontal pass only) ---
         *     if isHorizontalPass AND TryStepUp(hit, remainingDelta, out stepDelta):
         *         totalMoved += stepDelta
         *         break   // successfully climbed the step
         *
         *     // Slide: remove the component going INTO the surface
         *     remainingDelta -= totalMoved   // what's left
         *     remainingDelta = SlideAlongSurface(remainingDelta, hit.normal)
         *
         * return totalMoved
         */
    }

    // ================================================================
    //  SLOPE PROJECTION  <<<< CRITICAL MATH - SEE SECTION 4 >>>>
    // ================================================================

    Vector2 ProjectOnSlope(Vector2 desiredDelta)
    {
        if (!isGrounded || slopeAngle < 1f)
            return new Vector2(desiredDelta.x, 0);  // flat or airborne

        //  >>> THE KEY FORMULA <<<
        //
        //  tangent = perpendicular to groundNormal, pointing right
        //  tangent = new Vector2(groundNormal.y, -groundNormal.x)
        //
        //  projectedDelta = dot(desiredHorizontal, tangent) * tangent
        //
        //  This makes velocity FOLLOW the slope surface.

        Vector2 tangent = new Vector2(groundNormal.y, -groundNormal.x);
        // tangent always points "rightward" along the surface.
        // If character moves left (desiredDelta.x < 0), the dot product
        // naturally becomes negative, flipping the direction. No branching needed.

        float speed = desiredDelta.x;  // horizontal intent
        return tangent * speed;
    }

    // ================================================================
    //  GROUND SNAP (anti-launch on downhill)
    // ================================================================

    Vector2 GroundSnap()
    {
        /*
         * Cast capsule downward by SnapDownForce distance.
         * If hit AND slope angle is walkable:
         *     return Vector2.down * (hit.distance - SkinWidth)
         *     // also re-flag isGrounded = true
         * Else:
         *     return Vector2.zero  // genuinely left ground (e.g. ledge)
         */
    }

    // ================================================================
    //  STEP OFFSET (ghost collision fix)
    // ================================================================

    bool TryStepUp(RaycastHit2D wallHit, Vector2 moveDir, out Vector2 stepDelta)
    {
        /*
         * 1. Cast UP by StepOffset height from current position
         *    - If blocked (ceiling), fail.
         *
         * 2. From that elevated position, Cast HORIZONTALLY in moveDir
         *    by original remaining distance.
         *    - Record how far we actually moved.
         *
         * 3. From that new position, Cast DOWN by StepOffset
         *    - If we land on walkable ground: SUCCESS
         *      stepDelta = (up vector) + (horizontal moved) + (down to land)
         *      return true
         *    - If no ground or slope too steep: FAIL
         *      stepDelta = zero; return false
         */
    }

    // ================================================================
    //  HELPER - Shape Cast wrapper
    // ================================================================

    int CapsuleCast(Vector2 direction, float distance, RaycastHit2D[] results)
    {
        /*
         * return capsule.Cast(
         *     direction,
         *     filter,
         *     results,
         *     distance + SkinWidth
         * );
         *
         * The capsule's size/offset are read from the CapsuleCollider2D
         * component. Unity handles rotation automatically.
         * 
         * IMPORTANT: The capsule cast is performed from rb.position,
         * Unity uses the collider's current world-space shape.
         */
    }
}
```

---

## 3. PlayerController - Pseudocode

```csharp
public class PlayerController : MonoBehaviour
{
    // === Dependencies ===
    KinematicMotor2D  motor;
    MotorConfig       motorConfig;    // shared reference

    // === Player Tuning ===
    [Header("Horizontal")]
    float maxRunSpeed    = 8f;
    float accelTime      = 0.08f;     // seconds 0 -> max (ground)
    float decelTime      = 0.05f;     // seconds max -> 0 (ground)
    float airAccelFactor = 0.65f;

    [Header("Jump")]
    float jumpHeight     = 3.2f;      // desired apex height
    float jumpGravity;                // computed: 2h / t^2
    float fallGravity;                // 1.5x ~ 2x jumpGravity (heavy landing)
    float maxFallSpeed   = -18f;

    [Header("Assist")]
    float coyoteTime     = 0.08f;     // seconds still allowed to jump after leaving ground
    float jumpBuffer     = 0.10f;     // seconds buffer before landing

    // === Internal State ===
    Vector2 velocity;                  // persistent across frames
    float   coyoteTimer;
    float   jumpBufferTimer;
    bool    isJumping;                 // true from jump start until apex or release

    // === Events for Spine Animation (decoupled) ===
    public event Action OnJump;
    public event Action OnLand;
    // SpineAnimController subscribes to these + reads motor.State

    // ================================================================
    //  LIFECYCLE
    // ================================================================

    void Update()
    {
        // Input is read in Update for responsiveness
        ReadInput();
        TickTimers();
    }

    void FixedUpdate()
    {
        MotorState state = motor.State;

        // ---- Coyote Time Management ----
        if (state.IsGrounded)
            coyoteTimer = coyoteTime;
        // coyoteTimer is ticked down in Update()

        // ---- Horizontal ----
        float inputX    = GetHorizontalInput();  // -1..+1
        float targetVx  = inputX * maxRunSpeed;
        float accel     = state.IsGrounded ? accelRate : accelRate * airAccelFactor;
        velocity.x      = MoveTowards(velocity.x, targetVx, accel * dt);

        // ---- Gravity ----
        float grav = (velocity.y > 0 && isJumping) ? jumpGravity : fallGravity;
        velocity.y -= grav * dt;
        velocity.y  = Max(velocity.y, maxFallSpeed);

        // ---- Jump ----
        if (WantJump() && coyoteTimer > 0)
        {
            velocity.y  = CalculateJumpVelocity();  // sqrt(2 * g * h)
            isJumping   = true;
            coyoteTimer = 0;
            OnJump?.Invoke();
        }

        // ---- Variable Jump Height (release early = cut velocity) ----
        if (isJumping && !HoldingJump() && velocity.y > 0)
        {
            velocity.y *= 0.5f;   // instant cut
            isJumping = false;
        }

        // ---- Landing detection ----
        if (!wasGrounded && state.IsGrounded)
        {
            velocity.y = 0;
            isJumping  = false;
            OnLand?.Invoke();
        }

        // ---- Feed Motor ----
        motor.Move(velocity * Time.fixedDeltaTime);

        wasGrounded = state.IsGrounded;
    }

    // ================================================================
    //  JUMP PHYSICS DERIVATION
    // ================================================================

    void ComputeGravityConstants()
    {
        /*
         * Given desired jumpHeight (h) and timeToApex (t):
         *
         *   jumpGravity    = 2h / t^2
         *   jumpVelocity   = 2h / t       (= sqrt(2 * g * h) equivalently)
         *   fallGravity    = jumpGravity * fallMultiplier  (e.g. 1.5x)
         *
         * This gives a "Celeste-like" snappy jump:
         * - Predictable apex height regardless of frame rate
         * - Heavier fall for game feel
         */
    }
}
```

---

## 4. Slope Tangent Math - Detailed Derivation

This is the crux of "no launching off slopes." Here's the full breakdown:

### 4.1 The Problem

```
Character moving RIGHT on a downward slope:

        Player --->
         *
          \  slope surface
           \
            \___________

If we naively apply velocity = (speed, 0), the character lifts off
the surface for 1-2 frames before gravity pulls them back.
This causes jittery "bunny hopping" on slopes.
```

### 4.2 The Ground Normal

```
Unity's hit.normal gives us a vector perpendicular to the surface,
pointing OUTWARD (away from the collider):

            normal
              ^
              |  /
              | / slope surface
              |/
         -----+-------

For a slope tilted 30 degrees to the right:
  normal = (-sin30, cos30) = (-0.5, 0.866)

For flat ground:
  normal = (0, 1)
```

### 4.3 Computing the Tangent

```
Given normal = (nx, ny), the surface tangent pointing RIGHT is:

  tangent = (ny, -nx)

Why? Rotating a 2D vector 90 degrees clockwise:
  (x, y) -> (y, -x)

Verification on flat ground:
  normal  = (0, 1)
  tangent = (1, 0)   // points right along flat ground. Correct!

Verification on 30-degree slope (ascending right):
  normal  = (-0.5, 0.866)
  tangent = (0.866, 0.5)   // points right AND upward along slope. Correct!

Verification on 30-degree slope (descending right):
  normal  = (0.5, 0.866)
  tangent = (0.866, -0.5)  // points right AND downward along slope. Correct!
```

### 4.4 Projecting Velocity onto Tangent

```
We want the character's speed ALONG the surface to equal
their intended horizontal speed. We do NOT want them to slow down
on slopes (that would feel bad).

Method - direct scalar projection:

  projectedDelta = horizontalSpeed * tangent

This works because tangent is already a unit vector (the normal was
unit length, and 90-degree rotation preserves magnitude).

When horizontalSpeed is negative (moving left):
  projectedDelta flips direction automatically. No if-branches needed.
```

### 4.5 Why This Prevents Launching

```
On a downslope moving right:

  BEFORE (naive):
    velocity = (5, 0)       // moves horizontally, lifts off slope
    
  AFTER (projected):
    tangent  = (0.866, -0.5)
    velocity = 5 * (0.866, -0.5) = (4.33, -2.5)
    // Character moves right AND downward, hugging the slope!

The velocity vector now has a downward component that exactly
matches the slope angle. Combined with the GroundSnap pass,
the character is welded to the surface.
```

### 4.6 Code Summary

```csharp
Vector2 ProjectOnSlope(Vector2 desiredDelta)
{
    // Airborne or flat ground -> no projection needed
    if (!isGrounded || slopeAngle < 1f)
        return new Vector2(desiredDelta.x, 0);
    
    // Slope tangent (right-pointing along surface)
    Vector2 tangent = new Vector2(groundNormal.y, -groundNormal.x);
    
    // Project: preserve intended horizontal speed along slope
    return tangent * desiredDelta.x;
}
```

---

## 5. Step Offset Logic - Visual Explanation

```
Scenario: 1-pixel bump on flat ground. Without StepOffset,
horizontal cast hits the bump and velocity zeroes out.

  Step 1: Cast UP by StepOffset height
  
      +---+              +---+
      | ^ |  step up     |   |
      | | |  -------->   +---+
      +---+                |
   ====#====            ====#====     # = bump

  Step 2: Cast HORIZONTALLY from elevated position
  
                         +---+
                         | ->|  horizontal cast clears the bump
                         +---+
   ====#====            ====#====

  Step 3: Cast DOWN to find new ground
  
                              +---+
                              | v |  snap down
                              +---+
   ====#=====================#====

  Result: Character smoothly "steps over" the bump.
  If Step 3 finds no ground or a steep wall -> REJECT, treat as wall.
```

---

## 6. Moving Platform Sync - Sequence Diagram

```
Frame N:
  1. MovingPlatform.FixedUpdate() executes first  (Script Execution Order: -100)
     -> records FrameDelta = currentPos - lastPos
     -> updates lastPos = currentPos

  2. PlayerController.FixedUpdate() executes second (Script Execution Order: 0)
     -> computes player velocity
     -> calls motor.Move(velocity * dt)

  3. Inside KinematicMotor2D.Move():
     -> GroundProbe() detects platform collider
     -> Gets IMovingPlatform via GetComponentInParent
     -> Reads platformDelta = platform.FrameDelta
     -> Adds platformDelta to final rb.MovePosition

  Key: The platform must update BEFORE the player.
       Use Unity's Script Execution Order to guarantee this.
       Platform = -100, Player = 0.
```

---

## 7. Execution Order Summary

| Priority | Script              | Phase              |
|----------|---------------------|--------------------|
| -100     | MovingPlatform      | Record FrameDelta  |
| 0        | PlayerController    | Input + Physics    |
| 0        | KinematicMotor2D    | Cast + Move        |
| +100     | SpineAnimController | Read State, Animate|

---

## 8. File Structure (Final Implementation)

```
Scripts/
  Physics/
    MotorConfig.cs            // [Serializable] tuning data
    MotorState.cs             // readonly struct
    IMovingPlatform.cs        // interface
    KinematicMotor2D.cs       // the motor
  Player/
    PlayerController.cs       // brain
  Platform/
    SimpleMovingPlatform.cs   // implements IMovingPlatform
  Animation/
    SpineAnimBridge.cs        // subscribes to events, reads State
```

---

## 9. Key Design Decisions & Rationale

| Decision | Why |
|---|---|
| **Capsule Cast** (not Box or Circle) | Rounded bottom prevents catching on tile edges; rounded top smooths ceiling collisions. Capsule is the gold standard for platformer characters. |
| **Separate H and V passes** | Prevents diagonal movement from "eating" one axis. Celeste and Maddy Thorson's talks confirm this two-pass approach. |
| **SkinWidth = 0.015f** | Thin enough to be invisible, thick enough to prevent float-precision overlap. Industry standard is 0.01-0.02. |
| **MaxCastBounces = 3** | Handles corner sliding (wall + floor). More than 3 means degenerate geometry; cap prevents infinite loops. |
| **GroundSnap as separate pass** | Only activates when transitioning ground->air with downward intent. Won't interfere with intentional jumps. |
| **Motor knows nothing about input** | Motor is reusable for NPCs, enemies, any kinematic agent. Only PlayerController is human-specific. |
| **Events instead of Animator coupling** | Spine animation is completely decoupled. Motor/Controller can be tested without any animation system present. |
