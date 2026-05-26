using System;
using System.IO;
using System.Linq;
using AIBridge.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIBridge.Editor
{
    public partial class AIBridgeSettingsWindow
    {
        private void DrawRuntimeBridgeSettingsTab()
        {
            var settings = AIBridgeProjectSettings.Instance.RuntimeBridge;

            EditorGUILayout.LabelField(AIBridgeEditorText.T("Runtime Bridge", "Runtime Bridge"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                AIBridgeEditorText.T(
                    "Runtime Bridge lets AIBridgeCLI connect to AIBridgeRuntime inside Play Mode or a built Player. Release builds remain disabled unless explicitly allowed.",
                    "Runtime Bridge 允许 AIBridgeCLI 连接 Play Mode 或已编译 Player 内的 AIBridgeRuntime。Release Build 默认关闭，除非显式允许。"),
                MessageType.Info);

            EditorGUI.BeginChangeCheck();

            settings.EnableRuntimeBridge = EditorGUILayout.Toggle(
                AIBridgeEditorText.T("Enable Runtime Bridge", "启用 Runtime Bridge"),
                settings.EnableRuntimeBridge);

            settings.AutoInjectRuntimeBridgeInDevelopmentBuild = EditorGUILayout.Toggle(
                AIBridgeEditorText.T("Auto Inject In Development Build", "Development Build 自动注入"),
                settings.AutoInjectRuntimeBridgeInDevelopmentBuild);

            settings.AllowRuntimeBridgeInReleaseBuild = EditorGUILayout.Toggle(
                AIBridgeEditorText.T("Allow Runtime Bridge In Release Build", "允许 Release Build 启用 Runtime Bridge"),
                settings.AllowRuntimeBridgeInReleaseBuild);

            if (settings.AllowRuntimeBridgeInReleaseBuild)
            {
                EditorGUILayout.HelpBox(
                    AIBridgeEditorText.T(
                        "Release Build Runtime Bridge is a debugging backdoor. Use an auth token and restrict Allowed Actions before shipping.",
                        "Release Build Runtime Bridge 是调试入口。发布前请设置鉴权 Token 并限制 Allowed Actions。"),
                    MessageType.Warning);
            }

            settings.ExchangeDirectory = EditorGUILayout.DelayedTextField(
                AIBridgeEditorText.T("Runtime Directory", "Runtime 目录"),
                settings.ExchangeDirectory ?? string.Empty);

            settings.TargetId = EditorGUILayout.DelayedTextField(
                AIBridgeEditorText.T("Default Target Id", "默认 Target Id"),
                settings.TargetId ?? string.Empty);

            settings.AuthToken = EditorGUILayout.DelayedTextField(
                AIBridgeEditorText.T("Auth Token", "鉴权 Token"),
                settings.AuthToken ?? string.Empty);

            settings.AllowedActions = EditorGUILayout.DelayedTextField(
                AIBridgeEditorText.T("Allowed Actions", "允许的 Actions"),
                settings.AllowedActions ?? string.Empty);

            settings.HeartbeatIntervalSeconds = EditorGUILayout.Slider(
                AIBridgeEditorText.T("Heartbeat Interval", "Heartbeat 间隔"),
                settings.HeartbeatIntervalSeconds,
                0.1f,
                10f);

            settings.LogBufferSize = EditorGUILayout.IntSlider(
                AIBridgeEditorText.T("Log Buffer Size", "日志缓存数量"),
                settings.LogBufferSize,
                50,
                5000);

            settings.MaxResultBytes = EditorGUILayout.IntField(
                AIBridgeEditorText.T("Max Result Bytes", "最大结果字节数"),
                settings.MaxResultBytes);

            if (EditorGUI.EndChangeCheck())
            {
                settings.MaxResultBytes = Math.Max(1024, settings.MaxResultBytes);
                AIBridgeProjectSettings.Instance.SaveSettings();
                AIBridgeRuntimeBuildProcessor.SyncRuntimeBootstrapDefinesForActiveTarget();
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(AIBridgeEditorText.T("Resolved Runtime Directory", "解析后的 Runtime 目录"), EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(
                AIBridgeRuntimeBridgeEditorUtility.GetRuntimeDirectory(),
                EditorStyles.wordWrappedMiniLabel,
                GUILayout.Height(34));

            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(AIBridgeEditorText.T("Create Runtime Object", "创建 Runtime 对象"), GUILayout.Height(28)))
            {
                CreateOrSelectRuntimeObject();
            }

            if (GUILayout.Button(AIBridgeEditorText.T("Apply To Scene Runtime", "应用到场景 Runtime"), GUILayout.Height(28)))
            {
                ApplySettingsToSceneRuntimes(showDialog: true);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(AIBridgeEditorText.T("Open Players Panel", "打开 Players 面板"), GUILayout.Height(24)))
            {
                AIBridgePlayersWindow.OpenWindow();
            }

            if (GUILayout.Button(AIBridgeEditorText.T("Open Runtime Directory", "打开 Runtime 目录"), GUILayout.Height(24)))
            {
                OpenRuntimeDirectory();
            }

            if (GUILayout.Button(AIBridgeEditorText.T("Copy Launch Args", "复制启动参数"), GUILayout.Height(24)))
            {
                CopyLaunchArguments();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                AIBridgeEditorText.T(
                    "Allowed Actions accepts comma, semicolon, or newline separated runtime handler action names. Empty means custom actions are allowed in Editor/Development Build and blocked in Release Build.",
                    "Allowed Actions 支持用逗号、分号或换行分隔 Runtime handler action。为空时 Editor/Development Build 允许自定义 action，Release Build 阻止自定义 action。"),
                MessageType.None);
        }

        private static void CreateOrSelectRuntimeObject()
        {
            var runtime = FindSceneRuntime();
            if (runtime == null)
            {
                var gameObject = new GameObject("AIBridgeRuntime");
                Undo.RegisterCreatedObjectUndo(gameObject, "Create AIBridgeRuntime");
                runtime = gameObject.AddComponent<AIBridgeRuntime>();
            }

            ApplySettingsToRuntime(runtime);
            Selection.activeGameObject = runtime.gameObject;
            EditorGUIUtility.PingObject(runtime.gameObject);
            EditorSceneManager.MarkSceneDirty(runtime.gameObject.scene);
        }

        private static void ApplySettingsToSceneRuntimes(bool showDialog)
        {
            var runtimes = Resources.FindObjectsOfTypeAll<AIBridgeRuntime>()
                .Where(runtime => runtime != null
                    && runtime.gameObject != null
                    && runtime.gameObject.scene.IsValid()
                    && !EditorUtility.IsPersistent(runtime))
                .ToArray();

            for (var i = 0; i < runtimes.Length; i++)
            {
                ApplySettingsToRuntime(runtimes[i]);
                EditorUtility.SetDirty(runtimes[i]);
                EditorSceneManager.MarkSceneDirty(runtimes[i].gameObject.scene);
            }

            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "AIBridge",
                    AIBridgeEditorText.T(
                        $"Applied Runtime Bridge settings to {runtimes.Length} scene runtime object(s).",
                        $"已将 Runtime Bridge 设置应用到 {runtimes.Length} 个场景 Runtime 对象。"),
                    AIBridgeEditorText.T("OK", "确定"));
            }
        }

        private static AIBridgeRuntime FindSceneRuntime()
        {
            return Resources.FindObjectsOfTypeAll<AIBridgeRuntime>()
                .FirstOrDefault(runtime => runtime != null
                    && runtime.gameObject != null
                    && runtime.gameObject.scene.IsValid()
                    && !EditorUtility.IsPersistent(runtime));
        }

        private static void ApplySettingsToRuntime(AIBridgeRuntime runtime)
        {
            if (runtime == null)
            {
                return;
            }

            var source = AIBridgeProjectSettings.Instance.RuntimeBridge;
            if (runtime.runtimeSettings == null)
            {
                runtime.runtimeSettings = new AIBridgeRuntimeSettings();
            }

            runtime.runtimeSettings.enableRuntimeBridge = source.EnableRuntimeBridge;
            runtime.runtimeSettings.allowInReleaseBuild = source.AllowInReleaseBuild;
            runtime.runtimeSettings.exchangeDirectory = source.ExchangeDirectory ?? string.Empty;
            runtime.runtimeSettings.targetId = source.TargetId ?? string.Empty;
            runtime.runtimeSettings.authToken = source.AuthToken ?? string.Empty;
            runtime.runtimeSettings.allowedActions = ParseAllowedActions(source.AllowedActions);
            runtime.runtimeSettings.heartbeatIntervalSeconds = source.HeartbeatIntervalSeconds;
            runtime.runtimeSettings.logBufferSize = Math.Max(1, source.LogBufferSize);
            runtime.runtimeSettings.maxResultBytes = Math.Max(1024, source.MaxResultBytes);
            EditorUtility.SetDirty(runtime);
        }

        private static string[] ParseAllowedActions(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new string[0];
            }

            return value
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(action => action.Trim())
                .Where(action => !string.IsNullOrEmpty(action))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void OpenRuntimeDirectory()
        {
            var path = AIBridgeRuntimeBridgeEditorUtility.GetRuntimeDirectory();
            Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path);
        }

        private static void CopyLaunchArguments()
        {
            var settings = AIBridgeProjectSettings.Instance.RuntimeBridge;
            var runtimeDirectory = AIBridgeRuntimeBridgeEditorUtility.GetRuntimeDirectory();
            var targetId = string.IsNullOrWhiteSpace(settings.TargetId) ? "player1" : settings.TargetId.Trim();
            EditorGUIUtility.systemCopyBuffer =
                "--aibridge-runtime-dir " + AIBridgeRuntimeBridgeEditorUtility.Quote(runtimeDirectory)
                + " --aibridge-target-id " + AIBridgeRuntimeBridgeEditorUtility.Quote(targetId);
            Debug.Log(AIBridgeEditorText.T("[AIBridge] Runtime launch arguments copied.", "[AIBridge] Runtime 启动参数已复制。"));
        }
    }
}
