# Unity CLI workflow

- The live Unity project is located at `D:\Programy\UnityProjects\RageQuitting`. Run Unity CLI commands from that directory or pass `--project-path` explicitly. The workspace at `C:\Users\dunia\Documents\RageQuitting` contains the working documentation and is not the live Unity project.
- Unity CLI is installed at `C:\Users\dunia\AppData\Local\Unity\bin\unity.exe` (verified version `1.0.0-beta.6`). If `unity` is not visible in the current process `PATH`, invoke this executable by its absolute path instead of reinstalling it.
- Before changing scenes, prefabs, or assets, check the live Editor with:
  - `& "$env:LOCALAPPDATA\Unity\bin\unity.exe" status --format json`
  - `& "$env:LOCALAPPDATA\Unity\bin\unity.exe" command --project-path "D:\Programy\UnityProjects\RageQuitting" --format json`
- The Editor uses `com.unity.pipeline` and writes its authenticated descriptor to `D:\Programy\UnityProjects\RageQuitting\Library\Pipeline\.unity-pipeline-port`. The port is selected dynamically; the last verified port was `7801`, with Editor state `ready`.
- Do not connect to the Pipeline port with unauthenticated HTTP. Unity CLI reads the descriptor and token automatically.
- In the Codex sandbox, reading the protected Pipeline descriptor may return access denied and make `pipeline list` report `isReachable: false` even though the TCP port is open. In that case, rerun the Unity CLI command with escalated sandbox permission. The approved executable prefix should be `C:\Users\dunia\AppData\Local\Unity\bin\unity.exe`.
- Use the connected Editor for prefab, scene, and asset edits. Do not hand-edit Unity YAML while the live Editor is reachable.

# Research and planning

- When planning gameplay mechanics, actively research recommended implementations of comparable systems on the internet.
- Prefer current primary sources: official documentation, vendor examples, technical material from library authors, and source code.
- Critically verify every assumption. Clearly distinguish confirmed facts, inferences, design preferences, and unverified hypotheses.
- Ask about every ambiguity or tradeoff that could materially change a mechanic's behavior.
- Leave all key design decisions to the user, including decisions where even a small but meaningful uncertainty remains.

# Model configuration

- Use `gpt-6.1-sol` as the main agent model, with `xhigh` reasoning for research, planning, and final code review.
- Before starting one of these phases, check the current task's model and reasoning setting when that information is available.
- If the main agent cannot change an incorrect model or reasoning setting, ask the user to change it before continuing that phase.
- Never claim that a model or reasoning setting was changed without confirmation.

# Implementation handoff

- The main agent may automatically delegate implementation and corrections through subagent tools.
- Use `spawn_agent`, never `create_thread` or messages to a separate user task, for delegated subtasks.
- Exactly one subagent may run at a time. Every `spawn_agent` call must default to `fork_context: false`; never pass the full conversation history. Use `fork_context: true` only after explicit user approval for that task and after explaining that full context can significantly increase token usage.
- Before delegating, assess complexity, coupling, risk, and validation needs. Prepare a short, self-contained prompt containing only the needed context: the concrete goal; user decisions; exact writable file scope; project instructions; acceptance criteria; required tests and validation; project paths, Unity CLI details, and relevant Editor state; and a warning to preserve unrelated worktree changes.
- Point to local paths for large files, logs, and reports and instruct the subagent to read only needed fragments. Do not pass unrelated discussion history, old plans, full logs, or unrelated test results. The subagent should first read the indicated instructions and source files, then discover missing context in the repository.
- Before preparing the implementation prompt, assess the work's complexity, coupling, risk, and validation needs, then choose `gpt-6-luna` with `low` or `medium` reasoning accordingly.
- Use `low` reasoning for narrow, mechanical, well-specified changes with limited risk. Use `medium` reasoning for multi-file, architectural, networking, lifecycle-sensitive, or otherwise non-trivial changes.
- Split code implementation into the smallest sensible, independently reviewable and validatable batches. Complete and verify each batch before moving to the next when the change can be safely staged, so a failure or rejected patch does not invalidate the entire implementation.
- Every implementation prompt must include the approved plan and user decisions, the exact project path and write scope, all applicable project instructions, acceptance criteria, required tests, Unity CLI and connected Editor context, dirty-worktree precautions, and a requirement to report every changed file, validation result, failure, and unresolved risk.
- The separate implementation agent must edit the code directly and run the initial validation. The main agent must not duplicate that implementation in parallel.
- The subagent must edit files directly, perform initial validation, and return only: changed files; required test and validation results; key report paths; errors and deviations; and remaining risks or unperformed checks.
- The main agent must independently inspect the actual diff, integration, and reports rather than accepting the subagent's report at face value. It may perform non-colliding analysis, but must not implement the same scope in parallel.
- Send review corrections to the same subagent via `send_input`; do not create a replacement or separate review subagent. Keep the subagent alive until implementation, corrections, and validation are accepted.
- Repeat the `xhigh` review after corrections. Consider the handoff complete only after the implementation and validation are accepted.
- If subagent tools are unavailable, inform the user and do not implement locally without a new explicit decision.

# Documentation handoff

- For every project documentation entry or update, automatically delegate to one subagent via `spawn_agent` with `gpt-6-luna`, `low` reasoning, and `fork_context: false`, using the same minimal-context prompt and one-subagent rules above.
- Project documentation must always be updated in both the working documentation at `C:\Users\dunia\Documents\RageQuitting` and the Unity project documentation at `D:\Programy\UnityProjects\RageQuitting\Documentation`; update the matching file in both locations during the same task and verify that the resulting document contents match.
- The documentation prompt must name both paths, identify the exact documents and requested content, require content parity verification, and require the limited subagent report described above.
- This documentation rule does not apply to code implementation or corrections, which use the separate implementation-agent handoff with `low` or `medium` reasoning selected from the assessed complexity.

# Preflight validation workflow

- Use the `Fast` tier for documentation, local tools, Editor scripts, and narrow changes that do not affect runtime behavior.
- Use the `Gameplay` tier for runtime C#, multiplayer, physics, input, scenes, prefabs, ScriptableObjects, and gameplay assets.
- Use `Auto` only when the worktree is clean or every current change belongs to one task.
- The main agent must select the exact tier and final command in the implementation prompt. The implementation agent must not lower that tier.
- During iteration, targeted runs may use `EditModeFilter` and `PlayModeFilter`.
- After the final correction, run one unfiltered strict `Fast` or `Gameplay` preflight, as selected for the task.
- `AllowLowerTier` is for diagnostics only. A result that skips required checks cannot support acceptance.
- For work that requires `Full`, run strict `Gameplay`, select the available targeted tests, and explicitly report the missing `Full` checks.
- Use `KeepArtifacts` for important reproductions, expected failures, or diagnostics. Use the default retention policy for normal runs.
- Visual validation belongs to the user and is not a preflight step.
- Run preflight synchronously in the foreground.
- Wait with one long bounded operation and limited output instead of frequently polling the process.
- `Fast` normally takes 16–24 seconds; allow a 30-second budget.
- `Gameplay` normally takes 75–90 seconds.
- After 180 seconds, check progress once without starting a second preflight.
- If the process is making progress, wait for the runner timeout.
- Treat a timeout or lack of progress as a real failure or blocked state; preserve and report the diagnostic evidence.
- A normal run does not require the user to notify or wake the agent.
- `Invoke-UnityPreflight.ps1` does not support an `ArtifactDirectory` parameter.
- The agent report must include the status, exit code, `summary.json` path, every step whose status is not `passed`, and all limitations.

Run the final command from `D:\Programy\UnityProjects\RageQuitting`:

```powershell
.\Tools\AgentHarness\Invoke-UnityPreflight.ps1 -Tier Fast -Json
.\Tools\AgentHarness\Invoke-UnityPreflight.ps1 -Tier Gameplay -Json
```

# Code review

- Perform a detailed, critical review after every code implementation.
- Prioritize defects, regressions, multiplayer synchronization, lifecycle behavior, edge cases, and missing tests.
- Do not consider code correct solely because the separate implementation agent reports success.
- After corrections, review the entire changed area again rather than only the identified lines.
- Clearly report all residual risks and any validation that could not be completed.

# Transparency and continuous improvement

- Explicitly report errors, failed actions, deviations from the plan, and incomplete validation.
- Describe the impact of a problem and the actual current state of the work. Never present partial success as complete success.
- Use tokens efficiently throughout the task, especially when preparing implementation handoffs: keep prompts, context, tool output, and repeated analysis focused on what is needed for the current batch. Quality, correctness, and adequate validation always take priority over token savings.
- When user guidance repeats, propose adding a corresponding reusable instruction to this file.
- When a useful workflow, project improvement pattern, new tool, integration, or available capability is noticed, proactively propose it to the user and explain its practical benefit.
- Never modify this file without explicit user permission. Treat permission as limited to the changes the user authorized.
- Summarize every modification to this file in the dated `Changelog` section at the end of the file.

# Changelog

## 2026-08-27

- Added research and critical planning requirements.
- Added mandatory user ownership of key design decisions.
- Added GPT-5.6 Sol model and reasoning requirements.
- Added single-worker implementation and correction workflow.
- Added mandatory independent post-implementation review.
- Added transparency and process-improvement rules.
- Added explicit permission and changelog requirements for AGENTS.md changes.
- Added a single-agent `gpt-5.6-sol` low-reasoning rule for project documentation entries and updates.
- Added a dual-location synchronization and content-verification rule for project documentation.

## 2026-08-31

- Added complexity-based selection between `low` and `medium` reasoning for implementation agents.
- Added a requirement to split code changes into the smallest sensible reviewable and validatable batches.
- Added token-efficiency guidance with quality and correctness kept as higher priorities.
- Expanded continuous-improvement guidance to explicitly cover new tools, integrations, and available capabilities.

## 2026-09-05

- Replaced user-mediated handoff prompts with automatic delegation to one minimal-context subagent.
- Made `fork_context: false` the mandatory default and restricted full context to explicit user approval.
- Required prompts to carry only the needed scope, decisions, instructions, acceptance criteria, validation, paths, Editor state, and worktree warning.
- Preserved one-subagent execution and routed corrections back to the same subagent.
- Added the policy for selecting `Fast`, `Gameplay`, and restricted `Auto` preflight tiers.
- Added the requirement for one final unfiltered strict preflight after the last correction.
- Added synchronous waiting, expected-duration, progress-check, and timeout-reporting rules for preflight runs.

## 2026-09-23

- Switched the main agent to `gpt-6-sol`, retaining `xhigh` reasoning for research, planning, and final code review.
- Switched implementation and correction subagents to `gpt-6-luna` with complexity-based `low` or `medium` reasoning.
- Switched documentation subagents to `gpt-6-luna` with `low` reasoning.

## 2026-10-25

- Switched the main agent model to gpt-6.1-sol, retaining xhigh reasoning for research, planning, and final code review.
