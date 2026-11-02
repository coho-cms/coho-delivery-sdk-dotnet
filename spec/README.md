# spec

A copy of [coho-sdk-spec](https://github.com/coho-cms/coho-sdk-spec), the material every Coho SDK is tested against. Nothing here is edited by hand: to change it, change coho-sdk-spec and copy it again.

- `delivery/recordings/`: what the delivery API answered to a fixed set of requests. The runtime's tests replay them.
- `type-model/`: the type model's JSON Schema and fixtures. The generator's tests read them.
- `webhooks/signatures.json`: webhook requests a receiver must accept and refuse, made by the webhook sender's own code. `Webhooks.Verify`'s tests run every case.

Copied from coho-sdk-spec's working tree ahead of **v2026.1.0a9**, not yet tagged (2026-11-02): the type model at 1.7, with singletons and the `singletons` fixture, and delivery 2.1.0's recordings, among them `entry-with-assets`, `entry-with-repeater`, `entry-with-localized-items`, and `entry-with-nested-items`, whose `figure` and `outline` types and `citation`, `step`, `outlineSection`, `point`, and `note` components are in the delivery fixture; and `webhooks/signatures.json`, the webhook signatures a receiver must accept and refuse, also not yet committed there. Copy a tagged release and name it here.
