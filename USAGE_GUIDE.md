# PlatformerKit - Step-by-Step Usage Guide

---

## Part 1: Import Into Your Unity Project

### 1.1 Copy the Scripts folder

```
Your Unity Project/
  Assets/
    Scripts/            <-- copy the whole Scripts folder here
      Physics/
        KinematicMotor2D.cs
        MotorConfig.cs
        MotorState.cs
        IMovingPlatform.cs
      Player/
        PlayerController.cs
        PlayerConfig.cs
      Platform/
        SimpleMovingPlatform.cs
      Animation/
        SpineAnimBridge.cs
      Debug/
        MotorDebugHUD.cs
        SimpleFollow.cs
      Editor/
        TestSceneBuilder.cs
```

**Detailed steps:**

1. Open your Unity project
2. In the **Project** window at the bottom, navigate to `Assets`
3. Right-click in the empty area → **Show in Explorer** (Windows) / **Reveal in Finder** (Mac)
4. Copy the entire `Scripts` folder from the downloaded repo into the `Assets` folder
5. Switch back to Unity — wait for the progress bar in the bottom right to finish compiling

### 1.2 Verify: No compile errors

After Unity finishes compiling, check the **Console** window:
- Press `Ctrl+Shift+C` (Windows) or `Cmd+Shift+C` (Mac) to open Console
- If you see **zero** red errors from our scripts, you're good
- The GridPaintingState errors from Tilemap are unrelated (see FAQ below)

---

## Part 2: One-Click Test Scene (Fastest Way)

### 2.1 Create a new empty scene

1. Menu: **File → New Scene**
2. Choose **Basic (Built-in)** template → click **Create**
3. Menu: **File → Save As** → name it `TestLevel` → Save

### 2.2 Build the test scene

1. Look at the top menu bar — you should see a new menu called **PlatformerKit**
2. Click: **PlatformerKit → Build Test Scene**
3. A dialog asks "This will create test objects..." → click **Build**
4. Wait 1-2 seconds

**What just happened?** The script auto-created:
- A `[TEST_SCENE]` root object in your Hierarchy
- A Player with capsule collider, motor, and controller
- Flat ground, slopes, step bumps, walls, moving platforms
- A follow camera
- Config files at `Assets/MotorConfig_Test.asset` and `Assets/PlayerConfig_Test.asset`

### 2.3 Press Play and control the character

1. Click the **Play** button (top center of Unity, the triangle ▶)
2. Controls:
   - **A / D** or **Left Arrow / Right Arrow** — move left/right
   - **Space** — jump
   - **F1** — toggle the debug HUD on/off

### 2.4 What to look for in each test area

```
     LEFT SIDE                   CENTER                   RIGHT SIDE
   ┌─────────────┐        ┌───────────────────┐       ┌─────────────┐
   │  Descending │        │    Flat Ground     │       │  Ascending  │
   │   Slope     │        │                   │       │    Slope    │
   │  (test:     │        │  Player spawns    │       │  (test:     │
   │  downhill   │        │  here at (0,2)    │       │  uphill     │
   │  anti-      │        │                   │       │  movement)  │
   │  launch)    │        │  Bumps are here   │       │             │
   └─────────────┘        │  at X = 3 to 10   │       └─────────────┘
                          │                   │
                          │  Walls at X = -4  │
                          │  and X = +4       │
                          └───────────────────┘
                          
   [Platform V]            [Platform H ←→]             [Platform Diag]
   at X = -8               at Y = 3                    at X = 8
   goes UP/DOWN            goes LEFT/RIGHT             goes DIAGONAL
```

**Test 1 — Basic movement (flat ground)**
- Move left and right on the flat ground in the center
- Expected: smooth acceleration and deceleration, no jitter

**Test 2 — Jump**
- Press Space once quickly (tap)
- Then press Space and hold it
- Expected: tap = low jump, hold = high jump (variable jump height)

**Test 3 — Coyote Time**
- Walk off the edge of the flat ground (don't jump, just walk off)
- IMMEDIATELY after leaving the edge, press Space
- Expected: the character still jumps! (coyote time = 0.08s grace period)

**Test 4 — Uphill slope (right side)**
- Run to the right, onto the ascending slope
- Expected: character follows the slope surface smoothly, no speed loss

**Test 5 — Downhill slope (left side) — THIS IS THE CRITICAL TEST**
- Run to the left, up the descending slope, then turn around and run back down
- Or: jump onto the slope top and run downhill
- Expected: character hugs the slope surface, NO bouncing, NO launching into the air
- Watch the Debug HUD: "Slope" should show ~30 deg, state should stay GROUNDED

**Test 6 — Step offset bumps (center-right, X = 3 to 10)**
- Run to the right across the small red bumps
- Expected:
  - Bumps 0-4 (height 0.02 to 0.10): character runs over them seamlessly
  - The last bump at X=10 (height 0.20): character gets BLOCKED (can't auto-climb)
- This proves the step offset threshold (0.12) is working

**Test 7 — Moving platforms**
- Walk left to X = -8, find the vertical platform — stand on it
- Walk to center at Y = 3, find the horizontal platform — stand on it  
- Walk right to X = 8, find the diagonal platform — stand on it
- Expected: character rides each platform perfectly, no sliding or falling through

**Test 8 — Wall detection**
- Walk into the wall at X = -4 or X = +4
- Look at the Debug HUD
- Expected: "Wall" shows LEFT or RIGHT when touching the wall

---

## Part 3: Manual Setup (For Your Own Scene)

If you want to add the controller to YOUR existing scene instead of using the test builder:

### 3.1 Create config assets

1. In **Project** window, right-click in Assets folder
2. **Create → PlatformerKit → Motor Config** → name it `MyMotorConfig`
3. **Create → PlatformerKit → Player Config** → name it `MyPlayerConfig`
4. Click each one and review the default values in the Inspector

### 3.2 Set up the Player GameObject

1. In Hierarchy, right-click → **Create Empty** → name it `Player`
2. Set the tag to `Player` (dropdown at top of Inspector)
3. **Add Component** → search `Capsule Collider 2D`
   - Size: `X = 0.5, Y = 1.0`
   - Offset: `X = 0, Y = 0.5`
   - Direction: `Vertical`
4. **Add Component** → search `Rigidbody 2D`
   - Body Type: `Kinematic` (dropdown)
   - Interpolation: `Interpolate`
5. **Add Component** → search `Kinematic Motor 2D`
   - Drag `MyMotorConfig` into the Config slot
   - Collision Mask: click the dropdown, check `Default` (or whichever layer your ground uses)
6. **Add Component** → search `Player Controller`
   - Drag `MyPlayerConfig` into the Config slot

### 3.3 Set up ground

Every piece of ground MUST have:
- A **Collider2D** (BoxCollider2D, PolygonCollider2D, TilemapCollider2D, etc.)
- Be on a layer that matches the Motor's **Collision Mask**

Example — simple flat ground:
1. Create Empty → name it `Ground`
2. Add Component → `Box Collider 2D`
   - Size: `X = 30, Y = 1`
3. Position: `(0, -0.5, 0)` so the top surface is at Y = 0
4. Make sure it's on the `Default` layer

### 3.4 Set up a moving platform

1. Create Empty → name it `MyPlatform`
2. Add Component → `Rigidbody 2D` → set Body Type: **Kinematic**
3. Add Component → `Box Collider 2D` → Size: `X = 3, Y = 0.4`
4. Add Component → search `Simple Moving Platform`
   - Waypoints: set size to 2
     - Element 0: `(0, 0)`
     - Element 1: `(5, 0)` for horizontal, or `(0, 4)` for vertical
   - Speed: `3`
   - Wait Time: `0.5`
5. **CRITICAL**: Menu **Edit → Project Settings → Script Execution Order**
   - Click the `+` button
   - Find and add `SimpleMovingPlatform`
   - Set its order to `-100`
   - Click **Apply**
   
   (This ensures the platform moves BEFORE the player each frame)

### 3.5 Add the debug HUD (optional)

1. Create Empty → name it `DebugHUD`
2. Add Component → search `Motor Debug HUD`
3. It auto-finds the Player by tag at runtime

---

## Part 4: Tuning the Feel (Most Important!)

The whole point of using ScriptableObjects is live tuning.

### 4.1 How to tune at runtime

1. Press **Play**
2. In the **Project** window, click `MyPlayerConfig` (or `PlayerConfig_Test`)
3. The Inspector shows all values with sliders
4. **Drag the sliders while playing** — changes apply INSTANTLY
5. **Values are SAVED when you stop playing** (this is the ScriptableObject advantage)

### 4.2 What each value does

**PlayerConfig** (the feel knobs):

| Value | What it changes | Try this |
|---|---|---|
| `Max Run Speed` | Top horizontal speed | 6 feels slow, 12 feels Sonic |
| `Ground Accel Time` | 0→max speed time | 0.02 = instant snap, 0.15 = slidy |
| `Ground Decel Time` | max→0 speed time | 0.02 = instant stop, 0.12 = ice physics |
| `Air Control Factor` | Air maneuverability | 0.3 = committed jumps, 0.9 = full air control |
| `Jump Height` | Apex of jump in world units | 2.0 = low hop, 4.0 = soaring |
| `Time To Apex` | Seconds to reach peak | 0.25 = snappy, 0.5 = floaty |
| `Fall Gravity Multiplier` | How heavy the fall | 1.0 = symmetric, 2.5 = slams down |
| `Max Fall Speed` | Terminal velocity | 12 = floaty descent, 25 = fast falls |
| `Jump Cut Multiplier` | Release-early penalty | 0.0 = instant cut, 0.8 = almost no effect |
| `Coyote Time` | Grace period after ledge | 0.0 = none, 0.12 = generous |
| `Jump Buffer Time` | Early-press memory | 0.0 = frame-perfect only, 0.15 = forgiving |

**MotorConfig** (the physics knobs — change less often):

| Value | What it changes | Default | When to change |
|---|---|---|---|
| `Skin Width` | Collision shell thickness | 0.015 | If you see jitter or tunneling |
| `Ground Probe Distance` | How far below to check | 0.08 | If losing ground on rough terrain |
| `Ground Probe Width Ratio` | Shrink feet for probe | 0.9 | If false-grounding on ledge edges |
| `Max Slope Angle` | Walkable slope limit | 55 | If sliding on slopes you want walkable |
| `Snap Down Distance` | Downhill snap range | 0.25 | If launching on steep downhills |
| `Step Offset` | Auto-climb height | 0.12 | If getting stuck on tile seams |
| `Wall Normal Threshold` | Wall vs slope cutoff | 0.9 | If walls misdetect as slopes |

### 4.3 Recommended starting preset: "Celeste-like"

Click your `PlayerConfig` asset and set:
```
Max Run Speed         = 8.0
Ground Accel Time     = 0.06
Ground Decel Time     = 0.04
Air Control Factor    = 0.65
Jump Height           = 3.2
Time To Apex          = 0.4
Fall Gravity Multi    = 1.6
Max Fall Speed        = 18.0
Jump Cut Multiplier   = 0.5
Coyote Time           = 0.08
Jump Buffer Time      = 0.10
```

---

## Part 5: Connecting to Spine Animation

The `SpineAnimBridge.cs` is a ready-made stub. To connect it:

1. Select the Player GameObject
2. Add Component → search `Spine Anim Bridge`
3. Drag the Player's `KinematicMotor2D` into the Motor slot
4. Drag the Player's `PlayerController` into the Player slot
5. Open `SpineAnimBridge.cs` in your code editor
6. Uncomment the Spine-specific lines (marked with `// Uncomment when Spine is imported:`)
7. Set the animation names to match your Spine skeleton

The bridge subscribes to these events automatically:
- `motor.OnLanded` → plays land animation
- `motor.OnLeftGround` → lets state machine pick jump/fall
- `player.OnJump` → plays jump animation
- `player.OnFacingChanged` → flips skeleton

---

## FAQ

**Q: I see red errors about GridPaintingState**
A: That's a Unity Tilemap Editor bug, not our code. Go to Window → Package Manager → find "2D Tilemap Editor" → Update to latest version.

**Q: The character falls through the ground**
A: Check that the ground's Layer matches the Motor's Collision Mask. Click the Player → Inspector → Kinematic Motor 2D → Collision Mask dropdown. Make sure the ground's layer is checked.

**Q: The character slides on slopes**
A: Make sure the slope collider has a Physics Material 2D with friction = 0. Our motor handles slope movement mathematically; Unity's friction interferes with this.

**Q: The character shakes when standing on a moving platform**
A: Check Script Execution Order. The platform must execute at -100 (before the player). Menu: Edit → Project Settings → Script Execution Order.

**Q: How do I add wall-jump later?**
A: The motor already detects walls. In `PlayerController.cs`, add:
```csharp
// In FixedUpdate, after the jump section:
if (wantJump && state.IsOnWall && !state.IsGrounded)
{
    velocity = new Vector2(-state.WallDirection * wallJumpHForce, wallJumpVForce);
    isJumping = true;
    jumpBufferTimer = 0f;
    OnJump?.Invoke();
}
```
