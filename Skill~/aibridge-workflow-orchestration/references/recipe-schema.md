# Recipe Schema

Purpose: define the P0 documentation convention for AIBridge workflow recipes. P0 recipes are not executed or validated by the AIBridge CLI.

## File Locations

Use these locations for recipe documents when a project adopts them:

```text
.aibridge/workflows/<name>.aibridge-workflow.json
Templates~/Workflows/<name>.aibridge-workflow.json
```

Project-local recipes live under `.aibridge/workflows/`. Package templates live under `Templates~/Workflows/`.

## Top-Level Shape

```json
{
  "schemaVersion": 1,
  "name": "unity-sharded-review",
  "description": "Review Unity project shards and adversarially verify findings.",
  "inputs": {},
  "phases": [],
  "gates": [],
  "artifacts": []
}
```

Required fields:

- `schemaVersion`: integer schema version. Use `1` for P0 documents.
- `name`: stable lower-case recipe id.
- `description`: one sentence explaining the workflow.
- `inputs`: named input values or selectors.
- `phases`: ordered phase definitions.
- `gates`: validation gates that must be checked.
- `artifacts`: expected artifact outputs or artifact directories.

## Phase Shape

```json
{
  "id": "parallel-review",
  "type": "agent",
  "description": "Review one source shard.",
  "dependsOn": ["shard"],
  "parallel": true,
  "role": "reviewer",
  "inputs": {},
  "outputs": ["Finding"]
}
```

Recommended fields:

- `id`: unique phase id.
- `type`: `agent`, `cli`, `manual`, `barrier`, or `report`.
- `description`: concise task statement.
- `dependsOn`: upstream phase ids.
- `parallel`: whether independent items may run in parallel.
- `role`: agent or human role name.
- `inputs`: phase-local inputs.
- `outputs`: schema names produced by the phase.

## Step Types

- `agent`: AI subtask that reads evidence, reasons, drafts findings, proposes patches, or writes reports.
- `cli`: AIBridge CLI command or deterministic command sequence.
- `manual`: user or main-agent decision.
- `barrier`: waits for all upstream outputs and merges, deduplicates, ranks, or votes.
- `report`: produces final Markdown or JSON for humans or automation.

## Standard Schema Names

- `Finding`: issue, risk, or observation backed by evidence.
- `Verdict`: result of verifying a claim.
- `PlanItem`: ordered implementation or validation step.
- `PatchProposal`: proposed file changes before serial application.
- `ValidationResult`: result of a compile, test, log, screenshot, Runtime, or semantic gate.
- `ArtifactRef`: reference to a screenshot, log, JSON, patch, report, or other saved output.
- `RuntimeTargetRef`: Runtime target identity and connection evidence.

## ArtifactRef

```json
{
  "kind": "screenshot",
  "path": ".aibridge/workflows/runs/run-id/artifacts/game.png",
  "summary": "Inventory panel after click.",
  "sourceCommand": "screenshot game"
}
```

Rules:

- Use stable relative paths when artifacts live inside the project.
- Include the source command or source phase.
- Summarize why the artifact matters.
- Do not paste large logs or binary data into recipe fields.

## RuntimeTargetRef

```json
{
  "targetId": "AIBridgeDev_12345",
  "url": "http://127.0.0.1:27182",
  "platform": "WindowsPlayer",
  "status": "reachable",
  "evidence": "runtime status --target AIBridgeDev_12345"
}
```

Use a target id or URL in every Runtime sweep result so later phases can replay or diagnose the same target.

## P0 Compatibility Rules

- Do not add a `workflow` command to examples.
- Do not imply that AIBridge reads these JSON files in P0.
- Do not require a cross-tool agent runtime.
- Keep recipes portable across Codex, Claude, Cursor, and other assistants by using roles, phases, schemas, and artifact references instead of vendor-specific APIs.
