# HKU Library Seat Booking

> Current version: `0.1.6` - assisted Edge sign-in, sustained release-window retries, and faster preparation.

A lightweight Windows 10/11 utility for booking seats at The University of Hong Kong (HKU) Libraries. After the user provides their own booking information, the app stays in the corner of the desktop, shows the release countdown and current status, and attempts to submit a booking at the configured release time. It continues monitoring for the next release unless the user closes it.

The scheduling and retry workflow follows the same general approach as [SeatKiller-GUI](https://github.com/yeliudev/SeatKiller-GUI): save configuration locally, wait for the target time, submit the booking, retry on failure, and continue monitoring.

## What Problem This Solves

HKU library seats must be booked for specific dates and time slots. Popular slots may fill quickly, so manually checking the page and submitting at the correct moment can lead to missed opportunities.

This project focuses on one core workflow:

```text
Enter booking settings -> validate Cookie/login access -> wait for release
-> select the target library and date -> submit the slot and seat -> show the result
```

The application does not replace required on-site check-in. It does not bypass HKU authentication, CAPTCHA, authorization, or any other access control.

## Supported Booking Scope

This project is not a universal client for every HKU library, facility, or booking flow. The current release has the following support boundaries.

### Explicitly supported

| Location | Facility | Time slots |
| --- | --- | --- |
| Law Library Study Table | Study Table | `09:00-13:00`, `13:00-18:00`, `18:00-21:00` |
| Chi Wah Learning Commons | Study Room | Hourly sessions, including `23:00-23:45`; supports the `Submit -> Yes` confirmation flow |

Changing the target location rebuilds the time-slot list and replaces the previous location's slots. A preset slot displays the exact time applied by the application.

### Experimental and not independently verified

The settings dialog retains entry points for the following locations, but they currently rely on generic WebForms matching and generic time presets:

- Main Library 4/F Study Tables
- Main Library Level 3 Study Tables
- Deep Quiet Room Study Tables
- Main Library Single Study Rooms

### Not supported or not guaranteed

- Dental Library, Medical Library, or Music Library
- Faculty of Medicine or Research Student Centre
- Study Room at Meng Wah Complex or the University of Hong Kong History Gallery
- Discussion Rooms and other facility types
- Bulk booking, multiple seats, multiple accounts, or arbitrary custom URLs
- Any location or facility not listed above

The `Custom Location` and experimental entries exist for page-level adaptation and diagnostics. Their presence in the settings dialog does not mean that the location has been fully validated.

## Main Features

- Shows a first-run setup dialog for account details, Cookie, target library, seat, time slot, release time, and retry settings.
- Stores settings at `%LOCALAPPDATA%\HKU-Library-Seat-Booking\settings.json`.
- Encrypts the password and Cookie with Windows DPAPI for the current Windows user; plaintext credentials are not written to the settings file.
- Calculates the next release in Hong Kong time. The default is `00:00`, and the release time is configurable.
- Supports a target date offset from `0` to `7` days after the release date.
- Loads the booking page about 20 seconds before release and takes three server `Date` samples. Round-trip delay is removed from each sample, and the median offset is used to calibrate submission timing.
- Retries transient TLS and network failures for read-only GET requests, while avoiding blind retries of booking POST requests.
- Keeps retrying page preparation until two minutes after the release time, instead of abandoning the release after a short failure.
- Performs one controlled page pre-refresh about 500 ms before release, then uses adaptive single-request retry delays: 200 ms in the first 5 seconds, 500 ms up to 30 seconds, and 1000 ms afterward.
- Reuses a prepared page when its target date is already available, avoiding an extra full-page download at the release instant.
- Uses lightweight HEAD requests for follow-up clock samples while retaining one full page load for form preparation.
- Starts a redacted HAR recording two minutes before each release. It records request timing, status, safe form fields, HTTP failures, and connection exceptions, then saves the file whether booking succeeds or fails.
- Understands ASP.NET WebForms fields including `__VIEWSTATE`, `__EVENTVALIDATION`, library, facility type, date, slot, seat, and submission controls.
- Uses location-specific slot schedules and supports Chi Wah's hourly `Submit -> Yes` confirmation flow.
- Supports Cookie-based sessions and ordinary HTML login forms. Cookie mode should be used when HKU redirects through single sign-on.
- Provides an assisted Edge sign-in window. The user completes HKU SSO/MFA manually; the app imports the session locally without asking for manual Cookie extraction and never stores the HKU password for this flow.
- Supports configurable retries from `0` to `50`, with a retry interval from `100` to `60000` milliseconds.
- Provides a compact, borderless, always-on-top floating window that can be dragged and remembers its position.
- Supports configurable opacity from `35%` to `100%`.
- Can be minimized to the system tray and restored from the tray.
- Displays waiting, page preparation, submission, retry, success, failure, and authentication-required states.
- Runs one account for the current user and does not provide cloud sync, remote notifications, bulk booking, or a complex dashboard.

## HKU Booking Rules

The public HKU Libraries entry point is [Book a Space](https://lib.hku.hk/general/e-form/book-a-space.html). The actual booking system is hosted at `https://booking.lib.hku.hk/`. After login, the Study Table search page is `Secure/FacilityStatusDate.aspx`.

The app selects the `Location`, `Facility Type`, and `Date` WebForms fields and continues through the available slot and seat pages. Slot rules are location-specific:

| Location | Time slots |
| --- | --- |
| Law Library Study Table | `09:00-13:00`, `13:00-18:00`, `18:00-21:00` |
| Chi Wah Learning Commons | Hourly sessions, including the final `23:00-23:45` session |
| Other unconfigured locations | Generic editable presets or a custom time range |

HKU does not publish one permanent clock time that applies to every library, semester, and booking period. The app therefore defaults to `00:00` in Hong Kong time and makes the release time configurable.

Always verify the current rules in the HKU booking system. Availability, opening times, and page structure may change without notice.

## Installation

No .NET SDK, Node.js, Python, PyQt, Electron, or third-party NuGet package is required. A typical Windows 10/11 installation includes:

- Windows PowerShell 5.x
- .NET Framework 4.x
- The built-in C# compiler at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
- Windows Forms, System.Drawing, System.Security, and System.Runtime.Serialization

Installation steps:

1. Download or clone the complete `hku-library-seat-booking` project.
2. Run `bin\HkuLibrarySeatBooking.exe` directly, or use `run.ps1` to rebuild and launch it.
3. No administrator privileges are required. The app does not install a service or driver.

Start with PowerShell:

```powershell
cd ".\hku-library-seat-booking"
powershell -NoProfile -ExecutionPolicy Bypass -File .\run.ps1
```

Build without launching:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\run.ps1 -BuildOnly
```

Run the automated self-tests:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\run.ps1 -Test
```

The build output is `bin\HkuLibrarySeatBooking.exe`.

## Usage

1. Start the application and complete the first-run settings dialog.
2. Enter the following information:

   - **HKU username:** optional when Cookie mode is enabled.
   - **Password:** optional when Cookie mode is enabled.
   - **Browser Cookie:** recommended for HKU single sign-on sessions.
   - **Target library:** for example, `Main Library 4/F Study Tables`.
   - **Target seat:** the seat identifier shown by HKU. If left empty, the app selects the first matching non-disabled seat.
   - **Time slot:** generated from the selected location. Law Library uses its three fixed sessions; Chi Wah uses hourly sessions.
   - **Daily release time:** defaults to `00:00`.
   - **Target date offset:** `1` means the day after the release date.
   - **Retry count and interval.**
   - **Window opacity.**

3. Save the settings. The floating window displays the countdown and target information.
4. About 20 seconds before release, the status changes to page preparation.
5. At the configured time, the app submits the booking and displays the result.
6. Use Settings to change the configuration, `-` to hide the window in the system tray, and `X` to exit.
7. Verify the final result in HKU `My Booking Record`.

## Getting the Browser Cookie

The recommended method is to use **Sign in with Edge** in the settings window. It opens a dedicated Edge profile for this application. Complete the HKU sign-in there, and the app automatically imports the authenticated session without requiring manual DevTools work.

The manual method below remains available as a fallback.

1. Open `https://booking.lib.hku.hk/` in Microsoft Edge and sign in manually.
2. Open a page such as `Facility Status`, `View By Date`, or `New Booking`.
3. Press `F12` and open the **Network** tab.
4. Refresh the page with `Ctrl+R`.
5. Select a request to `booking.lib.hku.hk`, such as `FacilityStatusDate.aspx`.
6. Open **Headers** and locate the **Request Headers** section.
7. Find the `Cookie:` line and copy only the value after `Cookie:`.
8. Paste the complete Cookie value into the app's browser Cookie field, keep Cookie mode enabled, and save.

The Cookie may include `ASP.NET_SessionId` and authentication cookies. Copy the complete value rather than only one cookie.

Do not send your Cookie, password, or settings file to anyone. The Cookie expires when the session ends or when HKU invalidates it.

## Booking Diagnostics HAR

The application starts a diagnostic HAR recorder two minutes before each release and saves it after the booking attempt finishes. The file is written to:

`%LOCALAPPDATA%\HKU-Library-Seat-Booking\har\`

The generated HAR includes request methods, URLs, timing, response status, safe booking fields, and network or TLS exception statuses. Cookie values, `Set-Cookie` values, passwords, ViewState, event-validation tokens, email addresses, and other personal fields are redacted. Response bodies are omitted to avoid saving personal booking data.

This HAR is different from an Edge DevTools HAR: it records requests made by the application itself, including connection failures that never receive an HTTP response. It can identify whether the client observed `ReceiveFailure`, `ConnectFailure`, or `TrustFailure`, but it does not prove which network hop sent a TCP reset. Packet capture is still required to identify the exact sender of a reset packet.

## Advanced Adaptation

The Advanced Adaptation tab is available for changes in the HKU booking page structure. Leave all fields empty for normal use.

The app matches seats, time slots, and booking controls automatically. If the page changes, inspect the actual `name` and `value` attributes in the browser developer tools and enter the corresponding seat, slot, and submit fields manually.

The application does not upload pages, Cookies, credentials, or personal information. Version `0.1.6` has no remote logging, crash reporting, email notification, or account synchronization.

## Input and Output Examples

### Example Input

| Setting | Example |
| --- | --- |
| Target library | `Main Library 4/F Study Tables` |
| Target seat | `A-17`, or blank for automatic matching |
| Time slot | Session 2, `13:00-18:00` |
| Release time | `00:00` |
| Target date offset | `1` |
| Retry policy | `5` attempts, `800` ms apart |
| Opacity | `90%` |
| Cookie | Cookie value from a signed-in HKU booking session |

### Waiting Output

The app currently uses a Chinese interface. The following is an English rendering of the status content:

```text
Waiting for release
23:37:13
Release: 11-09 00:00:00 - automatic submission enabled
Target: Main Library 4/F Study Tables - 11-10 Tue - 13:00-18:00 - A-17
Release: 11-09 00:00:00 - 5 retries
```

### Success Output

```text
Booking successful
HKU returned a successful booking result. - 11-10 13:00-18:00
Target: Main Library 4/F Study Tables - 11-10 Tue - 13:00-18:00 - A-17
```

### Failure Output

```text
Booking failed
The target date or slot is not yet available, or no selectable seat was found.
```

Actual output depends on the page returned by HKU and may not match these examples exactly.

## Project Structure

```text
hku-library-seat-booking/
|-- .gitignore
|-- LICENSE
|-- README.md
|-- run.ps1
|-- src/
|   |-- Program.cs
|   |-- SettingsForm.cs
|   |-- BookingModels.cs
|   |-- ConfigStore.cs
|   |-- ReleaseSchedule.cs
|   |-- BookingScheduler.cs
|   |-- HkuBookingClient.cs
|   |-- WebFormParser.cs
|   |-- AssemblyInfo.cs
|   `-- app.manifest
|-- tests/
|   |-- CoreTests.cs
|   `-- TestProgram.cs
`-- bin/
    `-- HkuLibrarySeatBooking.exe
```

## Required Dependencies

- Operating system: Windows 10 or Windows 11.
- Runtime: the .NET Framework 4.x included with Windows.
- Build tools: Windows PowerShell and the built-in `csc.exe`.
- System assemblies: Windows Forms, System.Drawing, System.Security, and System.Runtime.Serialization.
- Network access: access to `booking.lib.hku.hk` and a valid HKU login Cookie.
- Third-party packages: none.
- External services: no custom server, cloud database, email service, or notification service.

## Testing Scope

The automated test suite covers:

- weekday, weekend, Law Library, and Chi Wah slot mapping
- location-specific slot-list switching
- multi-sample server-clock calibration and round-trip correction
- lightweight HEAD server-clock sampling
- Chi Wah `Submit -> Yes` confirmation handling
- target-date calculation and the post-release grace window
- countdown formatting
- form and select-field detection
- ASP.NET WebForms postbacks
- explicit field overrides and login-form parsing
- configuration boundaries and encrypted secret storage
- target-summary generation
- a multi-step booking flow against a local mock server

Current result: `20/20` tests passed.

The automated test does not submit a real HKU booking and does not send settings to an external server.

## Limitations and Responsible Use

- Use the app only with your own single HKU account.
- Real submissions require a valid HKU session and a target that is available under HKU's current rules.
- The app does not guarantee seat availability or a successful booking.
- HKU may change page fields, booking windows, authentication, or availability logic without notice.
- A successful result must still be checked in HKU `My Booking Record`.
- Seats may be released if the user does not complete the required on-site check-in.
- Do not use the project for bulk booking, credential sharing, account automation, or policy violations.

## License

This project is licensed under the [MIT License](LICENSE).

## Project Status

**The project is still under testing. Seat availability and successful booking are not guaranteed. Use at your own risk and verify every result in the official HKU booking system.**
