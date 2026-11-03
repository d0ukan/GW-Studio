# GPP Series Rev.G notes used by v0.0.24

Source: `GPP Series User Manual`, Rev.G, dated 2024-05-06.

## Model profiles

| Model | CH1 | CH2 | CH3 | CH4 |
| --- | --- | --- | --- | --- |
| GPP-3060 | 0-32 V / 0-6 A | 0-32 V / 0-6 A | 1.8/2.5/3.3/5 V selection, fixed 5 A rating | absent |
| GPP-4323 | 0-32 V / 0-3 A | 0-32 V / 0-3 A | 0-5 V / 0-1 A | 0-15 V / 0-1 A |

The application uses rated output ranges for ordinary programming rather than the manual's wider adjustment/protection headroom.

## Shared and model-specific command behavior

- `*IDN?` supplies manufacturer/model/serial/firmware and is the only source used for selecting a profile.
- `VSETx`, `ISETx`, `VOUTx?`, `IOUTx?` and `:OUTPutx:STATe` are used for normal programmable channels.
- GPP-3060 CH3 voltage uses `:SOURce3:VOLTage`; no current-setting command is sent for that fixed-current auxiliary channel.
- GPP-4323 adds fully programmable CH3/CH4 to the normal VSET/ISET path.
- Tracking, load and native sequence operations are intentionally constrained to CH1/CH2 on both profiles.

## Implementation decisions

- A single central profile supplies channel existence, limits, voltage mode, current programmability and CH1/CH2 feature scope.
- Service validation repeats UI validation so terminal-independent application paths cannot address a nonexistent channel or use the other model's limits.
- Connection reads identity, setpoints, measurements and output states only; it never issues a mutating command.
- Presets and PC-timed profiles carry the target model. Cross-model execution is rejected.
