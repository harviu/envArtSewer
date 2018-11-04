using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

public class PushPullXZ : MonoBehaviour
{
    // Inspector/Public Variables
    public PushPullParameters defaultParameters;// Defines aspects of movement concerning the object
    [NonSerialized]
    public PushPullParameters overrideParameters;// Parameters provided by grabbed objects that require special movement speed or collision checking
    public PlayerInteraction playerInteraction; // What is player interaction with the object like?
    public bool debug = false;                  // Output debug info from OnGUI?

    // Private Member Variables
    private Transform player;              // alias for efficiency
    private Transform target;              // the target for pushing and pulling
    private CharacterMotor playerMotor;    // the motor that enables and defines character movement
    private CharacterController playerCtrl;// the controller that maintains character state while moving
    private MouseLook playerMouse;         // the script enabling mouse movement for the character
    private MouseLook cameraMouse;         // the script enabling mouse movement for the character's attached camera
    private bool isGrabbing = false;       // is player grabbing an object?
    private bool isInRange = false;        // is character within grabbing range of an object? (false while grabbing)
    private int grabPollElapsedFrames = 0; // number of elapsed frames since last raycast
    private PushState playerPushState = PushState.None;
    private PushDirection playerPushDir = PushDirection.TargetForward;
    private Vector3 targetLastPosition;    // position of target last frame
    private Vector3 targetLastRotation;    // euler rotation of target last frame
    private Vector3 localGrabPosition;     // position of object in relation to player when first grabbed
    private Dictionary<ColliderBoundsVertex, Vector3> dicColliderPoints = new Dictionary<ColliderBoundsVertex, Vector3>();

    // Object Movement Properties
    private PushPullParameters Parameters { get { return overrideParameters ?? defaultParameters; } }
    private float SkinWidth { get { return Parameters.skinWidth; } }
    private float YBreak { get { return Parameters.yBreakThreshold; } }
    private int PushRays { get { return Parameters.pushRaycasting.squareRays; } }
    private int GroundRays { get { return Parameters.groundRaycasting.squareRays; } }
    private float PushStopDistance { get { return Parameters.pushRaycasting.pushStopDistance; } }
    private float GroundCheckDistance { get { return Parameters.groundRaycasting.groundCheckDistance; } }
    private LayerMask PushCollisionLayers { get { return Parameters.pushRaycasting.pushStopLayers; } }
    private LayerMask GroundCollisionLayers { get { return Parameters.groundRaycasting.groundCheckLayers; } }

    // Player Movement Properties
    private float PlayerMoveSpeedPercent { get { return Parameters.moveSpeedPercent; } }
    private float PlayerMouseSpeedPercent { get { return Parameters.mouseSpeedPercent; } }

    // Player Interaction Properties
    public KeyCode GrabKey { get { return playerInteraction.grabKey; } }
    public float GrabDistance { get { return playerInteraction.grabDistance; } }
    public float GrabMoveTime { get { return playerInteraction.grabMoveTime; } }
    public int GrabPollFrames { get { return playerInteraction.grabPollFrames; } }
    public LayerMask GrabCollision { get { return playerInteraction.grabLayers; } }
    public string GrabText { get { return playerInteraction.grabText; } }
    public GUIStyle GrabTextStyle { get { return playerInteraction.grabTextStyle; } }

    // How is player interacting with target?
    private enum PushState
    {
        None,
        MovingPlayer,
        Pushing,
        Pulling,
    }

    // Which direction does push move the target?
    private enum PushDirection
    {
        TargetForward,
        TargetBack,
        TargetRight,
        TargetLeft
    }

    // Designator for each vertex of rectangular collider bounds
    private enum ColliderBoundsVertex
    {
        UpperBackLeft,
        UpperBackRight,
        UpperForwardLeft,
        UpperForwardRight,
        LowerBackLeft,
        LowerBackRight,
        LowerForwardLeft,
        LowerFowardRight
    }

    void Start ()
    {
        player = transform;
        playerMotor = player.GetComponent<CharacterMotor>();
        playerCtrl = player.GetComponent<CharacterController>();
        playerMouse = player.GetComponent<MouseLook>();
        cameraMouse = Camera.main.GetComponent<MouseLook>();
    }

    void Update()
    {
        // if player grounded and not grabbing - check for grab
        if(!isGrabbing && playerCtrl.isGrounded)
        {
            grabPollElapsedFrames++;

            // since grab check is constant, limit by number of frames
            if(grabPollElapsedFrames % playerInteraction.grabPollFrames == 0)
            {
                CheckGrab();
                grabPollElapsedFrames = 0;
            }
        }

        if(isInRange || isGrabbing)
        {
            if(Input.GetKeyDown(GrabKey))
            {
                isGrabbing = !isGrabbing;

                if(isGrabbing)
                {
                    StartCoroutine(Grab());
                    isInRange = false;
                }
                else
                {
                    Release();
                }
            }
            else if(isGrabbing && playerPushState != PushState.MovingPlayer)
            {
                if(CheckForDetach())
                {
                    Release();
                    return;
                }

                // refresh collider points dictionary if target has moved
                if(target.position != targetLastPosition || targetLastRotation != target.eulerAngles)
                    dicColliderPoints = GetColliderVertexPositions(target);

                // update state
                targetLastPosition = target.position;
                targetLastRotation = target.eulerAngles;
                
                // Check for player input
                if(Input.GetKey(KeyCode.W) && IsValidMovement(PushState.Pushing)) // move forward
                {
                    playerPushState = PushState.Pushing;
                }
                else if(Input.GetKey(KeyCode.S) && IsValidMovement(PushState.Pulling)) // move back
                {
                    playerPushState = PushState.Pulling;
                }
                else if(Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.D)) // release
                {
                    Release();
                }
                else
                {
                    playerPushState = PushState.None;
                }
            }
        }
    }

    private bool IsValidMovement(PushState state)
    {
        if(playerPushDir == PushDirection.TargetForward || playerPushDir == PushDirection.TargetBack)
            return playerInteraction.canGrabFront || playerInteraction.canGrabBack;
        else if(playerPushDir == PushDirection.TargetRight || playerPushDir == PushDirection.TargetLeft)
            return playerInteraction.canGrabRight || playerInteraction.canGrabLeft;
        
        return false;
    }
    
    void FixedUpdate()
    {
        if(playerPushState == PushState.Pushing || playerPushState == PushState.Pulling)
        {
            GroundCheck();

            // exit early if player can't push
            if(playerPushState == PushState.Pushing && !CanPush())
                return;

            // exit early if player can't pull
            if(playerPushState == PushState.Pulling && !CanPull())
                return;

            Vector3 pushDir = target.forward;

            if(playerPushDir == PushDirection.TargetBack)
            {
                pushDir = -target.forward;
            }
            else if(playerPushDir == PushDirection.TargetRight)
            {
                pushDir = target.right;
            }
            else if(playerPushDir == PushDirection.TargetLeft)
            {
                pushDir = -target.right;
            }

            Vector3 velocity = pushDir * Input.GetAxis("Vertical") * playerMotor.movement.maxForwardSpeed;
            playerCtrl.Move(velocity * Time.deltaTime);
            Vector3 newPos = new Vector3(player.position.x + localGrabPosition.x, target.position.y, player.position.z + localGrabPosition.z);
            target.position = newPos;
        }
    }
    
    void OnGUI()
    {
        if(!isGrabbing && isInRange)
        {
            float textWidth  = 200;
            float textHeight = 30;
            
            float labelX = (Screen.width / 2) - (textWidth / 2);
            float labelY = (Screen.height / 2) - (textHeight / 2);
            
            GUI.Label(new Rect(labelX, labelY, textWidth, textHeight), GrabText, GrabTextStyle);
        }

        if(debug)
        {
            GUI.Label(new Rect(10, 10, 200, 20), string.Format("Is In Range = {0}", isInRange.ToString()));
            GUI.Label(new Rect(10, 30, 200, 20), string.Format("Is Grabbing = {0}", isGrabbing.ToString()));
            GUI.Label(new Rect(10, 50, 200, 20), string.Format("State = {0}", playerPushState.ToString()));
            GUI.Label(new Rect(10, 70, 200, 20), string.Format("Push Dir = {0}", playerPushDir.ToString()));
        }
    }

    private void CheckGrab()
    {
        Vector3 rayStart = player.position + (player.forward * player.GetComponent<Collider>().bounds.extents.z);
        
        RaycastHit hit;
        if(Physics.Raycast(rayStart, player.forward, out hit, GrabDistance, GrabCollision))
        {
            target = hit.transform;
            isInRange = true;

            // check that player can grab object face
            PushDirection pushDir = GetPushDirection();

            if(pushDir == PushDirection.TargetForward && playerInteraction.canGrabBack)
                return;
            else if(pushDir == PushDirection.TargetBack && playerInteraction.canGrabFront)
                return;
            else if(pushDir == PushDirection.TargetRight && playerInteraction.canGrabLeft)
                return;
            else if(pushDir == PushDirection.TargetLeft && playerInteraction.canGrabRight)
                return;
        }

        target = null;
        isInRange = false;
    }
    
    /// <summary>
    /// Check if target has broken tolerable Y movement or if player turned too far (> 90 degrees)
    /// </summary>
    /// <returns><c>true</c>, if for detach was checked, <c>false</c> otherwise.</returns>
    private bool CheckForDetach()
    {
        // get y difference
        float targetYDelta = Mathf.Abs((target.position.y - player.position.y) - localGrabPosition.y);
        
        // get target push direction
        Vector3 pushDir = target.forward;
        if(playerPushDir == PushDirection.TargetBack)
            pushDir = -target.forward;
        else if(playerPushDir == PushDirection.TargetRight)
            pushDir = target.right;
        else if(playerPushDir == PushDirection.TargetLeft)
            pushDir = -target.right;
        
        // check if either threshold broken
        if(targetYDelta > YBreak || Vector3.Dot(pushDir, player.forward) < 0)
        {
            return true;
        }
        return false;
    }

    private bool CanPush()
    {
        // initialize vectors
        Vector3 lowerBackLeft = dicColliderPoints[ColliderBoundsVertex.LowerBackLeft];
        Vector3 lowerBackRight = dicColliderPoints[ColliderBoundsVertex.LowerBackRight];
        Vector3 lowerForwardLeft = dicColliderPoints[ColliderBoundsVertex.LowerForwardLeft];
        Vector3 lowerForwardRight = dicColliderPoints[ColliderBoundsVertex.LowerFowardRight];
        Vector3 upperBackLeft = dicColliderPoints[ColliderBoundsVertex.UpperBackLeft];
        Vector3 upperBackRight = dicColliderPoints[ColliderBoundsVertex.UpperBackRight];
        Vector3 upperForwardLeft = dicColliderPoints[ColliderBoundsVertex.UpperForwardLeft];
        Vector3 upperForwardRight = dicColliderPoints[ColliderBoundsVertex.UpperForwardRight];
        Vector3 horizontalDirection = Vector3.zero;
        Vector3 verticalDirection = (upperBackLeft - lowerBackLeft).normalized;
        Vector3 rayVector = Vector3.zero;
        Vector3 pushDir = target.forward;
        Vector3 start = Vector3.zero;

        // get height and width
        float height = (upperBackLeft - lowerBackLeft).magnitude - (SkinWidth * 2);
        float width = 0;

        if(playerPushDir == PushDirection.TargetForward)
        {
            width = (lowerBackRight - lowerBackLeft).magnitude - SkinWidth * 2;
            horizontalDirection = (lowerBackRight - lowerBackLeft).normalized;
            pushDir = target.forward;
            start = lowerForwardLeft;
        }
        else if(playerPushDir == PushDirection.TargetBack)
        {
            width = (lowerBackRight - lowerBackLeft).magnitude - SkinWidth * 2;
            horizontalDirection = (lowerBackLeft - lowerBackRight).normalized;
            pushDir = -target.forward;
            start = lowerBackRight;
        }
        else if(playerPushDir == PushDirection.TargetRight)
        {
            width = (lowerForwardLeft - lowerBackLeft).magnitude - SkinWidth * 2;
            horizontalDirection = (lowerBackLeft - lowerForwardLeft).normalized;
            pushDir = target.right;
            start = lowerForwardRight;
        }
        else if(playerPushDir == PushDirection.TargetLeft)
        {
            width = (lowerForwardLeft - lowerBackLeft).magnitude - SkinWidth * 2;
            horizontalDirection = (lowerForwardLeft - lowerBackLeft).normalized;
            pushDir = -target.right;
            start = lowerBackLeft;
        }
        start += (horizontalDirection * SkinWidth) + (verticalDirection * SkinWidth);

        // get distance between rays
        float distanceBetweenHorizontalRays = width / (PushRays - 1);
        float distanceBetweenVerticalRays = height / (PushRays - 1);

        // perform raycasts
        for(int i = 0; i<PushRays; i++)
        {
            for(int j=0; j<PushRays; j++)
            {
                rayVector =
                    start +                                                     // starting point
                    (horizontalDirection * distanceBetweenHorizontalRays * i) + // plus local X or Z
                    (verticalDirection * distanceBetweenVerticalRays * j);      // plus local Y
                
                Debug.DrawRay(rayVector, pushDir * PushStopDistance);
                if(Physics.Raycast(rayVector, pushDir, PushStopDistance, PushCollisionLayers))
                {
                    return false;
                }
            }
        }
        
        return true;
    }

    private bool CanPull()
    {
        // initialize vectors
        Vector3 lowerBackLeft = dicColliderPoints[ColliderBoundsVertex.LowerBackLeft];
        Vector3 lowerBackRight = dicColliderPoints[ColliderBoundsVertex.LowerBackRight];
        Vector3 lowerForwardLeft = dicColliderPoints[ColliderBoundsVertex.LowerForwardLeft];
        Vector3 lowerForwardRight = dicColliderPoints[ColliderBoundsVertex.LowerFowardRight];
        Vector3 upperBackLeft = dicColliderPoints[ColliderBoundsVertex.UpperBackLeft];
        Vector3 upperBackRight = dicColliderPoints[ColliderBoundsVertex.UpperBackRight];
        Vector3 upperForwardLeft = dicColliderPoints[ColliderBoundsVertex.UpperForwardLeft];
        Vector3 upperForwardRight = dicColliderPoints[ColliderBoundsVertex.UpperForwardRight];
        Vector3 horizontalDirection = Vector3.zero;
        Vector3 verticalDirection = (upperBackLeft - lowerBackLeft).normalized;
        Vector3 rayVector = Vector3.zero;
        Vector3 pullDir = target.forward;
        Vector3 start = Vector3.zero;
        
        // get height and width
        float height = (upperBackLeft - lowerBackLeft).magnitude - (SkinWidth * 2);
        float width = 0;
        
        if(playerPushDir == PushDirection.TargetForward)
        {
            width = (lowerBackRight - lowerBackLeft).magnitude - SkinWidth * 2;
            horizontalDirection = (lowerBackRight - lowerBackLeft).normalized;
            pullDir = -target.forward;
            start = lowerBackLeft;
        }
        else if(playerPushDir == PushDirection.TargetBack)
        {
            width = (lowerBackRight - lowerBackLeft).magnitude - SkinWidth * 2;
            horizontalDirection = (lowerBackLeft - lowerBackRight).normalized;
            pullDir = target.forward;
            start = lowerForwardRight;
        }
        else if(playerPushDir == PushDirection.TargetRight)
        {
            width = (lowerForwardLeft - lowerBackLeft).magnitude - SkinWidth * 2;
            horizontalDirection = (lowerBackLeft - lowerForwardLeft).normalized;
            pullDir = -target.right;
            start = lowerForwardLeft;
        }
        else if(playerPushDir == PushDirection.TargetLeft)
        {
            width = (lowerForwardLeft - lowerBackLeft).magnitude - SkinWidth * 2;
            horizontalDirection = (lowerForwardLeft - lowerBackLeft).normalized;
            pullDir = target.right;
            start = lowerBackRight;
        }
        start += (horizontalDirection * SkinWidth) + (verticalDirection * SkinWidth);
        
        // get distance between rays
        float distanceBetweenHorizontalRays = width / (PushRays - 1);
        float distanceBetweenVerticalRays = height / (PushRays - 1);
        
        // perform raycasts
        for(int i = 0; i<PushRays; i++)
        {
            for(int j=0; j<PushRays; j++)
            {
                rayVector =
                    start +                                                     // starting point
                    (horizontalDirection * distanceBetweenHorizontalRays * i) + // plus local X or Z
                    (verticalDirection * distanceBetweenVerticalRays * j);      // plus local Y
                
                Debug.DrawRay(rayVector, pullDir * PushStopDistance);
                if(Physics.Raycast(rayVector, pullDir, PushStopDistance, PushCollisionLayers))
                {
                    return false;
                }
            }
        }
        
        return true;
    }

    private void GroundCheck()
    {
        Vector3 lowerBackLeft = dicColliderPoints[ColliderBoundsVertex.LowerBackLeft];
        Vector3 lowerBackRight = dicColliderPoints[ColliderBoundsVertex.LowerBackRight];
        Vector3 lowerForwardLeft = dicColliderPoints[ColliderBoundsVertex.LowerForwardLeft];
        Vector3 horizontalDirection = (lowerBackRight - lowerBackLeft).normalized;
        Vector3 verticalDirection = (lowerForwardLeft - lowerBackLeft).normalized;
        Vector3 rayVector = Vector3.zero;

        float width = (lowerBackRight - lowerBackLeft).magnitude;
        float length = (lowerForwardLeft - lowerBackLeft).magnitude;
        float distanceBetweenHorizontalRays = width / (GroundRays - 1);
        float distanceBetweenVerticalRays = length / (GroundRays - 1);
        bool isGrounded = false;

        for(int i=0; i<GroundRays; i++)
        {
            for(int j=0; j<GroundRays; j++)
            {
                rayVector = 
                    lowerBackLeft +                                             // starting point
                    (horizontalDirection * distanceBetweenHorizontalRays * i) + // plus local X
                    (verticalDirection * distanceBetweenVerticalRays * j) +     // plus local Z
                    Vector3.up * SkinWidth; // plus up to fight against gravity pushing into floor
                
                Debug.DrawRay(rayVector, Vector3.down * GroundCheckDistance);
                if(Physics.Raycast(rayVector, Vector3.down, GroundCheckDistance, GroundCollisionLayers))
                {
                    isGrounded = true;
                    break;
                }
            }
            
            if(isGrounded)
            {
                break;
            }
        }
        
        if(!isGrounded)
        {
            // Box no longer on ground, drop it
            target.GetComponent<Rigidbody>().isKinematic = false;
            target.GetComponent<Rigidbody>().useGravity = true;
            Release();
        }
    }

    private PushDirection GetPushDirection()
    {
        float dotForward = Vector3.Dot(player.forward, target.forward);
        float dotBack = Vector3.Dot(player.forward, -target.forward);
        float dotRight = Vector3.Dot(player.forward, target.right);
        float dotLeft = Vector3.Dot(player.forward, -target.right);

        if(dotForward > dotBack && dotForward > dotRight && dotForward > dotLeft)
            return PushDirection.TargetForward;
        else if(dotBack > dotForward && dotBack > dotRight && dotBack > dotLeft)
            return PushDirection.TargetBack;
        else if(dotRight > dotForward && dotRight > dotBack && dotRight > dotLeft)
            return PushDirection.TargetRight;
        else if(dotLeft > dotForward && dotLeft > dotBack && dotLeft > dotRight)
            return PushDirection.TargetLeft;

        return PushDirection.TargetForward;
    }

    private IEnumerator Grab()
    {
        // Disable player movement
        playerMotor.canControl = false;

        // get push direction
        playerPushDir = GetPushDirection();

        // populate collider bounding points dictionary
        dicColliderPoints = GetColliderVertexPositions(target);

        Vector3 startPos = player.position;
        Vector3 endPos = Vector3.zero;
        Vector3 temp = Vector3.zero;

        float zExtents = (dicColliderPoints[ColliderBoundsVertex.LowerForwardLeft] - dicColliderPoints[ColliderBoundsVertex.LowerBackLeft]).magnitude / 2;
        float xExtents = (dicColliderPoints[ColliderBoundsVertex.LowerBackRight] - dicColliderPoints[ColliderBoundsVertex.LowerBackLeft]).magnitude / 2; 

        if(playerPushDir == PushDirection.TargetForward)
        {
            temp = target.position - (target.forward * (zExtents + (playerCtrl.radius / 2) + GrabDistance));
        }
        else if(playerPushDir == PushDirection.TargetBack)
        {
            temp = target.position + (target.forward * (zExtents + (playerCtrl.radius / 2) + GrabDistance));
        }
        else if(playerPushDir == PushDirection.TargetRight)
        {
            temp = target.position - (target.right * (xExtents + (playerCtrl.radius / 2) + GrabDistance));
        }
        else if(playerPushDir == PushDirection.TargetLeft)
        {
            temp = target.position + (target.right * (xExtents + (playerCtrl.radius / 2) + GrabDistance));
        }

        endPos = new Vector3(temp.x, player.position.y, temp.z);

        // move player
        float startTime = Time.time;
        playerPushState = PushState.MovingPlayer;
        while(Time.time < startTime + GrabMoveTime)
        {
            Vector3 lastPos = player.position;
            Vector3 newPos = Vector3.Lerp(startPos, endPos, (Time.time - startTime)/GrabMoveTime) - lastPos;
            
            playerCtrl.Move(newPos);
            yield return null;
        }

        // set state
        playerPushState = PushState.None;
        localGrabPosition = target.position - player.position;
        targetLastPosition = target.position;
        targetLastRotation = target.eulerAngles;

        // smooth movement
        target.GetComponent<Rigidbody>().isKinematic = true;

        // set override push/pull parameters
        PushPullObject ppObj = target.GetComponent<PushPullObject>();
        if(ppObj != null)
        {
            overrideParameters = ppObj.parameters;
        }

        // Slow player movement
        SlowSpeed();
    }

    private void Release()
    {
        // Reset State Variables
        isGrabbing = false;
        playerPushState = PushState.None;

        // Make target subject to physics again
        target.GetComponent<Rigidbody>().isKinematic = false;

        // Restore player movement speed
        RestoreSpeed();

        // clear override push/pull parameters
        overrideParameters = null;

        // Enable player movement
        playerMotor.canControl = true;
    }

    private void SlowSpeed()
    {
        // Adjust movement speed
        playerMotor.movement.maxForwardSpeed *= PlayerMoveSpeedPercent;
        playerMotor.movement.maxSidewaysSpeed *= PlayerMoveSpeedPercent;
        playerMotor.movement.maxBackwardsSpeed *= PlayerMoveSpeedPercent;
        
        // Adjust mouse speed
        playerMouse.sensitivityX *= PlayerMouseSpeedPercent;
        cameraMouse.sensitivityY *= PlayerMouseSpeedPercent;
        
        // Turn off jumping
        playerMotor.jumping.enabled = false;
    }
    
    private void RestoreSpeed()
    {
        // Adjust movement speed
        playerMotor.movement.maxForwardSpeed /= PlayerMoveSpeedPercent;
        playerMotor.movement.maxSidewaysSpeed /= PlayerMoveSpeedPercent;
        playerMotor.movement.maxBackwardsSpeed /= PlayerMoveSpeedPercent;
        
        // Adjust mouse speed
        playerMouse.sensitivityX /= PlayerMouseSpeedPercent;
        cameraMouse.sensitivityY /= PlayerMouseSpeedPercent;
        
        // Turn on jumping
        playerMotor.jumping.enabled = true;
    }

    /// <summary>
    /// Gets the collider vertex positions.
    /// Based on code by Eric5h5 at:
    /// http://forum.unity3d.com/threads/get-vertices-of-box-collider.89301/
    /// </summary>
    /// <returns>The collider vertex positions.</returns>
    /// <param name="targetTrans">Target trans.</param>
    private Dictionary<ColliderBoundsVertex, Vector3> GetColliderVertexPositions(Transform targetTrans)
    {
        // get initial values and rotate target
        Matrix4x4 thisMatrix = targetTrans.localToWorldMatrix;
        Quaternion storedRotation = targetTrans.rotation;
        targetTrans.rotation = Quaternion.identity;

        // get extents and adjust for scaling
        Vector3 extents = targetTrans.GetComponent<Collider>().bounds.extents;
        float extentX = extents.x / targetTrans.localScale.x;
        float extentY = extents.y / targetTrans.localScale.y;
        float extentZ = extents.z / targetTrans.localScale.z;
        extents = new Vector3(extentX, extentY, extentZ);

        // build dictionary of points
        Dictionary<ColliderBoundsVertex, Vector3> dicVertexPoint = new Dictionary<ColliderBoundsVertex, Vector3>();

        dicVertexPoint.Add(ColliderBoundsVertex.UpperForwardRight, thisMatrix.MultiplyPoint3x4(extents));
        dicVertexPoint.Add(ColliderBoundsVertex.UpperForwardLeft, thisMatrix.MultiplyPoint3x4(new Vector3(-extents.x, extents.y, extents.z)));
        dicVertexPoint.Add(ColliderBoundsVertex.UpperBackRight, thisMatrix.MultiplyPoint3x4(new Vector3(extents.x, extents.y, -extents.z)));
        dicVertexPoint.Add(ColliderBoundsVertex.UpperBackLeft, thisMatrix.MultiplyPoint3x4(new Vector3(-extents.x, extents.y, -extents.z)));
        dicVertexPoint.Add(ColliderBoundsVertex.LowerFowardRight, thisMatrix.MultiplyPoint3x4(new Vector3(extents.x, -extents.y, extents.z)));
        dicVertexPoint.Add(ColliderBoundsVertex.LowerForwardLeft, thisMatrix.MultiplyPoint3x4(new Vector3(-extents.x, -extents.y, extents.z)));
        dicVertexPoint.Add(ColliderBoundsVertex.LowerBackRight, thisMatrix.MultiplyPoint3x4(new Vector3(extents.x, -extents.y, -extents.z)));
        dicVertexPoint.Add(ColliderBoundsVertex.LowerBackLeft, thisMatrix.MultiplyPoint3x4(-extents));

        // restore rotation and return dictionary
        targetTrans.rotation = storedRotation;
        return dicVertexPoint;
    }

    [Serializable]
    public class PlayerInteraction
    {
        public bool canGrabFront;
        public bool canGrabBack;
        public bool canGrabRight;
        public bool canGrabLeft;

        public KeyCode grabKey = KeyCode.E;    // What keypress grabs the object when in range?
        public float grabDistance = .5f;       // What distance the player can grab the object from? (raycast distance)
        public float grabMoveTime = .5f;       // How long does it take to center and move the player after grabbing?
        public int grabPollFrames = 3;         // How many frames between each CheckGrab() raycast?
        public LayerMask grabLayers;           // Which layers contain grabable objects?
        public string grabText = string.Empty; // What should the grab text say when player is in range?
        public GUIStyle grabTextStyle;         // What should the grab text look like?
    }
}
