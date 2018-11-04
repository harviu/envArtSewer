using System;
using UnityEngine;

[Serializable]
public class PushPullParameters
{
    // movement speeds
    [Range(0, 1)]
    public float moveSpeedPercent = .5f;   // What percentage of original speed should player move while grabbing an object?
    [Range(0, 1)]
    public float mouseSpeedPercent = .2f;  // What percentage of original sensitivity should mouse have while grabbing an object?
    public float skinWidth = .05f;        // How much external edge of collider should be ignored when determining raycast positions?
    public float yBreakThreshold = 0.2f;   // How far up/down does object need to move from original position to be released?
    public PushRaycasting pushRaycasting;
    public GroundRaycasting groundRaycasting;
    
    [Serializable]
    public class PushRaycasting
    {
        [Range(2, 10)]                       
        public int squareRays = 2;           // How many rays do we cast in front of object to check for collisions? (number is squared)
        public float pushStopDistance = .1f; // How far from a collision should we stop the object?
        public LayerMask pushStopLayers;     // Which layers should we check for collision while pushing the object?
    }
    
    [Serializable]
    public class GroundRaycasting
    {
        [Range(2, 10)]                       
        public int squareRays = 2;              // How many rays do we cast below object to check if grounded? (number is squared)
        public float groundCheckDistance = .1f; // How far below the object should we check for ground?
        public LayerMask groundCheckLayers;     // Which layers should we check for ground collision?
    }
}
