# Contextual success-response messages

## Change

- Successful API envelopes now use a message that describes the controller action, for example `Vehicle created successfully.` or `Profile updated successfully.`
- The response filter owns this mapping so controllers do not need to repeat response-envelope code.
- Any newly added action without a mapping receives a method-aware fallback message based on its HTTP method and action name.
- Swagger's envelope example now shows an action-specific message.
- Removed the duplicate `message` field from the Google callback payload; the standard envelope is now its single response message.
- Replaced bare `404` and raw string `400`/`409` responses in the affected APIs with descriptive `ProblemDetails` messages.

## Verification

- Build the Web API project with an isolated output path. The full solution build may be blocked while a locally running API holds its output DLLs; the test project also requires package restore before it can build.
