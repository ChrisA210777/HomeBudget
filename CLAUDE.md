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
- Server code is organised by feature; see `src/Budget.Server/Features/README.md`.
