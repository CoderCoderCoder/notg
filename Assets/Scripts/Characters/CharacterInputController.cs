using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Handle everything related to controlling the character. Interact with both the Character (visual, animation) and CharacterCollider
/// </summary>
public class CharacterInputController : Agent
{
    static int s_DeadHash = Animator.StringToHash("Dead");
    static int s_RunStartHash = Animator.StringToHash("runStart");
    static int s_MovingHash = Animator.StringToHash("Moving");
    static int s_JumpingHash = Animator.StringToHash("Jumping");
    static int s_JumpingSpeedHash = Animator.StringToHash("JumpSpeed");
    static int s_SlidingHash = Animator.StringToHash("Sliding");

    public TrackManager trackManager;
    public Character character;
    public CharacterCollider characterCollider;
    public GameObject blobShadow;
    public RayPerception rayPerception;
    public float laneChangeSpeed = 1.0f;
        
    public int maxLife = 3;

    public Consumable inventory;

    public int coins { get { return m_Coins; } set { m_Coins = value; } }
    public int premium { get { return m_Premium; } set { m_Premium = value; } }
    public int currentLife
    {
        get { return m_CurrentLife; }
        set
        {
            m_CurrentLife = value;
        }
    }
    public List<Consumable> consumables { get { return m_ActiveConsumables; } }
    public bool isJumping { get { return m_Jumping; } }
    public bool isSliding { get { return m_Sliding; } }

    [Header("Controls")]
    public bool isVerticalMovementEnabled = false;

    public float jumpLength = 2.0f;     // Distance jumped
    public float jumpHeight = 1.2f;

    public float slideLength = 2.0f;

    [Header("Sounds")]
    public AudioClip slideSound;
    public AudioClip powerUpUseSound;
    public AudioSource powerupSource;

    protected int m_Coins;
    protected int m_Premium;
    protected int m_CurrentLife;

    protected List<Consumable> m_ActiveConsumables = new List<Consumable>();

    protected int m_ObstacleLayer;

    protected bool m_IsInvincible;

    protected float m_JumpStart;
    protected bool m_Jumping;

    protected bool m_Sliding;
    protected float m_SlideStart;

    protected AudioSource m_Audio;

    protected int m_CurrentLane = k_StartingLane;
    protected Vector3 m_TargetPosition = Vector3.zero;

    protected readonly Vector3 k_StartingPosition = Vector3.forward * 2f;

    protected const int k_StartingLane = 1;
    protected const float k_GroundingSpeed = 80f;
    protected const float k_ShadowRaycastDistance = 100f;
    protected const float k_ShadowGroundOffset = 0.01f;
    protected const float k_TrackSpeedToJumpAnimSpeedRatio = 0.6f;
    protected const float k_TrackSpeedToSlideAnimSpeedRatio = 0.9f;
    public Dictionary<string, float> obstacleMapping;

    private const int NumberOfFeatures = 12;
    public float[] featureVector;

    protected void Awake()

    {
        m_Premium = 0;
        m_CurrentLife = 0;
        m_Sliding = false;
        m_SlideStart = 0.0f;
        obstacleMapping = new Dictionary<string, float>();
        obstacleMapping.Add("Pickup(Clone)", 0);
        obstacleMapping.Add("ObstacleBin(Clone)", 1);
        obstacleMapping.Add("ObstacleWheelyBin(Clone)", 1);
        obstacleMapping.Add("ObstacleRoadworksCone(Clone)", 1);
        featureVector = new float[NumberOfFeatures];
        ResetFeatures();
    }

#if !UNITY_STANDALONE
    protected Vector2 m_StartingTouch;
	protected bool m_IsSwiping = false;
#endif

    // Cheating functions, use for testing
    public void CheatInvincible(bool invincible)
    {
        m_IsInvincible = invincible;
    }

    public bool IsCheatInvincible()
    {
        return m_IsInvincible;
    }

    public void Init()
    {
        transform.position = k_StartingPosition;
        m_TargetPosition = Vector3.zero;

        m_CurrentLane = k_StartingLane;
        characterCollider.transform.localPosition = Vector3.zero;

        currentLife = maxLife;

        m_Audio = GetComponent<AudioSource>();

        m_ObstacleLayer = 1 << LayerMask.NameToLayer("Obstacle");
    }

    // Called at the beginning of a run or rerun
    public void Begin()
    {
        character.animator.SetBool(s_DeadHash, false);

        characterCollider.Init();

        m_ActiveConsumables.Clear();
    }

    public void End()
    {
        CleanConsumable();
    }

    public void CleanConsumable()
    {
        for (int i = 0; i < m_ActiveConsumables.Count; ++i)
        {
            m_ActiveConsumables[i].Ended(this);
            Destroy(m_ActiveConsumables[i].gameObject);
        }

        m_ActiveConsumables.Clear();
    }

    public void StartRunning()
    {
        if (character.animator)
        {
            character.animator.Play(s_RunStartHash);
            character.animator.SetBool(s_MovingHash, true);
        }
    }

    public void StopMoving()
    {
        trackManager.StopMove();
        if (character.animator)
        {
            character.animator.SetBool(s_MovingHash, false);
        }
    }

    private const float RaycastLength = 30f;

    public float CalculateNormalDistance(RaycastHit hit)
    {
        float distance = Vector3.Distance(hit.transform.position, transform.position);
        //Debug.Log(hit.point + " Hit " + hit.collider.gameObject.name + " Distance " + hit.distance + " calculated distance " + distance);
        if (hit.collider.gameObject.name.Equals("Pickup(Clone)")) return 1f;


        return (RaycastLength - distance) / RaycastLength;   // max - cur / max - min
    }

    private void CalculateFeatureVector()
    {
        RaycastHit hit;

        float lowRayHeight = 0.3f;
        float highRayHeight = 1.2f;

        float leftWidth = -1.5f;
        float centerWidth = 0f;
        float rightWidth = 1.5f;

        Vector3 lowRayStraight = new Vector3(centerWidth, transform.position.y + lowRayHeight, transform.position.z);
        Vector3 lowRayLeft = new Vector3(leftWidth, transform.position.y + lowRayHeight, transform.position.z);
        Vector3 lowRayRight = new Vector3(rightWidth, transform.position.y + lowRayHeight, transform.position.z);

        Vector3 highRayStraight = new Vector3(centerWidth, transform.position.y + highRayHeight, transform.position.z);
        Vector3 HighRayLeft = new Vector3(leftWidth, transform.position.y + highRayHeight, transform.position.z);
        Vector3 HighRayRight = new Vector3(rightWidth, transform.position.y + highRayHeight, transform.position.z);

        Vector3 ahead = transform.forward;
        ahead = ahead.normalized;
        float rayLength = 30.0f;

        // IF NO RAY THEN USE 1

        // HIGH LEFT
        if (Physics.Raycast(HighRayLeft, ahead, out hit, rayLength))
        {
            //print ("Left "+hit.transform.position);
            //print ("Left High: " + obstacleMapping [hit.collider.gameObject.name]);

            //if (hit.collider.gameObject.name.Equals("Pickup(Clone)"))
            //{
            //    featureVector.Add(0f);
            //} else {
            //    featureVector.Add(1f);
            //}
            featureVector[0] = CalculateNormalDistance(hit);
        }
        else
        {
            //featureVector.Add (0.0f);
            featureVector[0] = 1.0f;
        }

        // HIGH RIGHT
        if (Physics.Raycast(HighRayRight, ahead, out hit, rayLength))
        {
            //print ("Right "+hit.transform.position);
            //print ("Right High: "+ obstacleMapping[hit.collider.gameObject.name]);

            //if (hit.collider.gameObject.name.Equals("Pickup(Clone)"))
            //{
            //    featureVector.Add(0f);
            //}
            //else
            //{
            //    featureVector.Add(1f);
            //}
            featureVector[1] = CalculateNormalDistance(hit);
        }
        else
        {
            //featureVector.Add (0.0f);
            featureVector[1] = 1.0f;
        }

        // LOW LEFT
        if (Physics.Raycast(lowRayLeft, ahead, out hit, rayLength))
        {
            //print ("Left "+hit.transform.position);
            //print ("Left low: "+ obstacleMapping[hit.collider.gameObject.name]);

            //if (hit.collider.gameObject.name.Equals("Pickup(Clone)"))
            //{
            //    featureVector.Add(0f);
            //}
            //else
            //{
            //    featureVector.Add(1f);
            //}
            featureVector[2] = CalculateNormalDistance(hit);
            //featureVector.Add (obstacleMapping [hit.collider.gameObject.name]);
        }
        else
        {
            //featureVector.Add (0.0f);
            featureVector[2] = 1.0f;
        }

        // LOW RIGHT
        if (Physics.Raycast(lowRayRight, ahead, out hit, rayLength))
        {
            //print ("Right "+hit.transform.position);
            //print ("Right low: "+ obstacleMapping[hit.collider.gameObject.name]);

            //if (hit.collider.gameObject.name.Equals("Pickup(Clone)"))
            //{
            //    featureVector.Add(0f);
            //}
            //else
            //{
            //    featureVector.Add(1f);
            //}
            featureVector[3] = CalculateNormalDistance(hit);
            //featureVector.Add (obstacleMapping [hit.collider.gameObject.name]);
        }
        else
        {
            //featureVector.Add (0.0f);
            featureVector[3] = 1.0f;
        }

        // HIGH STRAIGHT
        if (Physics.Raycast(highRayStraight, ahead, out hit, rayLength))
        {
            //print ("heah "+hit.transform.position);
            //print ("HIGH: "+ obstacleMapping[hit.collider.gameObject.name]);

            //if (hit.collider.gameObject.name.Equals("Pickup(Clone)"))
            //{
            //    featureVector.Add(0f);
            //}
            //else
            //{
            //    featureVector.Add(1f);
            //}
            featureVector[4] = CalculateNormalDistance(hit);
            //featureVector.Add (obstacleMapping [hit.collider.gameObject.name]);
        }
        else
        {
            //featureVector.Add (0.0f);
            featureVector[4] = 1.0f;
        }

        // LOW STRAIGHT
        if (Physics.Raycast(lowRayStraight, ahead, out hit, rayLength))
        {
            //print ("heah "+hit.transform.position);
            //print ("LOW: "+ obstacleMapping[hit.collider.gameObject.name]);

            //if (hit.collider.gameObject.name.Equals("Pickup(Clone)"))
            //{
            //    featureVector.Add(0f);
            //}
            //else
            //{
            //    featureVector.Add(1f);
            //}
            featureVector[5] = CalculateNormalDistance(hit);
            //featureVector.Add (obstacleMapping [hit.collider.gameObject.name]);
        }
        else
        {
            //featureVector.Add (0.0f);
            featureVector[5] = 1.0f;
        }
    }

    protected void Update()
    {

        // OUR RAYCASTING START
        //CalculateFeatureVector();
        // OUR RAYCASTING END

#if UNITY_EDITOR || UNITY_STANDALONE
        // Use key input in editor or standalone
        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            ChangeLane(-1);
        }
        else if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            ChangeLane(1);
        }
        else if (Input.GetKeyDown(KeyCode.UpArrow) && isVerticalMovementEnabled)
        {
            Jump();
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow) && isVerticalMovementEnabled)
        {
            if (!m_Sliding)
                Slide();
        }
#else
        // Use touch input on mobile
        if (Input.touchCount == 1)
        {
			if(m_IsSwiping)
			{
				Vector2 diff = Input.GetTouch(0).position - m_StartingTouch;

				// Put difference in Screen ratio, but using only width, so the ratio is the same on both
                // axes (otherwise we would have to swipe more vertically...)
				diff = new Vector2(diff.x/Screen.width, diff.y/Screen.width);

				if(diff.magnitude > 0.01f) //we set the swip distance to trigger movement to 1% of the screen width
				{
					if(Mathf.Abs(diff.y) > Mathf.Abs(diff.x))
					{
						if(diff.y < 0)
						{
							Slide();
						}
						else
						{
							Jump();
						}
					}
					else
					{
						if(diff.x < 0)
						{
							ChangeLane(-1);
						}
						else
						{
							ChangeLane(1);
						}
					}
						
					m_IsSwiping = false;
				}
            }

        	// Input check is AFTER the swip test, that way if TouchPhase.Ended happen a single frame after the Began Phase
			// a swipe can still be registered (otherwise, m_IsSwiping will be set to false and the test wouldn't happen for that began-Ended pair)
			if(Input.GetTouch(0).phase == TouchPhase.Began)
			{
				m_StartingTouch = Input.GetTouch(0).position;
				m_IsSwiping = true;
			}
			else if(Input.GetTouch(0).phase == TouchPhase.Ended)
			{
				m_IsSwiping = false;
			}
        }
#endif

        Vector3 verticalTargetPosition = m_TargetPosition;

        if (m_Sliding)
        {
            // Slide time isn't constant but the slide length is (even if slightly modified by speed, to slide slightly further when faster).
            // This is for gameplay reason, we don't want the character to drasticly slide farther when at max speed.
            float correctSlideLength = slideLength * (1.0f + trackManager.speedRatio);
            float ratio = (trackManager.worldDistance - m_SlideStart) / correctSlideLength;
            if (ratio >= 1.0f)
            {
                // We slid to (or past) the required length, go back to running
                StopSliding();
            }
        }

        if (m_Jumping)
        {
            if (trackManager.isMoving)
            {
                // Same as with the sliding, we want a fixed jump LENGTH not fixed jump TIME. Also, just as with sliding,
                // we slightly modify length with speed to make it more playable.
                float correctJumpLength = jumpLength * (1.0f + trackManager.speedRatio);
                float ratio = (trackManager.worldDistance - m_JumpStart) / correctJumpLength;
                if (ratio >= 1.0f)
                {
                    m_Jumping = false;
                    character.animator.SetBool(s_JumpingHash, false);
                }
                else
                {
                    verticalTargetPosition.y = Mathf.Sin(ratio * Mathf.PI) * jumpHeight;
                }
            }
            else if (!AudioListener.pause)//use AudioListener.pause as it is an easily accessible singleton & it is set when the app is in pause too
            {
                verticalTargetPosition.y = Mathf.MoveTowards(verticalTargetPosition.y, 0, k_GroundingSpeed * Time.deltaTime);
                if (Mathf.Approximately(verticalTargetPosition.y, 0f))
                {
                    character.animator.SetBool(s_JumpingHash, false);
                    m_Jumping = false;
                }
            }
        }

        characterCollider.transform.localPosition = Vector3.MoveTowards(characterCollider.transform.localPosition, verticalTargetPosition, laneChangeSpeed * Time.deltaTime);

        // Put blob shadow under the character.
        RaycastHit hit;
        if (Physics.Raycast(characterCollider.transform.position + Vector3.up, Vector3.down, out hit, k_ShadowRaycastDistance, m_ObstacleLayer))
        {
            blobShadow.transform.position = hit.point + Vector3.up * k_ShadowGroundOffset;
        }
        else
        {
            Vector3 shadowPosition = characterCollider.transform.position;
            shadowPosition.y = k_ShadowGroundOffset;
            blobShadow.transform.position = shadowPosition;
        }
    }

    public void Jump()
    {
        if (!m_Jumping)
        {
            if (m_Sliding)
                StopSliding();

            float correctJumpLength = jumpLength * (1.0f + trackManager.speedRatio);
            m_JumpStart = trackManager.worldDistance;
            float animSpeed = k_TrackSpeedToJumpAnimSpeedRatio * (trackManager.speed / correctJumpLength);

            character.animator.SetFloat(s_JumpingSpeedHash, animSpeed);
            character.animator.SetBool(s_JumpingHash, true);
            m_Audio.PlayOneShot(character.jumpSound);
            m_Jumping = true;
        }
    }

    public void Slide()
    {
        if (!m_Sliding && !m_Jumping)
        {
            float correctSlideLength = slideLength * (1.0f + trackManager.speedRatio);
            m_SlideStart = trackManager.worldDistance;
            float animSpeed = k_TrackSpeedToJumpAnimSpeedRatio * (trackManager.speed / correctSlideLength);

            character.animator.SetFloat(s_JumpingSpeedHash, animSpeed);
            character.animator.SetBool(s_SlidingHash, true);
            m_Audio.PlayOneShot(slideSound);
            m_Sliding = true;

            characterCollider.Slide(true);
        }
    }

    public void StopSliding()
    {
        if (m_Sliding)
        {
            character.animator.SetBool(s_SlidingHash, false);
            m_Sliding = false;

            characterCollider.Slide(false);
        }
    }

    public void ChangeLane(int direction)
    {
        if (!trackManager.isMoving)
            return;

        int targetLane = m_CurrentLane + direction;

        if (targetLane < 0 || targetLane > 2)
            // Ignore, we are on the borders.
            return;

        m_CurrentLane = targetLane;
        m_TargetPosition = new Vector3((m_CurrentLane - 1) * trackManager.laneOffset, 0, 0);
    }

    public void UseInventory()
    {
        if (inventory != null && inventory.CanBeUsed(this))
        {
            UseConsumable(inventory);
            inventory = null;
        }
    }

    public void UseConsumable(Consumable c)
    {
        characterCollider.audio.PlayOneShot(powerUpUseSound);

        for (int i = 0; i < m_ActiveConsumables.Count; ++i)
        {
            if (m_ActiveConsumables[i].GetType() == c.GetType())
            {
                // If we already have an active consumable of that type, we just reset the time
                m_ActiveConsumables[i].ResetTime();
                Destroy(c.gameObject);
                return;
            }
        }

        // If we didn't had one, activate that one 
        c.transform.SetParent(transform, false);
        c.gameObject.SetActive(false);

        m_ActiveConsumables.Add(c);
        c.Started(this);
    }

    #region AI stuff


    public override void InitializeAgent()
    {
        base.InitializeAgent();
        rayPerception = GetComponentInChildren<RayPerception>();
        //Brain characterBrain = FindObjectOfType<Brain>();
        //GiveBrain(characterBrain);
    }

    public override void CollectObservations()
    {
        // 6 DOF raycasts
        const float rayLength = 30.0f;
        const float lowRayHeight = 0.1f;
        const float highRayHeight = 1.2f;

        var angles = new float[] { 30f, 60f, 75f, 90f, 105f, 120f, 150f };

        List<float> highPerceptions = rayPerception.Perceive(rayLength, angles, new string[] { "obstacle", "fish" }, lowRayHeight, 0f);
        List<float> lowPerceptions = rayPerception.Perceive(rayLength, angles, new string[] { "obstacle", "fish" }, highRayHeight, 0f);

        AddVectorObs(highPerceptions);
        AddVectorObs(lowPerceptions);
        //for (int i = 0; i < 6; i++)
        //{
        //    AddVectorObs(featureVector[i]);
        //}
    }

    public override void AgentAction(float[] vectorAction, string textAction)
    {
        int action = Mathf.FloorToInt(vectorAction[0]);
#if !UNITY_EDITOR
        switch(action){
            case 0:
                ChangeLane(-1);
                break;
            case 1:
                ChangeLane(1);
                break;
        }
#endif

        //if (Mathf.Approximately(0f, GetReward())) {
        AddReward(0.1f);
        //}

        if (currentLife <= 0) {
            Debug.Log("Im done");
            Done();
        }
    }

    public override void AgentReset()
    {
        //ResetFeatures();

        ((GameState)GameManager.instance.FindState("Game")).ResetAll();
    }

    public void CoinCollided() {
        AddReward(0.1f);
    }

    public void ObstacleCollided() {
        AddReward(-1f);
    }

    private void ResetFeatures()
    {
        for (int i = 0; i < NumberOfFeatures; i++)
            featureVector[i] = 0f;
    }
#endregion
}
