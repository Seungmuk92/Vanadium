-- =============================================================================
-- FULL RESET — wipe ALL Vanadium data and start from an empty schema
-- =============================================================================
-- DESTRUCTIVE. This deletes every table (notes, labels, API tokens, settings,
-- the old Users table, file-attachment metadata) AND the EF migration history.
-- There is no undo. Take a backup first if you might want anything back.
--
-- Run this against the Vanadium database (the one in your connection string,
-- e.g. Database=vanadium). It only needs the app's own DB user.
--
-- After this runs, the next API startup applies every migration from scratch on
-- the empty schema, producing the clean password-only schema (no Users table,
-- no UserId columns). You do NOT need password-only-preflight.sql when resetting.
-- =============================================================================

DROP SCHEMA public CASCADE;
CREATE SCHEMA public;

-- Restore default privileges (adjust the role name if your DB user is not "smoh").
GRANT ALL ON SCHEMA public TO smoh;
GRANT ALL ON SCHEMA public TO public;

-- The trigram search index needs this extension; the migration also creates it
-- with IF NOT EXISTS, so this line is just belt-and-suspenders.
CREATE EXTENSION IF NOT EXISTS pg_trgm;
