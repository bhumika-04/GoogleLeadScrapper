-- Re-runs and repeating sessions. A re-run is a new session cloned from a previous one (ParentSearchId);
-- leads not present in the parent run are flagged "New" in the UI.

ALTER TABLE dbo.Searches ADD
    ParentSearchId   BIGINT       NULL CONSTRAINT FK_Searches_Parent REFERENCES dbo.Searches(Id),
    RunNumber        INT          NOT NULL CONSTRAINT DF_Searches_RunNumber DEFAULT (1),
    RepeatFrequency  VARCHAR(10)  NULL CONSTRAINT CK_Searches_Repeat CHECK (RepeatFrequency IN ('Weekly', 'Monthly')),
    NextRunAt        DATETIME2(0) NULL;   -- set on the latest run of a repeating series only
GO

CREATE INDEX IX_Searches_NextRunAt ON dbo.Searches(NextRunAt) WHERE NextRunAt IS NOT NULL;
CREATE INDEX IX_Searches_Parent ON dbo.Searches(ParentSearchId) WHERE ParentSearchId IS NOT NULL;
