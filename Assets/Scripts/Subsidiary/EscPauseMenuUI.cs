using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
[AddComponentMenu("Game/UI/Esc Pause Menu UI")]
public class EscPauseMenuUI : MonoBehaviour
{
    [Header("根节点")]
    [Tooltip("整个暂停菜单根节点。")]
    public GameObject menuRoot;

    [Tooltip("主菜单面板（重新开始 / 选择关卡 / 退出游戏）。")]
    public GameObject mainPanel;

    [Tooltip("选关面板（6个关卡按钮）。")]
    public GameObject levelSelectPanel;

    [Header("输入")]
    public KeyCode toggleKey = KeyCode.Escape;

    [Header("暂停")]
    [Tooltip("打开菜单时是否暂停游戏。")]
    public bool pauseGameWhenOpen = true;

    [Tooltip("打开菜单时是否显示鼠标。")]
    public bool showCursorWhenOpen = true;

    [Tooltip("关闭菜单时是否锁定鼠标。")]
    public bool lockCursorWhenClosed = true;

    [Header("关卡设置")]
    [Tooltip("6个关卡对应的场景名。顺序对应按钮1~6。")]
    public string[] levelSceneNames = new string[6];

    [Header("关卡按钮")]
    [Tooltip("第1~6关对应的按钮，顺序要和 levelSceneNames 一致。")]
    public Button[] levelButtons = new Button[6];

    [Header("当前关卡按钮样式")]
    [Tooltip("当前所在关卡按钮显示为该颜色。")]
    public Color currentLevelButtonColor = new Color(0.45f, 0.45f, 0.45f, 1f);

    [Header("相机旋转速度设置")]
    [Tooltip("要被菜单修改旋转速度的 CameraMovement。一般拖 CameraRigRoot 上的 CameraMovement。")]
    public CameraMovement targetCameraMovement;

    [Tooltip("targetCameraMovement 为空时，是否在场景中自动查找 CameraMovement。")]
    public bool autoFindCameraMovement = true;

    [Tooltip("控制整体相机旋转速度的 Slider。")]
    public Slider rotationSpeedSlider;

    [Tooltip("显示当前旋转速度数值的 TMP_Text，可不填。")]
    public TMP_Text rotationSpeedValueText;

    [Tooltip("Slider 最小旋转速度。")]
    [Min(0.01f)] public float rotationSpeedMin = 60f;

    [Tooltip("Slider 最大旋转速度。")]
    [Min(0.01f)] public float rotationSpeedMax = 600f;

    [Tooltip("是否保持原本 pitchSpeed / yawSpeed 的比例。建议开启。")]
    public bool keepPitchYawRatio = true;

    [Tooltip("是否把旋转速度保存到 PlayerPrefs。开启后切换场景/重启后仍会保留。")]
    public bool saveRotationSpeedWithPlayerPrefs = true;

    [Tooltip("PlayerPrefs 保存相机旋转速度用 Key。")]
    public string rotationSpeedPrefsKey = "CameraRotationSpeed";

    [Header("玩家移动速度设置")]
    [Tooltip("要被菜单修改移动速度的 CharacterMovement。一般拖 Player 上的 CharacterMovement。")]
    public CharacterMovement targetCharacterMovement;

    [Tooltip("targetCharacterMovement 为空时，是否在场景中自动查找 CharacterMovement。")]
    public bool autoFindCharacterMovement = true;

    [Tooltip("控制玩家普通移动速度 playerSpeed 的 Slider。")]
    public Slider playerSpeedSlider;

    [Tooltip("显示当前玩家移动速度数值的 TMP_Text，可不填。")]
    public TMP_Text playerSpeedValueText;

    [Tooltip("玩家移动速度最小值。")]
    [Min(0.01f)] public float playerSpeedMin = 1f;

    [Tooltip("玩家移动速度最大值。")]
    [Min(0.01f)] public float playerSpeedMax = 12f;

    [Tooltip("是否把玩家移动速度保存到 PlayerPrefs。开启后切换场景/重启后仍会保留。")]
    public bool savePlayerSpeedWithPlayerPrefs = true;

    [Tooltip("PlayerPrefs 保存玩家移动速度用 Key。")]
    public string playerSpeedPrefsKey = "PlayerMoveSpeed";

    [Header("调试只读")]
    [SerializeField] private bool isOpen = false;

    public bool IsOpen => isOpen;

    private float defaultYawSpeed = 300f;
    private float defaultPitchSpeed = 220f;
    private float pitchYawRatio = 220f / 300f;
    private bool hasCachedDefaultCameraSpeed = false;

    private float defaultPlayerSpeed = 5f;
    private bool hasCachedDefaultPlayerSpeed = false;

    private void Awake()
    {
        InitializeCameraRotationSpeedUI();
        InitializePlayerSpeedUI();
        ApplyClosedStateImmediate();
    }

    private void OnDestroy()
    {
        if (rotationSpeedSlider != null)
            rotationSpeedSlider.onValueChanged.RemoveListener(SetCameraRotationSpeedFromSlider);

        if (playerSpeedSlider != null)
            playerSpeedSlider.onValueChanged.RemoveListener(SetPlayerSpeedFromSlider);
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleMenu();
        }
    }

    public void ToggleMenu()
    {
        SetMenuOpen(!isOpen);
    }

    public void OpenMenu()
    {
        SetMenuOpen(true);
    }

    public void CloseMenu()
    {
        SetMenuOpen(false);
    }

    public void ShowMainPanel()
    {
        if (mainPanel != null)
            mainPanel.SetActive(true);

        if (levelSelectPanel != null)
            levelSelectPanel.SetActive(false);
    }

    public void ShowLevelSelectPanel()
    {
        RefreshCurrentLevelButtonState();

        if (mainPanel != null)
            mainPanel.SetActive(false);

        if (levelSelectPanel != null)
            levelSelectPanel.SetActive(true);
    }

    public void RestartCurrentLevel()
    {
        Time.timeScale = 1f;
        string currentSceneName = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene(currentSceneName, LoadSceneMode.Single);
    }

    public void QuitGame()
    {
        Time.timeScale = 1f;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void LoadLevelByIndex(int levelIndex)
    {
        if (levelSceneNames == null)
            return;

        if (levelIndex < 0 || levelIndex >= levelSceneNames.Length)
        {
            Debug.LogWarning($"LoadLevelByIndex 失败：索引 {levelIndex} 超出范围。");
            return;
        }

        string sceneName = levelSceneNames[levelIndex];
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning($"LoadLevelByIndex 失败：levelSceneNames[{levelIndex}] 为空。");
            return;
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    public void LoadLevel1() => LoadLevelByIndex(0);
    public void LoadLevel2() => LoadLevelByIndex(1);
    public void LoadLevel3() => LoadLevelByIndex(2);
    public void LoadLevel4() => LoadLevelByIndex(3);
    public void LoadLevel5() => LoadLevelByIndex(4);
    public void LoadLevel6() => LoadLevelByIndex(5);

    private void SetMenuOpen(bool open)
    {
        isOpen = open;

        if (menuRoot != null)
            menuRoot.SetActive(isOpen);

        if (isOpen)
        {
            RefreshCameraRotationSpeedUIFromTarget();
            RefreshPlayerSpeedUIFromTarget();
            ShowMainPanel();

            if (pauseGameWhenOpen)
                Time.timeScale = 0f;

            if (showCursorWhenOpen)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }
        else
        {
            if (pauseGameWhenOpen)
                Time.timeScale = 1f;

            if (lockCursorWhenClosed)
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }
            else
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }
    }

    private void ApplyClosedStateImmediate()
    {
        isOpen = false;

        if (menuRoot != null)
            menuRoot.SetActive(false);

        if (pauseGameWhenOpen)
            Time.timeScale = 1f;

        if (lockCursorWhenClosed)
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
        else
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
    }

    private void RefreshCurrentLevelButtonState()
    {
        if (levelButtons == null || levelSceneNames == null)
            return;

        string currentSceneName = SceneManager.GetActiveScene().name;

        for (int i = 0; i < levelButtons.Length; i++)
        {
            Button button = levelButtons[i];
            if (button == null)
                continue;

            bool isCurrentLevel =
                i < levelSceneNames.Length &&
                !string.IsNullOrEmpty(levelSceneNames[i]) &&
                levelSceneNames[i] == currentSceneName;

            button.interactable = !isCurrentLevel;

            ColorBlock colors = button.colors;
            if (isCurrentLevel)
            {
                colors.normalColor = currentLevelButtonColor;
                colors.selectedColor = currentLevelButtonColor;
                colors.disabledColor = currentLevelButtonColor;
            }

            button.colors = colors;
        }
    }

    private void InitializeCameraRotationSpeedUI()
    {
        ResolveTargetCameraMovement();

        if (targetCameraMovement != null)
        {
            CacheDefaultCameraRotationSpeed();

            if (saveRotationSpeedWithPlayerPrefs && PlayerPrefs.HasKey(rotationSpeedPrefsKey))
            {
                float savedYawSpeed = PlayerPrefs.GetFloat(rotationSpeedPrefsKey);
                ApplyCameraRotationSpeed(savedYawSpeed, false);
            }
        }

        if (rotationSpeedSlider != null)
        {
            rotationSpeedSlider.onValueChanged.RemoveListener(SetCameraRotationSpeedFromSlider);

            float min = Mathf.Min(rotationSpeedMin, rotationSpeedMax);
            float max = Mathf.Max(rotationSpeedMin, rotationSpeedMax);

            rotationSpeedSlider.minValue = min;
            rotationSpeedSlider.maxValue = max;

            float currentSpeed = targetCameraMovement != null
                ? targetCameraMovement.yawSpeed
                : Mathf.Clamp(defaultYawSpeed, min, max);

            rotationSpeedSlider.SetValueWithoutNotify(Mathf.Clamp(currentSpeed, min, max));
            rotationSpeedSlider.onValueChanged.AddListener(SetCameraRotationSpeedFromSlider);
        }

        RefreshRotationSpeedValueText();
    }

    private void ResolveTargetCameraMovement()
    {
        if (targetCameraMovement != null)
            return;

        if (!autoFindCameraMovement)
            return;

        targetCameraMovement = FindObjectOfType<CameraMovement>();
    }

    private void CacheDefaultCameraRotationSpeed()
    {
        if (hasCachedDefaultCameraSpeed)
            return;

        if (targetCameraMovement == null)
            return;

        defaultYawSpeed = Mathf.Max(0.01f, targetCameraMovement.yawSpeed);
        defaultPitchSpeed = Mathf.Max(0.01f, targetCameraMovement.pitchSpeed);

        pitchYawRatio = defaultYawSpeed > 0.01f
            ? defaultPitchSpeed / defaultYawSpeed
            : 1f;

        hasCachedDefaultCameraSpeed = true;
    }

    public void SetCameraRotationSpeedFromSlider(float yawSpeed)
    {
        ApplyCameraRotationSpeed(yawSpeed, true);
    }

    public void SetCameraRotationSpeed(float yawSpeed)
    {
        ApplyCameraRotationSpeed(yawSpeed, true);
    }

    public void ResetCameraRotationSpeedToDefault()
    {
        ResolveTargetCameraMovement();

        if (targetCameraMovement == null)
            return;

        targetCameraMovement.yawSpeed = defaultYawSpeed;
        targetCameraMovement.pitchSpeed = defaultPitchSpeed;

        if (rotationSpeedSlider != null)
            rotationSpeedSlider.SetValueWithoutNotify(defaultYawSpeed);

        RefreshRotationSpeedValueText();

        if (saveRotationSpeedWithPlayerPrefs)
        {
            PlayerPrefs.SetFloat(rotationSpeedPrefsKey, defaultYawSpeed);
            PlayerPrefs.Save();
        }
    }

    private void ApplyCameraRotationSpeed(float yawSpeed, bool save)
    {
        ResolveTargetCameraMovement();

        float min = Mathf.Min(rotationSpeedMin, rotationSpeedMax);
        float max = Mathf.Max(rotationSpeedMin, rotationSpeedMax);
        float clampedYawSpeed = Mathf.Clamp(yawSpeed, min, max);

        if (targetCameraMovement != null)
        {
            CacheDefaultCameraRotationSpeed();

            targetCameraMovement.yawSpeed = clampedYawSpeed;

            if (keepPitchYawRatio)
                targetCameraMovement.pitchSpeed = Mathf.Max(0.01f, clampedYawSpeed * pitchYawRatio);
            else
                targetCameraMovement.pitchSpeed = clampedYawSpeed;
        }

        if (rotationSpeedSlider != null)
            rotationSpeedSlider.SetValueWithoutNotify(clampedYawSpeed);

        RefreshRotationSpeedValueText();

        if (save && saveRotationSpeedWithPlayerPrefs)
        {
            PlayerPrefs.SetFloat(rotationSpeedPrefsKey, clampedYawSpeed);
            PlayerPrefs.Save();
        }
    }

    private void RefreshCameraRotationSpeedUIFromTarget()
    {
        ResolveTargetCameraMovement();

        if (targetCameraMovement != null && rotationSpeedSlider != null)
        {
            float min = Mathf.Min(rotationSpeedMin, rotationSpeedMax);
            float max = Mathf.Max(rotationSpeedMin, rotationSpeedMax);
            rotationSpeedSlider.SetValueWithoutNotify(Mathf.Clamp(targetCameraMovement.yawSpeed, min, max));
        }

        RefreshRotationSpeedValueText();
    }

    private void RefreshRotationSpeedValueText()
    {
        if (rotationSpeedValueText == null)
            return;

        if (targetCameraMovement == null)
        {
            rotationSpeedValueText.text = "Camera Speed: 未绑定";
            return;
        }

        //rotationSpeedValueText.text =
        //    $"Camera Speed: {targetCameraMovement.yawSpeed:0} / {targetCameraMovement.pitchSpeed:0}";
        rotationSpeedValueText.text = $"{targetCameraMovement.yawSpeed:0}";
    }

    private void InitializePlayerSpeedUI()
    {
        ResolveTargetCharacterMovement();

        if (targetCharacterMovement != null)
        {
            CacheDefaultPlayerSpeed();

            if (savePlayerSpeedWithPlayerPrefs && PlayerPrefs.HasKey(playerSpeedPrefsKey))
            {
                float savedPlayerSpeed = PlayerPrefs.GetFloat(playerSpeedPrefsKey);
                ApplyPlayerSpeed(savedPlayerSpeed, false);
            }
        }

        if (playerSpeedSlider != null)
        {
            playerSpeedSlider.onValueChanged.RemoveListener(SetPlayerSpeedFromSlider);

            float min = Mathf.Min(playerSpeedMin, playerSpeedMax);
            float max = Mathf.Max(playerSpeedMin, playerSpeedMax);

            playerSpeedSlider.minValue = min;
            playerSpeedSlider.maxValue = max;

            float currentSpeed = targetCharacterMovement != null
                ? targetCharacterMovement.playerSpeed
                : Mathf.Clamp(defaultPlayerSpeed, min, max);

            playerSpeedSlider.SetValueWithoutNotify(Mathf.Clamp(currentSpeed, min, max));
            playerSpeedSlider.onValueChanged.AddListener(SetPlayerSpeedFromSlider);
        }

        RefreshPlayerSpeedValueText();
    }

    private void ResolveTargetCharacterMovement()
    {
        if (targetCharacterMovement != null)
            return;

        if (!autoFindCharacterMovement)
            return;

        targetCharacterMovement = FindObjectOfType<CharacterMovement>();
    }

    private void CacheDefaultPlayerSpeed()
    {
        if (hasCachedDefaultPlayerSpeed)
            return;

        if (targetCharacterMovement == null)
            return;

        defaultPlayerSpeed = Mathf.Max(0.01f, targetCharacterMovement.playerSpeed);
        hasCachedDefaultPlayerSpeed = true;
    }

    public void SetPlayerSpeedFromSlider(float playerSpeed)
    {
        ApplyPlayerSpeed(playerSpeed, true);
    }

    public void SetPlayerSpeed(float playerSpeed)
    {
        ApplyPlayerSpeed(playerSpeed, true);
    }

    public void ResetPlayerSpeedToDefault()
    {
        ResolveTargetCharacterMovement();

        if (targetCharacterMovement == null)
            return;

        targetCharacterMovement.playerSpeed = defaultPlayerSpeed;

        if (playerSpeedSlider != null)
            playerSpeedSlider.SetValueWithoutNotify(defaultPlayerSpeed);

        RefreshPlayerSpeedValueText();

        if (savePlayerSpeedWithPlayerPrefs)
        {
            PlayerPrefs.SetFloat(playerSpeedPrefsKey, defaultPlayerSpeed);
            PlayerPrefs.Save();
        }
    }

    private void ApplyPlayerSpeed(float playerSpeed, bool save)
    {
        ResolveTargetCharacterMovement();

        float min = Mathf.Min(playerSpeedMin, playerSpeedMax);
        float max = Mathf.Max(playerSpeedMin, playerSpeedMax);
        float clampedPlayerSpeed = Mathf.Clamp(playerSpeed, min, max);

        if (targetCharacterMovement != null)
            targetCharacterMovement.playerSpeed = clampedPlayerSpeed;

        if (playerSpeedSlider != null)
            playerSpeedSlider.SetValueWithoutNotify(clampedPlayerSpeed);

        RefreshPlayerSpeedValueText();

        if (save && savePlayerSpeedWithPlayerPrefs)
        {
            PlayerPrefs.SetFloat(playerSpeedPrefsKey, clampedPlayerSpeed);
            PlayerPrefs.Save();
        }
    }

    private void RefreshPlayerSpeedUIFromTarget()
    {
        ResolveTargetCharacterMovement();

        if (targetCharacterMovement != null && playerSpeedSlider != null)
        {
            float min = Mathf.Min(playerSpeedMin, playerSpeedMax);
            float max = Mathf.Max(playerSpeedMin, playerSpeedMax);
            playerSpeedSlider.SetValueWithoutNotify(Mathf.Clamp(targetCharacterMovement.playerSpeed, min, max));
        }

        RefreshPlayerSpeedValueText();
    }

    private void RefreshPlayerSpeedValueText()
    {
        if (playerSpeedValueText == null)
            return;

        if (targetCharacterMovement == null)
        {
            playerSpeedValueText.text = "Player Speed: 未绑定";
            return;
        }

        playerSpeedValueText.text = $"{targetCharacterMovement.playerSpeed:0.0}";
        //playerSpeedValueText.text = $"Player Speed: {targetCharacterMovement.playerSpeed:0.0}";
    }
}