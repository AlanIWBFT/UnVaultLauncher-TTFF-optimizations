# UnVault Launcher - Time-to-First-Frame optimizations

- NativeAOT compatible (no more SFX on startup, though it's no longer a single exe but has several dlls now) : -200ms
- A trick I stole from @thomasklemenc 's TaskSlinger (https://taskslinger.net/) : create a WARP device first and show the first frame, while creating the hardware device in the background. When ready, switch to the hardware one. This requires a patched Avalonia build (the build scripts are modified to do this automatically). ~300ms -> 40-50ms

- Other minor tweaks.
