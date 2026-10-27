# Sample solution

One library and four test projects, one per test framework and platform, used by the integration test:

| Project | Framework | Platform | Coverage engine |
| --- | --- | --- | --- |
| `tests/XunitV2` | xUnit v2 | VSTest | coverlet.collector |
| `tests/NUnitT` | NUnit | VSTest | coverlet.collector |
| `tests/MSTestT` | MSTest | Microsoft.Testing.Platform | coverlet.MTP |
| `tests/XunitV3` | xUnit v3 | Microsoft.Testing.Platform | coverlet.MTP |

`Shop.Calc.Never` is never called, `Abs` misses one branch, `Total.get` is uncovered.
