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

    // --- Unity Lifecycle ---
    void Start()
    {
        targetPosition = transform.position;

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

        string selected = algorithmDropdown.options[algorithmDropdown.value].text;
        bool isMoving = true;

        switch (selected)
        {
            case "Seek (Basic)":
                SeekBasic();
                break;
            case "Seek (Steering)":
                SeekSteering();
                break;
            case "Flee":
                Flee();
                break;
            case "Arrival":
                Arrive();
                break;
            case "Wander":
                Wander();
                break;
            default:
                isMoving = false;
                velocity = Vector3.zero;
                break;
        }

        UpdateLineTrail(isMoving);
        ScreenWrap();
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
        Vector3 desired = (transform.position - targetPosition).normalized * moveSpeed;
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
        // 1. Circle in front
        wanderCircleCenter = velocity.normalized * circleDistance;

        // 2. Random point on circle
        wanderTargetPoint = new Vector3(Mathf.Cos(wanderAngle), Mathf.Sin(wanderAngle), 0) * circleRadius;

        // 3. Jitter
        wanderAngle += Random.Range(-1f, 1f) * jitter * Time.deltaTime;

        // 4. Steering
        Vector3 wanderForce = wanderCircleCenter + wanderTargetPoint;
        Vector3 steering = wanderForce - velocity;
        steering = Vector3.ClampMagnitude(steering, maxForce);

        velocity += steering / mass * Time.deltaTime;
        velocity = Vector3.ClampMagnitude(velocity, moveSpeed);
        transform.position += velocity * Time.deltaTime;
        RotateTowards(velocity);

        // --- Draw in Game View ---
        DrawWanderCircle();
        DrawWanderTarget();
    }

    private void DrawWanderCircle()
    {
        if (wanderCircleRenderer == null) return;

        int segments = 40;
        wanderCircleRenderer.positionCount = segments;

        Vector3 worldCenter = transform.position + wanderCircleCenter;

        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2 / segments;
            wanderCircleRenderer.SetPosition(i, worldCenter + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * circleRadius);
        }
    }

    private void DrawWanderTarget()
    {
        if (wanderTargetRenderer == null) return;

        Vector3 worldCenter = transform.position + wanderCircleCenter;
        Vector3 worldTarget = worldCenter + wanderTargetPoint;

        wanderTargetRenderer.SetPosition(0, transform.position);
        wanderTargetRenderer.SetPosition(1, worldTarget);
    }

    // --- Helpers ---

    private void RotateTowards(Vector3 velocity)
    {
        if (velocity.sqrMagnitude > 0.001f)
        {
            float targetAngle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg - 90f;
            float newAngle = Mathf.MoveTowardsAngle(transform.eulerAngles.z, targetAngle, rotationSpeed * Time.deltaTime);
            transform.eulerAngles = new Vector3(0f, 0f, newAngle);
        }
    }

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
}
