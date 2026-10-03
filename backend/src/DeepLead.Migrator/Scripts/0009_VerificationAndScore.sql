-- Stage 5: count how many independent sites confirm a phone/email/person ("Verified" = 2+ sites), plus lead score inputs.

ALTER TABLE dbo.CompanyChannels ADD
    SourceDomains  NVARCHAR(400) NULL,   -- comma-separated distinct domains that showed this value, e.g. 'google.com,jaimagraphics.com'
    SourceCount    TINYINT       NOT NULL CONSTRAINT DF_CompanyChannels_SourceCount DEFAULT (1);

ALTER TABLE dbo.CompanyPeople ADD
    SourceDomains  NVARCHAR(400) NULL,
    SourceCount    TINYINT       NOT NULL CONSTRAINT DF_CompanyPeople_SourceCount DEFAULT (1);

ALTER TABLE dbo.Companies ADD
    FacebookFollowers INT NULL,
    LeadScoreAt       DATETIME2(0) NULL;
GO

-- Backfill: the one domain each existing row came from ("https://www.site.com/x" -> "site.com").
UPDATE dbo.CompanyChannels
SET SourceDomains = LOWER(REPLACE(LEFT(h.HostPath, CHARINDEX('/', h.HostPath + '/') - 1), 'www.', ''))
FROM dbo.CompanyChannels ch
CROSS APPLY (SELECT SUBSTRING(ch.SourceUrl, CHARINDEX('://', ch.SourceUrl) + 3, 400) AS HostPath) h
WHERE ch.SourceUrl IS NOT NULL AND CHARINDEX('://', ch.SourceUrl) > 0;

UPDATE dbo.CompanyPeople
SET SourceDomains = LOWER(REPLACE(LEFT(h.HostPath, CHARINDEX('/', h.HostPath + '/') - 1), 'www.', ''))
FROM dbo.CompanyPeople p
CROSS APPLY (SELECT SUBSTRING(p.SourceUrl, CHARINDEX('://', p.SourceUrl) + 3, 400) AS HostPath) h
WHERE CHARINDEX('://', p.SourceUrl) > 0;
