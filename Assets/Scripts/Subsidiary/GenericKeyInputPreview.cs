using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("Game/UI/Generic Key Input Preview")]
public class GenericKeyInputPreview : MonoBehaviour
{
    [Serializable]
    public sealed class KeyVisualBinding
    {
        [Header("按键")]
        public KeyCode key = KeyCode.W;

        [Tooltip("状态文本里显示的名字。留空则自动用 key.ToString()，Space 会自动显示为 Space。")]
        public string displayName = "";

        [Header("UI")]
        public Image iconImage;
        public Graphic labelGraphic;

        [NonSerialized] public Vector3 initialScale = Vector3.one;
        [NonSerialized] public bool isPressed = false;

        public string GetDisplayName()
        {
            if (!string.IsNullOrEmpty(displayName))
                return displayName;

            return key == KeyCode.Space ? "Space" : key.ToString();
        }
    }

    [Header("按键配置")]
    [Tooltip("这里可以自由添加任意按键绑定，例如 W/A/S/D/Space/E/Mouse0/Escape 等。")]
    public List<KeyVisualBinding> keyBindings = new List<KeyVisualBinding>()
    {
        new KeyVisualBinding() { key = KeyCode.W, displayName = "W" },
        new KeyVisualBinding() { key = KeyCode.A, displayName = "A" },
        new KeyVisualBinding() { key = KeyCode.S, displayName = "S" },
        new KeyVisualBinding() { key = KeyCode.D, displayName = "D" },
        new KeyVisualBinding() { key = KeyCode.Space, displayName = "Space" },
    };

    [Header("总状态文本（可选）")]
    [Tooltip("用于显示当前按下了哪些键，例如：W+A / Mouse0+E / Space / None")]
    public Text stateText;

    [Header("显示设置")]
    [Tooltip("是否按 keyBindings 列表中的顺序输出状态文本。建议开启。")]
    public bool useBindingOrderForStateText = true;

    [Tooltip("没有按键按下时显示的文本。")]
    public string noneText = "None";

    [Tooltip("多个按键同时按下时的连接符。")]
    public string joinSeparator = "+";

    [Header("颜色")]
    public Color idleColor = new Color(0.94f, 0.96f, 1.00f, 1.00f);
    public Color pressedColor = new Color(0.45f, 0.90f, 1.00f, 1.00f);

    [Header("缩放")]
    [Min(1f)] public float pressedScaleMultiplier = 1.06f;

    private void Awake()
    {
        CacheInitialScales();
        RefreshAllVisuals();
    }

    private void OnEnable()
    {
        CacheInitialScales();
        RefreshAllVisuals();
    }

    private void Update()
    {
        for (int i = 0; i < keyBindings.Count; i++)
        {
            KeyVisualBinding binding = keyBindings[i];
            if (binding == null)
                continue;

            binding.isPressed = Input.GetKey(binding.key);
            RefreshVisual(binding);
        }

        RefreshStateText();
    }

    private void CacheInitialScales()
    {
        for (int i = 0; i < keyBindings.Count; i++)
        {
            KeyVisualBinding binding = keyBindings[i];
            if (binding == null || binding.iconImage == null)
                continue;

            binding.initialScale = binding.iconImage.rectTransform.localScale;
        }
    }

    private void RefreshAllVisuals()
    {
        for (int i = 0; i < keyBindings.Count; i++)
        {
            KeyVisualBinding binding = keyBindings[i];
            if (binding == null)
                continue;

            binding.isPressed = Input.GetKey(binding.key);
            RefreshVisual(binding);
        }

        RefreshStateText();
    }

    private void RefreshVisual(KeyVisualBinding binding)
    {
        if (binding == null)
            return;

        Color targetColor = binding.isPressed ? pressedColor : idleColor;
        float scaleMultiplier = binding.isPressed ? pressedScaleMultiplier : 1f;

        if (binding.iconImage != null)
        {
            binding.iconImage.color = targetColor;
            binding.iconImage.rectTransform.localScale = binding.initialScale * scaleMultiplier;
        }

        if (binding.labelGraphic != null)
        {
            binding.labelGraphic.color = targetColor;
        }
    }

    private void RefreshStateText()
    {
        if (stateText == null)
            return;

        string pressedKeys = BuildPressedKeysString();
        stateText.text = string.IsNullOrEmpty(pressedKeys) ? noneText : pressedKeys;
    }

    private string BuildPressedKeysString()
    {
        List<string> pressed = new List<string>(keyBindings.Count);

        if (useBindingOrderForStateText)
        {
            for (int i = 0; i < keyBindings.Count; i++)
            {
                KeyVisualBinding binding = keyBindings[i];
                if (binding == null)
                    continue;

                if (binding.isPressed)
                    pressed.Add(binding.GetDisplayName());
            }
        }
        else
        {
            // 不按列表顺序时，仍然基于当前 bindings 扫描，只是不强调人为排序
            for (int i = 0; i < keyBindings.Count; i++)
            {
                KeyVisualBinding binding = keyBindings[i];
                if (binding == null)
                    continue;

                if (binding.isPressed)
                    pressed.Add(binding.GetDisplayName());
            }
        }

        if (pressed.Count == 0)
            return string.Empty;

        return string.Join(joinSeparator, pressed);
    }

    [ContextMenu("Refresh Now")]
    public void RefreshNow()
    {
        RefreshAllVisuals();
    }
}