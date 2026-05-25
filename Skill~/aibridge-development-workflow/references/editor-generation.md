# Editor 生成脚本规范

## 适用场景

复杂 Prefab、复杂场景、3D 特效资源、批量材质/网格/粒子系统等“首次生成”任务，可以优先使用 `.aibridge/code/*.csx` 一次性 Editor 脚本，再通过 `code execute` 执行。

不适用：

- 生成正式业务逻辑脚本并直接挂载。需要新 MonoBehaviour/ScriptableObject 类型时，先写入 `Assets/*.cs`，执行 `compile unity`，再添加组件或创建资产。
- 能用 `inspector`、`prefab patch --dryRun true`、`scene` 等正式命令清晰表达的低风险修改。
- 需要直接编辑 Prefab/Scene YAML 的结构修改；此类必须加载 `unity-yaml-editing`。

## 必须规则

1. 脚本文件放在 `.aibridge/code/<task-name>.csx`，长脚本不要用 `--code` 内联。
2. 输出资源集中到 `Assets/AIBridgeGenerated/<TaskName>/` 或用户指定目录。
3. 路径、资源名、尺寸、数量、颜色、Prefab 名等稳定参数定义为常量。
4. 脚本必须幂等：重复执行不会无限创建副本；必要时清理旧输出或复用已有资源。
5. 复杂对象结构使用 Unity Editor API 生成，不直接写 UnityYAML。
6. 优先使用 `AIBridgeGeneration` helper；仅在 helper 不覆盖时直接调用 `AssetDatabase`、`PrefabUtility`、`EditorSceneManager`。
7. 生成后返回结构化结果，至少包含创建/更新的 asset、prefab、scene、warning。
8. 执行后必须运行 `compile unity`，并检查 `get_logs --logType Error`。

## 推荐脚本模板

```csharp
const string TaskName = "MyEffect";
const string OutputRoot = "Assets/AIBridgeGenerated/" + TaskName;
const string PrefabPath = OutputRoot + "/" + TaskName + ".prefab";

var result = new AIBridgeGenerationResult();

AIBridgeGeneration.EnsureFolder(OutputRoot);
AIBridgeGeneration.DeleteAssetIfExists(PrefabPath);

var root = new GameObject(TaskName);
try
{
    var material = AIBridgeGeneration.LoadOrCreateMaterial(
        OutputRoot + "/Glow.mat",
        "Standard",
        result);
    material.color = Color.cyan;

    var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
    body.name = "Core";
    body.transform.SetParent(root.transform, false);
    body.GetComponent<Renderer>().sharedMaterial = material;

    AIBridgeGeneration.SavePrefab(root, PrefabPath, result);
    AIBridgeGeneration.RefreshAssets();
}
finally
{
    UnityEngine.Object.DestroyImmediate(root);
}

return result;
```

## Helper API

优先使用这些 Editor helper，减少重复样板和路径错误：

```csharp
AIBridgeGeneration.EnsureFolder("Assets/AIBridgeGenerated/MyTask");
AIBridgeGeneration.DeleteAssetIfExists("Assets/AIBridgeGenerated/MyTask/Old.prefab");
AIBridgeGeneration.LoadOrCreateMaterial("Assets/AIBridgeGenerated/MyTask/Mat.mat", "Standard", result);
AIBridgeGeneration.AddOrGetComponent<BoxCollider>(gameObject);
AIBridgeGeneration.SavePrefab(root, "Assets/AIBridgeGenerated/MyTask/Root.prefab", result);
AIBridgeGeneration.SaveScene(scene, "Assets/AIBridgeGenerated/MyTask/Scene.unity", result);
AIBridgeGeneration.MarkDirty(asset);
AIBridgeGeneration.RefreshAssets();
```

## 执行流程

```powershell
$CLI code execute --file ".aibridge/code/my_effect.csx" --allow-experimental true --timeout 30000
$CLI compile unity --timeout 120000
$CLI get_logs --logType Error --count 20
```

## 结果要求

脚本返回 `AIBridgeGenerationResult` 或等价结构：

- `assets`：创建或更新的普通资源路径。
- `prefabs`：创建或更新的 Prefab 路径。
- `scenes`：创建或更新的 Scene 路径。
- `warnings`：非阻断警告。
- `messages`：关键信息。
