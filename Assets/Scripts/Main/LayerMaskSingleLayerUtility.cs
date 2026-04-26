using UnityEngine;

public static class LayerMaskSingleLayerUtility
{
    public static bool IsSingleLayerMask(LayerMask mask)
    {
        int value = mask.value;
        return value != 0 && (value & (value - 1)) == 0;
    }

    public static int ToLayerIndex(LayerMask mask)
    {
        int value = mask.value;
        if (!IsSingleLayerMask(mask))
            return -1;

        int index = 0;
        while (value > 1)
        {
            value >>= 1;
            index++;
        }

        return index;
    }
}