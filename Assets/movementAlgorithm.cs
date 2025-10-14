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
    [SerializeField] private LineRenderer wanderCircleRenderer; // draws circle in game
    [SerializeField] private LineRenderer wanderTargetRenderer; // draws line to target

    // State-based wander parameters
    [SerializeField] private float arrivalThreshold = 0.5f;
    [SerializeField] private Vector2 mapBounds = new Vector2(20f, 15f); // x and y bounds for random points

    // Pursuit/Evade parameters
    [SerializeField] private Transform otherShip; // Reference to the other spaceship (only needed for Pursuit/Evade)
    [SerializeField] private LineRenderer predictionLineRenderer; // draws line to predicted position
    [SerializeField] private bool isSecondaryShip = false; // Set true for the secondary ship

    // Path Following parameters
    [SerializeField] private float waypointRadius = 0.3f; // How close to get before moving to next waypoint
    [SerializeField] private LineRenderer pathRenderer; // Draws the path
    [SerializeField] private LineRenderer waypointMarkersRenderer; // Draws circles at waypoints
    [SerializeField] private bool loopPath = true; // Whether to loop back to start
    [SerializeField] private float pathGenerationRadius = 8f; // Radius for random hexagon
    [SerializeField] private float waypointMarkerSize = 0.3f; // Size of waypoint circles

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
    private Vector3[] pathPoints; // Store generated path points

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

        // Setup wander circle renderer if not assigned
        if (wanderCircleRenderer != null)
        {
            wanderCircleRenderer.positionCount = 0;
            wanderCircleRenderer.loop = true;
        }

        if (wanderTargetRenderer != null)
        {
            wanderTargetRenderer.positionCount = 2;
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

        if (Input.GetMouseButtonDown(0))
        {
            Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mousePosition.z = 0f;
            targetPosition = mousePosition;
        }

        if (algorithmDropdown == null)
        {
            UpdateLineTrail(false);
            ScreenWrap();
            return;
        }

        // Use forced behavior if set, otherwise use dropdown
        string selected = algorithmDropdown.options[algorithmDropdown.value].text;

        // Secondary ship uses complementary behavior
        if (isSecondaryShip)
        {
            selected = GetComplementaryBehavior(selected);
        }

        if (otherShip != null && !isSecondaryShip) // Only primary ship controls visibility
        {
            // Get the actual dropdown value (not converted)
            string dropdownValue = algorithmDropdown.options[algorithmDropdown.value].text;

            bool shouldShowOtherShip = dropdownValue == "Pursuit (Basic)" ||
                                       dropdownValue == "Pursuit (Improved)" ||
                                       dropdownValue == "Evade";
            otherShip.gameObject.SetActive(shouldShowOtherShip);
        }
        bool isMoving = true;

        ClearPredictionVisuals();

        switch (selected)
        {
            case "Seek (Basic)":
                SeekBasic();
                ClearWanderVisuals();
                ClearPathVisuals();
                break;
            case "Seek (Steering)":
                SeekSteering();
                ClearWanderVisuals();
                ClearPathVisuals();
                break;
            case "Flee":
                Flee();
                ClearWanderVisuals();
                ClearPathVisuals();
                break;
            case "Arrival":
                Arrive();
                ClearWanderVisuals();
                ClearPathVisuals();
                break;
            case "Wander":
                Wander();
                ClearPathVisuals();
                break;
            case "Wander (State-Based)":
                WanderStateBased();
                ClearWanderVisuals();
                ClearPathVisuals();
                break;
            case "Path Following (Precise)":
                PathFollowingPrecise();
                ClearWanderVisuals();
                break;
            case "Pursuit (Basic)":
                PursuitBasic();
                ClearWanderVisuals();
                ClearPathVisuals();
                // Don't clear prediction visuals - keep the line
                break;
            case "Pursuit (Improved)":
                PursuitImproved();
                ClearWanderVisuals();
                ClearPathVisuals();
                // Don't clear prediction visuals - keep the line
                break;
            case "Evade":
                Evade();
                ClearWanderVisuals();
                ClearPathVisuals();
                // Don't clear prediction visuals - keep the line
                break;
            default:
                isMoving = false;
                velocity = Vector3.zero;
                ClearWanderVisuals();
                ClearPathVisuals();
                break;
        }

        UpdateLineTrail(isMoving);

        // Only screen wrap if not doing path following
        if (selected != "Path Following (Precise)")
        {
            ScreenWrap();
        }
    }

    // --- Algorithms ---

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
        Vector3 desired = (targetPosition - transform.position).normalized * moveSpeed;
        Vector3 steering = desired - velocity;
        steering = Vector3.ClampMagnitude(steering, maxForce);

        velocity += steering / mass * Time.deltaTime;
        velocity = Vector3.ClampMagnitude(velocity, moveSpeed);
        transform.position += velocity * Time.deltaTime;
        RotateTowards(velocity);
    }

    private void Flee()
    {
        // Use wrapped direction if fleeing from another ship
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
        // 1. Circle center is in front of the spaceship
        Vector3 forward = transform.up; // Since sprite rotation is based on Z-axis with -90 offset
        Vector3 circleCenter = forward * circleDistance;

        // 2. Random point on circle (relative to circle center)
        wanderTargetPoint = new Vector3(Mathf.Cos(wanderAngle), Mathf.Sin(wanderAngle), 0) * circleRadius;

        // 3. Jitter
        wanderAngle += Random.Range(-1f, 1f) * jitter * Time.deltaTime;

        // 4. Steering - target is the point on the circle
        Vector3 targetInWorld = transform.position + circleCenter + wanderTargetPoint;
        Vector3 desired = (targetInWorld - transform.position).normalized * moveSpeed;
        Vector3 steering = desired - velocity;
        steering = Vector3.ClampMagnitude(steering, maxForce);

        velocity += steering / mass * Time.deltaTime;
        velocity = Vector3.ClampMagnitude(velocity, moveSpeed);
        transform.position += velocity * Time.deltaTime;
        RotateTowards(velocity);

        // Store for visualization
        wanderCircleCenter = circleCenter;

        // --- Draw in Game View ---
        DrawWanderCircle();
        DrawWanderTarget();
    }

    private void WanderStateBased()
    {
        if (wanderState == WanderState.WANDERING)
        {
            // Pick a new random point within map bounds
            randomWanderTarget = new Vector3(
                Random.Range(-mapBounds.x, mapBounds.x),
                Random.Range(-mapBounds.y, mapBounds.y),
                0f
            );
            wanderState = WanderState.SEEKING;
        }

        if (wanderState == WanderState.SEEKING)
        {
            // Seek the random target
            Vector3 desired = (randomWanderTarget - transform.position).normalized * moveSpeed;
            Vector3 steering = desired - velocity;
            steering = Vector3.ClampMagnitude(steering, maxForce);

            velocity += steering / mass * Time.deltaTime;
            velocity = Vector3.ClampMagnitude(velocity, moveSpeed);
            transform.position += velocity * Time.deltaTime;
            RotateTowards(velocity);

            // Check if we've arrived at the target
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

        // Use wrapped direction for pursuit
        Vector3 wrappedDirection = GetWrappedDirection(transform.position, otherShip.position);
        Vector3 desired = wrappedDirection.normalized * moveSpeed;
        Vector3 steering = desired - velocity;
        steering = Vector3.ClampMagnitude(steering, maxForce);

        velocity += steering / mass * Time.deltaTime;
        velocity = Vector3.ClampMagnitude(velocity, moveSpeed);
        transform.position += velocity * Time.deltaTime;
        RotateTowards(velocity);

        // Only draw prediction if this is the pursuer
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

        // Get the target's velocity
        SpaceshipBehaviour targetBehaviour = otherShip.GetComponent<SpaceshipBehaviour>();
        Vector3 targetVelocity = targetBehaviour != null ? targetBehaviour.GetVelocity() : Vector3.zero;

        // Get the shortest wrapped direction to the target
        Vector3 wrappedDirection = GetWrappedDirection(transform.position, otherShip.position);
        float distance = wrappedDirection.magnitude;

        // Calculate time to intercept
        float T = distance / moveSpeed;

        // Predict target's future position
        Vector3 futurePosRelative = otherShip.position + (targetVelocity * T);

        // Apply the same wrapping logic to the predicted position
        Vector3 wrappedPredictionDirection = GetWrappedDirection(transform.position, futurePosRelative);
        predictedPosition = transform.position + wrappedPredictionDirection;

        // Seek the predicted position
        Vector3 desired = wrappedPredictionDirection.normalized * moveSpeed;
        Vector3 steering = desired - velocity;
        steering = Vector3.ClampMagnitude(steering, maxForce);

        velocity += steering / mass * Time.deltaTime;
        velocity = Vector3.ClampMagnitude(velocity, moveSpeed);
        transform.position += velocity * Time.deltaTime;
        RotateTowards(velocity);

        // Only draw prediction if this is the pursuer
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

        // Get the predator's velocity
        SpaceshipBehaviour predatorBehaviour = otherShip.GetComponent<SpaceshipBehaviour>();
        Vector3 predatorVelocity = predatorBehaviour != null ? predatorBehaviour.GetVelocity() : Vector3.zero;

        // Get the shortest wrapped direction to the predator
        Vector3 wrappedDirection = GetWrappedDirection(transform.position, otherShip.position);
        float distance = wrappedDirection.magnitude;

        // Calculate time to intercept based on relative speeds
        float combinedSpeed = moveSpeed + predatorVelocity.magnitude;
        float T = combinedSpeed > 0 ? distance / combinedSpeed : 0;

        // Clamp T to avoid extreme predictions
        T = Mathf.Clamp(T, 0.1f, 2f);

        // Predict predator's future position
        Vector3 futurePosRelative = otherShip.position + (predatorVelocity * T);

        // Apply the same wrapping logic to the predicted position
        Vector3 wrappedPredictionDirection = GetWrappedDirection(transform.position, futurePosRelative);

        // Only update predicted position if it's valid
        if (wrappedPredictionDirection.magnitude > 0.1f)
        {
            predictedPosition = transform.position + wrappedPredictionDirection;
        }
        else
        {
            // Fallback to current position if prediction is invalid
            predictedPosition = transform.position + wrappedDirection;
        }

        // Flee from the predicted position (opposite direction)
        Vector3 desired = -wrappedPredictionDirection.normalized * moveSpeed;

        // Add some perpendicular component for more interesting evasion
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

        // Only draw prediction if this is the evader
        if (!isSecondaryShip)
        {
            DrawPrediction(predictedPosition);
        }
    }

    private void PathFollowingPrecise()
    {
        // Generate path on first call
        if (pathPoints == null || pathPoints.Length == 0)
        {
            GenerateRandomHexagonPath();
        }

        // Get current target waypoint
        Vector3 currentTarget = pathPoints[currentWaypointIndex];

        // Seek the current waypoint
        Vector3 desired = (currentTarget - transform.position).normalized * moveSpeed;
        Vector3 steering = desired - velocity;
        steering = Vector3.ClampMagnitude(steering, maxForce);

        velocity += steering / mass * Time.deltaTime;
        velocity = Vector3.ClampMagnitude(velocity, moveSpeed);
        transform.position += velocity * Time.deltaTime;
        RotateTowards(velocity);

        // Check if we've reached the waypoint
        float distance = Vector3.Distance(transform.position, currentTarget);
        if (distance < waypointRadius)
        {
            // Move to next waypoint
            currentWaypointIndex++;

            if (currentWaypointIndex >= pathPoints.Length)
            {
                if (loopPath)
                {
                    currentWaypointIndex = 0; // Loop back to start
                }
                else
                {
                    currentWaypointIndex = pathPoints.Length - 1; // Stay at last point
                    velocity = Vector3.zero;
                }
            }
        }

        // Draw the path
        DrawPath();
        DrawWaypointMarkers();
    }

    private void GenerateRandomHexagonPath()
    {
        pathPoints = new Vector3[6];

        // Get camera bounds to keep points on screen
        Camera cam = Camera.main;
        Vector3 bottomLeft = cam.ViewportToWorldPoint(new Vector3(0.1f, 0.1f, 0));
        Vector3 topRight = cam.ViewportToWorldPoint(new Vector3(0.9f, 0.9f, 0));

        // Generate center point within bounds
        Vector3 center = new Vector3(
            Random.Range(bottomLeft.x + pathGenerationRadius, topRight.x - pathGenerationRadius),
            Random.Range(bottomLeft.y + pathGenerationRadius, topRight.y - pathGenerationRadius),
            0f
        );

        // Generate 6 points in hexagon pattern with random variations
        for (int i = 0; i < 6; i++)
        {
            float angle = i * 60f * Mathf.Deg2Rad;

            // Add random variation to radius and angle
            float radiusVariation = Random.Range(0.7f, 1.3f);
            float angleVariation = Random.Range(-15f, 15f) * Mathf.Deg2Rad;

            float finalAngle = angle + angleVariation;
            float finalRadius = pathGenerationRadius * radiusVariation;

            pathPoints[i] = center + new Vector3(
                Mathf.Cos(finalAngle) * finalRadius,
                Mathf.Sin(finalAngle) * finalRadius,
                0f
            );
        }

        // Start at first waypoint
        currentWaypointIndex = 0;

        Debug.Log("Generated random hexagon path with 6 points");
    }

    public Vector3 GetVelocity()
    {
        return velocity;
    }

    // Returns the complementary behavior for the secondary ship
    private string GetComplementaryBehavior(string primaryBehavior)
    {
        switch (primaryBehavior)
        {
            case "Pursuit (Basic)":
            case "Pursuit (Improved)":
                return "Flee";
            case "Evade":
                return "Pursuit (Basic)";
            default:
                return primaryBehavior; // Use same behavior for other modes
        }
    }

    // --- Visualization Methods ---

    private void DrawWanderCircle()
    {
        if (wanderCircleRenderer == null) return;

        int segments = 40;
        wanderCircleRenderer.positionCount = segments;

        // Circle is centered on the spaceship itself
        Vector3 worldCenter = transform.position;

        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2 / segments;
            wanderCircleRenderer.SetPosition(i, worldCenter + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * circleRadius);
        }
    }

    private void DrawWanderTarget()
    {
        if (wanderTargetRenderer == null) return;

        // Ensure we have at least 2 positions
        if (wanderTargetRenderer.positionCount < 2)
        {
            wanderTargetRenderer.positionCount = 2;
        }

        // The target point is on the circle around the spaceship
        Vector3 worldTarget = transform.position + wanderTargetPoint;

        wanderTargetRenderer.SetPosition(0, transform.position);
        wanderTargetRenderer.SetPosition(1, worldTarget);
    }

    private void ClearWanderVisuals()
    {
        if (wanderCircleRenderer != null)
        {
            wanderCircleRenderer.positionCount = 0;
        }

        if (wanderTargetRenderer != null)
        {
            wanderTargetRenderer.positionCount = 0;
        }
    }

    private void DrawPrediction(Vector3 predictionPoint)
    {
        if (predictionLineRenderer == null) return;

        predictionLineRenderer.positionCount = 2;
        predictionLineRenderer.SetPosition(0, transform.position);
        predictionLineRenderer.SetPosition(1, predictionPoint);
    }

    private void ClearPredictionVisuals()
    {
        if (predictionLineRenderer != null)
        {
            predictionLineRenderer.positionCount = 0;
        }
    }

    private void DrawPath()
    {
        if (pathRenderer == null || pathPoints == null || pathPoints.Length == 0) return;

        int pointCount = loopPath ? pathPoints.Length + 1 : pathPoints.Length;
        pathRenderer.positionCount = pointCount;

        for (int i = 0; i < pathPoints.Length; i++)
        {
            pathRenderer.SetPosition(i, pathPoints[i]);
        }

        // Close the loop if needed
        if (loopPath)
        {
            pathRenderer.SetPosition(pathPoints.Length, pathPoints[0]);
        }
    }

    private void DrawWaypointMarkers()
    {
        if (waypointMarkersRenderer == null || pathPoints == null || pathPoints.Length == 0) return;

        int segments = 12; // Segments per circle
        int totalPoints = pathPoints.Length * segments;
        waypointMarkersRenderer.positionCount = totalPoints;

        int index = 0;
        for (int i = 0; i < pathPoints.Length; i++)
        {
            Vector3 center = pathPoints[i];

            for (int j = 0; j < segments; j++)
            {
                float angle = j * Mathf.PI * 2 / segments;
                Vector3 point = center + new Vector3(
                    Mathf.Cos(angle) * waypointMarkerSize,
                    Mathf.Sin(angle) * waypointMarkerSize,
                    0f
                );
                waypointMarkersRenderer.SetPosition(index, point);
                index++;
            }
        }
    }

    private void ClearPathVisuals()
    {
        if (pathRenderer != null)
        {
            pathRenderer.positionCount = 0;
        }

        if (waypointMarkersRenderer != null)
        {
            waypointMarkersRenderer.positionCount = 0;
        }

        // Clear generated path points so a new one is generated next time
        pathPoints = null;
        currentWaypointIndex = 0;
    }

    // --- Helper Methods ---

    private void UpdateLineTrail(bool isMoving)
    {
        if (lineRenderer == null) return;

        if (isMoving)
        {
            if (trailPoints.Count == 0 || Vector3.Distance(trailPoints[^1], transform.position) > minPointDistance)
            {
                trailPoints.Add(transform.position);
                pointTimes.Add(Time.time);
            }
        }

        for (int i = 0; i < pointTimes.Count; i++)
        {
            if (Time.time - pointTimes[i] > trailDuration)
            {
                trailPoints.RemoveAt(i);
                pointTimes.RemoveAt(i);
                i--;
            }
            else break;
        }

        if (trailPoints.Count > 0)
        {
            lineRenderer.positionCount = trailPoints.Count;
            lineRenderer.SetPositions(trailPoints.ToArray());
        }
        else
        {
            lineRenderer.positionCount = 0;
        }
    }

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

    // Helper method to get the shortest direction considering screen wrapping
    private Vector3 GetWrappedDirection(Vector3 from, Vector3 to)
    {
        Camera cam = Camera.main;

        // Get world bounds
        Vector3 bottomLeft = cam.ViewportToWorldPoint(new Vector3(0, 0, 0));
        Vector3 topRight = cam.ViewportToWorldPoint(new Vector3(1, 1, 0));
        float worldWidth = topRight.x - bottomLeft.x;
        float worldHeight = topRight.y - bottomLeft.y;

        // Calculate direct distance
        Vector3 direct = to - from;

        // Calculate wrapped distances
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
}