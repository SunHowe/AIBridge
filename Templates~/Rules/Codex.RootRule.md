---
templateId: unity-integration
assistant: codex
version: 5
target: root-rule
---
## AIBridge Bootstrap

**CLI Alias**: `$CLI = {{CLI_PATH}}`

**{{COMMON_COMMANDS_TITLE}}**:
```bash
$CLI compile unity
$CLI get_logs --logType Error
$CLI editor log --message "Hello" --logType Warning
```

**{{ROUTING_TITLE}}**:
- {{QUICK_TASK_RULE}}
- {{DEVELOPMENT_TASK_RULE}}
- {{WORKFLOW_SKILL_RULE}}

**{{SKILL_INDEX_TITLE}}**:
{{SKILL_INDEX}}
