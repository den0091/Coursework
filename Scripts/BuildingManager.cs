using UnityEngine;
using Firebase.Firestore;
using Firebase.Extensions;
using System.Collections.Generic;
using System;
using UnityEngine.EventSystems;

public class BuildingManager : MonoBehaviour
{
    [Header("База Префабів")]
    public GameObject wallPrefab;
    public GameObject floorPrefab;
    public GameObject halfWallPrefab;
    public GameObject columnPrefab;

    [Header("Візуал")]
    public Material hologramMaterial;

    public enum BuildMode { None, Wall, Floor, HalfWall, Column, Delete }
    public BuildMode currentMode = BuildMode.None;

    private FirebaseFirestore db;
    private string worldID;
    private Dictionary<string, GameObject> builtObjects = new Dictionary<string, GameObject>();
    private ListenerRegistration buildListener;
    private GridManager gridManager;
    private float currentRotation = 0f;

    // ЗМІННІ ДЛЯ МАСОВОГО БУДІВНИЦТВА (Drag & Build)
    private bool isDragging = false;
    private Vector3 dragStartPos;
    private List<GameObject> dragPreviews = new List<GameObject>();

    void Start()
    {
        db = FirebaseFirestore.DefaultInstance;
        worldID = PlayerPrefs.GetString("RoomID", "test_world");
        gridManager = FindFirstObjectByType<GridManager>();

        if (PlayerPrefs.GetInt("IsMaster", 0) == 1) ListenToBuildings();
        else { ListenToBuildings(); this.enabled = false; }
    }

    void Update()
    {
        if (currentMode == BuildMode.None) return;

        // Скасування будівництва
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            if (isDragging) { isDragging = false; ClearPreviews(); return; }
            SetModeNone();
            return;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            if (!isDragging) ClearPreviews();
            return;
        }

        // Обертання
        if (Input.GetKeyDown(KeyCode.R) && !isDragging)
        {
            currentRotation += 90f;
            if (currentRotation >= 360f) currentRotation = 0f;
        }

        if (GetBuildHit(out Vector3 hitPoint, out Vector3 hitNormal))
        {
            Vector3 placePos = hitPoint + hitNormal * 0.1f;
            Vector3 snappedPos = gridManager.SnapToGrid(placePos);

            float snappedY = Mathf.Floor(placePos.y * 2f) / 2f;
            float yOffset = GetYOffset();
            snappedPos.y = snappedY + yOffset;

            // ПОЧАТОК ВИДІЛЕННЯ РАМКИ
            if (Input.GetMouseButtonDown(0))
            {
                isDragging = true;
                dragStartPos = snappedPos;
            }

            // ПІД ЧАС ВИДІЛЕННЯ (Малюємо голограми)
            if (isDragging)
            {
                bool hollow = Input.GetKey(KeyCode.LeftShift); // Затиснутий Shift = порожній центр

                // Фіксуємо висоту по стартовій точці, щоб можна було будувати рівні мости/дахи
                Vector3 dragEndPos = new Vector3(snappedPos.x, dragStartPos.y, snappedPos.z);

                List<Vector3> dragPoints = GetDragPositions(dragStartPos, dragEndPos, hollow);
                UpdatePreviews(dragPoints);

                // КІНЕЦЬ ВИДІЛЕННЯ (Відпускаємо кнопку - Будуємо!)
                if (Input.GetMouseButtonUp(0))
                {
                    isDragging = false;
                    CommitBatchAction(dragPoints);
                    ClearPreviews(); // Очищаємо екран після будівництва
                }
            }
            else
            {
                // Режим прицілу (один блок)
                UpdatePreviews(new List<Vector3> { snappedPos });
            }
        }
        else
        {
            if (!isDragging) ClearPreviews();
        }
    }

    // МАТЕМАТИКА РАМКИ: Отримуємо всі клітинки між стартом і кінцем
    List<Vector3> GetDragPositions(Vector3 start, Vector3 end, bool hollow)
    {
        List<Vector3> positions = new List<Vector3>();
        float cs = gridManager.CellSize;

        float minX = Mathf.Min(start.x, end.x);
        float maxX = Mathf.Max(start.x, end.x);
        float minZ = Mathf.Min(start.z, end.z);
        float maxZ = Mathf.Max(start.z, end.z);

        for (float x = minX; x <= maxX + 0.01f; x += cs)
        {
            for (float z = minZ; z <= maxZ + 0.01f; z += cs)
            {
                // Якщо увімкнено Hollow (Shift), перевіряємо, чи це край зони
                bool isBorder = Mathf.Abs(x - minX) < 0.01f || Mathf.Abs(x - maxX) < 0.01f ||
                                Mathf.Abs(z - minZ) < 0.01f || Mathf.Abs(z - maxZ) < 0.01f;

                if (hollow && !isBorder) continue; // Пропускаємо середину

                positions.Add(new Vector3(x, start.y, z)); // Висота завжди фіксована по старту
            }
        }
        return positions;
    }

    // ОНОВЛЕННЯ ГОЛОГРАМ (Розумний пул об'єктів)
    void UpdatePreviews(List<Vector3> positions)
    {
        // Додаємо нові голограми, якщо не вистачає
        while (dragPreviews.Count < positions.Count)
        {
            GameObject p = InstantiatePreviewPrefab();
            if (p != null) dragPreviews.Add(p);
            else break;
        }

        // Розставляємо і фарбуємо їх
        for (int i = 0; i < dragPreviews.Count; i++)
        {
            if (i < positions.Count)
            {
                dragPreviews[i].SetActive(true);
                dragPreviews[i].transform.position = positions[i];
                dragPreviews[i].transform.rotation = Quaternion.Euler(0, currentRotation, 0);

                bool canBuild = CanBuildHere(positions[i]);
                if (currentMode == BuildMode.Delete) canBuild = true; // Видаляти можна завжди

                Color tintColor;
                if (currentMode == BuildMode.Delete) tintColor = new Color(1f, 0f, 0f, 0.4f); // Червоний для видалення
                else tintColor = canBuild ? new Color(0f, 1f, 0f, 0.4f) : new Color(1f, 0f, 0f, 0.4f); // Зелений/Червоний для будівництва

                Renderer[] renderers = dragPreviews[i].GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                {
                    if (r.material.HasProperty("_Color")) r.material.color = tintColor;
                    if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", tintColor);
                }
            }
            else
            {
                dragPreviews[i].SetActive(false); // Ховаємо зайві
            }
        }
    }

    // ФІНАЛЬНИЙ ЗАПИС У FIREBASE (Batch - пакетом)
    void CommitBatchAction(List<Vector3> positions)
    {
        // ВИПРАВЛЕНО: У Unity C# ця функція називається StartBatch(), а не Batch()!
        WriteBatch batch = db.StartBatch();
        int operationsCount = 0;

        foreach (Vector3 pos in positions)
        {
            if (currentMode == BuildMode.Delete)
            {
                // Шукаємо будівлю в цій точці
                Vector3 halfExtents = new Vector3(0.45f, 0.45f, 0.45f);
                Collider[] hits = Physics.OverlapBox(pos, halfExtents, Quaternion.identity);
                foreach (var hit in hits)
                {
                    if (hit.name.StartsWith("build_"))
                    {
                        DocumentReference docRef = db.Collection("Worlds").Document(worldID).Collection("Buildings").Document(hit.name);
                        batch.Delete(docRef);
                        operationsCount++;
                    }
                }
            }
            else
            {
                // Будуємо, якщо місце вільне
                if (CanBuildHere(pos))
                {
                    string bID = "build_" + Guid.NewGuid().ToString().Substring(0, 6);
                    var data = new Dictionary<string, object>
                    {
                        { "type", (int)currentMode }, { "x", pos.x }, { "y", pos.y }, { "z", pos.z }, { "rot", currentRotation }
                    };
                    DocumentReference docRef = db.Collection("Worlds").Document(worldID).Collection("Buildings").Document(bID);
                    batch.Set(docRef, data);
                    operationsCount++;
                }
            }
        }

        // Відправляємо весь масив даних одним махом
        if (operationsCount > 0)
        {
            batch.CommitAsync();
            Debug.Log($"[BuildingManager] Відправлено пакет: {operationsCount} операцій.");
        }
    }

    GameObject InstantiatePreviewPrefab()
    {
        GameObject prefab = null;
        if (currentMode == BuildMode.Wall) prefab = wallPrefab;
        else if (currentMode == BuildMode.Floor) prefab = floorPrefab;
        else if (currentMode == BuildMode.HalfWall) prefab = halfWallPrefab;
        else if (currentMode == BuildMode.Column) prefab = columnPrefab;
        else if (currentMode == BuildMode.Delete) prefab = floorPrefab; // Квадрат для зони видалення

        if (prefab != null)
        {
            GameObject obj = Instantiate(prefab);
            Destroy(obj.GetComponent<Collider>());
            foreach (var r in obj.GetComponentsInChildren<Renderer>()) if (hologramMaterial != null) r.material = hologramMaterial;
            return obj;
        }
        return null;
    }

    float GetYOffset()
    {
        if (currentMode == BuildMode.Wall) return 1f;
        if (currentMode == BuildMode.HalfWall) return 0.5f;
        if (currentMode == BuildMode.Floor) return 0.025f;
        if (currentMode == BuildMode.Column) return 1f;
        if (currentMode == BuildMode.Delete) return 0.025f;
        return 0f;
    }

    bool GetBuildHit(out Vector3 hitPoint, out Vector3 hitNormal)
    {
        hitPoint = Vector3.zero;
        hitNormal = Vector3.up;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            hitPoint = hit.point;
            hitNormal = hit.normal;
            return true;
        }
        return false;
    }

    bool CanBuildHere(Vector3 checkPos)
    {
        Vector3 halfExtents = Vector3.one * 0.45f;
        if (currentMode == BuildMode.Wall) halfExtents = new Vector3(0.45f, 0.95f, 0.45f);
        else if (currentMode == BuildMode.HalfWall) halfExtents = new Vector3(0.45f, 0.45f, 0.45f);
        else if (currentMode == BuildMode.Floor) halfExtents = new Vector3(0.45f, 0.02f, 0.45f);
        else if (currentMode == BuildMode.Column) halfExtents = new Vector3(0.1f, 0.95f, 0.1f);

        Collider[] hits = Physics.OverlapBox(checkPos, halfExtents, Quaternion.Euler(0, currentRotation, 0));
        foreach (var hit in hits) if (hit.name.StartsWith("build_")) return false;
        return true;
    }

    void ListenToBuildings()
    {
        buildListener = db.Collection("Worlds").Document(worldID).Collection("Buildings").Listen(snap =>
        {
            List<string> currentIDs = new List<string>();
            foreach (var doc in snap.Documents)
            {
                currentIDs.Add(doc.Id);
                if (!builtObjects.ContainsKey(doc.Id))
                {
                    var data = doc.ToDictionary();
                    int type = Convert.ToInt32(data["type"]);
                    float rot = data.ContainsKey("rot") ? Convert.ToSingle(data["rot"]) : 0f;

                    GameObject prefabToSpawn = null;
                    if (type == (int)BuildMode.Wall) prefabToSpawn = wallPrefab;
                    else if (type == (int)BuildMode.Floor) prefabToSpawn = floorPrefab;
                    else if (type == (int)BuildMode.HalfWall) prefabToSpawn = halfWallPrefab;
                    else if (type == (int)BuildMode.Column) prefabToSpawn = columnPrefab;

                    if (prefabToSpawn != null)
                    {
                        GameObject newObj = Instantiate(prefabToSpawn, new Vector3(Convert.ToSingle(data["x"]), Convert.ToSingle(data["y"]), Convert.ToSingle(data["z"])), Quaternion.Euler(0, rot, 0));
                        newObj.name = doc.Id;
                        builtObjects.Add(doc.Id, newObj);
                    }
                }
            }
            List<string> toRemove = new List<string>();
            foreach (var id in builtObjects.Keys) if (!currentIDs.Contains(id)) toRemove.Add(id);
            foreach (var id in toRemove) { Destroy(builtObjects[id]); builtObjects.Remove(id); }
        });
    }

    void ClearPreviews()
    {
        foreach (var p in dragPreviews) Destroy(p);
        dragPreviews.Clear();
    }

    public void SetModeWall() { currentMode = BuildMode.Wall; currentRotation = 0f; ClearPreviews(); ClearTokens(); }
    public void SetModeFloor() { currentMode = BuildMode.Floor; currentRotation = 0f; ClearPreviews(); ClearTokens(); }
    public void SetModeHalfWall() { currentMode = BuildMode.HalfWall; currentRotation = 0f; ClearPreviews(); ClearTokens(); }
    public void SetModeColumn() { currentMode = BuildMode.Column; currentRotation = 0f; ClearPreviews(); ClearTokens(); }
    public void SetModeDelete() { currentMode = BuildMode.Delete; ClearPreviews(); ClearTokens(); }

    public void SetModeNone() { currentMode = BuildMode.None; isDragging = false; ClearPreviews(); }

    void ClearTokens()
    {
        isDragging = false;
        TokenManager tm = FindFirstObjectByType<TokenManager>();
        if (tm != null) tm.ForceCursorMode();
    }

    void OnDestroy() { buildListener?.Stop(); }
}