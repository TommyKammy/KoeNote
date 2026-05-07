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
        new(8, "review stage toggle", ApplyReviewStageToggle),
        new(9, "maintenance indexes", ApplyMaintenanceIndexes),
        new(10, "job recycle bin", ApplyJobRecycleBinSchema),
        new(11, "domain preset metadata", ApplyDomainPresetMetadataSchema),
        new(12, "domain preset deactivation", ApplyDomainPresetDeactivationSchema),
        new(13, "domain preset speaker alias tracking", ApplyDomainPresetSpeakerAliasTrackingSchema),
        new(14, "domain preset speaker alias restore", ApplyDomainPresetSpeakerAliasRestoreSchema),
        new(15, "transcript derivatives", ApplyTranscriptDerivativesSchema)
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
                engine_id TEXT NOT NULL DEFAULT 'faster-whisper-large-v3-turbo',
                enable_review_stage INTEGER NOT NULL DEFAULT 1,
                updated_at TEXT NOT NULL
            );
            """);
        migration.AddColumnIfMissing("asr_settings", "engine_id", "TEXT NOT NULL DEFAULT 'faster-whisper-large-v3-turbo'");
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
                engine_id TEXT NOT NULL DEFAULT 'faster-whisper-large-v3-turbo',
                enable_review_stage INTEGER NOT NULL DEFAULT 1,
                updated_at TEXT NOT NULL
            );
            """);
        migration.AddColumnIfMissing("asr_settings", "engine_id", "TEXT NOT NULL DEFAULT 'faster-whisper-large-v3-turbo'");
        migration.AddColumnIfMissing("asr_settings", "enable_review_stage", "INTEGER NOT NULL DEFAULT 1");
    }

    private static void ApplyReviewStageToggle(DatabaseMigrationContext migration)
    {
        migration.Execute("""
            CREATE TABLE IF NOT EXISTS asr_settings (
                settings_id INTEGER NOT NULL PRIMARY KEY CHECK (settings_id = 1),
                context_text TEXT NOT NULL DEFAULT '',
                hotwords_text TEXT NOT NULL DEFAULT '',
                engine_id TEXT NOT NULL DEFAULT 'faster-whisper-large-v3-turbo',
                enable_review_stage INTEGER NOT NULL DEFAULT 1,
                updated_at TEXT NOT NULL
            );
            """);
        migration.AddColumnIfMissing("asr_settings", "enable_review_stage", "INTEGER NOT NULL DEFAULT 1");
    }

    private static void ApplyMaintenanceIndexes(DatabaseMigrationContext migration)
    {
        migration.ExecuteIfTableExists("jobs", """
            CREATE INDEX IF NOT EXISTS idx_jobs_updated_at
            ON jobs(updated_at DESC);
            """);
        migration.ExecuteIfTableExists("job_log_events", """
            CREATE INDEX IF NOT EXISTS idx_job_log_events_job_created
            ON job_log_events(job_id, created_at DESC);
            """);
        migration.ExecuteIfTableExists("job_log_events", """
            CREATE INDEX IF NOT EXISTS idx_job_log_events_created
            ON job_log_events(created_at DESC);
            """);
        migration.ExecuteIfTableExists("transcript_segments", """
            CREATE INDEX IF NOT EXISTS idx_transcript_segments_job_start
            ON transcript_segments(job_id, start_seconds);
            """);
        migration.ExecuteIfTableExists("correction_drafts", """
            CREATE INDEX IF NOT EXISTS idx_correction_drafts_job_status
            ON correction_drafts(job_id, status);
            """);
        migration.ExecuteIfTableExists("correction_drafts", """
            CREATE INDEX IF NOT EXISTS idx_correction_drafts_job_segment
            ON correction_drafts(job_id, segment_id);
            """);
        migration.ExecuteIfTableExists("review_operation_history", """
            CREATE INDEX IF NOT EXISTS idx_review_operation_history_job_created
            ON review_operation_history(job_id, created_at DESC);
            """);
        migration.ExecuteIfTableExists("review_operation_history", """
            CREATE INDEX IF NOT EXISTS idx_review_operation_history_created
            ON review_operation_history(created_at DESC);
            """);
        migration.ExecuteIfTableExists("correction_memory_events", """
            CREATE INDEX IF NOT EXISTS idx_correction_memory_events_job_created
            ON correction_memory_events(job_id, created_at DESC);
            """);
        migration.ExecuteIfTableExists("asr_runs", """
            CREATE INDEX IF NOT EXISTS idx_asr_runs_job_created
            ON asr_runs(job_id, created_at DESC);
            """);
        migration.ExecuteIfTableExists("model_download_jobs", """
            CREATE INDEX IF NOT EXISTS idx_model_download_jobs_model_updated
            ON model_download_jobs(model_id, updated_at DESC);
            """);
    }

    private static void ApplyJobRecycleBinSchema(DatabaseMigrationContext migration)
    {
        migration.AddColumnIfTableExistsAndMissing("jobs", "is_deleted", "INTEGER NOT NULL DEFAULT 0");
        migration.AddColumnIfTableExistsAndMissing("jobs", "deleted_at", "TEXT");
        migration.AddColumnIfTableExistsAndMissing("jobs", "delete_reason", "TEXT NOT NULL DEFAULT ''");

        migration.ExecuteIfTableExists("jobs", """
            CREATE INDEX IF NOT EXISTS idx_jobs_deleted_updated
            ON jobs(is_deleted, updated_at DESC);
            """);

        migration.ExecuteIfTableExists("jobs", """
            CREATE INDEX IF NOT EXISTS idx_jobs_deleted_at
            ON jobs(deleted_at DESC);
            """);
    }

    private static void ApplyDomainPresetMetadataSchema(DatabaseMigrationContext migration)
    {
        migration.Execute("""
            CREATE TABLE IF NOT EXISTS review_guidelines (
                guideline_id TEXT NOT NULL PRIMARY KEY,
                preset_id TEXT NOT NULL,
                guideline_text TEXT NOT NULL,
                enabled INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);

        migration.Execute("""
            CREATE UNIQUE INDEX IF NOT EXISTS idx_review_guidelines_preset_text
            ON review_guidelines(preset_id, guideline_text);
            """);

        migration.Execute("""
            CREATE TABLE IF NOT EXISTS domain_preset_imports (
                import_id TEXT NOT NULL PRIMARY KEY,
                preset_id TEXT,
                display_name TEXT NOT NULL,
                schema_version INTEGER NOT NULL,
                source_path TEXT NOT NULL,
                context_updated INTEGER NOT NULL,
                added_hotword_count INTEGER NOT NULL,
                skipped_hotword_count INTEGER NOT NULL,
                added_correction_memory_count INTEGER NOT NULL,
                updated_correction_memory_count INTEGER NOT NULL,
                added_speaker_alias_count INTEGER NOT NULL,
                updated_speaker_alias_count INTEGER NOT NULL,
                skipped_speaker_alias_count INTEGER NOT NULL,
                added_review_guideline_count INTEGER NOT NULL,
                updated_review_guideline_count INTEGER NOT NULL,
                imported_at TEXT NOT NULL
            );
            """);

        migration.Execute("""
            CREATE INDEX IF NOT EXISTS idx_domain_preset_imports_imported
            ON domain_preset_imports(imported_at DESC);
            """);
    }

    private static void ApplyDomainPresetDeactivationSchema(DatabaseMigrationContext migration)
    {
        migration.AddColumnIfTableExistsAndMissing("domain_preset_imports", "deactivated_at", "TEXT");
    }

    private static void ApplyDomainPresetSpeakerAliasTrackingSchema(DatabaseMigrationContext migration)
    {
        migration.Execute("""
            CREATE TABLE IF NOT EXISTS domain_preset_speaker_alias_imports (
                import_id TEXT NOT NULL,
                job_id TEXT NOT NULL,
                speaker_id TEXT NOT NULL,
                previous_display_name TEXT,
                applied_display_name TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (import_id, job_id, speaker_id)
            );
            """);

        migration.Execute("""
            CREATE INDEX IF NOT EXISTS idx_domain_preset_speaker_alias_imports_import
            ON domain_preset_speaker_alias_imports(import_id);
            """);
    }

    private static void ApplyDomainPresetSpeakerAliasRestoreSchema(DatabaseMigrationContext migration)
    {
        migration.AddColumnIfTableExistsAndMissing("domain_preset_speaker_alias_imports", "previous_display_name", "TEXT");
        migration.AddColumnIfTableExistsAndMissing("domain_preset_speaker_alias_imports", "applied_display_name", "TEXT NOT NULL DEFAULT ''");
    }

    private static void ApplyTranscriptDerivativesSchema(DatabaseMigrationContext migration)
    {
        migration.Execute("""
            CREATE TABLE IF NOT EXISTS transcript_derivatives (
                derivative_id TEXT NOT NULL PRIMARY KEY,
                job_id TEXT NOT NULL,
                kind TEXT NOT NULL,
                content_format TEXT NOT NULL,
                content TEXT NOT NULL,
                source_kind TEXT NOT NULL,
                source_transcript_hash TEXT NOT NULL,
                source_segment_range TEXT,
                source_chunk_ids TEXT,
                model_id TEXT,
                prompt_version TEXT NOT NULL,
                generation_profile TEXT NOT NULL,
                status TEXT NOT NULL,
                error_message TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);

        migration.Execute("""
            CREATE INDEX IF NOT EXISTS idx_transcript_derivatives_job_kind_updated
            ON transcript_derivatives(job_id, kind, updated_at DESC);
            """);

        migration.Execute("""
            CREATE INDEX IF NOT EXISTS idx_transcript_derivatives_job_status
            ON transcript_derivatives(job_id, status);
            """);

        migration.Execute("""
            CREATE TABLE IF NOT EXISTS transcript_derivative_chunks (
                chunk_id TEXT NOT NULL PRIMARY KEY,
                derivative_id TEXT NOT NULL,
                job_id TEXT NOT NULL,
                chunk_index INTEGER NOT NULL,
                source_kind TEXT NOT NULL,
                source_segment_ids TEXT NOT NULL,
                source_start_seconds REAL,
                source_end_seconds REAL,
                source_transcript_hash TEXT NOT NULL,
                content_format TEXT NOT NULL,
                content TEXT NOT NULL,
                model_id TEXT,
                prompt_version TEXT NOT NULL,
                generation_profile TEXT NOT NULL,
                status TEXT NOT NULL,
                error_message TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);

        migration.Execute("""
            CREATE INDEX IF NOT EXISTS idx_transcript_derivative_chunks_derivative_index
            ON transcript_derivative_chunks(derivative_id, chunk_index);
            """);

        migration.Execute("""
            CREATE INDEX IF NOT EXISTS idx_transcript_derivative_chunks_job_status
            ON transcript_derivative_chunks(job_id, status);
            """);
    }

}
