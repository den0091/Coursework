using UnityEngine;
using Firebase.Firestore;
using Firebase.Extensions;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;
using System.Globalization;

public class TokenManager : MonoBehaviour
{
    [Header("Основні Посилання")]
    public GridManager gridManager;
    public TMP_Text modeText;
    public GameObject masterToolbar;

    [Header("Префаби Юнітів")]
    public GameObject heroPrefab;
    public GameObject monsterPrefab;

    [Header("Візуальні Асети")]
    public GameObject highlightTilePrefab;
    public Material radiusLineMat;
    public Material pathLineMat;

    [Header("Геймплей та Обмеження")]
    public int movementRange = 6;
    public float mapLimitX = 20f;
    public float mapLimitZ = 20f;
    public float maxStepHeight = 1.1f;

    public enum ToolMode { Cursor, SpawnHero, SpawnMonster, DeleteToken }
    public ToolMode currentMode = ToolMode.Cursor;

    private Transform selectedToken;
    public static bool hasSelectedToken = false; // Блокує камеру

    private bool isMaster;
    private string worldID;
    private string myPlayerPassword;
    private FirebaseFirestore db;

    private LineRenderer radiusCircleLine;
    private LineRenderer previewPathLine;
    private GameObject targetDot;
    private GameObject spawnPreview;

    private Dictionary<string, TokenUnit> activeUnits = new Dictionary<string, TokenUnit>();
    private ListenerRegistration tokenListener;

    public static bool isAiming = false;
    private Quaternion originalRotation;
    private List<Vector3> customWaypoints = new List<Vector3>();
    private Dictionary<string, string> lastProcessedPaths = new Dictionary<string, string>();
    private List<GameObject> pathHighlights = new List<GameObject>();

    private List<GameObject> waypointPins = new List<GameObject>();

    void Start()
    {
        db = FirebaseFirestore.DefaultInstance;
        if (gridManager == null) gridManager = FindFirstObjectByType<GridManager>();

        worldID = PlayerPrefs.GetString("RoomID", "test_world");
        isMaster = PlayerPrefs.GetInt("IsMaster", 0) == 1;
        myPlayerPassword = PlayerPrefs.GetString("MyTokenPassword", "");

        if (masterToolbar != null) masterToolbar.SetActive(isMaster);
        UpdateModeUI();

        CreateVisualizers();
        ListenToDatabase();
    }

    void CreateVisualizers()
    {
        GameObject circleObj = new GameObject("Visual_RadiusCircle");
        circleObj.transform.SetParent(this.transform);
        radiusCircleLine = circleObj.AddComponent<LineRenderer>();
        radiusCircleLine.material = radiusLineMat;
        radiusCircleLine.startWidth = 0.05f;
        radiusCircleLine.endWidth = 0.05f;
        radiusCircleLine.loop = true;
        radiusCircleLine.positionCount = 0;

        GameObject pathObj = new GameObject("Visual_PreviewPath");
        pathObj.transform.SetParent(this.transform);
        previewPathLine = pathObj.AddComponent<LineRenderer>();
        previewPathLine.material = pathLineMat;
        previewPathLine.startWidth = 0.08f;
        previewPathLine.endWidth = 0.08f;
        previewPathLine.positionCount = 0;
        previewPathLine.numCornerVertices = 5;

        targetDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        targetDot.name = "Visual_TargetDot";
        targetDot.transform.SetParent(this.transform);
        targetDot.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
        Destroy(targetDot.GetComponent<Collider>());
        targetDot.GetComponent<Renderer>().material = pathLineMat;
        targetDot.SetActive(false);
    }

    void Update()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            if (spawnPreview != null) spawnPreview.SetActive(false);
            return;
        }

        BuildingManager bm = FindFirstObjectByType<BuildingManager>();
        if (bm != null && bm.currentMode != BuildingManager.BuildMode.None) return;

        if (currentMode == ToolMode.Cursor) HandleSelectionMode();
        else if (currentMode == ToolMode.DeleteToken && isMaster) HandleDeleteMode();
        else if (isMaster) HandleSpawnMode();
    }

    void HandleDeleteMode()
    {
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            SetModeCursor();
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit) && hit.collider.GetComponent<TokenUnit>())
            {
                db.Collection("Worlds").Document(worldID).Collection("Tokens").Document(hit.collider.name).DeleteAsync();
            }
        }
    }

    void HandleSelectionMode()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isAiming)
            {
                isAiming = false;
                if (selectedToken != null) selectedToken.rotation = originalRotation;
                customWaypoints.Clear();
                ClearPreviewLine();
            }
            else if (selectedToken != null) DeselectAll();
            return;
        }

        if (selectedToken != null && isMaster)
        {
            if (Input.GetKey(KeyCode.LeftShift) && (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace)))
            {
                string idToDelete = selectedToken.name;
                DeselectAll();
                db.Collection("Worlds").Document(worldID).Collection("Tokens").Document(idToDelete).DeleteAsync();
                return;
            }
        }

        // ================= ЛІВА КНОПКА МИШІ =================
        if (Input.GetMouseButtonDown(0) && !isAiming)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit) && hit.collider.GetComponent<TokenUnit>())
            {
                TokenUnit clickedUnit = hit.collider.GetComponent<TokenUnit>();

                if (!isMaster)
                {
                    string dbPass = clickedUnit.password != null ? clickedUnit.password.Trim().ToLower() : "";
                    string myPass = myPlayerPassword != null ? myPlayerPassword.Trim().ToLower() : "";
                    if (string.IsNullOrEmpty(dbPass) || dbPass != myPass) { DeselectAll(); return; }
                }

                if (Input.GetKey(KeyCode.LeftAlt))
                {
                    UnitSheetUI sheet = FindFirstObjectByType<UnitSheetUI>();
                    if (sheet != null) sheet.OpenSheet(clickedUnit.gameObject.name, clickedUnit.tokenName, clickedUnit.password, clickedUnit.hp, clickedUnit.maxHp, clickedUnit.ac, clickedUnit.speed, clickedUnit.notes, clickedUnit.size);
                }
                else
                {
                    movementRange = clickedUnit.speed;
                    SelectToken(hit.collider.transform);
                }
            }
            else DeselectAll();
        }

        // 🔥 ОБЕРТАННЯ: Юніт стежить обличчям за мишкою!
        if (Input.GetMouseButton(0) && selectedToken != null && !isAiming)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            Plane groundPlane = new Plane(Vector3.up, new Vector3(0, selectedToken.position.y, 0));

            if (groundPlane.Raycast(ray, out float dist))
            {
                Vector3 lookPoint = ray.GetPoint(dist);
                Vector3 lookDir = (lookPoint - selectedToken.position).normalized;
                lookDir.y = 0; // Крутимо тільки по горизонталі

                if (lookDir.magnitude > 0.01f)
                {
                    selectedToken.rotation = Quaternion.Slerp(selectedToken.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 15f);
                    originalRotation = selectedToken.rotation;
                }
            }
        }

        if (Input.GetMouseButtonUp(0) && selectedToken != null && !isAiming)
        {
            SendRotationToDatabase(selectedToken.name, selectedToken.rotation.eulerAngles.y);
        }

        // ================= ПРАВА КНОПКА МИШІ =================
        if (selectedToken != null)
        {
            int movingSize = selectedToken.GetComponent<TokenUnit>().size;

            if (Input.GetMouseButtonDown(1))
            {
                isAiming = true;
                customWaypoints.Clear();
                originalRotation = selectedToken.rotation;

                if (TryGetWalkableSurface(selectedToken.position, selectedToken.position.y, out Vector3 startSurf))
                    customWaypoints.Add(startSurf);
                else
                    customWaypoints.Add(selectedToken.position);

                selectedToken.GetComponent<TokenUnit>().SetPickedUp(true);
            }

            if (isAiming && Input.GetMouseButton(1))
            {
                if (GetAccurateMouseGridPos(movingSize, out Vector3 targetCell))
                {
                    List<Vector3> checkPath = new List<Vector3>(customWaypoints);
                    checkPath.Add(targetCell);

                    bool isBlocked = IsPathBlocked(checkPath);
                    bool isReachable = IsPathReachable(selectedToken.position, customWaypoints, targetCell);
                    bool isOccupied = IsCellOccupied(targetCell);
                    bool fitsInCell = DoesUnitFit(targetCell);

                    bool canMove = isReachable && !isOccupied && !isBlocked && fitsInCell;

                    DrawPreviewPath(checkPath, canMove, targetCell);

                    Vector3 dir = (targetCell - selectedToken.position).normalized;
                    dir.y = 0;
                    if (dir.magnitude > 0.01f) selectedToken.rotation = Quaternion.Slerp(selectedToken.rotation, Quaternion.LookRotation(dir), 15f * Time.deltaTime);

                    if (Input.GetKeyDown(KeyCode.LeftShift) && canMove)
                    {
                        if (Vector3.Distance(customWaypoints[customWaypoints.Count - 1], targetCell) > 0.1f)
                        {
                            customWaypoints.Add(targetCell);
                        }
                    }
                }
            }

            if (isAiming && Input.GetMouseButtonUp(1))
            {
                isAiming = false;
                selectedToken.GetComponent<TokenUnit>().SetPickedUp(false);

                if (GetAccurateMouseGridPos(movingSize, out Vector3 targetCell))
                {
                    List<Vector3> finalCheckPath = new List<Vector3>(customWaypoints);
                    finalCheckPath.Add(targetCell);

                    bool isBlocked = IsPathBlocked(finalCheckPath);
                    bool isReachable = IsPathReachable(selectedToken.position, customWaypoints, targetCell);
                    bool isOccupied = IsCellOccupied(targetCell);
                    bool fitsInCell = DoesUnitFit(targetCell);

                    if (isReachable && !isOccupied && !isBlocked && fitsInCell)
                    {
                        customWaypoints.Add(targetCell);
                        SendMoveCommand(selectedToken.name, customWaypoints);
                        DeselectAll();
                    }
                    else
                    {
                        selectedToken.rotation = originalRotation;
                        selectedToken.GetComponent<TokenUnit>().PlayBonkAnimation(targetCell);
                        customWaypoints.Clear();
                        ClearPreviewLine();
                    }
                }
                else
                {
                    selectedToken.rotation = originalRotation;
                    customWaypoints.Clear();
                    ClearPreviewLine();
                }
            }
        }
    }

    Vector3 SnapForUnit(Vector3 rawPos, int unitSize)
    {
        float cs = gridManager.CellSize;
        if (unitSize % 2 != 0)
        {
            float halfCs = cs * 0.5f;
            float x = Mathf.Floor(rawPos.x / cs) * cs + halfCs;
            float z = Mathf.Floor(rawPos.z / cs) * cs + halfCs;
            return new Vector3(x, rawPos.y, z);
        }
        else
        {
            float x = Mathf.Round(rawPos.x / cs) * cs;
            float z = Mathf.Round(rawPos.z / cs) * cs;
            return new Vector3(x, rawPos.y, z);
        }
    }

    bool GetAccurateMouseGridPos(int movingSize, out Vector3 result)
    {
        result = Vector3.zero;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Vector3 hitPoint = Vector3.zero;
        bool hitFound = false;

        RaycastHit[] hits = Physics.RaycastAll(ray, 200f);
        float closestDist = Mathf.Infinity;

        foreach (var hit in hits)
        {
            if (hit.collider.isTrigger) continue;
            if (hit.collider.name.Contains("Visual_") || hit.collider.name.Contains("Highlight")) continue;
            if (hit.collider.GetComponent<TokenUnit>() != null) continue;
            if (hit.normal.y < 0.5f) continue;

            if (hit.distance < closestDist)
            {
                closestDist = hit.distance;
                hitPoint = hit.point;
                hitFound = true;
            }
        }

        if (!hitFound)
        {
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (groundPlane.Raycast(ray, out float dist)) hitPoint = ray.GetPoint(dist);
            else return false;
        }

        Vector3 snapped = SnapForUnit(hitPoint, movingSize);
        if (TryGetWalkableSurface(snapped, hitPoint.y, out Vector3 surface))
        {
            result = surface;
            return true;
        }
        return false;
    }

    bool TryGetWalkableSurface(Vector3 checkPos, float referenceY, out Vector3 surfacePoint)
    {
        surfacePoint = checkPos;
        surfacePoint.y = referenceY;

        float startY = referenceY + 4.0f;
        Ray ray = new Ray(new Vector3(checkPos.x, startY, checkPos.z), Vector3.down);
        RaycastHit[] hits = Physics.RaycastAll(ray, 8f);

        float highestY = -Mathf.Infinity;
        bool found = false;

        foreach (var hit in hits)
        {
            if (hit.collider.GetComponent<TokenUnit>() != null) continue;
            if (hit.collider.name.Contains("Visual_") || hit.collider.name.Contains("Highlight")) continue;
            if (hit.collider.isTrigger) continue;
            if (hit.normal.y < 0.5f) continue;

            if (hit.point.y > referenceY + maxStepHeight + 0.5f) continue;

            if (hit.point.y > highestY) { highestY = hit.point.y; found = true; }
        }
        if (found) { surfacePoint.y = highestY; return true; }
        return false;
    }

    bool DoesUnitFit(Vector3 targetPos)
    {
        int movingSize = selectedToken != null ? selectedToken.GetComponent<TokenUnit>().size : 1;
        float currentMaxStep = maxStepHeight * movingSize;
        float radius = (gridManager.CellSize * movingSize * 0.5f) - 0.1f;

        if (radius <= 0) return true;

        if (!TryGetWalkableSurface(targetPos, targetPos.y, out Vector3 centerSurf)) return false;

        Vector3 checkCenter = new Vector3(targetPos.x, centerSurf.y + (currentMaxStep * 0.5f) + 0.1f, targetPos.z);
        Vector3 halfExtents = new Vector3(radius, currentMaxStep * 0.5f, radius);

        Collider[] hits = Physics.OverlapBox(checkCenter, halfExtents, Quaternion.identity);
        foreach (var hit in hits)
        {
            if (hit.isTrigger) continue;
            if (hit.GetComponent<TokenUnit>() != null) continue;
            if (hit.name == "TableFloor" || hit.name.Contains("Visual_")) continue;

            if (hit.bounds.max.y > centerSurf.y + currentMaxStep) return false;
        }
        return true;
    }

    bool IsPathBlocked(List<Vector3> pathPoints)
    {
        if (pathPoints.Count < 2) return false;

        int movingSize = selectedToken != null ? selectedToken.GetComponent<TokenUnit>().size : 1;
        float currentMaxStep = maxStepHeight * movingSize;
        float radius = (gridManager.CellSize * movingSize * 0.5f) - 0.1f;
        Vector3 halfExtents = new Vector3(radius, 0.1f, radius);

        for (int i = 0; i < pathPoints.Count - 1; i++)
        {
            TryGetWalkableSurface(pathPoints[i], pathPoints[i].y, out Vector3 startP);
            TryGetWalkableSurface(pathPoints[i + 1], pathPoints[i + 1].y, out Vector3 endP);

            if (Mathf.Abs(endP.y - startP.y) > currentMaxStep) return true;
            if (!DoesUnitFit(pathPoints[i + 1])) return true;

            Vector3 startBox = new Vector3(startP.x, startP.y + currentMaxStep + 0.1f, startP.z);
            Vector3 endBox = new Vector3(endP.x, endP.y + currentMaxStep + 0.1f, endP.z);

            Vector3 dir = endBox - startBox;
            float dist = dir.magnitude;

            if (dist > 0.01f)
            {
                RaycastHit[] hits = Physics.BoxCastAll(startBox, halfExtents, dir.normalized, Quaternion.identity, dist);
                foreach (var hit in hits)
                {
                    if (hit.collider.isTrigger) continue;
                    if (hit.collider.GetComponent<TokenUnit>() != null) continue;
                    if (hit.collider.name == "TableFloor" || hit.collider.name.Contains("Visual_")) continue;

                    return true;
                }
            }
        }
        return false;
    }

    bool IsPathReachable(Vector3 start, List<Vector3> waypoints, Vector3 end)
    {
        Vector3 startFlat = new Vector3(start.x, 0, start.z);
        Vector3 endFlat = new Vector3(end.x, 0, end.z);
        if (Vector3.Distance(startFlat, endFlat) > (movementRange * gridManager.CellSize) + 0.1f) return false;
        float totalDist = 0f;
        Vector3 curr = startFlat;
        foreach (var wp in waypoints) { Vector3 flatWp = new Vector3(wp.x, 0, wp.z); totalDist += Vector3.Distance(curr, flatWp); curr = flatWp; }
        totalDist += Vector3.Distance(curr, endFlat);
        return totalDist <= (movementRange * gridManager.CellSize) * 1.5f;
    }

    bool IsCellOccupied(Vector3 checkPos)
    {
        int movingSize = selectedToken != null ? selectedToken.GetComponent<TokenUnit>().size : 1;
        foreach (var unit in activeUnits.Values)
        {
            if (selectedToken != null && unit.gameObject.name == selectedToken.name) continue;
            Vector3 uFlat = new Vector3(unit.transform.position.x, 0, unit.transform.position.z);
            Vector3 cFlat = new Vector3(checkPos.x, 0, checkPos.z);
            float existingRadius = gridManager.CellSize * (unit.size * 0.5f);
            float movingRadius = gridManager.CellSize * (movingSize * 0.5f);
            if (Vector3.Distance(uFlat, cFlat) < (existingRadius + movingRadius - 0.05f)) return true;
        }
        return false;
    }

    void SelectToken(Transform token)
    {
        selectedToken = token;
        hasSelectedToken = true;
        originalRotation = selectedToken.rotation;
        selectedToken.GetComponent<TokenUnit>().SetPickedUp(true);
        ShowRangeVisuals(token.position);
    }

    void DeselectAll()
    {
        if (selectedToken != null) selectedToken.GetComponent<TokenUnit>().SetPickedUp(false);
        selectedToken = null;
        hasSelectedToken = false;
        isAiming = false;
        customWaypoints.Clear();
        ClearVisuals();
        UpdateModeUI();
    }

    void ShowRangeVisuals(Vector3 centerPos)
    {
        ClearVisuals();
        float maxDist = movementRange * gridManager.CellSize;
        int segments = 120;
        radiusCircleLine.positionCount = segments + 1;

        for (int i = 0; i <= segments; i++)
        {
            float angle = i * (360f / segments);
            float x = Mathf.Sin(Mathf.Deg2Rad * angle) * maxDist;
            float z = Mathf.Cos(Mathf.Deg2Rad * angle) * maxDist;

            Vector3 point = new Vector3(centerPos.x + x, centerPos.y, centerPos.z + z);

            if (TryGetWalkableSurface(point, centerPos.y, out Vector3 surfaceHit)) point.y = surfaceHit.y + 0.05f;
            else point.y = centerPos.y + 0.05f;

            radiusCircleLine.SetPosition(i, point);
        }
    }

    void DrawPreviewPath(List<Vector3> path, bool canMove, Vector3 targetCell)
    {
        if (path == null || path.Count == 0) { ClearPreviewLine(); return; }

        float pathWidth = 0.08f;
        previewPathLine.startWidth = pathWidth;
        previewPathLine.endWidth = pathWidth;

        previewPathLine.positionCount = path.Count;
        for (int i = 0; i < path.Count; i++)
        {
            float y = 0.05f;
            if (TryGetWalkableSurface(path[i], path[i].y, out Vector3 hit)) y = hit.y + 0.05f;
            previewPathLine.SetPosition(i, new Vector3(path[i].x, y, path[i].z));
        }

        Color baseColor = canMove ? Color.green : Color.red;
        baseColor.a = 0.6f;
        previewPathLine.startColor = baseColor;
        previewPathLine.endColor = baseColor;

        for (int i = 0; i < waypointPins.Count; i++) waypointPins[i].SetActive(false);

        for (int i = 1; i < customWaypoints.Count; i++)
        {
            if (i - 1 >= waypointPins.Count)
            {
                GameObject pin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pin.name = "Visual_WaypointPin";
                pin.transform.SetParent(this.transform);
                pin.transform.localScale = new Vector3(0.25f, 0.02f, 0.25f);
                Destroy(pin.GetComponent<Collider>());
                pin.GetComponent<Renderer>().material = pathLineMat;
                waypointPins.Add(pin);
            }

            GameObject currentPin = waypointPins[i - 1];
            float pinY = customWaypoints[i].y + 0.06f;
            if (TryGetWalkableSurface(customWaypoints[i], customWaypoints[i].y, out Vector3 pHit)) pinY = pHit.y + 0.06f;

            currentPin.transform.position = new Vector3(customWaypoints[i].x, pinY, customWaypoints[i].z);
            currentPin.SetActive(true);

            Color pinColor = Color.Lerp(baseColor, Color.white, 0.5f);
            currentPin.GetComponent<Renderer>().material.color = pinColor;
        }

        targetDot.SetActive(true);
        float targetY = 0.05f;
        if (TryGetWalkableSurface(targetCell, targetCell.y, out Vector3 tgtHit)) targetY = tgtHit.y + 0.05f;
        targetDot.transform.position = new Vector3(targetCell.x, targetY, targetCell.z);

        targetDot.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);

        Color dotColor = Color.Lerp(baseColor, Color.black, 0.4f);
        dotColor.a = 0.9f;
        targetDot.GetComponent<Renderer>().material.color = dotColor;
    }

    void ClearPreviewLine()
    {
        previewPathLine.positionCount = 0;
        if (targetDot) targetDot.SetActive(false);
        foreach (var pin in waypointPins) pin.SetActive(false);
    }

    void ClearVisuals() { radiusCircleLine.positionCount = 0; ClearPreviewLine(); }

    void SendMoveCommand(string tokenID, List<Vector3> path)
    {
        Vector3 finalPos = path[path.Count - 1];
        string pathStr = "";
        for (int i = 0; i < path.Count; i++)
        {
            pathStr += path[i].x.ToString(CultureInfo.InvariantCulture) + "," +
                       path[i].y.ToString(CultureInfo.InvariantCulture) + "," +
                       path[i].z.ToString(CultureInfo.InvariantCulture);
            if (i < path.Count - 1) pathStr += "|";
        }
        var data = new Dictionary<string, object> { { "x", finalPos.x }, { "y", finalPos.y }, { "z", finalPos.z }, { "pathStr", pathStr } };
        db.Collection("Worlds").Document(worldID).Collection("Tokens").Document(tokenID).UpdateAsync(data);
    }

    void SendRotationToDatabase(string tokenID, float rotY)
    {
        var data = new Dictionary<string, object> { { "rotY", rotY } };
        db.Collection("Worlds").Document(worldID).Collection("Tokens").Document(tokenID).UpdateAsync(data);
    }

    void HandleSpawnMode()
    {
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            SetModeCursor();
            return;
        }

        if (GetAccurateMouseGridPos(1, out Vector3 previewPos))
        {
            if (spawnPreview == null || spawnPreview.name != currentMode.ToString())
            {
                if (spawnPreview != null) Destroy(spawnPreview);
                GameObject prefabToSpawn = (currentMode == ToolMode.SpawnHero) ? heroPrefab : monsterPrefab;
                spawnPreview = Instantiate(prefabToSpawn);
                spawnPreview.name = currentMode.ToString();

                Destroy(spawnPreview.GetComponent<TokenUnit>());
                Destroy(spawnPreview.GetComponent<Collider>());

                Renderer[] rs = spawnPreview.GetComponentsInChildren<Renderer>();
                foreach (var r in rs)
                {
                    Color c = r.material.color;
                    r.material.color = new Color(c.r, c.g, c.b, 0.4f);
                }
            }

            spawnPreview.SetActive(true);
            spawnPreview.transform.position = previewPos;

            if (Input.GetMouseButtonDown(0))
            {
                if (IsCellOccupied(previewPos)) return;

                string tokenID = "token_" + Guid.NewGuid().ToString().Substring(0, 5);
                int type = (currentMode == ToolMode.SpawnHero) ? 0 : 1;
                var data = new Dictionary<string, object>
                {
                    { "type", type }, { "x", previewPos.x }, { "y", previewPos.y }, { "z", previewPos.z }, { "size", 1 }
                };
                db.Collection("Worlds").Document(worldID).Collection("Tokens").Document(tokenID).SetAsync(data);
                SetModeCursor();
            }
        }
        else
        {
            if (spawnPreview != null) spawnPreview.SetActive(false);
        }
    }

    void ListenToDatabase()
    {
        tokenListener = db.Collection("Worlds").Document(worldID).Collection("Tokens").Listen(snap =>
        {
            List<string> currentIDs = new List<string>();
            foreach (var doc in snap.Documents)
            {
                currentIDs.Add(doc.Id);
                var data = doc.ToDictionary();
                if (!data.ContainsKey("x")) continue;

                Vector3 targetPos = new Vector3(
                    Convert.ToSingle(data["x"]),
                    Convert.ToSingle(data.ContainsKey("y") ? data["y"] : 0),
                    Convert.ToSingle(data["z"])
                );

                string pStr = data.ContainsKey("pathStr") ? data["pathStr"].ToString() : "";

                string uName = data.ContainsKey("name") ? data["name"].ToString() : "Unknown";
                string uPass = data.ContainsKey("password") ? data["password"].ToString() : "";
                int uHp = data.ContainsKey("hp") ? Convert.ToInt32(data["hp"]) : 10;
                int uMaxHp = data.ContainsKey("maxHp") ? Convert.ToInt32(data["maxHp"]) : 10;
                int uAc = data.ContainsKey("ac") ? Convert.ToInt32(data["ac"]) : 10;
                int uSpeed = data.ContainsKey("speed") ? Convert.ToInt32(data["speed"]) : 6;
                string uNotes = data.ContainsKey("notes") ? data["notes"].ToString() : "";
                int uSize = data.ContainsKey("size") ? Convert.ToInt32(data["size"]) : 1;
                float uRotY = data.ContainsKey("rotY") ? Convert.ToSingle(data["rotY"]) : 0f;

                if (!activeUnits.ContainsKey(doc.Id))
                {
                    int type = Convert.ToInt32(data["type"]);
                    GameObject prefab = type == 0 ? heroPrefab : monsterPrefab;
                    GameObject newObj = Instantiate(prefab, targetPos, Quaternion.identity);
                    newObj.name = doc.Id;
                    newObj.transform.localScale = new Vector3(uSize, uSize, uSize);

                    TokenUnit unitScript = newObj.GetComponent<TokenUnit>();
                    newObj.GetComponent<LineRenderer>().material = pathLineMat;
                    unitScript.UpdateStats(uName, uPass, uHp, uMaxHp, uAc, uSpeed, uNotes, uSize);
                    activeUnits.Add(doc.Id, unitScript);

                    if (TryGetWalkableSurface(new Vector3(targetPos.x, 0, targetPos.z), targetPos.y, out Vector3 hit1)) targetPos.y = hit1.y;

                    unitScript.transform.position = new Vector3(targetPos.x, targetPos.y + (uSize * 1.5f), targetPos.z);
                    unitScript.transform.rotation = Quaternion.Euler(0, uRotY, 0);
                    lastProcessedPaths[doc.Id] = pStr;
                }
                else
                {
                    TokenUnit unit = activeUnits[doc.Id];
                    unit.transform.localScale = new Vector3(uSize, uSize, uSize);
                    unit.UpdateStats(uName, uPass, uHp, uMaxHp, uAc, uSpeed, uNotes, uSize);

                    if (selectedToken == null || selectedToken.name != doc.Id)
                    {
                        unit.transform.rotation = Quaternion.Slerp(unit.transform.rotation, Quaternion.Euler(0, uRotY, 0), Time.deltaTime * 5f);
                    }

                    if (!lastProcessedPaths.ContainsKey(doc.Id) || lastProcessedPaths[doc.Id] != pStr)
                    {
                        lastProcessedPaths[doc.Id] = pStr;
                        List<Vector3> route = new List<Vector3>();
                        if (!string.IsNullOrEmpty(pStr))
                        {
                            string[] points = pStr.Split('|');
                            foreach (var pt in points)
                            {
                                string[] coords = pt.Split(',');
                                if (coords.Length == 3)
                                {
                                    float px = float.Parse(coords[0], CultureInfo.InvariantCulture);
                                    float py = float.Parse(coords[1], CultureInfo.InvariantCulture);
                                    float pz = float.Parse(coords[2], CultureInfo.InvariantCulture);
                                    route.Add(new Vector3(px, py, pz));
                                }
                                else if (coords.Length == 2)
                                {
                                    float px = float.Parse(coords[0], CultureInfo.InvariantCulture);
                                    float pz = float.Parse(coords[1], CultureInfo.InvariantCulture);
                                    if (TryGetWalkableSurface(new Vector3(px, 0, pz), targetPos.y, out Vector3 hit2)) route.Add(hit2);
                                    else route.Add(new Vector3(px, 0, pz));
                                }
                            }
                        }

                        if (route.Count > 0)
                        {
                            unit.MoveAlongPath(route);
                        }
                        else
                        {
                            if (TryGetWalkableSurface(new Vector3(targetPos.x, 0, targetPos.z), targetPos.y, out Vector3 hit3)) targetPos.y = hit3.y;
                            unit.transform.position = new Vector3(targetPos.x, targetPos.y + (uSize * 1.5f), targetPos.z);
                        }
                    }
                }
            }

            List<string> toRemove = new List<string>();
            foreach (var id in activeUnits.Keys) if (!currentIDs.Contains(id)) toRemove.Add(id);
            foreach (var id in toRemove) { Destroy(activeUnits[id].gameObject); activeUnits.Remove(id); lastProcessedPaths.Remove(id); }
        });
    }

    void OnDestroy() { tokenListener?.Stop(); }

    public void SetModeCursor() { currentMode = ToolMode.Cursor; ClearSpawnPreview(); UpdateModeUI(); ClearBuilder(); }
    public void SetModeHero() { currentMode = ToolMode.SpawnHero; ClearSpawnPreview(); UpdateModeUI(); ClearBuilder(); }
    public void SetModeMonster() { currentMode = ToolMode.SpawnMonster; ClearSpawnPreview(); UpdateModeUI(); ClearBuilder(); }
    public void SetModeDeleteToken() { currentMode = ToolMode.DeleteToken; ClearSpawnPreview(); UpdateModeUI(); ClearBuilder(); }

    public void ForceCursorMode()
    {
        currentMode = ToolMode.Cursor;
        ClearSpawnPreview();
        DeselectAll();
    }

    void ClearSpawnPreview()
    {
        if (spawnPreview != null) Destroy(spawnPreview);
    }

    void ClearBuilder()
    {
        BuildingManager bm = FindFirstObjectByType<BuildingManager>();
        if (bm != null && bm.currentMode != BuildingManager.BuildMode.None) bm.SetModeNone();
    }

    void UpdateModeUI()
    {
        if (modeText)
        {
            if (isMaster) modeText.text = $"Інструмент: {currentMode}";
            else modeText.text = "";
        }
    }
}