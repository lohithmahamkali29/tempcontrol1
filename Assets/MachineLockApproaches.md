# Machine Lock Approaches for `TempControl`

## Purpose
This note compares two offline software-copy protection approaches for the `TempControl` desktop application.

The goal is:
- the software should work on the intended target PC
- copying the published `.exe` or installed files to another PC should not allow normal use
- the protection should remain hidden from end users
- the target PC may not have internet access

---

# Approach 1: Hardcoded MAC Address Check

## Summary
In this approach, a MAC address of the target system is hardcoded into the application before publishing.

At runtime, the app reads the current PC's MAC address and compares it with the hardcoded one.

If they match:
- app runs

If they do not match:
- app exits or blocks startup

---

## How this would be implemented

### Deployment flow
1. identify the target PC network adapter MAC address
2. place that MAC address in the application code as a constant or configuration value
3. publish the application as `.exe` / installer
4. distribute only the built output, not the source code
5. on startup, the app checks the current machine MAC against the hardcoded MAC

### Runtime logic
1. app starts
2. app reads one or more current adapter MAC addresses
3. app compares them against the hardcoded MAC
4. if match, continue startup
5. if no match, stop app

### Where it would fit in this project
The check should happen before opening `MainWindow`, meaning in `App.xaml.cs` startup flow, not in `ModbusPollingService`.

---

## Steps

### 1. How to collect the MAC addresses from the target PC
The simplest ways are:

#### Option A: Command Prompt
Run:

`ipconfig /all`

Then note the `Physical Address` of the real network adapters.

#### Option B: PowerShell
Run:

`Get-NetAdapter | Format-Table Name, Status, MacAddress`

This is usually easier to read.

### 2. Which MAC addresses should be collected
Prefer collecting MAC addresses for:
- the physical Ethernet adapter
- the physical Wi-Fi adapter, if the PC has one

Avoid using MAC addresses from:
- Bluetooth adapters
- VPN adapters
- virtual adapters
- Hyper-V / VMware adapters
- temporary USB network adapters unless that is the actual permanent adapter used on the PC

### 3. Why two MAC addresses are preferred instead of one
We prefer storing two MAC addresses instead of one because:
- some target PCs use Ethernet sometimes and Wi-Fi at other times
- one adapter may be disabled during startup
- one adapter may fail or not be detected temporarily
- using two real physical adapter MACs reduces accidental lockout on the same machine

So the recommended simple version of the MAC approach is:
- store an allowed list of 2 MAC addresses from the same target PC
- allow startup if any one of them matches

### 4. In which file should the MAC addresses be added
They should be added in:

- `App.xaml.cs`

This is the correct file because startup validation must happen before the UI opens.

### 5. In which line/file should startup flow be changed
Based on the current project:

#### `App.xaml`
- currently `StartupUri="MainWindow.xaml"` is on line `5`
- this direct startup should be removed when MAC-based validation is implemented

Reason:
- if `StartupUri` remains, the app opens `MainWindow` automatically before custom validation logic can fully control startup

#### `App.xaml.cs`
- the `App` class currently starts at line `10`
- the MAC list would be added inside this class
- the startup validation logic would also be added inside this class

Practical placement:
- add the hardcoded allowed MAC list near the top of the `App` class
- add startup validation logic in the same file before the main window is created and shown

### 6. Simple implementation shape
The intended logic is:

1. app starts758
2. read current MAC addresses from the machine
3. normalize the MAC format
4. compare against the hardcoded allowed MAC list
5. if any match, create and show `MainWindow`
6. if no match, stop the app

### 7. Recommended simple rule for this project
If the team wants the easiest version of the MAC approach, use this rule:

- publish one dedicated build for one target PC
- hardcode 2 allowed physical MAC addresses from that PC
- validate in `App.xaml.cs`
- do not place this logic in `ModbusPollingService`, view models, or page code

---

## Advantages
- very simple concept
- quick to explain
- fast to prototype
- no server required
- works offline
- no visible activation step for user

---

## Disadvantages
- MAC address is not a stable machine identity
- a PC may have multiple MAC addresses
- Ethernet, Wi-Fi, VPN, virtual adapters, and dock adapters create ambiguity
- MAC address can change after hardware replacement or network changes
- MAC address can be spoofed
- valid customer PC may fail later if NIC changes
- weak against reverse engineering / patching
- relies on one single identifier only

---

## Risks
- wrong adapter selected during check
- target PC fails after network hardware change
- support issues during deployment
- protection can be bypassed more easily than stronger approaches

---

## Overall judgment
This approach is simple, but weak and fragile.

It may work for a narrow test setup, but it is not a strong or reliable production approach for machine-bound protection.

---

# Approach 2: Hardware Fingerprint + Local Machine-Bound Token

## Summary
In this approach, the application generates a machine identity from multiple hardware or system values. This identity is called a **hardware fingerprint**.

A hidden local token is then created for that fingerprint.

At runtime, the app:
- generates the current fingerprint
- reads the stored token
- verifies that the token belongs to this machine
- runs only if both match

---

## What is a hardware fingerprint?
A hardware fingerprint is a generated machine ID created from multiple machine values, for example:
- Windows `MachineGuid`
- BIOS serial
- motherboard serial
- disk / system drive information
- CPU or system information

These values are combined and hashed to produce one machine fingerprint.

### Simple meaning
- fingerprint = machine identity

---

## What is a local token?
The token is a hidden machine approval record stored on the target PC.

It can be stored in:
- `ProgramData`
- Windows Registry
- protected app data

It should contain information such as:
- product/app identifier
- approved machine fingerprint
- issue date
- optional expiry or version info
- signature or protected validation data

### Simple meaning
- token = permission for that machine

---

## How this would be implemented

### Deployment flow
1. install app on target PC or run a helper tool
2. application/helper reads machine values
3. application/helper generates hardware fingerprint
4. internal deployment tool creates a token for that fingerprint
5. token is copied/stored on the target PC
6. app validates token on every startup

### Runtime logic
1. app starts
2. app generates current machine fingerprint
3. app loads the local token
4. app verifies token validity
5. app compares token fingerprint with current fingerprint
6. if match, continue startup
7. if mismatch, stop app

### Where it would fit in this project
This check should be placed in `App.xaml.cs` before `MainWindow` is opened.

Since the app currently starts directly with `StartupUri="MainWindow.xaml"`, this startup flow would eventually need to be controlled from code.

---

## Advantages
- much stronger than MAC-only check
- works offline
- hidden from end users
- machine binding remains effective even if app files are copied
- copying the token to another PC should still fail if fingerprint does not match
- more stable because it uses multiple machine identifiers instead of one
- can be extended later if needed
- better long-term maintainability

---

## Disadvantages
- more implementation effort than MAC-only
- requires a small internal process for token creation
- hardware replacement on target PC may require re-issuing token
- care is needed when choosing fingerprint inputs so it is not too strict or too loose

---

## Risks
- if fingerprint uses unstable fields, valid machine may fail after service changes
- if token is implemented weakly, copying may still work
- if the app itself can freely generate valid tokens, protection becomes ineffective

---

## Hardening options
To improve this approach further:
- sign the token so only your team can issue valid tokens
- protect token locally using Windows `DPAPI`
- store token in `ProgramData` or Registry instead of next to the `.exe`
- validate at every startup before opening UI

---

## Overall judgment
This is the recommended offline protection approach for this project.

It is stronger, more reliable, and more professional than a MAC-only check.

---

# Comparison Table

| Area | Hardcoded MAC Check | Hardware Fingerprint + Token |
|---|---|---|
| Works offline | Yes | Yes |
| Hidden from user | Yes | Yes |
| Easy to prototype | Yes | Medium |
| Reliable after hardware/network changes | Low | Better |
| Security strength | Low | Medium to Strong |
| Handles copied `.exe` better | Low | Better |
| Handles copied token better | Not applicable / weak | Better if machine-bound |
| Maintainability | Low | Better |
| Recommended for production | No | Yes |

---

# Why Approach 2 is better than Approach 1

## 1. It does not depend on one unstable value
A MAC address is only one identifier and is easy to disturb.

A hardware fingerprint uses multiple values, so it is more dependable.

## 2. It is harder to bypass by simple copying
With MAC-only, the check is simpler and weaker.

With fingerprint + token, the app requires both:
- correct machine identity
- correct local approval token

## 3. It is better for offline deployment
Since the target PC has no internet, a machine-bound token is a practical offline solution.

## 4. It is more suitable for long-term support
If designed properly, it gives better control and clearer re-issue handling than a single hardcoded MAC value.

---

# Recommended direction for `TempControl`

## Recommendation
Use **Approach 2: Hardware Fingerprint + Local Machine-Bound Token**.

### Suggested model
- generate combined hardware fingerprint on target PC
- create internal token for that fingerprint
- store token in hidden/protected local location
- validate token at every startup before opening `MainWindow`

### Optional strengthening
- sign token with internal private key
- verify signature inside app with embedded public key
- protect local token using `DPAPI`

---

# Suggested implementation direction in this project

## Startup protection point
This protection should be implemented in startup flow, not in UI pages or `ModbusPollingService`.

### Best place
- `App.xaml.cs`

### Reason
The app should validate machine authorization **before** the main window and business logic are allowed to run.

---

# Final conclusion
For this offline WPF project, a hardcoded MAC check is a quick but weak solution. A hardware fingerprint plus machine-bound local token is the better approach because it is stronger, more reliable, and better suited for hidden offline deployment protection.

If a solution must be shown for discussion with management or senior developers:
- MAC-only can be presented as the simple baseline idea
- hardware fingerprint + token should be presented as the recommended production approach

---

# Change Log for This Chat

This section records what was changed during this chat so another developer or senior reviewer can quickly understand what was updated and what was left untouched.

## Documentation changes made

### 1. `Assets\DeploymentNotes.md`
Created as a deployment-oriented project note.

It includes:
- project summary
- runtime flow
- configured COM ports and TCP endpoint
- current Modbus status
- files involved in the project
- deployment caveats and known risks

### 2. `Assets\MachineLockApproaches.md`
Created and later updated.

It now includes:
- the 2 machine-lock approaches
- implementation summary for both approaches
- advantages and disadvantages
- comparison table
- recommendation section
- MAC-only `Steps` section
- how to collect MAC addresses
- which MAC addresses to use and avoid
- why 2 MAC addresses are preferred instead of 1
- where the MAC-check implementation would go in this project
- which files and current line references are involved

## What was added later to this file
Compared to the earlier version of this document, the following was added:

- a new `Steps` section under `Approach 1: Hardcoded MAC Address Check`
- instructions for collecting MAC addresses using `ipconfig /all`
- instructions for collecting MAC addresses using `Get-NetAdapter`
- guidance on physical adapters versus virtual/VPN adapters
- explanation of why 2 MAC addresses are preferred instead of 1
- explicit project placement notes for `App.xaml` and `App.xaml.cs`

## What was not changed in application code
During these documentation edits, the actual application logic was **not** changed.

No runtime code changes were made in:
- `App.xaml`
- `App.xaml.cs`
- `MainWindow.xaml`
- `Services\ModbusPollingService.cs`
- `Services\DatabaseService.cs`
- `Services\PlcDataStore.cs`
- any `ViewModels`
- any `Views`

## Current status after these edits
- machine-lock approach is **documented only**
- MAC-based startup validation is **not implemented yet**
- hardware fingerprint/token validation is **not implemented yet**
- current app behavior remains unchanged

## Review note for senior developers
If a senior developer starts implementation from this point:

- the documentation explains the intended MAC-only implementation path
- the minimum startup files to modify are `App.xaml` and `App.xaml.cs`
- no existing business logic was changed as part of these documentation edits

## Documentation rule followed from this point onward
For edits made in this chat, the intent is to document:
- what changed
- what did not change
- whether the change was documentation-only or code-related
