-- =============================================================================
-- Pre-flight for the password-only migration (EF migration: RemoveUserConcept)
-- =============================================================================
-- Run this against the Vanadium PostgreSQL database BEFORE applying the EF
-- migration `20260702000000_RemoveUserConcept`.
--
-- It performs NO schema changes. Its jobs are:
--   1. Back up the Users table (the migration DROPs it permanently).
--   2. Refuse to proceed if more than one user exists (dropping UserId would
--      merge multiple owners' notes/labels/tokens into one indistinguishable pool).
--   3. Report any rows whose UserId points at a non-existent user.
--   4. Print the owner's existing password hash so it can be reused as
--      Auth:PasswordHash (same PBKDF2-SHA256 format), preserving the current
--      password after Users is dropped.
--
-- Why this matters: after the migration there is no Users table and no per-row
-- UserId. The password moves to configuration (Auth:PasswordHash). If you skip
-- step 4 you can still log in by setting ANY new hash via POST /api/auth/hash,
-- but reusing the existing hash keeps your current password unchanged.
-- =============================================================================

BEGIN;

-- 1. Snapshot the Users table (username, password hash, and the UserId values
--    that Notes/Labels/LabelCategories/ApiTokens currently reference). This is
--    the only way back once the migration drops "Users".
DROP TABLE IF EXISTS "Users_backup_preflight";
CREATE TABLE "Users_backup_preflight" AS TABLE "Users";

-- 2. This app is single-user by design. Abort if that assumption is broken.
DO $$
DECLARE
    user_count int;
BEGIN
    SELECT count(*) INTO user_count FROM "Users";
    RAISE NOTICE 'Users present: %', user_count;
    IF user_count > 1 THEN
        RAISE EXCEPTION
            'Multiple users (%) found. Dropping UserId would merge their data. '
            'Consolidate to a single owner (or delete the extra users) before migrating.',
            user_count;
    END IF;
    IF user_count = 0 THEN
        RAISE WARNING
            'No users found. The migration will still run, but confirm this database '
            'is the right one before continuing.';
    END IF;
END $$;

-- 3. Rows whose UserId matches no user (each count should be 0). Non-zero means
--    the database is in an unexpected state — review before migrating.
SELECT 'Notes'           AS table_name, count(*) AS orphan_rows
    FROM "Notes"           n LEFT JOIN "Users" u ON u."Id" = n."UserId" WHERE u."Id" IS NULL
UNION ALL
SELECT 'Labels',           count(*)
    FROM "Labels"          l LEFT JOIN "Users" u ON u."Id" = l."UserId" WHERE u."Id" IS NULL
UNION ALL
SELECT 'LabelCategories',  count(*)
    FROM "LabelCategories" c LEFT JOIN "Users" u ON u."Id" = c."UserId" WHERE u."Id" IS NULL
UNION ALL
SELECT 'ApiTokens',        count(*)
    FROM "ApiTokens"       t LEFT JOIN "Users" u ON u."Id" = t."UserId" WHERE u."Id" IS NULL;

-- 4. The owner's credentials. Copy "password_hash" into Auth:PasswordHash
--    (appsettings / AUTH_PASSWORD_HASH env) to keep the SAME password.
SELECT "Id"           AS owner_user_id,
       "Username"     AS owner_username,
       "PasswordHash" AS password_hash
FROM "Users";

COMMIT;

-- =============================================================================
-- After this script succeeds:
--   1. Put the password hash from step 4 into Auth:PasswordHash.
--   2. Apply the EF migration:  dotnet ef database update --project Vanadium.Note.REST
--      (or just start the API — it runs Database.Migrate() on startup).
--   3. Verify login works, then drop the backup when you are confident:
--        DROP TABLE "Users_backup_preflight";
-- =============================================================================
