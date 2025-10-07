using UnityEngine;

public class movementAlgorithm : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;       // Movement speed
    [SerializeField] private float rotationSpeed = 180f; // Rotation speed in degrees per second
    private Vector3 targetPosition;                      // Where to move

    void Start()
    {
        targetPosition = transform.position;
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mousePosition.z = 0f;
            targetPosition = mousePosition;
        }

        // --- Movement ---
        transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);

        // --- Rotation ---
        Vector3 direction = targetPosition - transform.position;
        if (direction.sqrMagnitude > 0.001f)
        {
            float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            float newAngle = Mathf.MoveTowardsAngle(transform.eulerAngles.z, targetAngle, rotationSpeed * Time.deltaTime);
            transform.eulerAngles = new Vector3(0f, 0f, newAngle);
        }
    }
}
