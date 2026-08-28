"""Run inside the Dify API container to import the prepared Chatflow DSL."""

from sqlalchemy import select
from sqlalchemy.orm import Session

from extensions.ext_database import db
from models.account import Account, TenantAccountJoin
from services.app_dsl_service import AppDslService, ImportMode, ImportStatus


DSL_PATH = "/tmp/virtual-campus-chatflow.yml"


account = db.session.scalar(select(Account).order_by(Account.created_at).limit(1))
if account is None:
    raise RuntimeError("No Dify console account exists")
tenant_join = db.session.scalar(
    select(TenantAccountJoin).where(TenantAccountJoin.account_id == account.id).order_by(TenantAccountJoin.created_at)
)
if tenant_join is None:
    raise RuntimeError("The Dify account is not attached to a workspace")
account.set_tenant_id(tenant_join.tenant_id)

with open(DSL_PATH, "r", encoding="utf-8") as stream:
    yaml_content = stream.read()

with Session(db.engine, expire_on_commit=False) as session:
    result = AppDslService(session).import_app(
        account=account,
        import_mode=ImportMode.YAML_CONTENT,
        yaml_content=yaml_content,
    )
    if result.status == ImportStatus.FAILED:
        session.rollback()
        raise RuntimeError(str(result.model_dump(mode="json")))
    if result.status == ImportStatus.PENDING:
        session.rollback()
        raise RuntimeError("Import requires pending dependency installation: " + str(result.model_dump(mode="json")))
    session.commit()
    print(result.model_dump_json())
