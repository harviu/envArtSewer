using UnityEngine;
using System.Collections;

public class Climb : MonoBehaviour
{
	// Constants
	private const string TAG_PLAYER = "Player";
	private const string TAG_AXIS_V = "Vertical";
	
	// Public Variables (Inspector)
	public float climbSpeed        = 5.0f; // Speed that player climbs at
	public float attachTime        = 0.1f; // Time in seconds to center player with ladder
	public float detachTime        = 0.2f; // Time in seconds to detach player from ladder
	public float detachDistanceTop = 1.0f; // Distance from climb trigger player will be pushed on detach
	
	// Private Variables
	private ClimbState playerState = ClimbState.NONE; // Player climb status
	private bool isInTrigger = false;   // Is player in climb trigger?
	private float t_lerp   = 0;         // Progress for moving player via lerp
	private Vector3 start;                // Player start position for lerping
	private Vector3 end;                // Player end position for lerping
	private Transform player;            // Player transform
	private CharacterController playerCtrl;  // Player controller
	private CharacterMotor playerMotor; // Player motor
	
	private enum ClimbState
	{
		NONE,
		CENTERING,
		CLIMBING,
		DISMOUNTING
	}
	
	void Update ()
	{
		if(playerState == ClimbState.NONE)
		{
			if(isInTrigger)
			{
				if(Input.GetAxis(TAG_AXIS_V) > 0)       // Player moving forward
				{
					// Attach player to ladder
					playerMotor.grounded = false;       // (prevent jump if dropping off ladder)
					playerMotor.enabled = false;        // disable normal movement
					playerState = ClimbState.CENTERING; // center player with ladder
				}
			}
		}
		else if(playerState == ClimbState.CENTERING)
		{
			if(t_lerp == 0)
			{
				// initialize values
				start = player.position;
				end = new Vector3(transform.position.x, player.position.y, transform.position.z);
			}
			
			if(t_lerp < 1)
			{
				// center player
				t_lerp += Time.deltaTime / attachTime;
				player.position = Vector3.Lerp(start, end, t_lerp);
			}
			else
			{
				// finish centering
				t_lerp = 0;
				playerState = ClimbState.CLIMBING;
			}
		}
		else if(playerState == ClimbState.CLIMBING)
		{
			CollisionFlags playerCol = CollisionFlags.None;
			
			if(Input.GetKey (KeyCode.W)) // Player climbing up
			{
				playerCol = playerCtrl.Move(new Vector3(0, Time.deltaTime * climbSpeed, 0));
			}
			else if(Input.GetKey(KeyCode.S)) // Player climbing down
			{
				playerCol = playerCtrl.Move(new Vector3(0, Time.deltaTime * -climbSpeed, 0));
			}
			
			if(Input.GetKeyDown(KeyCode.Space)) // Player "let go" of ladder
			{
				Detach(ClimbState.NONE);
			}
			
			if((playerCol & CollisionFlags.CollidedBelow) != 0) // player touched ground - release
			{
				Detach(ClimbState.NONE);
			}
		}
		else if(playerState == ClimbState.DISMOUNTING)
		{
			if(t_lerp < 1)
			{
				// Move player off ladder
				t_lerp += Time.deltaTime / detachTime;
				Vector3 diff = Vector3.Lerp(start, end, t_lerp) - player.position;
				playerCtrl.Move(diff);
			}
			else
			{
				// Enable player movement
				t_lerp = 0;
				
				Detach(ClimbState.NONE);
			}
		}
	}
	
	void OnTriggerEnter(Collider other)
	{
		if(other.tag == TAG_PLAYER)
		{
			// Set values
			isInTrigger = true;
			player = other.transform;
			playerMotor = other.GetComponent<CharacterMotor>();
			playerCtrl = other.GetComponent<CharacterController>();
			
			// cancel any existing velocity
			playerMotor.movement.velocity = Vector3.zero;
		}
	}
	
	void OnTriggerExit(Collider other)
	{
		if(other.tag == TAG_PLAYER)
		{
			isInTrigger = false;
			
			if(playerState == ClimbState.CLIMBING)
			{
				if(player.position.y > transform.position.y) // player climbed off top
					Detach(ClimbState.DISMOUNTING);
				else // player dropped off bottom
					Detach(ClimbState.NONE);
			}
		}
	}
	
	private void Detach(ClimbState climbState)
	{
		// Set player status
		playerState = climbState;
		
		if(climbState == ClimbState.DISMOUNTING)
		{
			// set start and end
			start = player.position;
			// have player dismount forward if climbing off top of ladder
			end = player.position + (-transform.forward * detachDistanceTop);
		}
		else
		{
			// Clear player values
			playerMotor.enabled = true;
			playerMotor = null;
			playerCtrl = null;
		}
		
		// Reset values to initial state
		isInTrigger = false;
		t_lerp = 0;
	}
}