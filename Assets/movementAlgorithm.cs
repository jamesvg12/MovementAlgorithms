using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;

public class SpaceshipBehaviour : MonoBehaviour
{
    // --- Inspector Fields ---
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotationSpeed = 180f;
    [SerializeField] private float maxForce = 5f;
    [SerializeField] private float mass = 1f;
    [SerializeField] private float slowingRadius = 5f;
    [SerializeField] private LineRenderer lineRenderer; // trail
    [SerializeField] private float trailDuration = 2.0f;
    [SerializeField] private float minPointDistance = 0.1f;

    // Wander parameters
    [SerializeField] private float circleDistance = 2f;
    [SerializeField] private float circleRadius = 1f;
    [SerializeField] private float jitter = 20f;
    [SerializeField] private LineRenderer wanderCircleRenderer;
    [SerializeField] private LineRenderer wanderTargetRenderer;

    // State-based wander parameters
    [SerializeField] private float arrivalThreshold = 0.5f;
    [SerializeField] private Vector2 mapBounds = new Vector2(20f, 15f);

    // Pursuit/Evade parameters
    [SerializeField] private Transform otherShip;
    [SerializeField] private LineRenderer predictionLineRenderer;
    [SerializeField] private bool isSecondaryShip = false; 

    // Path Following parameters
    [SerializeField] private float waypointRadius = 0.3f; // Used for Precise snapping distance
    [SerializeField] private LineRenderer pathRenderer;
    [SerializeField] private LineRenderer waypointMarkersRenderer;
    [SerializeField] private float pathGenerationMargin = 0.08f; 
    [SerializeField] private GameObject waypointPrefab; 
    
    [SerializeField] private float smoothWaypointRadius = 3.0f; // Used for Smooth and Patrol

    // --- Persistent Private Variables ---
    private TMP_Dropdown algorithmDropdown;
    private Vector3 targetPosition;
    private Vector3 velocity = Vector3.zero;
    private float wanderAngle = 0f;

    // Trail management
    private List<Vector3> trailPoints = new List<Vector3>();
    private List<float> pointTimes = new List<float>();

    // Wander points
    private Vector3 wanderCircleCenter;
    private Vector3 wanderTargetPoint;

    // State-based wander
    private enum WanderState { WANDERING, SEEKING }
    private WanderState wanderState = WanderState.WANDERING;
    private Vector3 randomWanderTarget;

    // Path following
    private int currentWaypointIndex = 0;
    private Vector3[] pathPoints;
    private List<GameObject> waypointObjects = new List<GameObject>(); 
    
    // ⭐ NEW VARIABLE: Direction for Patrolling ⭐
    private int pathDirection = 1; // 1 for forward, -1 for backward

    // Prediction visualization
    private Vector3 predictedPosition;

    // --- Unity Lifecycle ---
    void Start()
    {
        targetPosition = transform.position;

        wanderAngle = Random.Range(0f, Mathf.PI * 2f);
        wanderTargetPoint = Vector3.zero;

        GameObject dropdownGO = GameObject.FindWithTag("AlgorithmUI");
        if (dropdownGO != null)
            algorithmDropdown = dropdownGO.GetComponent<TMP_Dropdown>();

        if (algorithmDropdown == null)
            Debug.LogError("Dropdown not found! Ensure the object is tagged 'AlgorithmUI'.");

        if (lineRenderer == null)
            lineRenderer = GetComponent<LineRenderer>();

        if (lineRenderer == null)
            Debug.LogError("Line Renderer not found on spaceship!");

        if (wanderCircleRenderer != null)
        {
            wanderCircleRenderer.positionCount = 0;
            wanderCircleRenderer.loop = true;
        }

        if (wanderTargetRenderer != null)
        {
            wanderTargetRenderer.positionCount = 0;
        }
        
        // FIX: Only the primary ship generates the path at startup.
        if (!isSecondaryShip)
        {
            GeneratePath(); 
        }

        if (predictionLineRenderer != null)
        {
            predictionLineRenderer.positionCount = 0;
        }
    }

    void Update()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        // Strict: Only primary ship reacts to mouse click
        if (!isSecondaryShip && Input.GetMouseButtonDown(0))
        {
            Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mousePosition.z = 0f;
            targetPosition = mousePosition;
        }
        
        // Handle dropdown logic for primary ship
        if (!isSecondaryShip && algorithmDropdown == null)
        {
            UpdateLineTrail(false);
            ScreenWrap();
            return;
        }
        
        string selected;
        
        // Determine the behavior based on ship type
        if (isSecondaryShip)
        {
            // secondaryShip only uses its complementary behavior
            selected = GetComplementaryBehavior(algorithmDropdown?.options[algorithmDropdown.value].text);
        }
        else
        {
            // primaryShip uses the dropdown selection
            selected = algorithmDropdown.options[algorithmDropdown.value].text;
        }

        // Handle visual elements based on primary ship's selection
        if (!isSecondaryShip && otherShip != null)
        {
            bool shouldShowOtherShip = selected == "Pursuit (Basic)" ||
                                        selected == "Pursuit (Improved)" ||
                                        selected == "Evade";
            otherShip.gameObject.SetActive(shouldShowOtherShip);
        }
        
        bool isMoving = true;

        ClearPredictionVisuals();
        // ⭐ UPDATED: Check for all path following modes ⭐
        bool isPathFollowing = selected == "Path Following (Precise)" || 
                               selected == "Path Following (Smooth)" || 
                               selected == "Path Following (Patrol)";
        ClearPathVisuals(!isPathFollowing); 

        switch (selected)
        {
            case "Path Following (Precise)":
                if (!isSecondaryShip)
                {
                    PathFollowingPrecise(); 
                }
                else
                {
                    isMoving = false; velocity = Vector3.zero;
                }
                ClearWanderVisuals();
                break;
                
            case "Path Following (Smooth)":
                if (!isSecondaryShip)
                {
                    PathFollowingSmooth(); 
                }
                else
                {
                    isMoving = false; velocity = Vector3.zero;
                }
                ClearWanderVisuals();
                break;
                
            // ⭐ NEW CASE: Path Following (Patrol) ⭐
            case "Path Following (Patrol)":
                if (!isSecondaryShip)
                {
                    PathFollowingPatrol(); 
                }
                else
                {
                    isMoving = false; velocity = Vector3.zero;
                }
                ClearWanderVisuals();
                break;
                
            // SECONDARY SHIP'S ALLOWED BEHAVIORS (and primary's when selected)
            case "Pursuit (Basic)":
                PursuitBasic();
                ClearWanderVisuals();
                break;
            case "Pursuit (Improved)":
                PursuitImproved();
                ClearWanderVisuals();
                break;
            case "Evade":
                Evade();
                ClearWanderVisuals();
                break;
            case "Wander": // Used by primary and secondary (as complement)
                Wander();
                break;
            
            // PRIMARY SHIP'S RESTRICTED BEHAVIORS
            case "Seek (Basic)":
                if (!isSecondaryShip) SeekBasic(); else { isMoving = false; velocity = Vector3.zero; }
                ClearWanderVisuals();
                break;
            case "Seek (Steering)":
                if (!isSecondaryShip) SeekSteering(); else { isMoving = false; velocity = Vector3.zero; }
                ClearWanderVisuals();
                break;
            case "Flee":
                if (!isSecondaryShip) Flee(); else { isMoving = false; velocity = Vector3.zero; }
                ClearWanderVisuals();
                break;
            case "Arrival":
                if (!isSecondaryShip) Arrive(); else { isMoving = false; velocity = Vector3.zero; }
                ClearWanderVisuals();
                break;
            case "Wander (State-Based)":
                if (!isSecondaryShip) WanderStateBased(); else { isMoving = false; velocity = Vector3.zero; }
                ClearWanderVisuals();
                break;

            default:
                isMoving = false;
                velocity = Vector3.zero;
                ClearWanderVisuals();
                break;
        }

        UpdateLineTrail(isMoving);

        if (!isPathFollowing)
        {
            ScreenWrap();
        }
    }

    // --- Steering Algorithms ---
    private void SeekBasic()
    {
        Vector3 direction = targetPosition - transform.position;
        direction.Normalize();
        velocity = direction * moveSpeed;
        transform.position += velocity * Time.deltaTime;
        RotateTowards(velocity);
    }

    private void SeekSteering()
    {
        Vector3 targetDirection = (targetPosition - transform.position).normalized;

        float targetAngle = Mathf.Atan2(targetDirection.y, targetDirection.x) * Mathf.Rad2Deg - 90f;
        float newAngle = Mathf.MoveTowardsAngle(transform.eulerAngles.z, targetAngle, rotationSpeed * Time.deltaTime);
        transform.eulerAngles = new Vector3(0f, 0f, newAngle);
        velocity = transform.up * moveSpeed;
        transform.position += velocity * Time.deltaTime;
    }

    private void Flee()
    {
        Vector3 fleeTarget = targetPosition;
        if (otherShip != null && isSecondaryShip)
        {
            Vector3 wrappedDirection = GetWrappedDirection(transform.position, otherShip.position);
            fleeTarget = transform.position + wrappedDirection;
        }

        Vector3 desired = (transform.position - fleeTarget).normalized * moveSpeed;
        Vector3 steering = desired - velocity;
        steering = Vector3.ClampMagnitude(steering, maxForce);

        velocity += steering / mass * Time.deltaTime;
        velocity = Vector3.ClampMagnitude(velocity, moveSpeed);
        transform.position += velocity * Time.deltaTime;
        RotateTowards(velocity);
    }

    private void Arrive()
    {
        Vector3 toTarget = targetPosition - transform.position;
        float distance = toTarget.magnitude;

        if (distance < 0.1f)
        {
            velocity = Vector3.zero;
            return;
        }

        float targetSpeed = (distance < slowingRadius)
            ? moveSpeed * (distance / slowingRadius)
            : moveSpeed;

        Vector3 desired = toTarget.normalized * targetSpeed;
        Vector3 steering = desired - velocity;
        steering = Vector3.ClampMagnitude(steering, maxForce);

        velocity += steering / mass * Time.deltaTime;
        velocity = Vector3.ClampMagnitude(velocity, moveSpeed);
        transform.position += velocity * Time.deltaTime;
        RotateTowards(velocity);
    }

    private void Wander()
    {
        Vector3 forward = transform.up;
        Vector3 circleCenter = forward * circleDistance;

        wanderTargetPoint = new Vector3(Mathf.Cos(wanderAngle), Mathf.Sin(wanderAngle), 0) * circleRadius;

        wanderAngle += Random.Range(-1f, 1f) * jitter * Time.deltaTime;

        Vector3 targetInWorld = transform.position + circleCenter + wanderTargetPoint;
        Vector3 desired = (targetInWorld - transform.position).normalized * moveSpeed;
        Vector3 steering = desired - velocity;
        steering = Vector3.ClampMagnitude(steering, maxForce);

        velocity += steering / mass * Time.deltaTime;
        velocity = Vector3.ClampMagnitude(velocity, moveSpeed);
        transform.position += velocity * Time.deltaTime;
        RotateTowards(velocity);

        wanderCircleCenter = circleCenter;

        DrawWanderCircle();
        DrawWanderTarget();
    }

    private void WanderStateBased()
    {
        if (wanderState == WanderState.WANDERING)
        {
            randomWanderTarget = new Vector3(
                Random.Range(-mapBounds.x, mapBounds.x),
                Random.Range(-mapBounds.y, mapBounds.y),
                0f
            );
            wanderState = WanderState.SEEKING;
        }

        if (wanderState == WanderState.SEEKING)
        {
            Vector3 desired = (randomWanderTarget - transform.position).normalized * moveSpeed;
            Vector3 steering = desired - velocity;
            steering = Vector3.ClampMagnitude(steering, maxForce);

            velocity += steering / mass * Time.deltaTime;
            velocity = Vector3.ClampMagnitude(velocity, moveSpeed);
            transform.position += velocity * Time.deltaTime;
            RotateTowards(velocity);

            float distance = Vector3.Distance(transform.position, randomWanderTarget);
            if (distance < arrivalThreshold)
            {
                wanderState = WanderState.WANDERING;
            }
        }
    }

    private void PursuitBasic()
    {
        if (otherShip == null)
        {
            Debug.LogWarning("Other ship not assigned for Pursuit!");
            return;
        }

        Vector3 wrappedDirection = GetWrappedDirection(transform.position, otherShip.position);
        Vector3 desired = wrappedDirection.normalized * moveSpeed;
        Vector3 steering = desired - velocity;
        steering = Vector3.ClampMagnitude(steering, maxForce);

        velocity += steering / mass * Time.deltaTime;
        velocity = Vector3.ClampMagnitude(velocity, moveSpeed);
        transform.position += velocity * Time.deltaTime;
        RotateTowards(velocity);

        if (!isSecondaryShip)
        {
            DrawPrediction(otherShip.position);
        }
    }

    private void PursuitImproved()
    {
        if (otherShip == null)
        {
            Debug.LogWarning("Other ship not assigned for Pursuit!");
            return;
        }

        SpaceshipBehaviour targetBehaviour = otherShip.GetComponent<SpaceshipBehaviour>();
        Vector3 targetVelocity = targetBehaviour != null ? targetBehaviour.GetVelocity() : Vector3.zero;

        Vector3 wrappedDirection = GetWrappedDirection(transform.position, otherShip.position);
        float distance = wrappedDirection.magnitude;

        float T = distance / moveSpeed;

        Vector3 futurePosRelative = otherShip.position + (targetVelocity * T);

        Vector3 wrappedPredictionDirection = GetWrappedDirection(transform.position, futurePosRelative);
        predictedPosition = transform.position + wrappedPredictionDirection;

        Vector3 desired = wrappedPredictionDirection.normalized * moveSpeed;
        Vector3 steering = desired - velocity;
        steering = Vector3.ClampMagnitude(steering, maxForce);

        velocity += steering / mass * Time.deltaTime;
        velocity = Vector3.ClampMagnitude(velocity, moveSpeed);
        transform.position += velocity * Time.deltaTime;
        RotateTowards(velocity);

        if (!isSecondaryShip)
        {
            DrawPrediction(predictedPosition);
        }
    }

    private void Evade()
    {
        if (otherShip == null)
        {
            Debug.LogWarning("Other ship not assigned for Evade!");
            return;
        }

        SpaceshipBehaviour predatorBehaviour = otherShip.GetComponent<SpaceshipBehaviour>();
        Vector3 predatorVelocity = predatorBehaviour != null ? predatorBehaviour.GetVelocity() : Vector3.zero;

        Vector3 wrappedDirection = GetWrappedDirection(transform.position, otherShip.position);
        float distance = wrappedDirection.magnitude;

        float combinedSpeed = moveSpeed + predatorVelocity.magnitude;
        float T = combinedSpeed > 0 ? distance / combinedSpeed : 0;

        T = Mathf.Clamp(T, 0.1f, 2f);

        Vector3 futurePosRelative = otherShip.position + (predatorVelocity * T);

        Vector3 wrappedPredictionDirection = GetWrappedDirection(transform.position, futurePosRelative);

        if (wrappedPredictionDirection.magnitude > 0.1f)
        {
            predictedPosition = transform.position + wrappedPredictionDirection;
        }
        else
        {
            predictedPosition = transform.position + wrappedDirection;
        }

        Vector3 desired = -wrappedPredictionDirection.normalized * moveSpeed;

        Vector3 perpendicular = new Vector3(-wrappedPredictionDirection.y, wrappedPredictionDirection.x, 0).normalized;
        float perpendicularStrength = 0.3f;
        desired += perpendicular * moveSpeed * perpendicularStrength;
        desired = desired.normalized * moveSpeed;

        Vector3 steering = desired - velocity;
        steering = Vector3.ClampMagnitude(steering, maxForce);

        velocity += steering / mass * Time.deltaTime;
        velocity = Vector3.ClampMagnitude(velocity, moveSpeed);
        transform.position += velocity * Time.deltaTime;
        RotateTowards(velocity);

        if (!isSecondaryShip)
        {
            DrawPrediction(predictedPosition);
        }
    }

    // --- Path Following (Uses Seek Steering and Simple Waypoint Snapping) ---
    private void PathFollowingPrecise()
    {
        // Re-generate if path is missing (happens when returning to this mode)
        if (pathPoints == null || pathPoints.Length == 0 || waypointObjects.Count == 0)
        {
            GeneratePath();
            if (pathPoints == null || pathPoints.Length == 0) return; 
        }

        DrawPath();

        Vector3 currentTarget = pathPoints[currentWaypointIndex];
        float distance = Vector3.Distance(transform.position, currentTarget);
        
        // Simple Waypoint Snapping/Looping Logic (using the small, precise radius)
        if (distance < waypointRadius) 
        {
            // Continuous Looping: Move to the next point and wrap around.
            currentWaypointIndex = (currentWaypointIndex + 1) % pathPoints.Length;
            currentTarget = pathPoints[currentWaypointIndex]; 
        }

        // Movement using Simple Seek Steering
        Vector3 desired = (currentTarget - transform.position).normalized * moveSpeed;
        Vector3 steering = desired - velocity;
        steering = Vector3.ClampMagnitude(steering, maxForce);

        velocity += steering / mass * Time.deltaTime;
        velocity = Vector3.ClampMagnitude(velocity, moveSpeed);
        transform.position += velocity * Time.deltaTime;
        RotateTowards(velocity);
    }
    
    // Path Following (Smooth)
    private void PathFollowingSmooth()
    {
        // Re-generate if path is missing (happens when returning to this mode)
        if (pathPoints == null || pathPoints.Length == 0 || waypointObjects.Count == 0)
        {
            GeneratePath();
            if (pathPoints == null || pathPoints.Length == 0) return; 
        }

        DrawPath();

        Vector3 currentTarget = pathPoints[currentWaypointIndex];
        float distance = Vector3.Distance(transform.position, currentTarget);
        
        // Simple Waypoint Snapping/Looping Logic (using the LARGE, smooth radius)
        if (distance < smoothWaypointRadius) 
        {
            // Continuous Looping: Move to the next point and wrap around.
            currentWaypointIndex = (currentWaypointIndex + 1) % pathPoints.Length;
            currentTarget = pathPoints[currentWaypointIndex]; 
        }

        // Movement using Simple Seek Steering
        Vector3 desired = (currentTarget - transform.position).normalized * moveSpeed;
        Vector3 steering = desired - velocity;
        steering = Vector3.ClampMagnitude(steering, maxForce);

        velocity += steering / mass * Time.deltaTime;
        velocity = Vector3.ClampMagnitude(velocity, moveSpeed);
        transform.position += velocity * Time.deltaTime;
        RotateTowards(velocity);
    }
    
    private void PathFollowingPatrol()
    {
        // Re-generate if path is missing (happens when returning to this mode)
        if (pathPoints == null || pathPoints.Length == 0 || waypointObjects.Count == 0)
        {
            GeneratePath();
            if (pathPoints == null || pathPoints.Length == 0) return; 
        }

        DrawPath();

        Vector3 currentTarget = pathPoints[currentWaypointIndex];
        float distance = Vector3.Distance(transform.position, currentTarget);
        
        // ⭐ Patrol Logic: Move index and flip direction at ends ⭐
        if (distance < smoothWaypointRadius) 
        {
            // 1. Move index forward or backward
            currentWaypointIndex += pathDirection;
         
            // 2. If at the end (or beyond) or beginning (or before), flip direction
            if (currentWaypointIndex >= pathPoints.Length || currentWaypointIndex < 0)
            {
                // Flip direction
                pathDirection *= -1; 
                
                // Correct index after flip:
                // If it went past the end (path.length), the index is now path.length. 
                // We flip direction (-1) and step back twice to hit path.length - 1.
                // If it went below 0 (index is now -1), we flip direction (+1) and step forward twice to hit 1.
                currentWaypointIndex += pathDirection * 2; 
                
                // Safety clamp (optional but good practice)
                currentWaypointIndex = Mathf.Clamp(currentWaypointIndex, 0, pathPoints.Length - 1);
            }
            currentTarget = pathPoints[currentWaypointIndex];
        }

        // Movement using Simple Seek Steering
        Vector3 desired = (currentTarget - transform.position).normalized * moveSpeed;
        Vector3 steering = desired - velocity;
        steering = Vector3.ClampMagnitude(steering, maxForce);

        velocity += steering / mass * Time.deltaTime;
        velocity = Vector3.ClampMagnitude(velocity, moveSpeed);
        transform.position += velocity * Time.deltaTime;
        RotateTowards(velocity);
    }

    // --- Helper & Utility Methods ---

    private void RotateTowards(Vector3 velocity)
    {
        if (velocity.sqrMagnitude > 0.001f)
        {
            float targetAngle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg - 90f;
            float newAngle = Mathf.MoveTowardsAngle(transform.eulerAngles.z, targetAngle, rotationSpeed * Time.deltaTime);
            transform.eulerAngles = new Vector3(0f, 0f, newAngle);
        }
    }

    private void ScreenWrap()
    {
        Camera cam = Camera.main;
        Vector3 viewportPos = cam.WorldToViewportPoint(transform.position);
        bool wrapped = false;

        if (viewportPos.x > 1f) { viewportPos.x = 0f; wrapped = true; }
        else if (viewportPos.x < 0f) { viewportPos.x = 1f; wrapped = true; }

        if (viewportPos.y > 1f) { viewportPos.y = 0f; wrapped = true; }
        else if (viewportPos.y < 0f) { viewportPos.y = 1f; wrapped = true; }

        transform.position = cam.ViewportToWorldPoint(viewportPos);

        if (wrapped)
        {
            trailPoints.Clear();
            pointTimes.Clear();
        }
    }

    private Vector3 GetWrappedDirection(Vector3 from, Vector3 to)
    {
        Camera cam = Camera.main;

        Vector3 bottomLeft = cam.ViewportToWorldPoint(new Vector3(0, 0, 0));
        Vector3 topRight = cam.ViewportToWorldPoint(new Vector3(1, 1, 0));
        float worldWidth = topRight.x - bottomLeft.x;
        float worldHeight = topRight.y - bottomLeft.y;

        Vector3 direct = to - from;

        Vector3 wrappedX = direct;
        if (direct.x > worldWidth / 2f)
            wrappedX.x -= worldWidth;
        else if (direct.x < -worldWidth / 2f)
            wrappedX.x += worldWidth;

        Vector3 wrappedY = wrappedX;
        if (wrappedX.y > worldHeight / 2f)
            wrappedY.y -= worldHeight;
        else if (wrappedX.y < -worldHeight / 2f)
            wrappedY.y += worldHeight;

        return wrappedY;
    }

    // --- UPDATED HELPER METHODS FOR PATH FOLLOWING ---
    
    private void GeneratePath()
    {
        ClearPathPrefabs();

        // FIX: Check for waypointPrefab and restrict error logging to primary ship.
        if (waypointPrefab == null)
        {
            if (!isSecondaryShip) 
            {
                Debug.LogError("Waypoint Prefab not assigned! Cannot generate visual path points.");
            }
            pathPoints = new Vector3[0];
            return;
        }

        // Define path boundaries using camera view and a margin
        Camera cam = Camera.main;
        float marginX = (cam.orthographicSize * cam.aspect) * pathGenerationMargin;
        float marginY = cam.orthographicSize * pathGenerationMargin;

        Vector3 lowerLeft = cam.ViewportToWorldPoint(new Vector3(0, 0, 0));
        Vector3 upperRight = cam.ViewportToWorldPoint(new Vector3(1, 1, 0));
        
        float minX = lowerLeft.x + marginX;
        float maxX = upperRight.x - marginX;
        float minY = lowerLeft.y + marginY;
        float maxY = upperRight.y - marginY;

        // Create a path with 4 waypoints forming a rectangle 
        const int numPoints = 4;
        Vector3[] newPathPoints = new Vector3[numPoints];

        newPathPoints[0] = new Vector3(minX, maxY, 0); 
        newPathPoints[1] = new Vector3(maxX, maxY, 0);
        newPathPoints[2] = new Vector3(maxX, minY, 0);
        newPathPoints[3] = new Vector3(minX, minY, 0);

        // INSTANTIATE PREFABS AND STORE POSITIONS
        pathPoints = new Vector3[numPoints];
        for (int i = 0; i < numPoints; i++)
        {
            GameObject newWaypoint = Instantiate(waypointPrefab, newPathPoints[i], Quaternion.identity);
            waypointObjects.Add(newWaypoint);
            pathPoints[i] = newWaypoint.transform.position; 
        }

        currentWaypointIndex = 0;
    }

    private void DrawPath()
    {
        if (pathPoints == null || pathPoints.Length < 2) 
        {
            if (pathRenderer != null) pathRenderer.positionCount = 0;
            return;
        }

        // Ensure prefabs are visible in this mode
        foreach (var obj in waypointObjects)
        {
            if (obj != null) obj.SetActive(true);
        }

        // Draw the connecting lines for the path
        if (pathRenderer != null)
        {
            pathRenderer.positionCount = pathPoints.Length;
            pathRenderer.SetPositions(pathPoints);
            // Patrol path is not technically a loop, but we draw it looped for visual path continuity
            pathRenderer.loop = true; 
        }
        
        if (waypointMarkersRenderer != null)
        {
            waypointMarkersRenderer.positionCount = 0;
        }
    }
    
    private void ClearPathPrefabs()
    {
        if (waypointObjects.Count > 0)
        {
            foreach (var obj in waypointObjects)
            {
                if (obj != null)
                {
                    Destroy(obj);
                }
            }
            waypointObjects.Clear();
        }
    }

    private void ClearPathVisuals(bool destroyPrefabs = true)
    {
        if (pathRenderer != null)
        {
            pathRenderer.positionCount = 0;
            pathRenderer.loop = false;
        }
        
        if (waypointMarkersRenderer != null)
        {
            waypointMarkersRenderer.positionCount = 0;
        }

        if (destroyPrefabs)
        {
            ClearPathPrefabs();
        }
        else
        {
            foreach (var obj in waypointObjects)
            {
                if (obj != null) obj.SetActive(false);
            }
        }
    }

    // --- Other Utility Methods ---
    public Vector3 GetVelocity()
    {
        return velocity;
    }

    // Determines the secondary ship's behavior based on the primary ship's selection
    private string GetComplementaryBehavior(string mainBehavior)
    {
        if (string.IsNullOrEmpty(mainBehavior))
        {
            return "Wander"; 
        }

        switch (mainBehavior)
        {
            case "Pursuit (Basic)":
            case "Pursuit (Improved)":
                return "Wander"; 
            case "Evade":
                return "Pursuit (Improved)";
            // Path following modes cause the secondary ship to default to Wander
            default:
                // Secondary ship defaults to Wander if the primary ship is doing something else (Seek, Path Following, etc.)
                return "Wander";
        }
    }

    private void UpdateLineTrail(bool isMoving)
    {
        if (lineRenderer == null) return;

        // Remove old points
        for (int i = pointTimes.Count - 1; i >= 0; i--)
        {
            if (Time.time - pointTimes[i] > trailDuration)
            {
                trailPoints.RemoveAt(i);
                pointTimes.RemoveAt(i);
            }
        }

        // Add a new point if moving and far enough from the last one
        if (isMoving && (trailPoints.Count == 0 || Vector3.Distance(trailPoints[trailPoints.Count - 1], transform.position) > minPointDistance))
        {
            trailPoints.Add(transform.position);
            pointTimes.Add(Time.time);
        }

        // Update the LineRenderer
        lineRenderer.positionCount = trailPoints.Count;
        lineRenderer.SetPositions(trailPoints.ToArray());
    }

    private void DrawWanderCircle()
    {
        if (wanderCircleRenderer == null) return;

        const int segments = 32;
        wanderCircleRenderer.positionCount = segments + 1;
        
        Vector3 worldCircleCenter = transform.position + transform.up * circleDistance;

        for (int i = 0; i <= segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2f;
            float x = Mathf.Cos(angle) * circleRadius;
            float y = Mathf.Sin(angle) * circleRadius;
            wanderCircleRenderer.SetPosition(i, worldCircleCenter + new Vector3(x, y, 0));
        }
    }

    private void DrawWanderTarget()
    {
        if (wanderTargetRenderer == null) return;
        
        Vector3 worldCircleCenter = transform.position + transform.up * circleDistance;
        Vector3 worldTargetPoint = worldCircleCenter + wanderTargetPoint;

        wanderTargetRenderer.SetPosition(0, worldCircleCenter);
        wanderTargetRenderer.SetPosition(1, worldTargetPoint);
    }
    
    private void ClearWanderVisuals()
    {
        if (wanderCircleRenderer != null) wanderCircleRenderer.positionCount = 0;
        if (wanderTargetRenderer != null) wanderTargetRenderer.positionCount = 0;
    }
    
    private void DrawPrediction(Vector3 predictionPoint)
    {
        if (predictionLineRenderer != null)
        {
            predictionLineRenderer.positionCount = 2;
            predictionLineRenderer.SetPosition(0, transform.position);
            predictionLineRenderer.SetPosition(1, predictionPoint);
        }
    }

    private void ClearPredictionVisuals()
    {
        if (predictionLineRenderer != null)
        {
            predictionLineRenderer.positionCount = 0;
        }
    }
}