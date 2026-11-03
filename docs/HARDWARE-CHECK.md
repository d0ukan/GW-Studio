# GW Studio hardware acceptance checklist

Use no-load or safely current-limited conditions and verify the front panel after every safety-critical action. Run the complete checklist once with a GPP-3060 and once with a GPP-4323.

1. Connect at 115200 8N1/LF. Confirm the connected instrument model appears after identification, serial/firmware information is shown, the correct channel cards are created, and existing output states are preserved.
2. Connect an unsupported simulator identity and confirm connection is rejected before channel commands.
3. With outputs OFF, verify boundary set/readback: 3060 CH1/2 at 32 V / 6 A; 3060 CH3 at each 1.8/2.5/3.3/5 V selection; 4323 CH1/2 at 32 V / 3 A, CH3 at 5 V / 1 A, CH4 at 15 V / 1 A.
4. Confirm out-of-range values, 3060 CH4, and 3060 CH3 current writes are rejected before transmission. Confirm APPLY never enables output.
5. Toggle every displayed channel individually. Create a mixed state: the first ALL press must make every detected output OFF. From verified all-OFF, the next ALL press may enable all.
6. Press ALL OUTPUT OFF during normal work and a running PC-timed profile; verify every detected output is OFF. Test disconnect with opt-in OFF disabled, then enabled.
7. Confirm polling, channel cards, graphs, CSV rows and PDF selection include exactly the active model's channels.
8. Confirm Tracking, Load and native Sequence expose CH1/CH2 only. Confirm Custom exposes 3060 CH1/2 and 4323 CH1-4.
9. Save one Custom profile and quick preset per model. Confirm the other model cannot load/run them and that legacy untagged profiles produce a clear warning.
10. Unplug during polling and simulate an OFF verification failure. Confirm CONNECTION LOST/logging stop and an explicit unconfirmed-OFF error.

Software current auto-off is an advisory polling-based layer, not a replacement for instrument OCP.
