namespace KoeNote.App.Services.Database;

internal static class KoeNoteDatabaseMigrations
{
    public static IReadOnlyList<DatabaseMigration> All { get; } =
    [
        new(1, "initial schema", ApplyInitialSchema),
        new(2, "status and ASR settings", ApplyStatusAndAsrSettingsSchema),
        new(3, "editing and undo", ApplyEditingAndUndoSchema),
        new(4, "correction memory", ApplyCorrectionMemorySchema),
        new(5, "ASR adapter", ApplyAsrAdapterSchema),
        new(6, "model catalog", ApplyModelCatalogSchema),
        new(7, "repair ASR settings engine id", ApplyAsrSettingsEngineIdRepair),
        new(8, "review stage toggle", ApplyReviewStageToggle)
    ];

    private static void ApplyInitialSchema(DatabaseMigrationContext migration)
    {
        migration.Execute("""
            CREATE TABLE IF NOT EXISTS jobs (
                job_id TEXT NOT NULL PRIMARY KEY,
                title TEXT NOT NULL,
                source_audio_path TEXT NOT NULL,
                normalized_audio_path TEXT,
                status TEXT NOT NULL,
                current_stage TEXT,
                progress_percent INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                asr_engine TEXT,
                asr_model TEXT,
                review_model TEXT,
                unreviewed_draft_count INTEGER NOT NULL DEFAULT 0
            );
            """);
        migration.Execute("""
            CREATE TABLE IF NOT EXISTS transcript_segments (
                segment_id TEXT NOT NULL,
                job_id TEXT NOT NULL,
                start_seconds REAL NOT NULL,
                end_seconds REAL NOT NULL,
                speaker_id TEXT,
                speaker_name TEXT,
                raw_text TEXT NOT NULL,
                normalized_text TEXT,
                final_text TEXT,
                review_state TEXT NOT NULL DEFAULT 'none',
                asr_confidence REAL,
                source TEXT NOT NULL DEFAULT 'asr',
                PRIMARY KEY (job_id, segment_id)
            );
            """);
        migration.Execute("""
            CREATE TABLE IF NOT EXISTS correction_drafts (
                draft_id TEXT NOT NULL PRIMARY KEY,
                job_id TEXT NOT NULL,
                segment_id TEXT NOT NULL,
                issue_type TEXT NOT NULL,
                original_text TEXT NOT NULL,
                suggested_text TEXT NOT NULL,
                reason TEXT NOT NULL,
                confidence REAL NOT NULL,
                status TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """);
        migration.Execute("""
            CREATE TABLE IF NOT EXISTS review_decisions (
                decision_id TEXT NOT NULL PRIMARY KEY,
                draft_id TEXT NOT NULL,
                action TEXT NOT NULL,
                final_text TEXT,
                manual_note TEXT,
                decided_at TEXT NOT NULL
            );
            """);
        migration.Execute("""
            CREATE TABLE IF NOT EXISTS stage_progress (
                stage_id TEXT NOT NULL PRIMARY KEY,
                job_id TEXT NOT NULL,
                stage TEXT NOT NULL,
                status TEXT NOT NULL,
                progress_percent INTEGER NOT NULL DEFAULT 0,
                started_at TEXT,
                finished_at TEXT,
                duration_seconds REAL,
                exit_code INTEGER,
                error_category TEXT,
                log_path TEXT
            );
            """);
        migration.Execute("""
            CREATE TABLE IF NOT EXISTS job_log_events (
                log_event_id TEXT NOT NULL PRIMARY KEY,
                job_id TEXT,
                stage TEXT,
                level TEXT NOT NULL,
                message TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """);
    }

    private static void ApplyStatusAndAsrSettingsSchema(DatabaseMigrationContext migration)
    {
        migration.AddColumnIfMissing("jobs", "last_error_category", "TEXT");
        migration.Execute("""
            CREATE TABLE IF NOT EXISTS asr_settings (
                settings_id INTEGER NOT NULL PRIMARY KEY CHECK (settings_id = 1),
                context_text TEXT NOT NULL DEFAULT '',
                hotwords_text TEXT NOT NULL DEFAULT '',
                engine_id TEXT NOT NULL DEFAULT 'vibevoice-crispasr',
                enable_review_stage INTEGER NOT NULL DEFAULT 1,
                updated_at TEXT NOT NULL
            );
            """);
        migration.AddColumnIfMissing("asr_settings", "engine_id", "TEXT NOT NULL DEFAULT 'vibevoice-crispasr'");
        migration.AddColumnIfMissing("asr_settings", "enable_review_stage", "INTEGER NOT NULL DEFAULT 1");
    }

    private static void ApplyEditingAndUndoSchema(DatabaseMigrationContext migration)
    {
        migration.Execute("""
            CREATE TABLE IF NOT EXISTS speaker_aliases (
                job_id TEXT NOT NULL,
                speaker_id TEXT NOT NULL,
                display_name TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                PRIMARY KEY (job_id, speaker_id)
            );
            """);

        migration.Execute("""
            CREATE TABLE IF NOT EXISTS review_operation_history (
                operation_id TEXT NOT NULL PRIMARY KEY,
                job_id TEXT NOT NULL,
                draft_id TEXT,
                segment_id TEXT,
                operation_type TEXT NOT NULL,
                before_json TEXT NOT NULL,
                after_json TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """);
    }

    private static void ApplyCorrectionMemorySchema(DatabaseMigrationContext migration)
    {
        migration.AddColumnIfMissing("correction_drafts", "source", "TEXT NOT NULL DEFAULT 'llm'");
        migration.AddColumnIfMissing("correction_drafts", "source_ref_id", "TEXT");

        migration.Execute("""
            CREATE TABLE IF NOT EXISTS user_terms (
                term_id TEXT NOT NULL PRIMARY KEY,
                surface TEXT NOT NULL,
                reading TEXT,
                category TEXT NOT NULL DEFAULT 'general',
                enabled INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);

        migration.Execute("""
            CREATE UNIQUE INDEX IF NOT EXISTS idx_user_terms_surface_category
            ON user_terms(surface, category);
            """);

        migration.Execute("""
            CREATE TABLE IF NOT EXISTS correction_memory (
                memory_id TEXT NOT NULL PRIMARY KEY,
                wrong_text TEXT NOT NULL,
                correct_text TEXT NOT NULL,
                issue_type TEXT NOT NULL,
                scope TEXT NOT NULL DEFAULT 'global',
                accepted_count INTEGER NOT NULL DEFAULT 1,
                rejected_count INTEGER NOT NULL DEFAULT 0,
                enabled INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);

        migration.Execute("""
            CREATE UNIQUE INDEX IF NOT EXISTS idx_correction_memory_pair_scope
            ON correction_memory(wrong_text, correct_text, scope);
            """);

        migration.Execute("""
            CREATE TABLE IF NOT EXISTS correction_memory_events (
                event_id TEXT NOT NULL PRIMARY KEY,
                memory_id TEXT,
                draft_id TEXT,
                job_id TEXT NOT NULL,
                segment_id TEXT,
                event_type TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """);
    }

    private static void ApplyAsrAdapterSchema(DatabaseMigrationContext migration)
    {
        migration.Execute("""
            CREATE TABLE IF NOT EXISTS asr_runs (
                asr_run_id TEXT NOT NULL PRIMARY KEY,
                job_id TEXT NOT NULL,
                engine_id TEXT NOT NULL,
                model_id TEXT NOT NULL,
                model_version TEXT,
                status TEXT NOT NULL,
                started_at TEXT,
                finished_at TEXT,
                duration_seconds REAL,
                peak_vram_mb INTEGER,
                raw_output_path TEXT,
                normalized_output_path TEXT,
                error_category TEXT,
                created_at TEXT NOT NULL
            );
            """);

        migration.AddColumnIfMissing("transcript_segments", "asr_run_id", "TEXT");
    }

    private static void ApplyModelCatalogSchema(DatabaseMigrationContext migration)
    {
        migration.Execute("""
            CREATE TABLE IF NOT EXISTS installed_models (
                model_id TEXT NOT NULL PRIMARY KEY,
                role TEXT NOT NULL,
                engine_id TEXT NOT NULL,
                display_name TEXT NOT NULL,
                family TEXT,
                version TEXT,
                file_path TEXT NOT NULL,
                manifest_path TEXT,
                size_bytes INTEGER,
                sha256 TEXT,
                verified INTEGER NOT NULL DEFAULT 0,
                license_name TEXT,
                source_type TEXT NOT NULL,
                installed_at TEXT NOT NULL,
                last_verified_at TEXT,
                status TEXT NOT NULL
            );
            """);

        migration.Execute("""
            CREATE TABLE IF NOT EXISTS installed_runtimes (
                runtime_id TEXT NOT NULL PRIMARY KEY,
                runtime_type TEXT NOT NULL,
                display_name TEXT NOT NULL,
                version TEXT,
                install_path TEXT NOT NULL,
                verified INTEGER NOT NULL DEFAULT 0,
                source_type TEXT NOT NULL,
                installed_at TEXT NOT NULL,
                status TEXT NOT NULL
            );
            """);

        migration.Execute("""
            CREATE TABLE IF NOT EXISTS model_download_jobs (
                download_id TEXT NOT NULL PRIMARY KEY,
                model_id TEXT NOT NULL,
                url TEXT NOT NULL,
                target_path TEXT NOT NULL,
                temp_path TEXT NOT NULL,
                status TEXT NOT NULL,
                bytes_total INTEGER,
                bytes_downloaded INTEGER NOT NULL DEFAULT 0,
                sha256_expected TEXT,
                sha256_actual TEXT,
                error_message TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);
    }

    private static void ApplyAsrSettingsEngineIdRepair(DatabaseMigrationContext migration)
    {
        migration.Execute("""
            CREATE TABLE IF NOT EXISTS asr_settings (
                settings_id INTEGER NOT NULL PRIMARY KEY CHECK (settings_id = 1),
                context_text TEXT NOT NULL DEFAULT '',
                hotwords_text TEXT NOT NULL DEFAULT '',
                engine_id TEXT NOT NULL DEFAULT 'vibevoice-crispasr',
                enable_review_stage INTEGER NOT NULL DEFAULT 1,
                updated_at TEXT NOT NULL
            );
            """);
        migration.AddColumnIfMissing("asr_settings", "engine_id", "TEXT NOT NULL DEFAULT 'vibevoice-crispasr'");
        migration.AddColumnIfMissing("asr_settings", "enable_review_stage", "INTEGER NOT NULL DEFAULT 1");
    }

    private static void ApplyReviewStageToggle(DatabaseMigrationContext migration)
    {
        migration.Execute("""
            CREATE TABLE IF NOT EXISTS asr_settings (
                settings_id INTEGER NOT NULL PRIMARY KEY CHECK (settings_id = 1),
                context_text TEXT NOT NULL DEFAULT '',
                hotwords_text TEXT NOT NULL DEFAULT '',
                engine_id TEXT NOT NULL DEFAULT 'vibevoice-crispasr',
                enable_review_stage INTEGER NOT NULL DEFAULT 1,
                updated_at TEXT NOT NULL
            );
            """);
        migration.AddColumnIfMissing("asr_settings", "enable_review_stage", "INTEGER NOT NULL DEFAULT 1");
    }
}
