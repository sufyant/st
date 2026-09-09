-- Grants control plane access to the application roles.
-- Run against the control_plane database after `migrate control-plane` has succeeded.

GRANT CONNECT ON DATABASE control_plane TO st_control, st_tenant;
GRANT USAGE ON SCHEMA control TO st_control, st_tenant;

GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA control TO st_control;
ALTER DEFAULT PRIVILEGES FOR ROLE st_migrator IN SCHEMA control
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO st_control;

GRANT SELECT ON control.tenants, control.memberships TO st_tenant;
