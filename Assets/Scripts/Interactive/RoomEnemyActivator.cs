using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Game/Enemy/Room Enemy Activator")]
[RequireComponent(typeof(Collider))]
public class RoomEnemyActivator : MonoBehaviour
{
    [Header("玩家识别")]
    [Tooltip("优先按 Layer 判断玩家。")]
    public LayerMask playerLayerMask;

    [Tooltip("可选：若填写 Tag，也允许通过 Tag 识别。")]
    public string playerTag = "Player";

    [Header("激活目标")]
    [Tooltip("进入该房间后要激活的敌人。为空时自动找子物体中的敌人。")]
    public List<SolidStateRoomEnemyAI> enemies = new List<SolidStateRoomEnemyAI>();

    [Tooltip("Awake 时若列表为空，自动收集子物体中的敌人。")]
    public bool autoCollectEnemiesOnAwake = true;

    [Tooltip("运行时自动移除已被销毁的敌人引用。")]
    public bool autoRemoveMissingEnemies = true;

    [Header("调试只读")]
    [SerializeField] private Transform currentPlayer;
    [SerializeField] private int playerInsideCount = 0;

    private Collider triggerCol;

    private void Reset()
    {
        triggerCol = GetComponent<Collider>();
        if (triggerCol != null)
            triggerCol.isTrigger = true;
    }

    private void Awake()
    {
        triggerCol = GetComponent<Collider>();
        if (triggerCol != null)
            triggerCol.isTrigger = true;

        if (autoCollectEnemiesOnAwake)
            AutoCollectEnemiesIfNeeded();

        RemoveMissingEnemies();
    }

    private void OnEnable()
    {
        RemoveMissingEnemies();
    }

    private void Update()
    {
        if (autoRemoveMissingEnemies)
            RemoveMissingEnemies();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (autoRemoveMissingEnemies)
            RemoveMissingEnemies();

        if (!IsPlayer(other, out Transform playerRoot))
            return;

        playerInsideCount++;
        currentPlayer = playerRoot;
        SetEnemiesActive(true, currentPlayer);
    }

    private void OnTriggerExit(Collider other)
    {
        if (autoRemoveMissingEnemies)
            RemoveMissingEnemies();

        if (!IsPlayer(other, out Transform playerRoot))
            return;

        playerInsideCount = Mathf.Max(0, playerInsideCount - 1);

        if (currentPlayer == playerRoot && playerInsideCount == 0)
            currentPlayer = null;

        if (playerInsideCount == 0)
            SetEnemiesActive(false, null);
    }

    public void CollectEnemiesFromChildren(bool includeInactive = true)
    {
        SolidStateRoomEnemyAI[] found = GetComponentsInChildren<SolidStateRoomEnemyAI>(includeInactive);
        enemies.Clear();

        for (int i = 0; i < found.Length; i++)
        {
            SolidStateRoomEnemyAI enemy = found[i];
            if (enemy == null)
                continue;

            if (!enemies.Contains(enemy))
                enemies.Add(enemy);
        }
    }

    public void RemoveMissingEnemies()
    {
        if (enemies == null)
            return;

        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            if (enemies[i] == null)
                enemies.RemoveAt(i);
        }
    }

    private void AutoCollectEnemiesIfNeeded()
    {
        if (enemies != null && enemies.Count > 0)
            return;

        CollectEnemiesFromChildren(true);
    }

    private bool IsPlayer(Collider other, out Transform playerRoot)
    {
        playerRoot = null;
        if (other == null)
            return false;

        Transform root = other.attachedRigidbody != null
            ? other.attachedRigidbody.transform
            : other.transform.root;

        GameObject go = root.gameObject;

        bool layerMatched = ((1 << go.layer) & playerLayerMask.value) != 0;
        bool tagMatched = !string.IsNullOrEmpty(playerTag) && go.CompareTag(playerTag);
        bool movementMatched = go.GetComponent<CharacterMovement>() != null;

        if (!layerMatched && !tagMatched && !movementMatched)
            return false;

        playerRoot = root;
        return true;
    }

    private void SetEnemiesActive(bool active, Transform player)
    {
        if (enemies == null)
            return;

        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            SolidStateRoomEnemyAI enemy = enemies[i];
            if (enemy == null)
            {
                enemies.RemoveAt(i);
                continue;
            }

            enemy.SetRoomActive(active, player);
        }
    }

#if UNITY_EDITOR
    [ContextMenu("收集子物体中的敌人")]
    private void EditorCollectEnemiesFromChildren()
    {
        CollectEnemiesFromChildren(true);
    }

    [ContextMenu("清理 Missing 敌人引用")]
    private void EditorRemoveMissingEnemies()
    {
        RemoveMissingEnemies();
    }
#endif
}