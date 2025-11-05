-- Add SourceLanguage column to Forums, Threads, and Messages tables
ALTER TABLE "Forums" ADD COLUMN IF NOT EXISTS "SourceLanguage" text NOT NULL DEFAULT 'en';
ALTER TABLE "Threads" ADD COLUMN IF NOT EXISTS "SourceLanguage" text NOT NULL DEFAULT 'en';
ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "SourceLanguage" text NOT NULL DEFAULT 'en';
