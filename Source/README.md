# Source Layout

- `BotLink/` - Discord/bot account linking and bot HTTP API calls.
- `Core/` - plugin entrypoint, shared models, and player lookup helpers.
- `Creative/` - creative-mode biome, inventory, command-zone, and vegetation behavior.
- `Network/` - shared RPC names, RPC wiring, and send-state helpers.
- `Patches/` - general Valheim patches that do not yet belong to a narrower feature area.
- `Siege/` - siege gateway and portal bridge behavior plus related test commands.
- `Telemetry/Common/` - shared telemetry serialization, math, and runtime metadata.
- `Telemetry/FrameMetrics/` - local frame-time measurement.
- `Telemetry/RpcTrace/` - RPC trace capture, local storage, upload, probes, and socket metrics.
- `Telemetry/ValheimEvents/` - ValheimEvents snapshot and telemetry publishing.
- `Telemetry/ZdoTrace/` - ZDO trace capture.

Keep new feature code in the narrowest folder that describes the behavior. Add a new top-level feature folder when a feature has multiple files or is expected to grow.
