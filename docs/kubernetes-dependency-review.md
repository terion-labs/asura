# Kubernetes dependency approval change

The Kubernetes panel adds the following managed runtime dependencies. Existing licensing exceptions and native dependencies remain as previously recorded. The current owner approval records bind the previous file hashes; the proposed refresh covers only the dependency additions and first-party Kubernetes module below. No publication is performed by updating these records.

| Component | Version | License |
| --- | --- | --- |
| Fractions | 7.3.0 | Bundled two-clause BSD license text; NuGet declares a license file |
| KubernetesClient.Aot | 19.0.2 | Apache-2.0 |
| YamlDotNet | 16.3.0 | MIT |
| Asura.Kubernetes | Product version | First-party module; existing Asura license |

The new SDK analyzer/source generator is a build dependency and is absent from the runtime catalogs. NuGet lock files also record build-time packages. Package archive hashes, license documents and provenance are in the three managed component catalogs.

The approval refresh would update `reviewedAtUtc` and the changed evidence hashes in `licenses/macos-release-legal.json`, `licenses/workspace-backend-release-legal.json`, and `licenses/workspace-backend-x64-release-legal.json`, retaining the recorded engineering-evidence basis and previous unrelated dispositions.

| Changed approval input | Current SHA-256 |
| --- | --- |
| [licenses/managed-components.json](../licenses/managed-components.json) | `5eb7ea276e06eae3f50daf0e42f3246caef041c4caf638fb624b7cd87842b00d` |
| [licenses/THIRD-PARTY-NOTICES.md](../licenses/THIRD-PARTY-NOTICES.md) | `e6412cd64cb359bc581bf7998285617061deb0f9bd508f91086e5e3c772b5077` |
| [licenses/workspace-backend-managed-components.json](../licenses/workspace-backend-managed-components.json) | `7c29cd7df7d7d51241e9e7d1618d03aeb1d9010fef6f2a408371eb0c50335496` |
| [licenses/workspace-backend-x64-managed-components.json](../licenses/workspace-backend-x64-managed-components.json) | `33348eb7f298fb16898854ba4f03b99170613e38980b90983efe110e9c1141b7` |
| [src/Asura.Backend/packages.linux-arm64.lock.json](../src/Asura.Backend/packages.linux-arm64.lock.json) | `22176a0c2a485881283726a60ab189a4b0853c2faf0aa88bf2f92f6b81d1101f` |
| [src/Asura.Backend/packages.linux-x64.lock.json](../src/Asura.Backend/packages.linux-x64.lock.json) | `93ad831e20f53ffe4df69e413766e98f5113584ab3ed1db7c41f70c50ec6b48e` |

Approval has not yet been recorded for these changed inputs. The implementation and test results are in [Kubernetes panel testing](kubernetes-panel-testing.md).
