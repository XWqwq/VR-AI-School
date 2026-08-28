"""Import the virtual-campus knowledge archive into the local Dify workspace.

Requires DIFY_DATASET_KEY in the environment. The key is never written to disk.
"""

from __future__ import annotations

import json
import os
import sys
import tempfile
import zipfile
from pathlib import Path, PurePosixPath

import requests
import yaml


BASE_URL = os.getenv("DIFY_BASE_URL", "http://localhost/v1").rstrip("/")
API_KEY = os.getenv("DIFY_DATASET_KEY", "").strip()
SUPPORTED_EXTENSIONS = {".pdf", ".docx", ".doc", ".md", ".txt"}
CATEGORY_NAMES = {
    "教学学业": "虚拟校园-教学学业",
    "知识库构建": "虚拟校园-校园办事",
    "规章制度": "虚拟校园-规章制度",
}


def decode_zip_name(name: str) -> str:
    try:
        return name.encode("cp437").decode("gbk")
    except (UnicodeEncodeError, UnicodeDecodeError):
        return name


def api(method: str, path: str, **kwargs):
    headers = kwargs.pop("headers", {})
    headers["Authorization"] = f"Bearer {API_KEY}"
    response = requests.request(method, f"{BASE_URL}{path}", headers=headers, timeout=180, **kwargs)
    if not response.ok:
        raise RuntimeError(f"{method} {path} -> {response.status_code}: {response.text[:800]}")
    return response.json() if response.content else {}


def ensure_dataset(name: str, description: str) -> str:
    existing = api("GET", "/datasets", params={"limit": 100, "keyword": name})
    for item in existing.get("data", []):
        if item.get("name") == name:
            return item["id"]
    created = api(
        "POST",
        "/datasets",
        json={
            "name": name,
            "description": description,
            "indexing_technique": "economy",
            "permission": "only_me",
        },
    )
    return created["id"]


def existing_document_names(dataset_id: str) -> set[str]:
    names: set[str] = set()
    page = 1
    while True:
        result = api("GET", f"/datasets/{dataset_id}/documents", params={"page": page, "limit": 100})
        names.update(item.get("name", "") for item in result.get("data", []))
        if not result.get("has_more"):
            return names
        page += 1


def upload_document(dataset_id: str, filename: str, content: bytes) -> None:
    data = {
        "indexing_technique": "economy",
        "doc_form": "text_model",
        "doc_language": "Chinese",
        "process_rule": {"mode": "automatic"},
    }
    api(
        "POST",
        f"/datasets/{dataset_id}/document/create-by-file",
        data={"data": json.dumps(data, ensure_ascii=False)},
        files={"file": (filename, content, "application/octet-stream")},
    )


def update_workflow_yaml(yaml_path: Path, dataset_ids: list[str]) -> None:
    with yaml_path.open("r", encoding="utf-8") as stream:
        workflow = yaml.safe_load(stream)
    for node in workflow["workflow"]["graph"]["nodes"]:
        if node.get("data", {}).get("type") == "knowledge-retrieval":
            node["data"]["dataset_ids"] = dataset_ids
    with yaml_path.open("w", encoding="utf-8", newline="\n") as stream:
        yaml.safe_dump(workflow, stream, allow_unicode=True, sort_keys=False, width=120)


def main() -> int:
    if not API_KEY:
        print("DIFY_DATASET_KEY is required", file=sys.stderr)
        return 2
    if len(sys.argv) != 4:
        print("usage: import_dify_knowledge.py ARCHIVE WORKFLOW_YAML MANIFEST_JSON", file=sys.stderr)
        return 2

    archive_path, yaml_path, manifest_path = map(Path, sys.argv[1:])
    datasets = {
        category: ensure_dataset(
            name,
            f"虚拟校园 AI 向导知识库：{category}。资料来源为项目知识库压缩包。",
        )
        for category, name in CATEGORY_NAMES.items()
    }
    existing = {category: existing_document_names(dataset_id) for category, dataset_id in datasets.items()}
    stats = {category: {"uploaded": 0, "skipped": 0, "failed": []} for category in datasets}

    with zipfile.ZipFile(archive_path) as archive:
        for entry in archive.infolist():
            decoded = decode_zip_name(entry.filename)
            parts = PurePosixPath(decoded).parts
            if entry.is_dir() or len(parts) < 3:
                continue
            category = parts[1]
            filename = parts[-1]
            if category not in datasets or Path(filename).suffix.lower() not in SUPPORTED_EXTENSIONS:
                continue
            if filename in existing[category]:
                stats[category]["skipped"] += 1
                continue
            try:
                upload_document(datasets[category], filename, archive.read(entry))
                existing[category].add(filename)
                stats[category]["uploaded"] += 1
                print(f"uploaded [{category}] {filename}", flush=True)
            except Exception as exc:  # Continue so one legacy file cannot abort the whole import.
                stats[category]["failed"].append({"file": filename, "error": str(exc)})
                print(f"failed [{category}] {filename}: {exc}", file=sys.stderr, flush=True)

    ordered_ids = [datasets[name] for name in CATEGORY_NAMES]
    update_workflow_yaml(yaml_path, ordered_ids)
    manifest = {
        "base_url": BASE_URL,
        "source_archive": archive_path.name,
        "datasets": [
            {"category": category, "name": CATEGORY_NAMES[category], "id": datasets[category], **stats[category]}
            for category in CATEGORY_NAMES
        ],
    }
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(manifest, ensure_ascii=False, indent=2))
    return 0 if not any(item["failed"] for item in stats.values()) else 1


if __name__ == "__main__":
    raise SystemExit(main())
