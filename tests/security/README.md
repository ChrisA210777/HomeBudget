# Security scan

The CI `security-scan` job (BUD-145, part of the OWASP review in BUD-74) runs
[OWASP ZAP's baseline scan](https://www.zaproxy.org/docs/docker/baseline-scan/) against the published app:

1. It publishes the app and runs it in the Testing environment against a throwaway SQL Server, as the end-to-end
   tests do.
2. It signs in with the test sign-in and gives ZAP the session cookie, so every request ZAP makes is signed in.
3. ZAP scans the app as `https://budget.test:7180` (mapped to the same machine) rather than `localhost`, because
   `localhost` is treated specially and doesn't get the Strict-Transport-Security header a deployed app sends.
4. ZAP crawls the app, with a browser as well (`-j`), so it sees the Blazor pages and the API calls they make. The
   baseline scan is **passive**: it reads the responses (headers, cookies, content) and doesn't attack the app.
5. The job checks the scan reached the household API (so it was signed in), and lists the endpoints it requested.
6. `check-zap-report.py` reads the report. The job fails on any **medium or high** alert that isn't listed in
   `zap-accepted.tsv`. Low and informational alerts are listed in the job summary but don't fail it.

The HTML and JSON reports are kept as the `zap-report` artifact on each run.

## When the scan fails

Open the job summary (or the `zap-report` artifact) to see the alert, its rule id and the URLs. Then either:

- **Fix it**, which is almost always the right answer; or
- **Accept it**, by adding a line to `zap-accepted.tsv`: the rule id, the alert's name, and a specific reason, separated
  by tabs. Mention it in the pull request so the owner agrees, and record it on the **Security Findings & Accepted
  Risks** page in Confluence (space SD, page 290455580). Remove both when the cause is fixed.

Low and informational alerts that have been looked at, and why no change was made, are on the same Confluence page.

## Running it locally

Start the app in the Testing environment as described in [tests/e2e/README.md](../e2e/README.md), sign in at
`https://localhost:7180/account/test-sign-in?name=Zap` in a browser, and copy the `__Host-budget` cookie's value. Then,
with Docker:

```bash
mkdir -p zap && chmod 777 zap
docker run --rm --network host -v "$PWD/zap:/zap/wrk:rw" ghcr.io/zaproxy/zaproxy:stable \
  zap-baseline.py -t https://localhost:7180 -j -m 3 -I -J report.json -r report.html \
  -z "-config replacer.full_list(0).description=signed-in -config replacer.full_list(0).enabled=true \
      -config replacer.full_list(0).matchtype=REQ_HEADER -config replacer.full_list(0).matchstr=Cookie \
      -config replacer.full_list(0).regex=false -config replacer.full_list(0).replacement=__Host-budget=<cookie>"
python3 tests/security/check-zap-report.py zap/report.json tests/security/zap-accepted.tsv
```

`--network host` only works on Linux; on Windows or macOS use `host.docker.internal` instead of `localhost`.
