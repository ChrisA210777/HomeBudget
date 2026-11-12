"""Checks a ZAP report (BUD-145): fails on medium or high alerts that aren't accepted in zap-accepted.tsv.

Usage: python3 check-zap-report.py <report.json> <zap-accepted.tsv>
Writes a summary table to the GitHub job summary when run in Actions.
"""

import json
import os
import sys

RISKS = {0: "Informational", 1: "Low", 2: "Medium", 3: "High"}


def accepted_rules(path):
    rules = {}
    with open(path, encoding="utf-8") as file:
        for line in file:
            if not line.strip() or line.startswith("#"):
                continue
            parts = line.rstrip("\n").split("\t")
            rules[parts[0].strip()] = parts[2].strip() if len(parts) > 2 else ""
    return rules


def main(report_path, accepted_path):
    with open(report_path, encoding="utf-8") as file:
        report = json.load(file)
    accepted = accepted_rules(accepted_path)

    alerts = {}
    for site in report.get("site", []):
        for alert in site.get("alerts", []):
            rule = str(alert.get("pluginid"))
            risk = int(alert.get("riskcode", 0))
            entry = alerts.setdefault(rule, {"name": alert.get("name") or alert.get("alert"), "risk": risk, "urls": set()})
            entry["risk"] = max(entry["risk"], risk)
            for instance in alert.get("instances", []):
                entry["urls"].add(instance.get("uri", ""))

    failures = []
    lines = ["## ZAP baseline scan", "", "| Rule | Alert | Risk | URLs | Status |", "| --- | --- | --- | --- | --- |"]
    for rule, entry in sorted(alerts.items(), key=lambda item: (-item[1]["risk"], item[0])):
        if entry["risk"] < 2:
            status = "below medium"
        elif rule in accepted:
            status = "accepted: " + accepted[rule]
        else:
            status = "**fails the scan**"
            failures.append(rule)
        lines.append(f"| {rule} | {entry['name']} | {RISKS.get(entry['risk'], entry['risk'])} | {len(entry['urls'])} | {status} |")
        print(f"{rule} {RISKS.get(entry['risk'], entry['risk'])}: {entry['name']} ({status})")
        if rule in failures:
            for url in sorted(entry["urls"])[:10]:
                print(f"    {url}")

    if not alerts:
        lines.append("| | No alerts | | | |")

    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as file:
            file.write("\n".join(lines) + "\n")

    if failures:
        print(f"\nThe scan found medium or high alerts that aren't accepted: {', '.join(failures)}.")
        print("Fix them, or add them to tests/security/zap-accepted.tsv with the reason they're accepted.")
        return 1

    print("\nNo medium or high alerts beyond the accepted ones.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2]))
