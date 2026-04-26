using System.Collections.Generic;
using UnityEngine;

public sealed class ProjectionAnalyzer
{
    private readonly Queue<int> queue = new Queue<int>();
    private readonly HashSet<int> visited = new HashSet<int>();
    private readonly HashSet<int> connectedIds = new HashSet<int>();
    private readonly List<int> connectedIdList = new List<int>();

    public IReadOnlyList<int> ConnectedIdList => connectedIdList;

    public bool BuildConnectedProjectionGroupFromObjectId(
        ProjectionFrameData frame,
        int seedObjectId,
        Vector2Int screenCenterPixel)
    {
        connectedIdList.Clear();
        connectedIds.Clear();
        queue.Clear();
        visited.Clear();

        if (frame == null ||
            frame.projectionMaskPixels == null ||
            frame.projectionIdPixels == null ||
            seedObjectId == 0)
        {
            return false;
        }

        RectInt roi = GetValidRoi(frame);

        int seedIndex = FindNearestPixelIndexWithObjectId(frame, seedObjectId, screenCenterPixel, roi);
        if (seedIndex < 0)
            return false;

        queue.Enqueue(seedIndex);
        visited.Add(seedIndex);

        int width = frame.width;

        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            int x = index % width;
            int y = index / width;

            int objectId = frame.projectionIdPixels[index];
            if (objectId != 0 && connectedIds.Add(objectId))
                connectedIdList.Add(objectId);

            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    if (ox == 0 && oy == 0)
                        continue;

                    int nx = x + ox;
                    int ny = y + oy;

                    if (nx < roi.xMin || nx >= roi.xMax || ny < roi.yMin || ny >= roi.yMax)
                        continue;

                    int ni = ny * width + nx;
                    if (visited.Contains(ni))
                        continue;

                    if (frame.projectionMaskPixels[ni] == 0)
                        continue;

                    visited.Add(ni);
                    queue.Enqueue(ni);
                }
            }
        }

        return connectedIdList.Count > 0;
    }

    public List<int> BuildConnectedProjectionGroupFromSeedIds(
        ProjectionFrameData frame,
        HashSet<int> seedProjectionIds)
    {
        List<int> result = new List<int>();

        if (frame == null ||
            frame.projectionMaskPixels == null ||
            frame.projectionIdPixels == null ||
            seedProjectionIds == null ||
            seedProjectionIds.Count == 0)
        {
            return result;
        }

        RectInt roi = GetValidRoi(frame);

        Queue<int> localQueue = new Queue<int>();
        HashSet<int> visitedPixels = new HashSet<int>();
        HashSet<int> localConnectedIds = new HashSet<int>();

        for (int y = roi.yMin; y < roi.yMax; y++)
        {
            int row = y * frame.width;
            for (int x = roi.xMin; x < roi.xMax; x++)
            {
                int i = row + x;

                if (frame.projectionMaskPixels[i] == 0)
                    continue;

                int projectionId = frame.projectionIdPixels[i];
                if (projectionId == 0)
                    continue;

                if (!seedProjectionIds.Contains(projectionId))
                    continue;

                if (visitedPixels.Add(i))
                    localQueue.Enqueue(i);
            }
        }

        while (localQueue.Count > 0)
        {
            int index = localQueue.Dequeue();
            int x = index % frame.width;
            int y = index / frame.width;

            int objectId = frame.projectionIdPixels[index];
            if (objectId != 0)
                localConnectedIds.Add(objectId);

            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    if (ox == 0 && oy == 0)
                        continue;

                    int nx = x + ox;
                    int ny = y + oy;

                    if (nx < roi.xMin || nx >= roi.xMax || ny < roi.yMin || ny >= roi.yMax)
                        continue;

                    int ni = ny * frame.width + nx;
                    if (visitedPixels.Contains(ni))
                        continue;

                    if (frame.projectionMaskPixels[ni] == 0)
                        continue;

                    visitedPixels.Add(ni);
                    localQueue.Enqueue(ni);
                }
            }
        }

        result.AddRange(localConnectedIds);
        return result;
    }

    public List<int> FindFullyCoveredSolidIds(
        ProjectionFrameData frame,
        HashSet<int> selectedProjectionIds,
        float coverageThreshold = 0.995f)
    {
        List<int> result = new List<int>();

        if (frame == null ||
            frame.projectionMaskPixels == null ||
            frame.projectionIdPixels == null ||
            frame.solidIdPixels == null ||
            selectedProjectionIds == null ||
            selectedProjectionIds.Count == 0)
        {
            return result;
        }

        RectInt roi = GetValidRoi(frame);

        Dictionary<int, int> totalPixelsBySolidId = new Dictionary<int, int>();
        Dictionary<int, int> coveredPixelsBySolidId = new Dictionary<int, int>();

        for (int y = roi.yMin; y < roi.yMax; y++)
        {
            int row = y * frame.width;
            for (int x = roi.xMin; x < roi.xMax; x++)
            {
                int i = row + x;

                int solidId = frame.solidIdPixels[i];
                if (solidId == 0)
                    continue;

                if (!totalPixelsBySolidId.ContainsKey(solidId))
                    totalPixelsBySolidId.Add(solidId, 0);

                totalPixelsBySolidId[solidId]++;

                if (frame.projectionMaskPixels[i] == 0)
                    continue;

                int projectionId = frame.projectionIdPixels[i];
                if (projectionId == 0)
                    continue;

                if (!selectedProjectionIds.Contains(projectionId))
                    continue;

                if (!coveredPixelsBySolidId.ContainsKey(solidId))
                    coveredPixelsBySolidId.Add(solidId, 0);

                coveredPixelsBySolidId[solidId]++;
            }
        }

        foreach (KeyValuePair<int, int> kv in totalPixelsBySolidId)
        {
            int solidId = kv.Key;
            int total = kv.Value;
            if (total <= 0)
                continue;

            coveredPixelsBySolidId.TryGetValue(solidId, out int covered);
            float ratio = (float)covered / total;

            if (ratio >= coverageThreshold)
                result.Add(solidId);
        }

        return result;
    }

    private RectInt GetValidRoi(ProjectionFrameData frame)
    {
        RectInt roi = frame.analysisRoi;
        if (roi.width <= 0 || roi.height <= 0)
            roi = new RectInt(0, 0, frame.width, frame.height);

        int xMin = Mathf.Clamp(roi.xMin, 0, frame.width);
        int yMin = Mathf.Clamp(roi.yMin, 0, frame.height);
        int xMax = Mathf.Clamp(roi.xMax, 0, frame.width);
        int yMax = Mathf.Clamp(roi.yMax, 0, frame.height);

        return new RectInt(
            xMin,
            yMin,
            Mathf.Max(0, xMax - xMin),
            Mathf.Max(0, yMax - yMin)
        );
    }

    private int FindNearestPixelIndexWithObjectId(
        ProjectionFrameData frame,
        int objectId,
        Vector2Int center,
        RectInt roi)
    {
        int bestIndex = -1;
        int bestDistSq = int.MaxValue;

        int centerX = Mathf.Clamp(center.x, roi.xMin, roi.xMax - 1);
        int centerY = Mathf.Clamp(center.y, roi.yMin, roi.yMax - 1);

        for (int y = roi.yMin; y < roi.yMax; y++)
        {
            int row = y * frame.width;
            for (int x = roi.xMin; x < roi.xMax; x++)
            {
                int index = row + x;
                if (frame.projectionIdPixels[index] != objectId)
                    continue;

                int dx = x - centerX;
                int dy = y - centerY;
                int distSq = dx * dx + dy * dy;

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestIndex = index;
                }
            }
        }

        return bestIndex;
    }
}