"""Run inside Dify API container to publish the campus app and create its API key."""

from sqlalchemy import select
from sqlalchemy.orm import sessionmaker

from extensions.ext_database import db
from models.account import Account, TenantAccountJoin
from models.enums import ApiTokenType
from models.model import ApiToken, App
from services.workflow_service import WorkflowService


APP_ID = "370c497b-30be-4a27-92b8-b55a5bd92072"
KEY_OUTPUT = "/tmp/virtual-campus-app-key"

account = db.session.scalar(select(Account).order_by(Account.created_at).limit(1))
if account is None:
    raise RuntimeError("No Dify account")
join = db.session.scalar(select(TenantAccountJoin).where(TenantAccountJoin.account_id == account.id).limit(1))
if join is None:
    raise RuntimeError("No Dify workspace")
account.set_tenant_id(join.tenant_id)
app_model = db.session.get(App, APP_ID)
if app_model is None:
    raise RuntimeError("Imported campus app not found")

with sessionmaker(db.engine).begin() as session:
    workflow = WorkflowService().publish_workflow(
        session=session,
        app_model=app_model,
        account=account,
        marked_name="虚拟校园 Ollama 知识库版",
        marked_comment="绑定三个本地校园知识库和 qwen2.5:7b",
    )
    attached_app = session.get(App, APP_ID)
    attached_app.workflow_id = workflow.id
    attached_app.updated_by = account.id

token = db.session.scalar(
    select(ApiToken.token).where(ApiToken.app_id == APP_ID, ApiToken.type == ApiTokenType.APP).limit(1)
)
if not token:
    token = ApiToken.generate_api_key("app-", 24)
    api_token = ApiToken()
    api_token.app_id = APP_ID
    api_token.tenant_id = join.tenant_id
    api_token.type = ApiTokenType.APP
    api_token.token = token
    db.session.add(api_token)
    db.session.commit()

with open(KEY_OUTPUT, "w", encoding="utf-8") as stream:
    stream.write(token)
print("PUBLISHED", APP_ID)
