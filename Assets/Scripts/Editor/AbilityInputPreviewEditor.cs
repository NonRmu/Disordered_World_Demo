using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AbilityInputPreview))]
public class AbilityInputPreviewEditor : Editor
{
    private SerializedProperty scriptProp;

    private SerializedProperty asciiWorldModeManagerProp;
    private SerializedProperty asciiRewindChargeControllerProp;
    private SerializedProperty abilitiesProp;

    private SerializedProperty lockedColorProp;
    private SerializedProperty readyColorProp;
    private SerializedProperty pressingColorProp;
    private SerializedProperty usingColorProp;
    private SerializedProperty cooldownColorProp;
    private SerializedProperty failedColorProp;

    private SerializedProperty pressingRadialColorProp;
    private SerializedProperty usingRadialColorProp;
    private SerializedProperty cooldownRadialColorProp;

    private SerializedProperty usingScaleMultiplierProp;

    private void OnEnable()
    {
        scriptProp = serializedObject.FindProperty("m_Script");

        asciiWorldModeManagerProp = serializedObject.FindProperty("asciiWorldModeManager");
        asciiRewindChargeControllerProp = serializedObject.FindProperty("asciiRewindChargeController");
        abilitiesProp = serializedObject.FindProperty("abilities");

        lockedColorProp = serializedObject.FindProperty("lockedColor");
        readyColorProp = serializedObject.FindProperty("readyColor");
        pressingColorProp = serializedObject.FindProperty("pressingColor");
        usingColorProp = serializedObject.FindProperty("usingColor");
        cooldownColorProp = serializedObject.FindProperty("cooldownColor");
        failedColorProp = serializedObject.FindProperty("failedColor");

        pressingRadialColorProp = serializedObject.FindProperty("pressingRadialColor");
        usingRadialColorProp = serializedObject.FindProperty("usingRadialColor");
        cooldownRadialColorProp = serializedObject.FindProperty("cooldownRadialColor");

        usingScaleMultiplierProp = serializedObject.FindProperty("usingScaleMultiplier");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.PropertyField(scriptProp);
        }

        EditorGUILayout.Space(4f);

        DrawReferencesSection();
        EditorGUILayout.Space(6f);

        DrawAbilitiesSection();
        EditorGUILayout.Space(6f);

        DrawColorsSection();
        EditorGUILayout.Space(6f);

        DrawRadialColorsSection();
        EditorGUILayout.Space(6f);

        EditorGUILayout.PropertyField(usingScaleMultiplierProp);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawReferencesSection()
    {
        EditorGUILayout.PropertyField(asciiWorldModeManagerProp);
        EditorGUILayout.PropertyField(asciiRewindChargeControllerProp);
    }

    private void DrawAbilitiesSection()
    {
        if (abilitiesProp == null || !abilitiesProp.isArray)
        {
            EditorGUILayout.HelpBox("abilities 列表不存在或不是数组。", MessageType.Error);
            return;
        }

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("添加能力"))
        {
            int index = abilitiesProp.arraySize;
            abilitiesProp.InsertArrayElementAtIndex(index);

            SerializedProperty newElement = abilitiesProp.GetArrayElementAtIndex(index);
            ResetAbilityElement(newElement);
        }

        if (GUILayout.Button("清空能力"))
        {
            abilitiesProp.ClearArray();
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(4f);

        for (int i = 0; i < abilitiesProp.arraySize; i++)
        {
            SerializedProperty element = abilitiesProp.GetArrayElementAtIndex(i);
            DrawAbilityElement(element, i);
            EditorGUILayout.Space(6f);
        }
    }

    private void DrawAbilityElement(SerializedProperty element, int index)
    {
        SerializedProperty abilityTypeProp = element.FindPropertyRelative("abilityType");
        SerializedProperty inputBindingModeProp = element.FindPropertyRelative("inputBindingMode");
        SerializedProperty triggerKeyProp = element.FindPropertyRelative("triggerKey");
        SerializedProperty boundActionTypeProp = element.FindPropertyRelative("boundActionType");
        SerializedProperty unlockedProp = element.FindPropertyRelative("unlocked");

        SerializedProperty iconImageProp = element.FindPropertyRelative("iconImage");
        SerializedProperty labelGraphicProp = element.FindPropertyRelative("labelGraphic");
        SerializedProperty radialProgressImageProp = element.FindPropertyRelative("radialProgressImage");

        SerializedProperty tapWindowDurationProp = element.FindPropertyRelative("tapWindowDuration");
        SerializedProperty tapConfirmFeedbackDurationProp = element.FindPropertyRelative("tapConfirmFeedbackDuration");

        SerializedProperty firstStageDurationProp = element.FindPropertyRelative("firstStageDuration");
        SerializedProperty secondStageDurationProp = element.FindPropertyRelative("secondStageDuration");
        SerializedProperty allowEnterSecondStageProp = element.FindPropertyRelative("allowEnterSecondStage");
        SerializedProperty failedFeedbackDurationProp = element.FindPropertyRelative("failedFeedbackDuration");

        string title = $"能力 {index}";
        if (abilityTypeProp != null)
            title += $" - {(AbilityInputPreview.AbilityType)abilityTypeProp.enumValueIndex}";

        element.isExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(element.isExpanded, title);

        if (element.isExpanded)
        {
            EditorGUILayout.BeginVertical("box");

            DrawMiniToolbar(index);

            EditorGUILayout.Space(4f);

            EditorGUILayout.PropertyField(abilityTypeProp);
            EditorGUILayout.PropertyField(inputBindingModeProp);
            EditorGUILayout.PropertyField(unlockedProp);

            EditorGUILayout.Space(4f);

            var inputMode = (AbilityInputPreview.InputBindingMode)inputBindingModeProp.enumValueIndex;
            var abilityType = (AbilityInputPreview.AbilityType)abilityTypeProp.enumValueIndex;
            var boundAction = (AbilityInputPreview.BoundActionType)boundActionTypeProp.enumValueIndex;

            switch (inputMode)
            {
                case AbilityInputPreview.InputBindingMode.LocalKey:
                    EditorGUILayout.PropertyField(triggerKeyProp);
                    break;

                case AbilityInputPreview.InputBindingMode.ASCIIWorldModeManagerAction:
                case AbilityInputPreview.InputBindingMode.ASCIIRewindChargeControllerAction:
                    EditorGUILayout.PropertyField(boundActionTypeProp);
                    break;

                default:
                    EditorGUILayout.PropertyField(triggerKeyProp);
                    EditorGUILayout.PropertyField(boundActionTypeProp);
                    break;
            }

            EditorGUILayout.Space(4f);

            EditorGUILayout.PropertyField(iconImageProp);
            EditorGUILayout.PropertyField(labelGraphicProp);
            EditorGUILayout.PropertyField(radialProgressImageProp);

            EditorGUILayout.Space(4f);

            DrawTypeSpecificSection(
                abilityType,
                inputMode,
                boundAction,
                tapWindowDurationProp,
                tapConfirmFeedbackDurationProp,
                firstStageDurationProp,
                secondStageDurationProp,
                allowEnterSecondStageProp,
                failedFeedbackDurationProp
            );

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    private void DrawMiniToolbar(int index)
    {
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("上移") && index > 0)
            abilitiesProp.MoveArrayElement(index, index - 1);

        if (GUILayout.Button("下移") && index < abilitiesProp.arraySize - 1)
            abilitiesProp.MoveArrayElement(index, index + 1);

        Color oldColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(1f, 0.65f, 0.65f);

        if (GUILayout.Button("删除"))
        {
            abilitiesProp.DeleteArrayElementAtIndex(index);
            GUI.backgroundColor = oldColor;
            EditorGUILayout.EndHorizontal();
            return;
        }

        GUI.backgroundColor = oldColor;
        EditorGUILayout.EndHorizontal();
    }

    private void DrawTypeSpecificSection(
        AbilityInputPreview.AbilityType abilityType,
        AbilityInputPreview.InputBindingMode inputMode,
        AbilityInputPreview.BoundActionType boundAction,
        SerializedProperty tapWindowDurationProp,
        SerializedProperty tapConfirmFeedbackDurationProp,
        SerializedProperty firstStageDurationProp,
        SerializedProperty secondStageDurationProp,
        SerializedProperty allowEnterSecondStageProp,
        SerializedProperty failedFeedbackDurationProp)
    {
        switch (abilityType)
        {
            case AbilityInputPreview.AbilityType.TapWindowConfirm:
            {
                bool isQBinding =
                    inputMode == AbilityInputPreview.InputBindingMode.ASCIIWorldModeManagerAction &&
                    boundAction == AbilityInputPreview.BoundActionType.QScanMode;

                if (isQBinding)
                {
                    EditorGUILayout.HelpBox(
                        "当前为 Q 扫描模式绑定。\n" +
                        "窗口持续时长由 ASCIIWorldModeManager.revertNearbyVirtualPreviewDuration 控制。\n" +
                        "这里只保留 tapConfirmFeedbackDuration 作为 UI 二次确认反馈时长配置。",
                        MessageType.Info);

                    EditorGUILayout.PropertyField(tapConfirmFeedbackDurationProp);
                }
                else
                {
                    EditorGUILayout.PropertyField(tapWindowDurationProp);
                    EditorGUILayout.PropertyField(tapConfirmFeedbackDurationProp);
                }

                break;
            }

            case AbilityInputPreview.AbilityType.TwoStageHoldCharge:
            {
                bool isRewindBinding =
                    inputMode == AbilityInputPreview.InputBindingMode.ASCIIRewindChargeControllerAction &&
                    boundAction == AbilityInputPreview.BoundActionType.RRewindMode;

                if (isRewindBinding)
                {
                    EditorGUILayout.HelpBox(
                        "当前为 R 回溯模式绑定。\n" +
                        "第一阶段、第二阶段、冷却时长均由 ASCIIRewindChargeController 控制。\n" +
                        "这里只保留 failedFeedbackDuration 作为 UI 失败反馈时长配置。",
                        MessageType.Info);

                    EditorGUILayout.PropertyField(failedFeedbackDurationProp);
                }
                else
                {
                    EditorGUILayout.PropertyField(firstStageDurationProp);
                    EditorGUILayout.PropertyField(secondStageDurationProp);
                    EditorGUILayout.PropertyField(allowEnterSecondStageProp);
                    EditorGUILayout.PropertyField(failedFeedbackDurationProp);
                }

                break;
            }

            case AbilityInputPreview.AbilityType.Toggle:
                EditorGUILayout.HelpBox("Toggle 类型无额外专属时间参数。", MessageType.None);
                break;

            case AbilityInputPreview.AbilityType.HoldActive:
                EditorGUILayout.HelpBox("HoldActive 类型无额外专属时间参数。", MessageType.None);
                break;
        }
    }

    private void DrawColorsSection()
    {
        EditorGUILayout.PropertyField(lockedColorProp);
        EditorGUILayout.PropertyField(readyColorProp);
        EditorGUILayout.PropertyField(pressingColorProp);
        EditorGUILayout.PropertyField(usingColorProp);
        EditorGUILayout.PropertyField(cooldownColorProp);
        EditorGUILayout.PropertyField(failedColorProp);
    }

    private void DrawRadialColorsSection()
    {
        EditorGUILayout.PropertyField(pressingRadialColorProp);
        EditorGUILayout.PropertyField(usingRadialColorProp);
        EditorGUILayout.PropertyField(cooldownRadialColorProp);
    }

    private void ResetAbilityElement(SerializedProperty element)
    {
        if (element == null)
            return;

        SerializedProperty abilityTypeProp = element.FindPropertyRelative("abilityType");
        SerializedProperty inputBindingModeProp = element.FindPropertyRelative("inputBindingMode");
        SerializedProperty triggerKeyProp = element.FindPropertyRelative("triggerKey");
        SerializedProperty boundActionTypeProp = element.FindPropertyRelative("boundActionType");
        SerializedProperty unlockedProp = element.FindPropertyRelative("unlocked");

        SerializedProperty cooldownDurationProp = element.FindPropertyRelative("cooldownDuration");
        SerializedProperty tapWindowDurationProp = element.FindPropertyRelative("tapWindowDuration");
        SerializedProperty tapConfirmFeedbackDurationProp = element.FindPropertyRelative("tapConfirmFeedbackDuration");
        SerializedProperty firstStageDurationProp = element.FindPropertyRelative("firstStageDuration");
        SerializedProperty secondStageDurationProp = element.FindPropertyRelative("secondStageDuration");
        SerializedProperty allowEnterSecondStageProp = element.FindPropertyRelative("allowEnterSecondStage");
        SerializedProperty failedFeedbackDurationProp = element.FindPropertyRelative("failedFeedbackDuration");

        if (abilityTypeProp != null) abilityTypeProp.enumValueIndex = (int)AbilityInputPreview.AbilityType.TapWindowConfirm;
        if (inputBindingModeProp != null) inputBindingModeProp.enumValueIndex = (int)AbilityInputPreview.InputBindingMode.LocalKey;
        if (triggerKeyProp != null) triggerKeyProp.enumValueIndex = (int)KeyCode.None;
        if (boundActionTypeProp != null) boundActionTypeProp.enumValueIndex = (int)AbilityInputPreview.BoundActionType.None;
        if (unlockedProp != null) unlockedProp.boolValue = true;

        if (cooldownDurationProp != null) cooldownDurationProp.floatValue = 0f;
        if (tapWindowDurationProp != null) tapWindowDurationProp.floatValue = 3f;
        if (tapConfirmFeedbackDurationProp != null) tapConfirmFeedbackDurationProp.floatValue = 0.2f;
        if (firstStageDurationProp != null) firstStageDurationProp.floatValue = 1f;
        if (secondStageDurationProp != null) secondStageDurationProp.floatValue = 1.5f;
        if (allowEnterSecondStageProp != null) allowEnterSecondStageProp.boolValue = true;
        if (failedFeedbackDurationProp != null) failedFeedbackDurationProp.floatValue = 0.2f;

        element.isExpanded = true;
    }
}