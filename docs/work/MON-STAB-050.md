# MON-STAB-050 — Chrome/CDP Stability Hardening

## Status
Implementation complete on `agent/monitor-stability-50-clean`; GitHub Actions validation is required before merge to `main`.

## Why this batch exists
A production monitor log exposed Chrome DevTools protocol error `-32000` with `Cannot find default execution context` during navigation/reload. PR #230 fixed that exact variant and was merged after all eight established PR workflows passed. This follow-up batch broadens the lifecycle/transport contract so adjacent Chrome/WebSocket teardown variants remain recoverable without weakening application-error diagnostics.

## Implementation
- Added `ChromeTransportFailureClassifier` as the canonical target-lifecycle and transport-message catalog.
- `ChromeDevToolsSessionPool.DevToolsSession.IsTargetLifecycleError` remains as a compatibility wrapper for existing tests, but now delegates to the shared classifier instead of owning a second message list.
- The classifier walks nested and aggregate exception trees.
- It recognizes WebSocket, socket, I/O, HTTP endpoint, timeout, disposed transport and task-cancelled boundary failures.
- It separately classifies expected `Browser.close` disconnect semantics so browser shutdown does not produce misleading exception diagnostics.
- Invalid CDP parameters, arbitrary JavaScript errors and ChatGPT UI error prose remain outside Chrome transport classification.
- Passive long-response behavior is unchanged: elapsed time alone never triggers refresh/reopen/recovery.

## 50-case acceptance matrix

### Target lifecycle — cases 01-18
1. Inspected target navigated or closed.
2. No target with given id.
3. Cannot find target.
4. Target closed.
5. Session closed.
6. Execution context was destroyed.
7. Context was destroyed.
8. Cannot find context with specified id.
9. Cannot find default execution context.
10. Cannot find execution context.
11. Frame with given id not found.
12. Cannot find frame with id.
13. Navigating frame was detached.
14. Frame was detached.
15. Execution context unavailable in detached frame.
16. Target crashed.
17. Renderer process gone.
18. Page/context/browser closed.

### Transport message variants — cases 19-31
19. Chrome closed the DevTools connection.
20. DevTools session invalidated.
21. Generic session invalidated wording.
22. Connection forcibly closed.
23. Remote host forcibly closed connection.
24. WebSocket remote close without close handshake.
25. Unable to connect.
26. Connection refused.
27. Target machine actively refused connection.
28. Connection reset.
29. Broken pipe.
30. WebSocket not connected.
31. Promise was collected.

### Exception-type boundaries — cases 32-38
32. `WebSocketException`.
33. `IOException`.
34. `TimeoutException`.
35. `HttpRequestException`.
36. `ObjectDisposedException`.
37. `TaskCanceledException` at the retry boundary; explicit caller cancellation remains handled by the existing earlier cancellation catch.
38. `SocketException`, including 10054 reset semantics.

### Expected Browser.close teardown — cases 39-43
39. WebSocket disconnect.
40. Disposed client socket.
41. Socket reset.
42. Missing WebSocket close handshake.
43. Reset-by-peer message.

### Nested/aggregate recovery — cases 44-47
44. Nested I/O transport failure.
45. Aggregate exception containing HTTP endpoint failure.
46. Nested disposed transport during browser close.
47. Nested default-execution-context lifecycle failure.

### Negative safety controls — cases 48-50
48. Invalid CDP parameters remain persistent/non-transient.
49. Arbitrary JavaScript `TypeError` remains persistent/non-transient.
50. ChatGPT UI prose such as `Something went wrong` is not a Chrome transport failure.

## Source contract
`ChromeTransportFailureClassifierMatrixTests` locks the 50 cases above and adds source assertions proving that the session-pool compatibility wrapper delegates to the canonical classifier and that the production default-context marker exists only once in the classifier source.

## Non-regression constraints
- Do not refresh or reopen a healthy chat because it is slow, unchanged, empty, thinking or streaming.
- Do not classify arbitrary ChatGPT assistant text as a Chrome transport failure.
- Do not hide invalid protocol parameters or JavaScript application errors behind retry logic.
- Keep the existing session retirement gate: active WebSocket I/O is aborted/retired safely and disposal happens only after exclusive command-gate reacquisition.

## Validation gate
The batch is mergeable only after the repository's established PR workflows are green, including Build GPTDeskTop, QA Release x64, QA Hidden Chrome CDP, QA Passive Chat Wait, QA Crash Process Recovery and development-task QA workflows.
