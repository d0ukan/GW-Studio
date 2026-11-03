# Unreleased

- Online update now reads the latest **GitHub Release** (`d0ukan/GW-Studio`) instead of the portal; the Setup `.exe` asset is downloaded and started directly.
- Added a **Portable** edition (marked by `portable.txt`) that never runs the installer and instead opens the release page.
- Added `installer/GW-Studio.iss` (Inno Setup) and a GitHub Actions release workflow producing a self-contained Setup `.exe` and Portable ZIP.
- Fixed the console test project so it builds; added update-parsing tests.

# v1.1.1 — 2026-10-02

- Fixed the Compact/Advanced toggle label so the button shows the destination mode: ADVANCED while Compact mode is active, and COMPACT while Advanced mode is active.
- No device-control, logging, 704 Lab, or update behavior changed.

# v1.1.0 — 2026-10-02

- Added a concise **Help** tab covering normal connection/control, Compact/Advanced modes, 704 Lab, graphs/logging, safety and online updates.
- Added optional background update checking through `portal.doukan.io/api/updates/gw-studio/latest` using slug `gw-studio`.
- Added **Update Now / Later** notification UI; Later never blocks normal use.
- Added asynchronous ZIP download to Windows TEMP, safe extraction, recursive Setup `.exe` discovery and installer launch. GW Studio exits only after the installer starts successfully.
- Added manual **Check for Updates** in Help with latest-version and connection-error feedback.
- Centralized runtime version display through `AppVersionInfo`; header, Settings/About, Help, PDF metadata and update checking use the same assembly version source.
- Added application/update logging for update checks, versions, availability, download/extract/setup events and errors.
- Project/assembly/file version promoted to **1.1.0 / 1.1.0.0**.

# v1.0.10 — 2026-09-24

- Restored the inherited dark TextBox theme for Quick Builder fields with per-field read-only rules; no other palette changes.
- Added live four-decimal measured V/A readouts directly below each channel's quick output button in the global header; disconnected/stale channels show a dash.
- Preserved all existing GW Studio and 704 functionality.

# v1.0.9 — 2026-09-24

- Half-scaled **only the Image Equation** reference for a selected single GPP channel: 25.0000 V at t=0.05 s, `V_CH(t) = 15.69 + 0.4655/t` through t<7.758 s and 15.7500 V afterward. Pre-hold: 0.050 s, ramp: 7.708 s.
- Re-labeled the quick template and curve menu with the **actual per-channel** values, rather than the two-channel reference values. Added a clear explanation in the 704 builder; fixed input fields are read-only while the equation is selected.
- ARM/RUN, plot tracker, PDF snapshots, PNG, and saved profile data all use real single-channel requested voltages; the app does not double measurements or automatically control the other series channel.
- Legacy saved image-equation profiles are migrated to half scale on load. Other Linear/Exponential/Logarithmic/Inverse-time, Custom Points, Segment Editor and Drag Editor profiles retain their existing voltage behavior.
- Output of two correctly configured series channels is a separate physical quantity and must be verified externally. This application remains PC-timed and is not a MIL-STD-704 compliance system.

# v1.0.8 — 2026-09-24

- Applied the existing high-contrast dark OxyPlot tracker template to the embedded and fullscreen 704 requested/measured graphs.
- Tracker background now matches the dark theme and text is readable in both 704 graph views; preserved the rest of the application theme and plotting behavior.

# v1.0.7 — 2026-09-23

- Removed the nested vertical scrollbar from the 704 RUN / SAVE / LOAD column.
- The entire 704 Lab now uses one outer vertical scrollbar for the graph, RUN, SAVE / LOAD and editors.
- RUN remains beside the graph; SAVE / LOAD follows directly below RUN and remains reachable on short laptop screens.
- Kept GRAPH / EDITORS jump controls, fullscreen graph, existing theme and device-control behavior.

# v1.0.6 — 2026-09-22

- 704 Lab now has a page-level vertical scrollbar so the graph and every editor remain reachable at laptop display heights.
- Placed the requested-vs-measured graph above the editors to keep it visible immediately on smaller screens.
- Added GRAPH and EDITORS jump buttons in the 704 header.
- Fixed graph-pane height and bounded its RUN/SAVE-LOAD sidebar scrolling independently; the outer page can also scroll.
- Preserved all v1.0.5 waveform functionality, fullscreen graphs, export, and the original dark/cyan theme.

# v1.0.5 — 2026-09-22

- Drag Editor now supports adding control points up to a 64-point safety/performance cap and removing the selected interior point.
- Dragging no longer rebuilds the complete OxyPlot model or refreshes command state on every mouse move; the existing series is mutated in place and finalized on mouse-up.
- 704 live requested/measured plot rendering is throttled to 250 ms, preview render points are capped at 2,500, and measured render points at 1,500; stored measurement data is unchanged.
- Preserved all v1.0.4 functionality, including PNG/PDF export and fullscreen 704 controls.

# v1.0.4 — 2026-09-22

- Fixed Drag Editor left-click dragging by reserving the drag plot mouse controller for editable control points instead of OxyPlot tracker/pan gestures.
- Replaced the hard-to-read default tracker popup with a dark themed fixed-precision Time/Voltage overlay shown while dragging.
- Increased drag control point hit area/marker size for easier selection.
- Made the right-side 704 RUN + SAVE/LOAD column vertically scrollable so controls remain accessible on shorter displays.
- Preserved v1.0.3 PNG/PDF export behavior.

# v1.0.3 — 2026-09-22

- Fixed Windows publish error CS0117 in `PlotExportService`: OxyPlot.Wpf 2.2.0 `PngExporter` does not expose a `Background` property.
- PNG export now uses the existing `PlotModel.Background` while configuring only supported exporter properties.
- Preserved all v1.0.2 PNG/PDF export features.

# v1.0.2 — 2026-09-22

- Added PNG export buttons for the embedded and fullscreen 704 requested-vs-measured graphs.
- Exported PNG files default to the Logs\Exports folder and preserve the current requested/measured overlay view.
- PDF session reports now append the latest 704 requested-vs-measured waveform snapshot when a 704 preview exists.

- Added a dedicated movable/resizable MIL-STD-704 requested-vs-measured graph window.
- Added true full-screen, minimize, maximize/restore and close controls for the 704 graph.
- Added compact ARM / RUN / STOP controls at the right side of the fullscreen 704 graph, plus Repeat and Delay.
- Fullscreen 704 plotting uses a detached PlotModel snapshot so the embedded and floating OxyPlot views do not fight over the same model instance.
- Preserved all v1.0.0 behavior and project branding.

# v1.0.0 — 2026-09-22

- Promoted GW Studio from the 0.x development series to the first stable **v1.0.0** release.
- Application/package version metadata is now 1.0.0 / 1.0.0.0.
- No feature behavior was removed; the complete v0.0.34 feature set is preserved as the v1.0.0 baseline.

# v0.0.34

- Added separate **704 Test Library**, **Segment Editor**, and **Drag Editor** tabs inside 704 Lab.
- 704 Test Library includes ready bench starting points for Normal transient, Power interrupt, Undervoltage, Overvoltage, Recovery and Ramp use cases; templates are explicitly labeled as non-certified starting points.
- Segment Editor supports Hold, Linear Ramp, Exponential Ramp and Output Off rows with add/delete/reorder controls.
- Drag Editor lets the user load the current waveform, drag a limited set of control points, and apply the edited shape as the requested profile.
- Added shared Loop/Repeat (1-100) and inter-loop delay controls for all 704 profile types.
- Repeat expansion is deferred until ARM; editor previews stay lightweight.
- Added plot/downsampling safeguards and segment-grid virtualization to avoid UI slowdown.
- Execution engine now skips redundant SCPI writes for dwell-only steps, including inter-loop delays and adjacent identical holds.
- Existing requested-vs-measured overlay, power interrupt, save/load, hardware-limit ARM validation and dark/cyan theme are preserved.

# v0.0.33

- 704 Lab now overlays the generated/requested voltage profile with live measured PSU voltage on the same plot.
- Added Show measured and Clear measured controls.
- Added Custom Points profiles using `time=voltage` pairs for multi-segment linear waveforms (for example image-derived transient envelopes).
- Added quick templates for the supplied Figure 13 upper and lower transient-envelope examples.
- Added Power Interrupt builder: user enters a duration such as 50 ms; execution turns the selected output OFF for that interval and restores it afterward.
- Power-interrupt timing is PC/serial timed and therefore not a precision compliance pulse generator; actual waveform must be verified from measured data / external instrumentation.
- Existing 704 save/load now persists mode, custom points, and interrupt duration.


## v0.0.33 — 2026-09-21
- Added a dedicated **704** button beside COMPACT/ADVANCED.
- Added a deliberately simple **704 Lab** with a large center waveform preview.
- Added Linear, Exponential, Logarithmic, Inverse-time, and exact supplied-image `31.38 + 0.931/t` waveform generation.
- Added editable start/end voltage, ramp duration, current limit, pre/post hold, and step interval.
- Added save/load/delete library for 704 waveform definitions.
- ARM/RUN/STOP reuses the existing safety-validated custom profile engine.
- Preview can exceed hardware limits; ARM rejects unsupported setpoints such as 50 V on a 32 V GPP channel.
- Adjacent voltage-only ramp points avoid redundant current/output writes.
- Existing theme, Compact/Advanced, reconnect, graph marker/set-value controls and multi-monitor graph windows remain unchanged.

# v0.0.31

## v0.0.31 — 2026-09-15
- Multi-monitor graph windows: graph windows are now independent/modeless and can be moved to another display while the main app remains usable.
- Standard movable/resizable window chrome with native minimize/maximize/restore support.
- Added in-window FULL SCREEN / EXIT FULL SCREEN, MINIMIZE, MAXIMIZE/RESTORE, and CLOSE controls.
- Full screen applies to the monitor where the graph window currently resides.
- Existing graph visibility controls, zoom/pan and output safety controls are preserved.

## v0.0.30 — 2026-09-15
- Added **Show markers** graph checkbox. Event markers/labels can now be hidden without deleting marker history.
- Added **Show set values** graph checkbox. Dashed Voltage Set and Current Limit reference traces can be hidden together without changing instrument settings.
- Added the same visibility toggles to full-screen graph mode.
- Existing GW Studio branding, v0.0.29 theme, graph history and device-control behavior are preserved.

## v0.0.29 — 2026-09-15
- Removed the legacy **GPP AUTO** placeholder from the main header.
- Before connection the header now shows only **GW Studio**; after identification the actual supported device model may still be shown.
- No theme or control behavior changes.


## v0.0.28 — 2026-09-14
- Restored the original v0.0.26 dark-theme palette.
- Primary accent returned to cyan/teal (#4ED4C4).
- Border and selection colors returned to the original v0.0.26 values.
- GW Studio branding, GW logo, application icon, and Dou naming from v0.0.27 are retained.
- Safety/status colors (green, amber, red) and all v0.0.27 functionality are unchanged.

- Rebranded the full application and solution as **GW Studio**.
- Normalized the creator attribution to **Dou** throughout the project.
- Added a new dark GW icon and changed the primary accent from cyan to GW orange.
- Renamed solution/project/test project files to `GW-Studio*` and root namespace to `Dou_GW_Studio`.
- Updated application-data, log, report and graph-export branding.
- Preserved GW Instek GPP-3060/GPP-4323 device identifiers and device-control behavior.

# Changelog

## v0.0.27 — 2026-09-14

- Added persistent **Compact / Advanced** UI modes with a one-click header switch.
- Compact mode keeps the bench essentials visible: live V/I/P, channel mode/status badge, requested voltage/current, APPLY SETTINGS, OUTPUT controls, quick outputs, reconnect and ALL OUTPUT OFF.
- Advanced-only channel details now include device setpoint readbacks, UI lock, warnings, software max-current protection, regulation notes and Quick Preset management.
- Live Graph, Custom, Sequence, Logger and Terminal tabs are hidden in Compact mode; Dashboard and Settings remain available.
- CH1/CH2 operating-mode editor is Advanced-only while the live mode/status badge stays visible in Compact mode.
- Switching back to Compact automatically returns to Dashboard, avoiding a hidden advanced tab remaining selected.
- The selected UI mode is stored in application settings and restored on the next launch.


## v0.0.24 — 2026-09-14

- Moved **Quick Presets** to the bottom of each ChannelCard.
- Restored compact, side-by-side **APPLY SETTINGS** and **OUTPUT ON/OFF** controls directly below Requested Voltage/Current.
- OUTPUT state is visually emphasized: **OFF = amber/orange**, **ON = green**.
- Voltage measurement and device set-voltage displays now use **four decimal places** (for example `28.9486 V`).
- Preserved theme, device detection, safety controls, logging, graphing, and existing channel behavior.

## v0.0.23 — 2026-09-14

- Moved each channel card's `APPLY SETTINGS` and dynamic `OUTPUT ON/OFF` buttons directly below the requested voltage/current controls.
- Kept optional soft-warning and max-current protection settings below the primary action row so the normal channel workflow no longer requires scrolling.
- Preserved command bindings, safety semantics, automatic GPP-3060/GPP-4323 behavior, and the existing visual theme.

## v0.0.22 — 2026-09-14
- Fixed CS1503 in `DevicePollingService`: `channels` is an array after `ToArray()`, so polling metrics now use `channels.Length`.
- Fixed CS8604 in discrete-voltage formatting/validation by using non-null arrays established through property-pattern matching.
- No theme, UI workflow, device-profile behavior or safety semantics were changed.

## v0.0.21 — 2026-09-14
- Unified GPP-3060 and GPP-4323 support in one model-neutral application.
- Added connect-time `*IDN?` auto-detection and a central per-model capability profile.
- Channel cards, quick controls, polling, graphs and reports now use the detected 3- or 4-channel topology.
- Added model-specific service guards, including absent-channel rejection and discrete/fixed-current GPP-3060 CH3 behavior.
- Model-tagged quick presets and Custom profiles prevent accidental cross-model execution.
- Preserved the existing theme, workflow, logging/reporting, mode/sequence scope and safety-first output semantics.

## v0.0.19 — 2026-09-14
- Increased the size of the **Actual Voltage**, **Actual Current**, and **Actual Power** readouts on the channel cards.
- Enlarged the corresponding labels and unit text for better readability.

## v0.0.18 — 2026-09-14
- Fixed Logger startup crash caused by a default TwoWay binding to the read-only `LogFolderPath` property.
- `LogFolderPath` is now explicitly bound `Mode=OneWay`; the path remains application-controlled and display-only.

## v0.0.17 — 2026-09-14
- Replaced the previous lightning application icon with a new **Gw** lettermark icon aligned with the dark/cyan engineering theme.
- Updated the EXE/application icon, main window icon, header icon and About/Settings icon to use the new **Gw** branding asset.
- Kept the user-facing product name as **GW Studio** with **Made by Dou** attribution.

## v0.0.16 — 2026-09-14
- Moved continuous CSV logger output to a dated `Logs\yyyy-MM-dd` folder beside the running application, with automatic `%LOCALAPPDATA%\GW Studio\Logs` fallback when the application directory is not writable.
- Logger UI now shows the resolved log folder / next CSV file and provides **OPEN LOGS FOLDER**.
- Added on-demand **PDF SESSION REPORT** export with selectable visible-window/full-history range, CH1/CH2 inclusion, Voltage/Current/Power vector graphs and graph markers.
- PDF reports include device identity, COM/connection health, per-channel set/actual values, Min/Avg/Max statistics, warning/protection status, operating mode, Custom status, Native Sequence status, sample range/count and Made by Dou/version footer.
- PDF generation is dependency-free and only runs on user request; raw high-rate measurements remain in CSV so normal acquisition/render performance is unaffected.

## v0.0.15 — 2026-09-13
- Enabled graph navigation only in single-graph full-screen mode.
- Mouse wheel zooms; right-button drag pans; middle-button drag creates a zoom rectangle; left-click keeps the time/value tracker.
- Added RESET / AUTOSIZE to restore the graph's configured time window and Y scale.
- Full-screen navigation state is isolated from the normal embedded graphs; closing full screen returns to the normal graph presentation.

## v0.0.14 — 2026-09-13
- Renamed the Windows assembly/executable to **GW Studio.exe**.
- Renamed the main solution/project files to **GPP-3060-Control-Studio.sln/.csproj**.
- Added a dedicated dark/cyan application icon and wired it to the EXE, main window, taskbar and About card.
- Updated assembly/file metadata to **GW Studio** and **Made by Dou**.
- Preserved internal namespace and LocalAppData paths for backward compatibility with existing settings and profiles.

## v0.0.13 — 2026-09-13
- Changed user-facing application branding from Dou-prefixed naming to **GW Studio**.
- Added **Made by Dou** attribution to the About/Settings card and assembly package metadata.
- Added lightweight dark-theme glyph/icon accents to the custom title bar, main product header and navigation tabs with no new NuGet dependency.
- Updated default graph CSV export filename prefix to `GPP-3060`.
- Preserved internal namespaces, solution identity and LocalAppData paths for backward compatibility with saved profiles/settings.


## v0.0.12 — 2026-09-13
- Added per-channel Quick Presets for CH1/CH2. Presets persist in the application settings and only load requested V/I values; hardware changes still require APPLY SETTINGS.
- Added optional soft upper warning thresholds for voltage/current. Thresholds are armed only by APPLY SETTINGS; amber warning is shown above the threshold and red critical warning above 110% of the configured threshold.
- Added lightweight Connection Health readout with last response time, timeout count, error count, successful replies and measured poll rate, without generating extra instrument queries.
- Added CSV export for only the currently visible graph time window and selected channels.
- Added graph note/marker support. Notes are attached to the latest sample and rendered as vertical annotated markers on Voltage/Current/Power plots, including full-screen plots.
- Added per-channel UI-only settings lock. Lock prevents requested setpoint editing/applying and preset loading while leaving output controls available.
- Added persistent UI preferences for selected COM port, graph time window, visible graph channels/signals and window size/maximized state.

## v0.0.11
- Polling post-sweep delay can now be set from 1–10000 ms.
- Added live measured sweep duration, effective acquisition period, and per-channel sample-rate readout in Settings.
- Decoupled serial acquisition/history/logging from WPF channel/chart rendering; UI updates are coalesced to prevent high-rate polling from flooding the dispatcher.
- Kept chart redraw throttling independent from acquisition so fast collection does not force equivalent UI refresh rates.
- Throttled visible polling traffic in the terminal to avoid dispatcher flooding at aggressive acquisition settings.
- Software max-current monitoring retains the peak current seen between UI refreshes so short readback spikes are not overwritten by a later lower sample.

## v0.0.10 — 2026-09-12

- `MAX CURRENT AUTO-OFF` alanı draft/applied olarak ayrıldı: TextBox düzenlenirken aktif protection limiti artık değişmez.
- Yeni max-current limiti yalnızca kanal kartındaki `APPLY SETTINGS` başarılı olduğunda arm edilir; böylece 0.25 A yazılırken ara değer olan 0.2 A nedeniyle yanlış trip oluşmaz.
- Alan boş bırakılıp `APPLY SETTINGS` basılması software max-current auto-off özelliğini devre dışı bırakır.
- UI tooltip/açıklaması yeni Apply davranışını açıkça belirtecek şekilde güncellendi.

## v0.0.9 — 2026-09-12

- Dashboard CH1/CH2 kartlarına isteğe bağlı `MAX CURRENT AUTO-OFF` alanı eklendi. Boş bırakılırsa devre dışı; ölçülen akım kullanıcı limitini aşarsa ilgili kanalın output'u yazılım tarafından OFF komutuyla kapatılır ve durum kartta gösterilir.
- Software current protection bir custom test/action çalışıyorsa onu iptal eder, ilgili kanalı OFF yapar ve OFF durumunu cihazdan doğrular. Bu özellik polling/USB gecikmesine bağlıdır ve donanımsal OCP yerine geçmez.
- Voltage, Current ve Power grafiklerinin her birine seçilen zaman penceresi ve seçili kanallar için `Min / Avg / Max` istatistikleri eklendi.
- Aynı Min/Avg/Max istatistikleri tek-grafik full-screen görünümünde de gösterilir; PAUSE durumunda dondurulan veri üzerinde hesaplanır.
- Önceki tracker template derleme düzeltmesi projeye kalıcı olarak işlendi (`System.Windows.Controls.ControlTemplate`).

## v0.0.8 — 2026-09-12

- `ALL ON / OFF` güvenlik mantığı değiştirildi: herhangi bir kanal ON ise (mixed state dahil) ilk basış artık her zaman CH1/CH2/CH3'ü OFF yapar.
- Yalnızca tüm kanallar zaten OFF olduğunda sonraki `ALL ON / OFF` basışı tüm kanalları ON yapar.
- ALL durum metni mixed output durumunda artık `MIXED` gösterebilir; tooltip yeni safety-first davranışı açıklar.

## v0.0.7 — 2026-09-12

- OxyPlot graph tracker/cursor tooltip koyu tema ile uyumlu hale getirildi; açık sistem tooltip arka planı kaldırıldı.
- Cursor bilgi kutusu koyu Surface arka plan, açık yüksek-kontrast metin, accent border/crosshair ve monospace değer yazımı kullanıyor.
- Pause + cursor ile zaman/değer incelemesinde okunabilirlik düzeltildi.

## v0.0.6 — 2026-09-12

- Borderless/maximized ana pencerenin Windows taskbar work-area sınırlarını aşması düzeltildi; alt status/message bar artık tam ekranda taskbar arkasında kalmıyor.
- Maksimize boyutlandırma aktif monitörün gerçek work area değerlerinden hesaplanıyor; çoklu monitör kullanımında da taskbar konumu dikkate alınıyor.
- LiveChartControl içindeki OxyPlot tracker binding namespace çakışması giderildi; `OxyPlot.PlotCommands.SnapTrack` kullanılıyor.

## v0.0.5 — 2026-09-12

- Uygulama varsayılan olarak maximized/full-screen çalışma alanında başlatıldı.
- Dashboard power readback, Voltage ve Current ile aynı görsel öncelikte büyük gösterildi; ayrıca Device Set Voltage / Device Current Limit ayrı readback alanları eklendi.
- Her Voltage/Current/Power grafiğine ayrı full-screen açma eklendi; full-screen görünüm yalnızca seçilen grafiği büyütür ve ALL/CH1/CH2/CH3 output güvenlik kontrollerini korur.
- OxyPlot tracker cursor eklendi; Pause sonrası çizgiye tıklayarak relative time ve ölçüm değeri incelenebilir.
- Custom Review transitions DataGrid çoklu seçime geçirildi; Ctrl/Shift seçimiyle birden fazla step tek seferde silinebilir.
- 10–50 ms polling ayarlarında UI yükünü azaltmak için serial acquisition ile chart rendering ayrıştırıldı; chart redraw throttle edildi ve gizli polling TX/RX kayıtları UI listesine hiç eklenmiyor.
- Independent / Tracking Series / Tracking Parallel kontrolü ve cihaz mode readback eklendi; güvenlik için mode değişiminde CH1/CH2 OFF şartı var ve FAST röle komutu kullanılmıyor.
- CH1/CH2 Load CV / CC / CR geçişi ve load setpoint alanları eklendi; load mode değişiminde ilgili kanal OFF olmalı.
- Native GPP Sequence editor eklendi: CH1/CH2, 1–300 s step, cycle/infinite, start group, end state, upload, enable/disable ve restart. Sequence enable kanalı otomatik ON yapmıyor.
- CH3 için 1.8 V / 2.5 V / 3.3 V / 5.0 V seçimi ve readback eklendi; current programlanmıyor. Manualdeki CH3 komut notu temel alındı ve donanım üzerinde ilk kabul testi gerektiği UI'da belirtildi.

## v0.0.4 — 2026-09-12

- ZIP/proje içindeki sürüm tutarsızlığı giderildi; assembly, görünür sürüm ve dokümantasyon v0.0.4 olarak eşitlendi.
- CH1/CH2 programlama aralığı 0–32 V / 0–6 A olarak güncellendi; grafik sabit ölçekleri de 32 V / 192 W olacak şekilde düzeltildi.
- Settings üzerinden polling bekleme alt sınırı gerçekten 10 ms olacak şekilde polling servisindeki 100 ms clamp kaldırıldı; 10 ms seçimin gerçek 10 ms örnekleme garantisi olmadığı UI'da açıklandı.
- Üst bara her zaman erişilebilir ALL, CH1, CH2 ve CH3 ON/OFF toggle kontrolleri eklendi; acil ALL OUTPUT OFF butonu korunarak ayrı bırakıldı.
- CH3 için sayısal setpoint kontrolü açılmadan yalnızca output ON/OFF kontrolü ve durum doğrulaması eklendi.
- Live Graph ve Custom içindeki ortak grafik paneline FULL SCREEN eklendi; tam ekran görünümde ALL/CH1/CH2/CH3 output kontrolleri ve ALL OUTPUT OFF erişilebilir kaldı.
- Custom profil saklama tam dosya yolu girişinden uygulama içi Saved Profiles kütüphanesine taşındı; profil adıyla kaydetme, listeden seçip yükleme, silme ve yenileme eklendi.
- Custom parametrelerinin yanına ⓘ yardım açıklamaları ve kısa parameter guide eklendi; Voltage Step varsayılanı 28 → 31 V olarak güncellendi.

## v0.0.2 — 2026-09-12

- COM port açılışındaki erişim reddi için port adı ve çözüm adımlarını içeren anlaşılır hata eklendi.
- Bağlantı hatası terminale ERR olarak da kaydediliyor; port nesnesi temizleniyor.
- Başka uygulamanın port kilidi zorla kaldırılmaz; o uygulamanın bağlantısı kapatılmalıdır.
- Görünen sürüm assembly üzerinden v0.0.2 olarak güncellenir.
- Yalnızca değişen dosyaları içeren yama dağıtımı.

## v0.0.1 — 2026-09-12

İlk gerçek uygulama. Kullanıcının özel ilk teslim talimatına göre başlangıç sürümü korundu.

- Mevcut .NET 8 WPF solution ve proje kimliği korunarak koyu temalı yedi sekmeli Control Studio oluşturuldu.
- USB CDC COM keşfi, 115200/8N1/LF, *IDN? kimlik doğrulaması ve read-only bağlantı başlangıcı eklendi.
- CH1/CH2 source setpoint/ölçüm, Apply readback doğrulama, ayrı output komutları ve periyodik izleme eklendi.
- Sıralı seri transaction ve çok komutlu cihaz işlemi kilitleri, timeout ve iptal sonrası cevap senkronizasyonu koruması eklendi.
- CH1–CH3 için ALL OUTPUT OFF ve OFF doğrulaması; otomatik OFF yapmayan varsayılan Disconnect/Close davranışı eklendi.
- OxyPlot ile yeniden kullanılan V/A/W grafikleri, kanal/sinyal/reference seçimi, zaman penceresi ve bounded history eklendi.
- Pulse, Voltage Step, Power Cycle, editable timeline, ARM/RUN/STOP, JSON profil kaydet/yükle ve PC Timed uyarıları eklendi.
- CSV streaming logger, bounded queue, terminal TX/RX/ERR kayıtları ve settings persistence eklendi.
- Source/Load/Tracking/Native Sequence için genişleme modelleri; uygulanmamış kontroller açıkça pasif bırakıldı.
- CH1/CH2 yazma sınırları 0–30 V / 0–6 A; 31 V genişletilmiş programlama doğrulaması ertelendi.
- System.IO.Ports 8.0.0 ve OxyPlot.Wpf 2.2.0 eklendi; packages.lock.json oluşturuldu.
- Release solution build 0 error/0 warning; 27 davranış/OS seri entegrasyon testi geçti. Gerçek Windows/GPP kabul testi bekliyor.


## v0.0.25
- Live CV/CC/CR and channel status badges.
- Automatic graph event markers.
- Manual/automatic reconnect support.
