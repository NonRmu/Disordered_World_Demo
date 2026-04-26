using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[AddComponentMenu("Game/Trigger/Load Next Level On Trigger")]
[RequireComponent(typeof(Collider))]
public class LoadNextLevelOnTrigger : MonoBehaviour
{
    public enum LastLevelBehavior
    {
        DoNothing = 0,
        ReloadCurrentScene = 1,
        LoadSpecificScene = 2
    }

    [Header("玩家判定")]
    [Tooltip("只有该 Layer 的对象进入时才会触发下一关。")]
    public LayerMask playerLayer;

    [Tooltip("是否忽略 Trigger Collider。")]
    public bool ignoreTriggerColliders = true;

    [Header("加载设置")]
    [Tooltip("触发后是否加载下一关。")]
    public bool loadNextSceneOnEnter = true;

    [Tooltip("加载下一关前的延迟时间（秒）。")]
    [Min(0f)] public float loadDelay = 0f;

    [Tooltip("开始加载后，是否阻止后续重复触发。")]
    public bool blockFurtherTriggerAfterLoadBegan = true;

    [Header("最后一关行为")]
    [Tooltip("当当前场景已经是 Build Settings 中最后一关时的处理方式。")]
    public LastLevelBehavior lastLevelBehavior = LastLevelBehavior.DoNothing;

    [Tooltip("当最后一关行为为 LoadSpecificScene 时，要加载的场景名。")]
    public string specificSceneName = "";

    [Header("调试只读")]
    [SerializeField] private bool loadStarted = false;

    private Collider cachedTrigger;

    private void Reset()
    {
        cachedTrigger = GetComponent<Collider>();
        if (cachedTrigger != null)
            cachedTrigger.isTrigger = true;
    }

    private void Awake()
    {
        cachedTrigger = GetComponent<Collider>();
        if (cachedTrigger != null)
            cachedTrigger.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null)
            return;

        if (ignoreTriggerColliders && other.isTrigger)
            return;

        if (blockFurtherTriggerAfterLoadBegan && loadStarted)
            return;

        GameObject targetObject = ResolveTargetObject(other);
        if (targetObject == null)
            return;

        if (!IsInLayerMask(targetObject.layer, playerLayer))
            return;

        if (!loadNextSceneOnEnter)
            return;

        loadStarted = true;

        if (loadDelay <= 0f)
            LoadNextScene();
        else
            Invoke(nameof(LoadNextScene), loadDelay);
    }

    private GameObject ResolveTargetObject(Collider other)
    {
        if (other == null)
            return null;

        if (other.attachedRigidbody != null)
            return other.attachedRigidbody.gameObject;

        return other.transform.root != null ? other.transform.root.gameObject : other.gameObject;
    }

    private void LoadNextScene()
    {
        Scene currentScene = SceneManager.GetActiveScene();
        if (!currentScene.IsValid())
            return;

        int currentIndex = currentScene.buildIndex;
        int nextIndex = currentIndex + 1;
        int totalSceneCount = SceneManager.sceneCountInBuildSettings;

        if (nextIndex < totalSceneCount)
        {
            SceneManager.LoadScene(nextIndex);
            return;
        }

        HandleLastLevel(currentIndex);
    }

    private void HandleLastLevel(int currentIndex)
    {
        switch (lastLevelBehavior)
        {
            case LastLevelBehavior.ReloadCurrentScene:
                SceneManager.LoadScene(currentIndex);
                break;

            case LastLevelBehavior.LoadSpecificScene:
                if (!string.IsNullOrEmpty(specificSceneName))
                    SceneManager.LoadScene(specificSceneName);
                break;

            case LastLevelBehavior.DoNothing:
            default:
                break;
        }
    }

    private bool IsInLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }
}