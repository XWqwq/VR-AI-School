"""Replace the existing virtual-campus app draft graph from the prepared DSL.

Run inside the Dify API container, then publish with dify_internal_publish_app.py.
"""

import json

import yaml
from sqlalchemy import select

from extensions.ext_database import db
from models.workflow import Workflow


APP_ID = "370c497b-30be-4a27-92b8-b55a5bd92072"
DSL_PATH = "/tmp/virtual-campus-chatflow.yml"

with open(DSL_PATH, "r", encoding="utf-8") as stream:
    graph = yaml.safe_load(stream)["workflow"]["graph"]

drafts = db.session.scalars(
    select(Workflow).where(Workflow.app_id == APP_ID, Workflow.version == "draft")
).all()
if not drafts:
    raise RuntimeError("Virtual-campus draft workflow not found")

for workflow in drafts:
    workflow.graph = json.dumps(graph, ensure_ascii=False)

db.session.commit()
print("UPDATED_DRAFTS", len(drafts), "NODES", len(graph["nodes"]), "EDGES", len(graph["edges"]))
