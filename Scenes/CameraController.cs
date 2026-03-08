using UnityEngine;

public class CameraController : MonoBehaviour
{
    [Header("Зв'язок зі столом")]
    public GridManager gridManager;

    [Header("Налаштування руху")]
    public float panSpeed = 15f;
    public float shiftMultiplier = 2.5f;
    public float zoomSpeed = 50f;
    public float rotationSpeed = 4f;
    public float movementSmoothness = 15f;

    [Header("Ліміти")]
    public float minZoomY = 2f;
    public float maxZoomY = 80f;
    public float minPitch = -15f;
    public float maxPitch = 89f;

    private Vector3 targetPosition;
    private float currentYaw;
    private float currentPitch;

    void Start()
    {
        if (gridManager == null) gridManager = FindFirstObjectByType<GridManager>();

        targetPosition = transform.position;

        Vector3 angles = transform.eulerAngles;
        currentPitch = angles.x;
        currentYaw = angles.y;
    }

    void Update()
    {
        HandleRotation();
        HandleMovement();
        HandleZoom();
        ClampToTable();

        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * movementSmoothness);
        transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(currentPitch, currentYaw, 0), Time.deltaTime * movementSmoothness);
    }

    void HandleRotation()
    {
        // 🔥 ВОНО: Блокуємо поворот камери, якщо ми зараз керуємо фігуркою!
        if (TokenManager.hasSelectedToken) return;

        if (Input.GetMouseButton(1))
        {
            currentYaw += Input.GetAxis("Mouse X") * rotationSpeed;
            currentPitch -= Input.GetAxis("Mouse Y") * rotationSpeed;
            currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);
        }
    }

    void HandleMovement()
    {
        float heightMultiplier = Mathf.Clamp(transform.position.y / 10f, 0.5f, 3f);

        float currentShiftBoost = Input.GetKey(KeyCode.LeftShift) ? shiftMultiplier : 1f;

        float currentSpeed = panSpeed * heightMultiplier * currentShiftBoost;

        Vector3 forward = new Vector3(transform.forward.x, 0, transform.forward.z).normalized;
        Vector3 right = new Vector3(transform.right.x, 0, transform.right.z).normalized;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        targetPosition += (forward * v + right * h) * currentSpeed * Time.deltaTime;

        if (Input.GetMouseButton(2))
        {
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");
            targetPosition -= (right * mouseX + forward * mouseY) * currentSpeed * 0.1f;
        }
    }

    void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            targetPosition += transform.forward * scroll * zoomSpeed;
        }
    }

    void ClampToTable()
    {
        if (gridManager != null)
        {
            float padding = gridManager.CellSize * 5f;

            float halfX = (gridManager.GridSizeX * gridManager.CellSize) / 2f;
            float halfZ = (gridManager.GridSizeZ * gridManager.CellSize) / 2f;

            float minX = gridManager.OffsetX - halfX - padding;
            float maxX = gridManager.OffsetX + halfX + padding;
            float minZ = gridManager.OffsetZ - halfZ - padding;
            float maxZ = gridManager.OffsetZ + halfZ + padding;

            targetPosition.x = Mathf.Clamp(targetPosition.x, minX, maxX);
            targetPosition.z = Mathf.Clamp(targetPosition.z, minZ, maxZ);
        }

        targetPosition.y = Mathf.Clamp(targetPosition.y, minZoomY, maxZoomY);
    }
}