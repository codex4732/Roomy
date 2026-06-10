# Roomy — Product Requirements Document (PRD)

| | |
|---|---|
| **Product** | Roomy — multi-tenant conference room booking |
| **Version** | 1.0 (v1 scope) |
| **Status** | Draft |
| **Last updated** | 2026-06-10 |
| **Companion doc** | [Technical Design](./technical-design.md) |

## 1. Overview

Roomy is a business-to-business application that lets organizations manage their conference rooms and lets their employees find and book those rooms. It is **multi-tenant**: each customer organization is a fully isolated tenant with its own users, locations, rooms, and policies. Tenants never see each other's data.

Roomy ships in two forms from a single codebase:

1. **Cloud SaaS** — one shared deployment operated by us, hosting all cloud tenants.
2. **Self-hosted** — a packaged distribution customers run inside their own network. A self-hosted deployment is also multi-tenant-capable (e.g., divisions or subsidiaries as tenants), even though many installs will run a single tenant.

The core problem Roomy solves: rooms are double-booked, ghost-booked (reserved but never used), and hard to find. Roomy addresses all three with conflict-free booking, configurable booking policies with optional approval, and check-in with automatic release of unused rooms.

## 2. Goals

- **G1** — Employees can find an available, suitable room and book it in under 30 seconds.
- **G2** — Zero double-bookings, guaranteed by the system even under concurrent demand.
- **G3** — Reclaim ghost bookings: rooms not checked into are automatically released for others.
- **G4** — Give facility teams control: approval workflows, booking windows, duration limits, quotas, and blackout periods.
- **G5** — Full tenant isolation with low operational overhead at the target scale (≤100 tenants).
- **G6** — One codebase serves both cloud SaaS and self-hosted deployments.

### Non-goals (v1)

Explicitly **out of scope** for v1, captured here so they shape the design without being built:

- **Calendar integration** (Google Calendar / Microsoft 365 sync, room resource mailboxes, attendee invites) — highest-priority fast-follow; the data model must not preclude it.
- **Native mobile apps** — the web app must be fully usable on mobile browsers instead.
- **Billing and self-serve signup** — tenants are provisioned by the platform operator. No plan tiers or payment flows in v1.
- **SSO via SAML/OIDC** — LDAP is the v1 directory integration; web SSO is future work.
- **Custom roles / granular RBAC** — v1 ships four fixed roles.
- **Visitor management, desk/hot-desk booking, catering/equipment ordering.**

## 3. Personas and roles

v1 defines four fixed roles. Roles 2–4 are scoped to a single tenant.

| # | Role | Who they are | What they can do |
|---|------|--------------|------------------|
| 1 | **Platform Operator** | Us (cloud) or the customer's IT team (self-hosted) | Provision/suspend tenants, assign the first Tenant Admin, view platform health. **Cannot read tenant business data** (bookings, rooms, users beyond the initial admin contact). |
| 2 | **Tenant Admin** | IT/office manager at the customer | Everything within their tenant: manage users and roles, locations, rooms, policies, LDAP settings, branding, kiosk devices. Implicitly has all Facility Manager capabilities for every location. |
| 3 | **Facility Manager** | Front-desk / facilities staff | Assigned per location. Manage rooms and amenities, approve/decline booking requests, manage blackout periods, override/cancel any booking, register kiosk devices — for their assigned locations only. |
| 4 | **Member** | Any employee | Search rooms, view availability, create/edit/cancel their own bookings, check in, view their booking history. |

A user has exactly one role. Every user belongs to exactly one tenant (a person at two organizations has two separate accounts).

## 4. Tenancy model

- **Tenant** = one customer organization. Tenants are created, suspended, and reactivated by the Platform Operator.
- Each tenant has: a unique **slug** (used as the subdomain in cloud, e.g. `acme.roomy.app`, and as a URL path or selector when self-hosted), a display name, a logo, a default locale, and tenant-level default policies.
- **Isolation requirement:** no API call, search result, notification, or report may ever expose one tenant's data to another tenant's user. This is a hard requirement enforced in depth (see technical design §4).
- Suspending a tenant immediately blocks all logins and kiosk traffic for that tenant; data is retained and restorable.

## 5. Functional requirements

Requirement IDs (`FR-x.y`) are referenced by the technical design and should be referenced by implementation tickets.

### 5.1 Tenant and user management

- **FR-1.1** Platform Operator can create a tenant (name, slug, initial Tenant Admin email) and suspend/reactivate it.
- **FR-1.2** Tenant Admin can invite users by email; invitees set a password via a time-limited link (72 h).
- **FR-1.3** Tenant Admin can change a user's role, deactivate, and reactivate users. Deactivation blocks login immediately; the user's future bookings are cancelled with notification to the organizer... unless the admin opts to keep them.
- **FR-1.4** Local authentication (email + password) is the default: email verification on signup, secure password reset, configurable password policy, account lockout after repeated failures.
- **FR-1.5 LDAP (optional, per tenant)** Tenant Admin can configure an LDAP/Active Directory connection (host, port, LDAPS/StartTLS, bind credentials, user base DN, user filter, attribute mapping, optional group→role mapping). When enabled:
  - Users authenticate against the tenant's directory; a local account record is created/updated on first successful login (just-in-time provisioning).
  - A "Test connection" action validates settings before saving.
  - Local accounts remain available as fallback; the Tenant Admin account can always log in locally (break-glass).
  - Self-hosted deployments may reach an on-network directory directly; cloud tenants must expose LDAPS over the network (documented requirement, not solved by v1 tooling).
- **FR-1.6** All administrative actions (user, role, policy, room, tenant changes) are recorded in a per-tenant audit log viewable by the Tenant Admin.

### 5.2 Locations and rooms

- **FR-2.1** Tenant Admin manages **locations** (name, address, IANA time zone, business hours per weekday). All times for a room are interpreted in its location's time zone.
- **FR-2.2** Facility Managers/Tenant Admin manage **rooms** within a location: name, floor/area, capacity, photo, description, amenities (from a tenant-managed amenity list, e.g. video conferencing, whiteboard, phone), and status (active / temporarily unavailable / retired).
- **FR-2.3** Marking a room temporarily unavailable or retired cancels (with notification) or blocks affected future bookings; the user performing the action sees the list of affected bookings before confirming.
- **FR-2.4** Each room has a stable, regenerable **QR code** linking to its check-in/booking page (see FR-6).

### 5.3 Search and availability

- **FR-3.1** Members can browse availability by location + date as a day/week timeline grid per room.
- **FR-3.2** Members can search: "rooms for ≥ N people, at location L, with amenities A, free from T1 to T2", results sorted by best fit (smallest sufficient capacity first).
- **FR-3.3** Availability shown reflects confirmed and pending bookings, blackout periods, business hours, and room status. Pending-approval slots are visibly "tentative".

### 5.4 Booking lifecycle

- **FR-4.1** A booking has: room, organizer, title (visible per tenant privacy setting: full title vs "Booked"), optional description, start/end time, attendee count (≤ room capacity, warning not block), and state.
- **FR-4.2 States:** `PendingApproval → Confirmed → CheckedIn → Completed`, with exits to `Declined`, `Cancelled`, `AutoReleased` (no-show). Bookings on rooms without approval start at `Confirmed`. The full state machine is normative in the technical design §6.3.
- **FR-4.3** **No double-booking:** two bookings on the same room may never overlap in time (`Confirmed`, `PendingApproval`, and `CheckedIn` states all block the slot). This must hold under concurrent submission.
- **FR-4.4** Organizers can edit time/title/attendees or cancel their own future bookings. Editing the time of an approved booking on an approval-required room re-enters approval. Facility Managers can cancel or move any booking in their locations (organizer is notified with a reason).
- **FR-4.5** Booking times snap to 15-minute increments. Minimum duration 15 min; maximum per policy (FR-5).
- **FR-4.6** Same-slot contention resolves first-committed-wins; the loser gets an immediate, clear conflict error with nearest-alternative suggestions (same room different time, or different room same time).

### 5.5 Recurring bookings

- **FR-4.7** Members can create a recurring series: daily, weekly (with weekday selection), or monthly (nth weekday or day-of-month), with an end date or occurrence count. Maximum series horizon: **6 months** ahead (tenant-configurable down).
- **FR-4.8** At creation, the system reports conflicts per occurrence and lets the user choose: (a) book only conflict-free occurrences, or (b) abort. Partial series clearly list skipped dates.
- **FR-4.9** Users can modify or cancel a **single occurrence** (becomes an exception) or the **whole remaining series** ("this and following").
- **FR-4.10** Occurrence times follow the location's local wall-clock time across DST transitions (a 09:00 weekly meeting stays at 09:00 local year-round).
- **FR-4.11** On an approval-required room, the series is approved/declined as one unit by the approver; occurrence-level exceptions then follow the normal single-booking rules.

### 5.6 Approvals and booking policies

- **FR-5.1** Per room: **requires approval** flag. Requests notify all Facility Managers of the location; any one may approve or decline (with optional reason). Decisions notify the organizer.
- **FR-5.2** Pending requests **auto-expire**: declined automatically if not decided by *min(48 h after submission, booking start time)*.
- **FR-5.3** Policies configurable at tenant level with per-location and per-room overrides (most specific wins):
  - **Booking window** — how far in advance bookings may start (default 60 days).
  - **Max duration** per booking (default 8 h) and **min duration** (default 15 min).
  - **Quota** — max active (future, not-cancelled) bookings per Member (default 10; series occurrences each count).
  - **Business hours enforcement** — bookings only within location business hours (default on).
- **FR-5.4** **Blackout periods** — Facility Managers can block a room, a set of rooms, or a whole location for a time range with a reason (e.g., maintenance, holidays). Creating a blackout shows affected existing bookings and cancels them with notification upon confirmation.
- **FR-5.5** Policy violations are rejected at submission with a human-readable reason naming the violated policy.

### 5.7 Check-in and auto-release

- **FR-6.1** Bookings on rooms with check-in enabled (per-room flag, default on) must be checked in between **10 min before start** and **grace period after start** (tenant-configurable grace, default 10 min, range 5–30).
- **FR-6.2** Check-in channels: (a) web app button on the booking, (b) scanning the room QR code (deep-links to the check-in action, validates the user has the active booking), (c) tapping on the room kiosk.
- **FR-6.3** If not checked in by the grace deadline, the booking transitions to `AutoReleased`: the slot is freed immediately, the organizer is notified, and a **no-show** is recorded against the organizer.
- **FR-6.4** Ending early: a checked-in user can end the meeting from web or kiosk, freeing the remainder of the slot.
- **FR-6.5** No-show counts per user are visible to Facility Managers and Tenant Admin (reporting only in v1; automatic sanctions are future work).
- **FR-6.6** Check-in applies per occurrence for recurring bookings.

### 5.8 Room kiosk (door display)

- **FR-7.1** A kiosk is a tablet running the Roomy kiosk web app in fullscreen, bound to exactly one room. Facility Managers register a device with a short-lived pairing code shown on the device; thereafter the device authenticates itself without a user session.
- **FR-7.2** Always-on display shows: room name, current status (color-coded: **green** free / **red** occupied / **amber** starting soon or awaiting check-in), current meeting (title per privacy setting + time), and the next bookings today.
- **FR-7.3** Kiosk actions: **Check in** (during the check-in window), **End meeting**, and **Book now** — an ad-hoc instant booking for 15/30/60 min if the room is free, attributed to a user who identifies via a short personal PIN or by scanning their personal QR from the web app. Anonymous ad-hoc booking is allowed if the tenant enables it (booking is attributed to "Walk-up").
- **FR-7.4** Kiosk reflects state changes (new booking, cancellation, auto-release) within **5 seconds**.
- **FR-7.5** Kiosks operate read-only from cache during short network outages and show a clear offline indicator; actions are disabled while offline.
- **FR-7.6** Facility Managers can see kiosk health (last-seen, app version) and revoke a device instantly.

### 5.9 Notifications

- **FR-8.1** Email notifications (per-user opt-out by category): booking confirmed/declined, approval requested (to approvers), upcoming booking with check-in reminder (15 min before start), auto-release/no-show, booking cancelled/moved by staff, blackout-caused cancellation.
- **FR-8.2** In-app notification center with the same events; unread badge.
- **FR-8.3** Self-hosted deployments configure their own SMTP relay; cloud uses the platform's email service. All email templates carry tenant branding (logo, name).

### 5.10 Reporting

- **FR-9.1** Tenant Admin and Facility Managers see per-location dashboards: room utilization % (booked time ÷ business hours), bookings per room, peak hours heat map, no-show rate, auto-release count, approval turnaround time. Filterable by date range; per-location for Facility Managers, tenant-wide for Tenant Admin.
- **FR-9.2** Export any report view as CSV.

## 6. Key user stories and acceptance criteria

Representative stories; the FR sections above are the complete requirement set.

**US-1 — Book a room fast (G1)**
*As a Member, I want to find a free room for 6 people with video conferencing this afternoon.*
- Given rooms exist matching the filters, when I search, results appear in < 2 s sorted by best capacity fit, showing free slots this afternoon.
- When I pick a slot and confirm, the booking is `Confirmed` (or `PendingApproval` with that clearly stated) and I receive a confirmation email within 1 min.

**US-2 — Concurrent booking race (G2)**
*Two Members submit overlapping bookings for the same room within the same second.*
- Exactly one booking succeeds. The other user immediately sees a conflict message with at least one alternative suggestion. No partial/phantom booking exists afterward.

**US-3 — Ghost booking reclaimed (G3)**
*A Member books 09:00–10:00 but never shows. Grace period is 10 min.*
- At 09:10 the booking becomes `AutoReleased`, the kiosk turns green within 5 s, a no-show is recorded, and the organizer gets a notification. Another Member can immediately book 09:15–10:00.

**US-4 — Approval flow (G4)**
*A Member requests the board room (approval required) for next Tuesday.*
- All Facility Managers of that location are notified. The first to act decides; the other sees the request as already handled. The slot shows "tentative" to others until decided. If nobody acts within 48 h, the request auto-declines and the Member is notified.

**US-5 — Recurring with conflicts**
*A Member books "every Monday 09:00–09:30 for 12 weeks" but weeks 5 and 9 conflict.*
- Before confirming, I see exactly which 2 dates conflict and choose to book the other 10. The series view lists 10 booked occurrences and 2 skipped dates.

**US-6 — LDAP login**
*A tenant has enabled LDAP. An employee who has never used Roomy logs in with their directory credentials.*
- Login succeeds against the directory, an account is auto-provisioned with the Member role (or the role mapped from their directory group), and they land on the booking page. With LDAP unreachable, a clear "directory unavailable" error appears and local fallback accounts still work.

**US-7 — Tenant isolation (G5)**
*A user from tenant A crafts API requests using IDs belonging to tenant B.*
- Every such request returns 404 (not 403 — existence is not disclosed). Search, availability, notifications, and reports never include tenant B data. Verified by automated cross-tenant tests in CI.

## 7. Non-functional requirements

| Area | Requirement |
|---|---|
| **Scale** | ≤100 tenants; up to 2,000 users and 500 rooms per tenant; ~50,000 users platform-wide; up to 10 bookings/sec sustained platform-wide at peak. Single-region deployment is acceptable. |
| **Performance** | Availability/search queries p95 < 500 ms; booking creation p95 < 1 s; kiosk state propagation < 5 s (FR-7.4). |
| **Availability** | Cloud SaaS target 99.5% monthly. Planned maintenance windows announced ≥48 h ahead. Kiosk degrades gracefully offline (FR-7.5). |
| **Security** | Tenant isolation enforced at the database layer in addition to application code (defense in depth). All traffic TLS. Passwords hashed with a modern KDF. LDAP bind credentials encrypted at rest. OWASP ASVS L2 as the review baseline. Per-tenant audit log (FR-1.6). |
| **Privacy** | Booking titles maskable per tenant (FR-4.1). Personal data limited to name, email, hashed credentials, bookings. User deletion anonymizes historical bookings rather than breaking reports. Data export per tenant on request. |
| **Time zones** | All times stored as UTC instants; recurrence rules evaluated in the location's IANA time zone; DST-safe (FR-4.10). Users see times in the location's time zone with their own zone shown when it differs. |
| **Accessibility** | WCAG 2.1 AA for the Member-facing web app and kiosk. |
| **Localization** | v1 ships English only, but all user-facing strings externalized; dates/times localized via locale settings. |
| **Browsers** | Last 2 versions of Chrome, Edge, Firefox, Safari; kiosk targets Chrome/WebView on Android tablets and Safari on iPad. |
| **Self-host parity** | Every v1 feature works self-hosted with no dependency on third-party SaaS (email via customer SMTP, no external auth service). |

## 8. Release scope and roadmap

**v1 (this spec):** everything in §5–§7.

**Fast-follows (designed-for, not built):** Microsoft 365/Google Calendar two-way sync; SAML/OIDC SSO; plan limits + billing; escalating no-show policies; desk booking; custom roles; occupancy sensors as a check-in source.

## 9. Open questions

| # | Question | Owner | Due |
|---|----------|-------|-----|
| OQ-1 | Cloud LDAP connectivity: do we need a connector agent for tenants unwilling to expose LDAPS publicly, and is that v1.x or v2? | Product | Before GA |
| OQ-2 | Kiosk hardware guidance: do we certify specific tablet models and publish a mounting/provisioning guide? | Product/Support | Before GA |
| OQ-3 | Anonymous walk-up bookings (FR-7.3): default on or off for new tenants? | Product | Before beta |
| OQ-4 | Data retention default for completed bookings and audit logs (proposal: 24 months, tenant-configurable)? | Product/Legal | Before GA |
