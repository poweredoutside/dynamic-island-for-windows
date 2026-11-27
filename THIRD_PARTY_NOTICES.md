# Bundled libraries

The installer includes these libraries and their transitive dependencies; no
separate programs need to be installed.

- NAudio 2.2.1 — MIT, https://github.com/naudio/NAudio
- Liquid Glass lens math adapted from liquid_glass_easy — MIT, Ahmed Gamil.
  Upstream: https://github.com/AhmeedGamil/liquid_glass_easy/tree/ba732aa38927a05c2a8604f843fa4bb54b87fb14.
  WPF/HLSL adaptation: Services/GlassLensMap.cs and Shaders/LiquidGlass.hlsl.
  License included in licenses/liquid_glass_easy/LICENSE. Flutter is not bundled.
- Ical.Net 4.3.1 — MIT, https://github.com/ical-org/ical.net
- Microsoft.Diagnostics.Tracing.TraceEvent 3.1.21 — MIT, https://github.com/microsoft/perfview
- LibreHardwareMonitorLib 0.9.6 — MPL-2.0. Unmodified source:
  https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/3d331e3370efb858411f19511373eff65a218701
  License and upstream notices are included in licenses/LibreHardwareMonitor.
- System.Management 10.0.2 and System.Security.Cryptography.ProtectedData 8.0.0 — MIT,
  https://github.com/dotnet/runtime
- NodaTime (Ical.Net dependency) — Apache-2.0, https://github.com/nodatime/nodatime
- PawnIO 2.2.0 signed official installer, redistributed unmodified. Driver source
  and GPL-2.0-or-later with IOCTL exception: https://github.com/namazso/PawnIO.
  Official installer: https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0.
  Corresponding driver source with submodules is available as
  PawnIO-2.2.0-source.zip beside the application installer in the same GitHub release.
  Driver installation is optional and is offered explicitly by the setup UI.

Optional network services: Gemini Live (Google), LRCLIB and GitHub Releases.
Their respective service terms apply. Gemini requires a user-supplied key.

Feature references: Atoll 1.8.0 public release notes,
https://github.com/Fuwni/dynamic-island-windows/releases/tag/v1.8.0.
No Atoll source code or binary assets were copied into this application.
DirectX ETW event identifiers were checked against Intel PresentMon's public
provider metadata (MIT): https://github.com/GameTechDev/PresentMon/tree/main/PresentData/ETW.
