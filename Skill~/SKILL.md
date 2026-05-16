---
name: aibridge
description: Unity Editor CLI integration for AIBridge. Use when Codex needs to compile Unity, inspect Console logs, search/read assets, manipulate GameObjects, Transforms, components, SerializedProperty values, scenes or prefabs, capture screenshots/GIFs, control Editor focus/menu items/game view, or run batched Unity automation through AIBridgeCLI.
---

# AI Bridge Unity Skill

## Invocation

Run commands from the Unity project root.

**CLI Path:** `./AIBridgeCache/CLI/AIBridgeCLI.exe`

**Example Alias:** `$CLI` is a shorthand for the platform-appropriate AIBridge CLI invocation. Examples use `$CLI` for consistency; replace it with the matching invocation form when running commands directly.

**Invocation Forms:**

- Windows executable: `./AIBridgeCache/CLI/AIBridgeCLI.exe <command> [action] [options]`
- macOS/Linux DLL: `dotnet ./AIBridgeCache/CLI/AIBridgeCLI.dll <command> [action] [options]`
- PowerShell call operator: `& "./AIBridgeCache/CLI/AIBridgeCLI.exe" <command> [action] [options]`

Most Unity-side commands require an `action` such as `asset search` or `inspector set_property`. CLI-only commands and helpers can differ: `focus` has no action, while `multi` uses `--cmd` or `--stdin`. Use `--help` or the generated command reference for the exact form.

**Global Options:**

- `--timeout <ms>` - Timeout (default: 5000)
- `--raw` / `--pretty` - JSON output (default: raw)
- `--json <json>` / `--stdin` - Complex parameters
- `--help` - Show help

**Cache Directory:** `AIBridgeCache/` (commands, results, screenshots)

## Operating Rules

- Use `compile unity` for Unity validation. `compile dotnet` is an explicit extra solution-build check, not a fallback.
- For Unity assets, prefer `asset search/find --format paths`; use host file reads for file contents, and `asset read_text` only when host reads are unavailable.
- For serialized edits, discover targets with `inspector get_components/get_properties/find_property`, then write with `inspector set_property/set_properties`; avoid raw YAML unless no Unity API path exists.
- For prefab asset edits, use `assetPath + objectPath + componentName` or `componentIndex`; `componentInstanceId` is scene-only.
- For complex prefab asset edits, use the `aibridge-prefab-patch` skill and prefer `prefab patch --ops <file>` with dry-run first.
- In PowerShell, avoid inline complex `--json`; build JSON in a variable, escape embedded quotes for native EXE argument passing, and pass command parameters directly, especially `inspector set_properties --values $values`.
- `focus` is Windows CLI-only, `screenshot` requires Play Mode, and `multi` is preferred for batch dispatch.
- `multi --cmd` accepts plain CLI commands separated by `&` and automatically emits Batch `call` lines; `call`, `delay`, `log`, `menu`, and `#` comment lines are kept as native Batch script.

## Related Resources

- `aibridge-prefab-patch`: specialized Skill for complex prefab asset edits.
- Generated command reference below: exact command syntax.

## Essential Commands

### `focus` - Bring Unity to Foreground

CLI-only, Windows-only. Triggers Unity refresh/compile via Windows API.

```bash
$CLI focus
# Output: {"Success":true,"ProcessId":1234,"WindowTitle":"MyProject - Unity 6000.0.51f1","Error":null}
```

### `multi` - Execute Multiple Commands (Recommended)

```bash
$CLI multi --cmd 'editor log --message Step1&gameobject create --name Cube --primitiveType Cube'
$CLI multi --stdin
```

<!-- AIBRIDGE:COMMANDS -->

---

**Skill Version**: 1.0
**Package**: cn.lys.aibridge
