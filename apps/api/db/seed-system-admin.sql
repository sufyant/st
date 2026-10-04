-- Makes a user the first system admin (0031). Runs once during setup, after the migrations (0020), as the owner or any role
-- allowed to write the catalog. Safe to run again: an existing user or grant is left as it is.
--
--   psql "$DATABASE_URL" -v external_id=user_... -f seed-system-admin.sql
--
-- external_id is the identity provider's user id (Clerk), the value the API sees as the user's NameIdentifier claim (0015).

\set ON_ERROR_STOP on

INSERT INTO catalog.users (id, external_id)
VALUES (uuidv7(), :'external_id')
ON CONFLICT (external_id) DO NOTHING;

-- The seed grants itself: no one granted it, so granted_by stays empty.
INSERT INTO catalog.system_admins (user_id, role, granted_by, granted_at)
SELECT id, 'Administrator', NULL, now() FROM catalog.users WHERE external_id = :'external_id'
ON CONFLICT (user_id) DO NOTHING;
