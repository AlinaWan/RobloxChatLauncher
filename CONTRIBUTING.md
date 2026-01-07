# Contributing to Roblox Chat Launcher

Thank you for your interest in contributing to **Roblox Chat Launcher**.
Before submitting issues, feedback, or code, please read this document carefully. This project has **strong philosophical and technical boundaries by design**.

---

## Project Philosophy

Roblox Chat Launcher exists because **Roblox removed in-game chat for everyone who does not submit facial images or government-issued identification**.

This project is a response to that decision — not as an act of hostility, but as a **privacy-preserving alternative** that restores basic social interaction without requiring players to surrender highly sensitive biometric or identity data.

### Privacy First — Always

This project is built on the following principles:

* **No biometric data**
* **No government IDs**
* **No persistent user identities**
* **No tracking across sessions**
* **No ability to identify who a user is in real life**

Currently, the proof-of-concept server **hashes the client IP address with a server-side secret (salt)** solely to determine whether multiple messages originate from the same machine during a session.

Important clarifications:

* The hash **cannot be reasonably reversed**
* The salt is **never exposed**
* The resulting identifier **cannot be tied to a real person**
* No usernames, accounts, emails, or device fingerprints are collected
* The system does **not** and **must not** attempt to identify users

If a contribution meaningfully weakens these guarantees, it will not be accepted.

---

## Scope of the Project

This repository is a **proof of concept**, not a finished product.

Its goals are to demonstrate:

* A **non-invasive**, native-feeling chat overlay
* Compatibility with Roblox **without injection or memory access**
* Keyboard passthrough that preserves muscle memory
* A communication layer that does **not** depend on Roblox’s chat systems

It is **not** intended to:

* Replace Discord globally
* Become a social network
* Introduce accounts, profiles, or friend lists
* Store chat history long-term
* Circumvent Roblox security through unsafe or exploitative methods

---

## What Contributions Are Welcome

We welcome contributions that **align with the philosophy above**, including:

### Code Contributions

* Bug fixes
* Performance improvements
* Input handling refinements
* Overlay UX / UI polish
* Accessibility improvements
* Safer or cleaner Win32 / .NET implementations
* Refactoring for clarity or maintainability
* Improved error handling and logging (without adding telemetry)

### Documentation & Feedback

* Improving README clarity
* Safer installation instructions
* Better explanations of technical decisions
* Diagrams or demos explaining the PoC
* Thoughtful architectural suggestions (even if not implemented)

### Security & Abuse Prevention Ideas

* Rate limiting strategies
* Validation improvements
* Anti-spam or abuse mitigation **that does not rely on user identity**
* Network-level protections that preserve anonymity

---

## What Contributions Will NOT Be Accepted

To be explicit, the following categories of contributions will be rejected:

* ❌ Roblox injection, DLL injection, or code hooking
* ❌ Reading or modifying Roblox process memory
* ❌ Network traffic interception or packet inspection
* ❌ Persistent user identifiers or accounts
* ❌ Device fingerprinting
* ❌ Adding telemetry, analytics, or tracking
* ❌ Any feature requiring real-world identity
* ❌ Anything that meaningfully increases legal or safety risk to users

This project prioritizes **safety, legality, and user trust** over features.

---

## Code Style & Expectations

If you submit code:

* Keep changes **focused and reviewable**
* Avoid unrelated refactors in the same PR
* Comment non-obvious Win32, registry, or input-handling logic
* Prefer clarity over cleverness
* Avoid introducing new dependencies unless clearly justified
* Ensure the launcher remains **non-invasive**

If you’re unsure whether an approach fits the project, open an issue or discussion first.

---

## Licensing

This project is licensed under **GNU GPL v3.0**.

By contributing code, you agree that:

* Your contribution is licensed under GPL v3.0
* You have the right to submit the code
* You understand that derivative works must remain GPL-compliant

---

## Maintainer Discretion

This project has a clear vision.

The maintainer reserves the right to:

* Reject contributions that conflict with project philosophy
* Decline features that expand scope unnecessarily
* Prioritize user safety and privacy over convenience

This is not a judgment of contributor intent — it is a safeguard for users.

---

## Final Note

If you are contributing because you believe **players should not be forced to trade biometric data for basic communication**, you’re in the right place.

Even small improvements help demonstrate that **privacy-respecting alternatives are possible**.

Thank you for contributing.
