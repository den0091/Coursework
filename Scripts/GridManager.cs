using UnityEngine;
using Firebase.Firestore;
using Firebase.Extensions;
using System.Collections.Generic;
using System;

public class GridManager : MonoBehaviour
{
    [Header("Налаштування візуалу сітки")]
    public float lineWidth = 0.05f;
    public Color lineColor = new Color(1f, 1f, 1f, 0.3f);
    public Color tableColor = new Color(0.15f, 0.15f, 0.15f);

    public int GridSizeX { get; private set; } = 20;
    public int GridSizeZ { get; private set; } = 20;
    public float OffsetX { get; private set; } = 0f;
    public float OffsetZ { get; private set; } = 0f;
    public float CellSize = 1f;

    private FirebaseFirestore db;
    private string worldID;
    private ListenerRegistration gridListener;

    private GameObject currentTable;
    private GameObject currentGridContainer;

    void Start()
    {
        db = FirebaseFirestore.DefaultInstance;
        worldID = PlayerPrefs.GetString("RoomID", "test_world");

        GridSizeX = PlayerPrefs.GetInt("GridSize", 20);
        GridSizeZ = GridSizeX;

        ListenToGridChanges();
    }

    void ListenToGridChanges()
    {
        gridListener = db.Collection("Worlds").Document(worldID).Listen(snapshot =>
        {
            if (snapshot.Exists)
            {
                var data = snapshot.ToDictionary();
                if (data.ContainsKey("gridSizeX")) GridSizeX = Convert.ToInt32(data["gridSizeX"]);
                if (data.ContainsKey("gridSizeZ")) GridSizeZ = Convert.ToInt32(data["gridSizeZ"]);
                if (data.ContainsKey("posX")) OffsetX = Convert.ToSingle(data["posX"]);
                if (data.ContainsKey("posZ")) OffsetZ = Convert.ToSingle(data["posZ"]);

                RefreshTableAndGrid();
            }
            else
            {
                RefreshTableAndGrid();
            }
        });
    }

    public void RefreshTableAndGrid()
    {
        if (currentTable != null) Destroy(currentTable);
        if (currentGridContainer != null) Destroy(currentGridContainer);

        GenerateTableFloor();
        GenerateGridVisuals();
    }

    void GenerateTableFloor()
    {
        currentTable = GameObject.CreatePrimitive(PrimitiveType.Plane);
        currentTable.name = "TableFloor";

        float planeScaleX = (GridSizeX * CellSize) / 10f;
        float planeScaleZ = (GridSizeZ * CellSize) / 10f;
        currentTable.transform.localScale = new Vector3(planeScaleX, 1f, planeScaleZ);
        currentTable.transform.position = new Vector3(OffsetX, 0, OffsetZ);

        Renderer rend = currentTable.GetComponent<Renderer>();
        if (rend != null)
        {
            Material tableMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            tableMat.color = tableColor;
            rend.material = tableMat;
        }

        currentTable.layer = LayerMask.NameToLayer("Default");
    }

    void GenerateGridVisuals()
    {
        currentGridContainer = new GameObject("GridLines");
        currentGridContainer.transform.SetParent(this.transform, false);
        currentGridContainer.transform.position = new Vector3(OffsetX, 0, OffsetZ);

        float halfX = (GridSizeX * CellSize) / 2f;
        float halfZ = (GridSizeZ * CellSize) / 2f;

        for (int x = 0; x <= GridSizeX; x++)
        {
            float xPos = -halfX + (x * CellSize);
            Vector3 startPos = new Vector3(xPos, 0.01f, -halfZ);
            Vector3 endPos = new Vector3(xPos, 0.01f, halfZ);
            DrawLine(currentGridContainer.transform, startPos, endPos, $"Line_Z_{x}");
        }

        for (int z = 0; z <= GridSizeZ; z++)
        {
            float zPos = -halfZ + (z * CellSize);
            Vector3 startPos = new Vector3(-halfX, 0.01f, zPos);
            Vector3 endPos = new Vector3(halfX, 0.01f, zPos);
            DrawLine(currentGridContainer.transform, startPos, endPos, $"Line_X_{z}");
        }
    }

    // НОВЕ: Функція швидкого перемальовування ПІД ЧАС потягушки
    public void PreviewMap(int tempX, int tempZ, Vector3 tempPos)
    {
        if (currentTable != null)
        {
            currentTable.transform.localScale = new Vector3((tempX * CellSize) / 10f, 1f, (tempZ * CellSize) / 10f);
            currentTable.transform.position = tempPos;
        }

        if (currentGridContainer != null) Destroy(currentGridContainer);

        currentGridContainer = new GameObject("GridLines");
        currentGridContainer.transform.SetParent(this.transform, false);
        currentGridContainer.transform.position = new Vector3(tempPos.x, 0, tempPos.z);

        float halfX = (tempX * CellSize) / 2f;
        float halfZ = (tempZ * CellSize) / 2f;

        for (int x = 0; x <= tempX; x++)
        {
            float xPos = -halfX + (x * CellSize);
            Vector3 startPos = new Vector3(xPos, 0.01f, -halfZ);
            Vector3 endPos = new Vector3(xPos, 0.01f, halfZ);
            DrawLine(currentGridContainer.transform, startPos, endPos, $"Line_Z_{x}");
        }

        for (int z = 0; z <= tempZ; z++)
        {
            float zPos = -halfZ + (z * CellSize);
            Vector3 startPos = new Vector3(-halfX, 0.01f, zPos);
            Vector3 endPos = new Vector3(halfX, 0.01f, zPos);
            DrawLine(currentGridContainer.transform, startPos, endPos, $"Line_X_{z}");
        }
    }

    void DrawLine(Transform parent, Vector3 start, Vector3 end, string name)
    {
        GameObject lineObj = new GameObject(name);
        lineObj.transform.SetParent(parent, false);

        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lineColor;
        lr.endColor = lineColor;
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.useWorldSpace = false;
    }

    public Vector3 SnapToGrid(Vector3 hitPoint)
    {
        float halfX = (GridSizeX * CellSize) / 2f;
        float halfZ = (GridSizeZ * CellSize) / 2f;

        float localX = hitPoint.x - (OffsetX - halfX);
        float localZ = hitPoint.z - (OffsetZ - halfZ);

        float snappedLocalX = Mathf.Floor(localX / CellSize) * CellSize + (CellSize / 2f);
        float snappedLocalZ = Mathf.Floor(localZ / CellSize) * CellSize + (CellSize / 2f);

        float finalX = snappedLocalX + (OffsetX - halfX);
        float finalZ = snappedLocalZ + (OffsetZ - halfZ);

        return new Vector3(finalX, 0f, finalZ);
    }

    void OnDestroy()
    {
        gridListener?.Stop();
    }
}