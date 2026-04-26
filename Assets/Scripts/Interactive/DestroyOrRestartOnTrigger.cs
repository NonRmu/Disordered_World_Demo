using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[AddComponentMenu("Game/Trigger/Destroy Or Restart On Trigger")]
[RequireComponent(typeof(Collider))]
public class DestroyOrRestartOnTrigger : MonoBehaviour
{
    [Header("玩家判定")]
    [Tooltip("玩家所在层。若进入触发器的对象属于该层，则重开当前关卡。")]
    public LayerMask playerLayer;

    [Header("销毁设置")]
    [Tooltip("是否销毁进入触发器的非玩家物体。")]
    public bool destroyNonPlayerObjects = true;

    [Tooltip("销毁时是否优先销毁刚体根对象；关闭则销毁命中的对象自身。")]
    public bool destroyRootObject = true;

    [Tooltip("是否忽略 Trigger Collider 进入事件。")]
    public bool ignoreTriggerColliders = false;

    [Header("重开关卡")]
    [Tooltip("玩家进入后是否立即重开当前场景。")]
    public bool restartSceneWhenPlayerEnters = true;

    [Tooltip("重开当前关卡前的延迟时间（秒）。")]
    [Min(0f)] public float restartDelay = 0f;

    [Tooltip("为防止重复触发，开始重开后不再继续响应。")]
    public bool blockFurtherTriggerAfterRestartBegan = true;

    private bool restartStarted = false;
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

        if (blockFurtherTriggerAfterRestartBegan && restartStarted)
            return;

        GameObject targetObject = ResolveTargetObject(other);
        if (targetObject == null)
            return;

        if (IsInLayerMask(targetObject.layer, playerLayer))
        {
            HandlePlayerEntered(targetObject);
            return;
        }

        if (destroyNonPlayerObjects)
            Destroy(targetObject);
    }

    private GameObject ResolveTargetObject(Collider other)
    {
        if (other == null)
            return null;

        if (destroyRootObject)
        {
            if (other.attachedRigidbody != null)
                return other.attachedRigidbody.gameObject;

            return other.transform.root != null ? other.transform.root.gameObject : other.gameObject;
        }

        return other.gameObject;
    }

    private void HandlePlayerEntered(GameObject playerObject)
    {
        if (!restartSceneWhenPlayerEnters)
            return;

        restartStarted = true;

        if (restartDelay <= 0f)
        {
            ReloadCurrentScene();
        }
        else
        {
            Invoke(nameof(ReloadCurrentScene), restartDelay);
        }
    }

    private void ReloadCurrentScene()
    {
        Scene currentScene = SceneManager.GetActiveScene();
        if (!currentScene.IsValid())
            return;

        SceneManager.LoadScene(currentScene.buildIndex);
    }

    private bool IsInLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }
}