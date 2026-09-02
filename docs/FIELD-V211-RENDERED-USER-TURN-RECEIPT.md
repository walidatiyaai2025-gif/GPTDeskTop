# v2.0.11 rendered user-turn receipt field closure

Field input: GPTDeskTop v2.0.10 could successfully create a fresh chat and physically submit a large Markdown-rich follow-up, but remain in unacknowledged reconciliation for ~90 seconds and report `Composer delivery was not confirmed`.

The reproduced pattern is materially different from the prior fresh-chat dwell continuity issue: the fresh chat is created, the 15-second dwell completes, physical send authority is granted, and the failure occurs only while proving the post-submit user-turn receipt.

Root cause: ChatGPT exposes the sent user message through rendered DOM text. Rich Markdown and very large/collapsed prompts are not guaranteed to be byte-for-byte identical to the raw composer source. Exact raw-text equality therefore creates a false negative after a real accepted submit.

v2.0.11 invariants:

- raw composer source and Markdown-rendered user-turn DOM may be treated as the same receipt only through strong normalized evidence;
- very large collapsed turns require a long normalized prefix (256 characters) and an independently observed increased user-turn count;
- unrelated/manual user turns must not match;
- generation observed after the verified idle-to-physical-submit edge plus a new user turn is acceptance evidence even when rendered text differs;
- no blind resend after an ambiguous physical submit;
- fresh-chat-per-response, 15-second pre-send dwell, 15-second inter-send cooldown, and exactly-once behavior remain intact.
