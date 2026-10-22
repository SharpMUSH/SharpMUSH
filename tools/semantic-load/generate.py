#!/usr/bin/env python3
"""Generates an N-times semantic load dataset from an extracted PF2e dataset (pages.jsonl, facts.jsonl).

Each replica copies every page and fact under new opaque ids, with page references kept inside their
replica. 99.9% of pages go in category `sealed`, which a read rule closes to every measured reader except
an administrator; the rest go in `open`. One `hot` page is the pf2e:class target of every subject, so it
has one incoming edge per subject. Output: pages.jsonl, facts.jsonl, rules.json, meta.json, manifest.json.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path


def replica_id(replica: int, original: str) -> str:
    return original if replica == 0 else hashlib.sha256(f"{replica}:{original}".encode()).hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True, help="extracted dataset with pages.jsonl and facts.jsonl")
    parser.add_argument("--times", type=int, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.output.exists():
        raise SystemExit(f"{args.output} exists; choose a new directory")
    args.output.mkdir(parents=True)
    pages = [json.loads(line) for line in (args.source / "pages.jsonl").read_text(encoding="utf-8").splitlines() if line.strip()]
    facts = [json.loads(line) for line in (args.source / "facts.jsonl").read_text(encoding="utf-8").splitlines() if line.strip()]
    hot = "hot-target"
    provenance = {"Package": "semantic-load", "SourceCommit": "generated", "SourcePath": "generated", "AuthoredField": "generated",
                  "LocalOverride": False, "PackageVersion": "1", "SourceBlobHash": "", "ExtractionRuleVersion": "semantic-load-1",
                  "EvidenceLocation": ""}
    count = 0
    with (args.output / "pages.jsonl").open("w", encoding="utf-8") as out:
        out.write(json.dumps({"id": hot, "key": {"Package": "semantic-load", "Namespace": "main", "SourceKey": "hot_target"},
                              "title": "Hot Target", "categories": ["open"]}) + "\n")
        for replica in range(args.times):
            for page in pages:
                key = dict(page["key"])
                if replica:
                    key["SourceKey"] = f"{key['SourceKey']}_r{replica}"
                count += 1
                category = "open" if count % 1000 == 0 else "sealed"
                out.write(json.dumps({"id": replica_id(replica, page["id"]), "key": key,
                                      "title": page["title"] if replica == 0 else f"{page['title']} ({replica})",
                                      "categories": [category]}) + "\n")
    subjects = set()
    edges = 0
    with (args.output / "facts.jsonl").open("w", encoding="utf-8") as out:
        for replica in range(args.times):
            for fact in facts:
                copy = dict(fact)
                copy["subject"] = replica_id(replica, fact["subject"])
                if fact["type"] == "PageRef":
                    copy["value"] = replica_id(replica, fact["value"])
                subjects.add(copy["subject"])
                out.write(json.dumps(copy) + "\n")
        for subject in sorted(subjects):
            edges += 1
            out.write(json.dumps({"subject": subject, "property": "pf2e:class", "type": "PageRef", "value": hot,
                                  "provenance": provenance}) + "\n")
    (args.output / "rules.json").write_text(json.dumps([{"Scope": "Category", "Key": "sealed", "Read": ["sealed.read"]}]), encoding="utf-8")
    (args.output / "meta.json").write_text(json.dumps({"HotTarget": hot, "Times": args.times, "Pages": count + 1,
                                                       "HotEdges": edges, "SealedShare": 0.999}), encoding="utf-8")
    manifest = {name: hashlib.sha256((args.output / name).read_bytes()).hexdigest()
                for name in ("pages.jsonl", "facts.jsonl", "rules.json", "meta.json")}
    manifest["source"] = {name: hashlib.sha256((args.source / name).read_bytes()).hexdigest() for name in ("pages.jsonl", "facts.jsonl")}
    (args.output / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(json.dumps({"pages": count + 1, "subjects": len(subjects), "hot_edges": edges}))


if __name__ == "__main__":
    main()
