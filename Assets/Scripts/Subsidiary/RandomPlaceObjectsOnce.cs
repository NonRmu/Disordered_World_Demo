using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class RandomPlaceObjectsOnce : MonoBehaviour
{
    [System.Serializable]
    public class PlacementItem
    {
        [Header("目标物体")]
        public Transform target;

        [Header("该物体占地区域(XZ)")]
        [Min(0.01f)] public float sizeX = 1f;
        [Min(0.01f)] public float sizeZ = 1f;

        [Header("是否保留原始Y坐标")]
        public bool keepOriginalY = true;

        [HideInInspector] public float cachedY;
    }

    [Header("总随机范围中心")]
    [Tooltip("通常直接挂在一个空物体上，这个脚本所在物体的位置就是总范围中心。")]
    public Transform areaCenter;

    [Header("总随机范围大小(XZ)")]
    [Min(0.01f)] public float areaSizeX = 20f;
    [Min(0.01f)] public float areaSizeZ = 20f;

    [Header("需要摆放的物体")]
    public PlacementItem[] items = new PlacementItem[3];

    [Header("额外间距")]
    [Tooltip("物体之间额外留出的安全距离。")]
    [Min(0f)] public float extraSpacing = 0f;

    [Header("随机尝试次数")]
    [Tooltip("每个物体最多尝试多少次找位置。")]
    [Min(1)] public int maxTriesPerItem = 200;

    [Header("执行设置")]
    [Tooltip("Start时自动执行一次。")]
    public bool executeOnStart = true;

    [Tooltip("若开启，使用固定随机种子，每次运行结果一致。")]
    public bool useFixedSeed = false;
    public int fixedSeed = 12345;

    private bool hasExecuted = false;

    private struct RectXZ
    {
        public Vector2 center;
        public Vector2 size;

        public float MinX => center.x - size.x * 0.5f;
        public float MaxX => center.x + size.x * 0.5f;
        public float MinZ => center.y - size.y * 0.5f;
        public float MaxZ => center.y + size.y * 0.5f;

        public bool Overlaps(RectXZ other, float spacing)
        {
            return !(MaxX + spacing <= other.MinX ||
                     MinX - spacing >= other.MaxX ||
                     MaxZ + spacing <= other.MinZ ||
                     MinZ - spacing >= other.MaxZ);
        }
    }

    private void Reset()
    {
        areaCenter = transform;
    }

    private void Start()
    {
        if (executeOnStart)
        {
            RandomPlaceOnce();
        }
    }

    [ContextMenu("随机摆放一次")]
    public void RandomPlaceOnce()
    {
        if (hasExecuted)
            return;

        if (areaCenter == null)
            areaCenter = transform;

        if (useFixedSeed)
            Random.InitState(fixedSeed);

        CacheOriginalY();
        TryPlaceAll();

        hasExecuted = true;
    }

    [ContextMenu("强制重新随机摆放")]
    public void ForceRandomPlace()
    {
        if (areaCenter == null)
            areaCenter = transform;

        if (useFixedSeed)
            Random.InitState(fixedSeed);

        CacheOriginalY();
        TryPlaceAll();

        hasExecuted = true;
    }

    private void CacheOriginalY()
    {
        if (items == null) return;

        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] != null && items[i].target != null)
            {
                items[i].cachedY = items[i].target.position.y;
            }
        }
    }

    private void TryPlaceAll()
{
    List<RectXZ> placedRects = new List<RectXZ>();

    for (int i = 0; i < items.Length; i++)
    {
        PlacementItem item = items[i];
        if (item == null || item.target == null)
            continue;

        bool placed = TryFindPositionForItem(item, placedRects, out Vector3 finalPos, out RectXZ finalRect);

        if (placed)
        {
            Rigidbody rb = item.target.GetComponent<Rigidbody>();

            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                rb.position = finalPos;
                item.target.position = finalPos;
            }
            else
            {
                item.target.position = finalPos;
            }

            placedRects.Add(finalRect);

            //Debug.Log($"[RandomPlaceObjectsOnce] 已放置: {item.target.name} -> {finalPos}", item.target);
        }
    }

    Physics.SyncTransforms();
}

    private bool TryFindPositionForItem(PlacementItem item, List<RectXZ> placedRects, out Vector3 finalPos, out RectXZ finalRect)
    {
        finalPos = Vector3.zero;
        finalRect = default;

        float halfAreaX = areaSizeX * 0.5f;
        float halfAreaZ = areaSizeZ * 0.5f;

        float halfItemX = item.sizeX * 0.5f;
        float halfItemZ = item.sizeZ * 0.5f;

        // 如果物体占地比总区域还大，直接失败
        if (item.sizeX > areaSizeX || item.sizeZ > areaSizeZ)
        {
            Debug.LogWarning($"[RandomPlaceObjectsOnce] 物体 {item.target.name} 的占地尺寸超过总范围，无法摆放。", this);
            return false;
        }

        float minX = areaCenter.position.x - halfAreaX + halfItemX;
        float maxX = areaCenter.position.x + halfAreaX - halfItemX;
        float minZ = areaCenter.position.z - halfAreaZ + halfItemZ;
        float maxZ = areaCenter.position.z + halfAreaZ - halfItemZ;

        for (int attempt = 0; attempt < maxTriesPerItem; attempt++)
        {
            float x = Random.Range(minX, maxX);
            float z = Random.Range(minZ, maxZ);

            RectXZ candidate = new RectXZ
            {
                center = new Vector2(x, z),
                size = new Vector2(item.sizeX, item.sizeZ)
            };

            bool overlaps = false;
            for (int j = 0; j < placedRects.Count; j++)
            {
                if (candidate.Overlaps(placedRects[j], extraSpacing))
                {
                    overlaps = true;
                    break;
                }
            }

            if (overlaps)
                continue;

            float y = item.keepOriginalY ? item.cachedY : item.target.position.y;

            finalPos = new Vector3(x, y, z);
            finalRect = candidate;
            return true;
        }

        return false;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Transform center = areaCenter != null ? areaCenter : transform;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(
            center.position,
            new Vector3(areaSizeX, 0.05f, areaSizeZ)
        );

        if (items == null) return;

        for (int i = 0; i < items.Length; i++)
        {
            PlacementItem item = items[i];
            if (item == null || item.target == null)
                continue;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(
                new Vector3(item.target.position.x, center.position.y, item.target.position.z),
                new Vector3(item.sizeX, 0.05f, item.sizeZ)
            );
        }
    }
#endif
}