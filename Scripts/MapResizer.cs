using UnityEngine;
using Firebase.Firestore;
using System.Collections.Generic;

public class MapResizer : MonoBehaviour
{
    public GridManager gridManager;
    public Material handleMaterial;

    private FirebaseFirestore db;
    private string worldID;
    private bool isResizingMode = false;
    private List<GameObject> handles = new List<GameObject>();

    private GameObject draggedHandle;
    private int initialGridX, initialGridZ;
    private Vector3 initialTablePos;
    private Vector3 initialDragPos;

    // Для оптимізації, щоб не перемальовувати сітку щокадру
    private int lastPreviewX = -1;
    private int lastPreviewZ = -1;
    private Vector3 lastPreviewPos = Vector3.zero;

    private struct MapState
    {
        public int x, z;
        public Vector3 pos;
        // ВИПРАВЛЕНО: Тепер змінні призначаються правильно!
        public MapState(int _x, int _z, Vector3 _pos) { x = _x; z = _z; pos = _pos; }
    }
    private Stack<MapState> undoStack = new Stack<MapState>();

    void Start()
    {
        InitFirebase();
        if (gridManager == null) gridManager = FindFirstObjectByType<GridManager>();
    }

    void InitFirebase()
    {
        if (db == null) db = FirebaseFirestore.DefaultInstance;
        if (string.IsNullOrEmpty(worldID)) worldID = PlayerPrefs.GetString("RoomID", "test_world");
    }

    public void ToggleResizerMode()
    {
        isResizingMode = !isResizingMode;
        if (isResizingMode)
        {
            SpawnHandles();
            UpdateHandlePositions(gridManager.GridSizeX, gridManager.GridSizeZ, new Vector3(gridManager.OffsetX, 0, gridManager.OffsetZ));
        }
        else
        {
            DestroyHandles();
            if (gridManager != null) gridManager.RefreshTableAndGrid();
        }
    }

    void SpawnHandles()
    {
        DestroyHandles();
        for (int i = 0; i < 8; i++)
        {
            GameObject handle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            handle.name = "ResizeHandle_" + i;
            handle.transform.localScale = Vector3.one * 1.5f;
            if (handleMaterial != null) handle.GetComponent<Renderer>().material = handleMaterial;
            handle.tag = "ResizeHandle";
            handles.Add(handle);
        }
    }

    void UpdateHandlePositions(int gridX, int gridZ, Vector3 center)
    {
        if (handles.Count < 8) return;

        float halfX = (gridX * gridManager.CellSize) / 2f;
        float halfZ = (gridZ * gridManager.CellSize) / 2f;
        float y = 0.5f;

        handles[0].transform.position = center + new Vector3(halfX, y, 0);
        handles[1].transform.position = center + new Vector3(-halfX, y, 0);
        handles[2].transform.position = center + new Vector3(0, y, halfZ);
        handles[3].transform.position = center + new Vector3(0, y, -halfZ);
        handles[4].transform.position = center + new Vector3(halfX, y, halfZ);
        handles[5].transform.position = center + new Vector3(-halfX, y, halfZ);
        handles[6].transform.position = center + new Vector3(halfX, y, -halfZ);
        handles[7].transform.position = center + new Vector3(-halfX, y, -halfZ);
    }

    void DestroyHandles()
    {
        foreach (var h in handles) Destroy(h);
        handles.Clear();
    }

    void Update()
    {
        if (!isResizingMode) return;

        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.Z))
        {
            if (undoStack.Count > 0)
            {
                MapState prevState = undoStack.Pop();
                SaveSizeToDatabase(prevState.x, prevState.z, prevState.pos);
                // Оновлюємо кульки під відкат
                UpdateHandlePositions(prevState.x, prevState.z, prevState.pos);
            }
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (hit.collider.CompareTag("ResizeHandle"))
                {
                    draggedHandle = hit.collider.gameObject;

                    initialGridX = gridManager.GridSizeX;
                    initialGridZ = gridManager.GridSizeZ;
                    initialTablePos = new Vector3(gridManager.OffsetX, 0, gridManager.OffsetZ);

                    undoStack.Push(new MapState(initialGridX, initialGridZ, initialTablePos));

                    Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
                    if (groundPlane.Raycast(ray, out float d))
                    {
                        initialDragPos = ray.GetPoint(d);
                    }

                    // Скидаємо запобіжник, щоб відмалювало перший кадр
                    lastPreviewX = -1;
                }
            }
        }

        if (Input.GetMouseButtonUp(0) && draggedHandle != null)
        {
            SaveSizeToDatabase(lastPreviewX, lastPreviewZ, lastPreviewPos);
            draggedHandle = null;
        }

        if (Input.GetMouseButton(0) && draggedHandle != null)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

            if (groundPlane.Raycast(ray, out float dist))
            {
                Vector3 currentMousePos = ray.GetPoint(dist);
                Vector3 dragDelta = currentMousePos - initialDragPos;
                float cs = gridManager.CellSize;

                int effDX = Mathf.RoundToInt(dragDelta.x / cs);
                int effDZ = Mathf.RoundToInt(dragDelta.z / cs);

                int idx = int.Parse(draggedHandle.name.Split('_')[1]);
                int newX = initialGridX;
                int newZ = initialGridZ;
                Vector3 newPos = initialTablePos;

                if (idx == 0 || idx == 4 || idx == 6)
                {
                    if (initialGridX + effDX < 10) effDX = 10 - initialGridX;
                    newX = initialGridX + effDX;
                    newPos.x = initialTablePos.x + (effDX * cs) / 2f;
                }
                else if (idx == 1 || idx == 5 || idx == 7)
                {
                    if (initialGridX - effDX < 10) effDX = initialGridX - 10;
                    newX = initialGridX - effDX;
                    newPos.x = initialTablePos.x + (effDX * cs) / 2f;
                }

                if (idx == 2 || idx == 4 || idx == 5)
                {
                    if (initialGridZ + effDZ < 10) effDZ = 10 - initialGridZ;
                    newZ = initialGridZ + effDZ;
                    newPos.z = initialTablePos.z + (effDZ * cs) / 2f;
                }
                else if (idx == 3 || idx == 6 || idx == 7)
                {
                    if (initialGridZ - effDZ < 10) effDZ = initialGridZ - 10;
                    newZ = initialGridZ - effDZ;
                    newPos.z = initialTablePos.z + (effDZ * cs) / 2f;
                }

                // ОПТИМІЗАЦІЯ: Перемальовуємо сітку ТІЛЬКИ якщо мишка проїхала цілу клітинку!
                if (newX != lastPreviewX || newZ != lastPreviewZ || newPos != lastPreviewPos)
                {
                    if (gridManager != null) gridManager.PreviewMap(newX, newZ, newPos);
                    UpdateHandlePositions(newX, newZ, newPos);

                    lastPreviewX = newX;
                    lastPreviewZ = newZ;
                    lastPreviewPos = newPos;
                }
            }
        }
    }

    void SaveSizeToDatabase(int x, int z, Vector3 pos)
    {
        InitFirebase();
        var data = new Dictionary<string, object>
        {
            { "gridSizeX", x },
            { "gridSizeZ", z },
            { "posX", pos.x },
            { "posZ", pos.z }
        };
        db.Collection("Worlds").Document(worldID).UpdateAsync(data);
    }
}