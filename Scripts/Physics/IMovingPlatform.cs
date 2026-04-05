// ============================================================================
//  IMovingPlatform.cs
//  Any moving/rotating platform implements this so the Motor can query
//  the exact displacement each physics frame.
// ============================================================================
using UnityEngine;

namespace PlatformerKit.Physics
{
    public interface IMovingPlatform
    {
        /// <summary>
        /// World-space displacement the platform moved THIS FixedUpdate.
        /// Calculated as currentPosition - previousPosition in the
        /// platform's own FixedUpdate (which must run BEFORE the player;
        /// set Script Execution Order to -100).
        /// </summary>
        Vector2 GetFrameDisplacement();
    }
}
