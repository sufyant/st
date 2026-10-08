-- Creates the database roles. Runs before the migrations, as a role allowed to create roles: a superuser
-- locally, a member of neon_superuser on Neon. Safe to run again; a rerun sets the passwords given.
--
--   psql "$DATABASE_URL" -v owner_password=... -v application_password=... -f bootstrap.sql

\set ON_ERROR_STOP on

SELECT 'CREATE ROLE api_owner' WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'api_owner') \gexec
SELECT 'CREATE ROLE api_application' WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'api_application') \gexec

-- No role bypasses row level security.
SELECT format('ALTER ROLE api_owner LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS PASSWORD %L', :'owner_password') \gexec
SELECT format('ALTER ROLE api_application LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS PASSWORD %L', :'application_password') \gexec

-- The owner creates each module's schema in its migrations; the application role receives privileges from those migrations.
SELECT format('GRANT CREATE ON DATABASE %I TO api_owner', current_database()) \gexec
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
