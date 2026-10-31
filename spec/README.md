# spec

A copy of [coho-sdk-spec](https://github.com/coho-cms/coho-sdk-spec), the material every Coho SDK is tested against. Nothing here is edited by hand: to change it, change coho-sdk-spec and copy it again.

- `delivery/recordings/`: what the delivery API answered to a fixed set of requests. The runtime's tests replay them.
- `type-model/`: the type model's JSON Schema and fixtures. The generator's tests read them.

Copied from coho-sdk-spec's working tree ahead of **v2026.1.0a7**, which is not yet tagged (2026-10-31): the type model at 1.5, with lists, the `lists` fixture, and localized component fields, and delivery 2.1.0's recordings, among them `entry-with-assets`, `entry-with-repeater`, and `entry-with-localized-items`, whose `figure` type and `citation` and `step` components are in the delivery fixture. Copy a tagged release and name it here.
