// ============================================================================
//  TestSceneBuilder.cs
//  Editor-only: one-click test scene generation.
//  Menu: PlatformerKit > Build Test Scene
//
//  Creates a complete test environment with:
//    - Player (capsule collider + Motor + Controller)
//    - Flat ground
//    - 30-degree slope (ascending)
//    - 30-degree slope (descending)
//    - Staircase with 1px-ish bumps (step offset test)
//    - Moving platform (horizontal ping-pong)
//    - Moving platform (vertical ping-pong)
//    - Wall for future wall-jump testing
//    - Camera that follows the player
//    - MotorConfig + PlayerConfig ScriptableObjects (auto-created)
// ============================================================================
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using PlatformerKit.Physics;
using PlatformerKit.Player;
using PlatformerKit.Platform;
using PlatformerKit.Debug;

namespace PlatformerKit.Editor
{
    public static class TestSceneBuilder
    {
        [MenuItem("PlatformerKit/Build Test Scene", priority = 0)]
        public static void BuildTestScene()
        {
            if (!EditorUtility.DisplayDialog(
                "Build Test Scene",
                "This will create test objects in the current scene.\n\n" +
                "Objects will be created under a [TEST_SCENE] root.\n" +
                "Continue?",
                "Build", "Cancel"))
                return;

            // ---- Root container ----
            var existing = GameObject.Find("[TEST_SCENE]");
            if (existing != null) Object.DestroyImmediate(existing);

            var root = new GameObject("[TEST_SCENE]");
            Undo.RegisterCreatedObjectUndo(root, "Build Test Scene");

            // ---- ScriptableObject configs ----
            var motorConfig  = CreateOrLoadConfig<MotorConfig>("Assets/MotorConfig_Test.asset");
            var playerConfig = CreateOrLoadConfig<PlayerConfig>("Assets/PlayerConfig_Test.asset");

            // ---- Physics Material (zero friction for slopes) ----
            var slippery = new PhysicsMaterial2D("Frictionless") { friction = 0f, bounciness = 0f };

            // ================ PLAYER ================
            var player = CreatePlayer(motorConfig, playerConfig, slippery);
            player.transform.SetParent(root.transform);
            player.transform.position = new Vector3(0f, 2f, 0f);

            // ================ GROUND PIECES ================
            var groundParent = new GameObject("--- Ground ---");
            groundParent.transform.SetParent(root.transform);

            // Flat ground (center)
            CreateBox("Flat Ground", groundParent.transform,
                new Vector3(0f, -0.5f, 0f), new Vector2(20f, 1f), 0f, slippery);

            // Slope UP (right side, 30 degrees)
            CreateBox("Slope Up 30deg", groundParent.transform,
                new Vector3(12f, 1.5f, 0f), new Vector2(10f, 1f), -30f, slippery);

            // Slope DOWN (left side, 30 degrees)
            CreateBox("Slope Down 30deg", groundParent.transform,
                new Vector3(-12f, 1.5f, 0f), new Vector2(10f, 1f), 30f, slippery);

            // Extended flat after slopes
            CreateBox("Flat After Slope R", groundParent.transform,
                new Vector3(19f, 4f, 0f), new Vector2(8f, 1f), 0f, slippery);
            CreateBox("Flat After Slope L", groundParent.transform,
                new Vector3(-19f, 4f, 0f), new Vector2(8f, 1f), 0f, slippery);

            // ================ STEP OFFSET TEST ================
            var stepsParent = new GameObject("--- Steps ---");
            stepsParent.transform.SetParent(root.transform);

            // Tiny bumps on flat ground (should auto-climb)
            for (int i = 0; i < 5; i++)
            {
                float height = 0.02f + i * 0.02f; // 0.02 to 0.10 world units
                CreateBox($"Bump_{i} (h={height:F2})", stepsParent.transform,
                    new Vector3(3f + i * 1.2f, height * 0.5f, 0f),
                    new Vector2(0.3f, height), 0f, slippery);
            }

            // One bump ABOVE step offset (should block)
            CreateBox("Bump_BLOCKER (h=0.20)", stepsParent.transform,
                new Vector3(10f, 0.1f, 0f), new Vector2(0.3f, 0.20f), 0f, slippery);

            // ================ WALLS (for future wall-jump) ================
            var wallsParent = new GameObject("--- Walls ---");
            wallsParent.transform.SetParent(root.transform);

            CreateBox("Wall Left", wallsParent.transform,
                new Vector3(-4f, 2f, 0f), new Vector2(1f, 6f), 0f, slippery);
            CreateBox("Wall Right", wallsParent.transform,
                new Vector3(4f, 2f, 0f), new Vector2(1f, 6f), 0f, slippery);

            // ================ MOVING PLATFORMS ================
            var platParent = new GameObject("--- Moving Platforms ---");
            platParent.transform.SetParent(root.transform);

            // Horizontal platform
            CreateMovingPlatform("Platform H", platParent.transform,
                new Vector3(0f, 3f, 0f),
                new Vector2[] { Vector2.zero, new Vector2(6f, 0f) },
                2.5f, slippery);

            // Vertical platform
            CreateMovingPlatform("Platform V", platParent.transform,
                new Vector3(-8f, 1f, 0f),
                new Vector2[] { Vector2.zero, new Vector2(0f, 5f) },
                2f, slippery);

            // Diagonal platform
            CreateMovingPlatform("Platform Diag", platParent.transform,
                new Vector3(8f, 1f, 0f),
                new Vector2[] { Vector2.zero, new Vector2(4f, 3f) },
                1.8f, slippery);

            // ================ CAMERA ================
            CreateFollowCamera(player.transform, root.transform);

            // ================ HUD ================
            var hud = new GameObject("DebugHUD");
            hud.transform.SetParent(root.transform);
            hud.AddComponent<PlatformerKit.Debug.MotorDebugHUD>();

            // ---- Final ----
            Selection.activeGameObject = player;
            SceneView.lastActiveSceneView?.FrameSelected();

            UnityEngine.Debug.Log("<color=green>[PlatformerKit] Test scene built successfully!</color>\n" +
                      "Press Play to test. Use A/D or Arrow Keys to move, Space to jump.");
        }

        // ================================================================
        //  FACTORY METHODS
        // ================================================================

        private static GameObject CreatePlayer(
            MotorConfig motorConfig, PlayerConfig playerConfig, PhysicsMaterial2D mat)
        {
            var go = new GameObject("Player");

            // Capsule Collider
            var capsule = go.AddComponent<CapsuleCollider2D>();
            capsule.size      = new Vector2(0.5f, 1f);
            capsule.offset    = new Vector2(0f, 0.5f);
            capsule.direction = CapsuleDirection2D.Vertical;
            capsule.sharedMaterial = mat;

            // Rigidbody (Motor sets this to Kinematic in Awake, but set here too)
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType      = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            // Motor
            var motor = go.AddComponent<KinematicMotor2D>();
            // Use SerializedObject to set private [SerializeField] fields
            var motorSO = new SerializedObject(motor);
            motorSO.FindProperty("config").objectReferenceValue = motorConfig;
            // Set collision mask to "Default" layer (layer 0)
            motorSO.FindProperty("collisionMask").intValue = 1; // layer 0 bitmask
            motorSO.ApplyModifiedPropertiesWithoutUndo();

            // Controller
            var controller = go.AddComponent<PlayerController>();
            var controllerSO = new SerializedObject(controller);
            controllerSO.FindProperty("config").objectReferenceValue = playerConfig;
            controllerSO.ApplyModifiedPropertiesWithoutUndo();

            // Visual placeholder (sprite)
            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform);
            visual.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sprite = CreateCapsuleSprite();
            sr.color  = new Color(0.2f, 0.8f, 1f); // light blue

            // Tag
            go.tag = "Player";
            go.layer = 0; // Default

            return go;
        }

        private static void CreateBox(
            string name, Transform parent,
            Vector3 position, Vector2 size, float rotation, PhysicsMaterial2D mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position    = position;
            go.transform.rotation    = Quaternion.Euler(0f, 0f, rotation);

            var col = go.AddComponent<BoxCollider2D>();
            col.size = size;
            col.sharedMaterial = mat;

            // Visual
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CreateSquareSprite();
            sr.color  = name.Contains("Bump") ? new Color(1f, 0.4f, 0.3f)
                      : name.Contains("Wall") ? new Color(0.6f, 0.6f, 0.7f)
                      : new Color(0.3f, 0.7f, 0.3f);
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = size;
            go.transform.localScale = Vector3.one;

            go.layer = 0; // Default — must match motor's collisionMask
        }

        private static void CreateMovingPlatform(
            string name, Transform parent,
            Vector3 position, Vector2[] waypoints, float speed, PhysicsMaterial2D mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = position;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(3f, 0.4f);
            col.sharedMaterial = mat;

            var platform = go.AddComponent<SimpleMovingPlatform>();
            var so = new SerializedObject(platform);
            var wpProp = so.FindProperty("waypoints");
            wpProp.arraySize = waypoints.Length;
            for (int i = 0; i < waypoints.Length; i++)
                wpProp.GetArrayElementAtIndex(i).vector2Value = waypoints[i];
            so.FindProperty("speed").floatValue = speed;
            so.FindProperty("waitTime").floatValue = 0.3f;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Visual
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite   = CreateSquareSprite();
            sr.color    = new Color(1f, 0.85f, 0.2f); // gold
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size     = new Vector2(3f, 0.4f);

            go.layer = 0;
        }

        private static void CreateFollowCamera(Transform target, Transform root)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.transform.SetParent(root);
                cam = camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
            }

            cam.orthographic     = true;
            cam.orthographicSize = 8f;
            cam.transform.position = new Vector3(0f, 3f, -10f);
            cam.backgroundColor = new Color(0.15f, 0.15f, 0.2f);

            // Add simple follow script
            var follow = cam.gameObject.GetComponent<PlatformerKit.Debug.SimpleFollow>();
            if (follow == null)
                follow = cam.gameObject.AddComponent<PlatformerKit.Debug.SimpleFollow>();
            follow.target = target;
        }

        // ================================================================
        //  ASSET HELPERS
        // ================================================================

        private static T CreateOrLoadConfig<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var instance = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(instance, path);
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log($"Created config: {path}");
            return instance;
        }

        private static Sprite CreateCapsuleSprite()
        {
            // 16x32 white texture as placeholder capsule
            var tex = new Texture2D(16, 32);
            var pixels = new Color[16 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 16; x++)
                {
                    float dx = (x - 7.5f) / 8f;
                    float dy = (y - 15.5f) / 16f;
                    float capsuleDist = Mathf.Sqrt(dx * dx + Mathf.Max(0, Mathf.Abs(dy) - 0.5f)
                                        * Mathf.Max(0, Mathf.Abs(dy) - 0.5f) / (0.5f * 0.5f)
                                        + dx * dx * 0) ;
                    pixels[y * 16 + x] = Color.white;
                }
            tex.SetPixels(pixels);
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 16, 32), new Vector2(0.5f, 0f), 32f);
        }

        private static Sprite CreateSquareSprite()
        {
            var tex = new Texture2D(4, 4);
            var pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        }
    }

}
#endif
