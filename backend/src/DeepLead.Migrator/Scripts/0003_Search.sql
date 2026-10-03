-- A Search is what the user submits; each (city x keyword) becomes one SearchAspect with its own results.

CREATE TABLE dbo.Searches
(
    Id                     BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Searches PRIMARY KEY,
    TenantId               INT                  NOT NULL CONSTRAINT FK_Searches_Tenants REFERENCES dbo.Tenants(Id),
    CreatedByUserId        INT                  NOT NULL CONSTRAINT FK_Searches_Users REFERENCES dbo.Users(Id),
    Name                   NVARCHAR(200)        NOT NULL,
    CountryIso2            CHAR(2)              NOT NULL CONSTRAINT FK_Searches_Countries REFERENCES dbo.Countries(Iso2),
    IncludeNativeLanguage  BIT                  NOT NULL CONSTRAINT DF_Searches_IncludeNative DEFAULT (0),
    Status                 VARCHAR(20)          NOT NULL CONSTRAINT DF_Searches_Status DEFAULT ('Pending'),
    CreatedAt              DATETIME2(0)         NOT NULL CONSTRAINT DF_Searches_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_Searches_Status CHECK (Status IN ('Pending', 'Running', 'Paused', 'Completed', 'Failed', 'Cancelled'))
);

CREATE INDEX IX_Searches_TenantId ON dbo.Searches(TenantId, CreatedAt DESC);

CREATE TABLE dbo.SearchAspects
(
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SearchAspects PRIMARY KEY,
    SearchId      BIGINT               NOT NULL CONSTRAINT FK_SearchAspects_Searches REFERENCES dbo.Searches(Id),
    CityId        INT                  NOT NULL CONSTRAINT FK_SearchAspects_Cities REFERENCES dbo.Cities(Id),
    Keyword       NVARCHAR(200)        NOT NULL,          -- as entered (English)
    Status        VARCHAR(20)          NOT NULL CONSTRAINT DF_SearchAspects_Status DEFAULT ('Pending'),
    LeadCount     INT                  NOT NULL CONSTRAINT DF_SearchAspects_LeadCount DEFAULT (0),
    CreatedAt     DATETIME2(0)         NOT NULL CONSTRAINT DF_SearchAspects_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_SearchAspects_Status CHECK (Status IN ('Pending', 'Running', 'Paused', 'Completed', 'Failed', 'Cancelled'))
);

CREATE INDEX IX_SearchAspects_SearchId ON dbo.SearchAspects(SearchId);

-- Keyword variants actually searched: English always, plus native translations when enabled
CREATE TABLE dbo.AspectQueries
(
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AspectQueries PRIMARY KEY,
    AspectId      BIGINT               NOT NULL CONSTRAINT FK_AspectQueries_SearchAspects REFERENCES dbo.SearchAspects(Id),
    LanguageCode  VARCHAR(10)          NOT NULL CONSTRAINT FK_AspectQueries_Languages REFERENCES dbo.Languages(Code),
    QueryText     NVARCHAR(400)        NOT NULL
);

CREATE INDEX IX_AspectQueries_AspectId ON dbo.AspectQueries(AspectId);

-- Progress of each pipeline stage per aspect (1 Maps, 2 Company intelligence, 3 Google Search, 4 Visit URLs, 5 Merge & verify)
CREATE TABLE dbo.AspectStages
(
    AspectId      BIGINT        NOT NULL CONSTRAINT FK_AspectStages_SearchAspects REFERENCES dbo.SearchAspects(Id),
    Stage         TINYINT       NOT NULL CONSTRAINT CK_AspectStages_Stage CHECK (Stage BETWEEN 1 AND 5),
    Status        VARCHAR(20)   NOT NULL CONSTRAINT DF_AspectStages_Status DEFAULT ('Pending'),
    ItemsTotal    INT           NULL,
    ItemsDone     INT           NOT NULL CONSTRAINT DF_AspectStages_ItemsDone DEFAULT (0),
    StartedAt     DATETIME2(0)  NULL,
    FinishedAt    DATETIME2(0)  NULL,
    LastError     NVARCHAR(MAX) NULL,
    CONSTRAINT PK_AspectStages PRIMARY KEY (AspectId, Stage),
    CONSTRAINT CK_AspectStages_Status CHECK (Status IN ('Pending', 'Running', 'Paused', 'Blocked', 'Completed', 'Failed', 'Cancelled'))
);
