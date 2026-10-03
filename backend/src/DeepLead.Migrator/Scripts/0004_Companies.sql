-- Global company cache (public data, shared across tenants). Aspects link to companies via AspectLeads.

CREATE TABLE dbo.Companies
(
    Id                 BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Companies PRIMARY KEY,
    Name               NVARCHAR(300)        NOT NULL,
    NormalizedName     NVARCHAR(300)        NOT NULL,     -- lower-case, punctuation/legal suffix stripped; used for matching
    CountryIso2        CHAR(2)              NOT NULL CONSTRAINT FK_Companies_Countries REFERENCES dbo.Countries(Iso2),
    CityId             INT                  NULL CONSTRAINT FK_Companies_Cities REFERENCES dbo.Cities(Id),
    Address            NVARCHAR(500)        NULL,
    Latitude           DECIMAL(9,6)         NULL,
    Longitude          DECIMAL(9,6)         NULL,
    Website            NVARCHAR(500)        NULL,
    WebsiteDomain      NVARCHAR(255)        NULL,
    Category           NVARCHAR(200)        NULL,
    -- Google Maps
    MapsPlaceId        NVARCHAR(200)        NULL,
    MapsUrl            NVARCHAR(1000)       NULL,
    Rating             DECIMAL(2,1)         NULL,
    ReviewCount        INT                  NULL,
    BusinessStatus     VARCHAR(30)          NOT NULL CONSTRAINT DF_Companies_BusinessStatus DEFAULT ('Operational'),
    -- Company intelligence (Stage 2) – best confirmed value; evidence lives in CompanyFieldValues
    TeamSize           NVARCHAR(100)        NULL,         -- usually a range, e.g. '11-50'
    Turnover           NVARCHAR(100)        NULL,         -- usually a range, e.g. '5-25 Cr'
    OwnerName          NVARCHAR(200)        NULL,
    -- India identifiers
    Gstin              CHAR(15)             NULL,
    Cin                CHAR(21)             NULL,
    UdyamNumber        VARCHAR(25)          NULL,
    -- Signals & score
    SignalsJson        NVARCHAR(MAX)        NULL CONSTRAINT CK_Companies_SignalsJson CHECK (SignalsJson IS NULL OR ISJSON(SignalsJson) = 1),
    LeadScore          TINYINT              NULL,
    CreatedAt          DATETIME2(0)         NOT NULL CONSTRAINT DF_Companies_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt          DATETIME2(0)         NOT NULL CONSTRAINT DF_Companies_UpdatedAt DEFAULT (SYSUTCDATETIME()),
    LastEnrichedAt     DATETIME2(0)         NULL,
    CONSTRAINT CK_Companies_BusinessStatus CHECK (BusinessStatus IN ('Operational', 'TemporarilyClosed'))  -- permanently closed are never stored
);

CREATE UNIQUE INDEX UX_Companies_MapsPlaceId ON dbo.Companies(MapsPlaceId) WHERE MapsPlaceId IS NOT NULL;
CREATE INDEX IX_Companies_Match ON dbo.Companies(CountryIso2, CityId, NormalizedName);
CREATE INDEX IX_Companies_WebsiteDomain ON dbo.Companies(WebsiteDomain) WHERE WebsiteDomain IS NOT NULL;
CREATE INDEX IX_Companies_Gstin ON dbo.Companies(Gstin) WHERE Gstin IS NOT NULL;
CREATE INDEX IX_Companies_Cin ON dbo.Companies(Cin) WHERE Cin IS NOT NULL;

-- Which companies each aspect found (per-aspect separation)
CREATE TABLE dbo.AspectLeads
(
    AspectId      BIGINT       NOT NULL CONSTRAINT FK_AspectLeads_SearchAspects REFERENCES dbo.SearchAspects(Id),
    CompanyId     BIGINT       NOT NULL CONSTRAINT FK_AspectLeads_Companies REFERENCES dbo.Companies(Id),
    MapsRank      INT          NULL,
    FoundBy       VARCHAR(20)  NOT NULL,                  -- 'Maps', 'Search'
    FoundAt       DATETIME2(0) NOT NULL CONSTRAINT DF_AspectLeads_FoundAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_AspectLeads PRIMARY KEY (AspectId, CompanyId)
);

CREATE INDEX IX_AspectLeads_CompanyId ON dbo.AspectLeads(CompanyId);

-- ICP score per session: the same company can be relevant for one session's ICP and not another's
CREATE TABLE dbo.SearchLeadScores
(
    SearchId      BIGINT        NOT NULL CONSTRAINT FK_SearchLeadScores_Searches REFERENCES dbo.Searches(Id),
    CompanyId     BIGINT        NOT NULL CONSTRAINT FK_SearchLeadScores_Companies REFERENCES dbo.Companies(Id),
    IcpScore      TINYINT       NOT NULL CONSTRAINT CK_SearchLeadScores_IcpScore CHECK (IcpScore BETWEEN 0 AND 100),
    IsRelevant    BIT           NOT NULL,
    Reason        NVARCHAR(1000) NULL,
    ScoredAt      DATETIME2(0)  NOT NULL CONSTRAINT DF_SearchLeadScores_ScoredAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_SearchLeadScores PRIMARY KEY (SearchId, CompanyId)
);

-- Phones, emails: one row per value, with validation result
CREATE TABLE dbo.CompanyChannels
(
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CompanyChannels PRIMARY KEY,
    CompanyId        BIGINT               NOT NULL CONSTRAINT FK_CompanyChannels_Companies REFERENCES dbo.Companies(Id),
    ChannelType      VARCHAR(10)          NOT NULL CONSTRAINT CK_CompanyChannels_Type CHECK (ChannelType IN ('Phone', 'Email')),
    Value            NVARCHAR(320)        NOT NULL,       -- as found
    NormalizedValue  NVARCHAR(320)        NOT NULL,       -- E.164 for phones, lower-case for emails
    PhoneKind        VARCHAR(20)          NULL,           -- 'Mobile', 'Landline', 'TollFree', ...
    IsValid          BIT                  NULL,           -- NULL = not yet validated
    ValidationNote   NVARCHAR(200)        NULL,
    SourceUrl        NVARCHAR(1000)       NULL,
    FoundAt          DATETIME2(0)         NOT NULL CONSTRAINT DF_CompanyChannels_FoundAt DEFAULT (SYSUTCDATETIME())
);

CREATE UNIQUE INDEX UX_CompanyChannels_Value ON dbo.CompanyChannels(CompanyId, ChannelType, NormalizedValue);

-- Owner, directors, core team, decision makers
CREATE TABLE dbo.CompanyPeople
(
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CompanyPeople PRIMARY KEY,
    CompanyId        BIGINT               NOT NULL CONSTRAINT FK_CompanyPeople_Companies REFERENCES dbo.Companies(Id),
    FullName         NVARCHAR(200)        NOT NULL,
    Designation      NVARCHAR(200)        NULL,
    Din              CHAR(8)              NULL,           -- India director identification number
    IsOwner          BIT                  NOT NULL CONSTRAINT DF_CompanyPeople_IsOwner DEFAULT (0),
    IsDecisionMaker  BIT                  NOT NULL CONSTRAINT DF_CompanyPeople_IsDecisionMaker DEFAULT (0),
    Phone            NVARCHAR(50)         NULL,
    Email            NVARCHAR(320)        NULL,
    LinkedInUrl      NVARCHAR(500)        NULL,
    SourceUrl        NVARCHAR(1000)       NOT NULL,
    FoundAt          DATETIME2(0)         NOT NULL CONSTRAINT DF_CompanyPeople_FoundAt DEFAULT (SYSUTCDATETIME())
);

CREATE INDEX IX_CompanyPeople_CompanyId ON dbo.CompanyPeople(CompanyId);

CREATE TABLE dbo.CompanySocials
(
    Id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CompanySocials PRIMARY KEY,
    CompanyId      BIGINT               NOT NULL CONSTRAINT FK_CompanySocials_Companies REFERENCES dbo.Companies(Id),
    Platform       VARCHAR(20)          NOT NULL CONSTRAINT CK_CompanySocials_Platform CHECK (Platform IN ('LinkedIn', 'Facebook', 'Instagram', 'YouTube', 'X')),
    Url            NVARCHAR(500)        NOT NULL,
    Followers      INT                  NULL,
    LastPostAt     DATE                 NULL,
    FoundAt        DATETIME2(0)         NOT NULL CONSTRAINT DF_CompanySocials_FoundAt DEFAULT (SYSUTCDATETIME())
);

CREATE UNIQUE INDEX UX_CompanySocials ON dbo.CompanySocials(CompanyId, Platform, Url);

CREATE TABLE dbo.CompanyProducts
(
    Id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CompanyProducts PRIMARY KEY,
    CompanyId      BIGINT               NOT NULL CONSTRAINT FK_CompanyProducts_Companies REFERENCES dbo.Companies(Id),
    Name           NVARCHAR(300)        NOT NULL,
    SourceUrl      NVARCHAR(1000)       NULL
);

CREATE INDEX IX_CompanyProducts_CompanyId ON dbo.CompanyProducts(CompanyId);
