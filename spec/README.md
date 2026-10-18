# spec

A copy of [coho-sdk-spec](https://github.com/coho-cms/coho-sdk-spec), the material every Coho SDK is tested against. Nothing here is edited by hand: to change it, change coho-sdk-spec and copy it again.

- `delivery/recordings/`: what the delivery API answered to a fixed set of requests. The runtime's tests replay them.
- `type-model/`: the type model's JSON Schema and fixtures. The generator's tests read them.

Copied from coho-sdk-spec **v2026.1.0a3** (2026-10-18): the type model at 1.1, with asset fields and the `assets` fixture, and delivery 2.1.0's `entry-with-assets` recording, whose `figure` type is in the delivery fixture. Copy a tagged release and name it here.
