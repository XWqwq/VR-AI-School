"""Apply keyword-only weights to imported Dify workflow snapshots."""

import json

from sqlalchemy import select

from extensions.ext_database import db
from models.workflow import Workflow


APP_ID = "370c497b-30be-4a27-92b8-b55a5bd92072"
workflows = db.session.scalars(select(Workflow).where(Workflow.app_id == APP_ID)).all()
for workflow in workflows:
    graph = workflow.graph_dict
    for node in graph.get("nodes", []):
        if node.get("data", {}).get("type") == "llm":
            params = node["data"].setdefault("model", {}).setdefault("completion_params", {})
            params["think"] = False
            params["max_tokens"] = 512
        if node.get("data", {}).get("type") != "knowledge-retrieval":
            continue
        config = node["data"].setdefault("multiple_retrieval_config", {})
        config["reranking_enable"] = False
        config["reranking_mode"] = "weighted_score"
        config["weights"] = {
            "vector_setting": {
                "vector_weight": 0.0,
                "embedding_provider_name": "",
                "embedding_model_name": "",
            },
            "keyword_setting": {"keyword_weight": 1.0},
        }
    workflow.graph = json.dumps(graph, ensure_ascii=False)
db.session.commit()
print("UPDATED", len(workflows))
