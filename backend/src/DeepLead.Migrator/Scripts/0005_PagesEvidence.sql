-- Visited pages (hybrid storage: JSON in DB, cleaned text as gzip on disk for 30 days) and per-field evidence.

CREATE TABLE dbo.Pages
(
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Pages PRIMARY KEY,
    Url              NVARCHAR(2000)       NOT NULL,
    UrlHash          BINARY(32)           NOT NULL,       -- SHA-256 of normalized URL
    Domain           NVARCHAR(255)        NOT NULL,
    CompanyId        BIGINT               NULL CONSTRAINT FK_Pages_Companies REFERENCES dbo.Companies(Id),
    HttpStatus       SMALLINT             NULL,
    ContentHash      BINARY(32)           NULL,
    TextPath         NVARCHAR(400)        NULL,           -- relative path under data/pages; NULL once purged
    ExtractedJson    NVARCHAR(MAX)        NULL CONSTRAINT CK_Pages_ExtractedJson CHECK (ExtractedJson IS NULL OR ISJSON(ExtractedJson) = 1),
    ExtractedBy      VARCHAR(10)          NULL CONSTRAINT CK_Pages_ExtractedBy CHECK (ExtractedBy IN ('Rules', 'OpenAI', 'Both')),
    FetchedAt        DATETIME2(0)         NOT NULL CONSTRAINT DF_Pages_FetchedAt DEFAULT (SYSUTCDATETIME()),
    TextPurgedAt     DATETIME2(0)         NULL
);

CREATE UNIQUE INDEX UX_Pages_UrlHash ON dbo.Pages(UrlHash);
CREATE INDEX IX_Pages_CompanyId ON dbo.Pages(CompanyId);
CREATE INDEX IX_Pages_Purge ON dbo.Pages(FetchedAt) WHERE TextPath IS NOT NULL;

-- Every candidate value with its proof. "Real figures only": no row without SourceUrl.
CREATE TABLE dbo.CompanyFieldValues
(
    Id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CompanyFieldValues PRIMARY KEY,
    CompanyId      BIGINT               NOT NULL CONSTRAINT FK_CompanyFieldValues_Companies REFERENCES dbo.Companies(Id),
    FieldName      VARCHAR(50)          NOT NULL,         -- 'TeamSize', 'Turnover', 'OwnerName', 'CoreMember', ...
    Value          NVARCHAR(500)        NOT NULL,
    SourceUrl      NVARCHAR(2000)       NOT NULL,
    PageId         BIGINT               NULL CONSTRAINT FK_CompanyFieldValues_Pages REFERENCES dbo.Pages(Id),
    Quote          NVARCHAR(1000)       NULL,             -- exact sentence from the page
    ExtractedBy    VARCHAR(10)          NOT NULL CONSTRAINT CK_CompanyFieldValues_ExtractedBy CHECK (ExtractedBy IN ('Maps', 'Rules', 'OpenAI')),
    QuoteVerified  BIT                  NOT NULL CONSTRAINT DF_CompanyFieldValues_QuoteVerified DEFAULT (0),
    FoundAt        DATETIME2(0)         NOT NULL CONSTRAINT DF_CompanyFieldValues_FoundAt DEFAULT (SYSUTCDATETIME())
);

CREATE INDEX IX_CompanyFieldValues_Company_Field ON dbo.CompanyFieldValues(CompanyId, FieldName);

-- Every search-engine request: drives the adaptive rate limiter and source-health view
CREATE TABLE dbo.SearchRequestLog
(
    Id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SearchRequestLog PRIMARY KEY,
    Engine         VARCHAR(20)          NOT NULL,         -- 'GoogleMaps', 'Google', 'Bing', 'DuckDuckGo'
    QueryText      NVARCHAR(500)        NOT NULL,
    PageNumber     SMALLINT             NOT NULL CONSTRAINT DF_SearchRequestLog_PageNumber DEFAULT (1),
    ResultCount    INT                  NULL,
    Outcome        VARCHAR(20)          NOT NULL CONSTRAINT CK_SearchRequestLog_Outcome CHECK (Outcome IN ('Ok', 'Empty', 'Captcha', 'Blocked', 'Error')),
    DurationMs     INT                  NULL,
    ExecutedAt     DATETIME2(0)         NOT NULL CONSTRAINT DF_SearchRequestLog_ExecutedAt DEFAULT (SYSUTCDATETIME())
);

CREATE INDEX IX_SearchRequestLog_Engine_Time ON dbo.SearchRequestLog(Engine, ExecutedAt DESC) INCLUDE (Outcome);

CREATE TABLE dbo.OpenAiUsage
(
    Id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OpenAiUsage PRIMARY KEY,
    TenantId       INT                  NULL CONSTRAINT FK_OpenAiUsage_Tenants REFERENCES dbo.Tenants(Id),
    AspectId       BIGINT               NULL,
    CompanyId      BIGINT               NULL,
    Purpose        VARCHAR(30)          NOT NULL,         -- 'Translate', 'Stage2Extract', 'PageExtract', 'IcpScore', 'SuggestKeywords'
    Model          VARCHAR(50)          NOT NULL,
    InputTokens    INT                  NOT NULL,
    OutputTokens   INT                  NOT NULL,
    CreatedAt      DATETIME2(0)         NOT NULL CONSTRAINT DF_OpenAiUsage_CreatedAt DEFAULT (SYSUTCDATETIME())
);

CREATE INDEX IX_OpenAiUsage_Tenant_Time ON dbo.OpenAiUsage(TenantId, CreatedAt);

CREATE TABLE dbo.ExportHistory
(
    Id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExportHistory PRIMARY KEY,
    TenantId       INT                  NOT NULL CONSTRAINT FK_ExportHistory_Tenants REFERENCES dbo.Tenants(Id),
    UserId         INT                  NOT NULL CONSTRAINT FK_ExportHistory_Users REFERENCES dbo.Users(Id),
    SearchId       BIGINT               NOT NULL CONSTRAINT FK_ExportHistory_Searches REFERENCES dbo.Searches(Id),
    Format         VARCHAR(10)          NOT NULL CONSTRAINT CK_ExportHistory_Format CHECK (Format IN ('Excel', 'Csv')),
    RowsExported   INT                  NOT NULL,
    CreatedAt      DATETIME2(0)         NOT NULL CONSTRAINT DF_ExportHistory_CreatedAt DEFAULT (SYSUTCDATETIME())
);

CREATE TABLE dbo.AuditLog
(
    Id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLog PRIMARY KEY,
    TenantId       INT                  NULL,
    UserId         INT                  NULL,
    Action         VARCHAR(50)          NOT NULL,
    Details        NVARCHAR(MAX)        NULL,
    CreatedAt      DATETIME2(0)         NOT NULL CONSTRAINT DF_AuditLog_CreatedAt DEFAULT (SYSUTCDATETIME())
);
