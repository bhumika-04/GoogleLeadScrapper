-- Stage 2 (people discovery) and connected social/directory accounts.

-- People: personal social handles + matching key, so the same person found on several pages is stored once.
ALTER TABLE dbo.CompanyPeople ADD
    NormalizedName  NVARCHAR(200) NULL,
    FacebookUrl     NVARCHAR(500) NULL,
    InstagramUrl    NVARCHAR(500) NULL,
    XUrl            NVARCHAR(500) NULL,
    Source          VARCHAR(30)   NULL;     -- 'LinkedInSearch', 'Website', 'IndiaMart', 'Mca', ...
GO

UPDATE dbo.CompanyPeople SET NormalizedName = LOWER(FullName) WHERE NormalizedName IS NULL;
ALTER TABLE dbo.CompanyPeople ALTER COLUMN NormalizedName NVARCHAR(200) NOT NULL;
CREATE UNIQUE INDEX UX_CompanyPeople_Name ON dbo.CompanyPeople(CompanyId, NormalizedName);
GO

ALTER TABLE dbo.Companies ADD PeopleEnrichedAt DATETIME2(0) NULL;   -- company cache: skip re-research for a while
GO

-- One login session per tenant + platform. The browser session (cookies) is stored encrypted, never passwords.
CREATE TABLE dbo.ConnectedAccounts
(
    Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ConnectedAccounts PRIMARY KEY,
    TenantId         INT               NOT NULL CONSTRAINT FK_ConnectedAccounts_Tenants REFERENCES dbo.Tenants(Id),
    Platform         VARCHAR(20)       NOT NULL CONSTRAINT CK_ConnectedAccounts_Platform
                                       CHECK (Platform IN ('LinkedIn', 'Facebook', 'Instagram', 'IndiaMart', 'Justdial')),
    Status           VARCHAR(20)       NOT NULL CONSTRAINT CK_ConnectedAccounts_Status
                                       CHECK (Status IN ('ConnectRequested', 'WaitingForLogin', 'SaveRequested', 'Connected', 'Expired', 'Failed', 'Disconnected')),
    SessionState     VARBINARY(MAX)    NULL,     -- AES-GCM encrypted Playwright storage state (cookies + local storage)
    AccountLabel     NVARCHAR(200)     NULL,     -- optional note the user types, e.g. 'leadbot.linkedin@company.com'
    RequestedByUserId INT              NULL CONSTRAINT FK_ConnectedAccounts_Users REFERENCES dbo.Users(Id),
    RequestedAt      DATETIME2(0)      NULL,
    ConnectedAt      DATETIME2(0)      NULL,
    LastUsedAt       DATETIME2(0)      NULL,
    LastError        NVARCHAR(1000)    NULL,
    UpdatedAt        DATETIME2(0)      NOT NULL CONSTRAINT DF_ConnectedAccounts_UpdatedAt DEFAULT (SYSUTCDATETIME())
);

CREATE UNIQUE INDEX UX_ConnectedAccounts_TenantPlatform ON dbo.ConnectedAccounts(TenantId, Platform);
CREATE INDEX IX_ConnectedAccounts_Status ON dbo.ConnectedAccounts(Status) WHERE Status = 'ConnectRequested';
