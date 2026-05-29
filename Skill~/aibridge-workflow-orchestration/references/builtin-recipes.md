# Built-In Recipe Drafts

Purpose: provide P0 recipe templates that agents can follow consistently today and that later CLI/schema work can formalize.

These drafts are not executable CLI specs.

## unity-change-implementation

Use for scoped Unity or AIBridge package changes.

- Phases: analyze -> implement serially -> compile -> check logs -> optional runtime or screenshot validation -> report.
- Roles: analyzer, implementer, validator.
- Outputs: `PlanItem`, `PatchProposal` when needed, `ValidationResult`, `ArtifactRef`.
- AIBridge gates: `compile unity`, `get_logs --logType Error`, targeted `test run`, optional screenshot or Runtime command.
- Avoid when the request is pure Q&A or no files/assets will change.

## unity-sharded-review

Use for broad code or asset review that can be split by directory, package, feature, or file list.

- Phases: shard -> parallel review -> barrier deduplicate/rank -> adversarial verify -> report.
- Roles: shard planner, shard reviewer, verifier, report writer.
- Outputs: `Finding`, `Verdict`, `ArtifactRef`.
- AIBridge gates: `rg`/file evidence, optional `code_index`, targeted compile/tests only for high-confidence risky findings.
- Avoid when the review scope is tiny enough for one pass.

## runtime-target-sweep

Use for validating multiple Player or Play Mode targets.

- Phases: discover targets -> parallel target status/log/screenshot/perf collection -> barrier compare -> report.
- Roles: target discoverer, target collector, comparer.
- Outputs: `RuntimeTargetRef`, `ValidationResult`, `ArtifactRef`.
- AIBridge gates: `runtime list_targets`, `runtime discover`, `runtime status`, `runtime logs`, `runtime screenshot`, `runtime perf`.
- Avoid when only one known target needs a direct command.

## runtime-ui-validation

Use for validating UI behavior through Play Mode or Runtime input and evidence.

- Phases: prepare target -> perform input actions -> collect logs/screenshots -> verdict -> report.
- Roles: action runner, visual/log validator.
- Outputs: `ValidationResult`, `ArtifactRef`, `Verdict`.
- AIBridge gates: `input click/drag/type` in Play Mode, `runtime call` for registered handlers, `screenshot game`, `runtime screenshot`, `get_logs`, `runtime logs`.
- Avoid when the UI state cannot be reached or the task only asks for static code review.

## prefab-asset-sweep

Use for broad Prefab or serialized asset inspection with controlled edits.

- Phases: parallel inspect -> barrier patch proposal review -> dry-run -> serial apply -> validate.
- Roles: asset inspector, patch proposer, implementer, validator.
- Outputs: `Finding`, `PatchProposal`, `ValidationResult`.
- AIBridge gates: `asset search`, `inspector get_components`, `inspector get_properties`, `prefab patch --dry-run`, `compile unity`, `get_logs --logType Error`.
- Avoid parallel writes to Prefabs, scenes, `.asset` files, or `.meta` files.

## bug-hunter-loop

Use for iterative evidence-first bug discovery when the initial failure is vague.

- Phases: collect evidence -> generate candidates -> adversarial verify -> implement one confirmed fix -> validate -> loop until dry or budget reached.
- Roles: evidence collector, candidate generator, verifier, implementer.
- Outputs: `Finding`, `Verdict`, `PatchProposal`, `ValidationResult`.
- AIBridge gates: logs, compile, tests, screenshots, Runtime status/logs/handlers/calls, optional Code Index.
- Avoid when the user requested a report-only review or when evidence is too weak to justify edits.
