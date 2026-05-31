# 任务分流规则

## 目标

`aibridge-development-workflow` 是兼容入口，不直接假设所有任务都是实现任务。进入工作流后先选择一个主分支，再加载对应规范和检查清单。

## 分支选择

| 主分支 | 触发信号 | 默认目标 | 常用 Skills / 工具 |
|---|---|---|---|
| 实施分支 | 创建、修改、修复、重构、生成、迁移、提交 | 改动当前工作树并验证 | `aibridge`、`aibridge-code-index`、`aibridge-prefab-patch`、`unity-yaml-editing`、`aibridge-batch-script` |
| 调试诊断分支 | 排查、诊断、复现、为什么、追踪、日志、Runtime、Player、Play Mode、性能、UI 异常 | 收集证据并给出根因判断 | `aibridge`、`aibridge-code-index`、`aibridge-workflow-orchestration`、`aibridge-batch-script` |
| 审查分支 | review、audit、检查风险、设计评审、只读分析 | 输出 confirmed findings 和剩余风险 | `aibridge-code-index`、`rg`、按需 `aibridge-workflow-orchestration` |
| 验证分支 | 编译、日志、截图、测试、Runtime/UI 验证、回归确认 | 给出可重复验证结果 | `aibridge`、现有 workflow recipe |
| 编排分支 | workflow recipe、多 Agent、并行 sweep、对抗验证、结构化 artifact | 设计或执行结构化 workflow | `aibridge-workflow-orchestration` |

## 交接规则

- 调试诊断分支发现 confirmed 根因且用户要求修复时，交接到实施分支；交接内容必须包含症状、证据、候选根因状态和建议修改范围。
- 实施分支完成改动后，按风险选择验证分支补充 Runtime、截图、UI 或多目标证据。
- 审查分支发现问题后，未得到修复授权前不直接改文件。
- 编排分支只定义流程、角色、artifact 和 gate；具体 Unity 对象修改仍由实施分支串行完成。

## 输出格式

```text
【任务分流模式】
主分支：调试诊断分支
辅助分支：编排分支（需要 Runtime 多目标 sweep 时）
理由：用户目标是排查运行时异常，当前验收是证据和根因结论，不是立即修改代码。
```
