# spec

A copy of [coho-sdk-spec](https://github.com/coho-cms/coho-sdk-spec), the material every Coho SDK is tested against. Nothing here is edited by hand: to change it, change coho-sdk-spec and copy it again.

- `delivery/recordings/`: what the delivery API answered to a fixed set of requests. The runtime's tests replay them.
- `type-model/`: the type model's JSON Schema and fixtures. The generator's tests read them.

Copied 2026-10-17 from coho-delivery-sdk-typescript's copy of 2026-10-13, before coho-sdk-spec's first tag. From then on, copy a tagged release and name it here.
