"""Run inside the Dify API container to create/reuse a dataset API key.

The token is written only to the container's temporary directory and is not
printed, so project logs cannot accidentally capture it.
"""

from sqlalchemy import select

from extensions.ext_database import db
from models.account import Account, TenantAccountJoin
from models.enums import ApiTokenType
from models.model import ApiToken


account = db.session.scalar(select(Account).order_by(Account.created_at).limit(1))
if account is None:
    raise RuntimeError("No Dify console account")
join = db.session.scalar(
    select(TenantAccountJoin).where(TenantAccountJoin.account_id == account.id).limit(1)
)
if join is None:
    raise RuntimeError("No Dify workspace")

token = db.session.scalar(
    select(ApiToken.token)
    .where(ApiToken.tenant_id == join.tenant_id, ApiToken.type == ApiTokenType.DATASET)
    .limit(1)
)
if not token:
    token = ApiToken.generate_api_key("dataset-", 24)
    api_token = ApiToken()
    api_token.tenant_id = join.tenant_id
    api_token.type = ApiTokenType.DATASET
    api_token.token = token
    db.session.add(api_token)
    db.session.commit()

with open("/tmp/virtual-campus-dataset-key", "w", encoding="utf-8") as stream:
    stream.write(token)

print("DATASET_KEY_READY")
