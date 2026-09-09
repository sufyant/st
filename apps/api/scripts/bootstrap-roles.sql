-- Creates the four login roles. Requires a superuser connection and runs once per environment.
-- Passwords are supplied as psql variables so that no secret is stored in the repository:
--   psql -v migrator_password=... -v provisioner_password=... \
--        -v control_password=... -v tenant_password=... -f scripts/bootstrap-roles.sql

CREATE ROLE st_migrator LOGIN PASSWORD :'migrator_password' CREATEDB;
CREATE ROLE st_provisioner LOGIN PASSWORD :'provisioner_password' CREATEDB;
CREATE ROLE st_control LOGIN PASSWORD :'control_password';
CREATE ROLE st_tenant LOGIN PASSWORD :'tenant_password';

-- Provisioning owns the tenant tables it creates, and the deploy-time migrator alters them,
-- so the migrator must be able to act as their owner.
GRANT st_provisioner TO st_migrator;
