using UnityEngine;

[AddComponentMenu("Game/Camera Movement")]
public class CameraMovement : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("玩家根节点。")]
    public Transform player;

    [Tooltip("世界模式管理器。")]
    public ASCIIWorldModeManager asciiWorldModeManager;
    public EscPauseMenuUI escPauseMenuUI;

    [Header("相机层级")]
    [Tooltip("YawPivot。为空时自动取第一个子物体。")]
    public Transform yawPivot;

    [Tooltip("PitchPivot。为空时自动取 YawPivot 的第一个子物体。")]
    public Transform pitchPivot;

    [Header("普通模式跟随")]
    [Tooltip("普通第三人称跟随偏移。")]
    public Vector3 normalFollowOffset = new Vector3(0f, 1.35f, 0f);

    [Tooltip("普通模式跟随平滑速度。")]
    public float normalFollowSmooth = 12f;

    [Header("投影视图跟随")]
    [Tooltip("投影视图越肩偏移，X=肩偏移，Y=高度。")]
    public Vector3 projectionFollowOffset = new Vector3(0.35f, 1.35f, 0f);

    [Tooltip("投影视图前向构图补偿。")]
    public float projectionForwardCompensation = 0.45f;

    [Tooltip("投影视图跟随平滑速度。")]
    public float projectionFollowSmooth = 18f;

    [Header("旋转")]
    [Tooltip("水平旋转速度。")]
    public float yawSpeed = 300f;

    [Tooltip("俯仰旋转速度。")]
    public float pitchSpeed = 220f;

    [Tooltip("最小俯仰角。")]
    public float minPitch = -60f;

    [Tooltip("最大俯仰角。")]
    public float maxPitch = 60f;

    [Header("相机局部位置")]
    [Tooltip("普通模式下 MainCamera 的局部位置。")]
    public Vector3 normalCameraLocalPos = new Vector3(0f, 0f, -4.5f);

    [Tooltip("投影视图下 MainCamera 的局部位置。")]
    public Vector3 projectionCameraLocalPos = new Vector3(0f, 0f, -2.2f);

    [Tooltip("相机局部位置插值速度。")]
    public float cameraLocalLerpSpeed = 10f;

    [Header("FOV")]
    [Tooltip("普通模式 FOV。")]
    public float normalFOV = 60f;

    [Tooltip("投影视图 FOV。")]
    public float projectionFOV = 50f;

    [Tooltip("FOV 插值速度。")]
    public float fovLerpSpeed = 8f;

    [Header("鼠标")]
    [Tooltip("是否在激活时锁定鼠标。")]
    public bool lockCursorWhenActive = true;

    [Header("准星")]
    public bool drawCrosshairInProjectionView = true;
    public float crosshairSize = 10f;
    public float crosshairThickness = 2f;
    public Color crosshairColor = Color.white;

    private Camera cam;
    private float yaw;
    private float pitch;
    private Texture2D crosshairTex;

    private void Awake()
    {
        if (yawPivot == null && transform.childCount > 0)
            yawPivot = transform.GetChild(0);

        if (pitchPivot == null && yawPivot != null && yawPivot.childCount > 0)
            pitchPivot = yawPivot.GetChild(0);

        cam = GetComponentInChildren<Camera>();

        if (yawPivot != null)
            yaw = yawPivot.eulerAngles.y;

        if (pitchPivot != null)
        {
            pitch = pitchPivot.localEulerAngles.x;
            if (pitch > 180f) pitch -= 360f;
        }

        if (player != null)
            transform.position = player.position + normalFollowOffset;

        if (cam != null)
        {
            cam.transform.localPosition = normalCameraLocalPos;
            cam.transform.localRotation = Quaternion.identity;
            cam.fieldOfView = normalFOV;
        }

        crosshairTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        crosshairTex.SetPixel(0, 0, Color.white);
        crosshairTex.Apply();
    }

    private void LateUpdate()
    {
        if (escPauseMenuUI != null && escPauseMenuUI.IsOpen)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            return;
        }

        if (player == null || yawPivot == null || pitchPivot == null || cam == null)
            return;

        bool freeCursor = Input.GetKey(KeyCode.LeftAlt);

        Cursor.visible = freeCursor;
        if (lockCursorWhenActive)
        {
            Cursor.lockState = freeCursor
                ? CursorLockMode.Confined
                : CursorLockMode.Locked;
        }

        // if (freeCursor)
        //     return;

        bool inProjectionView = asciiWorldModeManager != null && asciiWorldModeManager.InProjectionView;

        UpdateRotation();
        UpdateRigFollow(inProjectionView);
        UpdateCameraLocal(inProjectionView);
    }

    private void UpdateRotation()
    {
        float mouseX = Input.GetAxis("Mouse X") * yawSpeed * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * pitchSpeed * Time.deltaTime;

        yaw += mouseX;
        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        yawPivot.rotation = Quaternion.Euler(0f, yaw, 0f);
        pitchPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void UpdateRigFollow(bool inProjectionView)
    {
        Vector3 targetPos;
        float smooth;

        if (!inProjectionView)
        {
            targetPos = player.position + normalFollowOffset;
            smooth = normalFollowSmooth;
        }
        else
        {
            Vector3 right = yawPivot.right;
            Vector3 forward = yawPivot.forward;

            targetPos = player.position
                        + Vector3.up * projectionFollowOffset.y
                        + right * projectionFollowOffset.x
                        + forward * projectionForwardCompensation;

            smooth = projectionFollowSmooth;
        }

        float t = 1f - Mathf.Exp(-smooth * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPos, t);
    }

    private void UpdateCameraLocal(bool inProjectionView)
    {
        Vector3 targetLocalPos = inProjectionView ? projectionCameraLocalPos : normalCameraLocalPos;
        float targetFov = inProjectionView ? projectionFOV : normalFOV;

        float posT = 1f - Mathf.Exp(-cameraLocalLerpSpeed * Time.deltaTime);
        cam.transform.localPosition = Vector3.Lerp(cam.transform.localPosition, targetLocalPos, posT);
        cam.transform.localRotation = Quaternion.identity;

        float fovT = 1f - Mathf.Exp(-fovLerpSpeed * Time.deltaTime);
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, fovT);
    }

    private void OnGUI()
    {
        if (!drawCrosshairInProjectionView || asciiWorldModeManager == null || !asciiWorldModeManager.InProjectionView)
            return;

        if (crosshairTex == null)
            return;

        Color old = GUI.color;
        GUI.color = crosshairColor;

        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;
        float half = crosshairSize * 0.5f;
        float thickness = Mathf.Max(1f, crosshairThickness);

        GUI.DrawTexture(new Rect(cx - half, cy - thickness * 0.5f, crosshairSize, thickness), crosshairTex);
        GUI.DrawTexture(new Rect(cx - thickness * 0.5f, cy - half, thickness, crosshairSize), crosshairTex);

        GUI.color = old;
    }
}