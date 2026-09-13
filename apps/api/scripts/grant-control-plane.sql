-- Grants control plane access to the application roles.
-- Run against the control_plane database after `migrate control-plane` has succeeded.

GRANT CONNECT ON DATABASE control_plane TO control, resolver;
GRANT USAGE ON SCHEMA control TO control, resolver;

GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA control TO control;
ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA control
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO control;

GRANT SELECT ON control.tenants, control.memberships TO resolver;
