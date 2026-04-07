# 05 — Test Environment & QA Strategy

## Goals
- Catch bugs early with automated tests
- Validate crafting recipes are mathematically correct
- Ensure performance targets are met on low-end mobile hardware
- Equal day-one support for iOS and Android

## Minimum Supported Devices
| Platform | Min OS | Graphics API | Approx. Coverage |
|----------|--------|-------------|-----------------|
| Android | Android 7.0 (API 24) | Vulkan | ~95%+ active devices |
| iOS | iOS 12 / iPhone 6s | Metal | ~98%+ active devices |

- **URP** configured for Vulkan (Android) and Metal (iOS)
- OpenGL ES fallback not targeted — DOTS + Vulkan/Metal is the performance baseline
- Both platforms tested in parallel from day one — no platform priority

## Test Types

### Unit Tests
- Individual crafting recipe validation against a **canonical recipe JSON** (source of truth)
- Every recipe's inputs/outputs verified automatically on each CI build
- Building input/output logic
- Idle time calculation accuracy
- Power system eV calculations
- Prestige net worth calculations

### Recipe Validation Pipeline
- A canonical `recipes.json` file defines every recipe's correct inputs, outputs, and quantities
- Based on real-world atomic/molecular data — maintained as the scientific source of truth
- Unit tests diff every in-game ScriptableObject recipe against `recipes.json` on every PR
- Failed recipes block merging — no incorrect science ships without a deliberate override
- Overrides (intentional simplifications) documented inline in `recipes.json` with a reason field

### Integration Tests
- Full pipeline simulations (input → conveyor → building → output)
- Power grid coverage calculations across sample grid layouts
- Save/load integrity checks — verify no data loss across prestige
- Research unlock chain validation — confirm unlock triggers fire correctly

### Performance Tests
- Entity count stress tests using DOTS (target: 10k+ entities at 60fps on mid-range)
- Conveyor system throughput benchmarks
- Power system spatial query performance
- Frame rate profiled on physical devices — not just editor

## Tools
| Tool | Purpose |
|------|---------|
| Unity Test Framework | Unit & integration tests |
| Unity Profiler | Performance profiling |
| Firebase Test Lab | Real Android device testing |
| Xcode Instruments | iOS-specific profiling and memory |
| GitHub Actions | Automated test runs on every PR |

## Test Environments
- **Local:** Developer machine + Unity Editor play mode
- **CI:** Full test suite runs automatically on every PR (see 06-ci-cd.md)
- **Device Lab:** Firebase Test Lab (Android) + TestFlight devices (iOS)

## Coverage Targets
- Unit test coverage: 80%+ on all simulation systems
- All recipes in `recipes.json` must have a corresponding passing test
- Performance benchmarks must pass before any build is promoted to beta

## Notes / Open Questions
- [ ] Define exact entity count budget based on grid size decisions
- [ ] Decide who maintains `recipes.json` — manual curation vs generated from a chemistry API
