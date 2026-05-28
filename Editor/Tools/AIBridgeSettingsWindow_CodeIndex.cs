using UnityEditor;
using UnityEngine;

namespace AIBridge.Editor
{
    public partial class AIBridgeSettingsWindow
    {
        private const float CodeIndexSettingsLabelWidthRatio = 0.3f;
        private const float CodeIndexSettingsMinLabelWidth = 220f;
        private const float CodeIndexSettingsMaxLabelWidth = 300f;

        private static readonly string[] CodeIndexCleanupModeLabels =
        {
            "Process Only",
            "Process + Temp",
            "Full Cleanup"
        };

        private static readonly string[] CodeIndexCleanupModeLabelsCn =
        {
            "仅进程状态",
            "进程和临时状态",
            "完整清理"
        };

        private void DrawCodeIndexSettingsTab()
        {
            var settings = AIBridgeProjectSettings.Instance.CodeIndex;

            EditorGUILayout.LabelField(AIBridgeEditorText.T("Read-only Code Index", "只读 Code Index"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                AIBridgeEditorText.T(
                    "Code Index starts a project-local Roslyn daemon for read-only symbol, definition, reference, implementation, caller, and diagnostic queries.",
                    "Code Index 会启动当前工程本地的 Roslyn daemon，用于只读符号、定义、引用、实现、调用者和诊断查询。"),
                MessageType.Info);

            var oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = GetCodeIndexSettingsLabelWidth();
            EditorGUI.BeginChangeCheck();

            settings.EnableCodeIndex = EditorGUILayout.Toggle(
                AIBridgeEditorText.T("Enable Code Index", "启用 Code Index"),
                settings.EnableCodeIndex);

            using (new EditorGUI.DisabledScope(!settings.EnableCodeIndex))
            {
                settings.PrewarmOnUnityStartup = EditorGUILayout.Toggle(
                    AIBridgeEditorText.T("Prewarm On Unity Startup", "Unity 启动后自动预热"),
                    settings.PrewarmOnUnityStartup);

                settings.WarmupDelaySeconds = EditorGUILayout.IntSlider(
                    AIBridgeEditorText.T("Warmup Delay Seconds", "预热延迟秒数"),
                    Mathf.Max(0, settings.WarmupDelaySeconds),
                    0,
                    60);

                using (new EditorGUI.DisabledScope(true))
                {
                    settings.WarmupMode = EditorGUILayout.TextField(
                        AIBridgeEditorText.T("Warmup Mode", "预热模式"),
                        string.IsNullOrEmpty(settings.WarmupMode) ? AIBridgeProjectSettings.DefaultCodeIndexWarmupMode : settings.WarmupMode);
                }

                settings.AutoRefreshOnFileChange = EditorGUILayout.Toggle(
                    AIBridgeEditorText.T("Auto Refresh On File Change", "文件变化后自动刷新"),
                    settings.AutoRefreshOnFileChange);

                settings.FallbackToTextSearch = EditorGUILayout.Toggle(
                    AIBridgeEditorText.T("Fallback To Text Search", "语义不可用时文本降级"),
                    settings.FallbackToTextSearch);

                var cleanupIndex = GetCodeIndexCleanupModeIndex(settings.CleanupModeOnQuit);
                cleanupIndex = EditorGUILayout.Popup(
                    AIBridgeEditorText.T("Cleanup Mode On Quit", "退出清理策略"),
                    cleanupIndex,
                    AIBridgeEditorText.Language == AIBridgeEditorLanguage.SimplifiedChinese
                        ? CodeIndexCleanupModeLabelsCn
                        : CodeIndexCleanupModeLabels);
                settings.CleanupModeOnQuit = AIBridgeProjectSettings.SupportedCodeIndexCleanupModes[Mathf.Clamp(cleanupIndex, 0, AIBridgeProjectSettings.SupportedCodeIndexCleanupModes.Length - 1)];
            }

            var settingsChanged = EditorGUI.EndChangeCheck();
            EditorGUIUtility.labelWidth = oldLabelWidth;

            if (settingsChanged)
            {
                settings.WarmupDelaySeconds = Mathf.Max(0, settings.WarmupDelaySeconds);
                settings.WarmupMode = AIBridgeProjectSettings.DefaultCodeIndexWarmupMode;
                settings.CleanupModeOnQuit = AIBridgeProjectSettings.NormalizeCodeIndexCleanupMode(settings.CleanupModeOnQuit);
                AIBridgeProjectSettings.Instance.SaveSettings();
                AIBridgeCodeIndexEditorUtility.WriteCodeIndexConfig();
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(AIBridgeEditorText.T("Resolved Paths", "解析路径"), EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(
                AIBridgeCodeIndexEditorUtility.ResolveCliPath() ?? AIBridgeEditorText.T("AIBridgeCLI not found", "未找到 AIBridgeCLI"),
                EditorStyles.wordWrappedMiniLabel,
                GUILayout.Height(20));
            EditorGUILayout.SelectableLabel(
                AIBridgeCodeIndexEditorUtility.GetIndexDirectory(),
                EditorStyles.wordWrappedMiniLabel,
                GUILayout.Height(20));

            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(AIBridgeEditorText.T("Warmup Now", "立即预热"), GUILayout.Height(24)))
            {
                var started = AIBridgeCodeIndexEditorUtility.StartWarmupNoWait(manual: true);
                Debug.Log(AIBridgeEditorText.T(
                    started ? "[AIBridge] Code Index warmup started." : "[AIBridge] Code Index warmup was not started.",
                    started ? "[AIBridge] Code Index 预热已启动。" : "[AIBridge] Code Index 预热未启动。"));
            }

            if (GUILayout.Button(AIBridgeEditorText.T("Shutdown", "关闭索引"), GUILayout.Height(24)))
            {
                AIBridgeCodeIndexEditorUtility.ShutdownDaemon(settings.CleanupModeOnQuit, 3000);
                Debug.Log(AIBridgeEditorText.T("[AIBridge] Code Index daemon shutdown requested.", "[AIBridge] 已请求关闭 Code Index daemon。"));
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(AIBridgeEditorText.T("Open State Directory", "打开状态目录"), GUILayout.Height(24)))
            {
                AIBridgeCodeIndexEditorUtility.OpenIndexDirectory();
            }

            if (GUILayout.Button(AIBridgeEditorText.T("Copy Status CLI", "复制状态命令"), GUILayout.Height(24)))
            {
                EditorGUIUtility.systemCopyBuffer = AIBridgeCodeIndexEditorUtility.BuildCliCommand("code_index status");
                Debug.Log(AIBridgeEditorText.T("[AIBridge] Code Index status CLI command copied.", "[AIBridge] Code Index 状态命令已复制。"));
            }
            EditorGUILayout.EndHorizontal();
        }

        private static int GetCodeIndexCleanupModeIndex(string cleanupMode)
        {
            var normalized = AIBridgeProjectSettings.NormalizeCodeIndexCleanupMode(cleanupMode);
            for (var i = 0; i < AIBridgeProjectSettings.SupportedCodeIndexCleanupModes.Length; i++)
            {
                if (AIBridgeProjectSettings.SupportedCodeIndexCleanupModes[i] == normalized)
                {
                    return i;
                }
            }

            return 0;
        }

        private float GetCodeIndexSettingsLabelWidth()
        {
            return Mathf.Clamp(
                position.width * CodeIndexSettingsLabelWidthRatio,
                CodeIndexSettingsMinLabelWidth,
                CodeIndexSettingsMaxLabelWidth);
        }
    }
}
