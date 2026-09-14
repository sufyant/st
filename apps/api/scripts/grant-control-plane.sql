-- Grants control plane access to the application roles.
-- Run against the control_plane database after `migrate control-plane` has succeeded.

-- PostgreSQL grants CONNECT to PUBLIC on every new database, so every tenant's dynamic role could
-- otherwise open a session here. Revoking it leaves only the roles named below.
REVOKE CONNECT ON DATABASE control_plane FROM PUBLIC;

GRANT CONNECT ON DATABASE control_plane TO control, resolver;
GRANT CONNECT ON DATABASE control_plane TO migrator, provisioner;
GRANT USAGE ON SCHEMA control TO control, resolver;

GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA control TO control;
ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA control
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO control;

GRANT SELECT ON control.tenants, control.memberships, control.tenant_credentials TO resolver;
