using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[AddComponentMenu("Game/ASCII World Object")]
public class ASCIIWorldObject : MonoBehaviour
{
    public enum RuntimeState
    {
        Solid = 0,
        Virtual = 1,
        Projection = 2
    }

    private static readonly int ProjectionObjectIdProp = Shader.PropertyToID("_ProjectionObjectId");

    [Header("对象 ID（运行时由 Registry 自动分配）")]
    [SerializeField, ASCIIReadOnly, Min(1)] private int objectId = 1;

    // 运行时状态由 ASCIIWorldRegistryManager 维护，不在 Inspector 显示
    [System.NonSerialized] private RuntimeState currentState = RuntimeState.Solid;

    private Renderer[] cachedRenderers;
    private MaterialPropertyBlock mpb;

    public RuntimeState CurrentState
    {
        get => currentState;
        set => currentState = value;
    }

    public int ObjectId => Mathf.Max(1, objectId);

    public Renderer[] CachedRenderers
    {
        get
        {
            if (cachedRenderers == null || cachedRenderers.Length == 0)
                cachedRenderers = GetComponentsInChildren<Renderer>(true);
            return cachedRenderers;
        }
    }

    private void Awake()
    {
        EnsurePropertyBlock();
        RefreshRenderers();
        ApplyObjectIdToRenderers();
    }

    private void OnEnable()
    {
        EnsurePropertyBlock();
        ApplyObjectIdToRenderers();
    }

    private void OnValidate()
    {
        if (objectId < 1)
            objectId = 1;

        RefreshRenderers();
        EnsurePropertyBlock();

        if (Application.isPlaying)
            ApplyObjectIdToRenderers();
    }

    public void RefreshRenderers()
    {
        cachedRenderers = GetComponentsInChildren<Renderer>(true);
    }

    public void SetObjectIdFromManager(int newId)
    {
        objectId = Mathf.Max(1, newId);
        ApplyObjectIdToRenderers();
    }

    public void ApplyObjectIdToRenderers()
    {
        EnsurePropertyBlock();

        Renderer[] renderers = CachedRenderers;
        if (renderers == null || renderers.Length == 0)
            return;

        int id = ObjectId;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer rend = renderers[i];
            if (rend == null)
                continue;

            if (rend.sharedMaterials == null || rend.sharedMaterials.Length == 0)
                continue;

            mpb.Clear();
            rend.GetPropertyBlock(mpb);
            mpb.SetInt(ProjectionObjectIdProp, id);
            rend.SetPropertyBlock(mpb);
        }
    }

    private void EnsurePropertyBlock()
    {
        if (mpb == null)
            mpb = new MaterialPropertyBlock();
    }
}

public class ASCIIReadOnlyAttribute : PropertyAttribute
{
}

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(ASCIIReadOnlyAttribute))]
public class ASCIIReadOnlyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        bool oldEnabled = GUI.enabled;
        GUI.enabled = false;
        EditorGUI.PropertyField(position, property, label, true);
        GUI.enabled = oldEnabled;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUI.GetPropertyHeight(property, label, true);
    }
}
#endif