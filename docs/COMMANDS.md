# GPP-3060 / GPP-4323 command map

Derived from the GPP Series User Manual, Rev.G, 2024-05-06.

| Operation | Command | 3060 scope | 4323 scope |
| --- | --- | --- | --- |
| Identity | `*IDN?` | exact `GPP-3060` | exact `GPP-4323` |
| Set/query voltage | `VSETx:<v>` / `VSETx?` | CH1/2 | CH1-4 |
| Set/query current | `ISETx:<i>` / `ISETx?` | CH1/2 | CH1-4 |
| Auxiliary voltage | `:SOURce3:VOLTage <v>` / `?` | CH3: 1.8/2.5/3.3/5 V | not used |
| Measure | `VOUTx?`, `IOUTx?` | CH1-3 | CH1-4 |
| Output | `:OUTPutx:STATe ON/OFF` / `?` | CH1-3 | CH1-4 |
| Tracking | `TRACK0/1/2` | CH1/2 | CH1/2 |
| Source/load mode | `:MODEx?`, `:LOADx:...` | CH1/2 | CH1/2 |
| Native sequence | `:SEQUencex:...` | CH1/2 | CH1/2 |

The application uses rated ranges rather than wider adjustment/protection headroom. Multi-command operations are serialized. APPLY writes current first where programmable, then voltage, reads setpoints back, and never changes output state. ALL actions write every active-model channel before verification.
