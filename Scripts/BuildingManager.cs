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
    public GameObject doorPrefab;
    public GameObject torchPrefab;

    [Header("Візуал")]
    public Material hologramMaterial;

    public enum BuildMode { None, Wall, Floor, HalfWall, Column, Delete, Door, Torch }
    public BuildMode currentMode = BuildMode.None;

    private FirebaseFirestore db;
    private string worldID;
    private Dictionary<string, GameObject> builtObjects = new Dictionary<string, GameObject>();
    private ListenerRegistration buildListener;
    private GridManager gridManager;
    private float currentRotation = 0f;

    private bool isDragging = false;
    private Vector3 dragStartPos;
    private List<GameObject> dragPreviews = new List<GameObject>();
    private Vector3 lastHitNormal = Vector3.up;

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

        if (Input.GetKeyDown(KeyCode.R) && !isDragging)
        {
            currentRotation += 90f;
            if (currentRotation >= 360f) currentRotation = 0f;
        }

        if (GetBuildHit(out Vector3 hitPoint, out Vector3 hitNormal))
        {
            lastHitNormal = hitNormal;

            Vector3 placePos = hitPoint + hitNormal * 0.1f;
            Vector3 snappedPos = gridManager.SnapToGrid(placePos);

            float snappedY = Mathf.Floor(placePos.y * 2f) / 2f;
            float yOffset = GetYOffset();
            snappedPos.y = snappedY + yOffset;

            if (currentMode == BuildMode.Torch)
            {
                snappedPos = hitPoint + hitNormal * 0.05f;
                snappedPos.x = Mathf.Round(snappedPos.x * 2f) / 2f;
                snappedPos.z = Mathf.Round(snappedPos.z * 2f) / 2f;
                snappedPos.y = Mathf.Round(snappedPos.y * 2f) / 2f;
            }

            if (Input.GetMouseButtonDown(0))
            {
                isDragging = true;
                dragStartPos = snappedPos;
            }

            if (isDragging)
            {
                bool hollow = Input.GetKey(KeyCode.LeftShift);
                Vector3 dragEndPos = new Vector3(snappedPos.x, dragStartPos.y, snappedPos.z);
                List<Vector3> dragPoints = GetDragPositions(dragStartPos, dragEndPos, hollow);
                UpdatePreviews(dragPoints, hitNormal);

                if (Input.GetMouseButtonUp(0))
                {
                    isDragging = false;
                    CommitBatchAction(dragPoints, hitNormal);
                    ClearPreviews();
                }
            }
            else
            {
                UpdatePreviews(new List<Vector3> { snappedPos }, hitNormal);
            }
        }
        else
        {
            if (!isDragging) ClearPreviews();
        }
    }

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
                bool isBorder = Mathf.Abs(x - minX) < 0.01f || Mathf.Abs(x - maxX) < 0.01f ||
                                Mathf.Abs(z - minZ) < 0.01f || Mathf.Abs(z - maxZ) < 0.01f;
                if (hollow && !isBorder) continue;
                positions.Add(new Vector3(x, start.y, z));
            }
        }
        return positions;
    }

    void UpdatePreviews(List<Vector3> positions, Vector3 hitNormal)
    {
        while (dragPreviews.Count < positions.Count)
        {
            GameObject p = InstantiatePreviewPrefab();
            if (p != null) dragPreviews.Add(p);
            else break;
        }

        for (int i = 0; i < dragPreviews.Count; i++)
        {
            if (i < positions.Count)
            {
                dragPreviews[i].SetActive(true);
                dragPreviews[i].transform.position = positions[i];

                if (currentMode == BuildMode.Torch)
                {
                    if (Mathf.Abs(hitNormal.y) > 0.9f)
                    {
                        dragPreviews[i].transform.rotation = Quaternion.Euler(0f, currentRotation, 0f);
                    }
                    else
                    {
                        Quaternion wallRot = Quaternion.LookRotation(-hitNormal, Vector3.up);
                        dragPreviews[i].transform.rotation = wallRot * Quaternion.Euler(-22.5f, 0f, 0f);
                    }
                }
                else
                {
                    dragPreviews[i].transform.rotation = Quaternion.Euler(0, currentRotation, 0);
                }

                bool canBuild = CanBuildHere(positions[i]);
                if (currentMode == BuildMode.Delete) canBuild = true;

                Color tintColor;
                if (currentMode == BuildMode.Delete) tintColor = new Color(1f, 0f, 0f, 0.4f);
                else tintColor = canBuild ? new Color(0f, 1f, 0f, 0.4f) : new Color(1f, 0f, 0f, 0.4f);

                Renderer[] renderers = dragPreviews[i].GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                {
                    if (r.material.HasProperty("_Color")) r.material.color = tintColor;
                    if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", tintColor);
                }
            }
            else
            {
                dragPreviews[i].SetActive(false);
            }
        }
    }

    void CommitBatchAction(List<Vector3> positions, Vector3 normal)
    {
        WriteBatch batch = db.StartBatch();
        int operationsCount = 0;

        foreach (Vector3 pos in positions)
        {
            if (currentMode == BuildMode.Delete)
            {
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
                if (CanBuildHere(pos))
                {
                    string bID = "build_" + Guid.NewGuid().ToString().Substring(0, 6);
                    var data = new Dictionary<string, object>
                    {
                        { "type", (int)currentMode },
                        { "x", pos.x }, { "y", pos.y }, { "z", pos.z },
                        { "rot", currentRotation },
                        { "nX", normal.x }, { "nY", normal.y }, { "nZ", normal.z }
                    };
                    DocumentReference docRef = db.Collection("Worlds").Document(worldID).Collection("Buildings").Document(bID);
                    batch.Set(docRef, data);
                    operationsCount++;
                }
            }
        }

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
        else if (currentMode == BuildMode.Delete) prefab = floorPrefab;
        else if (currentMode == BuildMode.Door) prefab = doorPrefab;
        else if (currentMode == BuildMode.Torch) prefab = torchPrefab;

        if (prefab != null)
        {
            GameObject obj = Instantiate(prefab);
            Destroy(obj.GetComponent<Collider>());
            foreach (var r in obj.GetComponentsInChildren<Renderer>())
                if (hologramMaterial != null) r.material = hologramMaterial;
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
        if (currentMode == BuildMode.Door) return 1f;
        return 0f;
    }

    bool GetBuildHit(out Vector3 hitPoint, out Vector3 hitNormal)
    {
        hitPoint = Vector3.zero;
        hitNormal = Vector3.up;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        if (currentMode == BuildMode.Torch)
        {
            RaycastHit[] allHits = Physics.RaycastAll(ray, 100f);
            System.Array.Sort(allHits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in allHits)
            {
                if (hit.collider.name.StartsWith("build_") || hit.collider.name == "TableFloor")
                {
                    hitPoint = hit.point;
                    hitNormal = hit.normal;
                    return true;
                }
            }
            return false;
        }

        if (Physics.Raycast(ray, out RaycastHit h, 100f))
        {
            hitPoint = h.point;
            hitNormal = h.normal;
            return true;
        }
        return false;
    }

    bool CanBuildHere(Vector3 checkPos)
    {
        if (currentMode == BuildMode.Torch) return true;

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
                    else if (type == (int)BuildMode.Door) prefabToSpawn = doorPrefab;
                    else if (type == (int)BuildMode.Torch) prefabToSpawn = torchPrefab;

                    if (prefabToSpawn != null)
                    {
                        Vector3 spawnPos = new Vector3(
                            Convert.ToSingle(data["x"]),
                            Convert.ToSingle(data["y"]),
                            Convert.ToSingle(data["z"])
                        );

                        Quaternion spawnRot = Quaternion.Euler(0, rot, 0);

                        if (type == (int)BuildMode.Torch && data.ContainsKey("nX"))
                        {
                            Vector3 normal = new Vector3(
                                Convert.ToSingle(data["nX"]),
                                Convert.ToSingle(data["nY"]),
                                Convert.ToSingle(data["nZ"])
                            );

                            if (Mathf.Abs(normal.y) > 0.9f)
                            {
                                spawnRot = Quaternion.Euler(0f, rot, 0f);
                            }
                            else
                            {
                                Quaternion wallRot = Quaternion.LookRotation(-normal, Vector3.up);
                                spawnRot = wallRot * Quaternion.Euler(-22.5f, 0f, 0f);
                            }
                        }

                        GameObject newObj = Instantiate(prefabToSpawn, spawnPos, spawnRot);
                        newObj.name = doc.Id;
                        builtObjects.Add(doc.Id, newObj);
                    }
                }
            }

            List<string> toRemove = new List<string>();
            foreach (var id in builtObjects.Keys)
                if (!currentIDs.Contains(id)) toRemove.Add(id);
            foreach (var id in toRemove)
            {
                Destroy(builtObjects[id]);
                builtObjects.Remove(id);
            }
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
    public void SetModeDoor() { currentMode = BuildMode.Door; currentRotation = 0f; ClearPreviews(); ClearTokens(); }
    public void SetModeTorch() { currentMode = BuildMode.Torch; currentRotation = 0f; ClearPreviews(); ClearTokens(); }
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
