using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class RoomArrayGenerator : MonoBehaviour
{
    [Header("Prefab")]
    [Tooltip("房间 Prefab")]
    public GameObject roomPrefab;

    [Tooltip("顶部填充 Prefab。建议原始尺寸为 1x1x1，Scale 为 (1,1,1)。")]
    public GameObject topFillPrefab;

    [Tooltip("侧面封边 Prefab。X/Z 共用，建议原始尺寸为 1x1x1，Scale 为 (1,1,1)。")]
    public GameObject sideWallPrefab;

    [Header("阵列规模")]
    [Min(1)] public int rows = 3;      // Z 方向
    [Min(1)] public int columns = 9;   // X 方向

    [Header("房间尺寸（格）")]
    [Min(0.01f)] public float roomWidth = 22f;   // X
    [Min(0.01f)] public float roomDepth = 22f;   // Z
    [Min(0.01f)] public float roomHeight = 4f;   // Y

    [Header("间距（格）")]
    [Tooltip("相邻房间在 X 方向的空隙")]
    [Min(0f)] public float gapX = 3f;

    [Tooltip("相邻房间在 Z 方向的空隙")]
    [Min(0f)] public float gapZ = 5f;

    [Header("顶部填充")]
    [Tooltip("顶部填充层厚度，默认 1 格")]
    [Min(0.01f)] public float fillHeight = 1f;

    [Tooltip("顶部填充层底部所在高度。默认等于房间高度，即从房顶开始铺。")]
    public float fillBaseY = 4f;

    [Header("侧面封边")]
    [Tooltip("侧面墙厚度（格）")]
    [Min(0.01f)] public float sideWallThickness = 1f;

    [Header("单位换算")]
    [Tooltip("1 格 = 多少 Unity 单位")]
    [Min(0.0001f)] public float cellSize = 1f;

    [Header("布局")]
    [Tooltip("阵列是否以当前物体为中心自动居中")]
    public bool autoCenter = true;

    [Header("不生成房间的位置")]
    [Tooltip("x=列, y=排。比如 (0,0) 表示第1排第1列。")]
    public List<Vector2Int> blockedCells = new List<Vector2Int>();

    [Header("根节点")]
    public string generatedRootName = "GeneratedRooms";
    public string roomRootName = "Rooms";
    public string topFillRootName = "TopFill";
    public string sideWallRootName = "SideWalls";

    private const string RoomPrefix = "Room_";
    private const string FillPrefix = "Fill_";
    private const string SidePrefix = "Side_";

    public void GenerateAll()
    {
        if (roomPrefab == null)
        {
            Debug.LogWarning("RoomArrayGenerator: 未指定 roomPrefab。", this);
            return;
        }

        Transform generatedRoot = GetOrCreateChild(transform, generatedRootName);
        ClearChildren(generatedRoot);

        Transform roomRoot = GetOrCreateChild(generatedRoot, roomRootName);
        Transform topFillRoot = GetOrCreateChild(generatedRoot, topFillRootName);
        Transform sideWallRoot = GetOrCreateChild(generatedRoot, sideWallRootName);

        Dictionary<Vector2Int, Transform> rooms = GenerateRooms(roomRoot);
        GenerateTopFill(topFillRoot, rooms);
        GenerateSideWalls(sideWallRoot, rooms);
    }

    public void GenerateRoomsOnly()
    {
        if (roomPrefab == null)
        {
            Debug.LogWarning("RoomArrayGenerator: 未指定 roomPrefab。", this);
            return;
        }

        Transform generatedRoot = GetOrCreateChild(transform, generatedRootName);
        Transform roomRoot = GetOrCreateChild(generatedRoot, roomRootName);

        ClearChildren(roomRoot);
        GenerateRooms(roomRoot);
    }

    public void GenerateTopFillOnly()
    {
        if (topFillPrefab == null)
        {
            Debug.LogWarning("RoomArrayGenerator: 未指定 topFillPrefab。", this);
            return;
        }

        Transform generatedRoot = GetOrCreateChild(transform, generatedRootName);
        Transform roomRoot = GetOrCreateChild(generatedRoot, roomRootName);
        Transform topFillRoot = GetOrCreateChild(generatedRoot, topFillRootName);

        ClearChildren(topFillRoot);

        Dictionary<Vector2Int, Transform> rooms = CollectExistingRooms(roomRoot);
        GenerateTopFill(topFillRoot, rooms);
    }

    public void GenerateSideWallsOnly()
    {
        if (sideWallPrefab == null)
        {
            Debug.LogWarning("RoomArrayGenerator: 未指定 sideWallPrefab。", this);
            return;
        }

        Transform generatedRoot = GetOrCreateChild(transform, generatedRootName);
        Transform roomRoot = GetOrCreateChild(generatedRoot, roomRootName);
        Transform sideWallRoot = GetOrCreateChild(generatedRoot, sideWallRootName);

        ClearChildren(sideWallRoot);

        Dictionary<Vector2Int, Transform> rooms = CollectExistingRooms(roomRoot);
        GenerateSideWalls(sideWallRoot, rooms);
    }

    public void ClearTopFillOnly()
    {
        Transform generatedRoot = transform.Find(generatedRootName);
        if (generatedRoot == null)
            return;

        Transform root = generatedRoot.Find(topFillRootName);
        if (root != null)
            ClearChildren(root);
    }

    public void ClearSideWallsOnly()
    {
        Transform generatedRoot = transform.Find(generatedRootName);
        if (generatedRoot == null)
            return;

        Transform root = generatedRoot.Find(sideWallRootName);
        if (root != null)
            ClearChildren(root);
    }

    public void ClearAllGenerated()
    {
        Transform generatedRoot = transform.Find(generatedRootName);
        if (generatedRoot != null)
            ClearChildren(generatedRoot);
    }

    private Dictionary<Vector2Int, Transform> GenerateRooms(Transform roomRoot)
    {
        Dictionary<Vector2Int, Transform> spawnedRooms = new Dictionary<Vector2Int, Transform>();
        HashSet<Vector2Int> blocked = BuildBlockedSet();

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                Vector2Int cell = new Vector2Int(col, row);
                if (blocked.Contains(cell))
                    continue;

                Transform room = InstantiatePrefab(roomPrefab, roomRoot);
                if (room == null)
                    continue;

                room.name = $"{RoomPrefix}R{row}_C{col}";
                room.localPosition = GetRoomLocalPosition(col, row);
                room.localRotation = Quaternion.identity;
                room.localScale = Vector3.one;

                spawnedRooms[cell] = room;
            }
        }

        return spawnedRooms;
    }

    private Dictionary<Vector2Int, Transform> CollectExistingRooms(Transform roomRoot)
    {
        Dictionary<Vector2Int, Transform> result = new Dictionary<Vector2Int, Transform>();
        if (roomRoot == null)
            return result;

        for (int i = 0; i < roomRoot.childCount; i++)
        {
            Transform child = roomRoot.GetChild(i);
            if (child == null)
                continue;

            if (!TryParseRoomName(child.name, out int row, out int col))
                continue;

            Vector2Int key = new Vector2Int(col, row);
            if (!result.ContainsKey(key))
                result.Add(key, child);
        }

        return result;
    }

    private void GenerateTopFill(Transform fillRoot, Dictionary<Vector2Int, Transform> rooms)
    {
        if (topFillPrefab == null)
            return;

        float yCenter = (fillBaseY + fillHeight * 0.5f) * cellSize;

        float roomW = roomWidth * cellSize;
        float roomD = roomDepth * cellSize;
        float fillH = fillHeight * cellSize;

        // 1) 横向顶部填充：gapX * roomDepth * fillHeight
        if (gapX > 0f)
        {
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns - 1; col++)
                {
                    Vector2Int a = new Vector2Int(col, row);
                    Vector2Int b = new Vector2Int(col + 1, row);

                    if (!rooms.ContainsKey(a) || !rooms.ContainsKey(b))
                        continue;

                    Vector3 posA = GetRoomLocalPosition(col, row);
                    Vector3 posB = GetRoomLocalPosition(col + 1, row);

                    float leftEdge = posA.x + roomW * 0.5f;
                    float rightEdge = posB.x - roomW * 0.5f;
                    float width = rightEdge - leftEdge;
                    if (width <= 0f)
                        continue;

                    Vector3 center = new Vector3(
                        (leftEdge + rightEdge) * 0.5f,
                        yCenter,
                        posA.z
                    );

                    CreateBlock(
                        topFillPrefab,
                        fillRoot,
                        $"{FillPrefix}InnerX_R{row}_C{col}_to_C{col + 1}",
                        center,
                        new Vector3(width, fillH, roomD)
                    );
                }
            }
        }

        // 2) 纵向顶部填充：roomWidth * gapZ * fillHeight
        if (gapZ > 0f)
        {
            for (int row = 0; row < rows - 1; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    Vector2Int a = new Vector2Int(col, row);
                    Vector2Int b = new Vector2Int(col, row + 1);

                    if (!rooms.ContainsKey(a) || !rooms.ContainsKey(b))
                        continue;

                    Vector3 posA = GetRoomLocalPosition(col, row);
                    Vector3 posB = GetRoomLocalPosition(col, row + 1);

                    float nearEdge = posA.z + roomD * 0.5f;
                    float farEdge = posB.z - roomD * 0.5f;
                    float depth = farEdge - nearEdge;
                    if (depth <= 0f)
                        continue;

                    Vector3 center = new Vector3(
                        posA.x,
                        yCenter,
                        (nearEdge + farEdge) * 0.5f
                    );

                    CreateBlock(
                        topFillPrefab,
                        fillRoot,
                        $"{FillPrefix}InnerZ_R{row}_C{col}_to_R{row + 1}",
                        center,
                        new Vector3(roomW, fillH, depth)
                    );
                }
            }
        }

        // 3) 交叉顶部填充：gapX * gapZ * fillHeight
        if (gapX > 0f && gapZ > 0f)
        {
            for (int row = 0; row < rows - 1; row++)
            {
                for (int col = 0; col < columns - 1; col++)
                {
                    Vector2Int a = new Vector2Int(col, row);
                    Vector2Int b = new Vector2Int(col + 1, row);
                    Vector2Int c = new Vector2Int(col, row + 1);
                    Vector2Int d = new Vector2Int(col + 1, row + 1);

                    if (!rooms.ContainsKey(a) || !rooms.ContainsKey(b) || !rooms.ContainsKey(c) || !rooms.ContainsKey(d))
                        continue;

                    Vector3 posA = GetRoomLocalPosition(col, row);
                    Vector3 posB = GetRoomLocalPosition(col + 1, row);
                    Vector3 posC = GetRoomLocalPosition(col, row + 1);

                    float leftEdge = posA.x + roomW * 0.5f;
                    float rightEdge = posB.x - roomW * 0.5f;
                    float nearEdge = posA.z + roomD * 0.5f;
                    float farEdge = posC.z - roomD * 0.5f;

                    float width = rightEdge - leftEdge;
                    float depth = farEdge - nearEdge;
                    if (width <= 0f || depth <= 0f)
                        continue;

                    Vector3 center = new Vector3(
                        (leftEdge + rightEdge) * 0.5f,
                        yCenter,
                        (nearEdge + farEdge) * 0.5f
                    );

                    CreateBlock(
                        topFillPrefab,
                        fillRoot,
                        $"{FillPrefix}Cross_R{row}_C{col}",
                        center,
                        new Vector3(width, fillH, depth)
                    );
                }
            }
        }
    }
    
    private void GenerateSideWalls(Transform sideRoot, Dictionary<Vector2Int, Transform> rooms)
    {
        if (sideWallPrefab == null)
            return;

        float wallHeight = Mathf.Max(0f, roomHeight - fillHeight) * cellSize;
        if (wallHeight <= 0f)
            return;

        float wallCenterY = wallHeight * 0.5f;
        float wallThickness = sideWallThickness * cellSize;

        float roomW = roomWidth * cellSize;
        float roomD = roomDepth * cellSize;

        // ============================================================
        // 统一逻辑：
        // 不再只处理“阵列最外圈”，而是处理所有“暴露在空缺外侧”的边。
        //
        // 顶部填充分两类：
        // 1) 横向条带（连接左右两个房间，尺寸：gapX × roomDepth × fillHeight）
        //    它的“前/后”两侧，如果外面没有相邻连续结构，则需要补侧墙。
        //
        // 2) 纵向条带（连接前后两个房间，尺寸：roomWidth × gapZ × fillHeight）
        //    它的“左/右”两侧，如果外面没有相邻连续结构，则需要补侧墙。
        //
        // 这样 blocked cell 形成的内部洞口，也会被视为“外侧”并封边。
        // ============================================================

        // ------------------------------------------------------------
        // A. 横向顶部填充条带（X 向连接）
        // 条带存在条件：当前 row 上 col 和 col+1 两个房间都存在
        // 对它检查前侧 / 后侧是否暴露
        // ------------------------------------------------------------
        if (gapX > 0f)
        {
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns - 1; col++)
                {
                    Vector2Int a = new Vector2Int(col, row);
                    Vector2Int b = new Vector2Int(col + 1, row);

                    if (!rooms.ContainsKey(a) || !rooms.ContainsKey(b))
                        continue;

                    Vector3 posA = GetRoomLocalPosition(col, row);
                    Vector3 posB = GetRoomLocalPosition(col + 1, row);

                    float leftEdge = posA.x + roomW * 0.5f;
                    float rightEdge = posB.x - roomW * 0.5f;
                    float width = rightEdge - leftEdge;
                    if (width <= 0f)
                        continue;

                    // 前侧是否暴露：
                    // 只有当 row-1 这一排的对应两个房间都存在时，
                    // 该横向条带前方才算被连续结构“接住”；
                    // 否则就视为外侧，需要封边。
                    bool hasFrontSupport =
                        row > 0 &&
                        rooms.ContainsKey(new Vector2Int(col, row - 1)) &&
                        rooms.ContainsKey(new Vector2Int(col + 1, row - 1));

                    if (!hasFrontSupport)
                    {
                        Vector3 center = new Vector3(
                            (leftEdge + rightEdge) * 0.5f,
                            wallCenterY,
                            posA.z - roomD * 0.5f + wallThickness * 0.5f
                        );

                        CreateBlock(
                            sideWallPrefab,
                            sideRoot,
                            $"{SidePrefix}Front_XGap_R{row}_C{col}",
                            center,
                            new Vector3(width, wallHeight, wallThickness)
                        );
                    }

                    // 后侧是否暴露
                    bool hasBackSupport =
                        row < rows - 1 &&
                        rooms.ContainsKey(new Vector2Int(col, row + 1)) &&
                        rooms.ContainsKey(new Vector2Int(col + 1, row + 1));

                    if (!hasBackSupport)
                    {
                        Vector3 center = new Vector3(
                            (leftEdge + rightEdge) * 0.5f,
                            wallCenterY,
                            posA.z + roomD * 0.5f - wallThickness * 0.5f
                        );

                        CreateBlock(
                            sideWallPrefab,
                            sideRoot,
                            $"{SidePrefix}Back_XGap_R{row}_C{col}",
                            center,
                            new Vector3(width, wallHeight, wallThickness)
                        );
                    }
                }
            }
        }

        // ------------------------------------------------------------
        // B. 纵向顶部填充条带（Z 向连接）
        // 条带存在条件：当前 col 上 row 和 row+1 两个房间都存在
        // 对它检查左侧 / 右侧是否暴露
        // ------------------------------------------------------------
        if (gapZ > 0f)
        {
            for (int row = 0; row < rows - 1; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    Vector2Int a = new Vector2Int(col, row);
                    Vector2Int b = new Vector2Int(col, row + 1);

                    if (!rooms.ContainsKey(a) || !rooms.ContainsKey(b))
                        continue;

                    Vector3 posA = GetRoomLocalPosition(col, row);
                    Vector3 posB = GetRoomLocalPosition(col, row + 1);

                    float nearEdge = posA.z + roomD * 0.5f;
                    float farEdge = posB.z - roomD * 0.5f;
                    float depth = farEdge - nearEdge;
                    if (depth <= 0f)
                        continue;

                    // 左侧是否暴露
                    bool hasLeftSupport =
                        col > 0 &&
                        rooms.ContainsKey(new Vector2Int(col - 1, row)) &&
                        rooms.ContainsKey(new Vector2Int(col - 1, row + 1));

                    if (!hasLeftSupport)
                    {
                        Vector3 center = new Vector3(
                            posA.x - roomW * 0.5f + wallThickness * 0.5f,
                            wallCenterY,
                            (nearEdge + farEdge) * 0.5f
                        );

                        CreateBlock(
                            sideWallPrefab,
                            sideRoot,
                            $"{SidePrefix}Left_ZGap_R{row}_C{col}",
                            center,
                            new Vector3(wallThickness, wallHeight, depth)
                        );
                    }

                    // 右侧是否暴露
                    bool hasRightSupport =
                        col < columns - 1 &&
                        rooms.ContainsKey(new Vector2Int(col + 1, row)) &&
                        rooms.ContainsKey(new Vector2Int(col + 1, row + 1));

                    if (!hasRightSupport)
                    {
                        Vector3 center = new Vector3(
                            posA.x + roomW * 0.5f - wallThickness * 0.5f,
                            wallCenterY,
                            (nearEdge + farEdge) * 0.5f
                        );

                        CreateBlock(
                            sideWallPrefab,
                            sideRoot,
                            $"{SidePrefix}Right_ZGap_R{row}_C{col}",
                            center,
                            new Vector3(wallThickness, wallHeight, depth)
                        );
                    }
                }
            }
        }
    }

    private void CreateBlock(GameObject prefab, Transform parent, string objectName, Vector3 localCenter, Vector3 size)
    {
        if (prefab == null)
            return;

        if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
            return;

        Transform block = InstantiatePrefab(prefab, parent);
        if (block == null)
            return;

        block.name = objectName;
        block.localPosition = localCenter;
        block.localRotation = Quaternion.identity;
        block.localScale = size;
    }

    private Vector3 GetRoomLocalPosition(int col, int row)
    {
        float stepX = (roomWidth + gapX) * cellSize;
        float stepZ = (roomDepth + gapZ) * cellSize;

        Vector3 originOffset = Vector3.zero;

        if (autoCenter)
        {
            float totalWidth = (columns - 1) * stepX;
            float totalDepth = (rows - 1) * stepZ;
            originOffset = new Vector3(-totalWidth * 0.5f, 0f, -totalDepth * 0.5f);
        }

        return originOffset + new Vector3(col * stepX, 0f, row * stepZ);
    }

    private HashSet<Vector2Int> BuildBlockedSet()
    {
        HashSet<Vector2Int> set = new HashSet<Vector2Int>();

        for (int i = 0; i < blockedCells.Count; i++)
        {
            Vector2Int cell = blockedCells[i];
            if (cell.x < 0 || cell.x >= columns || cell.y < 0 || cell.y >= rows)
                continue;

            set.Add(cell);
        }

        return set;
    }

    private static bool TryParseRoomName(string roomName, out int row, out int col)
    {
        row = -1;
        col = -1;

        if (string.IsNullOrEmpty(roomName))
            return false;

        string[] parts = roomName.Split('_');
        if (parts.Length < 3)
            return false;

        string rowPart = parts[1];
        string colPart = parts[2];

        if (!rowPart.StartsWith("R") || !colPart.StartsWith("C"))
            return false;

        if (!int.TryParse(rowPart.Substring(1), out row))
            return false;

        if (!int.TryParse(colPart.Substring(1), out col))
            return false;

        return true;
    }

    private Transform GetOrCreateChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
            return child;

        GameObject go = new GameObject(childName);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private Transform InstantiatePrefab(GameObject prefab, Transform parent)
    {
        if (prefab == null)
            return null;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            return instance != null ? instance.transform : null;
        }
#endif

        GameObject runtimeInstance = Instantiate(prefab, parent);
        return runtimeInstance != null ? runtimeInstance.transform : null;
    }

    private void ClearChildren(Transform root)
    {
        if (root == null)
            return;

        List<GameObject> toDelete = new List<GameObject>();
        for (int i = 0; i < root.childCount; i++)
            toDelete.Add(root.GetChild(i).gameObject);

        for (int i = toDelete.Count - 1; i >= 0; i--)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(toDelete[i]);
            else
                Destroy(toDelete[i]);
#else
            Destroy(toDelete[i]);
#endif
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(RoomArrayGenerator))]
public class RoomArrayGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(10);

        RoomArrayGenerator generator = (RoomArrayGenerator)target;

        if (GUILayout.Button("生成全部（房间 + 顶部填充 + 侧面封边）"))
        {
            generator.GenerateAll();
            EditorUtility.SetDirty(generator);
        }

        if (GUILayout.Button("只生成房间"))
        {
            generator.GenerateRoomsOnly();
            EditorUtility.SetDirty(generator);
        }

        if (GUILayout.Button("只生成顶部填充"))
        {
            generator.GenerateTopFillOnly();
            EditorUtility.SetDirty(generator);
        }

        if (GUILayout.Button("只生成侧面封边"))
        {
            generator.GenerateSideWallsOnly();
            EditorUtility.SetDirty(generator);
        }

        if (GUILayout.Button("只清空顶部填充"))
        {
            generator.ClearTopFillOnly();
            EditorUtility.SetDirty(generator);
        }

        if (GUILayout.Button("只清空侧面封边"))
        {
            generator.ClearSideWallsOnly();
            EditorUtility.SetDirty(generator);
        }

        if (GUILayout.Button("清空全部"))
        {
            generator.ClearAllGenerated();
            EditorUtility.SetDirty(generator);
        }
    }
}
#endif