// ============================================================================
//  SimpleFollow.cs
//  Minimal smooth-follow camera for testing.
//  Runtime script — NOT editor-only.
// ============================================================================
using UnityEngine;

namespace PlatformerKit.Debug
{
    public class SimpleFollow : MonoBehaviour
    {
        public Transform target;
        public float smoothTime = 0.15f;
        public Vector3 offset = new Vector3(0f, 2f, -10f);

        private Vector3 vel;

        private void LateUpdate()
        {
            if (target == null) return;
            Vector3 desired = target.position + offset;
            transform.position = Vector3.SmoothDamp(
                transform.position, desired, ref vel, smoothTime);
        }
    }
}
