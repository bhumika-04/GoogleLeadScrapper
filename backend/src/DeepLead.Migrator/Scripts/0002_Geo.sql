-- Countries, languages, cities (cities are bulk-loaded from GeoNames later)

CREATE TABLE dbo.Languages
(
    Code          VARCHAR(10)   NOT NULL CONSTRAINT PK_Languages PRIMARY KEY,   -- ISO 639-1, e.g. 'en', 'hi', 'ar'
    Name          NVARCHAR(100) NOT NULL,
    NativeName    NVARCHAR(100) NOT NULL,
    IsRtl         BIT           NOT NULL CONSTRAINT DF_Languages_IsRtl DEFAULT (0)
);

CREATE TABLE dbo.Countries
(
    Iso2          CHAR(2)       NOT NULL CONSTRAINT PK_Countries PRIMARY KEY,
    Iso3          CHAR(3)       NOT NULL,
    Name          NVARCHAR(100) NOT NULL,
    PhoneCode     VARCHAR(10)   NOT NULL,
    GoogleDomain  VARCHAR(50)   NOT NULL CONSTRAINT DF_Countries_GoogleDomain DEFAULT ('google.com'),
    IsEnabled     BIT           NOT NULL CONSTRAINT DF_Countries_IsEnabled DEFAULT (1)
);

-- Native languages per country; English is always searched and is not stored here
CREATE TABLE dbo.CountryLanguages
(
    CountryIso2   CHAR(2)     NOT NULL CONSTRAINT FK_CountryLanguages_Countries REFERENCES dbo.Countries(Iso2),
    LanguageCode  VARCHAR(10) NOT NULL CONSTRAINT FK_CountryLanguages_Languages REFERENCES dbo.Languages(Code),
    Priority      TINYINT     NOT NULL CONSTRAINT DF_CountryLanguages_Priority DEFAULT (1),
    CONSTRAINT PK_CountryLanguages PRIMARY KEY (CountryIso2, LanguageCode)
);

CREATE TABLE dbo.Cities
(
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Cities PRIMARY KEY,
    GeoNameId     INT               NULL,
    CountryIso2   CHAR(2)           NOT NULL CONSTRAINT FK_Cities_Countries REFERENCES dbo.Countries(Iso2),
    Name          NVARCHAR(200)     NOT NULL,
    AsciiName     NVARCHAR(200)     NOT NULL,
    Region        NVARCHAR(200)     NULL,                -- state / emirate / province
    Latitude      DECIMAL(9,6)      NULL,
    Longitude     DECIMAL(9,6)      NULL,
    Population    INT               NULL,
    IsUserAdded   BIT               NOT NULL CONSTRAINT DF_Cities_IsUserAdded DEFAULT (0)   -- typed in the City box, not in GeoNames
);

CREATE UNIQUE INDEX UX_Cities_GeoNameId ON dbo.Cities(GeoNameId) WHERE GeoNameId IS NOT NULL;
CREATE INDEX IX_Cities_Country_Name ON dbo.Cities(CountryIso2, AsciiName) INCLUDE (Name, Region, Population);
CREATE INDEX IX_Cities_Country_Population ON dbo.Cities(CountryIso2, Population DESC) INCLUDE (Name, Region);   -- "Top 50/100/200/500" quick-add
