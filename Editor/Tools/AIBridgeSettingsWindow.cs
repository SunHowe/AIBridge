using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using AIBridge.Editor.ScriptExecution;

namespace AIBridge.Editor
{
    /// <summary>
    /// Main settings window for AI Bridge.
    /// </summary>
    public partial class AIBridgeSettingsWindow : EditorWindow
    {
        // 页签枚举
        private enum TabType
        {
            BasicSettings,    // 基础设置
            GifSettings,      // GIF 设置
            LogSettings,      // 日志设置
            DirectoryInfo,    // 目录信息
            Scripts,          // 脚本执行
            RuntimeBridge,    // Runtime Bridge
            CodeIndex,        // Code Index
            CacheCleanup,
            Actions           // 操作
        }

        private sealed class AssistantIntegrationSelectionState
        {
            public AssistantIntegrationTarget Target { get; set; }
            public bool IsDetected { get; set; }
            public string Detail { get; set; }
            public bool IsSelected { get; set; }
            public string SkillRootDirectory { get; set; }
        }

        private Vector2 _scrollPosition;
        private bool _bridgeEnabled;
        private bool _debugLogging;
        private List<AssistantIntegrationSelectionState> _assistantIntegrationSelections;
        private List<RecommendedSkillInfo> _recommendedSkills;
        private int _selectedRecommendedRepositoryIndex;
        private TabType _currentTab = TabType.BasicSettings; // 当前选中的页签
        private static EditorOption<string> _scriptDirectoryOption;

        // GIF Settings
        private int _gifFrameCount;
        private int _gifFps;
        private float _gifScale;
        private int _gifColorCount;
        private float _gifStartDelay;

        [MenuItem("AIBridge/Settings")]
        private static void OpenWindow()
        {
            var window = GetWindow<AIBridgeSettingsWindow>();
            window.titleContent = new GUIContent(AIBridgeEditorText.T("AI Bridge Settings", "AI Bridge 设置"));
            window.minSize = new Vector2(400, 500);
            window.Show();
        }

        private void OnEnable()
        {
            LoadSettings();
            if (_scriptDirectoryOption == null)
            {
                _scriptDirectoryOption = new EditorOption<string>("AIBridge_ScriptDirectory", AIBridgeProjectSettings.DefaultScriptDirectory, ReadScriptDirectory, WriteScriptDirectory);
            }
            _scriptDirectory = _scriptDirectoryOption.Value;
            RefreshScriptList();
        }

        private void LoadSettings()
        {
            _bridgeEnabled = AIBridge.Enabled;
            _debugLogging = AIBridgeLogger.DebugEnabled;

            _gifFrameCount = GifRecorderSettings.DefaultFrameCount;
            _gifFps = GifRecorderSettings.DefaultFps;
            _gifScale = GifRecorderSettings.DefaultScale;
            _gifColorCount = GifRecorderSettings.DefaultColorCount;
            _gifStartDelay = GifRecorderSettings.DefaultStartDelay;

            LoadLogSettings();
        }

        private void OnGUI()
        {
            titleContent = new GUIContent(AIBridgeEditorText.T("AI Bridge Settings", "AI Bridge 设置"));
            DrawHeader();
            EditorGUILayout.Space(5);

            // 绘制页签工具栏
            DrawTabToolbar();
            EditorGUILayout.Space(5);

            // 绘制当前页签内容
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            
            switch (_currentTab)
            {
                case TabType.BasicSettings:
                    DrawBridgeSettings();
                    break;
                case TabType.GifSettings:
                    DrawGifSettings();
                    break;
                case TabType.LogSettings:
                    DrawLogSettingsTab();
                    break;
                case TabType.DirectoryInfo:
                    DrawDirectoryInfo();
                    break;
                case TabType.Scripts:
                    DrawScriptsTab();
                    break;
                case TabType.RuntimeBridge:
                    DrawRuntimeBridgeSettingsTab();
                    break;
                case TabType.CodeIndex:
                    DrawCodeIndexSettingsTab();
                    break;
                case TabType.CacheCleanup:
                    DrawCacheCleanupSettingsTab();
                    break;
                case TabType.Actions:
                    DrawActions();
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawTabToolbar()
        {
            var tabNames = new[]
            {
                AIBridgeEditorText.T("Basic", "基础设置"),
                AIBridgeEditorText.T("GIF", "GIF 设置"),
                AIBridgeEditorText.T("Logs", "日志设置"),
                AIBridgeEditorText.T("Directories", "目录信息"),
                AIBridgeEditorText.T("Scripts", "脚本执行"),
                AIBridgeEditorText.T("Runtime", "Runtime"),
                AIBridgeEditorText.T("Code Index", "代码索引"),
                AIBridgeEditorText.T("Cache", "缓存清理"),
                AIBridgeEditorText.T("Actions", "操作")
            };
            _currentTab = (TabType)GUILayout.Toolbar((int)_currentTab, tabNames);
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField(AIBridgeEditorText.T("AI Bridge Settings", "AI Bridge 设置"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                AIBridgeEditorText.T(
                    "AI Bridge enables communication between AI assistants and Unity Editor.\nUse F12 to capture screenshots and F11 to record GIFs in Play mode.",
                    "AI Bridge 用于在 AI 助手和 Unity Editor 之间建立通信。\nPlay 模式下可用 F12 截图，F11 录制 GIF。"),
                MessageType.Info);

            if (GUILayout.Button(AIBridgeEditorText.T("Open Workflows", "打开 Workflows"), GUILayout.Height(24)))
            {
                AIBridgeWorkflowsWindow.OpenWindow();
            }
        }

        private void DrawBridgeSettings()
        {
            EditorGUILayout.LabelField(AIBridgeEditorText.T("Bridge Settings", "Bridge 设置"), EditorStyles.boldLabel);

            DrawLanguageSetting();

            EditorGUI.BeginChangeCheck();
            _bridgeEnabled = EditorGUILayout.Toggle(AIBridgeEditorText.T("Bridge Enabled", "启用 Bridge"), _bridgeEnabled);
            if (EditorGUI.EndChangeCheck())
            {
                AIBridge.Enabled = _bridgeEnabled;
            }

            EditorGUI.BeginChangeCheck();
            _debugLogging = EditorGUILayout.Toggle(AIBridgeEditorText.T("Debug Logging", "调试日志"), _debugLogging);
            if (EditorGUI.EndChangeCheck())
            {
                AIBridgeLogger.DebugEnabled = _debugLogging;
            }

            DrawCodeExecutionSetting();
        }

        private void DrawCodeExecutionSetting()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(AIBridgeEditorText.T("Experimental", "实验功能"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                AIBridgeEditorText.T(
                    "Code execution is enabled by default and lets AI/CLI run temporary C# inside Unity Editor. Disable it for untrusted projects or callers.",
                    "代码执行默认启用，允许 AI/CLI 在 Unity Editor 内运行临时代码。不可信项目或调用方环境中请关闭。"),
                MessageType.Warning);

            var settings = AIBridgeProjectSettings.Instance;
            EditorGUI.BeginChangeCheck();
            var enabled = EditorGUILayout.Toggle(AIBridgeEditorText.T("Enable Code Execution", "启用代码执行"), settings.EnableCodeExecution);
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            if (enabled)
            {
                var accepted = settings.CodeExecutionRiskAccepted || EditorUtility.DisplayDialog(
                    AIBridgeEditorText.T("Enable Code Execution", "启用代码执行"),
                    AIBridgeEditorText.T(
                        "After enabling, AI/CLI can execute temporary C# code inside Unity Editor. Use only in trusted projects and trusted caller environments.",
                        "开启后 AI/CLI 可在 Unity Editor 内执行临时代码，仅在可信项目和可信调用方环境中使用。"),
                    AIBridgeEditorText.T("Enable", "启用"),
                    AIBridgeEditorText.T("Cancel", "取消"));

                if (!accepted)
                {
                    Repaint();
                    return;
                }

                settings.CodeExecutionRiskAccepted = true;
            }

            settings.EnableCodeExecution = enabled;
            settings.SaveSettings();
        }

        private void DrawLanguageSetting()
        {
            var settings = AIBridgeProjectSettings.Instance;
            EditorGUI.BeginChangeCheck();
            var selectedIndex = EditorGUILayout.Popup(
                AIBridgeEditorText.T("Language", "语言"),
                AIBridgeEditorText.GetLanguageIndex(settings.EditorLanguage),
                AIBridgeEditorText.LanguageLabels);
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            var language = AIBridgeEditorText.LanguageValues[Mathf.Clamp(selectedIndex, 0, AIBridgeEditorText.LanguageValues.Length - 1)];
            if (settings.EditorLanguage == language && settings.EditorLanguageInitialized)
            {
                return;
            }

            settings.EditorLanguage = language;
            settings.EditorLanguageInitialized = true;
            settings.SaveSettings();
            Repaint();
        }

        private void DrawGifSettings()
        {
            EditorGUILayout.LabelField(AIBridgeEditorText.T("GIF Recording Settings (F11)", "GIF 录制设置 (F11)"), EditorStyles.boldLabel);

            _gifFrameCount = EditorGUILayout.IntSlider(AIBridgeEditorText.T("Frame Count", "帧数"), _gifFrameCount, 10, GifRecorder.MaxFrameCount);
            EditorGUILayout.LabelField(AIBridgeEditorText.T($"  Duration: {(float)_gifFrameCount / _gifFps:F1}s", $"  时长: {(float)_gifFrameCount / _gifFps:F1}s"), EditorStyles.miniLabel);

            _gifFps = EditorGUILayout.IntSlider("FPS", _gifFps, 10, 30);

            _gifScale = EditorGUILayout.Slider(AIBridgeEditorText.T("Scale", "缩放"), _gifScale, 0.25f, 1f);
            EditorGUILayout.LabelField(AIBridgeEditorText.T($"  Output: {(int)(1920 * _gifScale)}x{(int)(1080 * _gifScale)} (at 1080p)", $"  输出: {(int)(1920 * _gifScale)}x{(int)(1080 * _gifScale)} (按 1080p 估算)"), EditorStyles.miniLabel);

            _gifColorCount = EditorGUILayout.IntSlider(AIBridgeEditorText.T("Color Count", "颜色数量"), _gifColorCount, 64, 256);

            _gifStartDelay = EditorGUILayout.Slider(AIBridgeEditorText.T("Start Delay (s)", "开始延迟 (秒)"), _gifStartDelay, 0f, 5f);

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(AIBridgeEditorText.T("Save GIF Settings", "保存 GIF 设置")))
            {
                GifRecorderSettings.DefaultFrameCount = _gifFrameCount;
                GifRecorderSettings.DefaultFps = _gifFps;
                GifRecorderSettings.DefaultScale = _gifScale;
                GifRecorderSettings.DefaultColorCount = _gifColorCount;
                GifRecorderSettings.DefaultStartDelay = _gifStartDelay;
                Debug.Log(AIBridgeEditorText.T("[AIBridge] GIF settings saved.", "[AIBridge] GIF 设置已保存。"));
            }

            if (GUILayout.Button(AIBridgeEditorText.T("Reset to Defaults", "重置为默认值")))
            {
                GifRecorderSettings.ResetToDefaults();
                LoadSettings();
                Debug.Log(AIBridgeEditorText.T("[AIBridge] GIF settings reset to defaults.", "[AIBridge] GIF 设置已重置为默认值。"));
            }
            EditorGUILayout.EndHorizontal();

            if (GifRecorder.IsRecording)
            {
                EditorGUILayout.HelpBox(AIBridgeEditorText.T("GIF Recording in progress...", "正在录制 GIF..."), MessageType.Warning);
            }
        }

        private void DrawDirectoryInfo()
        {
            EditorGUILayout.LabelField(AIBridgeEditorText.T("Directory Information", "目录信息"), EditorStyles.boldLabel);
            DrawAIBridgeRootDirectorySettings();
            EditorGUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.TextField(AIBridgeEditorText.T("Bridge Directory", "Bridge 目录"), AIBridge.BridgeDirectory);
            if (GUILayout.Button(AIBridgeEditorText.T("Open", "打开"), GUILayout.Width(60)))
            {
                AIBridge.OpenBridgeDirectory();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.TextField(AIBridgeEditorText.T("Screenshots Directory", "截图目录"), ScreenshotHelper.ScreenshotsDir);
            if (GUILayout.Button(AIBridgeEditorText.T("Open", "打开"), GUILayout.Width(60)))
            {
                ScreenshotHelper.EnsureScreenshotsDirectory();
                EditorUtility.RevealInFinder(ScreenshotHelper.ScreenshotsDir);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAIBridgeRootDirectorySettings()
        {
            var settings = AIBridgeProjectSettings.Instance;
            var projectRoot = GetProjectRoot();

            EditorGUILayout.LabelField(AIBridgeEditorText.T("AIBridge Root Directory", "AIBridge 根目录"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                AIBridgeEditorText.T(
                    "Enable this only when the AIBridge package source is in a custom directory. If disabled, empty, or missing, AIBridge uses Packages/cn.lys.aibridge.",
                    "仅当 AIBridge 包源码位于自定义目录时启用。未启用、未配置或目录不存在时，AIBridge 使用 Packages/cn.lys.aibridge。"),
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            var useCustom = EditorGUILayout.Toggle(
                AIBridgeEditorText.T("Use Custom AIBridge Root", "使用自定义 AIBridge 根目录"),
                settings.UseCustomAIBridgeRootDirectory);
            if (EditorGUI.EndChangeCheck())
            {
                SetUseCustomAIBridgeRootDirectory(useCustom);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(!settings.UseCustomAIBridgeRootDirectory);
            EditorGUI.BeginChangeCheck();
            var customRoot = EditorGUILayout.DelayedTextField(
                AIBridgeEditorText.T("Custom Root", "自定义根目录"),
                settings.CustomAIBridgeRootDirectory);
            if (EditorGUI.EndChangeCheck())
            {
                SetCustomAIBridgeRootDirectory(customRoot);
            }
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button(AIBridgeEditorText.T("Browse", "浏览"), GUILayout.Width(64)))
            {
                BrowseCustomAIBridgeRootDirectory();
            }

            if (GUILayout.Button(AIBridgeEditorText.T("Open", "打开"), GUILayout.Width(52)))
            {
                OpenAIBridgeRootDirectory();
            }

            if (GUILayout.Button(AIBridgeEditorText.T("Reset", "重置"), GUILayout.Width(52)))
            {
                ResetCustomAIBridgeRootDirectory();
            }

            EditorGUILayout.EndHorizontal();

            var activeRoot = AIBridgeRootDirectoryUtility.GetAIBridgeRootDirectory(projectRoot);
            EditorGUILayout.TextField(AIBridgeEditorText.T("Active Root", "当前生效根目录"), AIBridgeRootDirectoryUtility.ToDisplayPath(projectRoot, activeRoot));

            if (settings.UseCustomAIBridgeRootDirectory && string.IsNullOrEmpty(settings.CustomAIBridgeRootDirectory))
            {
                EditorGUILayout.HelpBox(
                    AIBridgeEditorText.T(
                        "Custom root is enabled but no path is configured. The default package root is active.",
                        "已启用自定义根目录，但未配置路径。当前使用默认包根目录。"),
                    MessageType.Warning);
            }
            else if (settings.UseCustomAIBridgeRootDirectory
                     && !string.IsNullOrEmpty(settings.CustomAIBridgeRootDirectory)
                     && !Directory.Exists(AIBridgeRootDirectoryUtility.ResolveConfiguredDirectory(projectRoot, settings.CustomAIBridgeRootDirectory)))
            {
                EditorGUILayout.HelpBox(
                    AIBridgeEditorText.T(
                        "The configured custom root directory does not exist. The default package root is active.",
                        "配置的自定义根目录不存在。当前使用默认包根目录。"),
                    MessageType.Warning);
            }
        }

        private void SetUseCustomAIBridgeRootDirectory(bool useCustom)
        {
            var settings = AIBridgeProjectSettings.Instance;
            if (settings.UseCustomAIBridgeRootDirectory == useCustom)
            {
                return;
            }

            settings.UseCustomAIBridgeRootDirectory = useCustom;
            settings.SaveSettings();
            SkillInstaller.RefreshInstalledIntegrationsNoDialog();
            Repaint();
        }

        private void SetCustomAIBridgeRootDirectory(string directory)
        {
            var normalized = AIBridgeRootDirectoryUtility.NormalizeConfiguredDirectory(directory);
            if (!string.IsNullOrEmpty(normalized) && !AIBridgeRootDirectoryUtility.IsValidConfiguredDirectory(normalized))
            {
                EditorUtility.DisplayDialog(
                    AIBridgeEditorText.T("Invalid Directory", "无效目录"),
                    AIBridgeEditorText.T(
                        "The AIBridge root directory must be an absolute path or a project-relative path without '..'.",
                        "AIBridge 根目录必须是绝对路径，或不包含 '..' 的项目相对路径。"),
                    AIBridgeEditorText.T("OK", "确定"));
                return;
            }

            var settings = AIBridgeProjectSettings.Instance;
            if (settings.CustomAIBridgeRootDirectory == normalized)
            {
                settings.SaveSettings();
                SkillInstaller.RefreshInstalledIntegrationsNoDialog();
                Repaint();
                return;
            }

            settings.CustomAIBridgeRootDirectory = normalized;
            settings.SaveSettings();
            SkillInstaller.RefreshInstalledIntegrationsNoDialog();
            Repaint();
        }

        private void BrowseCustomAIBridgeRootDirectory()
        {
            var projectRoot = GetProjectRoot();
            var settings = AIBridgeProjectSettings.Instance;
            var currentDirectory = AIBridgeRootDirectoryUtility.GetResolvedAIBridgeRootDirectory(projectRoot);
            if (!string.IsNullOrEmpty(settings.CustomAIBridgeRootDirectory))
            {
                var configuredDirectory = AIBridgeRootDirectoryUtility.ResolveConfiguredDirectory(projectRoot, settings.CustomAIBridgeRootDirectory);
                if (Directory.Exists(configuredDirectory))
                {
                    currentDirectory = configuredDirectory;
                }
            }

            var selectedDirectory = EditorUtility.OpenFolderPanel(
                AIBridgeEditorText.T("Select AIBridge Root Directory", "选择 AIBridge 根目录"),
                currentDirectory,
                string.Empty);
            if (string.IsNullOrEmpty(selectedDirectory))
            {
                return;
            }

            AIBridgeProjectSettings.Instance.UseCustomAIBridgeRootDirectory = true;
            SetCustomAIBridgeRootDirectory(AIBridgeRootDirectoryUtility.ToProjectRelativeOrFullPath(projectRoot, selectedDirectory));
        }

        private void OpenAIBridgeRootDirectory()
        {
            var projectRoot = GetProjectRoot();
            var root = AIBridgeRootDirectoryUtility.GetResolvedAIBridgeRootDirectory(projectRoot);
            if (Directory.Exists(root))
            {
                EditorUtility.RevealInFinder(root);
                return;
            }

            EditorUtility.DisplayDialog(
                AIBridgeEditorText.T("Directory Not Found", "目录不存在"),
                AIBridgeEditorText.T(
                    "The active AIBridge root directory does not exist.",
                    "当前生效的 AIBridge 根目录不存在。"),
                AIBridgeEditorText.T("OK", "确定"));
        }

        private void ResetCustomAIBridgeRootDirectory()
        {
            var settings = AIBridgeProjectSettings.Instance;
            settings.UseCustomAIBridgeRootDirectory = AIBridgeProjectSettings.DefaultUseCustomAIBridgeRootDirectory;
            settings.CustomAIBridgeRootDirectory = AIBridgeProjectSettings.DefaultCustomAIBridgeRootDirectory;
            settings.SaveSettings();
            SkillInstaller.RefreshInstalledIntegrationsNoDialog();
            Repaint();
        }

        private void DrawActions()
        {
            EditorGUILayout.LabelField(AIBridgeEditorText.T("Actions", "操作"), EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(AIBridgeEditorText.T("Process Commands Now", "立即处理命令"), GUILayout.Height(30)))
            {
                AIBridge.ProcessCommandsNow();
                Debug.Log(AIBridgeEditorText.T("[AIBridge] Commands processed.", "[AIBridge] 命令已处理。"));
            }

            if (GUILayout.Button(AIBridgeEditorText.T("Clear Screenshot Cache", "清理截图缓存"), GUILayout.Height(30)))
            {
                ClearScreenshotCache();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField(AIBridgeEditorText.T("Hotkeys", "快捷键"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                AIBridgeEditorText.T(
                    "F12 - Capture Screenshot (Play mode only)\nF11 - Start/Stop GIF Recording (Play mode only)",
                    "F12 - 截图（仅 Play 模式）\nF11 - 开始/停止 GIF 录制（仅 Play 模式）"),
                MessageType.None);
        }

    }
}
