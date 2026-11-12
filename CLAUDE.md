# Working on Home Budget

Notes for anyone (person or AI assistant) making changes in this repo.

## Keep the documentation current

Documentation lives in two places, and both are part of "done":

- **This repo:** `README.md` (running locally, sign-in set-up, build and test), `infra/README.md` (Azure set-up),
  `tests/e2e/README.md`, and the README in `src/Budget.Server/Features/`.
- **Confluence:** space SD, under the Home Budget folder, on roughcommon.atlassian.net.

**Any change that affects what a page describes updates that page in the same piece of work**, before the PR is
merged: a new feature, a renamed screen, a changed rule, a new setting, a new environment variable, a format
version. Don't leave it for later. If a decision is made in conversation, write it down where it belongs.

| When you change… | Update |
| --- | --- |
| How to run, build or test locally; local secrets; dev tunnels | README.md and Confluence **Local Development** (286621710) |
| Sign-in providers or their set-up | README.md, Local Development, **Deploying to Azure** (286883842), ADR-002 note |
| Azure resources, Bicep, the deploy workflow, GitHub variables or secrets | infra/README.md and **Deploying to Azure** |
| Logging, telemetry, health checks, correlation ids | **Monitoring with Application Insights** (289308673) and Architecture Overview |
| Projects, layers, hosting, auth, multi-tenancy, PWA behaviour, navigation | **Architecture Overview** (285638657) |
| Entities, fields, rules or relationships | **Domain Model** (285671425) |
| Who can do what | **Roles & Permissions** (285704193) |
| A term people will see or use | **Glossary** (285736961) |
| The export/import format (any contract change bumps `schemaVersion`) | **Data Export & Import Format** (287571969) |
| What the app does, or what's in or out of scope | **Project Overview** (285605889) |
| Ideas for later, or the status of ones listed there | **Future Ideas (to discuss)** (287899649) |
| A UI pattern, layout rule or wording convention | **UI Conventions** (289603597) and the *UI conventions* section below |
| An architectural decision | A new ADR page under **Architecture Decision Records** (285769729), plus a row in its table |

**ADRs are records of a decision.** Don't rewrite one when things change later: add a dated "Update" panel at the
top saying what changed and where, or write a new ADR that supersedes it and update both statuses.

When editing Confluence pages with panels, dates, status lozenges or page links, edit the specific parts rather
than replacing the whole page, so nothing is lost.

## How work is done

- Work is tracked in Jira project **BUD**. Each change has a story; mark it In Progress when starting and Done when
  its PR is merged.
- Each change goes on its own branch with a pull request. CI (build, tests, end-to-end) runs on the PR; the owner
  merges.
- Secrets never go in the repo: user secrets locally, GitHub secrets for Azure.
- The Content Security Policy (`Hosting/SecurityHeaders.cs`) allows only the app's own files: no inline scripts or
  event handlers, and no scripts, fonts, images or API calls from other sites. Put scripts in `wwwroot/js`; if something
  truly needs another origin, change the policy deliberately and say why. Every e2e test fails on a policy violation.
- Server code is organised by feature; see `src/Budget.Server/Features/README.md`.
- Styles for one page or component go in its own scoped `.razor.css` file (use `::deep` for MudBlazor's elements).
  `wwwroot/css/app.css` is only for app-wide rules, so features don't all add to the end of one file.

## UI conventions

Decided while reviewing the app on phones and desktops (BUD-108, BUD-121 to BUD-135). Follow them in new work, reuse
the shared components rather than building new versions, and check a new page against them on a 360px-wide phone as
well as a desktop. The UI Conventions page in Confluence has the reasons and examples.

**Phones first, larger screens keep the fuller layout.** Compact variants (icon-only buttons with `icon-only-xs`,
short labels, a Filter panel) are for phones only (below 600px); larger screens keep full labels and inline controls.

**Navigation**
- Drawer sections Home / Planning / Money / Household; phone bar Home, Budget, Income, Outgoings, More (`NavItems.cs`).
- Back means back: links to another page carry `?from=` via `BackLink`, and its back button returns there.

**Toolbars and choices**
- A page's controls sit in one `page-toolbar` that stays on one line on a phone: toggles on the left, a menu taking
  the rest of the width. A lone menu starts on the left. Controls on a line are centred on one another with a gap
  (e2e `expectToolbarTidy`).
- Choices and filters are compact menus (`FilterMenu`): "Label: value ▾", the current choice ticked. A filter is
  outlined while it shows everything and filled while it narrows the list; a choice that isn't a filter (e.g.
  grouping) is never highlighted.
- Menu and button text never wraps: it's cut short with an ellipsis (`toolbar-menu` in app.css). Use MudBlazor's own
  menu button; don't put a button inside `ActivatorContent` (it won't open the menu and nests controls).
- Menu option test ids are `{menu-test-id}-option`. Menus' popovers render outside the page, so their styles go in
  app.css (e.g. `.period-menu`), not the page's scoped CSS.
- More than two filters on a phone become one **Filter** button showing the count (filled when any are on) that opens
  a bottom panel with *Clear all* and *Show N items*; active filters show as chips that remove themselves with ✕.
- "Expand/collapse all" is one icon button with a tooltip and an aria-label, not a text button.

**Periods**
- Use `PeriodNavigator`: ‹ name ▾ ›. Where a page offers more than one length of period, they're chosen from the name's
  menu ("Week view", "Month view", "12-month view"), not a separate toggle.
- "Go to today" is an icon that floats beside the next arrow; it never moves the name.
- Names come from `PeriodNames` and always include the year: "November 2026", "Nov 2026 – Oct 2027"; a run of days
  has a two-digit year, once, at the end: "8 – 14 Nov 26", "27 Dec – 2 Jan 27".

**Amounts and wording**
- Format money with `Money.Format` / `Money.Signed`: the minus is joined to the number and amounts never wrap
  (`text-nowrap`).
- Amounts line up in one right-hand column. A heading or total above a panel is inset by the panel's padding so its
  amount lines up with the amounts inside.
- Words go to the left of an amount and the amount is bold: "Short by **£150.00**", "Running total **£1,240.00**",
  "Surplus **£400.00**". Status is shown by an icon and words, never colour alone.
- Long names wrap beside their icon (a grid, not centred), or stop at two lines with `two-lines`; chips are
  left-aligned.

**Accessibility**
- Every icon-only button has an aria-label and a tooltip; never nest interactive controls; touch targets are at
  least 44px on phones; pages pass the axe scan in light and dark themes.
- Headings focused after navigation show no focus outline (App.razor), but controls always do.

**Process**
- When a layout choice is open, render a quick mock-up of the options to compare before building.
- Styles for one page or component go in its scoped `.razor.css` (see above).

